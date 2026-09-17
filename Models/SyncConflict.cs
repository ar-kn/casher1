using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PhoneAccounting.App.Models;

/// <summary>
/// صراع مُلتقَط (P3.1 §3): يُحفظ الطرفان بلا منتصر، ويُكتشف نفس الصراع على أكثر من جهاز
/// — فالتعرّف حتمي من SyncId الكيان فيدمج السجلان بدل أن يتضاعفا (قرار تثبيت #1).
/// الحسم يدوي حصراً على MAIN عبر عملية موقّعة؛ وصولها = إغلاق الصف محلياً،
/// و«القيمة المتروكة» تُحفظ دائماً في ResolvedValueJson.
/// </summary>
[Table("SyncConflicts")]
public class SyncConflict
{
    [Key]
    public int Id { get; set; }

    /// <summary>هوية حتمية = SyncId الكيان المتعارَش عليه — لا يتبع هويةً محلية لأي جهاز.</summary>
    [Required]
    [MaxLength(256)]
    public string ConflictSyncId { get; set; } = string.Empty;

    /// <summary>جدول الكيان (Products/Sales/...).</summary>
    [Required]
    [MaxLength(64)]
    public string EntityTable { get; set; } = string.Empty;

    /// <summary>قيمة هذا الجهاز لحظة الالتقاط (أسطر DeltaRow مُسلسلة) — صورة الواقع المحلي كاملة.</summary>
    public string? LocalJson { get; set; }

    /// <summary>قيمة الطرف الوارد لحظة الالتقاط — «صراع-واحد-لكيان»: التعديلات اللاحقة تُحدّثها في ذات السجل.</summary>
    public string? RemoteJson { get; set; }

    public bool IsResolved { get; set; }

    /// <summary>KeepLocal | KeepRemote | StaysDeleted — اسم قيمته من ConflictResolutionType.</summary>
    [MaxLength(32)]
    public string? ResolutionType { get; set; }

    [MaxLength(128)]
    public string? ResolvedBy { get; set; }

    public string? ResolvedReason { get; set; }

    public DateTime? ResolvedAt { get; set; }

    /// <summary>«القيمة المتروكة» (الطرف الخاسر) تُحفظ دائماً — أي وجهٍ للحقيقة لا يضيع أبداً.</summary>
    public string? ResolvedValueJson { get; set; }

    /// <summary>بصمة القرار (P3.2C): OriginSeq لعملية الحسم الموقّعة في السجل — الحسم لا يُغلق إلا بها.</summary>
    public long? ResolvedAtSeq { get; set; }

    /// <summary>جهة إصدار عملية الحسم (OriginDevice) — مع ResolvedAtSeq تعريفٌ كامل للعملية في السجل.</summary>
    public string? ResolutionOpOrigin { get; set; }

    public DateTime? LastUpdatedAt { get; set; }
}