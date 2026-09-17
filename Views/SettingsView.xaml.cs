using System.Windows;
using System.Windows.Controls;
using PhoneAccounting.App.ViewModels;

namespace PhoneAccounting.App.Views;

public partial class SettingsView : UserControl
{
    private SettingsViewModel _vm = null!;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += SettingsView_Loaded;
    }

    private void SettingsView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            _vm = vm;
            SyncSecretPasswordBox.Password = vm.SyncSecret;
        }
    }

    private void SyncSecretPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.SyncSecret = SyncSecretPasswordBox.Password;
        }
    }
}