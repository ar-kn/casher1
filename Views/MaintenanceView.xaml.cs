using System.Windows.Controls;
using PhoneAccounting.App.ViewModels;

namespace PhoneAccounting.App.Views;

public partial class MaintenanceView : UserControl
{
    public MaintenanceView()
    {
        InitializeComponent();
        DataContext = new MaintenanceViewModel();
    }
}
