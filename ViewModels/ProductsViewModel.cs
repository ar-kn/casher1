using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Infrastructure;
using PhoneAccounting.App.Models;
using PhoneAccounting.App.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace PhoneAccounting.App.ViewModels;

public partial class ProductRow : ObservableObject
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public required string CategoryName { get; init; }
    public string? SubType { get; init; }
    public string? Barcode { get; init; }
    public string? Imei { get; init; }
    public decimal BuyPrice { get; init; }
    public decimal SellPrice { get; init; }
    public int Stock { get; init; }
    public int MinStock { get; init; }
    public required string DeviceName { get; init; }
    public required string DeviceIconGlyph { get; init; }

    [ObservableProperty]
    private bool isActive;

    public bool IsLow { get => Stock > 0 && Stock <= MinStock; set { } }
    public bool IsOutOfStock { get => Stock <= 0; set { } }

    public double MarginPercent { get => BuyPrice > 0 ? (double)((SellPrice - BuyPrice) / BuyPrice * 100) : 0; set { } }

    /// <summary>نسبة تعبئة شريط مستوى المخزون (0-100) بالمقارنة مع ثلاثة أضعاف الحد الأدنى كمرجع "مخزون صحي".</summary>
    public double StockBarPercent
    {
        get
        {
            var healthy = Math.Max(1, MinStock * 3);
            return Math.Min(100, Math.Max(0, (double)Stock / healthy * 100));
        }
        set { }
    }

    public string StatusText { get => IsOutOfStock ? "نفد" : IsLow ? "منخفض" : "متوفر"; set { } }
}

/// <summary>عقدة في شجرة مجلدات المخزون (براند ← نوع).</summary>
public partial class FolderNode : ObservableObject
{
    public required string Name { get; init; }
    public required string Icon { get; init; }
    public int? CategoryId { get; init; }
    public string? SubType { get; init; }
    public int Count { get; init; }
    public ObservableCollection<FolderNode> Children { get; } = [];

    [ObservableProperty]
    private bool isSelected;
}

public class CategoryOption(int id, string display)
{
    public int Id { get; } = id;
    public string Display { get; } = display;
}

public class DeviceOption(DeviceRole role, string display)
{
    public DeviceRole Role { get; } = role;
    public string Display { get; } = display;
}

public partial class ProductsViewModel : ObservableObject
{
    public ObservableCollection<ProductRow> Products { get; } = [];
    public ObservableCollection<FolderNode> Folders { get; } = [];

    public List<CategoryOption> FormCategories { get; } = [];
    public List<string> SubTypes { get; } = [.. SubTypeHelper.Options];
    public List<DeviceOption> Devices { get; } =
    [
        new(DeviceRole.Cashier, "جهاز الكاشير"),
        new(DeviceRole.Accessories, "جهاز الإكسسوارات"),
        new(DeviceRole.Repair, "جهاز الصيانة"),
        new(DeviceRole.MainServer, "الجهاز الرئيسي"),
    ];

    [ObservableProperty]
    private string searchText = "";

    [ObservableProperty]
    private FolderNode? selectedFolder;

    [ObservableProperty]
    private int overallProductCount;

    [ObservableProperty]
    private int lowStockCount;

    [ObservableProperty]
    private int outOfStockCount;

    [ObservableProperty]
    private int availableCount;

    public string AvailablePercentText
    {
        get
        {
            if (OverallProductCount <= 0) return "";
            var percent = (double)AvailableCount / OverallProductCount * 100;
            return $"{percent:0}% من إجمالي المخزون";
        }
    }

    [ObservableProperty]
    private decimal totalStockValue;

    // ===== الحالة (فلتر) =====
    public List<string> StatusOptions { get; } = ["الكل", "متوفر", "منخفض", "نفد"];

    [ObservableProperty]
    private string selectedStatus = "الكل";

    // ===== ترقيم الصفحات =====
    public const int PageSize = 15;

    [ObservableProperty]
    private int currentPage = 1;

    [ObservableProperty]
    private int totalPages = 1;

    [ObservableProperty]
    private int totalFilteredCount;

    [ObservableProperty]
    private string exportMessage = "";

    [ObservableProperty]
    private bool exportIsError;

    [ObservableProperty]
    private string resultMessage = "";

    [ObservableProperty]
    private bool isResultError;

    // ===== نموذج الإضافة/التعديل (بطاقة المنتج المنبثقة) =====
    [ObservableProperty]
    private bool isEditing;

