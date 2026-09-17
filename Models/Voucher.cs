namespace PhoneAccounting.App.Models;

/// <summary>سند قبض: تسجيل دفعة يسدّدها زبون من دينه المستحق (Balance) لدى المحل.</summary>
public class Voucher
{
    public int Id { get; set; }
    public required string VoucherNumber { get; set; }
    public DateTime Date { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public decimal Amount { get; set; }
    public VoucherPaymentMethod PaymentMethod { get; set; } = VoucherPaymentMethod.Cash;
    public string? Note { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    public string? SyncId { get; set; }
    public string? OriginDevice { get; set; }
    public long? DeletedAt { get; set; }
}
