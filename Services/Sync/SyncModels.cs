using System;
using System.Collections.Generic;

namespace PhoneAccounting.App.Services.Sync;

/// <summary>صف مُسلسل من عملية = وحدة الدلتا. كل صف يحمل SyncId (مفتاح الربط الوحيد) وحقوله + مراجع FKs بالـ SyncId ليُعاد ربطها محلياً عند الوجهة.</summary>
public sealed class DeltaRow
{
    public string T { get; set; } = "";                       // اسم الجدول (Sales/SaleItems/...)
    public string S { get; set; } = "";                       // SyncId
    public string? Origin { get; set; }                       // منشئ الصف الأصلي
    public Dictionary<string, JsonScalar> F { get; set; } = new();  // الحقول القياسية
    public Dictionary<string, string?> R { get; set; } = new();     // مراجع FKs: {اسم الخاصية: SyncId}
}

/// <summary>حاوية مرنة لقيمة JSON أساسية (عدد/نص/منطقي/تاريخ/فارغ).</summary>
public sealed class JsonScalar
{
    public JsonKind Kind { get; set; }
    public long? Num { get; set; }
    public string? Str { get; set; }
    public bool? Bool { get; set; }
    public decimal? Dec { get; set; }
    public DateTime? Date { get; set; }

    public static JsonScalar OfNull() => new() { Kind = JsonKind.Null };
    public static JsonScalar Of(long v) => new() { Kind = JsonKind.Number, Num = v };
    public static JsonScalar Of(decimal v) => new() { Kind = JsonKind.Decimal, Dec = v };
    public static JsonScalar Of(bool v) => new() { Kind = JsonKind.Bool, Bool = v };
    public static JsonScalar Of(string v) => new() { Kind = JsonKind.Text, Str = v };
    public static JsonScalar Of(DateTime v) => new() { Kind = JsonKind.Date, Date = v };
}

public enum JsonKind { Null, Number, Decimal, Text, Bool, Date }

/// <summary>عملية كاملة جاهزة للنقل: بيانات سطر العملية + صفوف الكيانات بأثرها (شاهد القبر لأي حذف).
/// هوية العملية عبر الشبكة = (OriginDevice, OriginSeq) — لا تُعدَّل بالترحيل.</summary>
public sealed class DeltaPacket
{
    /// <summary>المصدر الحقيقي للعملية (ليس بالضرورة المُرسِل — تمرير في الانتشارية).</summary>
    public string? OriginDevice { get; set; }

    /// <summary>الترقيم عند المصدر الحقيقي — أساس idempotency وعلامات الماء.</summary>
    public long OriginSeq { get; set; }

    /// <summary>الترقيم المحلي عند باني الحزمة (مرجعي — أُبقي للتوافق الخلفي).</summary>
    public int OpSeq { get; set; }
    public string OpType { get; set; } = "";
    public string? EntityName { get; set; }
    public int? EntityId { get; set; }
    public string? Document { get; set; }
    public decimal Amount { get; set; }
    public int UserId { get; set; }
    public string? UserName { get; set; }
    public int UserRole { get; set; }
    public int DeviceRole { get; set; }
    public string? SummaryJson { get; set; }
    public DateTime CommittedAtUtc { get; set; }
    public List<DeltaRow> Rows { get; set; } = new();
}