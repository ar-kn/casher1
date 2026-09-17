using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;
using PhoneAccounting.App.Services;
using System.Collections.ObjectModel;

namespace PhoneAccounting.App.ViewModels;

public partial class RepairPartRow : ObservableObject
{
    public int ProductId { get; init; }
    public required string ProductName { get; init; }

    [ObservableProperty]
    private int quantity = 1;

    [ObservableProperty]
    private decimal unitPrice;

    public decimal LineTotal
    {
        get => Quantity * UnitPrice;
        set { }
    }

    partial void OnQuantityChanged(int value)
    {
        if (value < 1) Quantity = 1;
        OnPropertyChanged(nameof(LineTotal));
    }

    partial void OnUnitPriceChanged(decimal value)
    {
        if (value < 0) UnitPrice = 0;
        OnPropertyChanged(nameof(LineTotal));
    }
}

public partial class RepairPartSearchRow
{
    public required int ProductId { get; init; }
    public required string Name { get; init; }
    public decimal SellPrice { get; init; }
    public int Stock { get; init; }
}

public partial class RepairJobEditViewModel : ObservableObject
{
    private readonly int _jobId;
    private List<RepairPart> _originalParts = [];

    public ObservableCollection<RepairPartRow> Parts { get; } = [];
    public ObservableCollection<RepairPartSearchRow> SearchResults { get; } = [];

    public List<StatusOption> StatusOptions { get; } =
    [
        new(RepairStatus.Received, "مستلم"),
        new(RepairStatus.InProgress, "قيد الإصلاح"),
        new(RepairStatus.Completed, "منتهي"),
        new(RepairStatus.Delivered, "مسلّم"),
    ];

    public List<PaymentOption> PaymentMethods { get; } =
    [
        new(PaymentMethod.Cash, "نقدي"),
        new(PaymentMethod.Card, "بطاقة"),
        new(PaymentMethod.Credit, "آجل"),
    ];

    [ObservableProperty]
    private string jobNumber = "";

    [ObservableProperty]
    private string receivedDateText = "";

    [ObservableProperty]
    private string customerName = "";

    [ObservableProperty]
    private string customerPhone = "";

    [ObservableProperty]
    private string deviceName = "";

    [ObservableProperty]
    private string issue = "";

    [ObservableProperty]
    private string notes = "";

    [ObservableProperty]
    private string searchText = "";

    [ObservableProperty]
    private string resultMessage = "";

    [ObservableProperty]
    private bool isResultError;

    [ObservableProperty]
    private decimal laborFee;

    [ObservableProperty]
    private StatusOption? status;

    [ObservableProperty]
    private PaymentOption? payment;

    [ObservableProperty]
    private bool isPaid;

    [ObservableProperty]
    private decimal partsTotal;

    [ObservableProperty]
    private decimal total;

    [ObservableProperty]
    private bool isClosed;

    public RepairJobEditViewModel(int jobId)
    {
        _jobId = jobId;
        Load();
    }

    private void Load()
    {
        using var db = new AppDbContext();
        var job = db.RepairJobs
            .Include(j => j.Parts)
            .First(j => j.Id == _jobId);

        _originalParts = job.Parts.ToList();

        JobNumber = job.JobNumber;
        ReceivedDateText = job.ReceivedDate.ToString("dd/MM/yyyy HH:mm",
            new System.Globalization.CultureInfo("ar-IQ"));
        CustomerName = job.CustomerName;
        CustomerPhone = job.CustomerPhone ?? "";
        DeviceName = job.DeviceName;
        Issue = job.Issue;
        Notes = job.Notes ?? "";
        LaborFee = job.LaborFee;
        IsPaid = job.IsPaid;
        IsClosed = job.Status == RepairStatus.Delivered;

        Status = StatusOptions.FirstOrDefault(s => s.Status == job.Status) ?? StatusOptions[0];
        Payment = PaymentMethods.FirstOrDefault(p => p.Method == job.PaymentMethod) ?? PaymentMethods[0];

        foreach (var part in job.Parts.OrderBy(p => p.Id))
        {
            Parts.Add(new RepairPartRow
            {
                ProductId = part.ProductId,
                ProductName = part.ProductName,
                Quantity = part.Quantity,
                UnitPrice = part.UnitPrice
            });
        }

        RefreshTotals();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            SearchResults.Clear();
            return;
        }

        SearchResults.Clear();
        var term = value.Trim();

        using var db = new AppDbContext();
        var alreadyAdded = Parts.Select(p => p.ProductId).ToHashSet();

