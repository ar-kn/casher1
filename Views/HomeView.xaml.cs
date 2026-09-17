using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using PhoneAccounting.App.ViewModels;

namespace PhoneAccounting.App.Views;

public partial class HomeView : UserControl
{
    private readonly HomeViewModel _viewModel;

    public HomeView()
    {
        InitializeComponent();
        _viewModel = new HomeViewModel();
        DataContext = _viewModel;

        _viewModel.NavigateToProductsRequested += OnNavigateToProductsRequested;
        _viewModel.NavigateToLowStockRequested += OnNavigateToLowStockRequested;
        _viewModel.QuickAddRequested += OnQuickAddRequested;
        _viewModel.ViewSaleRequested += OnViewSaleRequested;
        _viewModel.OpenProductRequested += OnOpenProductRequested;
        _viewModel.NavigateToCustomersRequested += OnNavigateToCustomersRequested;
        _viewModel.NavigateToEmployeesRequested += OnNavigateToEmployeesRequested;
        _viewModel.ShowSupplierRequested += OnShowSupplierRequested;

        // تُعاد تعبئة بيانات لوحة التحكم في كل مرة تصبح فيها هذه الشاشة هي المحتوى الظاهر
        // (مثلاً بعد إتمام بيع عبر "نقطة البيع" من الشريط الجانبي مباشرة)، لا فقط بعد زر "إضافة سريع".
        Loaded += (_, _) => _viewModel.Refresh();
    }

    private static MainViewModel? MainVm =>
        System.Windows.Application.Current.MainWindow is MainWindow main
            ? main.DataContext as MainViewModel
            : null;

    private void OnNavigateToProductsRequested(string searchTerm)
    {
        var mainVm = MainVm;
        var productsItem = mainVm?.Items.FirstOrDefault(i => i.Title == "المخزون");
        if (productsItem?.Content is ProductsView { DataContext: ProductsViewModel productsVm })
            productsVm.SearchText = searchTerm;

        if (productsItem is not null && mainVm is not null)
            mainVm.SelectedItem = productsItem;
    }

    private void OnNavigateToLowStockRequested()
    {
        var mainVm = MainVm;
        var productsItem = mainVm?.Items.FirstOrDefault(i => i.Title == "المخزون");
        if (productsItem is not null && mainVm is not null)
            mainVm.SelectedItem = productsItem;
    }

    private void OnQuickAddRequested()
    {
        var window = new PosWindow
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
        _viewModel.Refresh();
    }

    private void OnViewSaleRequested(int saleId)
    {
        var window = new InvoiceDetailsWindow(saleId: saleId)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
        _viewModel.Refresh();
    }

    private void OnOpenProductRequested(int productId)
    {
        var mainVm = MainVm;
        var productsItem = mainVm?.Items.FirstOrDefault(i => i.Title == "المخزون");
        if (productsItem is null || mainVm is null) return;

        mainVm.SelectedItem = productsItem;
        if (productsItem.Content is ProductsView { DataContext: ProductsViewModel productsVm })
            productsVm.OpenById(productId);
    }

    private void OnNavigateToCustomersRequested(string searchTerm)
    {
        var mainVm = MainVm;
        var customersItem = mainVm?.Items.FirstOrDefault(i => i.Title == "العملاء");
        if (customersItem is null || mainVm is null) return;

        mainVm.SelectedItem = customersItem;
        if (!string.IsNullOrWhiteSpace(searchTerm) &&
            customersItem.Content is CustomersView { DataContext: CustomersViewModel customersVm })
        {
            customersVm.SearchText = searchTerm;
        }
    }

    private void OnNavigateToEmployeesRequested(string searchTerm)
    {
        var mainVm = MainVm;
        var employeesItem = mainVm?.Items.FirstOrDefault(i => i.Title == "الموظفون");
        if (employeesItem is not null && mainVm is not null)
            mainVm.SelectedItem = employeesItem;
    }

    private void OnShowSupplierRequested(int supplierId)
    {
        var window = new SupplierDetailsWindow(supplierId)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (_viewModel.SearchCommand.CanExecute(null))
            _viewModel.SearchCommand.Execute(null);
    }
}
