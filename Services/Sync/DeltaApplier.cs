using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;

namespace PhoneAccounting.App.Services.Sync;

/// <summary>
/// حصيلة تطبيق الدلتا: ما طُبّق فعلاً + أصنافٌ معروضةُ تطبيقٍ بصراعٍ ملتقَط (⊕ لا تُتذكَّر علامة ماء):
/// تحصين «الصدق» — لا يُقال عن رسالةٍ أن الطرف «تلقّى وطبّق» ما رفضه بصراع.
/// </summary>
public sealed record ApplyOutcome(int Applied, IReadOnlyCollection<string> ConflictedOrigins);

/// <summary>
/// تطبيق الدلتا عند الوجهة — قواعد ثابتة:
/// • داخل نفس الـ transaction (إيصال + صف العملية + صفوف الكيانات) — لا يقع نصف تطبيق.
/// • idempotent: إيصال (OriginDevice, OriginSeq) موجود ⇒ تخطٍّ فوري — حتى لو وصلت العملية نفسها من مُرسِلَين مختلفين (تمرير).
/// • وحدات الربط بالـ SyncId (وليس الـ Id المحلي)؛ إعادة ربط FKs عبر مفتاح SyncId ثم الخريطة.
/// • سطر العملية يُسجَّل محلياً بترقيم Seq محلي متزايد، وتُحفظ هوية العملية (الأصل + ترقيمه) لحظة التطبيق.
/// • الحذف شاهد قبر: الصف يصل وفيه DeletedAt — يُكتب كما هو (لا حذف فيزيائي).
/// • P3.1 — التقاط الصراع بلا منتصر: صف وارد يلمس كياناً غيّرناه محلياً بقيمةٍ لم يرَها المُرسِل بعد
///   (علامة ماء أصلِنا عند القريب أقدم من Seq تعديلنا) ⇒ لا تُطبَّق الحزمة، ويُحفظ الطرفان بهوية حتمية.
///   حارس الإنذار الكاذب: إن تساوت القيمتان (تطبيعياً) = لا صراع، تطبيق طبيعي.
/// • P3.1 — عملية الحسم (ConflictResolution): تُطبَّق صفوفها كقيمة الفائز وتُغلق الصراع المحلي ذا المعرّف الحتمي.
/// </summary>
public static class DeltaApplier
{
    /// <summary>
    /// خطاف فوضى خامل في الإنتاج — يسمح للهارنس بمحاكاة «انطفاءٍ في منتصف تطبيق دلتا» (§10.1–2):
    /// يُستدعى بعد SaveChanges وقبل Commit الحزمة الجارية؛ إعادةُ true ترمي فيُطالَ موسّعُ using
    /// المعاملة rollback شامل — فيثبت أن كتاباتٍ صارت على القرص داخل معاملةٍ غير مؤكَّدة لا تنجو.
    /// </summary>
    internal static Func<string?, int, bool>? ChaosInjectBeforeCommit { get; set; }

