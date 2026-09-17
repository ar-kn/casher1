using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;

namespace PhoneAccounting.App.Services.Sync;

/// <summary>
/// البند 15.5 (M2.3) — الجمع المالي بترتيب توافقي:
/// • المجموع يُحسب بمرور واحد ORDER BY Seq (نفس الترتيب على كل الأجهزة) ⇒ نفس النتيجة.
/// • مقارنة جهازين بتسامح 0.01 (فروق تمثيل decimal لا تُعدّ خرقاً).
/// • التنبيه والتحمّل كشفٌ فقط (seçedo لا تصحيح تلقائي) — تُسجَّل الفروق ولا تُصلَح.
/// </summary>
public static class FinancialTotals
{
    public const decimal CompareTolerance = 0.01m;

    /// <summary>مجموع كل العمليات بترتيب Seq التصاعدي — يعطي نفس الرقم على كل الأجهزة المتطابقة.</summary>
    public static decimal CanonicalSum(AppDbContext db)
        => db.OperationLogs
            .AsNoTracking()
            .OrderBy(o => o.Seq)
            .Select(o => (decimal)o.Amount)
            .AsEnumerable()
            .Aggregate(0m, (acc, amount) => acc + amount);

    /// <summary>مجموع عمليات نوع محدد (مثل المبيعات) بترتيب Seq — للمقارنات الفرعية.</summary>
    public static decimal CanonicalSumOf(AppDbContext db, OperationType type)
        => db.OperationLogs
            .AsNoTracking()
            .Where(o => o.OpType == type.ToString())
            .OrderBy(o => o.Seq)
            .Select(o => (decimal)o.Amount)
            .AsEnumerable()
            .Aggregate(0m, (acc, amount) => acc + amount);

    /// <summary>مقارنة بتحمّل محدد (افتراضياً 0.01) — تُرجع الفرق دون تصحيح.</summary>
    public static (bool Equal, decimal Diff) Compare(decimal a, decimal b, decimal tolerance = CompareTolerance)
    {
        var diff = Math.Abs(a - b);
        return (diff <= tolerance, diff);
    }

    /// <summary>تحقق شامل لجهازين — يُسجّل التباين ويُعيد نتيجة منطقية (كشف لا إصلاح).</summary>
    public static bool Verify(AppDbContext a, AppDbContext b, out decimal diff)
    {
        var (equal, d) = Compare(CanonicalSum(a), CanonicalSum(b));
        diff = d;
        if (!equal)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "PhoneAccounting", "sync-financial.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] تباين المجموع المالي بين جهازين: {diff:0.00}\n");
            }
            catch { }
        }
        return equal;
    }
}