using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Infrastructure;
using PhoneAccounting.App.Models;
using PhoneAccounting.App.Services;
using PhoneAccounting.App.Services.Sync;
using System.Collections.ObjectModel;

namespace PhoneAccounting.App.ViewModels;

public partial class PosCartItem : ObservableObject
{
    public int ProductId { get; init; }
    public required string ProductName { get; init; }
    public string? Barcode { get; init; }
    public decimal BuyPrice { get; init; }
    public string? ProductSyncId { get; init; }

    /// <summary>وسم مشتقّ «قيمته قيد المراجعة» (P3.2A): لا يمنع البيع، يُعلم فقط.</summary>
    [ObservableProperty]
    private bool underReview;

    [ObservableProperty]
    private string? imei;

    [ObservableProperty]
    private int quantity = 1;

    [ObservableProperty]
    private decimal unitPrice;

    public decimal LineTotal
    {
        get => Quantity * UnitPrice;
        set { }
    }

    public decimal LineCost
    {
        get => Quantity * BuyPrice;
        set { }
    }

    partial void OnQuantityChanged(int value)
    {
        if (value < 1) Quantity = 1;
        OnPropertyChanged(nameof(LineTotal));
        OnPropertyChanged(nameof(LineCost));
    }

    partial void OnUnitPriceChanged(decimal value)
    {
        if (value < 0) UnitPrice = 0;
        OnPropertyChanged(nameof(LineTotal));
    }
}

public partial class PosSearchRow
{
    public required int ProductId { get; init; }
    public required string Name { get; init; }
    public string? Barcode { get; init; }
    public string? Imei { get; init; }
    public string? ProductSyncId { get; init; }
    public bool UnderReview { get; init; }
    public decimal SellPrice { get; init; }
    public decimal BuyPrice { get; init; }
    public int Stock { get; init; }
}

public partial class CustomerOption
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string? Phone { get; init; }
    public required string Display { get; init; }
    public decimal Balance { get; init; }
}

public partial class RecentSaleRow
{
    public int Id { get; init; }
    public required string InvoiceNumber { get; init; }
    public required string DateText { get; init; }
    public required string CustomerName { get; init; }
    public decimal Total { get; init; }
}

public partial class SupplierOption
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string? Phone { get; init; }
    public required string Display { get; init; }
    public decimal Balance { get; init; }
}

public partial class PurchaseCartItem : ObservableObject
{
    public int ProductId { get; init; }
    public required string ProductName { get; init; }
    public string? Barcode { get; init; }

    [ObservableProperty]
    private int quantity = 1;

    [ObservableProperty]
    private decimal unitCost;

    public decimal LineTotal
    {
        get => Quantity * UnitCost;
        set { }
    }

    partial void OnQuantityChanged(int value)
    {
        if (value < 1) Quantity = 1;
        OnPropertyChanged(nameof(LineTotal));
    }

    partial void OnUnitCostChanged(decimal value)
    {
        if (value < 0) UnitCost = 0;
        OnPropertyChanged(nameof(LineTotal));
    }
}

public partial class PosViewModel : ObservableObject
{
    private const int WalkInCustomerId = 0;
    private readonly int _editSaleId;
    private int _lastSavedSaleId;

    public ObservableCollection<PosCartItem> Cart { get; } = [];
    public ObservableCollection<PosSearchRow> SearchResults { get; } = [];
    public ObservableCollection<RecentSaleRow> RecentSales { get; } = [];

    [ObservableProperty]
    private string searchText = "";

    [ObservableProperty]
    private string headerLabel = "فاتورة البيع رقم";

    [ObservableProperty]
    private string headerValue = "";

    [ObservableProperty]
    private int invoiceNumber;

    [ObservableProperty]
    private string invoiceDisplay = "";

    [ObservableProperty]
    private string dateText = "";

    [ObservableProperty]
    private string cashierName = "";

    [ObservableProperty]
    private CustomerOption? selectedCustomer;

    [ObservableProperty]
    private bool isNewCustomer;

    [ObservableProperty]
    private string newCustomerName = "";

    [ObservableProperty]
    private string newCustomerPhone = "";

    [ObservableProperty]
    private PaymentMethod paymentMethod = PaymentMethod.Cash;

    [ObservableProperty]
    private decimal discount;

    [ObservableProperty]
    private decimal subtotal;

    [ObservableProperty]
    private decimal total;

    [ObservableProperty]
    private decimal profit;

