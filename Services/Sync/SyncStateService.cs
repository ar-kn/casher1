using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhoneAccounting.App.Data;

namespace PhoneAccounting.App.Services.Sync;

public enum PeerHealthKind { Ok, Rejected, Unreachable, Invalid }

/// <summary>صف قريب في الشاشة — الحالة تُحدَّث بالاستطلاع (قراءة-فقط) لا بالأحداث.</summary>
public partial class SyncPeerRow : ObservableObject
{
    public string Host { get; init; } = "";
    public int Port { get; init; }
    public string DisplayName => Host + ":" + Port;

    [ObservableProperty]
    private PeerHealthKind statusKind = PeerHealthKind.Unreachable;

    [ObservableProperty]
    private string statusText = "غير مُستطلَع";

    [ObservableProperty]
    private string detail = "";

    [ObservableProperty]
    private string lastSeen = "";

    [ObservableProperty]
    private string remoteDevice = "";
}

/// <summary>إيصال واحد في سجل زمني واحد — فاشل ثم ناجح بنفس الترتيب الزمني (هي القصة).</summary>
public sealed class SyncReceiptRow
{
    public DateTime At { get; init; } = DateTime.Now;
    public string Time => At.ToString("HH:mm:ss");
    public string Peer { get; init; } = "";
    public bool Success { get; init; }
    public int Pulled { get; init; }
    public int Pushed { get; init; }
    public string Message { get; init; } = "";
}

/// <summary>
/// P3.0 — خدمة حالة المزامنة: الشاشة تعرض وتأمر فقط، وكل منطق TCP/القفل هنا فيُختبر من الهارنس مباشرة.
/// - استطلاع دوري للأقران (قراءة-فقط عبر Probe: نظير حي + سر + إصدار) — لا أحداث ولا Observable pipelines.
/// - «زامن الآن» يمر عبر SyncWriteLock حصراً وتُسجَّل فاشل/ناجح في سجل زمني واحد (الإيصالات تشمل الفاشل).
/// - كشف عطل حقيقي: نظير ميت/منفذ مغلق، سر خاطئ، SchemaVersion مختلف — يُكتشَف ويُعرَض ويُسجَّل.</summary>
public partial class SyncStateService : ObservableObject
{
    public static SyncStateService Instance { get; } = new();

    // ===== مزاريب قابلة للتوجيه من الاختبار (الافتراضيات = إنتاج) =====
    public static Func<string> DbFileProvider { get; set; } = () => DataPath.DatabaseFile;
    public static Func<string> PeersProvider { get; set; } = () => Database.GetSetting("SyncPeers", "");
    public static Func<TimeSpan> PollDelayProvider { get; set; } = () =>
    {
        var s = Database.GetSetting("SyncPollSeconds", "5");
        var seconds = int.TryParse(s, out var v) && v >= 2 && v <= 60 ? v : 5;
        return TimeSpan.FromSeconds(seconds);
    };

    /// <summary>سياق توزيع الواجهة لتحديث التجميعات آمنة من خيوط الخلفية؛ null = تحديث مباشر (اختبار بلا نافذة).</summary>
    public static SynchronizationContext? UiContext { get; set; }

    public static Action<string>? Log { get; set; }

    public static TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(3);

    public ObservableCollection<SyncPeerRow> Peers { get; } = new();
    public ObservableCollection<SyncReceiptRow> Receipts { get; } = new();

    [ObservableProperty]
    private string deviceLabel = "";

    [ObservableProperty]
    private string lastSyncText = "لم تُجرَ مزامنة يدوية بعد";

    private CancellationTokenSource? _pollCts;
    private readonly object _pollLock = new();

    // ===================== دورة الحياة (يُفعَّل عند ظهور الشاشة) =====================

    public void StartPolling()
    {
        lock (_pollLock)
        {
            if (_pollCts is not null) return;
            var cts = new CancellationTokenSource();
            _pollCts = cts;
            _ = Task.Run(() => PollLoop(cts.Token));
        }
    }

    public void StopPolling()
    {
        CancellationTokenSource? cts;
        lock (_pollLock) { cts = _pollCts; _pollCts = null; }
        cts?.Cancel();
    }

