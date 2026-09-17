using System.Windows;
using PhoneAccounting.App.Infrastructure;
using PhoneAccounting.App.ViewModels;

namespace PhoneAccounting.App.Views;

public partial class PosWindow : Window
{
    private readonly PosViewModel _viewModel;

    public PosWindow() : this(0)
    {
    }

    public PosWindow(int editSaleId)
    {
        InitializeComponent();
        _viewModel = new PosViewModel(editSaleId);
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _viewModel.RefreshTotalsForDisplay();
    }

    /// <summary>يفتح تبويب فاتورة البيع مع زبون محدد مسبقاً (يُستدعى من شاشة العملاء).</summary>
    public void PrefillSaleForCustomer(int customerId) => _viewModel.PrefillSaleFor(customerId);

    /// <summary>يفتح تبويب سند القبض مع زبون محدد مسبقاً (يُستدعى من شاشة العملاء).</summary>
    public void PrefillVoucherForCustomer(int customerId) => _viewModel.PrefillVoucherFor(customerId);

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        WindowEffects.Apply(this);
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}
