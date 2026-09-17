using System.Windows;
using PhoneAccounting.App.Infrastructure;
using PhoneAccounting.App.ViewModels;

namespace PhoneAccounting.App.Views;

public partial class PurchaseEditWindow : Window
{
    public PurchaseEditWindow(int purchaseId)
    {
        InitializeComponent();
        DataContext = new PurchaseEditViewModel(purchaseId);
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