    [ObservableProperty]
    private string formTitle = "إضافة منتج جديد";

    [ObservableProperty]
    private int editingId;

    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private string englishName = "";

    [ObservableProperty]
    private CategoryOption? formCategory;

    [ObservableProperty]
    private string formSubType = "أخرى";

    [ObservableProperty]
    private string barcode = "";

    [ObservableProperty]
    private string imei = "";

    [ObservableProperty]
    private decimal buyPrice;

    [ObservableProperty]
    private decimal sellPrice;

    [ObservableProperty]
    private int stock;

    [ObservableProperty]
    private int minStock = 5;

    [ObservableProperty]
    private bool formIsUsed;

    [ObservableProperty]
    private bool formIsActive = true;

    [ObservableProperty]
    private DeviceOption selectedDevice;

    public bool CanManage => Session.CurrentUser?.Role is UserRole.Admin or UserRole.Inventory or UserRole.Cashier;

    public ProductsViewModel()
    {
        selectedDevice = Devices[0];
        LoadCategories();
        ReloadFolders();
        Load();
    }

    private void LoadCategories()
    {
        FormCategories.Clear();

        using var db = new AppDbContext();
        var all = db.Categories.AsNoTracking().OrderBy(c => c.Name).ToList();
        var byId = all.ToDictionary(c => c.Id);

        foreach (var category in all)
        {
            var depth = 0;
            var parent = category.ParentId;
            while (parent is not null && byId.TryGetValue(parent.Value, out var p))
            {
                depth++;
                parent = p.ParentId;
            }

            var prefix = depth == 0 ? "" : new string('\u2003', depth) + "\u2514 ";
            FormCategories.Add(new CategoryOption(category.Id, $"{prefix}{category.Name}"));
        }
    }

    /// <summary>يبني شجرة المجلدات: مجلد لكل فئة (براند) وداخله مجلدات لكل نوع قطعة (SubType).</summary>
    public void ReloadFolders()
    {
        var selectedPath = SelectedFolder is { } sf ? (sf.CategoryId, sf.SubType) : (null, null);

        Folders.Clear();

        var deviceFilter = AllowedDevices();
        using var db = new AppDbContext();
        var products = db.Products.AsNoTracking()
            .Where(p => deviceFilter.Contains(p.Device))
            .ToList();

        var total = products.Count;
        Folders.Add(new FolderNode
        {
            Name = "جميع المنتجات",
            Icon = "\uE8B9",
            Count = total
        });

        var byCategory = products.GroupBy(p => p.CategoryId).ToDictionary(g => g.Key, g => g.ToList());
        var categories = db.Categories.AsNoTracking().OrderBy(c => c.Name).ToList();

        foreach (var cat in categories)
        {
            if (!byCategory.TryGetValue(cat.Id, out var items)) continue;

            var brand = new FolderNode
            {
                Name = cat.Name,
                Icon = "\uE8B7",
                CategoryId = cat.Id,
                Count = items.Count
            };

            foreach (var group in items
                         .GroupBy(p => string.IsNullOrWhiteSpace(p.SubType) ? "أخرى" : p.SubType)
                         .OrderBy(g => g.Key))
            {
                brand.Children.Add(new FolderNode
                {
                    Name = group.Key,
                    Icon = "\uE8EC",
                    CategoryId = cat.Id,
                    SubType = group.Key,
                    Count = group.Count()
                });
            }

            Folders.Add(brand);
            if (selectedPath is ({ } catId, var subType) && catId == cat.Id)
            {
                var target = subType is null
                    ? Folders.First(f => f is { CategoryId: not null } x && x.CategoryId == catId && x.SubType is null)
                    : Folders.First(f => f is { CategoryId: not null } x && x.CategoryId == catId && x.SubType == subType);
                SelectedFolder = target;
            }
        }
    }

