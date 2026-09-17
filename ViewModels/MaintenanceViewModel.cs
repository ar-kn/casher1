using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;
using PhoneAccounting.App.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace PhoneAccounting.App.ViewModels;

public class StatusOption(RepairStatus? status, string display)
{
    public RepairStatus? Status { get; } = status;
    public string Display { get; } = display;
}

public class PaymentOption(PaymentMethod method, string display)
{
    public PaymentMethod Method { get; } = method;
    public string Display { get; } = display;
}

public partial class MaintenanceRow : ObservableObject
{
    public int Id { get; init; }
    public required string JobNumber { get; init; }
    public required string ReceivedDateText { get; init; }
    public required string CustomerName { get; init; }
    public required string DeviceName { get; init; }
    public required string Issue { get; init; }
    public RepairStatus Status { get; init; }
    public required string StatusText { get; init; }
    public required Brush StatusBrush { get; init; }
    public decimal LaborFee { get; init; }
    public decimal PartsTotal { get; init; }
    public decimal Total { get; init; }
    public bool IsPaid { get; init; }
    public string PaidText => IsPaid ? "مدفوع" : "غير مدفوع";
    public Brush PaidBrush => IsPaid
        ? (Brush)Application.Current.FindResource("SuccessBrush")
        : (Brush)Application.Current.FindResource("DangerBrush");
}

public partial class MaintenanceViewModel : ObservableObject
{
    public ObservableCollection<MaintenanceRow> Jobs { get; } = [];

    public List<StatusOption> StatusFilters { get; } =
    [
        new(null, "كل الحالات"),
        new(RepairStatus.Received, "مستلم"),
        new(RepairStatus.InProgress, "قيد الإصلاح"),
        new(RepairStatus.Completed, "منتهي"),
        new(RepairStatus.Delivered, "مسلّم"),
    ];

    [ObservableProperty]
    private string searchText = "";

    [ObservableProperty]
    private StatusOption? selectedStatusFilter;

    [ObservableProperty]
    private string defaultFeeText = "";

    [ObservableProperty]
    private string resultMessage = "";

    [ObservableProperty]
    private bool isResultError;

    [ObservableProperty]
    private int receivedCount;

    [ObservableProperty]
    private int inProgressCount;

    [ObservableProperty]
    private int completedCount;

    [ObservableProperty]
    private int deliveredCount;

    [ObservableProperty]
    private decimal pendingAmount;

    public string DateText => DateTime.Now.ToString("dddd، dd MMMM yyyy",
        new System.Globalization.CultureInfo("ar-IQ"));

    public MaintenanceViewModel()
    {
        DefaultFeeText = Database.GetSetting("RepairDefaultFee", "25000");
        SelectedStatusFilter = StatusFilters[0];
        Refresh();
    }

    partial void OnSearchTextChanged(string value) => Refresh();

    partial void OnSelectedStatusFilterChanged(StatusOption? value) => Refresh();

    [RelayCommand]
    private void SaveDefaultFee()
    {
        ResultMessage = "";
        IsResultError = false;

        if (!decimal.TryParse(DefaultFeeText, out var fee) || fee < 0)
        {
            ResultMessage = "أدخل قيمة صحيحة لأجرة الصيانة الافتراضية";
            IsResultError = true;
            return;
        }

        Database.SetSetting("RepairDefaultFee", fee.ToString("0"));
        ResultMessage = $"تم حفظ أجرة الصيانة الافتراضية: {fee:N0} د.ع";
    }

    [RelayCommand]
    private void OpenReceiveJob()
    {
        var window = new Views.RepairReceiveWindow
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
        Refresh();
    }

    [RelayCommand]
    private void OpenJob(MaintenanceRow? row)
    {
        if (row is null) return;
        var window = new Views.RepairJobEditWindow(row.Id)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
        Refresh();
    }

    private void Refresh()
    {
        Jobs.Clear();

        using var db = new AppDbContext();
        var all = db.RepairJobs.AsNoTracking()
            .Include(j => j.Parts)
            .OrderByDescending(j => j.ReceivedDate)
            .ToList();

        ReceivedCount = all.Count(j => j.Status == RepairStatus.Received);
        InProgressCount = all.Count(j => j.Status == RepairStatus.InProgress);
        CompletedCount = all.Count(j => j.Status == RepairStatus.Completed);
        DeliveredCount = all.Count(j => j.Status == RepairStatus.Delivered);
        PendingAmount = all.Where(j => !j.IsPaid).Sum(j => j.Total);

        var term = SearchText.Trim();
        var status = SelectedStatusFilter?.Status;

        foreach (var j in all)
        {
            if (status is not null && j.Status != status) continue;
            if (term.Length > 0 &&
                !j.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !j.DeviceName.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !j.JobNumber.Contains(term, StringComparison.OrdinalIgnoreCase))
                continue;

            Jobs.Add(new MaintenanceRow
            {
                Id = j.Id,
                JobNumber = j.JobNumber,
                ReceivedDateText = j.ReceivedDate.ToString("dd/MM/yyyy HH:mm"),
                CustomerName = j.CustomerName,
                DeviceName = j.DeviceName,
                Issue = j.Issue,
                Status = j.Status,
                StatusText = StatusText(j.Status),
                StatusBrush = StatusBrush(j.Status),
                LaborFee = j.LaborFee,
                PartsTotal = j.PartsTotal,
                Total = j.Total,
                IsPaid = j.IsPaid
            });
        }
    }

    private static string StatusText(RepairStatus status) => status switch
    {
        RepairStatus.Received => "مستلم",
        RepairStatus.InProgress => "قيد الإصلاح",
        RepairStatus.Completed => "منتهي",
        RepairStatus.Delivered => "مسلّم",
        _ => ""
    };

    private static Brush StatusBrush(RepairStatus status)
    {
        var key = status switch
        {
            RepairStatus.Received => "StatusReceivedBrush",
            RepairStatus.InProgress => "StatusInProgressBrush",
            RepairStatus.Completed => "StatusCompletedBrush",
            RepairStatus.Delivered => "StatusDeliveredBrush",
            _ => "TextSecondaryBrush"
        };
        return (Brush)Application.Current.FindResource(key);
    }
}