    /// <summary>عدد أصناف السلة قيد المراجعة (P3.2A) — مشتقّ من SyncConflicts، لا عمود مخزّن.</summary>
    [ObservableProperty]
    private int underReviewCount;

    public bool HasUnderReviewItems => UnderReviewCount > 0;

    partial void OnUnderReviewCountChanged(int value) => OnPropertyChanged(nameof(HasUnderReviewItems));

    [ObservableProperty]
    private string resultMessage = "";

    [ObservableProperty]
    private bool isResultError;

    public List<CustomerOption> Customers { get; } = [];
    public List<PaymentMethodOption> PaymentMethods { get; } =
    [
        new(PaymentMethod.Cash, "نقدي"),
        new(PaymentMethod.Card, "بطاقة"),
        new(PaymentMethod.Credit, "آجل"),
    ];

    // ================= وضع الفاتورة (بيع / سند قبض) =================
    [ObservableProperty]
    private InvoiceMode mode = InvoiceMode.Sale;

    public ObservableCollection<CustomerOption> DebtCustomers { get; } = [];
    public ObservableCollection<CustomerOption> VoucherSearchResults { get; } = [];

    [ObservableProperty]
    private string voucherSearchText = "";

    [ObservableProperty]
    private CustomerOption? voucherCustomer;

    [ObservableProperty]
    private decimal voucherAmount;

    [ObservableProperty]
    private string voucherNote = "";

    [ObservableProperty]
    private VoucherPaymentMethod voucherPaymentMethod = VoucherPaymentMethod.Cash;

    public List<VoucherPaymentMethodOption> VoucherPaymentMethods { get; } =
    [
        new(VoucherPaymentMethod.Cash, "نقداً"),
        new(VoucherPaymentMethod.Card, "بطاقة دفع"),
        new(VoucherPaymentMethod.Transfer, "حوالة بنكية"),
    ];

    [ObservableProperty]
    private string voucherResultMessage = "";

    [ObservableProperty]
    private bool voucherIsError;



    // ================= فاتورة شراء =================
    public ObservableCollection<PurchaseCartItem> PurchaseCart { get; } = [];
    public ObservableCollection<PosSearchRow> PurchaseSearchResults { get; } = [];
    public ObservableCollection<SupplierOption> Suppliers { get; } = [];

    [ObservableProperty]
    private string purchaseSearchText = "";

    [ObservableProperty]
    private SupplierOption? selectedSupplier;

    [ObservableProperty]
    private bool isNewSupplier;

    [ObservableProperty]
    private string newSupplierName = "";

    [ObservableProperty]
    private string newSupplierPhone = "";

    [ObservableProperty]
    private string purchaseInvoiceDisplay = "";

    [ObservableProperty]
    private string purchaseDateText = "";

    [ObservableProperty]
    private string purchaseNote = "";

    [ObservableProperty]
    private PaymentMethod purchasePaymentMethod = PaymentMethod.Cash;

    [ObservableProperty]
    private decimal purchaseDiscount;

    [ObservableProperty]
    private decimal purchaseSubtotal;

    [ObservableProperty]
    private decimal purchaseTotal;

    [ObservableProperty]
    private string purchaseResultMessage = "";

    [ObservableProperty]
    private bool purchaseIsResultError;

    private int _purchaseInvoiceDayCount;



    public bool CanSell => Session.CurrentUser?.Role is UserRole.Admin or UserRole.Cashier or UserRole.Inventory;

    public bool IsEditMode => _editSaleId > 0;

    public PosViewModel() : this(0)
    {
    }

    public PosViewModel(int editSaleId)
    {
        _editSaleId = editSaleId;
        cashierName = Session.CurrentUser?.DisplayName ?? "";
        LoadCustomers();
        LoadRecentSales();
        if (editSaleId > 0)
            LoadSaleForEdit();
        else
            RefreshInvoice();
    }

