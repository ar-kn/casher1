using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;
using System.Collections.ObjectModel;

namespace PhoneAccounting.App.ViewModels;

public partial class ReportDeviceOption
{
    public DeviceRole? Device { get; init; }
    public required string Display { get; init; }
}

public partial class ReportEmployeeOption
{
    public int Id { get; init; }
    public required string Display { get; init; }
}

public partial class SaleReportRow
{
    public required string InvoiceNumber { get; init; }
    public required string DateText { get; init; }
    public required string CustomerName { get; init; }
    public required string CashierName { get; init; }
    public required string DeviceText { get; init; }
    public required string PaymentText { get; init; }
    public int ItemCount { get; init; }
    public decimal Discount { get; init; }
    public decimal Total { get; init; }
    public decimal Profit { get; init; }
}

public partial class PurchaseReportRow
{
    public required string InvoiceNumber { get; init; }
    public required string DateText { get; init; }
    public required string SupplierName { get; init; }
    public required string EmployeeName { get; init; }
    public required string DeviceText { get; init; }
    public int ItemCount { get; init; }
    public decimal Total { get; init; }
}

public partial class TopProductRow
{
    public required string ProductName { get; init; }
    public int Quantity { get; init; }
    public decimal Revenue { get; init; }
    public decimal Profit { get; init; }
}

public partial class DailyProfitRow
{
    public required string DayText { get; init; }
    public decimal Sales { get; init; }
    public decimal Purchases { get; init; }
    public decimal Profit { get; init; }
    public decimal Net { get; init; }
}

public partial class ReportsViewModel : ObservableObject
{
    public ObservableCollection<SaleReportRow> Sales { get; } = [];
    public ObservableCollection<PurchaseReportRow> Purchases { get; } = [];
    public ObservableCollection<TopProductRow> TopProducts { get; } = [];
    public ObservableCollection<DailyProfitRow> DailyProfits { get; } = [];

    public List<ReportDeviceOption> Devices { get; } = [];
    public List<ReportEmployeeOption> Employees { get; } = [];

    [ObservableProperty]
    private DateTime? fromDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    [ObservableProperty]
    private DateTime? toDate = DateTime.Today;

    [ObservableProperty]
    private ReportDeviceOption? selectedDevice;

    [ObservableProperty]
    private ReportEmployeeOption? selectedEmployee;

    [ObservableProperty]
    private string periodText = "";

    // الإحصائيات
    [ObservableProperty]
    private int salesCount;

    [ObservableProperty]
    private decimal salesTotal;

    [ObservableProperty]
    private decimal discountTotal;

    [ObservableProperty]
    private decimal profitTotal;

    [ObservableProperty]
    private int purchasesCount;

    [ObservableProperty]
    private decimal purchasesTotal;

    [ObservableProperty]
    private bool isLoaded;

