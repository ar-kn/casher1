using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;

namespace PhoneAccounting.App.Services;

/// <summary>
/// إقفال اليوم (الاعتماد): بمجرد تنفيذه تُقفل فواتير اليوم وكل الأيام قبلها نهائياً
/// من التعديل/الحذف. الإقفال = إما عملية CloseDay صريحة أو أول مزامنة ناجحة (أيهما أسبق) —
/// والمزامنة غير مفعلة بعد في المرحلة 1، لذا الأساس هنا هو CloseDay.
/// </summary>
public static class CloseDayService
{
    public const string LastCloseDateKey = "LastCloseDate";

    /// <summary>آخر تاريخ مقفَل — المستندات بتاريخ مساوٍ أو أسبق أصبحت نهائية.</summary>
    public static DateTime LastCloseDate
    {
        get
        {
            var raw = Database.GetSetting(LastCloseDateKey, "");
            return DateTime.TryParse(raw, out var d) ? d.Date : DateTime.MinValue.Date;
        }
    }

    /// <summary>هل المستند مقفل (يومه مساوٍ أو أسبق من آخر إقفال)؟</summary>
    public static bool IsLocked(DateTime documentDate) => documentDate.Date <= LastCloseDate;

    public static bool CanClose => Session.CurrentUser?.Role is UserRole.Admin or UserRole.Accountant;

    /// <summary>
    /// ينفذ إقفال اليوم: يسجّل عملية CloseDay في سجل العمليات ويثبّت تاريخ الإقفال
    /// في نفس الـ transaction (حفظ واحد).
    /// </summary>
    public static string Execute(DateTime? closeDate = null)
    {
        if (!CanClose)
            return "إقفال اليوم متاح فقط للمدير أو المحاسب";

        var target = (closeDate ?? DateTime.Today).Date;
        if (target <= LastCloseDate)
            return $"اليوم {target:yyyy-MM-dd} مقفَل سلفاً (آخر إقفال: {LastCloseDate:yyyy-MM-dd})";

        using var db = new AppDbContext();

        var operation = OperationWriter.Register(
            db,
            OperationType.CloseDay,
            target.ToString("yyyy-MM-dd"),
            0,
            entityName: "Days",
            summaryJson: $"{{\"closeDate\":\"{target:yyyy-MM-dd:O}\"}}");

        var setting = db.AppSettings.FirstOrDefault(s => s.Key == LastCloseDateKey);
        if (setting is null)
            db.AppSettings.Add(new AppSetting { Key = LastCloseDateKey, Value = target.ToString("yyyy-MM-dd") });
        else
            setting.Value = target.ToString("yyyy-MM-dd");

        db.SaveChanges();

        _ = operation; // سطر العملية يُحفظ مع الإعداد في نفس SaveChanges
        return $"تم إقفال اليوم {target:yyyy-MM-dd} — فواتير هذا اليوم وما قبله أصبحت نهائية";
    }
}