    private void LoadSaleForEdit()
    {
        using var db = new AppDbContext();
        var sale = db.Sales.AsNoTracking()
            .Include(s => s.Items)
            .FirstOrDefault(s => s.Id == _editSaleId && s.DeletedAt == null);
        if (sale is null)
        {
            RefreshInvoice();
            return;
        }

        HeaderLabel = "تعديل فاتورة رقم";
        HeaderValue = sale.InvoiceNumber;
        DateText = sale.Date.ToString("dddd، dd MMMM yyyy — HH:mm",
            new System.Globalization.CultureInfo("ar-IQ"));

        var underReview = SyncConflictService.UnderReviewSyncIds(db, "Products");

        foreach (var item in sale.Items.OrderBy(i => i.Id))
        {
            var product = db.Products.AsNoTracking()
                .FirstOrDefault(p => p.Id == item.ProductId);

            var cartItem = new PosCartItem
            {
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                Barcode = product?.Barcode,
                Imei = item.Imei,
                ProductSyncId = product?.SyncId,
                UnderReview = product?.SyncId is not null && underReview.Contains(product.SyncId),
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                BuyPrice = item.BuyPrice
            };
            HookCartItem(cartItem);
            Cart.Add(cartItem);
        }

        Discount = sale.Discount;
        PaymentMethod = sale.PaymentMethod;
        RefreshTotals();

        if (sale.CustomerId is int cid && cid > 0)
        {
            SelectedCustomer = Customers.FirstOrDefault(c => c.Id == cid) ?? Customers[0];
        }
    }

    private void RefreshInvoice()
    {
        InvoiceNumber = InvoiceService.NextSaleNumber();
        InvoiceDisplay = InvoiceService.SaleInvoiceNumber(InvoiceNumber);
        HeaderLabel = "فاتورة البيع رقم";
        HeaderValue = InvoiceNumber.ToString();
        DateText = DateTime.Now.ToString("dddd، dd MMMM yyyy — HH:mm",
            new System.Globalization.CultureInfo("ar-IQ"));
    }

    private void LoadRecentSales()
    {
        RecentSales.Clear();

        using var db = new AppDbContext();
        var sales = db.Sales.AsNoTracking()
            .Include(s => s.Customer)
            .Where(s => s.DeletedAt == null)
            .OrderByDescending(s => s.Date)
            .Take(10)
            .ToList();

        foreach (var s in sales)
        {
            RecentSales.Add(new RecentSaleRow
            {
                Id = s.Id,
                InvoiceNumber = s.InvoiceNumber,
                DateText = s.Date.ToString("dd/MM — HH:mm",
                    new System.Globalization.CultureInfo("ar-IQ")),
                CustomerName = s.Customer?.Name ?? "زبون مباشر",
                Total = s.Total
            });
        }
    }

    [RelayCommand]
    private void ViewSale(RecentSaleRow? row)
    {
        if (row is null) return;

        var window = new Views.InvoiceDetailsWindow(saleId: row.Id)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
        LoadRecentSales();
    }

    private void LoadCustomers()
    {
        Customers.Clear();
        Customers.Add(new CustomerOption
        {
            Id = WalkInCustomerId,
            Name = "زبون مباشر",
            Phone = null,
            Display = "زبون مباشر (بدون بطاقة)"
        });

        using var db = new AppDbContext();
        foreach (var c in db.Customers.AsNoTracking().OrderBy(c => c.Name))
        {
            Customers.Add(new CustomerOption
            {
                Id = c.Id,
                Name = c.Name,
                Phone = c.Phone,
                Balance = c.Balance,
                Display = string.IsNullOrWhiteSpace(c.Phone)
                    ? c.Name
                    : $"{c.Name} — {c.Phone}"
            });
        }

        SelectedCustomer = Customers[0];
    }

    /// <summary>يفتح تبويب فاتورة البيع مع تعبئة زبون محدد مسبقاً (يُستدعى من شاشة العملاء).</summary>
    public void PrefillSaleFor(int customerId)
    {
        Mode = InvoiceMode.Sale;
        var match = Customers.FirstOrDefault(c => c.Id == customerId);
        if (match is not null) SelectedCustomer = match;
    }

    public string CustomerDisplay
        => IsNewCustomer
            ? (string.IsNullOrWhiteSpace(NewCustomerName) ? "زبون جديد" : NewCustomerName.Trim())
            : SelectedCustomer?.Display ?? "زبون مباشر";

    partial void OnSelectedCustomerChanged(CustomerOption? value) => OnPropertyChanged(nameof(CustomerDisplay));
    partial void OnNewCustomerNameChanged(string value) => OnPropertyChanged(nameof(CustomerDisplay));

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
        var deviceFilter = AllowedDevices();

        var matches = db.Products.AsNoTracking()
            .Where(p => p.IsActive && deviceFilter.Contains(p.Device))
            .Where(p => p.Name.Contains(term) ||
                        (p.EnglishName != null && p.EnglishName.Contains(term)) ||
                        (p.Barcode != null && p.Barcode.Contains(term)) ||
                        (p.Imei != null && p.Imei.Contains(term)))
            .OrderBy(p => p.Name)
            .Take(12)
            .ToList();

