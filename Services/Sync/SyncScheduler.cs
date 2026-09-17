using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhoneAccounting.App.Data;

namespace PhoneAccounting.App.Services.Sync;

/// <summary>
/// الجدولة الدورية التلقائية للمزامنة (реб) — بدء خادم الاستقبال + دورة دورية على قائمة الأقران الثابتة:
/// • الفاصل: إعداد SyncIntervalMinutes (افتراضي 5 دقائق).
/// • عند فشل قريب: تراجع أسّي 1s×2ⁿ بسقف 5 دقائق قبل المحاولة التالية؛ يلغي القريب مؤقتاً من cycle.
/// • كل مزامنة تتخذ آخر علامة ماء (المُنجِز الوسيط ينسج مرورَها — لا إعادة إرسال).
/// • لا يمنع استخدام المستخدم؛ يعمل في الخلفية على ذروة المتزامن.
/// </summary>
public static class SyncScheduler
{
    private static CancellationTokenSource? _cts;

    public static Action<string>? Log { get; set; }

    public static void Start()
    {
        if (!ReadBool("AutoSync", true)) return;

        if (!HasUsableSecret())
        {
            Log?.Invoke("SyncScheduler: لا مزامنة — " + SyncPeer.SecretGuardReason + " (لم يُبدأ خادمُ الاستقبال ولا الدورة)");
            return;
        }

        var port = ReadInt("SyncPort", SyncPeer.DefaultPort);
        var dbFile = DataPath.DatabaseFile;

        try
        {
            _ = SyncPeer.StartServer(port, dbFile, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"SyncScheduler: تعذر بدء خادم الاستقبال على {port} — {ex.Message}");
        }

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => SyncLoop(_cts.Token));
        Log?.Invoke("SyncScheduler: بدأت الجدولة الدورية");
    }

    public static void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private static async Task SyncLoop(CancellationToken ct)
    {
        var baseInterval = TimeSpan.FromMinutes(Math.Max(1, ReadInt("SyncIntervalMinutes", 5)));
        var consecutiveFailures = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(baseInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var peers = ReadString("SyncPeers", "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (peers.Length == 0) continue;

            var anyFailed = false;
            foreach (var peer in peers)
            {
                if (ct.IsCancellationRequested) return;

                var parts = peer.Split(':', 2);
                if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
                {
                    Log?.Invoke($"SyncScheduler: قريب غير صالح في الإعدادات — «{peer}» (الصيغة host:port)");
                    continue;
                }

                try
                {
                    var result = SyncPeer.Synchronize(parts[0], port, DataPath.DatabaseFile, maxAttempts: 1);
                    Log?.Invoke($"SyncScheduler: {peer} — pulled={result.Pulled} pushed={result.Pushed} accepted={result.Accepted} {(result.Accepted ? "" : result.Reason ?? "")}");
                    if (result.Pulled > 0 || result.Pushed > 0) consecutiveFailures = 0;
                    if (!result.Accepted) anyFailed = true;
                }
                catch (Exception ex)
                {
                    anyFailed = true;
                    Log?.Invoke($"SyncScheduler: فشل الاتصال بـ {peer} — {ex.Message}");
                }
            }

            if (anyFailed)
            {
                consecutiveFailures++;
                if (consecutiveFailures > 5)
                {
                    // تراجع أسّي: الإيقاع يصبح تدريجياً (1s×2^(n-5)) بسقف 5 دقائق — بدل الدقائق الثابتة.
                    var backoff = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, consecutiveFailures - 5), 300));
                    Log?.Invoke($"SyncScheduler: تراجع أسّي بعد فشل متكرر للقرناء — مهلة {backoff.TotalSeconds:0} ثانية");
                    try { await Task.Delay(backoff, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }
            }
            else
            {
                consecutiveFailures = 0;
            }
        }
    }

    private static bool HasUsableSecret()
    {
        try
        {
            using var db = new AppDbContext();
            return SyncPeer.HasUsableSecret(db);
        }
        catch
        {
            return false;
        }
    }

    private static string ReadString(string key, string fallback)
    {
        try
        {
            using var db = new AppDbContext();
            return Database.GetSetting(key, fallback);
        }
        catch
        {
            return fallback;
        }
    }

    private static bool ReadBool(string key, bool fallback)
    {
        try
        {
            using var db = new AppDbContext();
            var raw = Database.GetSetting(key, fallback ? "true" : "false");
            return !string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase) && raw != "0";
        }
        catch
        {
            return fallback;
        }
    }

    private static int ReadInt(string key, int fallback)
    {
        try
        {
            using var db = new AppDbContext();
            var raw = Database.GetSetting(key, fallback.ToString());
            return int.TryParse(raw, out var v) ? v : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}