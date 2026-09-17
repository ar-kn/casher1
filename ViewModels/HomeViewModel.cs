using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;
using PhoneAccounting.App.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace PhoneAccounting.App.ViewModels;

public partial class TodaySaleRow
{
    public int Id { get; init; }
    public required string InvoiceNumber { get; init; }
    public required string Time { get; init; }
    public required string CustomerName { get; init; }
    public required string CashierName { get; init; }
    public required string PaymentText { get; init; }
    public required string ItemCount { get; init; }
    public decimal Total { get; init; }
}

public partial class LowStockRow
{
    public required string Name { get; init; }
    public int Stock { get; init; }
    public int MinStock { get; init; }
}

/// <summary>نقطة واحدة في الرسم البياني لتحليل المبيعات.</summary>
public partial class ChartPoint
{
    public required string Label { get; init; }
    public decimal Value { get; init; }
}

/// <summary>حصة فئة واحدة من توزيع مبيعات اليوم حسب التصنيف.</summary>
public partial class CategorySlice
{
    public required string Name { get; init; }
    public decimal Amount { get; init; }
    public double Percent { get; init; }
    public required string ColorHex { get; init; }
    /// <summary>بيانات القوس (Path.Data) الخاصة بهذه الحصة داخل حلقة التوزيع.</summary>
    public required string ArcData { get; init; }
}

public enum SearchResultKind
{
    Product,
    Employee,
    Customer,
    Supplier
}

/// <summary>نتيجة واحدة من البحث الموسّع في الصفحة الرئيسية (منتج/موظف/زبون/مورد).</summary>
public partial class GlobalSearchRow
{
    public required SearchResultKind Kind { get; init; }
    public int TargetId { get; init; }
    public required string Title { get; init; }
    /// <summary>سطر المواصفات التفصيلية (متوفر/منخفض/نفد للسلع، الدور للموظف، الهاتف والرصيد للزبون/المورد).</summary>
    public required string Detail { get; init; }
    /// <summary>المسار الكامل في النظام للانتقال إليه عند النقر، مثل «المخزون ← هواتف ← آيفون».</summary>
    public required string PathText { get; init; }
    public required string IconGlyph { get; init; }
}

public partial class HomeViewModel : ObservableObject
{
    private const double ChartVirtualWidth = 900;
    private const double ChartVirtualHeight = 200;
    private const double RingRadius = 38;
    private const double RingCenter = 50;

    [ObservableProperty]
    private string welcomeText = "";

    [ObservableProperty]
    private string dateText = "";

    [ObservableProperty]
    private string deviceTitle = "";

    [ObservableProperty]
    private string companyName = "";

    // إحصائيات اليوم
    [ObservableProperty]
    private int todaySalesCount;

    [ObservableProperty]
    private decimal todaySalesTotal;

    [ObservableProperty]
    private decimal todayProfit;

    [ObservableProperty]
    private int productCount;

    [ObservableProperty]
    private int userCount;

    [ObservableProperty]
    private int lowStockCount;

    // اتجاهات حقيقية (مقارنة فعلية بالأمس، لا أرقام مُختلَقة)
    [ObservableProperty]
    private decimal? salesTrendPercent;

    [ObservableProperty]
    private decimal profitMarginPercent;

    [ObservableProperty]
    private string invoicesPerHourText = "";

    public ObservableCollection<TodaySaleRow> TodaySales { get; } = [];
    public ObservableCollection<LowStockRow> LowStockItems { get; } = [];
    public ObservableCollection<ChartPoint> ChartPoints { get; } = [];
    public ObservableCollection<CategorySlice> CategoryBreakdown { get; } = [];

    [ObservableProperty]
    private CategorySlice? topCategory;

    private PointCollection _linePoints = [];
    public PointCollection LinePoints
    {
        get => _linePoints;
        private set => SetProperty(ref _linePoints, value);
    }

    private PointCollection _areaPoints = [];
    public PointCollection AreaPoints
    {
        get => _areaPoints;
        private set => SetProperty(ref _areaPoints, value);
    }

    [ObservableProperty]
    private decimal? chartTrendPercent;

    [ObservableProperty]
    private string chartTrendLabel = "";

