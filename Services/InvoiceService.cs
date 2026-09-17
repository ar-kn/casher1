using PhoneAccounting.App.Data;

namespace PhoneAccounting.App.Services;

public static class InvoiceService
{
    /// <summary>الرقم التسلسلي لفاتورة اليوم = عدد الفواتير المسجلة اليوم + 1.</summary>
    public static int NextSaleNumber()
    {
        using var db = new AppDbContext();
        var today = DateTime.Today;
        return db.Sales.Count(s => s.Date.Date == today) + 1;
    }

    public static int NextPurchaseNumber()
    {
        using var db = new AppDbContext();
        var today = DateTime.Today;
        return db.Purchases.Count(p => p.Date.Date == today) + 1;
    }

    public static int NextRepairNumber()
    {
        using var db = new AppDbContext();
        var today = DateTime.Today;
        return db.RepairJobs.Count(j => j.ReceivedDate.Date == today) + 1;
    }

    public static int NextVoucherNumber()
    {
        using var db = new AppDbContext();
        var today = DateTime.Today;
        return db.Vouchers.Count(v => v.Date.Date == today) + 1;
    }

    /// <summary>بادئة الجهاز في أرقام المستندات — أول 8 خانات من الهوية (32 بت) كافية لفريقٍ من عدة أجهزة وتقصُر على الورق (البند 2).</summary>
    public static string DevicePrefix()
    {
        var id = Session.DeviceId;
        if (string.IsNullOrWhiteSpace(id)) return "DEV";
        return id.Length <= 8 ? id : id[..8];
    }

    /// <summary>رقم فاتورة فريد للبيع يحوي بادئة الجهاز والتاريخ وعدد فواتير ذلك اليوم على الجهاز نفسه.</summary>
    public static string SaleInvoiceNumber(int dayCount) =>
        SaleInvoiceNumber(dayCount, DateTime.Today);

    public static string SaleInvoiceNumber(int dayCount, DateTime date) =>
        $"F-{DevicePrefix()}-{date:yyyy-MM-dd}-{dayCount:D3}";

    public static string PurchaseInvoiceNumber(int dayCount) =>
        PurchaseInvoiceNumber(dayCount, DateTime.Today);

    public static string PurchaseInvoiceNumber(int dayCount, DateTime date) =>
        $"P-{DevicePrefix()}-{date:yyyy-MM-dd}-{dayCount:D3}";

    public static string RepairJobNumber(int dayCount) =>
        RepairJobNumber(dayCount, DateTime.Today);

    public static string RepairJobNumber(int dayCount, DateTime date) =>
        $"R-{DevicePrefix()}-{date:yyyy-MM-dd}-{dayCount:D3}";

    public static string VoucherNumberDisplay(int dayCount) =>
        VoucherNumberDisplay(dayCount, DateTime.Today);

    public static string VoucherNumberDisplay(int dayCount, DateTime date) =>
        $"V-{DevicePrefix()}-{date:yyyy-MM-dd}-{dayCount:D3}";
}
