using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace PhoneAccounting.App.ViewModels;

public partial class CustomerRow : ObservableObject
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string? Phone { get; init; }
    public decimal Balance { get; init; }
    public int InvoiceCount { get; init; }
    public required string LastActivityText { get; init; }
    public string BalanceState => Balance > 0 ? "مدين" : Balance < 0 ? "دائن" : "مسوّى";

    [ObservableProperty]
    private bool isSelected;
}

/// <summary>سجل معاملة واحدة (فاتورة بيع آجلة أو سند قبض) ضمن السجل المالي لزبون محدد.</summary>
public partial class CustomerTransactionRow
{
    public required string TypeText { get; init; }
    public required string ReferenceText { get; init; }
    public required string DateText { get; init; }
    /// <summary>موجب = يزيد الدين (فاتورة آجلة)، سالب = ينقص الدين (سند قبض).</summary>
    public decimal Amount { get; init; }
    public required string IconGlyph { get; init; }
}

public partial class CustomersViewModel : ObservableObject
{
    [ObservableProperty]
    private string searchText = "";

    public List<string> FilterOptions { get; } = ["الكل", "مدينون", "مسوّى"];

    [ObservableProperty]
    private string selectedFilter = "الكل";

    public ObservableCollection<CustomerRow> Customers { get; } = [];
    public ObservableCollection<CustomerTransactionRow> SelectedCustomerTransactions { get; } = [];

    [ObservableProperty]
    private CustomerRow? selectedCustomer;

    [ObservableProperty]
    private int customersCount;

    [ObservableProperty]
    private decimal totalReceivables;

    [ObservableProperty]
    private int debtorsCount;

    /// <summary>معدل التحصيل التراكمي: كل ما تم تحصيله عبر سندات القبض تاريخياً، مقارنةً بكل ما بيع بالآجل تاريخياً.</summary>
    [ObservableProperty]
    private double collectionRatePercent;

    // ===== إضافة زبون جديد =====
    [ObservableProperty]
    private bool isAdding;

    [ObservableProperty]
    private string newName = "";

    [ObservableProperty]
    private string newPhone = "";

    [ObservableProperty]
    private string resultMessage = "";

    [ObservableProperty]
    private bool isResultError;

    [ObservableProperty]
    private string exportMessage = "";

    [ObservableProperty]
    private bool exportIsError;

    /// <summary>يطلب فتح نقطة البيع (تبويب فاتورة بيع) مع تعبئة هذا الزبون.</summary>
    public event Action<int>? OpenSaleRequested;

    /// <summary>يطلب فتح نقطة البيع (تبويب سند قبض) مع تعبئة هذا الزبون.</summary>
    public event Action<int>? OpenVoucherRequested;

    public CustomersViewModel()
    {
        Load();
    }

    partial void OnSearchTextChanged(string value) => Load();
    partial void OnSelectedFilterChanged(string value) => Load();

    /// <summary>يُعاد تحميل كل البيانات (يُستدعى أيضاً عند العودة من نقطة البيع بعد بيع أو سند قبض).</summary>
    public void Load()
    {
        Customers.Clear();

        using var db = new AppDbContext();
        var all = db.Customers.AsNoTracking().Include(c => c.Sales).OrderBy(c => c.Name).ToList();

        CustomersCount = all.Count;
        TotalReceivables = all.Sum(c => c.Balance > 0 ? c.Balance : 0);
        DebtorsCount = all.Count(c => c.Balance > 0);

        var totalCreditSales = db.Sales
            .Where(s => s.PaymentMethod == PaymentMethod.Credit && s.DeletedAt == null)
            .Sum(s => (decimal?)s.Total) ?? 0;
        var totalCollected = db.Vouchers.Sum(v => (decimal?)v.Amount) ?? 0;
        CollectionRatePercent = totalCreditSales > 0
            ? Math.Min(100, (double)(totalCollected / totalCreditSales * 100))
            : 0;

        IEnumerable<Customer> filtered = all;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            filtered = filtered.Where(c => c.Name.Contains(term) || (c.Phone != null && c.Phone.Contains(term)));
        }
        if (SelectedFilter == "مدينون") filtered = filtered.Where(c => c.Balance > 0);
        else if (SelectedFilter == "مسوّى") filtered = filtered.Where(c => c.Balance <= 0);

        var previouslySelectedId = SelectedCustomer?.Id;

        foreach (var c in filtered)
        {
            var activeSales = c.Sales.Where(s => s.DeletedAt == null).ToList();
            var lastSale = activeSales.OrderByDescending(s => s.Date).FirstOrDefault();
            Customers.Add(new CustomerRow
            {
                Id = c.Id,
                Name = c.Name,
                Phone = c.Phone,
                Balance = c.Balance,
                InvoiceCount = activeSales.Count,
                LastActivityText = lastSale is not null ? lastSale.Date.ToString("yyyy/MM/dd") : "—"
            });
        }

