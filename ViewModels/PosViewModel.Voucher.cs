using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;
using PhoneAccounting.App.Services;

namespace PhoneAccounting.App.ViewModels;

/// <summary>التبويب الثاني: سند القبض (تحصيل دين زبون).</summary>
public partial class PosViewModel
{
    public decimal VoucherCustomerBalance => VoucherCustomer?.Balance ?? 0;
    public decimal VoucherRemainingAfterPayment => Math.Max(0, VoucherCustomerBalance - VoucherAmount);

    partial void OnVoucherCustomerChanged(CustomerOption? value)
    {
        OnPropertyChanged(nameof(VoucherCustomerBalance));
        OnPropertyChanged(nameof(VoucherRemainingAfterPayment));
        VoucherAmount = 0;
        VoucherResultMessage = "";
        VoucherIsError = false;
        VoucherSearchText = "";
        VoucherSearchResults.Clear();
    }

    partial void OnVoucherSearchTextChanged(string value)
    {
        VoucherSearchResults.Clear();
        if (string.IsNullOrWhiteSpace(value)) return;

        var term = value.Trim();
        foreach (var c in DebtCustomers.Where(c =>
                     c.Name.Contains(term) ||
                     (c.Phone != null && c.Phone.Contains(term))))
        {
            VoucherSearchResults.Add(c);
        }
    }

    partial void OnVoucherAmountChanged(decimal value)
    {
        if (value < 0) VoucherAmount = 0;
        OnPropertyChanged(nameof(VoucherRemainingAfterPayment));
    }

    [RelayCommand]
    private void SelectSaleTab() => Mode = InvoiceMode.Sale;

    [RelayCommand]
    private void SelectVoucherTab()
    {
        Mode = InvoiceMode.Voucher;
        LoadDebtCustomers();
    }

    private void LoadDebtCustomers()
    {
        var previouslySelectedId = VoucherCustomer?.Id;
        DebtCustomers.Clear();
        VoucherSearchText = "";
        VoucherSearchResults.Clear();

        using var db = new AppDbContext();
        var list = db.Customers.AsNoTracking()
            .Where(c => c.Balance > 0)
            .OrderByDescending(c => c.Balance)
            .ToList();

        foreach (var c in list)
        {
            DebtCustomers.Add(new CustomerOption
            {
                Id = c.Id,
                Name = c.Name,
                Phone = c.Phone,
                Balance = c.Balance,
                Display = string.IsNullOrWhiteSpace(c.Phone)
                    ? c.Name
                    : $"{c.Name} — {c.Phone}"
            });
        }

        VoucherCustomer = previouslySelectedId is int pid
            ? DebtCustomers.FirstOrDefault(c => c.Id == pid)
            : null;
    }

    [RelayCommand]
    private void SelectVoucherCustomer(CustomerOption? customer)
    {
        if (customer is null) return;
        VoucherCustomer = customer;
    }

    /// <summary>يفتح تبويب سند القبض مع تعبئة زبون محدد مسبقاً (يُستدعى من شاشة العملاء).
    /// لا يفعل شيئاً إن لم يكن على هذا الزبون دين مستحق أصلاً.</summary>
    public void PrefillVoucherFor(int customerId)
    {
        Mode = InvoiceMode.Voucher;
        LoadDebtCustomers();
        var match = DebtCustomers.FirstOrDefault(c => c.Id == customerId);
        if (match is not null) VoucherCustomer = match;
    }

    [RelayCommand]
    private void SaveVoucher()
    {
        VoucherResultMessage = "";
        VoucherIsError = false;

        if (VoucherCustomer is null)
        {
            VoucherResultMessage = "اختر الزبون أولاً";
            VoucherIsError = true;
            return;
        }

        if (VoucherAmount <= 0)
        {
            VoucherResultMessage = "أدخل مبلغاً صحيحاً أكبر من صفر";
            VoucherIsError = true;
            return;
        }

        if (VoucherAmount > VoucherCustomerBalance)
        {
            VoucherResultMessage = $"المبلغ أكبر من الدين المستحق ({VoucherCustomerBalance:N0} د.ع)";
            VoucherIsError = true;
            return;
        }

        try
        {
            using var db = new AppDbContext();
            var customer = db.Customers.First(c => c.Id == VoucherCustomer.Id);

            var dayCount = InvoiceService.NextVoucherNumber();
            var voucher = new Voucher
            {
                VoucherNumber = InvoiceService.VoucherNumberDisplay(dayCount),
                Date = DateTime.Now,
                CustomerId = customer.Id,
                Amount = VoucherAmount,
                PaymentMethod = VoucherPaymentMethod,
                Note = string.IsNullOrWhiteSpace(VoucherNote) ? null : VoucherNote.Trim(),
                UserId = Session.CurrentUser?.Id ?? 0
            };

            customer.Balance -= VoucherAmount;
            db.Vouchers.Add(voucher);

            // سجل العمليات: يُمنح Seq عند Commit مع نفس الحفظ
            var voucherOp = OperationWriter.Register(
                db,
                OperationType.VoucherIn,
                voucher.VoucherNumber,
                voucher.Amount,
                entityName: "Vouchers");

            db.SaveChanges();

            // M2.4: ربط معرّف الكيان للمستند الجديد (يصل الـ Id بعد الحفظ) — فرض وجود EntityId في مسار الكتابة
            if (voucher.Id > 0 && voucherOp.EntityId is null)
            {
                OperationWriter.SetEntityTarget(voucherOp, "Vouchers", voucher.Id);
                db.SaveChanges();
            }

            VoucherResultMessage = $"تم تسجيل سند القبض رقم {voucher.VoucherNumber} بمبلغ {VoucherAmount:N0} {Session.Currency}";
            VoucherIsError = false;

            VoucherAmount = 0;
            VoucherNote = "";
            LoadDebtCustomers();
            LoadCustomers(); // لتحديث رصيد الزبون في قائمة زبائن البيع أيضاً
        }
        catch (Exception ex)
        {
            VoucherResultMessage = $"خطأ في الحفظ: {ex.Message}";
            VoucherIsError = true;
        }
    }
}
