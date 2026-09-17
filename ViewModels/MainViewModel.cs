using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhoneAccounting.App.Models;
using PhoneAccounting.App.Services;
using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace PhoneAccounting.App.ViewModels;

public partial class NavigationItem : ObservableObject
{
    public required string Title { get; init; }
    public required string Icon { get; init; }
    public required UserControl Content { get; init; }

    [ObservableProperty]
    private bool isSelected;
}

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string displayName = "";

    [ObservableProperty]
    private string roleTitle = "";

    [ObservableProperty]
    private string companyName = "";

    [ObservableProperty]
    private string deviceTitle = "";

    public ObservableCollection<NavigationItem> Items { get; } = [];

    [ObservableProperty]
    private NavigationItem? selectedItem;

    private NavigationItem? _posItem;

    public MainViewModel()
    {
        displayName = Session.CurrentUser?.DisplayName ?? "";
        roleTitle = RoleTitleFor(Session.CurrentUser?.Role ?? UserRole.Admin);
        companyName = Session.CompanyName;
        deviceTitle = DeviceTitleFor(Session.Device);

        Items.Add(new NavigationItem
        {
            Title = "لوحة التحكم",
            Icon = "\uE80F",
            Content = new Views.HomeView()
        });
        _posItem = new NavigationItem
        {
            Title = "نقطة البيع",
            Icon = "\uE7C1",
            Content = new Views.PlaceholderView("نقطة البيع", "تفتح نافذة البيع عند الاختيار")
        };
        Items.Add(_posItem);
        Items.Add(new NavigationItem
        {
            Title = "المخزون",
            Icon = "\uE7B8",
            Content = new Views.ProductsView()
        });
        Items.Add(new NavigationItem
        {
            Title = "العملاء",
            Icon = "\uE716",
            Content = new Views.CustomersView()
        });

        if (Session.CurrentUser?.Role == UserRole.Admin)
        {
            Items.Add(new NavigationItem
            {
                Title = "الموظفون",
                Icon = "\uE8F1",
                Content = new Views.EmployeesView()
            });
        }
        Items.Add(new NavigationItem
        {
            Title = "الصيانة",
            Icon = "\uE77B",
            Content = new Views.MaintenanceView()
        });
        Items.Add(new NavigationItem
        {
            Title = "المزامنة",
            Icon = "\uE895",
            Content = new Views.SyncView()
        });

        if (Session.Device == DeviceRole.MainServer)
        {
            Items.Add(new NavigationItem
            {
                Title = "الخلافات وقرارها",
                Icon = "\uE7BA",
                Content = new Views.ResolveView()
            });
        }
        Items.Add(new NavigationItem
        {
            Title = "التقارير",
            Icon = "\uE9D5",
            Content = new Views.ReportsView()
        });
        Items.Add(new NavigationItem
        {
            Title = "الإعدادات",
            Icon = "\uE713",
            Content = new Views.SettingsView()
        });

        SelectedItem = Items[0];
    }

    partial void OnSelectedItemChanged(NavigationItem? value)
    {
        foreach (var item in Items)
            item.IsSelected = ReferenceEquals(item, value);

        if (value is not null && ReferenceEquals(value, _posItem))
        {
            // إعادة التحديد إلى الرئيسية ثم فتح نافذة البيع
            SelectedItem = Items[0];

            var window = new Views.PosWindow
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowDialog();
        }
    }

    [RelayCommand]
    private void Logout()
    {
        Session.CurrentUser = null;

        var login = new Views.LoginWindow();
        System.Windows.Application.Current.MainWindow?.Close();
        login.Show();
    }

    private static string RoleTitleFor(UserRole role) => role switch
    {
        UserRole.Admin => "مدير النظام",
        UserRole.Accountant => "محاسب",
        UserRole.Cashier => "كاشير",
        UserRole.RepairTech => "فني صيانة",
        UserRole.Inventory => "مسؤول مخزون",
        _ => "موظف"
    };

    private static string DeviceTitleFor(DeviceRole device) => device switch
    {
        DeviceRole.MainServer => "الجهاز الرئيسي — المحاسبة",
        DeviceRole.Accessories => "جهاز الإكسسوارات",
        DeviceRole.Repair => "جهاز الصيانة",
        DeviceRole.Cashier => "جهاز الكاشير",
        _ => "جهاز"
    };
}