        var matches = db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.Stock > 0)
            .Where(p => p.Name.Contains(term) ||
                        (p.EnglishName != null && p.EnglishName.Contains(term)) ||
                        (p.Barcode != null && p.Barcode.Contains(term)))
            .OrderBy(p => p.Name)
            .Take(12)
            .ToList();

        foreach (var p in matches.Where(p => !alreadyAdded.Contains(p.Id)))
        {
            SearchResults.Add(new RepairPartSearchRow
            {
                ProductId = p.Id,
                Name = p.Name,
                SellPrice = p.SellPrice,
                Stock = p.Stock
            });
        }
    }

    [RelayCommand]
    private void AddPart(RepairPartSearchRow? row)
    {
        if (row is null) return;

        var existing = Parts.FirstOrDefault(p => p.ProductId == row.ProductId);
        if (existing is not null)
        {
            if (existing.Quantity >= row.Stock)
            {
                ResultMessage = $"المتاح من «{row.Name}» هو {row.Stock} فقط";
                IsResultError = true;
                return;
            }
            existing.Quantity++;
            RefreshTotals();
            return;
        }

        Parts.Add(new RepairPartRow
        {
            ProductId = row.ProductId,
            ProductName = row.Name,
            Quantity = 1,
            UnitPrice = row.SellPrice
        });
        RefreshTotals();
        SearchText = "";
        SearchResults.Clear();
        ResultMessage = "";
        IsResultError = false;
    }

    [RelayCommand]
    private void RemovePart(RepairPartRow? part)
    {
        if (part is null) return;
        Parts.Remove(part);
        RefreshTotals();
    }

    partial void OnLaborFeeChanged(decimal value)
    {
        if (value < 0) LaborFee = 0;
        RefreshTotals();
    }

    private void RefreshTotals()
    {
        PartsTotal = Parts.Sum(p => p.LineTotal);
        Total = LaborFee + PartsTotal;
    }

    [RelayCommand]
    private void Save()
    {
        ResultMessage = "";
        IsResultError = false;

        if (string.IsNullOrWhiteSpace(CustomerName))
        {
            ShowError("أدخل اسم العميل");
            return;
        }

        if (string.IsNullOrWhiteSpace(DeviceName))
        {
            ShowError("أدخل نوع الجهاز");
            return;
        }

        if (string.IsNullOrWhiteSpace(Issue))
        {
            ShowError("أدخل وصف العطل");
            return;
        }

        if (LaborFee < 0)
        {
            ShowError("أدخل أجرة صيانة صحيحة");
            return;
        }

        var targetStatus = Status?.Status ?? RepairStatus.Received;

        try
        {
            using var db = new AppDbContext();

            // تراجع أثر المخزون للقطع الأصلية ثم خصم الجديد
            foreach (var old in _originalParts)
            {
                var product = db.Products.FirstOrDefault(p => p.Id == old.ProductId);
                if (product is not null) product.Stock += old.Quantity;
            }

            var job = db.RepairJobs.Include(j => j.Parts).First(j => j.Id == _jobId);
            db.RepairParts.RemoveRange(job.Parts);

            var partsTotal = 0m;
            foreach (var part in Parts)
            {
                var product = db.Products.FirstOrDefault(p => p.Id == part.ProductId);
                var available = product?.Stock ?? 0;
                if (product is null || available < part.Quantity)
                {
                    ShowError($"الكمية المطلوبة من «{part.ProductName}» غير متوفرة بالمخزون (المتاح {available})");
                    return;
                }

                db.RepairParts.Add(new RepairPart
                {
                    RepairJobId = job.Id,
                    ProductId = part.ProductId,
                    ProductName = part.ProductName,
                    Quantity = part.Quantity,
                    UnitPrice = part.UnitPrice
                });
                partsTotal += part.LineTotal;
                product.Stock -= part.Quantity;
                product.UpdatedAt = DateTime.Now;
            }

            job.CustomerName = CustomerName.Trim();
            job.CustomerPhone = string.IsNullOrWhiteSpace(CustomerPhone) ? null : CustomerPhone.Trim();
            job.DeviceName = DeviceName.Trim();
            job.Issue = Issue.Trim();
            job.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
            job.LaborFee = LaborFee;
            job.PaymentMethod = Payment?.Method ?? Models.PaymentMethod.Cash;
            job.IsPaid = IsPaid;
            job.PartsTotal = partsTotal;
            job.Total = LaborFee + partsTotal;
            job.Status = targetStatus;

            if (targetStatus >= RepairStatus.Completed && job.CompletedDate is null)
                job.CompletedDate = DateTime.Now;
            if (targetStatus == RepairStatus.Delivered)
            {
                if (job.CompletedDate is null) job.CompletedDate = DateTime.Now;
                if (job.DeliveredDate is null) job.DeliveredDate = DateTime.Now;
                IsClosed = true;
            }
            if (targetStatus < RepairStatus.Completed) job.CompletedDate = null;
            if (targetStatus < RepairStatus.Delivered) job.DeliveredDate = null;

            // سجل العمليات: عملية خ-stock إذا أُضيفت قطع، وإلا تحديث حالة فقط
            OperationWriter.Register(
                db,
                Parts.Count > 0 ? Models.OperationType.RepairPartUsage : Models.OperationType.RepairJob,
                job.JobNumber,
                job.Total,
                entityName: "RepairJobs",
                entityId: job.Id);

            db.SaveChanges();

            _originalParts = Parts.Select(p => new RepairPart
            {
                ProductId = p.ProductId,
                ProductName = p.ProductName,
                Quantity = p.Quantity,
                UnitPrice = p.UnitPrice
            }).ToList();

            ResultMessage = $"تم حفظ القطعة {JobNumber} — {StatusText(targetStatus)}";
        }
        catch (Exception ex)
        {
            ShowError($"خطأ في الحفظ: {ex.Message}");
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

    private void ShowError(string message)
    {
        ResultMessage = message;
        IsResultError = true;
    }
}
