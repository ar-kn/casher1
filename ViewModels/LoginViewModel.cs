using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Services;
using PhoneAccounting.App.Views;
using System.Windows;

namespace PhoneAccounting.App.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    [ObservableProperty]
    private string username = "";

    [ObservableProperty]
    private string password = "";

    [ObservableProperty]
    private string errorMessage = "";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string companyName = "";

    private bool _mustChangePassword;

    public LoginViewModel()
    {
        companyName = Session.CompanyName;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "أدخل اسم المستخدم وكلمة المرور";
            return;
        }

        IsBusy = true;
        ErrorMessage = "";
        _mustChangePassword = false;

        try
        {
            await Task.Run(() =>
            {
                using var db = new AppDbContext();
                var username = Username.Trim();

                var lockedSeconds = AuthService.RemainingLockSeconds(db, username);
                if (lockedSeconds > 0)
                {
                    AuthService.LogAttempt(db, username, false, "locked");
                    ErrorMessage = $"هذا الحساب مقفل مؤقتاً بسبب محاولات فاشلة — حاول بعد {Math.Ceiling(lockedSeconds / 60.0):0} دقيقة";
                    return;
                }

                var user = db.Users.AsNoTracking()
                    .FirstOrDefault(u => u.Username == username);

                if (user is null || !PasswordHasher.Verify(Password, user.PasswordHash))
                {
                    AuthService.LogAttempt(db, username, false, "wrong_credentials");
                    var remaining = AuthService.RemainingLockSeconds(db, username);
                    ErrorMessage = remaining > 0
                        ? $"محاولات فاشلة كثيرة — الحساب مقفل مؤقتاً لمدة {Math.Ceiling(remaining / 60.0):0} دقيقة"
                        : "اسم المستخدم أو كلمة المرور غير صحيحة";
                    return;
                }

                if (!user.IsActive)
                {
                    AuthService.LogAttempt(db, username, false, "inactive");
                    ErrorMessage = "هذا الحساب موقوف — راجع مدير النظام";
                    return;
                }

                user.LastLoginAt = DateTime.Now;
                db.Update(user);
                db.SaveChanges();

                Session.CurrentUser = user;
                _mustChangePassword = user.MustChangePassword;

                AuthService.LogAttempt(db, username, true, null);
            });

            if (ErrorMessage.Length > 0) return;

            if (_mustChangePassword)
            {
                MustChangePassword();
            }
            else
            {
                OpenMainWindow();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"خطأ: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>حساب بحاجة لتغيير كلمة مرور إلزامي (افتراضية/ضعيفة) — لا يفتح الرئيسية قبل التغيير.</summary>
    private void MustChangePassword()
    {
        MessageBox.Show(
            "حسابك يستخدم كلمة مرور افتراضية — عليك تغييرها قبل متابعة العمل.",
            "تغيير كلمة المرور إلزامي",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        var window = new ChangePasswordWindow(isForced: true);
        window.ShowDialog();

        if (window.Success)
        {
            OpenMainWindow();
        }
        else
        {
            Session.CurrentUser = null;
            var login = new LoginWindow();
            System.Windows.Application.Current.MainWindow?.Close();
            login.Show();
        }
    }

    private void OpenMainWindow()
    {
        try
        {
            var window = new MainWindow();
            System.Windows.Application.Current.MainWindow?.Close();
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"خطأ عند فتح النافذة الرئيسية:\n{ex}",
                "خطأ",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}