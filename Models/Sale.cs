namespace PhoneAccounting.App.Models;

public class Sale
{
    public int Id { get; set; }
    public required string InvoiceNumber { get; set; }
    public DateTime Date { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public DeviceRole Device { get; set; }
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public decimal Profit { get; set; }
    public List<SaleItem> Items { get; set; } = [];

    public string? SyncId { get; set; }
    public string? OriginDevice { get; set; }
    public long? DeletedAt { get; set; }
}

public class SaleItem
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public Sale? Sale { get; set; }
    public int ProductId { get; set; }
    public required string ProductName { get; set; }
    /// <summary>رقم IMEI الفعلي للجهاز المباع (إن وُجد)، يُسجَّل بشكل ثابت على الفاتورة حتى لو تغيّر لاحقاً على المنتج.</summary>
    public string? Imei { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal BuyPrice { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal Total { get => Quantity * UnitPrice; set { } }

    public string? SyncId { get; set; }
    public string? OriginDevice { get; set; }
    public long? DeletedAt { get; set; }
}

public class Purchase
{
    public int Id { get; set; }
    public required string InvoiceNumber { get; set; }
    public DateTime Date { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public DeviceRole Device { get; set; }
    public string? SupplierName { get; set; }
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public decimal Discount { get; set; }
    public string? Note { get; set; }
    public decimal Total { get; set; }
    public List<PurchaseItem> Items { get; set; } = [];

    public string? SyncId { get; set; }
    public string? OriginDevice { get; set; }
    public long? DeletedAt { get; set; }
}

public class PurchaseItem
{
    public int Id { get; set; }
    public int PurchaseId { get; set; }
    public Purchase? Purchase { get; set; }
    public int ProductId { get; set; }
    public required string ProductName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    public string? SyncId { get; set; }
    public string? OriginDevice { get; set; }
    public long? DeletedAt { get; set; }
}
