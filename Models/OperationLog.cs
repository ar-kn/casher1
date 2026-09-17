namespace PhoneAccounting.App.Models;

public enum OperationType
{
    Sale = 0,
    SaleEdit = 1,
    SaleVoid = 2,
    Purchase = 3,
    PurchaseEdit = 4,
    PurchaseReturn = 5,
    VoucherIn = 6,
    RepairJob = 7,
    RepairPartUsage = 8,
    StockAdjustment = 9,
    CloseDay = 10,
    /// <summary>قرار حسم صراع (P3.1) — عملية موقّعة جديدة من MAIN فقط، تحمل القيمة الفائزة في صفوفها وتُغلق الصراع محلياً عند كل جهاز.</summary>
    ConflictResolution = 11,
    /// <summary>استدراك مصالحة (P3.3) — عملية موقّعة جديدة (OriginDevice+OriginSeq) تعوّض/تعكس بلا إعادة كتابة التاريخ، تنتشر بالدلتا idempotent.</summary>
    StockCorrection = 12
}

public class OperationLog
{
    public long Id { get; set; }

    /// <summary>الترتيب الصارم داخل الجهاز — يُمنح عند Commit فقط (لا عند الإنشاء في الذاكرة).</summary>
    public long Seq { get; set; }

    public required string OpType { get; set; }
    public string? EntityName { get; set; }
    public int? EntityId { get; set; }
    public string? DocumentNumber { get; set; }
    public decimal Amount { get; set; }
    public int UserId { get; set; }
    public UserRole UserRole { get; set; }
    public DeviceRole DeviceRole { get; set; }
    public string? SummaryJson { get; set; }
    public DateTime CommittedAt { get; set; }

    /// <summary>الجهة الأصلية الكاتبة للعملية: DeviceId المُنشئ محلياً، وDeviceId الجهاز البعيد عند استلامها منه.
    /// أساس منع الإرجاع (echo) بين جهازين — العمليات ذات أصل الطرف المقابل لا تُعاد إليه.</summary>
    public string? OriginDevice { get; set; }

    /// <summary>الترقيم عند الجهة الأصلية (هوية العملية عبر الشبكة في الانتشارية).
    /// للعمليات المحلية = Seq نفسه (يُملأ عند منح الـ Seq)؛ للمدمجة من دلتا = OriginSeq الحزمة — لا يُعاد ترقيمه.</summary>
    public long OriginSeq { get; set; }
}