        // P3.2A: وسم «قيمته قيد المراجعة» مشتقّ من حالة الخلافات المفتوحة — استعلام واحد للنتائج كلها.
        var underReview = SyncConflictService.UnderReviewSyncIds(db, "Products");

        foreach (var p in matches)
        {
            SearchResults.Add(new PosSearchRow
            {
                ProductId = p.Id,
                Name = p.Name,
                Barcode = p.Barcode,
                Imei = p.Imei,
                ProductSyncId = p.SyncId,
                UnderReview = p.SyncId is not null && underReview.Contains(p.SyncId),
                SellPrice = p.SellPrice,
                BuyPrice = p.BuyPrice,
                Stock = p.Stock
            });
        }
    }

    [RelayCommand]
    private void AddToCart(PosSearchRow? row)
    {
        if (row is null) return;

        var existing = Cart.FirstOrDefault(c => c.ProductId == row.ProductId);
        if (existing is not null)
        {
            if (existing.Quantity < row.Stock)
            {
                existing.Quantity++;
                RefreshTotals();
            }
            return;
        }

        if (row.Stock <= 0)
        {
            ResultMessage = $"«{row.Name}» نفدت كميته — لا يمكن بيعه";
            IsResultError = true;
            return;
        }

        var cartItem = new PosCartItem
        {
            ProductId = row.ProductId,
            ProductName = row.Name,
            Barcode = row.Barcode,
            Imei = row.Imei,
            ProductSyncId = row.ProductSyncId,
            UnderReview = row.UnderReview,
            Quantity = 1,
            UnitPrice = row.SellPrice,
            BuyPrice = row.BuyPrice
        };
        HookCartItem(cartItem);
        Cart.Add(cartItem);
        RefreshTotals();
        SearchText = "";
        SearchResults.Clear();
    }

    /// <summary>يربط تغييرات الكمية/السعر داخل كل صف بالسلة بتحديث الإجماليات في الشريط الجانبي مباشرة.</summary>
    private void HookCartItem(PosCartItem item)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PosCartItem.Quantity) or nameof(PosCartItem.UnitPrice))
                RefreshTotals();
        };
    }

    [RelayCommand]
    private void IncrementCartQuantity(PosCartItem? item)
    {
        if (item is null) return;
        item.Quantity++;
    }

    [RelayCommand]
    private void DecrementCartQuantity(PosCartItem? item)
    {
        if (item is null) return;
        item.Quantity--;
    }

    [RelayCommand]
    private void RemoveFromCart(PosCartItem? item)
    {
        if (item is null) return;
        Cart.Remove(item);
        RefreshTotals();
    }

    [RelayCommand]
    private void ClearCart()
    {
        Cart.Clear();
        Discount = 0;
        RefreshTotals();
        ResultMessage = "";
        IsResultError = false;
    }

    public void RefreshTotalsForDisplay() => RefreshTotals();

    private void RefreshTotals()
    {
        Subtotal = Cart.Sum(c => c.LineTotal);
        Total = Subtotal - Discount;
        Profit = Cart.Sum(c => c.LineCost) == 0
            ? 0
            : Total - Cart.Sum(c => c.LineCost);
        if (Total < 0) Total = 0;
        RefreshUnderReviewFlags();
    }

    /// <summary>تحديث وسوم «قيد المراجعة» في السلة (P3.2A): استعلام واحد عن الخلافات المفتوحة على الأصناف.</summary>
    private void RefreshUnderReviewFlags()
    {
        if (Cart.Count == 0)
        {
            UnderReviewCount = 0;
            return;
        }

        using var db = new AppDbContext();
        var underReview = SyncConflictService.UnderReviewSyncIds(db, "Products");

        int flagged = 0;
        foreach (var item in Cart)
        {
            bool flag = item.ProductSyncId is not null && underReview.Contains(item.ProductSyncId);
            item.UnderReview = flag;
            if (flag) flagged++;
        }
        UnderReviewCount = flagged;
    }

    partial void OnDiscountChanged(decimal value)
    {
        if (value < 0) Discount = 0;
        RefreshTotals();
    }

    [RelayCommand]
    private void SaveSale()
    {
        ResultMessage = "";
        IsResultError = false;

        if (Cart.Count == 0)
        {
            ShowError("أضف منتجاً واحداً على الأقل");
            return;
        }

        // التحقق من المخزون قبل الحفظ
        using (var checkDb = new AppDbContext())
        {
            foreach (var item in Cart)
            {
                var product = checkDb.Products.First(p => p.Id == item.ProductId);
                if (item.Quantity > product.Stock)
                {
                    ShowError($"كمية «{item.ProductName}» أكبر من المتوفر ({product.Stock})");
                    return;
                }
            }
        }

        // معلومات الزبون
        string customerName;
        int? customerId = null;

        if (IsNewCustomer)
        {
            customerName = NewCustomerName.Trim();
            if (string.IsNullOrWhiteSpace(customerName))
            {
                ShowError("أدخل اسم الزبون");
                return;
            }
        }
        else
        {
            customerName = SelectedCustomer?.Name ?? "زبون مباشر";
            if (SelectedCustomer is not null && SelectedCustomer.Id != WalkInCustomerId)
                customerId = SelectedCustomer.Id;
        }

        try
        {
            using var db = new AppDbContext();

            // قفل الاعتماد: فواتير الأيام المقفلة بعد إقفال اليوم لا تُعدَّل (المرحلة 1)
            if (_editSaleId > 0)
            {
                var editDate = db.Sales.AsNoTracking()
                    .Where(s => s.Id == _editSaleId)
                    .Select(s => s.Date)
                    .FirstOrDefault();
                if (editDate != default && CloseDayService.IsLocked(editDate))
                {
                    ShowError("لا يمكن تعديل هذه الفاتورة — يومها مقفل بعد إقفال اليوم");
                    return;
                }
            }

            // عند التعديل: تراجع أثر المخزون للأصناف الأصلية أولاً
            Sale? existingSale = null;
            int? oldDebtCustomerId = null;
            decimal oldDebtAmount = 0;
            if (_editSaleId > 0)
            {
                existingSale = db.Sales.Include(s => s.Items).FirstOrDefault(s => s.Id == _editSaleId);
                if (existingSale is not null)
                {
                    // نحفظ أثر الدين القديم قبل أي تعديل، لأن sale لاحقاً هي نفس existingSale بالمرجع
                    if (existingSale.PaymentMethod == PaymentMethod.Credit && existingSale.CustomerId is int oldCid)
                    {
                        oldDebtCustomerId = oldCid;
                        oldDebtAmount = existingSale.Total;
                    }

                    foreach (var oldItem in existingSale.Items.ToList())
                    {
                        var product = db.Products.First(p => p.Id == oldItem.ProductId);
                        product.Stock += oldItem.Quantity;
                    }
                    db.SaleItems.RemoveRange(existingSale.Items);
                    existingSale.Items.Clear();
                }
            }

            // إنشاء زبون جديد إذا لزم
            if (IsNewCustomer)
            {
                var customer = new Customer
                {
                    Name = customerName,
                    Phone = string.IsNullOrWhiteSpace(NewCustomerPhone) ? null : NewCustomerPhone.Trim(),
                    CreatedAt = DateTime.Now
                };
                db.Customers.Add(customer);
                db.SaveChanges();
                customerId = customer.Id;
            }

            Sale sale;
            if (existingSale is not null)
            {
                sale = existingSale;
            }
            else
            {
                var dayCount = InvoiceService.NextSaleNumber();
                sale = new Sale
                {
                    InvoiceNumber = InvoiceService.SaleInvoiceNumber(dayCount),
                    Date = DateTime.Now,
                    UserId = Session.CurrentUser?.Id ?? 0,
                    Device = Session.Device
                };
            }

            sale.CustomerId = customerId;
            sale.PaymentMethod = PaymentMethod;
            sale.Discount = Discount;
            sale.Total = 0;
            sale.Profit = 0;

            var total = 0m;
            var cost = 0m;

            foreach (var item in Cart)
            {
                sale.Items.Add(new SaleItem
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Imei = string.IsNullOrWhiteSpace(item.Imei) ? null : item.Imei.Trim(),
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    BuyPrice = item.BuyPrice
                });
                total += item.LineTotal;
                cost += item.LineCost;

                var product = db.Products.First(p => p.Id == item.ProductId);
                product.Stock -= item.Quantity;
                product.UpdatedAt = DateTime.Now;
            }

            sale.Total = total - Discount;
            sale.Profit = sale.Total - cost;

            // ===== سجل العمليات (Unit of Work): Seq يُمنح عند Commit مع نفس الحفظ =====
            var op = OperationWriter.Register(
                db,
                _editSaleId > 0 ? OperationType.SaleEdit : OperationType.Sale,
                sale.InvoiceNumber,
                sale.Total,
                entityName: "Sales",
                entityId: sale.Id > 0 ? sale.Id : null);

            // ===== تحديث دين الزبون (Balance) بما يطابق طريقة الدفع الآجلة =====
            if (oldDebtCustomerId is int reverseCid)
            {
                var oldDebtCustomer = db.Customers.FirstOrDefault(c => c.Id == reverseCid);
                if (oldDebtCustomer is not null)
                    oldDebtCustomer.Balance -= oldDebtAmount;
            }

            if (sale.PaymentMethod == PaymentMethod.Credit && sale.CustomerId is int newDebtCid)
            {
                var newDebtCustomer = db.Customers.FirstOrDefault(c => c.Id == newDebtCid);
                if (newDebtCustomer is not null)
                    newDebtCustomer.Balance += sale.Total;
            }

            if (existingSale is null)
                db.Sales.Add(sale);

            int saveAttempts = 0;
            while (saveAttempts < 5)
            {
                saveAttempts++;
                try
                {
                    db.SaveChanges();
                    break;
                }
                catch (DbUpdateException) when (saveAttempts < 5)
                {
                    sale.InvoiceNumber = InvoiceService.SaleInvoiceNumber(InvoiceService.NextSaleNumber() + saveAttempts);
                    op.DocumentNumber = sale.InvoiceNumber;
                }
            }

            _lastSavedSaleId = sale.Id;

            // M2.4: ربط معرّف الكيان للمستند الجديد (يصل الـ Id بعد الحفظ) — فرض وجود EntityId في مسار الكتابة
            if (op.EntityId is null && sale.Id > 0)
            {
                OperationWriter.SetEntityTarget(op, "Sales", sale.Id);
                db.SaveChanges();
            }

            var message = _editSaleId > 0
                ? $"تم تحديث فاتورة البيع رقم {sale.InvoiceNumber} — الإجمالي {sale.Total:N0} {Session.Currency}"
                : $"تم تسجيل فاتورة البيع رقم {sale.InvoiceNumber} — الإجمالي {sale.Total:N0} {Session.Currency}";

            ResultMessage = message;
            IsResultError = false;

            Cart.Clear();
            Discount = 0;
            RefreshTotals();
            if (_editSaleId > 0)
            {
                HeaderLabel = "تعديل فاتورة رقم";
                HeaderValue = sale.InvoiceNumber;
            }
            else
            {
                RefreshInvoice();
            }
            LoadCustomers();
            LoadRecentSales();
        }
        catch (Exception ex)
        {
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var logPath = System.IO.Path.Combine(desktop, "PosSaveError.log");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SAVE SALE ERROR\n{ex}\n\n");
            }
            catch { }
            var detail = ex.InnerException?.Message ?? ex.Message;
            ShowError($"خطأ في الحفظ: {detail}");
        }
    }

    [RelayCommand]
    private void SaveAndPrintSale()
    {
        SaveSale();
        if (_lastSavedSaleId > 0 && !IsResultError)
            PrintSale();
    }

    [RelayCommand]
    private void PrintSale()
    {
        if (_lastSavedSaleId <= 0)
        {
            ShowError("لا توجد فاتورة محفوظة لطباعتها بعد. احفظ الفاتورة أولاً.");
            return;
        }

        using var db = new AppDbContext();
        var sale = db.Sales
            .Include(s => s.Items)
            .Include(s => s.Customer)
            .FirstOrDefault(s => s.Id == _lastSavedSaleId && s.DeletedAt == null);

        if (sale is null)
        {
            ShowError("لم يتم العثور على الفاتورة المطلوبة.");
            return;
        }

        var company = Database.GetSetting("CompanyName", "شركة الهواتف");
        ReportPrinter.PrintSaleReceipt(company, sale);
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

    private void ShowError(string message)
    {
        ResultMessage = message;
        IsResultError = true;
    }
}

public partial class PaymentMethodOption
{
    public PaymentMethodOption(PaymentMethod method, string display)
    {
        Method = method;
        Display = display;
    }

    public PaymentMethod Method { get; }
    public string Display { get; }
}

public partial class VoucherPaymentMethodOption
{
    public VoucherPaymentMethodOption(VoucherPaymentMethod method, string display)
    {
        Method = method;
        Display = display;
    }

    public VoucherPaymentMethod Method { get; }
    public string Display { get; }
}
