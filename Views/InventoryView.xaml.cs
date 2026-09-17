using System.Windows.Controls;
using PhoneAccounting.App.ViewModels;

namespace PhoneAccounting.App.Views;

public partial class InventoryView : UserControl
{
    public InventoryView()
    {
        InitializeComponent();
        DataContext = new InventoryViewModel();
    }
}
