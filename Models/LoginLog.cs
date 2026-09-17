namespace PhoneAccounting.App.Models;

/// <summary>
/// سجل محاولات الدخول — مرجع تدقيق أمني محلي (لا يُزامَن): كل محاولة ناجحة/فاشلة/مقفلة
/// تُسجَّل مع الجهاز والسبب. يُستخدم أيضاً كأساس لقفل الحساب ضد تخمين كلمات المرور.
/// </summary>
public class LoginLog
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public bool Success { get; set; }
    public string? Reason { get; set; }
    public DateTime AttemptedAt { get; set; }
}