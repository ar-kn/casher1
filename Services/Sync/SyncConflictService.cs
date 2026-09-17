using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;

namespace PhoneAccounting.App.Services.Sync;

/// <summary>
/// الحسم اليدوي للصراعات (P3.1 §3) — حصراً على MAIN:
/// 1) يُبنى القرار (KeepLocal/KeepRemote/StaysDeleted حصراً — ثلاثية تلائم حذف/تعديل).
/// 2) يعمل كعملية موقّعة جديدة (OriginDevice+OriginSeq عبر OperationWriter.Register) تنتقل في الدلتا،
///    إذ يبني DeltaBuilder صفوفها من حالة الكيان الحالية (التي صارت الفائز).
/// 3) القيمة «المتروكة» تُحفظ دائماً في ResolvedValueJson.
/// لا إصلاح آلي أبداً — الرفض من غير MAIN محسوم هنا.
/// </summary>
public static class SyncConflictService
{
    /// <summary>حسم صراع لكيان — MAIN وبمستخدم مدير (الفاحص لحظة الضغط، لا عند فتح الشاشة). يطبّق الفائز محلياً،
    /// يغلق الصف المحلي المفتوح، ويسجّل القرار كعملية موقّعة؛ كل محاولة محسومة/مرفوضة تُدوَّن في سجل تدقيق محلي
    /// غير مزامَن (ConflictAuditLog — P3.2C). الموارد: حسمٌ بلا صلاحية مدير = رفضٌ بلا أثر في السجل، مع
    /// تسجيل (من حاول، متى، أي صراع، السبب RoleMismatch).
    /// `remoteJsonOverride`: القيمة البعيدة من استطلاع القريب (P3.2B) — يجعل KeepRemote ممكناً على MAIN حتى بلا صف صراع محلي.</summary>
    public static bool Resolve(AppDbContext db, string table, string entitySyncId,
        ConflictResolutionType type, string? reason, out string error,
        string? remoteJsonOverride = null)
    {
        error = "";
        if (Session.Device != DeviceRole.MainServer)
        {
            error = "الحسم حصراً على MAIN فقط";
            return false;
        }

        // ===== بوابة الصلاحية (P3.2C): مدير حصرياً، تُفحص لحظة الضغط لا عند عرض الشاشة =====
        var user = Session.CurrentUser;
        if (user?.Role != UserRole.Admin)
        {
            error = user is null
                ? "انتهت الجلسة أو لا يوجد مستخدم — سجّل الدخول كمدير أولاً"
                : "الحسم حصراً من المدير فقط — صَلاحيتك الحالية لا تسمح";
            RecordAudit(db, user, "Rejected", "RoleMismatch", entitySyncId, table);
            db.SaveChanges();
            return false;
        }

        var open = db.SyncConflicts.FirstOrDefault(c => c.ConflictSyncId == entitySyncId && !c.IsResolved);

        int localId = DeltaApplier.LocalIdBySyncId(db, table, entitySyncId);
        List<DeltaRow> winningRows;

        switch (type)
        {
            case ConflictResolutionType.KeepRemote:
                var remoteJson = open?.RemoteJson ?? remoteJsonOverride;
                if (string.IsNullOrWhiteSpace(remoteJson))
                {
                    error = "لا قيمة بَعيدية للحسم — صراع محلي بلا RemoteJson ولا قيمة من الاستطلاع";
                    return false;
                }
                winningRows = DeltaApplier.RowsFromJson(remoteJson);
                break;
            case ConflictResolutionType.KeepLocal:
            case ConflictResolutionType.StaysDeleted:
                if (localId <= 0)
                {
                    error = "الكيان غير موجود محلياً على MAIN — لا قيمة محلية للحسم";
                    return false;
                }
                winningRows = DeltaBuilder.SnapshotRows(db, table, localId);
                if (winningRows.Count == 0)
                {
                    error = "لا صفوف محلية للحسم";
                    return false;
                }
                if (type == ConflictResolutionType.StaysDeleted)
                {
                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    foreach (var row in winningRows)
                        row.F["DeletedAt"] = JsonScalar.Of((long)now); // شاهد القبر = القيمة المعتمدة «يبقى محذوفاً»
                }
                break;
            default:
                error = "نوع حسم غير معروف";
                return false;
        }

        string by = user.DisplayName;
        int resolvedUserId = user.Id;

        var payload = JsonSerializer.Serialize(new
        {
            conflictSyncId = entitySyncId,
            table,
            type = type.ToString(),
            by,
            reason
        });

        // عملية موقّعة جديدة — سيُمنح OriginSeq عند Commit (الشرط المجمَّد 1) عبر AssignPendingOpSeqs.
        var op = OperationWriter.Register(db, OperationType.ConflictResolution, null, 0m, table, localId <= 0 ? null : localId, payload);

        // تطبيق الفائز محلياً على MAIN.
        DeltaApplier.ApplyResolutionRows(db, winningRows, resolvedUserId);

        // إغلاق صف MAIN المحلي + حفظ «القيمة المتروكة».
        if (open is not null)
        {
            open.IsResolved = true;
            open.ResolutionType = type.ToString();
            open.ResolvedBy = by;
            open.ResolvedReason = reason;
            open.ResolvedAt = DateTime.UtcNow;
            open.ResolvedValueJson = type == ConflictResolutionType.KeepRemote ? open.LocalJson : open.RemoteJson;
        }

        // تدقيق القبول (نفس معاملة الحسم — سجل محلي غير مزامَن).
        RecordAudit(db, user, "Accepted", type.ToString(), entitySyncId, table);

        db.SaveChanges();

        // بصمة القرار (P3.2C): بعد منح Seq عند Commit — يُربط الصف المحلي (إن وُجد) بعملية السجل.
        if (open is not null)
        {
            open.ResolvedAtSeq = op.OriginSeq;
            open.ResolutionOpOrigin = op.OriginDevice;
            db.SaveChanges();
        }
        return true;
    }

