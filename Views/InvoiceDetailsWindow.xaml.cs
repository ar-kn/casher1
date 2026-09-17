using System.Windows;
using PhoneAccounting.App.Infrastructure;
using PhoneAccounting.App.ViewModels;

namespace PhoneAccounting.App.Views;

public partial class InvoiceDetailsWindow : Window
{
    private readonly InvoiceDetailsViewModel _viewModel;

    public InvoiceDetailsWindow(int saleId = 0, int purchaseId = 0)
    {
        InitializeComponent();
        _viewModel = new InvoiceDetailsViewModel(saleId, purchaseId);
        DataContext = _viewModel;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        WindowEffects.Apply(this);
    }

    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel.EditInvoiceCommand.Execute(null);
    }

    private void Void_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel.VoidOrderCommand.Execute(null);
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
