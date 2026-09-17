using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;
using PhoneAccounting.App.Services;
using PhoneAccounting.App.Services.Sync;
using System.Collections.ObjectModel;

namespace PhoneAccounting.App.ViewModels;

public partial class InvoiceDetailItemRow
{
    public required string ProductName { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal LineTotal { get; init; }

    /// <summary>وسم مشتقّ «قيمته قيد المراجعة» (P3.2A) — من حالة SyncConflicts وقت العرض.</summary>
    public bool UnderReview { get; init; }
}

public partial class InvoiceDetailsViewModel : ObservableObject
{
    private readonly int _saleId;
    private readonly int _purchaseId;

    [ObservableProperty]
    private string title = "";

    [ObservableProperty]
    private string kindText = "";

    [ObservableProperty]
    private string invoiceNumber = "";

    [ObservableProperty]
    private string dateText = "";

    [ObservableProperty]
    private string employeeName = "";

    [ObservableProperty]
    private string deviceText = "";

    [ObservableProperty]
    private string partyText = "";

    [ObservableProperty]
    private string partyLabel = "";

    [ObservableProperty]
    private string paymentText = "";

    [ObservableProperty]
    private string discountText = "";

    [ObservableProperty]
    private decimal total;

    [ObservableProperty]
    private string profitText = "";

    [ObservableProperty]
    private bool isSale;

    [ObservableProperty]
    private string voidButtonText = "";

    [ObservableProperty]
    private string voidHelp = "";

    [ObservableProperty]
    private bool canVoid;

    [ObservableProperty]
    private string actionMessage = "";

    [ObservableProperty]
    private bool actionIsError;

    public ObservableCollection<InvoiceDetailItemRow> Items { get; } = [];

    public InvoiceDetailsViewModel(int saleId = 0, int purchaseId = 0)
    {
        _saleId = saleId;
        _purchaseId = purchaseId;
        Load();
    }

    private void Load()
    {
        using var db = new AppDbContext();

        if (_saleId > 0)
        {
            IsSale = true;
            Title = "تفاصيل فاتورة البيع";
            KindText = "فاتورة بيع";

            var sale = db.Sales.AsNoTracking()
                .Include(s => s.User)
                .Include(s => s.Customer)
                .Include(s => s.Items)
                .FirstOrDefault(s => s.Id == _saleId);
            if (sale is null) return;

            InvoiceNumber = sale.InvoiceNumber;
            DateText = sale.Date.ToString("dddd، dd MMMM yyyy — HH:mm",
                new System.Globalization.CultureInfo("ar-IQ"));
            EmployeeName = sale.User?.DisplayName ?? "غير محدد";
            DeviceText = ReportsViewModel.DeviceTextFor(sale.Device);
            PartyLabel = "الزبون";
            PartyText = sale.Customer?.Name ?? "زبون مباشر";
            PaymentText = PaymentTextFor(sale.PaymentMethod);
            DiscountText = sale.Discount.ToString("N0");
            Total = sale.Total;
            ProfitText = sale.Profit.ToString("N0");

            var productIds = sale.Items.Select(i => i.ProductId).ToList();
            var syncByProduct = db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionary(p => p.Id, p => p.SyncId);
            var underReview = SyncConflictService.UnderReviewSyncIds(db, "Products");

            foreach (var item in sale.Items.OrderBy(i => i.Id))
            {
                Items.Add(new InvoiceDetailItemRow
                {
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    LineTotal = item.Total,
                    UnderReview = syncByProduct.TryGetValue(item.ProductId, out var sync) &&
                                  sync is not null && underReview.Contains(sync)
                });
            }

            VoidButtonText = "إلغاء الفاتورة";
            VoidHelp = "إلغاء الفاتورة يعيد الكمية للمخزون ويعكس دين الزبون، ويُسجَّل كعملية «إلغاء فاتورة» ولا يمكن التراجع عنه.";
            CanVoid = CorrectionService.CanCorrect && !CorrectionService.IsSaleVoided(db, sale.Id);
        }
        else if (_purchaseId > 0)
        {
            IsSale = false;
            Title = "تفاصيل فاتورة الشراء";
            KindText = "فاتورة شراء";

            var purchase = db.Purchases.AsNoTracking()
                .Include(p => p.User)
                .Include(p => p.Items)
                .FirstOrDefault(p => p.Id == _purchaseId);
            if (purchase is null) return;

            InvoiceNumber = purchase.InvoiceNumber;
            DateText = purchase.Date.ToString("dddd، dd MMMM yyyy — HH:mm",
                new System.Globalization.CultureInfo("ar-IQ"));
            EmployeeName = purchase.User?.DisplayName ?? "غير محدد";
            DeviceText = ReportsViewModel.DeviceTextFor(purchase.Device);
            PartyLabel = "المورد";
            PartyText = purchase.SupplierName ?? "غير محدد";
            PaymentText = "—";
            DiscountText = "—";
            Total = purchase.Total;
            ProfitText = "—";

            foreach (var item in purchase.Items.OrderBy(i => i.Id))
            {
                Items.Add(new InvoiceDetailItemRow
                {
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    LineTotal = item.Quantity * item.UnitPrice
                });
            }

            VoidButtonText = "مرتجع شراء";
            VoidHelp = "المرتجع يخصم الكمية من المخزون ويعكس دين المورد، ويُسجَّل كعملية «مرتجع شراء» ولا يمكن التراجع عنه.";
            CanVoid = CorrectionService.CanCorrect && !CorrectionService.IsPurchaseReturned(db, purchase.Id);
        }
    }