    /// <summary>
    /// senderWatermarks = خريطة علامات الماء للمُرسِل ({أصل: آخر ما تلقّاه}) — أساس قاعدة الكشف السطرية:
    /// «الكشف = صف وارد يلمس كياناً محلياً غيّرناه برقم يتجاوز ما أخبر المُرسِلُ أنه يعرفه عنا».
    /// غياب الخريطة (استدعاء مباشر M2.x) = بلا كشف.
    /// </summary>
    public static ApplyOutcome Apply(AppDbContext db, string remoteDeviceId, IEnumerable<DeltaPacket> packets,
        string? ownerDeviceId = null, IReadOnlyDictionary<string, long>? senderWatermarks = null)
    {
        // قفل كاتب واحد (M2.5): لا يتزامن تطبيق دلتا مع أي حفظ آخر في العملية — لا يقع تطبيقان متعاصران لجملة.
        try
        {
        using (SyncWriteLock.Acquire(TimeSpan.FromSeconds(SyncWriteLock.AcquireTimeoutSeconds), "تطبيق دلتا"))
        {
            var applied = 0;
            var conflicted = new HashSet<string>();
            foreach (var packet in packets)
            {
            var origin = packet.OriginDevice ?? remoteDeviceId;
            var originSeq = packet.OriginSeq > 0 ? packet.OriginSeq : packet.OpSeq;
            if (string.IsNullOrWhiteSpace(origin)) continue;

            if (db.SyncLogs.Any(s => s.OriginDevice == origin && s.OriginSeq == originSeq))
                continue; // إيصال موجود ⇒ تخطٍّ idempotent (مهما كان المُرسِل) — لا كشف ثانٍ ولا تكرار (قرار تثبيت #1)

            using var tx = db.Database.BeginTransaction();
            var resolvedUser = EnsureUserForOp(db, packet.UserName, packet.UserRole);

            if (packet.OpType == nameof(OperationType.ConflictResolution))
            {
                // عملية قرار MAIN: صفوفها = القيمة الفائزة — تُطبَّق بلا فحص صراع (هي السلطة النهائية).
                var parentKeys = new Dictionary<string, int>();
                foreach (var row in packet.Rows)
                {
                    var lid = UpsertRow(db, row, remoteDeviceId, resolvedUser, parentKeys);
                    if (lid > 0 && !parentKeys.ContainsKey(row.S))
                        parentKeys[row.S] = lid;
                }
                CloseLocalConflict(db, packet);
                InsertLocalOp(db, packet, resolvedUser, parentKeys, origin, originSeq);
                RecordReceipt(db, origin, originSeq, packet, ownerDeviceId);
                db.SaveChanges();
                if (ChaosInjectBeforeCommit?.Invoke(ownerDeviceId, applied) == true)
                    throw new InvalidOperationException("ChaosInjection: انطفاء مُحاكى في منتصف تطبيق دلتا (فوضى §10)");
                tx.Commit();
                applied++;
                continue;
            }

            if (ownerDeviceId is not null && senderWatermarks is not null
                && origin != ownerDeviceId && packet.Rows.Count > 0)
            {
                var primary = packet.Rows.FirstOrDefault(r => r.T == packet.EntityName) ?? packet.Rows[0];
                var localId = LocalIdBySyncId(db, primary.T, primary.S);
                if (localId > 0 && CouldConflict(db, ownerDeviceId, packet, primary, localId, senderWatermarks))
                {
                    var localRows = DeltaBuilder.SnapshotRows(db, packet.EntityName!, localId);
                    if (!RowSetsEqual(packet.Rows, localRows))
                    {
                        CaptureConflict(db, primary.T, primary.S, packet.Rows, localRows);
                        RecordReceipt(db, origin, originSeq, packet, ownerDeviceId);
                        db.SaveChanges();
                        tx.Commit();
                        conflicted.Add(origin); // لا تُتقدَّم علامة ماء هذا الأصل — فلا يكذب «أعرف ما طبّقته»
                        continue;               // لا منتصر: لا تُطبَّق الحزمة إطلاقاً
                    }
                }
            }

            var parentKeys2 = new Dictionary<string, int>(); // SyncId → الـ Id المحلي الناتج
            foreach (var row in packet.Rows)
            {
                var localId2 = UpsertRow(db, row, remoteDeviceId, resolvedUser, parentKeys2);
                if (localId2 > 0 && !parentKeys2.ContainsKey(row.S))
                    parentKeys2[row.S] = localId2;
            }

            InsertLocalOp(db, packet, resolvedUser, parentKeys2, origin, originSeq);
            RecordReceipt(db, origin, originSeq, packet, ownerDeviceId);

            db.SaveChanges();
            if (ChaosInjectBeforeCommit?.Invoke(ownerDeviceId, applied) == true)
                throw new InvalidOperationException("ChaosInjection: انطفاء مُحاكى في منتصف تطبيق دلتا (فوضى §10)");
            tx.Commit();
            applied++;
        }
            return new ApplyOutcome(applied, conflicted);
        }
        }
        catch (TimeoutException)
        {
            // قفل الكتابة مشغول بحفظ جارٍ (بيع/تطبيق آخر) — تُؤجَّل الحزمة لدورة المزامنة القادمة
            return new ApplyOutcome(0, Array.Empty<string>());
        }
    }