    // ===== البحث الموسّع في الصفحة الرئيسية =====
    public ObservableCollection<GlobalSearchRow> SearchResults { get; } = [];

    [ObservableProperty]
    private string searchText = "";

    [ObservableProperty]
    private bool searchActive;

    [ObservableProperty]
    private bool hasResults;

    [ObservableProperty]
    private string searchStatus = "";

    public event Action<string>? NavigateToProductsRequested;
    public event Action? QuickAddRequested;
    public event Action? NavigateToLowStockRequested;
    public event Action<int>? ViewSaleRequested;
    public event Action<int>? OpenProductRequested;
    public event Action<string>? NavigateToCustomersRequested;
    public event Action<string>? NavigateToEmployeesRequested;
    public event Action<int>? ShowSupplierRequested;

    // ===== الرسم البياني: 0=يومي (اليوم بالساعة) 1=أسبوعي (آخر 7 أيام) 2=شهري (هذا الشهر) 3=سنوي (هذا العام) =====
    [ObservableProperty]
    private int chartPeriodIndex = 1;

    partial void OnChartPeriodIndexChanged(int value) => LoadChart();

    // ===== تصدير التقرير =====
    [ObservableProperty]
    private string exportMessage = "";

    [ObservableProperty]
    private bool exportIsError;

    public HomeViewModel()
    {
        companyName = Session.CompanyName;
        welcomeText = $"أهلاً، {Session.CurrentUser?.DisplayName ?? ""}";
        dateText = DateTime.Now.ToString("dddd، dd MMMM yyyy", new CultureInfo("ar-IQ"));
        deviceTitle = DeviceTitleFor(Session.Device);

        Refresh();
    }

    /// <summary>يُعاد تحميل كل بيانات الشاشة (يُستدعى عند فتحها أو بعد العودة من نقطة البيع).</summary>
    public void Refresh()
    {
        LoadData();
        LoadCategoryBreakdown();
        LoadChart();
    }

    private void LoadData()
    {
        try
        {
            using var db = new AppDbContext();
            var todayStart = DateTime.Today;
            var yesterdayStart = todayStart.AddDays(-1);

            var todaySales = db.Sales
                .Include(s => s.User)
                .Include(s => s.Customer)
                .Include(s => s.Items)
                .Where(s => s.Date >= todayStart)
                .Where(s => s.DeletedAt == null) // تُستثنى الملغاة
                .OrderByDescending(s => s.Date)
                .ToList();

            TodaySalesCount = todaySales.Count;
            TodaySalesTotal = todaySales.Sum(s => s.Total);
            TodayProfit = todaySales.Sum(s => s.Profit);

            TodaySales.Clear();
            foreach (var sale in todaySales)
            {
                TodaySales.Add(new TodaySaleRow
                {
                    Id = sale.Id,
                    InvoiceNumber = sale.InvoiceNumber,
                    Time = sale.Date.ToString("HH:mm"),
                    CustomerName = sale.Customer?.Name ?? "زبون مباشر",
                    CashierName = sale.User?.DisplayName ?? "",
                    PaymentText = PaymentTextFor(sale.PaymentMethod),
                    ItemCount = sale.Items.Sum(i => i.Quantity).ToString(),
                    Total = sale.Total
                });
            }

            // اتجاه المبيعات الحقيقي: اليوم مقابل الأمس (فقط إذا وُجدت مبيعات بالأمس لمقارنة صحيحة)
            var yesterdayTotal = db.Sales
                .Where(s => s.Date >= yesterdayStart && s.Date < todayStart)
                .Where(s => s.DeletedAt == null)
                .Sum(s => (decimal?)s.Total) ?? 0;
            SalesTrendPercent = yesterdayTotal > 0
                ? Math.Round((TodaySalesTotal - yesterdayTotal) / yesterdayTotal * 100, 1)
                : null;

            ProfitMarginPercent = TodaySalesTotal > 0
                ? Math.Round(TodayProfit / TodaySalesTotal * 100, 1)
                : 0;

            var hoursElapsed = Math.Max(1.0, (DateTime.Now - todayStart).TotalHours);
            InvoicesPerHourText = (TodaySalesCount / hoursElapsed).ToString("0.#", CultureInfo.InvariantCulture);

            ProductCount = db.Products.Count();
            UserCount = db.Users.Count();

            var low = db.Products
                .Where(p => p.IsActive && p.Stock <= p.MinStock)
                .OrderBy(p => p.Stock)
                .ToList();

            LowStockCount = low.Count;
            LowStockItems.Clear();
            foreach (var p in low)
            {
                LowStockItems.Add(new LowStockRow
                {
                    Name = p.Name,
                    Stock = p.Stock,
                    MinStock = p.MinStock
                });
            }
        }
        catch
        {
            // تجاهل الأخطاء في شاشة البداية
        }
    }

