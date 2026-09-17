using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PhoneAccounting.App.Models;

/// <summary>
/// خريطة علامات الماء لكل مصدر (البند 15.2 من SYNC-DESIGN): لمعرفة لكل قريبٍ من هذا الجهاز
/// مدى ما تلقّاه من كل مصدر — {المصدر: آخر OriginSeq}. عند delta_request يرسل الجهاز خريطته
/// فيجيب القريب بكل ما يتجاوزها أيًّا كان مصدره (أصلياته ومُمرَّراته).
/// المفتاح: (OwnerDeviceId, OriginDevice).
/// </summary>
[Table("SyncPeerState")]
public class SyncPeerState
{
    [Key]
    public int Id { get; set; }

    /// <summary>هذا الجهاز (مالك الخريطة).</summary>
    [Required]
    [MaxLength(128)]
    public string OwnerDeviceId { get; set; } = string.Empty;

    /// <summary>المصدر الذي تتبعه الخريطة.</summary>
    [Required]
    [MaxLength(128)]
    public string OriginDevice { get; set; } = string.Empty;

    /// <summary>آخر OriginSeq معروف لهذا المصدر (0 = لم يصل شيء بعد).</summary>
    public long LastOriginSeq { get; set; }

    public DateTime? UpdatedAt { get; set; }
}