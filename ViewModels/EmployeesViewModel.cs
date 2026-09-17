using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;
using PhoneAccounting.App.Services;
using System.Collections.ObjectModel;

namespace PhoneAccounting.App.ViewModels;

public partial class EmployeeRow : ObservableObject
{
    public int Id { get; init; }
    public required string Username { get; init; }
    public required string DisplayName { get; init; }
    public required string RoleTitle { get; init; }
    public required string CreatedDate { get; init; }

    [ObservableProperty]
    private bool isActive;

    [ObservableProperty]
    private bool isAdmin;
}

public partial class EmployeesViewModel : ObservableObject
{
    public ObservableCollection<EmployeeRow> Employees { get; } = [];

    public List<RoleOption> Roles { get; } =
    [
        new(UserRole.Cashier, "كاشير"),
        new(UserRole.Accountant, "محاسب"),
        new(UserRole.RepairTech, "فني صيانة"),
        new(UserRole.Inventory, "مسؤول مخزون")
    ];

    [ObservableProperty]
    private string newDisplayName = "";

    [ObservableProperty]
    private string newUsername = "";

    [ObservableProperty]
    private string newPassword = "";

    [ObservableProperty]
    private UserRole newRole = UserRole.Cashier;

    [ObservableProperty]
    private string errorMessage = "";

    [ObservableProperty]
    private string successMessage = "";

    public bool CanManage => Session.CurrentUser?.Role == UserRole.Admin;

    public EmployeesViewModel()
    {
        Load();
    }

    private void Load()
    {
        Employees.Clear();

        using var db = new AppDbContext();
        foreach (var user in db.Users.AsNoTracking().OrderBy(u => u.DisplayName).ToList())
        {
            Employees.Add(new EmployeeRow
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                RoleTitle = RoleTitleFor(user.Role),
                CreatedDate = user.CreatedAt.ToString("yyyy/MM/dd"),
                IsActive = user.IsActive,
                IsAdmin = user.Role == UserRole.Admin
            });
        }
    }

    [RelayCommand]
    private void AddEmployee()
    {
        ErrorMessage = "";
        SuccessMessage = "";

        if (string.IsNullOrWhiteSpace(NewDisplayName) ||
            string.IsNullOrWhiteSpace(NewUsername) ||
            string.IsNullOrEmpty(NewPassword))
        {
            ErrorMessage = "أكمل جميع الحقول (الاسم، اسم المستخدم، كلمة المرور)";
            return;
        }

        if (NewUsername.Length < 3)
        {
            ErrorMessage = "اسم المستخدم 3 أحرف على الأقل";
            return;
        }

        if (!AuthService.IsValidPassword(NewPassword))
        {
            ErrorMessage = "كلمة المرور: 8 أحرف على الأقل وتحتوي حرفاً ورقماً";
            return;
        }

        try
        {
            using var db = new AppDbContext();
            if (db.Users.Any(u => u.Username == NewUsername.Trim()))
            {
                ErrorMessage = "اسم المستخدم موجود مسبقاً";
                return;
            }

            db.Users.Add(new User
            {
                Username = NewUsername.Trim(),
                DisplayName = NewDisplayName.Trim(),
                PasswordHash = PasswordHasher.Hash(NewPassword),
                Role = NewRole,
                IsActive = true
            });
            db.SaveChanges();

            SuccessMessage = $"تمت إضافة الموظف «{NewDisplayName.Trim()}»";
            NewDisplayName = "";
            NewUsername = "";
            NewPassword = "";
            Load();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"خطأ: {ex.Message}";
        }
    }

    [RelayCommand]
    private void DeleteEmployee(EmployeeRow? row)
    {
        if (row is null) return;
        if (row.IsAdmin)
        {
            ErrorMessage = "لا يمكن حذف حساب المدير الرئيسي";
            return;
        }

        var result = System.Windows.MessageBox.Show(
            $"هل أنت متأكد من حذف الموظف «{row.DisplayName}»؟",
            "تأكيد الحذف",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            using var db = new AppDbContext();
            var user = db.Users.Find(row.Id);
            if (user is not null)
            {
                db.Users.Remove(user);
                db.SaveChanges();
            }

            SuccessMessage = $"تم حذف الموظف «{row.DisplayName}»";
            ErrorMessage = "";
            Load();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"لا يمكن حذف الموظف — ربما لديه سجلات مرتبطة:\n{ex.Message}";
        }
    }

    [RelayCommand]
    private void ToggleActive(EmployeeRow? row)
    {
        if (row is null) return;
        if (row.IsAdmin)
        {
            ErrorMessage = "لا يمكن إيقاف حساب المدير الرئيسي";
            row.IsActive = true;
            return;
        }

        using var db = new AppDbContext();
        var user = db.Users.Find(row.Id);
        if (user is not null)
        {
            user.IsActive = row.IsActive;
            db.SaveChanges();
        }
    }

    public static string RoleTitleFor(UserRole role) => role switch
    {
        UserRole.Admin => "مدير النظام",
        UserRole.Accountant => "محاسب",
        UserRole.Cashier => "كاشير",
        UserRole.RepairTech => "فني صيانة",
        UserRole.Inventory => "مسؤول مخزون",
        _ => "موظف"
    };
}

public class RoleOption(UserRole key, string value)
{
    public UserRole Key { get; } = key;
    public string Value { get; } = value;
}
