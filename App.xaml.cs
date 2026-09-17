using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Services;

namespace PhoneAccounting.App;

public partial class App : Application
{
    [DllImport("user32.dll")]
    private static extern IntPtr SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("shcore.dll")]
    private static extern int GetProcessDpiAwareness(IntPtr hprocess, out int value);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);

    static App()
    {
        try
        {
            var prev = SetProcessDpiAwarenessContext(new IntPtr(-4));
            int v = 0;
            GetProcessDpiAwareness(Process.GetCurrentProcess().Handle, out v);
            var tc = GetThreadDpiAwarenessContext();
            int awareness = GetAwarenessFromDpiAwarenessContext(tc);
            string[] names = { "Unaware", "System", "PerMonitorV1", "PerMonitorV2" };
            File.WriteAllText(
                Path.Combine(Path.GetTempPath(), "pos_dpi.log"),
                $"Set(-4) returned={prev}; ProcessDpiAwareness={v}; ThreadAwareness={awareness} ({names[awareness]}); lastErr={Marshal.GetLastWin32Error()}");
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "pos_dpi.log"), "EX: " + ex);
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

#if DEBUG
        // إثبات تشغيلي للمزامنة (M2.1) — قواعد مؤقتة على القرص فقط، لا يمس قاعدة الإنتاج.
        if (Environment.GetEnvironmentVariable("PHONEACCOUNTING_SYNCTEST") == "1")
        {
            PhoneAccounting.App.Services.Sync.SyncTestHarness.Run();
            return;
        }
#endif

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "PhoneAccounting_errors.log"),
                    $"[AppDomain {DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{args.ExceptionObject}\n\n");
            }
            catch { }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "PhoneAccounting_errors.log"),
                    $"[TaskScheduler {DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{args.Exception}\n\n");
            }
            catch { }
            args.SetObserved();
        };

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                var log = args.Exception.ToString();
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "PhoneAccounting_errors.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{log}\n\n");
            }
            catch { }

            MessageBox.Show(
                $"حدث خطأ غير متوقع:\n{args.Exception.Message}",
                "خطأ",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            Database.Initialize();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"تعذر فتح قاعدة البيانات:\n{ex.Message}",
                "خطأ حرج",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        try
        {
            BackupService.RunAutoIfDue();
        }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "PhoneAccounting_errors.log"),
                    $"[Backup {DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{ex}\n\n");
            }
            catch { }
        }

        try
        {
            PhoneAccounting.App.Services.Sync.SyncScheduler.Log = message =>
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "PhoneAccounting_sync.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            PhoneAccounting.App.Services.Sync.SyncScheduler.Start();

            // فريق P3.0: الخدمة تُوجّه تحديثات التجميعات إلى واجهة WPF وتكتب الأثر لنفس السجل.
            PhoneAccounting.App.Services.Sync.SyncStateService.UiContext =
                new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher);
            PhoneAccounting.App.Services.Sync.SyncStateService.Log = PhoneAccounting.App.Services.Sync.SyncScheduler.Log;
        }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "PhoneAccounting_errors.log"),
                    $"[SyncScheduler {DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{ex}\n\n");
            }
            catch { }
        }

        var loginWindow = new Views.LoginWindow();
        MainWindow = loginWindow;
        loginWindow.Show();
    }
}
