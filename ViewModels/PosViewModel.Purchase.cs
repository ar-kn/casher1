using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;
using PhoneAccounting.App.Services;

namespace PhoneAccounting.App.ViewModels;

/// <summary>التبويب الثالث: فاتورة الشراء من مورد.</summary>
public partial class PosViewModel
{
    partial void OnPurchaseDiscountChanged(decimal value)
    {
        if (value < 0) PurchaseDiscount = 0;
        RefreshPurchaseTotals();
    }

    [RelayCommand]
    private void SelectPurchaseTab()
    {
        Mode = InvoiceMode.Purchase;
        LoadSuppliers();
        if (string.IsNullOrEmpty(PurchaseInvoiceDisplay))
            RefreshPurchaseInvoice();
    }

    private void RefreshPurchaseInvoice()
    {
        _purchaseInvoiceDayCount = InvoiceService.NextPurchaseNumber();
        PurchaseInvoiceDisplay = InvoiceService.PurchaseInvoiceNumber(_purchaseInvoiceDayCount);
        PurchaseDateText = DateTime.Now.ToString("dddd، dd MMMM yyyy — HH:mm",
            new System.Globalization.CultureInfo("ar-IQ"));
    }

    private void LoadSuppliers()
    {
        Suppliers.Clear();
        using var db = new AppDbContext();
        foreach (var s in db.Suppliers.AsNoTracking().OrderBy(s => s.Name))
        {
            Suppliers.Add(new SupplierOption
            {
                Id = s.Id,
                Name = s.Name,
                Phone = s.Phone,
                Balance = s.Balance,
                Display = string.IsNullOrWhiteSpace(s.Phone) ? s.Name : $"{s.Name} — {s.Phone}"
            });
        }
    }

