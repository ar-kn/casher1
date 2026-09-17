namespace PhoneAccounting.App.Models;

/// <summary>مورد نشتري منه منتجات؛ Balance = المبلغ الذي ندين به لهذا المورد (شراء آجل).</summary>
public class Supplier
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Phone { get; set; }
    public decimal Balance { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<Purchase> Purchases { get; set; } = [];

    public string? SyncId { get; set; }
    public string? OriginDevice { get; set; }
    public long? DeletedAt { get; set; }
}
