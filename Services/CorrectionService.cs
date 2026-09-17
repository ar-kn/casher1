using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;

namespace PhoneAccounting.App.Services;

/// <summary>
/// عمليات التصحيح (البند 8.5): إلغاء فاتورة بيع (Void) ومرتجع شراء —
/// عمليات جدية تسجَّل في OperationLog ولا تعدّل الصف الأصلي (حذف ناعم فقط).
/// متاحة بعد إقفال اليوم عمداً؛ التقييد ينطبق على التعديل لا على التصحيح.
/// </summary>
public static class CorrectionService
{
    public static bool CanCorrect => Session.CurrentUser?.Role is UserRole.Admin or UserRole.Accountant;

    public static bool IsSaleVoided(AppDbContext db, int saleId) =>
        db.OperationLogs.Any(o =>
            o.OpType == OperationType.SaleVoid.ToString() && o.EntityId == saleId);

    public static bool IsPurchaseReturned(AppDbContext db, int purchaseId) =>
        db.OperationLogs.Any(o =>
            o.OpType == OperationType.PurchaseReturn.ToString() && o.EntityId == purchaseId);

    public static string VoidSale(AppDbContext db, int saleId, string reason)
    {
        var sale = db.Sales.Include(s => s.Items).FirstOrDefault(s => s.Id == saleId);
        if (sale is null) return "الفاتورة غير موجودة";
        if (sale.DeletedAt is not null) return "الفاتورة غير متاحة";
        if (IsSaleVoided(db, saleId)) return "هذه الفاتورة أُلغيَت مسبقاً";

        // إعادة الكمية للمخزون (عكس تأثير فاتورة البيع)
        foreach (var item in sale.Items)
        {
            var product = db.Products.FirstOrDefault(p => p.Id == item.ProductId);
            if (product is null) continue;
            product.Stock += item.Quantity;
            product.UpdatedAt = DateTime.Now;
        }

        // عكس دين الزبون إذا كانت الفاتورة آجلة
        if (sale.PaymentMethod == PaymentMethod.Credit && sale.CustomerId is int cid)
        {
            var customer = db.Customers.FirstOrDefault(c => c.Id == cid);
            if (customer is not null) customer.Balance -= sale.Total;
        }

        // حذف ناعم — الصف يبقى للسجل والتدقيق، ولا يُسترجَع إلا بعملية جديدة
        sale.DeletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        OperationWriter.Register(
            db,
            OperationType.SaleVoid,
            sale.InvoiceNumber,
            sale.Total,
            entityName: "Sales",
            entityId: sale.Id,
            summaryJson: JsonNote(reason));

        db.SaveChanges();
        return $"تم إلغاء فاتورة البيع {sale.InvoiceNumber} وإعادة الكمية للمخزون";
    }

    public static string ReturnPurchase(AppDbContext db, int purchaseId, string reason)
    {
        var purchase = db.Purchases.Include(p => p.Items).FirstOrDefault(p => p.Id == purchaseId);
        if (purchase is null) return "الفاتورة غير موجودة";
        if (purchase.DeletedAt is not null) return "الفاتورة غير متاحة";
        if (IsPurchaseReturned(db, purchaseId)) return "هذه الفاتورة أُرجعت مسبقاً";

        // خصم الكمية من المخزون (عكس تأثير فاتورة الشراء)
        foreach (var item in purchase.Items)
        {
            var product = db.Products.FirstOrDefault(p => p.Id == item.ProductId);
            if (product is null) continue;
            product.Stock -= item.Quantity;
            product.UpdatedAt = DateTime.Now;
        }

        // عكس دين المورد إذا كانت الفاتورة آجلة
        if (purchase.PaymentMethod == PaymentMethod.Credit && purchase.SupplierId is int sid)
        {
            var supplier = db.Suppliers.FirstOrDefault(s => s.Id == sid);
            if (supplier is not null) supplier.Balance -= purchase.Total;
        }

        purchase.DeletedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        OperationWriter.Register(
            db,
            OperationType.PurchaseReturn,
            purchase.InvoiceNumber,
            purchase.Total,
            entityName: "Purchases",
            entityId: purchase.Id,
            summaryJson: JsonNote(reason));

        db.SaveChanges();
        return $"تم تسجيل مرتجع فاتورة الشراء {purchase.InvoiceNumber} وخصم الكمية من المخزون";
    }

    private static string JsonNote(string reason)
    {
        return System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["reason"] = reason,
            ["by"] = Session.CurrentUser?.DisplayName,
            ["device"] = Session.Device.ToString()
        });
    }
}