    private void LoadCategoryBreakdown()
    {
        CategoryBreakdown.Clear();
        try
        {
            using var db = new AppDbContext();
            var todayStart = DateTime.Today;

            var itemsToday = db.SaleItems
                .Include(i => i.Sale)
                .Where(i => i.Sale != null && i.Sale.Date >= todayStart)
                .Select(i => new { i.ProductId, i.Quantity, i.UnitPrice })
                .ToList();

            if (itemsToday.Count == 0) return;

            var products = db.Products.Include(p => p.Category).ToDictionary(p => p.Id);

            var byCategory = itemsToday
                .GroupBy(i => products.TryGetValue(i.ProductId, out var p) ? (p.Category?.Name ?? "غير مصنف") : "غير مصنف")
                .Select(g => new { Name = g.Key, Amount = g.Sum(i => i.Quantity * i.UnitPrice) })
                .OrderByDescending(g => g.Amount)
                .Take(4)
                .ToList();

            var total = byCategory.Sum(c => c.Amount);
            if (total <= 0) return;

            string[] palette = ["#0071E3", "#53E16F", "#5E5E63", "#F59E0B"];
            var cumulative = 0.0;
            for (int i = 0; i < byCategory.Count; i++)
            {
                var c = byCategory[i];
                var percent = (double)(c.Amount / total * 100);
                var arc = BuildRingArcData(cumulative, cumulative + percent);
                cumulative += percent;

                CategoryBreakdown.Add(new CategorySlice
                {
                    Name = c.Name,
                    Amount = c.Amount,
                    Percent = percent,
                    ColorHex = palette[i % palette.Length],
                    ArcData = arc
                });
            }

            TopCategory = CategoryBreakdown.FirstOrDefault();
        }
        catch
        {
            // تجاهل أخطاء التوزيع ولا تكسر الشاشة الرئيسية
        }
    }

    /// <summary>يبني بيانات قوس WPF (Path.Data) لحصة دائرية بين نسبتين مئويتين (0-100)، تبدأ من الأعلى.</summary>
    private static string BuildRingArcData(double startPercent, double endPercent)
    {
        // نُبقي فجوة بسيطة بين الحصص لتمييزها بصرياً، كما في التصميم الأصلي
        var clampedEnd = Math.Min(endPercent, startPercent + Math.Max(0.1, (endPercent - startPercent) * 0.97));

        double ToRad(double percent) => (percent / 100.0 * 360.0 - 90.0) * Math.PI / 180.0;

        var startRad = ToRad(startPercent);
        var endRad = ToRad(clampedEnd);

        var x1 = RingCenter + RingRadius * Math.Cos(startRad);
        var y1 = RingCenter + RingRadius * Math.Sin(startRad);
        var x2 = RingCenter + RingRadius * Math.Cos(endRad);
        var y2 = RingCenter + RingRadius * Math.Sin(endRad);

        var largeArc = (clampedEnd - startPercent) > 50 ? 1 : 0;

        return string.Create(CultureInfo.InvariantCulture,
            $"M {x1:F2},{y1:F2} A {RingRadius},{RingRadius} 0 {largeArc} 1 {x2:F2},{y2:F2}");
    }

