using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Infrastructure;
using PhoneAccounting.App.Models;
using System.Collections.ObjectModel;
using System.Linq;

namespace PhoneAccounting.App.ViewModels;

public partial class InventoryRow : ObservableObject
{
    public int Index { get; init; }
    public int Id { get; init; }
    public required string Name { get; init; }
    public string? Barcode { get; init; }
    public decimal BuyPrice { get; init; }
    public decimal SellPrice { get; init; }
    public int Stock { get; init; }
    public int MinStock { get; init; }
    public required string DeviceName { get; init; }
    public int CategoryId { get; init; }
    public required string CategoryName { get; init; }

    public bool IsLow { get => Stock <= MinStock; set { } }
    public bool IsOutOfStock { get => Stock <= 0; set { } }
    public decimal Value { get => BuyPrice * Stock; set { } }
}

public partial class InventoryViewModel : ObservableObject
{
    public ObservableCollection<InventoryRow> Rows { get; } = [];

    [ObservableProperty]
    private string searchText = "";

    [ObservableProperty]
    private int totalProducts;

    [ObservableProperty]
    private int totalUnits;

    [ObservableProperty]
    private decimal totalValue;

    [ObservableProperty]
    private int lowStockCount;

    [ObservableProperty]
    private int outOfStockCount;

    public InventoryViewModel()
    {
        Load();
    }

    private void Load()
    {
        Rows.Clear();

        using var db = new AppDbContext();
        var query = db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(p =>
                p.Name.Contains(term) ||
                (p.EnglishName != null && p.EnglishName.Contains(term)) ||
                (p.Barcode != null && p.Barcode.Contains(term)) ||
                (p.Imei != null && p.Imei.Contains(term)));
        }

        var list = query.OrderBy(p => p.Name).ToList();

        var index = 0;
        foreach (var p in list)
        {
            Rows.Add(new InventoryRow
            {
                Index = ++index,
                Id = p.Id,
                Name = p.Name,
                Barcode = p.Barcode,
                BuyPrice = p.BuyPrice,
                SellPrice = p.SellPrice,
                Stock = p.Stock,
                MinStock = p.MinStock,
                DeviceName = ProductsViewModel.DeviceTitleFor(p.Device),
                CategoryId = p.CategoryId,
                CategoryName = p.Category?.Name ?? ""
            });
        }

        TotalProducts = Rows.Count;
        TotalUnits = Rows.Sum(r => r.Stock);
        TotalValue = Rows.Sum(r => r.Value);
        LowStockCount = Rows.Count(r => r.IsLow);
        OutOfStockCount = Rows.Count(r => r.IsOutOfStock);
    }

    partial void OnSearchTextChanged(string value) => Load();

    [RelayCommand]
    private void Refresh() => Load();

    [RelayCommand]
    private void Print()
    {
        var summary = $"عدد الأصناف: {TotalProducts:N0}    إجمالي القطع: {TotalUnits:N0}    " +
                      $"قيمة الجرد (تكلفة): {TotalValue:N0} د.ل    أصناف منخفضة: {LowStockCount}    " +
                      $"نافدة: {OutOfStockCount}";

        ReportPrinter.PrintInventoryReport(
            "تقرير جرد المخزون",
            $"تاريخ الإصدار: {DateTime.Now:yyyy/MM/dd HH:mm}",
            summary,
            Rows.ToList());
    }
}