    public ReportsViewModel()
    {
        Devices.Add(new ReportDeviceOption { Device = null, Display = "كل الأجهزة" });
        Devices.Add(new ReportDeviceOption { Device = DeviceRole.MainServer, Display = "الجهاز الرئيسي" });
        Devices.Add(new ReportDeviceOption { Device = DeviceRole.Accessories, Display = "جهاز الإكسسوارات" });
        Devices.Add(new ReportDeviceOption { Device = DeviceRole.Repair, Display = "جهاز الصيانة" });
        Devices.Add(new ReportDeviceOption { Device = DeviceRole.Cashier, Display = "جهاز الكاشير" });
        SelectedDevice = Devices[0];

        Employees.Add(new ReportEmployeeOption { Id = 0, Display = "كل الموظفين" });
        using (var db = new AppDbContext())
        {
            foreach (var u in db.Users.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.DisplayName))
            {
                Employees.Add(new ReportEmployeeOption { Id = u.Id, Display = u.DisplayName });
            }
        }
        SelectedEmployee = Employees[0];

        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        try
        {
            var from = (FromDate ?? DateTime.Today).Date;
            var to = (ToDate ?? DateTime.Today).Date.AddDays(1);
            var device = SelectedDevice?.Device;
            int? employeeId = SelectedEmployee?.Id > 0 ? SelectedEmployee.Id : null;

            using var db = new AppDbContext();

            var salesQuery = db.Sales
                .AsNoTracking()
                .Include(s => s.User)
                .Include(s => s.Customer)
                .Include(s => s.Items)
                .Where(s => s.Date >= from && s.Date < to)
                .Where(s => s.DeletedAt == null); // الفواتير الملغاة لا تُحسب
            if (device is not null) salesQuery = salesQuery.Where(s => s.Device == device);
            if (employeeId is not null) salesQuery = salesQuery.Where(s => s.UserId == employeeId);
            var sales = salesQuery.OrderByDescending(s => s.Date).ToList();

            var purchasesQuery = db.Purchases
                .AsNoTracking()
                .Include(p => p.User)
                .Include(p => p.Items)
                .Where(p => p.Date >= from && p.Date < to)
                .Where(p => p.DeletedAt == null); // المرتجعات لا تُحسب
            if (device is not null) purchasesQuery = purchasesQuery.Where(p => p.Device == device);
            if (employeeId is not null) purchasesQuery = purchasesQuery.Where(p => p.UserId == employeeId);
            var purchases = purchasesQuery.OrderByDescending(p => p.Date).ToList();

            Sales.Clear();
            foreach (var s in sales)
            {
                Sales.Add(new SaleReportRow
                {
                    InvoiceNumber = s.InvoiceNumber,
                    DateText = s.Date.ToString("dd/MM/yyyy HH:mm",
                        new System.Globalization.CultureInfo("ar-IQ")),
                    CustomerName = s.Customer?.Name ?? "زبون مباشر",
                    CashierName = s.User?.DisplayName ?? "غير محدد",
                    DeviceText = DeviceTextFor(s.Device),
                    PaymentText = PaymentTextFor(s.PaymentMethod),
                    ItemCount = s.Items.Sum(i => i.Quantity),
                    Discount = s.Discount,
                    Total = s.Total,
                    Profit = s.Profit
                });
            }

            Purchases.Clear();
            foreach (var p in purchases)
            {
                Purchases.Add(new PurchaseReportRow
                {
                    InvoiceNumber = p.InvoiceNumber,
                    DateText = p.Date.ToString("dd/MM/yyyy HH:mm",
                        new System.Globalization.CultureInfo("ar-IQ")),
                    SupplierName = p.SupplierName ?? "غير محدد",
                    EmployeeName = p.User?.DisplayName ?? "غير محدد",
                    DeviceText = DeviceTextFor(p.Device),
                    ItemCount = p.Items.Sum(i => i.Quantity),
                    Total = p.Total
                });
            }

            TopProducts.Clear();
            var top = sales
                .SelectMany(s => s.Items)
                .GroupBy(i => i.ProductName)
                .Select(g => new TopProductRow
                {
                    ProductName = g.Key,
                    Quantity = g.Sum(i => i.Quantity),
                    Revenue = g.Sum(i => i.Quantity * i.UnitPrice),
                    Profit = g.Sum(i => i.Quantity * (i.UnitPrice - i.BuyPrice))
                })
                .OrderByDescending(x => x.Quantity)
                .Take(15);
            foreach (var row in top)
                TopProducts.Add(row);

            DailyProfits.Clear();
            var dailySales = sales
                .GroupBy(s => s.Date.Date)
                .ToDictionary(g => g.Key, g => (Total: g.Sum(s => s.Total), Profit: g.Sum(s => s.Profit)));
            var dailyPurchases = purchases
                .GroupBy(p => p.Date.Date)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Total));

            for (var day = from; day < to; day = day.AddDays(1))
            {
                var hasData = dailySales.TryGetValue(day, out var s);
                dailyPurchases.TryGetValue(day, out var p);

                if (!hasData && p == 0) continue;

                DailyProfits.Add(new DailyProfitRow
                {
                    DayText = day.ToString("dddd dd/MM/yyyy",
                        new System.Globalization.CultureInfo("ar-IQ")),
                    Sales = s.Total,
                    Purchases = p,
                    Profit = s.Profit,
                    Net = s.Total - p
                });
            }

            SalesCount = sales.Count;
            SalesTotal = sales.Sum(s => s.Total);
            DiscountTotal = sales.Sum(s => s.Discount);
            ProfitTotal = sales.Sum(s => s.Profit);
            PurchasesCount = purchases.Count;
            PurchasesTotal = purchases.Sum(p => p.Total);

            PeriodText = $"{from:dd/MM/yyyy} — {(to.AddDays(-1)):dd/MM/yyyy}";
            IsLoaded = true;
        }
        catch
        {
            IsLoaded = false;
        }
    }

    public static string DeviceTextFor(DeviceRole device) => device switch
    {
        DeviceRole.MainServer => "الجهاز الرئيسي",
        DeviceRole.Accessories => "جهاز الإكسسوارات",
        DeviceRole.Repair => "جهاز الصيانة",
        DeviceRole.Cashier => "جهاز الكاشير",
        _ => "غير محدد"
    };

    public static string PaymentTextFor(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "نقدي",
        PaymentMethod.Card => "بطاقة",
        PaymentMethod.Credit => "آجل",
        _ => ""
    };
}
