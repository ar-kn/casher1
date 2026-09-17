using System.Windows;
using PhoneAccounting.App.Infrastructure;
using PhoneAccounting.App.ViewModels;

namespace PhoneAccounting.App.Views;

public partial class ProductCardWindow : Window
{
    private ProductsViewModel? ViewModel => DataContext as ProductsViewModel;

    public ProductCardWindow()
    {
        InitializeComponent();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        WindowEffects.Apply(this);
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null || !vm.SaveProductCommand.CanExecute(null)) return;
        vm.SaveProductCommand.Execute(null);
        if (vm.IsResultError) return;
        Close();
    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null || !vm.DeleteCurrentCommand.CanExecute(null)) return;
        vm.DeleteCurrentCommand.Execute(null);
        if (vm.IsResultError) return;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        ViewModel?.CancelEdit();
        Close();
    }
}