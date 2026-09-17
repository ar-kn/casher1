namespace PhoneAccounting.App.Models;

public enum RepairStatus
{
    Received = 0,
    InProgress = 1,
    Completed = 2,
    Delivered = 3
}

public class RepairJob
{
    public int Id { get; set; }
    public required string JobNumber { get; set; }
    public DateTime ReceivedDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public DateTime? DeliveredDate { get; set; }
    public required string CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public required string DeviceName { get; set; }
    public required string Issue { get; set; }
    public string? Notes { get; set; }
    public RepairStatus Status { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public DeviceRole Device { get; set; }
    public decimal LaborFee { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public bool IsPaid { get; set; }
    public decimal PartsTotal { get; set; }
    public decimal Total { get; set; }
    public List<RepairPart> Parts { get; set; } = [];

    public string? SyncId { get; set; }
    public string? OriginDevice { get; set; }
    public long? DeletedAt { get; set; }
}

public class RepairPart
{
    public int Id { get; set; }
    public int RepairJobId { get; set; }
    public RepairJob? RepairJob { get; set; }
    public int ProductId { get; set; }
    public required string ProductName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal LineTotal
    {
        get => Quantity * UnitPrice;
        set { }
    }

    public string? SyncId { get; set; }
    public string? OriginDevice { get; set; }
    public long? DeletedAt { get; set; }
}
