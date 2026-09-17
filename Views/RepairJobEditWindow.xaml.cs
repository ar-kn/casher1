using System.Windows;
using PhoneAccounting.App.Infrastructure;
using PhoneAccounting.App.ViewModels;

namespace PhoneAccounting.App.Views;

public partial class RepairJobEditWindow : Window
{
    public RepairJobEditWindow(int repairJobId)
    {
        InitializeComponent();
        DataContext = new RepairJobEditViewModel(repairJobId);
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
