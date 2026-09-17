using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;

namespace PhoneAccounting.App.Services;

/// <summary>
/// حماية عمود الهوية: قفل ضد القوة الغاشمة (يُحتسب من سجل الدخول نفسه فينجو من إعادة التشغيل)،
/// تسجيل كل محاولات الدخول (من؟ متى؟ على أي جهاز؟ لماذا فشل؟)، وسياسة كلمات مرور صارمة.
/// </summary>
public static class AuthService
{
    public const int MaxFailures = 10;          // عدد المحاولات الفاشلة قبل القفل (10 بدل 5 — مرونة عمل)
    public const int LockWindowMinutes = 10;    // النافذة الزمنية التي تُحتسب فيها المحاولات
    public const int LockDurationMinutes = 2;   // مدة القفل المؤقت — دقيقتان كافية ضد التخمين ولا تعطل الدوام

    /// <summary>هل الحساب مقفل مؤقتاً الآن؟</summary>
    public static bool IsLocked(AppDbContext db, string username) => RemainingLockSeconds(db, username) > 0;

    /// <summary>الثواني المتبقية من القفل المؤقت (0 = غير مقفل).</summary>
    public static int RemainingLockSeconds(AppDbContext db, string username)
    {
        var windowStart = DateTime.UtcNow.AddMinutes(-LockWindowMinutes);
        var recent = db.LoginLogs
            .Where(l => l.Username == username && !l.Success && l.AttemptedAt >= windowStart)
            .OrderByDescending(l => l.AttemptedAt)
            .Take(MaxFailures)
            .ToList();

        if (recent.Count < MaxFailures) return 0;

        var unlocksAt = recent[0].AttemptedAt.AddMinutes(LockDurationMinutes);
        return Math.Max((int)(unlocksAt - DateTime.UtcNow).TotalSeconds, 0);
    }

    /// <summary>يُسجّل محاولة دخول في السجل الأمني (ناجحة / فاشلة / أخرى).</summary>
    public static void LogAttempt(AppDbContext db, string username, bool success, string? reason)
    {
        db.LoginLogs.Add(new LoginLog
        {
            Username = username,
            DeviceId = string.IsNullOrWhiteSpace(Session.DeviceId) ? "unknown" : Session.DeviceId,
            Success = success,
            Reason = reason,
            AttemptedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    /// <summary>
    /// سياسة كلمة المرور: 8 أحرف على الأقل وتحتوي حرفاً ورقماً.
    /// </summary>
    public static bool IsValidPassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8) return false;
        return password.Any(char.IsLetter) && password.Any(char.IsDigit);
    }
}