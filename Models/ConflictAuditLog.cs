namespace PhoneAccounting.App.Models;

/// <summary>
/// سجل تدقيق محلي للحسم (P3.2C) — مسار «من حاول حسم ما لا يملكه» على نمط LoginLogs:
/// لا يُزامَن إطلاقاً (أداة محلية كاشفة، ليست بيانات عمل)، والاحتفاظ دائم.
/// يُكتب قبولُ الحسم أيضاً فيكون موازياً موضوعياً لبصمة العملية في السجل.
/// </summary>
public class ConflictAuditLog
{
    public long Id { get; set; }

    public string UserName { get; set; } = "";

    public string UserRole { get; set; } = "";

    public string DeviceId { get; set; } = "";

    public string ConflictSyncId { get; set; } = "";

    public string EntityTable { get; set; } = "";

    /// <summary>Accepted | Rejected — ثابتان.</summary>
    public string Outcome { get; set; } = "";

    /// <summary>سبب الرفض (RoleMismatch …) أو ملاحظة القبول.</summary>
    public string? Reason { get; set; }

    public DateTime AttemptedAt { get; set; }
}