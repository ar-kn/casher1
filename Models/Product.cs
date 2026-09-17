namespace PhoneAccounting.App.Models;

public class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public List<Product> Products { get; set; } = [];

    public string? SyncId { get; set; }
    public string? OriginDevice { get; set; }
    public long? DeletedAt { get; set; }
}

public class Product
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? EnglishName { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public string? SubType { get; set; }
    public string? Barcode { get; set; }
    public string? Imei { get; set; }
    public decimal BuyPrice { get; set; }
    public decimal SellPrice { get; set; }
    public int Stock { get; set; }
    public int MinStock { get; set; } = 5;
    public bool IsUsed { get; set; }
    public DeviceRole Device { get; set; } = DeviceRole.Cashier;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public string? SyncId { get; set; }
    public string? OriginDevice { get; set; }
    public long? DeletedAt { get; set; }
}

public class Customer
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Phone { get; set; }
    /// <summary>الدين المستحق على الزبون (يزيد بفواتير البيع الآجلة، وينقص بسندات القبض).</summary>
    public decimal Balance { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<Sale> Sales { get; set; } = [];

    public string? SyncId { get; set; }
    public string? OriginDevice { get; set; }
    public long? DeletedAt { get; set; }
}
