using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using PhoneAccounting.App.Data;
using PhoneAccounting.App.Models;

namespace PhoneAccounting.App.Services.Sync;

/// <summary>
/// وحدة المصالحة (P3.3): كشفُ التفاوت ماليٌّ بـ FinancialTotals (كشف بلا إصلاح، لا كتابة)،
/// والاستدراك = عملية موقّعة جديدة StockCorrection عبر OperationWriter (OriginDevice+OriginSeq)
/// تصدر محلياً وتنتشر عبر الدلتا تطبيقاً idempotent — لا يُعاد كتابة التاريخ أبداً،
/// وأي استدراك قابل للعكس باستدراك معاكس موقّع من أي جهة مصرّحة (ثم تتقارب الأجهزة، لا بالحذف).
/// </summary>
public static class ReconciliationService
{
    /// <summary>الاستدراك يقصره المدير/المحاسب (نفس بوابة عمليات التصحيح في المرحلة 1).</summary>
    public static bool CanCorrect => Session.CurrentUser?.Role is UserRole.Admin or UserRole.Accountant;

    /// <summary>مقارنة المجموع المالي بين قاعدة محلية وقاعدة قريب — كشف فقط، لا تصحيح ولا كتابة.</summary>
    public static (bool Equal, decimal Diff) VerifyAgainst(AppDbContext local, AppDbContext peer)
    {
        var (equal, d) = FinancialTotals.Compare(
            FinancialTotals.CanonicalSum(local),
            FinancialTotals.CanonicalSum(peer));
        return (equal, d);
    }

    /// <summary>
    /// إصدار استدراك موقّع بمبلغ صريح موجَّه (موجب للتعويض، سالب للعكس) — يحمل هوية الجهة
    /// المصدِرة ومتى المزامنة ينتشر للبقية؛ لا يعدّل أي صف سابق إطلاقاً.
    /// </summary>
    public static string IssueCorrection(AppDbContext db, decimal amount, string reason)
    {
        if (!CanCorrect)
            return "مرفوض: الاستدراك يقصره المدير/المحاسب";
        if (amount == 0m)
            return "لا يُقبل استدراك صفري";

        OperationWriter.Register(
            db,
            OperationType.StockCorrection,
            documentNumber: null,
            amount,
            summaryJson: System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["kind"] = "reconcile",
                ["reason"] = reason,
                ["by"] = Session.CurrentUser?.DisplayName,
                ["device"] = Session.DeviceId
            }));

        db.SaveChanges();

        return $"سُجّل استدراك موقّع بمقدار {amount:N2} — {reason}";
    }
}