    /// <summary>تدوين محلي (غير مزامَن) لمحاولة حسم على MAIN — السبب القياسي للرفض: RoleMismatch.</summary>
    private static void RecordAudit(AppDbContext db, User? user, string outcome, string? reason, string entitySyncId, string table)
    {
        db.ConflictAuditLogs.Add(new ConflictAuditLog
        {
            UserName = user?.DisplayName ?? "(لا جلسة)",
            UserRole = user?.Role.ToString() ?? "",
            DeviceId = Session.DeviceId,
            ConflictSyncId = entitySyncId,
            EntityTable = table,
            Outcome = outcome,
            Reason = reason,
            AttemptedAt = DateTime.UtcNow
        });
    }

    /// <summary>اشتقاق «قيمته قيد المراجعة» (وسم P3.1): صراع مفتوح على الكيان = مشتقٌّ من حالة المحلي لا عمود مخزّن.</summary>
    public static bool IsConflicted(AppDbContext db, string table, string entitySyncId)
        => db.SyncConflicts.Any(c => c.EntityTable == table && c.ConflictSyncId == entitySyncId && !c.IsResolved);

    public static int OpenConflictCount(AppDbContext db)
        => db.SyncConflicts.Count(c => !c.IsResolved);

    public static List<SyncConflict> OpenConflicts(AppDbContext db)
        => db.SyncConflicts.AsNoTracking().Where(c => !c.IsResolved).OrderByDescending(c => c.LastUpdatedAt).ToList();

    /// <summary>موصّل قراءة-فقط «قيمته قيد المراجعة» (P3.2A): SyncId للكيانات ذات الخلاف المفتوح — استعلام واحد بلا Tracking.</summary>
    public static HashSet<string> UnderReviewSyncIds(AppDbContext db, string table)
        => db.SyncConflicts.AsNoTracking()
            .Where(c => c.EntityTable == table && !c.IsResolved)
            .Select(c => c.ConflictSyncId)
            .ToHashSet();
}