    private async Task PollLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollDelayProvider(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try { ProbeOnce(); }
            catch (Exception ex) { Log?.Invoke("SyncStateService: فشل الاستطلاع — " + ex.Message); }
        }
    }

    // ===================== الاستطلاع (قراءة-فقط) =====================

    public void ProbeOnce()
    {
        var dbFile = DbFileProvider();
        var peers = ParsePeers(PeersProvider());
        EnsureRows(peers);

        var probes = peers
            .Select(p => Task.Run(() => SyncPeer.Probe(p.Host, p.Port, dbFile, ProbeTimeout)))
            .ToArray();
        var results = Task.WhenAll(probes).GetAwaiter().GetResult();

        for (var i = 0; i < peers.Count; i++) UpdateRow(peers[i], results[i]);
    }

    private void EnsureRows(List<PeerSpec> peers)
    {
        OnUi(() =>
        {
            for (var i = Peers.Count - 1; i >= 0; i--)
            {
                if (!peers.Any(p => p.Host == Peers[i].Host && p.Port == Peers[i].Port))
                    Peers.RemoveAt(i);
            }
            foreach (var p in peers)
            {
                if (!Peers.Any(x => x.Host == p.Host && x.Port == p.Port))
                    Peers.Add(new SyncPeerRow { Host = p.Host, Port = p.Port });
            }
        });
    }

    private void UpdateRow(PeerSpec p, ProbeResult result)
    {
        var row = Peers.FirstOrDefault(x => x.Host == p.Host && x.Port == p.Port);
        if (row is null) return;

        OnUi(() =>
        {
            row.StatusKind = result.Kind switch
            {
                ProbeKind.Ok => PeerHealthKind.Ok,
                ProbeKind.Rejected => PeerHealthKind.Rejected,
                _ => PeerHealthKind.Unreachable
            };
            row.StatusText = result.Kind switch
            {
                ProbeKind.Ok => "متصل",
                ProbeKind.Rejected => "مرفوض",
                _ => "غير متاح"
            };
            row.Detail = result.Kind == ProbeKind.Ok ? "المصافحة سليمة" : (result.Reason ?? "غير متاح");
            row.RemoteDevice = result.RemoteDeviceId ?? "";
            row.LastSeen = DateTime.Now.ToString("HH:mm:ss");
        });
    }

    // ===================== «زامن الآن» — كل كاتب عبر SyncWriteLock + سجل يشمل الفاشل =====================

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        try { RefreshDeviceText(); } catch { }

        var dbFile = DbFileProvider();
        var peers = ParsePeers(PeersProvider());
        if (peers.Count == 0)
        {
            LastSyncText = "لا أقران مضبوطة — أضِفها في الإعدادات (host:port)";
            return;
        }

        EnsurePeerRows(peers);
        LastSyncText = "جارٍ المزامنة...";

        var receipts = new List<SyncReceiptRow>();
        await Task.Run(() => SyncNowCore(peers, dbFile, receipts)).ConfigureAwait(false);
        PublishRound(peers.Count, receipts);
    }

    /// <summary>مسار متزامن للاختبار (بلا أوامر ولا awaits) — نفس الخدمة، نفس الإيصالات، نفس السبك.</summary>
    public void SyncNowBlocking()
    {
        try { RefreshDeviceText(); } catch { }

        var dbFile = DbFileProvider();
        var peers = ParsePeers(PeersProvider());
        if (peers.Count == 0)
        {
            LastSyncText = "لا أقران مضبوطة — أضِفها في الإعدادات (host:port)";
            return;
        }

        EnsurePeerRows(peers);
        LastSyncText = "جارٍ المزامنة...";

        var receipts = new List<SyncReceiptRow>();
        SyncNowCore(peers, dbFile, receipts);
        PublishRound(peers.Count, receipts);
    }

    private static void SyncNowCore(IReadOnlyList<PeerSpec> peers, string dbFile, List<SyncReceiptRow> receipts)
    {
        foreach (var p in peers)
        {
            SyncReceiptRow receipt;
            try
            {
                using (SyncWriteLock.Acquire(TimeSpan.FromSeconds(SyncWriteLock.AcquireTimeoutSeconds), "sync-now"))
                {
                    var res = SyncPeer.Synchronize(p.Host, p.Port, dbFile, maxAttempts: 1);
                    receipt = res.Accepted
                        ? new SyncReceiptRow { Peer = p.Raw, Success = true, Pulled = res.Pulled, Pushed = res.Pushed, Message = $"نجح — سحب {res.Pulled} / دفع {res.Pushed}" }
                        : new SyncReceiptRow { Peer = p.Raw, Success = false, Message = "فشل — " + (res.Reason ?? "رفض غير مفسَّر") };
                }
            }
            catch (TimeoutException)
            {
                receipt = new SyncReceiptRow { Peer = p.Raw, Success = false, Message = "فشل — قفل الكتابة مشغول (عملية أخرى جارية) — جرّب بعد لحظات" };
            }
            catch (Exception ex)
            {
                receipt = new SyncReceiptRow { Peer = p.Raw, Success = false, Message = "فشل — " + ex.Message };
            }

            receipts.Add(receipt);
            Log?.Invoke($"SyncNow [{p.Raw}] — {(receipt.Success ? "OK" : "FAIL")} — {receipt.Message}");
        }
    }

    private void PublishRound(int total, List<SyncReceiptRow> receipts)
    {
        OnUi(() => { foreach (var rc in receipts) Receipts.Add(rc); });
        LastSyncText = $"اكتملت الجولة — نجح {receipts.Count(x => x.Success)} من {total} (التفاصيل في السجل)";
    }

    private void EnsurePeerRows(IReadOnlyList<PeerSpec> peers)
    {
        OnUi(() =>
        {
            foreach (var p in peers)
                if (!Peers.Any(x => x.Host == p.Host && x.Port == p.Port))
                    Peers.Add(new SyncPeerRow { Host = p.Host, Port = p.Port });
        });
    }

    // ===================== عرض هوية هذا الجهاز =====================

    public void RefreshDeviceText()
    {
        try
        {
            using var db = new AppDbContext(DbFileProvider());
            var id = SyncPeer.PeerConfig(db).DeviceId;
            DeviceLabel = "هذا الجهاز: " + id;
        }
        catch
        {
            DeviceLabel = "هذا الجهاز: —";
        }
    }

    // ===================== أدوات =====================

    private sealed record PeerSpec(string Host, int Port, string Raw);

    private static List<PeerSpec> ParsePeers(string csv)
    {
        var list = new List<PeerSpec>();
        foreach (var seg in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = seg.Split(':', 2);
            if (parts.Length != 2 || !int.TryParse(parts[1], out var port)) continue;
            list.Add(new PeerSpec(parts[0], port, seg));
        }
        return list;
    }

    private static void OnUi(Action action)
    {
        var ctx = UiContext;
        if (ctx is null) action();
        else ctx.Post(_ => action(), null);
    }
}