    /// <summary>إن لم يوجد المستخدم محلياً: منشأة وهمية غير قابلة للدخول (IsActive=false) لتحفظ اسم المنفّذ في التقارير ولتفادي كسر قيد الـ FK.</summary>
    private static int EnsureUserForOp(AppDbContext db, string? userName, int userRole)
    {
        if (string.IsNullOrWhiteSpace(userName)) return 0;
        var existing = db.Users.FirstOrDefault(u => u.DisplayName == userName);
        if (existing is not null) return existing.Id;

        var placeholder = new User
        {
            Username = $"sync_{Guid.NewGuid():N}"[..16],
            PasswordHash = "!disabled!",
            DisplayName = userName,
            Role = (UserRole)userRole,
            IsActive = false,
            MustChangePassword = false
        };
        db.Users.Add(placeholder);
        db.SaveChanges();
        return placeholder.Id;
    }

    private static int UpsertRow(
        AppDbContext db,
        DeltaRow row,
        string remoteDeviceId,
        int resolvedUser,
        Dictionary<string, int> parentKeys)
    {
        return row.T switch
        {
            "Categories" => UpsertCategory(db, row),
            "Products" => UpsertProduct(db, row, parentKeys),
            "Customers" => UpsertCustomer(db, row),
            "Suppliers" => UpsertSupplier(db, row),
            "Sales" => UpsertSale(db, row, remoteDeviceId, resolvedUser, parentKeys),
            "SaleItems" => UpsertSaleItem(db, row, parentKeys),
            "Purchases" => UpsertPurchase(db, row, remoteDeviceId, resolvedUser, parentKeys),
            "PurchaseItems" => UpsertPurchaseItem(db, row, parentKeys),
            "RepairJobs" => UpsertRepairJob(db, row, remoteDeviceId, resolvedUser, parentKeys),
            "RepairParts" => UpsertRepairPart(db, row, parentKeys),
            "Vouchers" => UpsertVoucher(db, row, remoteDeviceId, resolvedUser, parentKeys),
            _ => 0
        };
    }

    // ===== أدوات إعادة الربط =====

    private static int ResolveParent(Dictionary<string, int> parentKeys, string? syncId, int defaultId = 0)
        => syncId is not null && parentKeys.TryGetValue(syncId, out var id) ? id : defaultId;

    private static int ResolveRef(AppDbContext db, DeltaRow row, string refName, string table)
        => row.R.TryGetValue(refName, out var syncId) && !string.IsNullOrWhiteSpace(syncId)
            ? LocalIdBySyncId(db, table, syncId)
            : 0;

    internal static int LocalIdBySyncId(AppDbContext db, string table, string syncId)
    {
        return table switch
        {
            "Customers" => db.Customers.AsNoTracking().Where(x => x.SyncId == syncId).Select(x => x.Id).FirstOrDefault(),
            "Products" => db.Products.AsNoTracking().Where(x => x.SyncId == syncId).Select(x => x.Id).FirstOrDefault(),
            "Categories" => db.Categories.AsNoTracking().Where(x => x.SyncId == syncId).Select(x => x.Id).FirstOrDefault(),
            "Suppliers" => db.Suppliers.AsNoTracking().Where(x => x.SyncId == syncId).Select(x => x.Id).FirstOrDefault(),
            "Sales" => db.Sales.AsNoTracking().Where(x => x.SyncId == syncId).Select(x => x.Id).FirstOrDefault(),
            "Purchases" => db.Purchases.AsNoTracking().Where(x => x.SyncId == syncId).Select(x => x.Id).FirstOrDefault(),
            "RepairJobs" => db.RepairJobs.AsNoTracking().Where(x => x.SyncId == syncId).Select(x => x.Id).FirstOrDefault(),
            "Vouchers" => db.Vouchers.AsNoTracking().Where(x => x.SyncId == syncId).Select(x => x.Id).FirstOrDefault(),
            _ => 0
        };
    }

    // ===================== P3.1 — كشف الصراع بلا منتصر =====================

    /// <summary>قاعدة الكشف السطرية: هل غيرنا الكيان محلياً برقمٍ يتجاوز ما يُفصح المُرسِلُ أنه يعرفه عنا؟</summary>
    private static bool CouldConflict(AppDbContext db, string myDeviceId, DeltaPacket packet,
        DeltaRow primary, int localId, IReadOnlyDictionary<string, long> senderWatermarks)
    {
        var seen = senderWatermarks.TryGetValue(myDeviceId, out var w) ? w : 0;
        var localLastSeq = db.OperationLogs.AsNoTracking()
            .Where(o => o.OriginDevice == myDeviceId && o.EntityName == packet.EntityName && o.EntityId == localId)
            .Select(o => (long?)o.OriginSeq)
            .Max() ?? 0;
        return localLastSeq > seen;
    }

