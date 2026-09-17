using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;

namespace PhoneAccounting.App.Services;

public static class Session
{
    public static User? CurrentUser { get; set; }
    public static bool IsLoggedIn => CurrentUser is not null;

    /// <summary>معرّف الجهاز الثابت — يُستبدل في الاختبارات/المحاكاة فقط (M2.x) وليس في التشغيل الحقيقي.</summary>
    public static string? DeviceIdOverride { get; set; }

    public static string CompanyName => Database.GetSetting("CompanyName", "شركة الهواتف");
    public static string Currency => Database.GetSetting("Currency", "د.ع");
    public static DeviceRole Device => Enum.TryParse(Database.GetSetting("DeviceRole", "Cashier"), out DeviceRole role)
        ? role
        : DeviceRole.Cashier;

    public static string DeviceId => DeviceIdOverride ?? Database.GetSetting("DeviceId", "");
}
