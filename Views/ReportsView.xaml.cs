using System.Windows.Controls;
using PhoneAccounting.App.ViewModels;

namespace PhoneAccounting.App.Views;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
        DataContext = new ReportsViewModel();
    }
}
