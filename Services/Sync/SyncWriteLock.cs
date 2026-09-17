using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneAccounting.App.Services.Sync;

/// <summary>
/// قفل كاتب منطقي واحد (البند 15.6 — M2.5): «تطبيق دلتا لا يتزامن مع حفظ بيع».
/// - قفل عدّادSemaphore يمنع التعامل المتزامن مع القاعدة خارج المعاملة المشتركة.
/// - الدخول المتداخل بنفس الخيط مسموح (إعادة دخول)؛ الدخول من خيط آخر ينتظر المهلة ثم يُسجّل استنزافاً.
/// - الاستنزاف يُسجّل ثم يُرمي TimeoutException (لا انتظار لا نهائي) — يتأخر_busy_timeout على SQLite
///   هو الضامن النهائي لتنقل قفل الملف عبر العمليات.
/// </summary>
public static class SyncWriteLock
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    [ThreadStatic] private static bool _inside;

    public const double AcquireTimeoutSeconds = 5;

    public static IDisposable Acquire(TimeSpan timeout, string owner)
    {
        if (_inside) return new ReentrantScope();

        if (!Gate.Wait(timeout))
        {
            TryLog($"مزامنة: انشغال قفل الكتابة > {timeout.TotalSeconds:0}s ({owner}) — انتظار تحرير");
            throw new TimeoutException($"قفل الكتابة مشغول (بعد {timeout.TotalSeconds:0}s): {owner}");
        }

        _inside = true;
        return new GateScope();
    }

    public static async Task<IDisposable> AcquireAsync(TimeSpan timeout, string owner)
    {
        if (_inside) return new ReentrantScope();

        if (!await Gate.WaitAsync(timeout).ConfigureAwait(false))
        {
            TryLog($"مزامنة: انشغال قفل الكتابة async > {timeout.TotalSeconds:0}s ({owner})");
            throw new TimeoutException($"قفل الكتابة مشغول async (بعد {timeout.TotalSeconds:0}s): {owner}");
        }

        _inside = true;
        return new GateScope();
    }

    private static void TryLog(string message)
    {
        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhoneAccounting", "sync-lock.log");
            System.IO.File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    private sealed class GateScope : IDisposable
    {
        public void Dispose()
        {
            _inside = false;
            Gate.Release();
        }
    }

    private sealed class ReentrantScope : IDisposable
    {
        public void Dispose() { }
    }
}
