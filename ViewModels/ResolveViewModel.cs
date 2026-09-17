using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;
using PhoneAccounting.App.Services;
using PhoneAccounting.App.Services.Sync;

namespace PhoneAccounting.App.ViewModels;

/// <summary>صف خلاف واحد في شاشة الحسم — مُدمَج عبر SyncId (صراع-واحد-لكيان حتى لو اكتشفه جهازان).</summary>
public partial class ResolveConflictRow : ObservableObject
{
    public required string Table { get; init; }
    public required string SyncId { get; init; }
    public required string DisplayName { get; init; }
    public required string LocalValueText { get; init; }
    public required string RemoteValueText { get; init; }
    public string Sources { get; set; } = "";
    public string? LocalSourceJson { get; init; }
    public string? RemoteSourceJson { get; init; }

    /// <summary>أول مُستطلَع أبلغ بالخلاف — يُعاد سؤاله عند الحسم (شرط الطزاجة).</summary>
    public required string FreshHost { get; init; }
    public required int FreshPort { get; init; }

    [ObservableProperty]
    private string? decision;
}

/// <summary>
/// P3.2B — شاشة «الخلافات وقرارها» على MAIN. لا توجد صفوف صراع محلية على MAIN (اكتشافٌ أوراقِيٌّ محض)،
/// فالقائمة تُجمع بـ«استطلاع» قراءة-فقط لخوادم الأقران، والحسم продолжение يمرّ بالعمليات الموقّعة العادية.
/// العرض رقيق: كل منطق الاستطلاع/الدمج/الطزاجة/الحسم في هذه الخدمة.
/// </summary>
public partial class ResolveViewModel : ObservableObject
{
    private readonly IReadOnlyList<(string Host, int Port)> _peers;
    private readonly Func<string> _dbFile;

    public ResolveViewModel(IReadOnlyList<(string Host, int Port)>? peers = null,
        Func<string>? dbFile = null)
    {
        _dbFile = dbFile ?? SyncStateService.DbFileProvider;
        _peers = peers ?? ParsePeers(SyncStateService.PeersProvider());
    }

    public static Func<string> DbFileProvider
    {
        get => SyncStateService.DbFileProvider;
        set => SyncStateService.DbFileProvider = value;
    }

    public ObservableCollection<ResolveConflictRow> Pending { get; } = new();

    [ObservableProperty]
    private ResolveConflictRow? selectedRow;

    [ObservableProperty]
    private string message = "";

    [ObservableProperty]
    private bool isBusy;

    public bool IsMain => Session.Device == DeviceRole.MainServer;

    /// <summary>بوابة العرض للحسم: MAIN + جلسة مستخدم بصلاحية مدير — تُقرأ لحظة الضغط لا لحظة فتح الشاشة (P3.2C).</summary>
    public bool CanResolve => IsMain && Session.CurrentUser?.Role == UserRole.Admin;

