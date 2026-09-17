using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;

namespace PhoneAccounting.App.Services;

/// <summary>
/// مسار الكتابة الموحّد (Unit of Work) — كل عملية تجارية تمر عبر Register ثم db.SaveChanges():
/// 1) يُمنح Seq عند Commit (لا عند إنشاء الصف في الذاكرة).
/// 2) سطر العملية يُكتب في OperationLog داخل نفس الـ transaction (فيفشل الحفظ يسقط السطر معه).
/// 3) هوية المستخدم + الدور + الجهاز تُلتقط من Session وقت التنفيذ.
/// </summary>
public static class OperationWriter
{
    public static OperationLog Register(
        AppDbContext db,
        OperationType type,
        string? documentNumber,
        decimal amount,
        string? entityName = null,
        int? entityId = null,
        string? summaryJson = null)
    {
        // Seq=0 علامة انتظار: الرقم يُمنح لحظة Commit فعلياً داخل SaveChanges override
        // (AppDbContext.AssignPendingOpSeqs) — لا عند الاستدعاء، فلا يُستهلك رقم لعملية لم تُحفظ.
        var operation = new OperationLog
        {
            Seq = 0,
            OpType = type.ToString(),
            EntityName = entityName,
            EntityId = entityId,
            DocumentNumber = documentNumber,
            Amount = amount,
            UserId = Session.CurrentUser?.Id ?? 0,
            UserRole = Session.CurrentUser?.Role ?? UserRole.Admin,
            DeviceRole = Session.Device,
            SummaryJson = summaryJson,
            CommittedAt = DateTime.UtcNow,
            OriginDevice = Session.DeviceId
        };

        db.OperationLogs.Add(operation);
        return operation;
    }

    /// <summary>يربط المعرّف المحلي للكيان الرئيسي بعد الحفظ (للمستندات الجديدة التي لا يحضر Id إلا بعد SaveChanges).</summary>
    public static void SetEntityTarget(OperationLog operation, string entityName, int entityId)
    {
        operation.EntityName = entityName;
        operation.EntityId = entityId;
    }

    /// <summary>أنواع العمليات الوثائقية التي يجب أن تحمل EntityId (فرض M2.4).</summary>
    public static readonly string[] DocumentOpTypes =
    {
        nameof(OperationType.Sale),
        nameof(OperationType.SaleEdit),
        nameof(OperationType.Purchase),
        nameof(OperationType.PurchaseEdit),
        nameof(OperationType.RepairJob),
        nameof(OperationType.RepairPartUsage),
        nameof(OperationType.VoucherIn)
    };

    /// <summary>تدقيق قابل للفرض: مستندات في السجل تفتقر EntityId — يُستدعى بعد أي حفظ للرصد (لا تصحيح تلقائي).</summary>
    public static List<OperationLog> DocumentOpsMissingEntityId(AppDbContext db)
        => db.OperationLogs.AsNoTracking().Where(o => DocumentOpTypes.Contains(o.OpType) && o.EntityId == null).ToList();
}