using System.Windows.Controls;
using PhoneAccounting.App.Services.Sync;

namespace PhoneAccounting.App.Views;

/// <summary>
/// شاشة مزامنة رقيقة: تعرض وتأمر فقط — DataContext هو SyncStateService مباشرة،
/// وكل منطق TCP/القفل/الكشف داخل الخدمة (مُختبرًة من الهارنس بلا واجهة).
/// دورة الحياة هنا: تفعيل الاستطلاع عند الظهور وإيقافه عند المغادرة.
/// </summary>
public partial class SyncView : UserControl
{
    public SyncView()
    {
        InitializeComponent();
        DataContext = SyncStateService.Instance;

        Loaded += (_, _) =>
        {
            SyncStateService.Instance.RefreshDeviceText();
            SyncStateService.Instance.StartPolling();
        };
        Unloaded += (_, _) => SyncStateService.Instance.StopPolling();
    }
}