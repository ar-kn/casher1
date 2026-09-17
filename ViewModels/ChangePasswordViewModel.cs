using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Services;

namespace PhoneAccounting.App.ViewModels;

public partial class ChangePasswordViewModel : ObservableObject
{
    [ObservableProperty]
    private string currentPassword = "";

    [ObservableProperty]
    private string newPassword = "";

    [ObservableProperty]
    private string confirmPassword = "";

    [ObservableProperty]
    private string errorMessage = "";

    public bool IsForced { get; }
    public bool Success { get; private set; }

    public string Hint => IsForced
        ? "كلمة مرور الحساب ما تزال افتراضية/ضعيفة — يجب تغييرها قبل متابعة العمل."
        : "غيّر كلمة مرور حسابك المشفّرة. احتفظ بها سرية ولا تشاركها.";

    public event EventHandler? CloseRequested;

    public ChangePasswordViewModel(bool isForced = false)
    {
        IsForced = isForced;
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = "";

        var user = Session.CurrentUser;
        if (user is null)
        {
            ErrorMessage = "لا توجد جلسة نشطة — أعد تسجيل الدخول";
            return;
        }

        if (!PasswordHasher.Verify(CurrentPassword, user.PasswordHash))
        {
            ErrorMessage = "كلمة المرور الحالية غير صحيحة";
            return;
        }

        if (!AuthService.IsValidPassword(NewPassword))
        {
            ErrorMessage = "كلمة المرور الجديدة: 8 أحرف على الأقل وتحتوي حرفاً ورقماً";
            return;
        }

        if (NewPassword != ConfirmPassword)
        {
            ErrorMessage = "كلمتا المرور غير متطابقتين";
            return;
        }

        try
        {
            using var db = new AppDbContext();
            var row = db.Users.First(u => u.Id == user.Id);
            row.PasswordHash = PasswordHasher.Hash(NewPassword);
            row.MustChangePassword = false;
            db.SaveChanges();

            // تحديث جلسة المستخدم الحالية بأحدث قيمة
            user.PasswordHash = row.PasswordHash;
            user.MustChangePassword = false;

            AuthService.LogAttempt(db, user.Username, true, "password_changed");

            Success = true;
            CurrentPassword = "";
            NewPassword = "";
            ConfirmPassword = "";
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"خطأ: {ex.Message}";
        }
    }
}