    private void Load()
    {
        Products.Clear();

        var deviceFilter = AllowedDevices();

        using var db = new AppDbContext();
        var activeAll = db.Products.AsNoTracking().Where(p => deviceFilter.Contains(p.Device) && p.IsActive).ToList();
        OverallProductCount = activeAll.Count;
        LowStockCount = activeAll.Count(p => p.Stock > 0 && p.Stock <= p.MinStock);
        OutOfStockCount = activeAll.Count(p => p.Stock <= 0);
        AvailableCount = activeAll.Count(p => p.Stock > p.MinStock);
        OnPropertyChanged(nameof(AvailablePercentText));

        var query = db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => deviceFilter.Contains(p.Device));

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(p =>
                p.Name.Contains(term) ||
                (p.EnglishName != null && p.EnglishName.Contains(term)) ||
                (p.Barcode != null && p.Barcode.Contains(term)) ||
                (p.Imei != null && p.Imei.Contains(term)));
        }

        if (SelectedFolder is { CategoryId: not null } folder)
        {
            var ids = CategoryWithChildren(folder.CategoryId.Value);
            query = query.Where(p => ids.Contains(p.CategoryId));

            if (folder.SubType is { } subType)
                query = query.Where(p => (p.SubType ?? "أخرى") == subType);
        }

        if (SelectedStatus == "متوفر")
        {
            query = query.Where(p => p.Stock > p.MinStock);
        }
        else if (SelectedStatus == "منخفض")
        {
            query = query.Where(p => p.Stock > 0 && p.Stock <= p.MinStock);
        }
        else if (SelectedStatus == "نفد")
        {
            query = query.Where(p => p.Stock <= 0);
        }

        var allMatching = query.OrderBy(p => p.Name).ToList();

        TotalFilteredCount = allMatching.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalFilteredCount / (double)PageSize));
        if (CurrentPage > TotalPages) CurrentPage = TotalPages;
        if (CurrentPage < 1) CurrentPage = 1;

        var pageItems = allMatching.Skip((CurrentPage - 1) * PageSize).Take(PageSize).ToList();

        foreach (var p in pageItems)
        {
            Products.Add(new ProductRow
            {
                Id = p.Id,
                Name = p.Name,
                CategoryName = p.Category?.Name ?? "",
                SubType = p.SubType,
                Barcode = p.Barcode,
                Imei = p.Imei,
                BuyPrice = p.BuyPrice,
                SellPrice = p.SellPrice,
                Stock = p.Stock,
                MinStock = p.MinStock,
                DeviceName = DeviceTitleFor(p.Device),
                DeviceIconGlyph = DeviceIconFor(p.Device),
                IsActive = p.IsActive
            });
        }

        TotalStockValue = allMatching.Sum(p => p.BuyPrice * p.Stock);
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPage >= TotalPages) return;
        CurrentPage++;
        Load();
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPage <= 1) return;
        CurrentPage--;
        Load();
    }

    [RelayCommand]
    private void ExportProducts()
    {
        ExportMessage = "";
        ExportIsError = false;

        var deviceFilter = AllowedDevices();
        using var db = new AppDbContext();
        var all = db.Products.AsNoTracking().Include(p => p.Category)
            .Where(p => deviceFilter.Contains(p.Device))
            .OrderBy(p => p.Name)
            .ToList();

        if (all.Count == 0)
        {
            ExportMessage = "لا توجد أصناف لتصديرها";
            ExportIsError = true;
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "تصدير المخزون",
            Filter = "ملف CSV (*.csv)|*.csv",
            FileName = $"المخزون_{DateTime.Now:yyyy-MM-dd}.csv"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("الاسم,النوع,الباركود,التصنيف,الجهاز,شراء,بيع,المخزون,الحد الأدنى");
            foreach (var p in all)
            {
                sb.AppendLine($"{CsvEscape(p.Name)},{CsvEscape(p.SubType ?? "")},{CsvEscape(p.Barcode ?? "")}," +
                              $"{CsvEscape(p.Category?.Name ?? "")},{CsvEscape(DeviceTitleFor(p.Device))}," +
                              $"{p.BuyPrice},{p.SellPrice},{p.Stock},{p.MinStock}");
            }
            System.IO.File.WriteAllText(dialog.FileName, sb.ToString(), new System.Text.UTF8Encoding(true));
            ExportMessage = $"تم تصدير {all.Count} صنف بنجاح";
        }
        catch (Exception ex)
        {
            ExportMessage = $"تعذّر التصدير: {ex.Message}";
            ExportIsError = true;
        }
    }

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    private HashSet<int> CategoryWithChildren(int id)
    {
        using var db = new AppDbContext();
        var all = db.Categories.AsNoTracking().ToList();
        var result = new HashSet<int> { id };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var c in all)
            {
                if (c.ParentId is not null && result.Contains(c.ParentId.Value) && result.Add(c.Id))
                    changed = true;
            }
        }
        return result;
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

    partial void OnSearchTextChanged(string value) { CurrentPage = 1; Load(); }
    partial void OnSelectedFolderChanged(FolderNode? value) { CurrentPage = 1; Load(); }
    partial void OnSelectedStatusChanged(string value) { CurrentPage = 1; Load(); }

    /// <summary>يفتح بطاقة منتج منبثقة لعرض وتعديل منتج محدد (يُستدعى من البحث الموسّع بالصفحة الرئيسية).</summary>
    public void OpenById(int id)
    {
        LoadIntoForm(id);
        ShowCardWindow();
    }

    /// <summary>يفتح بطاقة منتج منبثقة للعرض والتعديل.</summary>
    [RelayCommand]
    private void OpenCard(ProductRow? row)
    {
        if (row is null) return;
        LoadIntoForm(row.Id);
        ShowCardWindow();
    }

    /// <summary>يفتح بطاقة إضافة منتج جديد.</summary>
    [RelayCommand]
    private void StartAdd()
    {
        if (!CanManage) return;

        IsEditing = true;
        FormTitle = "إضافة صنف جديد";
        EditingId = 0;
        Name = "";
        EnglishName = "";
        Barcode = "";
        Imei = "";
        BuyPrice = 0;
        SellPrice = 0;
        Stock = 0;
        MinStock = 3;
        FormCategory = FormCategories.FirstOrDefault();
        FormSubType = SubTypes.FirstOrDefault() ?? "أخرى";
        FormIsUsed = false;
        FormIsActive = true;
        SelectedDevice = Devices[0];
        ResultMessage = "";
        IsResultError = false;

        ShowCardWindow();
    }

    private void ShowCardWindow()
    {
        var window = new Views.ProductCardWindow
        {
            Owner = Application.Current.MainWindow,
            DataContext = this
        };
        window.ShowDialog();
        ReloadFolders();
        Load();
    }

    /// <summary>يملأ النموذج من بيانات منتج موجود (يعمل للعرض حتى لو لا يملك المستخدم صلاحية التعديل).</summary>
    private void LoadIntoForm(int id)
    {
        using var db = new AppDbContext();
        var p = db.Products.FirstOrDefault(x => x.Id == id);
        if (p is null) return;

        IsEditing = true;
        FormTitle = $"تفاصيل: {p.Name}";
        EditingId = p.Id;
        Name = p.Name;
        EnglishName = p.EnglishName ?? "";
        Barcode = p.Barcode ?? "";
        Imei = p.Imei ?? "";
        BuyPrice = p.BuyPrice;
        SellPrice = p.SellPrice;
        Stock = p.Stock;
        MinStock = p.MinStock;
        FormCategory = FormCategories.FirstOrDefault(c => c.Id == p.CategoryId);
        FormSubType = string.IsNullOrWhiteSpace(p.SubType) ? "أخرى" : p.SubType;
        FormIsUsed = p.IsUsed;
        FormIsActive = p.IsActive;
        SelectedDevice = Devices.FirstOrDefault(d => d.Role == p.Device) ?? Devices[0];
        ResultMessage = "";
        IsResultError = false;
    }

    public void CancelEdit()
    {
        IsEditing = false;
        ResultMessage = "";
    }

    [RelayCommand]
    private void SaveProduct()
    {
        ResultMessage = "";
        IsResultError = false;

        if (!CanManage)
        {
            ShowError("ليس لديك صلاحية التعديل");
            return;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            ShowError("أدخل اسم المنتج");
            return;
        }

        if (FormCategory is null)
        {
            ShowError("اختر تصنيفاً للمنتج");
            return;
        }

        if (SellPrice <= 0)
        {
            ShowError("أدخل سعر بيع صحيحاً");
            return;
        }

        try
        {
            using var db = new AppDbContext();
            Product product;

            if (EditingId == 0)
            {
                product = new Product
                {
                    Name = Name.Trim(),
                    EnglishName = string.IsNullOrWhiteSpace(EnglishName) ? null : EnglishName.Trim(),
                    CategoryId = FormCategory.Id,
                    SubType = string.IsNullOrWhiteSpace(FormSubType) ? null : FormSubType.Trim(),
                    Barcode = string.IsNullOrWhiteSpace(Barcode) ? null : Barcode.Trim(),
                    Imei = string.IsNullOrWhiteSpace(Imei) ? null : Imei.Trim(),
                    BuyPrice = BuyPrice,
                    SellPrice = SellPrice,
                    Stock = Stock,
                    MinStock = MinStock,
                    IsUsed = FormIsUsed,
                    IsActive = FormIsActive,
                    Device = SelectedDevice.Role,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                db.Products.Add(product);
            }
            else
            {
                product = db.Products.First(x => x.Id == EditingId);
                var oldStock = product.Stock;
                product.Name = Name.Trim();
                product.EnglishName = string.IsNullOrWhiteSpace(EnglishName) ? null : EnglishName.Trim();
                product.CategoryId = FormCategory.Id;
                product.SubType = string.IsNullOrWhiteSpace(FormSubType) ? null : FormSubType.Trim();
                product.Barcode = string.IsNullOrWhiteSpace(Barcode) ? null : Barcode.Trim();
                product.Imei = string.IsNullOrWhiteSpace(Imei) ? null : Imei.Trim();
                product.BuyPrice = BuyPrice;
                product.SellPrice = SellPrice;
                product.MinStock = MinStock;
                product.IsUsed = FormIsUsed;
                product.IsActive = FormIsActive;
                product.Device = SelectedDevice.Role;

                // ===== تعديل المخزون هو عملية رسمية: سبب إجباري + سطر في OperationLog =====
                if (oldStock != Stock)
                {
                    var dialog = new Views.InputDialog(
                        "تعديل مخزون",
                        $"تغيير الكمية من {oldStock} إلى {Stock} — اذكر السبب (إجباري):",
                        "");
                    if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value))
                    {
                        ShowError("أُلغي الحفظ — تعديل المخزون يتطلب سبباً مكتوباً");
                        return;
                    }

                    OperationWriter.Register(
                        db,
                        OperationType.StockAdjustment,
                        product.Name,
                        0,
                        entityName: "Products",
                        entityId: product.Id,
                        summaryJson: $"الكمية: {oldStock} → {Stock} — السبب: {dialog.Value.Trim()} — بواسطة: {Session.CurrentUser?.DisplayName}");
                }

                product.Stock = Stock;
                product.UpdatedAt = DateTime.Now;
            }

            db.SaveChanges();

            ResultMessage = EditingId == 0
                ? $"تمت إضافة المنتج «{Name.Trim()}»"
                : $"تم حفظ التعديلات على «{Name.Trim()}»";

            IsEditing = false;
            ReloadFolders();
            Load();
        }
        catch (Exception ex)
        {
            ShowError($"خطأ في الحفظ: {ex.Message}");
        }
    }

    /// <summary>حذف المنتج المفتوح حالياً في البطاقة.</summary>
    [RelayCommand]
    private void DeleteCurrent() => DeleteById(EditingId, Name);

    [RelayCommand]
    private void DeleteProduct(ProductRow? row)
    {
        if (row is null) return;
        DeleteById(row.Id, row.Name);
    }

    private void DeleteById(int id, string name)
    {
        if (!CanManage) return;

        var result = MessageBox.Show(
            $"هل أنت متأكد من حذف المنتج «{name}»؟",
            "تأكيد الحذف",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            using var db = new AppDbContext();
            var product = db.Products.FirstOrDefault(p => p.Id == id);
            if (product is null) return;

            var hasSales = db.SaleItems.Any(i => i.ProductId == id);
            var hasPurchases = db.PurchaseItems.Any(i => i.ProductId == id);

            if (hasSales || hasPurchases)
            {
                // المنتج مرتبط بمعاملات — لا يُحذف، يُعطَّل فقط
                product.IsActive = false;
                db.SaveChanges();
                ResultMessage = $"«{name}» له معاملات سابقة — تم إيقافه بدلاً من حذفه";
                IsResultError = true;
            }
            else
            {
                db.Products.Remove(product);
                db.SaveChanges();
                ResultMessage = $"تم حذف المنتج «{name}»";
            }

            ReloadFolders();
            Load();
        }
        catch (Exception ex)
        {
            ShowError($"لا يمكن حذف المنتج: {ex.Message}");
        }
    }

    // ===== إدارة المجلدات (الشجرة) =====

    [RelayCommand]
    private void AddBrandFolder()
    {
        if (!CanManage) return;

        var dlg = new Views.InputDialog("إضافة مجلد/براند جديد", "اسم المجلد (مثال: نوكيا):", "");
        if (dlg.ShowDialog() != true) return;

        var name = dlg.Value.Trim();
        if (name.Length == 0) return;

        try
        {
            using var db = new AppDbContext();
            if (db.Categories.AsNoTracking().Any(c => c.Name == name))
            {
                ShowError($"يوجد مجلد باسم «{name}» مسبقاً");
                return;
            }
            db.Categories.Add(new Category { Name = name });
            db.SaveChanges();
            ReloadFolders();
            LoadCategories();
        }
        catch (Exception ex)
        {
            ShowError($"لا يمكن إنشاء المجلد: {ex.Message}");
        }
    }

    [RelayCommand]
    private void RenameFolder(FolderNode? node)
    {
        if (!CanManage || node?.CategoryId is not { } catId) return;

        // مجلد نوع فرعي: إعادة التسمية تعيد تسمية النوع لكل منتجات البراند تحت هذا النوع
        if (node.SubType is { } subType && subType != "أخرى")
        {
            var dlg = new Views.InputDialog("إعادة تسمية النوع", "الاسم الجديد:", node.Name);
            if (dlg.ShowDialog() != true) return;

            var name = dlg.Value.Trim();
            if (name.Length == 0 || name == node.Name) return;

            try
            {
                using var db = new AppDbContext();
                var products = db.Products.Where(p => p.CategoryId == catId && p.SubType == subType).ToList();
                foreach (var p in products)
                {
                    p.SubType = name;
                    p.UpdatedAt = DateTime.Now;
                }
                db.SaveChanges();
                ReloadFolders();
                Load();
            }
            catch (Exception ex)
            {
                ShowError($"لا يمكن إعادة التسمية: {ex.Message}");
            }
            return;
        }

        var dlg2 = new Views.InputDialog("إعادة تسمية المجلد", "الاسم الجديد:", node.Name);
        if (dlg2.ShowDialog() != true) return;

        var name2 = dlg2.Value.Trim();
        if (name2.Length == 0 || name2 == node.Name) return;

        try
        {
            using var db = new AppDbContext();
            var category = db.Categories.FirstOrDefault(c => c.Id == catId);
            if (category is null) return;
            category.Name = name2;
            db.SaveChanges();
            ReloadFolders();
            LoadCategories();
        }
        catch (Exception ex)
        {
            ShowError($"لا يمكن إعادة التسمية: {ex.Message}");
        }
    }

    [RelayCommand]
    private void DeleteFolder(FolderNode? node)
    {
        if (!CanManage || node?.CategoryId is not { } catId) return;

        // مجلد نوع فرعي: حذفه ينقل منتجاته إلى «أخرى» (تصفير النوع)
        if (node.SubType is { } subType && subType != "أخرى")
        {
            var confirmSub = MessageBox.Show(
                $"هل تريد إزالة النوع «{node.Name}»؟ ستُنقل منتجاته إلى «أخرى».",
                "تأكيد الحذف",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmSub != MessageBoxResult.Yes) return;

            try
            {
                using var db = new AppDbContext();
                var products = db.Products.Where(p => p.CategoryId == catId && p.SubType == subType).ToList();
                foreach (var p in products)
                {
                    p.SubType = null;
                    p.UpdatedAt = DateTime.Now;
                }
                db.SaveChanges();
                ReloadFolders();
                Load();
            }
            catch (Exception ex)
            {
                ShowError($"لا يمكن حذف النوع: {ex.Message}");
            }
            return;
        }

        var result = MessageBox.Show(
            $"هل أنت متأكد من حذف المجلد «{node.Name}»؟",
            "تأكيد الحذف",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            using var db = new AppDbContext();
            var hasProducts = db.Products.Any(p => p.CategoryId == catId);
            if (hasProducts)
            {
                ShowError($"المجلد «{node.Name}» يحتوي منتجات — انقلها أو احذفها أولاً");
                return;
            }
            var category = db.Categories.FirstOrDefault(c => c.Id == catId);
            if (category is null) return;
            db.Categories.Remove(category);
            db.SaveChanges();
            SelectedFolder = null;
            ReloadFolders();
            LoadCategories();
            Load();
        }
        catch (Exception ex)
        {
            ShowError($"لا يمكن حذف المجلد: {ex.Message}");
        }
    }

    private void ShowError(string message)
    {
        ResultMessage = message;
        IsResultError = true;
    }

    public static string DeviceTitleFor(DeviceRole device) => device switch
    {
        DeviceRole.MainServer => "الرئيسي",
        DeviceRole.Accessories => "الإكسسوارات",
        DeviceRole.Repair => "الصيانة",
        DeviceRole.Cashier => "الكاشير",
        _ => "غير محدد"
    };

    /// <summary>أيقونة تمثيلية عامة للجهاز (رمز واحد مُثبَت العمل، بدون صور منتج حقيقية غير متوفرة في النظام).</summary>
    public static string DeviceIconFor(DeviceRole device) => "\uE8B9";
}