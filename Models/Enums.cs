namespace PhoneAccounting.App.Models;

public enum DeviceRole
{
    MainServer = 0,
    Accessories = 1,
    Repair = 2,
    Cashier = 3
}

public enum UserRole
{
    Admin = 0,
    Accountant = 1,
    Cashier = 2,
    RepairTech = 3,
    Inventory = 4
}

public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
    Credit = 2
}

/// <summary>وضع الفاتورة المعروض داخل نافذة نقطة البيع.</summary>
public enum InvoiceMode
{
    Sale = 0,
    Voucher = 1,
    Purchase = 2
}

/// <summary>طريقة استلام مبلغ سند القبض (لأغراض العرض والتقارير فقط).</summary>
public enum VoucherPaymentMethod
{
    Cash = 0,
    Card = 1,
    Transfer = 2
}

/// <summary>قرار الحسم اليدوي (P3.1 §3) — ثلاثي ليشمل حالة «حذف/تعديل»: بقاء المحذوف حَسماً قائماً بذاته.</summary>
public enum ConflictResolutionType
{
    KeepLocal = 0,
    KeepRemote = 1,
    StaysDeleted = 2
}
