using System.Windows;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Infrastructure;

namespace PhoneAccounting.App.Views;

public partial class SupplierDetailsWindow : Window
{
    public SupplierDetailsWindow(int supplierId)
    {
        InitializeComponent();
        Load(supplierId);
    }

    private void Load(int supplierId)
    {
        using var db = new AppDbContext();
        var supplier = db.Suppliers.Find(supplierId);
        if (supplier is null)
        {
            NameText.Text = "مورد غير موجود";
            return;
        }

        var purchases = db.Purchases
            .Where(p => p.SupplierId == supplier.Id || p.SupplierName == supplier.Name)
            .OrderByDescending(p => p.Date)
            .ToList();

        NameText.Text = supplier.Name;
        PhoneText.Text = supplier.Phone ?? "";
        BalanceText.Text = $"{supplier.Balance:N0} د.ع";
        PurchasesCountText.Text = purchases.Count.ToString();
        PurchasesTotalText.Text = $"{purchases.Sum(p => p.Total):N0} د.ع";
        LastPurchaseText.Text = purchases.Count > 0
            ? $"{purchases[0].InvoiceNumber} — {purchases[0].Date:yyyy/MM/dd HH:mm}"
            : "لا توجد مشتريات مسجلة";
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        WindowEffects.Apply(this);
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}