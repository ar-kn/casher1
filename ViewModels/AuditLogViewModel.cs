using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;
using System.Collections.ObjectModel;

namespace PhoneAccounting.App.ViewModels;

public partial class AuditLogRow
{
    public long Seq { get; init; }
    public required string TypeText { get; init; }
    public string? DocumentNumber { get; init; }
    public decimal Amount { get; init; }
    public required string RoleText { get; init; }
    public required string DeviceText { get; init; }
    public required string When { get; init; }
}

/// <summary>عرض سجل العمليات (البند 8.4) — آخر 50 عملية في الجهاز.</summary>
public partial class AuditLogViewModel : ObservableObject
{
    public ObservableCollection<AuditLogRow> Entries { get; } = [];

    public AuditLogViewModel()
    {
        using var db = new AppDbContext();
        var rows = db.OperationLogs.AsNoTracking()
            .OrderByDescending(o => o.Seq)
            .Take(50)
            .ToList();

        foreach (var o in rows)
        {
            Entries.Add(new AuditLogRow
            {
                Seq = o.Seq,
                TypeText = TypeLabel(o.OpType),
                DocumentNumber = o.DocumentNumber,
                Amount = o.Amount,
                RoleText = RoleLabel(o.UserRole),
                DeviceText = ProductsViewModel.DeviceTitleFor(o.DeviceRole),
                When = o.CommittedAt.ToLocalTime().ToString("dd-MM-yyyy HH:mm")
            });
        }
    }

    public static string TypeLabel(string op) => op switch
    {
        nameof(OperationType.Sale) => "بيع",
        nameof(OperationType.SaleEdit) => "تعديل بيع",
        nameof(OperationType.SaleVoid) => "إلغاء فاتورة",
        nameof(OperationType.Purchase) => "شراء",
        nameof(OperationType.PurchaseEdit) => "تعديل شراء",
        nameof(OperationType.PurchaseReturn) => "مرتجع شراء",
        nameof(OperationType.VoucherIn) => "سند قبض",
        nameof(OperationType.RepairJob) => "أمر صيانة",
        nameof(OperationType.RepairPartUsage) => "قطع صيانة",
        nameof(OperationType.StockAdjustment) => "تسوية مخزون",
        nameof(OperationType.CloseDay) => "إقفال اليوم",
        _ => op
    };

    private static string RoleLabel(UserRole role) => role switch
    {
        UserRole.Admin => "مدير",
        UserRole.Accountant => "محاسب",
        UserRole.Cashier => "كاشير",
        UserRole.RepairTech => "فني صيانة",
        UserRole.Inventory => "مخزن",
        _ => role.ToString()
    };
}