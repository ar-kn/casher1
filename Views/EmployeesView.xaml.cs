using System.Windows.Controls;
using PhoneAccounting.App.ViewModels;

namespace PhoneAccounting.App.Views;

public partial class EmployeesView : UserControl
{
    public EmployeesView()
    {
        InitializeComponent();
        DataContext = new EmployeesViewModel();
    }
}