        SelectedCustomer = previouslySelectedId is int pid
            ? Customers.FirstOrDefault(c => c.Id == pid)
            : Customers.FirstOrDefault();
    }

    partial void OnSelectedCustomerChanged(CustomerRow? value)
    {
        foreach (var row in Customers)
            row.IsSelected = row == value;
        LoadTransactions();
    }

    private void LoadTransactions()
    {
        SelectedCustomerTransactions.Clear();
        if (SelectedCustomer is null) return;

        using var db = new AppDbContext();
        var sales = db.Sales.AsNoTracking().Where(s => s.CustomerId == SelectedCustomer.Id && s.DeletedAt == null).ToList();
        var vouchers = db.Vouchers.AsNoTracking().Where(v => v.CustomerId == SelectedCustomer.Id).ToList();

        var rows = new List<(DateTime Date, CustomerTransactionRow Row)>();

        foreach (var s in sales)
        {
            rows.Add((s.Date, new CustomerTransactionRow
            {
                TypeText = s.PaymentMethod == PaymentMethod.Credit ? "فاتورة بيع آجلة" : "فاتورة بيع",
                ReferenceText = s.InvoiceNumber,
                DateText = s.Date.ToString("yyyy/MM/dd HH:mm"),
                Amount = s.PaymentMethod == PaymentMethod.Credit ? s.Total : 0,
                IconGlyph = "\uE7D2"
            }));
        }

        foreach (var v in vouchers)
        {
            rows.Add((v.Date, new CustomerTransactionRow
            {
                TypeText = "سند قبض",
                ReferenceText = v.VoucherNumber,
                DateText = v.Date.ToString("yyyy/MM/dd HH:mm"),
                Amount = -v.Amount,
                IconGlyph = "\uE8C7"
            }));
        }

        foreach (var r in rows.OrderByDescending(r => r.Date).Take(12))
            SelectedCustomerTransactions.Add(r.Row);
    }

    [RelayCommand]
    private void SelectCustomer(CustomerRow? row)
    {
        if (row is null) return;
        SelectedCustomer = row;
    }

    [RelayCommand]
    private void StartAdd()
    {
        IsAdding = true;
        NewName = "";
        NewPhone = "";
        ResultMessage = "";
        IsResultError = false;
    }

    [RelayCommand]
    private void CancelAdd() => IsAdding = false;

    [RelayCommand]
    private void SaveNewCustomer()
    {
        ResultMessage = "";
        IsResultError = false;

        if (string.IsNullOrWhiteSpace(NewName))
        {
            ResultMessage = "أدخل اسم الزبون";
            IsResultError = true;
            return;
        }

        using var db = new AppDbContext();
        db.Customers.Add(new Customer
        {
            Name = NewName.Trim(),
            Phone = string.IsNullOrWhiteSpace(NewPhone) ? null : NewPhone.Trim()
        });
        db.SaveChanges();

        IsAdding = false;
        Load();
    }

    [RelayCommand]
    private void NewSaleForSelected()
    {
        if (SelectedCustomer is null) return;
        OpenSaleRequested?.Invoke(SelectedCustomer.Id);
    }

    [RelayCommand]
    private void NewVoucherForSelected()
    {
        if (SelectedCustomer is null || SelectedCustomer.Balance <= 0) return;
        OpenVoucherRequested?.Invoke(SelectedCustomer.Id);
    }

    [RelayCommand]
    private void ExportCustomers()
    {
        ExportMessage = "";
        ExportIsError = false;

        if (Customers.Count == 0)
        {
            ExportMessage = "لا يوجد زبائن لتصديرهم";
            ExportIsError = true;
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "تصدير قائمة الزبائن",
            Filter = "ملف CSV (*.csv)|*.csv",
            FileName = $"الزبائن_{DateTime.Now:yyyy-MM-dd}.csv"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("الاسم,الهاتف,الرصيد,الحالة,عدد الفواتير,آخر نشاط");
            foreach (var c in Customers)
            {
                sb.AppendLine($"{Csv(c.Name)},{Csv(c.Phone ?? "")},{c.Balance},{Csv(c.BalanceState)},{c.InvoiceCount},{Csv(c.LastActivityText)}");
            }
            File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
            ExportMessage = $"تم تصدير {Customers.Count} زبون بنجاح";
        }
        catch (Exception ex)
        {
            ExportMessage = $"تعذّر التصدير: {ex.Message}";
            ExportIsError = true;
        }
    }

    private static string Csv(string value) =>
        value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
