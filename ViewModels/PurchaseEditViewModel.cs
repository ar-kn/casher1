using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;
using PhoneAccounting.App.Services;
using System.Collections.ObjectModel;

namespace PhoneAccounting.App.ViewModels;

public partial class PurchaseEditItem : ObservableObject
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

public partial class PurchaseEditSearchRow
{
    public required int ProductId { get; init; }
    public required string Name { get; init; }
    public decimal BuyPrice { get; init; }
    public int Stock { get; init; }
}

public partial class PurchaseEditViewModel : ObservableObject
{
    private readonly int _purchaseId;
    private List<PurchaseItem> _originalItems = [];

    public ObservableCollection<PurchaseEditItem> Items { get; } = [];
    public ObservableCollection<PurchaseEditSearchRow> SearchResults { get; } = [];

    [ObservableProperty]
    private string invoiceNumber = "";

    [ObservableProperty]
    private string dateText = "";

    [ObservableProperty]
    private string supplierName = "";

    [ObservableProperty]
    private string searchText = "";

    [ObservableProperty]
    private decimal total;

    [ObservableProperty]
    private string resultMessage = "";

    [ObservableProperty]
    private bool isResultError;

    public PurchaseEditViewModel(int purchaseId)
    {
        _purchaseId = purchaseId;
        Load();
    }

    private void Load()
    {
        using var db = new AppDbContext();
        var purchase = db.Purchases
            .Include(p => p.Items)
            .First(p => p.Id == _purchaseId);

        _originalItems = purchase.Items.ToList();
        InvoiceNumber = purchase.InvoiceNumber;
        DateText = purchase.Date.ToString("dddd، dd MMMM yyyy",
            new System.Globalization.CultureInfo("ar-IQ"));
        SupplierName = purchase.SupplierName ?? "";

        foreach (var item in purchase.Items.OrderBy(i => i.Id))
        {
            Items.Add(new PurchaseEditItem
            {
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice
            });
        }

        RefreshTotal();
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
        var alreadyAdded = Items.Select(i => i.ProductId).ToHashSet();

        var matches = db.Products.AsNoTracking()
            .Where(p => p.IsActive)
            .Where(p => p.Name.Contains(term) || (p.EnglishName != null && p.EnglishName.Contains(term)))
            .OrderBy(p => p.Name)
            .Take(10)
            .ToList();

        foreach (var p in matches.Where(p => !alreadyAdded.Contains(p.Id)))
        {
            SearchResults.Add(new PurchaseEditSearchRow
            {
                ProductId = p.Id,
                Name = p.Name,
                BuyPrice = p.BuyPrice,
                Stock = p.Stock
            });
        }
    }

    [RelayCommand]
    private void AddProduct(PurchaseEditSearchRow? row)
    {
        if (row is null) return;

        var existing = Items.FirstOrDefault(i => i.ProductId == row.ProductId);
        if (existing is not null)
        {
            existing.Quantity++;
            RefreshTotal();
            return;
        }

        Items.Add(new PurchaseEditItem
        {
            ProductId = row.ProductId,
            ProductName = row.Name,
            Quantity = 1,
            UnitPrice = row.BuyPrice
        });
        RefreshTotal();
        SearchText = "";
        SearchResults.Clear();
    }

    [RelayCommand]
    private void RemoveItem(PurchaseEditItem? item)
    {
        if (item is null) return;
        Items.Remove(item);
        RefreshTotal();
    }

    private void RefreshTotal()
    {
        Total = Items.Sum(i => i.LineTotal);
    }

    [RelayCommand]
    private void Save()
    {
        ResultMessage = "";
        IsResultError = false;

        if (string.IsNullOrWhiteSpace(SupplierName))
        {
            ResultMessage = "أدخل اسم المورد/الشركة";
            IsResultError = true;
            return;
        }

        if (Items.Count == 0)
        {
            ResultMessage = "لا يمكن ترك الفاتورة بدون أصناف";
            IsResultError = true;
            return;
        }

        try
        {
            using var db = new AppDbContext();

            // قفل الاعتماد: التعديل مسموح فقط في فترة السماح (قبل إقفال يوم الفاتورة)
            var purchaseDate = db.Purchases.AsNoTracking()
                .Where(p => p.Id == _purchaseId)
                .Select(p => p.Date)
                .FirstOrDefault();
            if (CloseDayService.IsLocked(purchaseDate))
            {
                ResultMessage = "لا يمكن تعديل هذه الفاتورة — يومها مقفل بعد إقفال اليوم";
                IsResultError = true;
                return;
            }

            // تراجع أثر المخزون للأصناف الأصلية
            foreach (var old in _originalItems)
            {
                var product = db.Products.First(p => p.Id == old.ProductId);
                product.Stock -= old.Quantity;
            }

            var purchase = db.Purchases.Include(p => p.Items).First(p => p.Id == _purchaseId);
            db.PurchaseItems.RemoveRange(purchase.Items);

            var total = 0m;
            foreach (var item in Items)
            {
                db.PurchaseItems.Add(new PurchaseItem
                {
                    PurchaseId = purchase.Id,
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice
                });
                total += item.LineTotal;

                var product = db.Products.First(p => p.Id == item.ProductId);
                product.Stock += item.Quantity;
                product.UpdatedAt = DateTime.Now;
            }

            purchase.Total = total;
            purchase.SupplierName = SupplierName.Trim();

            OperationWriter.Register(
                db,
                OperationType.PurchaseEdit,
                purchase.InvoiceNumber,
                purchase.Total,
                entityName: "Purchases",
                entityId: _purchaseId);

            db.SaveChanges();

            _originalItems = Items.Select(i => new PurchaseItem
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList();

            ResultMessage = $"تم حفظ تعديلات فاتورة المشتريات رقم {purchase.InvoiceNumber}";
        }
        catch (Exception ex)
        {
            ResultMessage = $"خطأ في الحفظ: {ex.Message}";
            IsResultError = true;
        }
    }
}
