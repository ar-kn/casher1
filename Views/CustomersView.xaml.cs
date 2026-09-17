using System.Windows.Controls;
using PhoneAccounting.App.ViewModels;

namespace PhoneAccounting.App.Views;

public partial class CustomersView : UserControl
{
    private readonly CustomersViewModel _viewModel;

    public CustomersView()
    {
        InitializeComponent();
        _viewModel = new CustomersViewModel();
        DataContext = _viewModel;

        _viewModel.OpenSaleRequested += OnOpenSaleRequested;
        _viewModel.OpenVoucherRequested += OnOpenVoucherRequested;

        // تُعاد تعبئة القائمة في كل مرة تصبح فيها هذه الشاشة هي المحتوى الظاهر
        // (مثلاً بعد إتمام بيع آجل أو سند قبض من نقطة البيع).
        Loaded += (_, _) => _viewModel.Load();
    }

    private void OnOpenSaleRequested(int customerId)
    {
        var window = new PosWindow
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.PrefillSaleForCustomer(customerId);
        window.ShowDialog();
        _viewModel.Load();
    }

    private void OnOpenVoucherRequested(int customerId)
    {
        var window = new PosWindow
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.PrefillVoucherForCustomer(customerId);
        window.ShowDialog();
        _viewModel.Load();
    }
}