    /// <summary>حارس الإنذار الكاذب: مطابقة القيم (تطبيعية) — طرفان انتهيا لنفس النتيجة = لا صراع أبداً.</summary>
    private static bool RowSetsEqual(IReadOnlyList<DeltaRow> a, IReadOnlyList<DeltaRow> b)
    {
        if (a.Count != b.Count) return false;
        var keyed = b.ToDictionary(x => x.T + "#" + x.S);
        foreach (var row in a)
        {
            if (!keyed.TryGetValue(row.T + "#" + row.S, out var other)) return false;
            if (!FieldsEqual(row.F, other.F) || !RefsEqual(row.R, other.R)) return false;
        }
        return true;
    }

    private static bool FieldsEqual(Dictionary<string, JsonScalar> a, Dictionary<string, JsonScalar> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (k, v) in a)
        {
            if (!b.TryGetValue(k, out var w) || ScalarKey(v) != ScalarKey(w)) return false;
        }
        return true;
    }

    private static bool RefsEqual(Dictionary<string, string?> a, Dictionary<string, string?> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (k, v) in a)
        {
            if (!b.TryGetValue(k, out var w)) return false;
            if ((v ?? "") != (w ?? "")) return false;
        }
        return true;
    }

    private static string ScalarKey(JsonScalar v) => v.Kind switch
    {
        JsonKind.Null => "null",
        JsonKind.Number => "num:" + v.Num,
        JsonKind.Decimal => "dec:" + (v.Dec ?? 0m).ToString(CultureInfo.InvariantCulture),
        JsonKind.Bool => "bool:" + (v.Bool ?? false),
        JsonKind.Date => "date:" + (v.Date ?? default).Ticks,
        JsonKind.Text => "text:" + v.Str,
        _ => "?"
    };

    private static void CaptureConflict(AppDbContext db, string table, string syncId, List<DeltaRow> remote, List<DeltaRow> local)
    {
        // صراع-واحد-لكيان بهوية حتمية: صف واحد مفتوح، والتعديلات اللاحقة تحدّث RemoteJson في ذات السجل.
        var existing = db.SyncConflicts.FirstOrDefault(c => c.ConflictSyncId == syncId && !c.IsResolved);
        if (existing is null)
        {
            db.SyncConflicts.Add(new SyncConflict
            {
                ConflictSyncId = syncId,
                EntityTable = table,
                LocalJson = RowsToJson(local),
                RemoteJson = RowsToJson(remote),
                IsResolved = false,
                LastUpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.RemoteJson = RowsToJson(remote);
            existing.LastUpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>توجيهُ الحسم في SummaryJson؛ وصولها من MAIN يُغلق الصف المفتوح ذا المعرّف الحتمي ويُثبّت «القيمة المتروكة».
    /// حماية P3.2C: لا يُغلق صراعٌ إلا بعملية موقّعة بترقيم أصلٍ صحيح (OriginSeq>0) — إغلاقٌ بلا عملية مطابقة يُتجاهل.</summary>
    private static void CloseLocalConflict(AppDbContext db, DeltaPacket packet)
    {
        if (packet.OriginSeq <= 0) return;

        var meta = TryParseResolutionMeta(packet.SummaryJson);
        if (meta is null) return;

        var conflict = db.SyncConflicts.FirstOrDefault(c => c.ConflictSyncId == meta.ConflictSyncId && !c.IsResolved);
        if (conflict is null) return;

        conflict.IsResolved = true;
        conflict.ResolutionType = meta.Type;
        // بصمة P3.2C: الحاسم = مستخدم عملية السجل نفسها (fallback لوسم الحسم للتوافق الخلفي).
        conflict.ResolvedBy = string.IsNullOrWhiteSpace(packet.UserName) ? meta.By : packet.UserName!;
        conflict.ResolvedAtSeq = packet.OriginSeq;
        conflict.ResolutionOpOrigin = packet.OriginDevice;
        conflict.ResolvedReason = meta.Reason;
        conflict.ResolvedAt = DateTime.UtcNow;
        // «القيمة المتروكة تُحفظ دائماً» — عند المستقبِل = قيمته المحلية المهجورة (دلالة الجهة المتلقية للقرار)
        conflict.ResolvedValueJson = conflict.LocalJson;
    }

    // ===================== الأدوات المساعدة للحسم (P3.1) =====================

    private static readonly JsonSerializerOptions RowJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>وسمُ الحسم يُنتج بمفاتيح صغيرة من Resolve؛ قراءة غير حساسة للحالة (خطر انكسار ربط record).</summary>
    private static readonly JsonSerializerOptions MetaJson = new() { PropertyNameCaseInsensitive = true };

    internal static string RowsToJson(List<DeltaRow> rows) => JsonSerializer.Serialize(rows, RowJson);

    internal static List<DeltaRow> RowsFromJson(string? json)
        => string.IsNullOrWhiteSpace(json) ? new() : JsonSerializer.Deserialize<List<DeltaRow>>(json, RowJson) ?? new();

    /// <summary>سطور عرض لصفوفِ جدولٍ مُعيَّن داخل لقطةٍ مُسلسَلة (P3.2B شاشة الحسم) — للعرض البشري فقط،
    /// تُستبعد صفوفُ الأباء (الفئات وغيرها) فيُعرض اسمُ الكيان لا اسمُ الجدول الأب.</summary>
    public static List<string> DescribeRows(string? json, string table)
    {
        var lines = new List<string>();
        foreach (var row in RowsFromJson(json).Where(x => x.T == table))
        {
            var name = row.F.GetValueOrDefault("Name")?.Str;
            if (!string.IsNullOrWhiteSpace(name) && !lines.Contains(name)) lines.Add(name);
            if (row.F.TryGetValue("SellPrice", out var price) && price.Dec.HasValue) lines.Add("سعر " + price.Dec);
            else if (row.F.TryGetValue("BuyPrice", out var buy) && buy.Dec.HasValue) lines.Add("شراء " + buy.Dec);
            if (row.F.TryGetValue("DeletedAt", out var del) && del.Num is not null) lines.Add("محذوف");
        }
        return lines;
    }

    private sealed record ResolutionMeta(
        string? ConflictSyncId, string? Table, string? Type, string? By, string? Reason);

    private static ResolutionMeta? TryParseResolutionMeta(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ResolutionMeta>(json, MetaJson);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>تطبيق صفوف الحسم محلياً (على MAIN نفسه داخل خدمة الحسم) — بدون سطر عملية إضافي: العملية موقّعة من قبل Register.</summary>
    internal static void ApplyResolutionRows(AppDbContext db, List<DeltaRow> rows, int resolvedUserId)
    {
        var parentKeys = new Dictionary<string, int>();
        foreach (var row in rows)
        {
            var lid = UpsertRow(db, row, "", resolvedUserId, parentKeys);
            if (lid > 0 && !parentKeys.ContainsKey(row.S))
                parentKeys[row.S] = lid;
        }
    }

    private static void CopyScalar<T>(T target, DeltaRow row) where T : class
    {
        var type = target.GetType();
        foreach (var (name, value) in row.F)
        {
            var prop = type.GetProperty(name);
            if (prop is null || !prop.CanWrite) continue;

            var nullable = Nullable.GetUnderlyingType(prop.PropertyType);
            var t = nullable ?? prop.PropertyType;

            object? val;
            switch (value.Kind)
            {
                case JsonKind.Null:
                    if (nullable is not null || !t.IsValueType) prop.SetValue(target, null);
                    continue;
                case JsonKind.Number when t == typeof(long): val = value.Num; break;
                case JsonKind.Number when t == typeof(int): val = (int)value.Num!.Value; break;
                case JsonKind.Number when t == typeof(short): val = (short)value.Num!.Value; break;
                case JsonKind.Number when t == typeof(byte): val = (byte)value.Num!.Value; break;
                case JsonKind.Number when t.IsEnum: val = Enum.ToObject(t, value.Num!.Value); break;
                case JsonKind.Decimal when t == typeof(decimal): val = value.Dec; break;
                case JsonKind.Decimal when t == typeof(float): val = (float)value.Dec!.Value; break;
                case JsonKind.Decimal when t == typeof(double): val = (double)value.Dec!.Value; break;
                case JsonKind.Text when t == typeof(string): val = value.Str; break;
                case JsonKind.Text when t.IsEnum: val = Enum.Parse(t, value.Str!); break;
                case JsonKind.Bool when t == typeof(bool): val = value.Bool; break;
                case JsonKind.Date when t == typeof(DateTime): val = value.Date; break;
                default: continue;
            }

            prop.SetValue(target, val);
        }
    }

    // ===== عمليات الـ Upsert لكل جدول =====

    private static int UpsertCategory(AppDbContext db, DeltaRow row)
    {
        var cat = db.Categories.FirstOrDefault(x => x.SyncId == row.S) ?? new Category { SyncId = row.S, Name = row.F.GetValueOrDefault("Name")?.Str ?? "" };
        if (string.IsNullOrEmpty(cat.OriginDevice)) cat.OriginDevice = row.Origin;
        CopyScalar(cat, row);
        db.Categories.Entry(cat).State = cat.Id == 0 ? EntityState.Added : EntityState.Modified;
        db.SaveChanges();
        return cat.Id;
    }

    private static int UpsertProduct(AppDbContext db, DeltaRow row, Dictionary<string, int> parentKeys)
    {
        var product = db.Products.FirstOrDefault(x => x.SyncId == row.S) ?? new Product { SyncId = row.S, Name = row.F.GetValueOrDefault("Name")?.Str ?? "" };
        if (string.IsNullOrEmpty(product.OriginDevice)) product.OriginDevice = row.Origin;
        CopyScalar(product, row);

        var catId = ResolveRef(db, row, "CategoryId", "Categories");
        if (catId == 0)
            catId = ResolveParent(parentKeys, row.R.GetValueOrDefault("CategoryId"));
        if (catId > 0) product.CategoryId = catId;

        db.Products.Entry(product).State = product.Id == 0 ? EntityState.Added : EntityState.Modified;
        db.SaveChanges();
        return product.Id;
    }

    private static int UpsertCustomer(AppDbContext db, DeltaRow row)
    {
        var customer = db.Customers.FirstOrDefault(x => x.SyncId == row.S) ?? new Customer { SyncId = row.S, Name = row.F.GetValueOrDefault("Name")?.Str ?? "" };
        if (string.IsNullOrEmpty(customer.OriginDevice)) customer.OriginDevice = row.Origin;
        CopyScalar(customer, row);
        db.Customers.Entry(customer).State = customer.Id == 0 ? EntityState.Added : EntityState.Modified;
        db.SaveChanges();
        return customer.Id;
    }

    private static int UpsertSupplier(AppDbContext db, DeltaRow row)
    {
        var supplier = db.Suppliers.FirstOrDefault(x => x.SyncId == row.S) ?? new Supplier { SyncId = row.S, Name = row.F.GetValueOrDefault("Name")?.Str ?? "" };
        if (string.IsNullOrEmpty(supplier.OriginDevice)) supplier.OriginDevice = row.Origin;
        CopyScalar(supplier, row);
        db.Suppliers.Entry(supplier).State = supplier.Id == 0 ? EntityState.Added : EntityState.Modified;
        db.SaveChanges();
        return supplier.Id;
    }

    private static int UpsertSale(AppDbContext db, DeltaRow row, string remoteDeviceId, int resolvedUser, Dictionary<string, int> parentKeys)
    {
        var sale = db.Sales.FirstOrDefault(x => x.SyncId == row.S) ?? new Sale { SyncId = row.S, InvoiceNumber = row.F.GetValueOrDefault("InvoiceNumber")?.Str ?? "" };
        if (string.IsNullOrEmpty(sale.OriginDevice)) sale.OriginDevice = row.Origin;
        CopyScalar(sale, row);

        var customerId = ResolveRef(db, row, "CustomerId", "Customers");
        if (customerId == 0) customerId = ResolveParent(parentKeys, row.R.GetValueOrDefault("CustomerId"));
        sale.CustomerId = customerId > 0 ? customerId : null;
        if (resolvedUser > 0) sale.UserId = resolvedUser;

        db.Sales.Entry(sale).State = sale.Id == 0 ? EntityState.Added : EntityState.Modified;
        db.SaveChanges();
        return sale.Id;
    }

    private static int UpsertSaleItem(AppDbContext db, DeltaRow row, Dictionary<string, int> parentKeys)
    {
        var saleId = ResolveParent(parentKeys, row.R.GetValueOrDefault("SaleId"));
        if (saleId == 0) return 0;

        var productId = ResolveRef(db, row, "ProductId", "Products");
        if (productId == 0) productId = ResolveParent(parentKeys, row.R.GetValueOrDefault("ProductId"));

        var item = db.SaleItems.FirstOrDefault(x => x.SyncId == row.S) ?? new SaleItem { SyncId = row.S, ProductName = row.F.GetValueOrDefault("ProductName")?.Str ?? "" };
        if (string.IsNullOrEmpty(item.OriginDevice)) item.OriginDevice = row.Origin;
        CopyScalar(item, row);
        item.SaleId = saleId;
        if (productId > 0) item.ProductId = productId;

        db.SaleItems.Entry(item).State = item.Id == 0 ? EntityState.Added : EntityState.Modified;
        db.SaveChanges();
        return item.Id;
    }

    private static int UpsertPurchase(AppDbContext db, DeltaRow row, string remoteDeviceId, int resolvedUser, Dictionary<string, int> parentKeys)
    {
        var purchase = db.Purchases.FirstOrDefault(x => x.SyncId == row.S) ?? new Purchase { SyncId = row.S, InvoiceNumber = row.F.GetValueOrDefault("InvoiceNumber")?.Str ?? "" };
        if (string.IsNullOrEmpty(purchase.OriginDevice)) purchase.OriginDevice = row.Origin;
        CopyScalar(purchase, row);

        var supplierId = ResolveRef(db, row, "SupplierId", "Suppliers");
        if (supplierId == 0) supplierId = ResolveParent(parentKeys, row.R.GetValueOrDefault("SupplierId"));
        purchase.SupplierId = supplierId > 0 ? supplierId : null;
        if (resolvedUser > 0) purchase.UserId = resolvedUser;

        db.Purchases.Entry(purchase).State = purchase.Id == 0 ? EntityState.Added : EntityState.Modified;
        db.SaveChanges();
        return purchase.Id;
    }

    private static int UpsertPurchaseItem(AppDbContext db, DeltaRow row, Dictionary<string, int> parentKeys)
    {
        var purchaseId = ResolveParent(parentKeys, row.R.GetValueOrDefault("PurchaseId"));
        if (purchaseId == 0) return 0;

        var productId = ResolveRef(db, row, "ProductId", "Products");
        if (productId == 0) productId = ResolveParent(parentKeys, row.R.GetValueOrDefault("ProductId"));

        var item = db.PurchaseItems.FirstOrDefault(x => x.SyncId == row.S) ?? new PurchaseItem { SyncId = row.S, ProductName = row.F.GetValueOrDefault("ProductName")?.Str ?? "" };
        if (string.IsNullOrEmpty(item.OriginDevice)) item.OriginDevice = row.Origin;
        CopyScalar(item, row);
        item.PurchaseId = purchaseId;
        if (productId > 0) item.ProductId = productId;

        db.PurchaseItems.Entry(item).State = item.Id == 0 ? EntityState.Added : EntityState.Modified;
        db.SaveChanges();
        return item.Id;
    }

    private static int UpsertRepairJob(AppDbContext db, DeltaRow row, string remoteDeviceId, int resolvedUser, Dictionary<string, int> parentKeys)
    {
        var job = db.RepairJobs.FirstOrDefault(x => x.SyncId == row.S) ?? new RepairJob
        {
            SyncId = row.S,
            JobNumber = row.F.GetValueOrDefault("JobNumber")?.Str ?? "",
            CustomerName = row.F.GetValueOrDefault("CustomerName")?.Str ?? "",
            DeviceName = row.F.GetValueOrDefault("DeviceName")?.Str ?? "",
            Issue = row.F.GetValueOrDefault("Issue")?.Str ?? ""
        };
        if (string.IsNullOrEmpty(job.OriginDevice)) job.OriginDevice = row.Origin;
        CopyScalar(job, row);
        if (resolvedUser > 0) job.UserId = resolvedUser;

        db.RepairJobs.Entry(job).State = job.Id == 0 ? EntityState.Added : EntityState.Modified;
        db.SaveChanges();
        return job.Id;
    }

    private static int UpsertRepairPart(AppDbContext db, DeltaRow row, Dictionary<string, int> parentKeys)
    {
        var jobId = ResolveParent(parentKeys, row.R.GetValueOrDefault("RepairJobId"));
        if (jobId == 0) return 0;

        var productId = ResolveRef(db, row, "ProductId", "Products");
        if (productId == 0) productId = ResolveParent(parentKeys, row.R.GetValueOrDefault("ProductId"));

        var part = db.RepairParts.FirstOrDefault(x => x.SyncId == row.S) ?? new RepairPart { SyncId = row.S, ProductName = row.F.GetValueOrDefault("ProductName")?.Str ?? "" };
        if (string.IsNullOrEmpty(part.OriginDevice)) part.OriginDevice = row.Origin;
        CopyScalar(part, row);
        part.RepairJobId = jobId;
        if (productId > 0) part.ProductId = productId;

        db.RepairParts.Entry(part).State = part.Id == 0 ? EntityState.Added : EntityState.Modified;
        db.SaveChanges();
        return part.Id;
    }

    private static int UpsertVoucher(AppDbContext db, DeltaRow row, string remoteDeviceId, int resolvedUser, Dictionary<string, int> parentKeys)
    {
        var voucher = db.Vouchers.FirstOrDefault(x => x.SyncId == row.S) ?? new Voucher { SyncId = row.S, VoucherNumber = row.F.GetValueOrDefault("VoucherNumber")?.Str ?? "" };
        if (string.IsNullOrEmpty(voucher.OriginDevice)) voucher.OriginDevice = row.Origin;
        CopyScalar(voucher, row);

        var customerId = ResolveRef(db, row, "CustomerId", "Customers");
        if (customerId == 0) customerId = ResolveParent(parentKeys, row.R.GetValueOrDefault("CustomerId"));
        voucher.CustomerId = customerId > 0 ? customerId : 0;
        if (resolvedUser > 0) voucher.UserId = resolvedUser;

        db.Vouchers.Entry(voucher).State = voucher.Id == 0 ? EntityState.Added : EntityState.Modified;
        db.SaveChanges();
        return voucher.Id;
    }

    private static void InsertLocalOp(AppDbContext db, DeltaPacket packet, int resolvedUser, Dictionary<string, int> parentKeys, string origin, long originSeq)
    {
        var primaryS = PrimarySyncId(packet);
        var localEntityId = parentKeys.TryGetValue(primaryS, out var id) ? id : packet.EntityId;
        var nextSeq = db.OperationLogs.Any() ? db.OperationLogs.Max(o => o.Seq) + 1 : 1;

        db.OperationLogs.Add(new OperationLog
        {
            Seq = nextSeq, // الترقيم المحلي للسجل — لا يُعاد ترقيم هوية العملية (OriginSeq/OriginDevice أدناه)
            OpType = packet.OpType,
            EntityName = packet.EntityName,
            EntityId = localEntityId,
            DocumentNumber = packet.Document,
            Amount = packet.Amount,
            UserId = resolvedUser,
            UserRole = (UserRole)packet.UserRole,
            DeviceRole = (DeviceRole)packet.DeviceRole,
            SummaryJson = packet.SummaryJson,
            CommittedAt = packet.CommittedAtUtc,
            OriginDevice = origin,   // أصل العملية الحقيقي (قد يختلف عن المُرسِل في التمرير)
            OriginSeq = originSeq    // ترقيم الأصل — لا يُعاد ترقيمه
        });
    }

    private static void RecordReceipt(AppDbContext db, string origin, long originSeq, DeltaPacket packet, string? ownerDeviceId)
    {
        var owner = ownerDeviceId;
        if (string.IsNullOrWhiteSpace(owner)) owner = PhoneAccounting.App.Services.Session.DeviceId;
        if (string.IsNullOrWhiteSpace(owner)) owner = "unknown";

        db.SyncLogs.Add(new SyncLogEntry
        {
            DeviceId = owner,
            OriginDevice = origin,
            OriginSeq = originSeq,
            OpType = packet.OpType,
            EntityName = packet.EntityName,
            EntitySyncId = PrimarySyncId(packet),
            Direction = "In",
            ReceivedAtUtc = System.DateTime.UtcNow
        });
    }

    /// <summary>
    /// SyncId الكيان «الأساس» للحزمة — صفُّ الكيان الذي يُحصي باسمه EntityId للسجل، لا صفّ أطفال/مرافق
    /// (الحزم قد تسبق صف الكيان بصفوف Categories/Products مؤثَّرة قبل سطر OperationLog لنفسه).
    /// </summary>
    private static string PrimarySyncId(DeltaPacket packet)
    {
        if (packet.Rows.Count == 0) return "";
        var primary = packet.Rows.LastOrDefault(x => x.T == packet.EntityName);
        return primary?.S ?? packet.Rows[0].S;
    }
}