    private void LoadChart()
    {
        ChartPoints.Clear();
        try
        {
            using var db = new AppDbContext();
            var now = DateTime.Now;
            var today = DateTime.Today;
            List<(DateTime Start, DateTime End, string Label)> buckets = [];
            List<(DateTime Start, DateTime End)> previousBuckets = [];

            switch (ChartPeriodIndex)
            {
                case 0: // يومي: اليوم موزّعاً على فترات 3 ساعات
                    for (int h = 0; h < 24; h += 3)
                    {
                        var s = today.AddHours(h);
                        buckets.Add((s, s.AddHours(3), s.ToString("HH:00")));
                    }
                    previousBuckets.Add((today.AddDays(-1), today));
                    break;

                case 2: // شهري: هذا الشهر بالأسابيع
                    var monthStart = new DateTime(today.Year, today.Month, 1);
                    var wStart = monthStart;
                    var wIndex = 1;
                    while (wStart <= today)
                    {
                        var wEnd = wStart.AddDays(7) > today.AddDays(1) ? today.AddDays(1) : wStart.AddDays(7);
                        buckets.Add((wStart, wEnd, $"أسبوع {wIndex}"));
                        wStart = wStart.AddDays(7);
                        wIndex++;
                    }
                    var prevMonthStart = monthStart.AddMonths(-1);
                    previousBuckets.Add((prevMonthStart, monthStart));
                    break;

                case 3: // سنوي: هذا العام بالأشهر
                    for (int m = 1; m <= today.Month; m++)
                    {
                        var mStart = new DateTime(today.Year, m, 1);
                        buckets.Add((mStart, mStart.AddMonths(1), mStart.ToString("MMM", new CultureInfo("ar-IQ"))));
                    }
                    previousBuckets.Add((new DateTime(today.Year - 1, 1, 1), new DateTime(today.Year, 1, 1)));
                    break;

                default: // أسبوعي: آخر 7 أيام
                    for (int i = 6; i >= 0; i--)
                    {
                        var d = today.AddDays(-i);
                        buckets.Add((d, d.AddDays(1), d.ToString("ddd", new CultureInfo("ar-IQ"))));
                    }
                    previousBuckets.Add((today.AddDays(-13), today.AddDays(-6)));
                    break;
            }

            if (buckets.Count == 0) return;

            var rangeStart = buckets[0].Start < previousBuckets[0].Start ? buckets[0].Start : previousBuckets[0].Start;
            var salesInRange = db.Sales
                .Where(s => s.Date >= rangeStart && s.Date <= now)
                .Where(s => s.DeletedAt == null)
                .Select(s => new { s.Date, s.Total })
                .ToList();

            var values = buckets
                .Select(b => salesInRange.Where(s => s.Date >= b.Start && s.Date < b.End).Sum(s => s.Total))
                .ToList();

            for (int i = 0; i < buckets.Count; i++)
            {
                ChartPoints.Add(new ChartPoint { Label = buckets[i].Label, Value = values[i] });
            }

            // اتجاه حقيقي: مجموع الفترة الحالية (حتى الآن) مقابل الفترة المكافئة السابقة
            var currentSum = values.Sum();
            var (prevStart, prevEnd) = previousBuckets[0];
            var previousSum = salesInRange.Where(s => s.Date >= prevStart && s.Date < prevEnd).Sum(s => s.Total);

            ChartTrendPercent = previousSum > 0 ? Math.Round((currentSum - previousSum) / previousSum * 100, 1) : null;
            ChartTrendLabel = ChartPeriodIndex switch
            {
                0 => "أعلى من نفس وقت الأمس",
                2 => "أعلى من الشهر الماضي",
                3 => "أعلى من العام الماضي",
                _ => "أعلى من الأسبوع الماضي"
            };

            RecomputeLinePoints();
        }
        catch
        {
            // تجاهل أخطاء الرسم البياني ولا تكسر الشاشة الرئيسية
        }
    }

    private void RecomputeLinePoints()
    {
        if (ChartPoints.Count == 0)
        {
            LinePoints = [];
            AreaPoints = [];
            return;
        }

        var max = ChartPoints.Max(p => p.Value);
        if (max <= 0) max = 1;

        var stepX = ChartPoints.Count > 1 ? ChartVirtualWidth / (ChartPoints.Count - 1) : 0;
        var line = new PointCollection();
        for (int i = 0; i < ChartPoints.Count; i++)
        {
            var x = stepX * i;
            var y = ChartVirtualHeight - (double)(ChartPoints[i].Value / max) * ChartVirtualHeight;
            line.Add(new Point(x, y));
        }
        LinePoints = line;

        var area = new PointCollection(line) { new Point(ChartVirtualWidth, ChartVirtualHeight), new Point(0, ChartVirtualHeight) };
        AreaPoints = area;
    }

