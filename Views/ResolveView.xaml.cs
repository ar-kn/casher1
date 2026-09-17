using System.Windows.Controls;
using PhoneAccounting.App.ViewModels;

namespace PhoneAccounting.App.Views;

/// <summary>
/// شاشة «الخلافات وقرارها» — رقيقة: DataContext هو ResolveViewModel مباشرة،
/// وكل منطق الاستطلاع/الدمج/الطزاجة/الحسم في الخدمة (مُختبرة من الهارنس بلا واجهة).
/// تُحدَّث القائمة عند الظهور فقط، والزر «حدّث» يعيد الاستطلاع.
/// </summary>
public partial class ResolveView : UserControl
{
    public ResolveView()
    {
        InitializeComponent();
        DataContext = new ResolveViewModel();

        Loaded += (_, _) =>
        {
            if (DataContext is ResolveViewModel vm)
                vm.Refresh();
        };
    }
}