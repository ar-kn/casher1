using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PhoneAccounting.App.Infrastructure;
using PhoneAccounting.App.ViewModels;

namespace PhoneAccounting.App.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow()
    {
        InitializeComponent();
        _viewModel = new LoginViewModel();
        DataContext = _viewModel;
        Loaded += (_, _) => Application.Current.MainWindow = this;
        Loaded += (_, _) => UsernameBox.Focus();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        WindowEffects.Apply(this);
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        _viewModel.Password = PasswordBox.Password;
        if (_viewModel.LoginCommand.CanExecute(null))
            _viewModel.LoginCommand.Execute(null);
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // PasswordBox.Password لا يدعم الربط المباشر (Binding) لأسباب أمنية في WPF،
        // لذا يجب مزامنته يدويًا مع الـ ViewModel عند كل تغيير حتى يعمل زر "دخول"
        // بالماوس تمامًا كما يعمل الضغط على Enter.
        _viewModel.Password = PasswordBox.Password;
    }
}