    [RelayCommand]
    private void EditInvoice()
    {
        if (_saleId > 0)
        {
            using (var checkDb = new AppDbContext())
            {
                var saleDate = checkDb.Sales.AsNoTracking()
                    .Where(s => s.Id == _saleId)
                    .Select(s => s.Date)
                    .FirstOrDefault();
                if (saleDate != default && CloseDayService.IsLocked(saleDate))
                {
                    System.Windows.MessageBox.Show(
                        "لا يمكن تعديل هذه الفاتورة — يومها مقفل بعد إقفال اليوم",
                        "الفاتورة نهائية",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    return;
                }
            }

            var window = new Views.PosWindow(_saleId)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowDialog();
        }
        else if (_purchaseId > 0)
        {
            using (var checkDb = new AppDbContext())
            {
                var purchaseDate = checkDb.Purchases.AsNoTracking()
                    .Where(p => p.Id == _purchaseId)
                    .Select(p => p.Date)
                    .FirstOrDefault();
                if (purchaseDate != default && CloseDayService.IsLocked(purchaseDate))
                {
                    System.Windows.MessageBox.Show(
                        "لا يمكن تعديل هذه الفاتورة — يومها مقفل بعد إقفال اليوم",
                        "الفاتورة نهائية",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    return;
                }
            }

            var window = new Views.PurchaseEditWindow(_purchaseId)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowDialog();
        }

        Items.Clear();
        Load();
    }

    [RelayCommand]
    private void VoidOrder()
    {
        ActionMessage = "";
        ActionIsError = false;

        if (!CanVoid)
        {
            ActionMessage = CorrectionService.CanCorrect
                ? "العملية سُجِّلت مسبقاً لهذه الفاتورة"
                : "هذه العملية متاحة للمدير والمحاسب فقط";
            ActionIsError = true;
            return;
        }

        var dialog = new Views.InputDialog(
            VoidButtonText,
            IsSale
                ? "سبب الإلغاء (إجباري — يُسجَّل في سجل العمليات):"
                : "سبب المرتجع (إجباري — يُسجَّل في سجل العمليات):",
            "");
        if (dialog.ShowDialog() != true) return;

        var reason = dialog.Value.Trim();
        if (reason.Length == 0)
        {
            ActionMessage = "السبب مطلوب لإتمام عملية التصحيح";
            ActionIsError = true;
            return;
        }

        using var db = new AppDbContext();
        ActionMessage = IsSale
            ? CorrectionService.VoidSale(db, _saleId, reason)
            : CorrectionService.ReturnPurchase(db, _purchaseId, reason);
        ActionIsError = !ActionMessage.StartsWith("تم");

        Items.Clear();
        Load();
    }

    private static string PaymentTextFor(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "نقدي",
        PaymentMethod.Card => "بطاقة",
        PaymentMethod.Credit => "آجل",
        _ => ""
    };
}
