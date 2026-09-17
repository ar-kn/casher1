using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PhoneAccounting.App.ViewModels;

namespace PhoneAccounting.App.Views;

public partial class ProductsView : UserControl
{
    public ProductsView()
    {
        InitializeComponent();
        DataContext = new ProductsViewModel();
    }

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ProductsViewModel vm && e.NewValue is FolderNode node)
        {
            vm.SelectedFolder = node;
        }
    }

    private void ProductRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is ButtonBase) return;
        if (sender is FrameworkElement { DataContext: ProductRow row } &&
            DataContext is ProductsViewModel vm)
        {
            vm.OpenCardCommand.Execute(row);
        }
    }
}