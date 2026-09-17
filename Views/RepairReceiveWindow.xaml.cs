using System.Windows;
using PhoneAccounting.App.Infrastructure;
using PhoneAccounting.App.ViewModels;

namespace PhoneAccounting.App.Views;

public partial class RepairReceiveWindow : Window
{
    private readonly RepairReceiveViewModel _viewModel;

    public RepairReceiveWindow()
    {
        InitializeComponent();
        _viewModel = new RepairReceiveViewModel();
        DataContext = _viewModel;
        _viewModel.Saved += (_, _) => Close();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        WindowEffects.Apply(this);
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