    /// <summary>جولة استطلاع قراءة-فقط لكل الأقران، ثم دمج المكتشفات بالـ SyncId في صف واحد.</summary>
    public void Refresh()
    {
        IsBusy = true;
        try
        {
            Pending.Clear();
            var merged = new Dictionary<(string Table, string SyncId), (ResolveConflictRow Row, List<string> Labels)>();
            foreach (var (host, port) in _peers)
            {
                var survey = SyncPeer.QueryOpenConflicts(host, port, _dbFile());
                if (!survey.Accepted) continue;

                foreach (var c in survey.Conflicts)
                {
                    var key = (c.EntityTable, c.ConflictSyncId);
                    var label = string.IsNullOrWhiteSpace(survey.RemoteDeviceId) ? host : survey.RemoteDeviceId!;
                    if (merged.TryGetValue(key, out var existing))
                    {
                        existing.Labels.Add(label);
                    }
                    else
                    {
                        merged[key] = (BuildRow(c, host, port, label), new List<string> { label });
                    }
                }
            }

            foreach (var group in merged.Values)
            {
                group.Row.Sources = string.Join(" و", group.Labels.Distinct());
                Pending.Add(group.Row);
            }

            Message = Pending.Count == 0
                ? "لا خلافات مفتوحة لدى الأقران"
                : $"توجد {Pending.Count} خلافات مفتوحة — الحسم من MAIN فقط";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static ResolveConflictRow BuildRow(SurveyConflictDto c, string host, int port, string label)
    {
        var localRows = DeltaApplier.DescribeRows(c.LocalJson, c.EntityTable);
        var remoteRows = DeltaApplier.DescribeRows(c.RemoteJson, c.EntityTable);
        var name = FirstName(localRows) ?? FirstName(remoteRows) ?? "منتج";
        return new ResolveConflictRow
        {
            Table = c.EntityTable,
            SyncId = c.ConflictSyncId,
            DisplayName = name,
            LocalValueText = Describe(localRows),
            RemoteValueText = Describe(remoteRows),
            Sources = label,
            LocalSourceJson = c.LocalJson,
            RemoteSourceJson = c.RemoteJson,
            FreshHost = host,
            FreshPort = port
        };
    }

    private static string? FirstName(List<string> names) => names.Count > 0 ? names[0] : null;

    private static string Describe(List<string> lines) => lines.Count == 0 ? "—" : string.Join(" ، ", lines);

    /// <summary>حسم الصف المختار — تحقق الطزاجة أولاً (إعادة سؤال المُبلِّغ)، ثم حسم عبر العملية الموقّعة من MAIN.</summary>
    public bool ResolveSelected(ConflictResolutionType type, string? reason, out string error)
    {
        error = "";
        if (!IsMain)
        {
            error = "الحسم حصراً من MAIN فقط";
            return false;
        }
        // البوابة النهائية في الخدمة نفسها (P3.2C) — تُفحص لحظة الضغط وتُدوّن الرفض بسجل محلي؛
        // CanResolve هنا طبقة عرض (لتعتيم/إخفاء الأزرار) لا موقفُ قرارٍ متفرّد.
        var row = SelectedRow;
        if (row is null)
        {
            error = "اختر خلافاً أولاً";
            return false;
        }
        if (IsBusy)
        {
            error = "عملية جارية — انتظر";
            return false;
        }

        IsBusy = true;
        try
        {
            // ===== شرط الطزاجة: إعادة استطلاع المُبلِّغ، يُحسَم فقط إن بقي مفتوحاً بنفس القيمتين =====
            var fresh = SyncPeer.QueryOpenConflicts(row.FreshHost, row.FreshPort, _dbFile());
            var still = fresh.Accepted
                ? fresh.Conflicts.FirstOrDefault(c => c.EntityTable == row.Table && c.ConflictSyncId == row.SyncId)
                : null;
            if (still is null)
            {
                error = "أُغلِق الخلاف لدى المُبلِّغ خلال الاستطلاع — حدِّث الشاشة وأعد المحاولة";
                Refresh();
                return false;
            }
            if (still.LocalJson != row.LocalSourceJson || still.RemoteJson != row.RemoteSourceJson)
            {
                error = "تغيّرت حالة الخلاف لدى المُبلِّغ منذ العرض — حدِّث الشاشة وأعد المحاولة";
                Refresh();
                return false;
            }

            using var db = new AppDbContext(_dbFile());
            if (!SyncConflictService.Resolve(db, row.Table, row.SyncId, type, reason, out error,
                    remoteJsonOverride: row.RemoteSourceJson))
            {
                return false;
            }

            row.Decision = type.ToString();
            Pending.Remove(row);
            SelectedRow = Pending.FirstOrDefault();
            Message = $"حُسم الخلاف ({row.DisplayName}) — {type} — يُوزَّع على الأقران";
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void RefreshAction() => Refresh();

    [RelayCommand]
    private void ResolveKeepLocal() { _ = TryResolve(ConflictResolutionType.KeepLocal); }
    [RelayCommand]
    private void ResolveKeepRemote() { _ = TryResolve(ConflictResolutionType.KeepRemote); }
    [RelayCommand]
    private void ResolveStaysDeleted() { _ = TryResolve(ConflictResolutionType.StaysDeleted); }

    private bool TryResolve(ConflictResolutionType type)
        => ResolveSelected(type, null, out var err) || FailStatus(err);

    private bool FailStatus(string err)
    {
        Message = err;
        return false;
    }

    private static List<(string Host, int Port)> ParsePeers(string csv)
    {
        var list = new List<(string, int)>();
        foreach (var seg in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = seg.Split(':', 2);
            if (parts.Length != 2 || !int.TryParse(parts[1], out var port)) continue;
            list.Add((parts[0], port));
        }
        return list;
    }
}