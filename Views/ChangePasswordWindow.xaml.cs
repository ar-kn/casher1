using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PhoneAccounting.App.Infrastructure;
using PhoneAccounting.App.ViewModels;

namespace PhoneAccounting.App.Views;

public partial class ChangePasswordWindow : Window
{
    private readonly ChangePasswordViewModel _viewModel;

    public bool Success => _viewModel.Success;

    public ChangePasswordWindow(bool isForced = false)
    {
        InitializeComponent();
        _viewModel = new ChangePasswordViewModel(isForced);
        DataContext = _viewModel;
        _viewModel.CloseRequested += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
        Loaded += (_, _) => CurrentBox.Focus();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        WindowEffects.Apply(this);
    }

    // PasswordBox لا يدعم الربط المباشر لأسباب أمنية — المزامنة اليدوية مع الـ ViewModel
    private void Box_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.CurrentPassword = CurrentBox.Password;
        _viewModel.NewPassword = NewPasswordBox.Password;
        _viewModel.ConfirmPassword = ConfirmBox.Password;
    }

    private void Box_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Box_PasswordChanged(sender, new RoutedEventArgs());
        if (_viewModel.SaveCommand.CanExecute(null))
            _viewModel.SaveCommand.Execute(null);
    }
}