    [RelayCommand]
    private void Search()
    {
        var term = SearchText.Trim();

        if (term.Length == 0)
        {
            SearchActive = false;
            SearchResults.Clear();
            HasResults = false;
            SearchStatus = "";
            return;
        }

        SearchActive = true;
        SearchResults.Clear();
        SearchStatus = "";
        HasResults = false;

        try
        {
            using var db = new AppDbContext();

            // منتجات الجهاز الحالي فقط (الكاشير يرى الكل، جهاز الإكسسوارات يرى أصنافه فقط ...إلخ)
            var allowed = AllowedDevices();
            var products = db.Products.AsNoTracking().Include(p => p.Category)
                .Where(p => allowed.Contains(p.Device) &&
                            (p.Name.Contains(term) ||
                             (p.EnglishName != null && p.EnglishName.Contains(term)) ||
                             (p.Barcode != null && p.Barcode.Contains(term)) ||
                             (p.Imei != null && p.Imei.Contains(term))))
                .OrderBy(p => p.Name)
                .Take(8)
                .ToList();

            foreach (var p in products)
            {
                var statusText = p.Stock <= 0 ? "نفد المخزون" : p.Stock <= p.MinStock ? "منخفض المخزون" : "متوفر";
                SearchResults.Add(new GlobalSearchRow
                {
                    Kind = SearchResultKind.Product,
                    TargetId = p.Id,
                    Title = p.Name,
                    Detail = $"{statusText} · {p.Stock} قطعة · بيع {p.SellPrice:N0}",
                    PathText = $"المخزون ← {p.Category?.Name ?? "غير مصنف"}" +
                               (string.IsNullOrWhiteSpace(p.SubType) ? "" : $" ← {p.SubType}"),
                    IconGlyph = "\uE7B8"
                });
            }

            var customers = db.Customers.AsNoTracking()
                .Where(c => c.Name.Contains(term) || (c.Phone != null && c.Phone.Contains(term)))
                .OrderBy(c => c.Name)
                .Take(8)
                .ToList();

            foreach (var c in customers)
            {
                SearchResults.Add(new GlobalSearchRow
                {
                    Kind = SearchResultKind.Customer,
                    TargetId = c.Id,
                    Title = c.Name,
                    Detail = $"الهاتف: {c.Phone ?? "—"} · الرصيد: {c.Balance:N0} د.ع",
                    PathText = "العملاء ← ملف الزبون",
                    IconGlyph = "\uE716"
                });
            }

            var employees = db.Users.AsNoTracking()
                .Where(u => u.DisplayName.Contains(term) || u.Username.Contains(term))
                .OrderBy(u => u.DisplayName)
                .Take(8)
                .ToList();

            foreach (var u in employees)
            {
                SearchResults.Add(new GlobalSearchRow
                {
                    Kind = SearchResultKind.Employee,
                    TargetId = u.Id,
                    Title = u.DisplayName,
                    Detail = RoleTitleFor(u.Role) + (u.IsActive ? "" : " · غير مفعّل"),
                    PathText = "الإدارة ← الموظفون",
                    IconGlyph = "\uE77B"
                });
            }

            var suppliers = db.Suppliers.AsNoTracking()
                .Where(s => s.Name.Contains(term) || (s.Phone != null && s.Phone.Contains(term)))
                .OrderBy(s => s.Name)
                .Take(8)
                .ToList();

            foreach (var s in suppliers)
            {
                SearchResults.Add(new GlobalSearchRow
                {
                    Kind = SearchResultKind.Supplier,
                    TargetId = s.Id,
                    Title = s.Name,
                    Detail = $"الهاتف: {s.Phone ?? "—"} · مستحق له: {s.Balance:N0} د.ع",
                    PathText = "المشتريات ← الموردون",
                    IconGlyph = "\uE8C7"
                });
            }

            HasResults = SearchResults.Count > 0;
            SearchStatus = HasResults ? "" : $"لا توجد نتائج مطابقة لـ «{term}»";
        }
        catch
        {
            SearchStatus = "تعذّر البحث — تحقق من قاعدة البيانات";
        }
    }

