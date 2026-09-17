using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;
using PhoneAccounting.App.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace PhoneAccounting.App.ViewModels;

public partial class SettingsUserRow : ObservableObject
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsActive { get; set; }
    public string Phone { get; set; } = "";
}

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _companyName = Database.GetSetting("CompanyName", "شركة الهواتف");

    [ObservableProperty]
    private string _currency = Database.GetSetting("Currency", "د.ع");

    [ObservableProperty]
    private string _repairDefaultFee = Database.GetSetting("RepairDefaultFee", "25000");

    [ObservableProperty]
    private ObservableCollection<SettingsUserRow> _users = new();

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private string _deviceRole = Database.GetSetting("DeviceRole", "Cashier");

    [ObservableProperty]
    private string _syncSecret = Database.GetSetting("SyncSecret", "");

    [ObservableProperty]
    private string _syncPort = Database.GetSetting("SyncPort", "45678");

    [ObservableProperty]
    private string _syncPeers = Database.GetSetting("SyncPeers", "");

    public string DeviceIdText =>
        "هوية هذا الجهاز: " + Session.DeviceId +
        " — بادئة أرقام فواتيره (مثال): F-" + Session.DeviceId + "-<التاريخ>-<العداد>";

    public bool CanEditPairing => Session.CurrentUser?.Role is UserRole.Admin;

    public string LastCloseDateText => CloseDayService.LastCloseDate == DateTime.MinValue.Date
        ? "لم يُقفَل أي يوم بعد"
        : $"آخر إقفال: {CloseDayService.LastCloseDate:dd-MM-yyyy}";

    public bool CanCloseDay => Session.CurrentUser?.Role is UserRole.Admin or UserRole.Accountant;

    public bool CanAudit => Session.CurrentUser?.Role is UserRole.Admin or UserRole.Accountant;

    [ObservableProperty]
    private string _backupStatus = "";

    public string BackupInfoText
    {
        get
        {
            var files = BackupService.ListBackups();
            if (files.Count == 0) return "لا توجد نسخ احتياطية بعد — أول نسخة تُؤخذ تلقائياً عند الإقلاع التالي أو بالزر أدناه.";
            return $"آخر نسخة: {Path.GetFileName(files[0])} — عدد النسخ المحفوظة: {files.Count} (الحد الأقصى 30)";
        }
    }

    public SettingsViewModel()
    {
        using var db = new AppDbContext();
        foreach (var u in db.Users.OrderBy(x => x.Username))
        {
            Users.Add(new SettingsUserRow
            {
                Id = u.Id,
                Username = u.Username,
                Role = u.Role.ToString(),
                IsActive = u.IsActive,
                Phone = u.DisplayName ?? u.Username
            });
        }
    }

    [RelayCommand]
    private void CloseDay()
    {
        if (!CanCloseDay)
        {
            StatusMessage = "إقفال اليوم متاح فقط للمدير أو المحاسب";
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            "سيتم قفل فواتير اليوم وكل ما قبله نهائياً من التعديل، ولا يمكن التراجع عن الإقفال. متابعة؟",
            "إقفال اليوم",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        StatusMessage = CloseDayService.Execute();
        OnPropertyChanged(nameof(LastCloseDateText));
    }

    [RelayCommand]
    private void OpenOperationLog()
    {
        new Views.AuditLogWindow { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog();
    }

    [RelayCommand]
    private void ChangePassword()
    {
        new Views.ChangePasswordWindow(isForced: false)
        {
            Owner = System.Windows.Application.Current.MainWindow,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner
        }.ShowDialog();
    }

    [RelayCommand]
    private void BackupNow()
    {
        if (Session.CurrentUser?.Role is not (UserRole.Admin or UserRole.Accountant))
        {
            BackupStatus = "النسخ الاحتياطي متاح للمدير والمحاسب فقط";
            return;
        }

        BackupStatus = "";
        BackupService.CreateBackup();
        BackupStatus = BackupInfoText;
    }

    [RelayCommand]
    private void RestoreBackup()
    {
        if (Session.CurrentUser?.Role is not (UserRole.Admin or UserRole.Accountant))
        {
            BackupStatus = "الاستعادة متاحة للمدير والمحاسب فقط";
            return;
        }

        if (!Directory.Exists(BackupService.BackupFolder)) Directory.CreateDirectory(BackupService.BackupFolder);

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "اختر نسخة احتياطية للاستعادة",
            InitialDirectory = BackupService.BackupFolder,
            Filter = "قواعد SQLite|*.db|كل الملفات|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        var confirm = System.Windows.MessageBox.Show(
            "سَتُستبدل قاعدة البيانات الحالية بالنسخة المختارة. تُحفظ نسخة أمان من القاعدة الحالية تلقائياً قبل الاستبدال، ثم يلزم إعادة تشغيل التطبيق. متابعة؟",
            "استعادة نسخة احتياطية",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        BackupStatus = BackupService.Restore(dialog.FileName);
        OnPropertyChanged(nameof(BackupInfoText));
    }

    [RelayCommand]
    private void Save()
    {
        Database.SetSetting("CompanyName", CompanyName.Trim());
        Database.SetSetting("Currency", Currency.Trim());
        if (decimal.TryParse(RepairDefaultFee, out _))
            Database.SetSetting("RepairDefaultFee", RepairDefaultFee.Trim());

        if (CanEditPairing)
        {
            if (!int.TryParse(SyncPort, out var port) || port is < 1 or > 65535)
            {
                StatusMessage = "منفذ المزامنة غير صالح — مدخله رقم بين 1 و 65535 (الافتراضي 45678)";
                return;
            }
            Database.SetSetting("DeviceRole", DeviceRole);
            Database.SetSetting("SyncSecret", SyncSecret.Trim());
            Database.SetSetting("SyncPort", port.ToString());
            Database.SetSetting("SyncPeers", NormalizePeers(SyncPeers));
            OnPropertyChanged(nameof(DeviceIdText));
        }

        var msg = "تم حفظ الإعدادات بنجاح";
        if (CanEditPairing) msg += " — يُفعَّل دور الجهاز بعد إعادة التشغيل";
        StatusMessage = msg;
    }

    public static string NormalizePeers(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        return string.Join(",",
            raw.Split(',', ';', ' ', '\r', '\n', '\t')
               .Select(x => x.Trim())
               .Where(x => x.Length > 0));
    }
}