    partial void OnPurchaseSearchTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            PurchaseSearchResults.Clear();
            return;
        }

        PurchaseSearchResults.Clear();
        var term = value.Trim();

        using var db = new AppDbContext();
        var deviceFilter = AllowedDevices();

        var matches = db.Products.AsNoTracking()
            .Where(p => p.IsActive && deviceFilter.Contains(p.Device))
            .Where(p => p.Name.Contains(term) ||
                        (p.EnglishName != null && p.EnglishName.Contains(term)) ||
                        (p.Barcode != null && p.Barcode.Contains(term)))
            .OrderBy(p => p.Name)
            .Take(12)
            .ToList();

        foreach (var p in matches)
        {
            PurchaseSearchResults.Add(new PosSearchRow
            {
                ProductId = p.Id,
                Name = p.Name,
                Barcode = p.Barcode,
                Imei = p.Imei,
                SellPrice = p.SellPrice,
                BuyPrice = p.BuyPrice,
                Stock = p.Stock
            });
        }
    }

    [RelayCommand]
    private void AddToPurchaseCart(PosSearchRow? row)
    {
        if (row is null) return;

        var existing = PurchaseCart.FirstOrDefault(c => c.ProductId == row.ProductId);
        if (existing is not null)
        {
            existing.Quantity++;
            RefreshPurchaseTotals();
            return;
        }

        var cartItem = new PurchaseCartItem
        {
            ProductId = row.ProductId,
            ProductName = row.Name,
            Barcode = row.Barcode,
            Quantity = 1,
            UnitCost = row.BuyPrice
        };
        cartItem.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PurchaseCartItem.Quantity) or nameof(PurchaseCartItem.UnitCost))
                RefreshPurchaseTotals();
        };
        PurchaseCart.Add(cartItem);
        RefreshPurchaseTotals();
        PurchaseSearchText = "";
        PurchaseSearchResults.Clear();
    }

    [RelayCommand]
    private void IncrementPurchaseCartQuantity(PurchaseCartItem? item)
    {
        if (item is null) return;
        item.Quantity++;
    }

    [RelayCommand]
    private void DecrementPurchaseCartQuantity(PurchaseCartItem? item)
    {
        if (item is null) return;
        item.Quantity--;
    }

    [RelayCommand]
    private void RemoveFromPurchaseCart(PurchaseCartItem? item)
    {
        if (item is null) return;
        PurchaseCart.Remove(item);
        RefreshPurchaseTotals();
    }

    [RelayCommand]
    private void ClearPurchaseCart()
    {
        PurchaseCart.Clear();
        RefreshPurchaseTotals();
        PurchaseResultMessage = "";
        PurchaseIsResultError = false;
    }

    private void RefreshPurchaseTotals()
    {
        PurchaseSubtotal = PurchaseCart.Sum(c => c.LineTotal);
        PurchaseTotal = Math.Max(0, PurchaseSubtotal - PurchaseDiscount);
    }

    [RelayCommand]
    private void SavePurchase()
    {
        PurchaseResultMessage = "";
        PurchaseIsResultError = false;

        if (!IsNewSupplier && SelectedSupplier is null)
        {
            PurchaseResultMessage = "اختر المورد أو أضف مورداً جديداً";
            PurchaseIsResultError = true;
            return;
        }

        if (IsNewSupplier && string.IsNullOrWhiteSpace(NewSupplierName))
        {
            PurchaseResultMessage = "أدخل اسم المورد الجديد";
            PurchaseIsResultError = true;
            return;
        }

        if (PurchaseCart.Count == 0)
        {
            PurchaseResultMessage = "أضف منتجاً واحداً على الأقل";
            PurchaseIsResultError = true;
            return;
        }

        try
        {
            using var db = new AppDbContext();

            int? supplierId = null;
            string? supplierName = null;

            if (IsNewSupplier)
            {
                var supplier = new Supplier
                {
                    Name = NewSupplierName.Trim(),
                    Phone = string.IsNullOrWhiteSpace(NewSupplierPhone) ? null : NewSupplierPhone.Trim()
                };
                db.Suppliers.Add(supplier);
                db.SaveChanges();
                supplierId = supplier.Id;
                supplierName = supplier.Name;
            }
            else if (SelectedSupplier is not null)
            {
                supplierId = SelectedSupplier.Id;
                supplierName = SelectedSupplier.Name;
            }

            var dayCount = InvoiceService.NextPurchaseNumber();
            var purchase = new Purchase
            {
                InvoiceNumber = InvoiceService.PurchaseInvoiceNumber(dayCount),
                Date = DateTime.Now,
                UserId = Session.CurrentUser?.Id ?? 0,
                Device = Session.Device,
                SupplierId = supplierId,
                SupplierName = supplierName,
                PaymentMethod = PurchasePaymentMethod,
                Discount = PurchaseDiscount,
                Note = string.IsNullOrWhiteSpace(PurchaseNote) ? null : PurchaseNote.Trim(),
                Total = 0
            };

            var total = 0m;
            foreach (var item in PurchaseCart)
            {
                purchase.Items.Add(new PurchaseItem
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitCost
                });
                total += item.LineTotal;

                var product = db.Products.First(p => p.Id == item.ProductId);
                product.Stock += item.Quantity;
                product.BuyPrice = item.UnitCost;
                product.UpdatedAt = DateTime.Now;
            }

            purchase.Total = total - PurchaseDiscount;

            if (purchase.PaymentMethod == PaymentMethod.Credit && supplierId is int sid)
            {
                var supplier = db.Suppliers.First(s => s.Id == sid);
                supplier.Balance += purchase.Total;
            }

            db.Purchases.Add(purchase);

            // سجل العمليات: يُمنح Seq عند Commit مع نفس الحفظ
            var purchaseOp = OperationWriter.Register(
                db,
                OperationType.Purchase,
                purchase.InvoiceNumber,
                purchase.Total,
                entityName: "Purchases");

            db.SaveChanges();

            // M2.4: ربط معرّف الكيان للمستند الجديد (يصل الـ Id بعد الحفظ) — فرض وجود EntityId في مسار الكتابة
            if (purchase.Id > 0 && purchaseOp.EntityId is null)
            {
                OperationWriter.SetEntityTarget(purchaseOp, "Purchases", purchase.Id);
                db.SaveChanges();
            }

            PurchaseResultMessage = $"تم تسجيل فاتورة الشراء رقم {purchase.InvoiceNumber} بقيمة {purchase.Total:N0} {Session.Currency}";
            PurchaseIsResultError = false;

            PurchaseCart.Clear();
            PurchaseDiscount = 0;
            PurchaseNote = "";
            IsNewSupplier = false;
            NewSupplierName = "";
            NewSupplierPhone = "";
            SelectedSupplier = null;
            RefreshPurchaseTotals();
            RefreshPurchaseInvoice();
            LoadSuppliers();
        }
        catch (Exception ex)
        {
            PurchaseResultMessage = $"خطأ في الحفظ: {ex.Message}";
            PurchaseIsResultError = true;
        }
    }
}