    /// <summary>ينتقل إلى المخزون مع تطبيق نص البحث الحالي على الجدول (بديل الزر Enter القديم).</summary>
    [RelayCommand]
    private void BrowseAllInInventory()
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return;
        NavigateToProductsRequested?.Invoke(SearchText.Trim());
        SearchActive = false;
    }

    /// <summary>يفتح الوجهة المناسبة عند النقر على نتيجة من نتائج البحث.</summary>
    [RelayCommand]
    private void OpenResult(GlobalSearchRow? row)
    {
        if (row is null) return;

        switch (row.Kind)
        {
            case SearchResultKind.Product:
                OpenProductRequested?.Invoke(row.TargetId);
                break;
            case SearchResultKind.Customer:
                NavigateToCustomersRequested?.Invoke(SearchText.Trim());
                break;
            case SearchResultKind.Employee:
                NavigateToEmployeesRequested?.Invoke(SearchText.Trim());
                break;
            case SearchResultKind.Supplier:
                ShowSupplierRequested?.Invoke(row.TargetId);
                break;
        }

        SearchActive = false;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchActive = false;
        SearchResults.Clear();
        HasResults = false;
        SearchStatus = "";
        SearchText = "";
    }

    private HashSet<DeviceRole> AllowedDevices()
    {
        var device = Session.Device;
        return device switch
        {
            DeviceRole.Accessories => new HashSet<DeviceRole> { DeviceRole.Accessories },
            DeviceRole.Repair => new HashSet<DeviceRole> { DeviceRole.Repair },
            _ => new HashSet<DeviceRole>
            {
                DeviceRole.Cashier, DeviceRole.Accessories,
                DeviceRole.Repair, DeviceRole.MainServer
            }
        };
    }

    private static string RoleTitleFor(UserRole role) => role switch
    {
        UserRole.Admin => "مدير النظام",
        UserRole.Accountant => "محاسب",
        UserRole.Cashier => "كاشير",
        UserRole.RepairTech => "فني صيانة",
        UserRole.Inventory => "مسؤول مخزون",
        _ => "موظف"
    };

    [RelayCommand]
    private void QuickAdd() => QuickAddRequested?.Invoke();

    [RelayCommand]
    private void ViewAllAlerts() => NavigateToLowStockRequested?.Invoke();

    [RelayCommand]
    private void ViewSale(int saleId) => ViewSaleRequested?.Invoke(saleId);

    [RelayCommand]
    private void ExportReport()
    {
        ExportMessage = "";
        ExportIsError = false;

        if (TodaySales.Count == 0)
        {
            ExportMessage = "لا توجد مبيعات اليوم لتصديرها";
            ExportIsError = true;
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "تصدير مبيعات اليوم",
            Filter = "ملف CSV (*.csv)|*.csv",
            FileName = $"مبيعات_{DateTime.Now:yyyy-MM-dd}.csv"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("رقم الفاتورة,الزبون,الكاشير,الوقت,طريقة الدفع,عدد القطع,المبلغ");
            foreach (var s in TodaySales)
            {
                sb.AppendLine($"{Csv(s.InvoiceNumber)},{Csv(s.CustomerName)},{Csv(s.CashierName)}," +
                              $"{Csv(s.Time)},{Csv(s.PaymentText)},{Csv(s.ItemCount)},{s.Total}");
            }

            File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
            ExportMessage = $"تم تصدير {TodaySales.Count} فاتورة بنجاح";
            ExportIsError = false;
        }
        catch (Exception ex)
        {
            ExportMessage = $"تعذّر التصدير: {ex.Message}";
            ExportIsError = true;
        }
    }

    private static string Csv(string value) =>
        value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    private static string PaymentTextFor(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "نقدي",
        PaymentMethod.Card => "بطاقة",
        PaymentMethod.Credit => "آجل",
        _ => ""
    };

    private static string DeviceTitleFor(DeviceRole device) => device switch
    {
        DeviceRole.MainServer => "الجهاز الرئيسي — المحاسبة",
        DeviceRole.Accessories => "جهاز الإكسسوارات",
        DeviceRole.Repair => "جهاز الصيانة",
        DeviceRole.Cashier => "جهاز الكاشير",
        _ => "جهاز"
    };
}
