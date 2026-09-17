namespace PhoneAccounting.App.Infrastructure;

/// <summary>أنواع قطع المنتجات المستخدمة في مجلدات المخزون (براند ← نوع).</summary>
public static class SubTypeHelper
{
    public static IReadOnlyList<string> Options { get; } =
    [
        "هاتف", "شاشة", "فلت شحن", "كفر/غلاف", "لاصق", "شاحن", "كيبل", "سماعة", "ظهر", "أخرى"
    ];

    public static string Detect(string name, string categoryName)
    {
        if (name.Contains("شاشة")) return "شاشة";
        if (name.Contains("فلت")) return "فلت شحن";
        if (name.Contains("كفر") || categoryName.Contains("كفر")) return "كفر/غلاف";
        if (name.Contains("لاصق")) return "لاصق";
        if (name.Contains("شاحن")) return "شاحن";
        if (name.Contains("كيبل")) return "كيبل";
        if (name.Contains("سماعة")) return "سماعة";
        if (name.Contains("ظهر")) return "ظهر";
        if (name.Contains("هاتف") || categoryName is "آيفون" or "سامسونج" or "ريلمي" or "ريدمي" or "تكنو")
            return "هاتف";
        return "أخرى";
    }
}