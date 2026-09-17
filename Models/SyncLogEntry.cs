using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PhoneAccounting.App.Models;

/// <summary>
/// إيصال الدلتا — هوية العملية عبر الشبكة في الانتشارية:
/// - هوية العملية = `(OriginDevice, OriginSeq)` وليست المُرسِل: العملية الواحدة قد تصل من مُرسِلَين مختلفين (تمرير).
/// - سطور «Out» = عمليات مُرسلة (مصدرها هذا الجهاز غالباً) للتحقق من عدم إعادة الإرسال.
/// - سطور «In» = إيصالات واردة (أُحدِث هنا) للتحقق من عدم التكرار عند التطبيق.
/// لا يُزامَن هذا الجدول هو نفسه (أداة، لا بيانات عمل).
/// </summary>
[Table("SyncLog")]
public class SyncLogEntry
{
    [Key]
    public int Id { get; set; }

    /// <summary>الجهة المالكة للسجل (هذا الجهاز) — معلوماتي.</summary>
    [Required]
    [MaxLength(128)]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>المصدر الحقيقي للعملية (قد يختلف عن المُرسِل في التمرير).</summary>
    [Required]
    [MaxLength(128)]
    public string OriginDevice { get; set; } = string.Empty;

    /// <summary>الترقيم عند المصدر الحقيقي — مع OriginDevice يشكّل مفتاح idempotency (UNIQUE).</summary>
    public long OriginSeq { get; set; }

    [MaxLength(64)]
    public string? OpType { get; set; }

    [MaxLength(64)]
    public string? EntityName { get; set; }

    [MaxLength(256)]
    public string? EntitySyncId { get; set; }

    public string? PayloadJson { get; set; }

    public DateTime? CommittedAtUtc { get; set; }

    [MaxLength(8)]
    public string? Direction { get; set; } // Out | In

    public DateTime? SentAtUtc { get; set; }

    public DateTime? ReceivedAtUtc { get; set; }
}