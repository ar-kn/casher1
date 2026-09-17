using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PhoneAccounting.App.Models;
using PhoneAccounting.App.ViewModels;

namespace PhoneAccounting.App.Infrastructure;

/// <summary>يطبع تقارير نصية/جدولية عبر حوار الطباعة القياسي.</summary>
public static class ReportPrinter
{
    public static void PrintInventoryReport(
        string title,
        string subtitle,
        string summary,
        IReadOnlyList<InventoryRow> rows)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            FlowDirection = FlowDirection.RightToLeft,
            PagePadding = new Thickness(48, 36, 48, 36),
            ColumnWidth = double.PositiveInfinity
        };

        doc.Blocks.Add(new Paragraph(new Run(title))
        {
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center
        });

        doc.Blocks.Add(new Paragraph(new Run(subtitle))
        {
            FontSize = 12,
            Foreground = Brushes.Gray,
            TextAlignment = TextAlignment.Center
        });

        doc.Blocks.Add(new Paragraph(new Run(summary))
        {
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 8)
        });

        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 8, 0, 8) };
        table.Columns.Add(new TableColumn { Width = new GridLength(38) });
        table.Columns.Add(new TableColumn { Width = new GridLength(90) });
        table.Columns.Add(new TableColumn { Width = new GridLength(220) });
        table.Columns.Add(new TableColumn { Width = new GridLength(130) });
        table.Columns.Add(new TableColumn { Width = new GridLength(110) });
        table.Columns.Add(new TableColumn { Width = new GridLength(90) });
        table.Columns.Add(new TableColumn { Width = new GridLength(90) });
        table.Columns.Add(new TableColumn { Width = new GridLength(90) });

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        var header = new TableRow();
        header.Cells.Add(MakeCell("رقم", bold: true));
        header.Cells.Add(MakeCell("الباركود", bold: true));
        header.Cells.Add(MakeCell("اسم المنتج", bold: true));
        header.Cells.Add(MakeCell("الجهاز", bold: true));
        header.Cells.Add(MakeCell("سعر الشراء", bold: true));
        header.Cells.Add(MakeCell("سعر البيع", bold: true));
        header.Cells.Add(MakeCell("الكمية", bold: true));
        header.Cells.Add(MakeCell("القيمة", bold: true));
        group.Rows.Add(header);

        var index = 1;
        foreach (var row in rows)
        {
            var line = new TableRow();
            line.Cells.Add(MakeCell(index.ToString()));
            line.Cells.Add(MakeCell(row.Barcode ?? ""));
            line.Cells.Add(MakeCell(row.Name));
            line.Cells.Add(MakeCell(row.DeviceName));
            line.Cells.Add(MakeCell(row.BuyPrice.ToString("N0")));
            line.Cells.Add(MakeCell(row.SellPrice.ToString("N0")));
            line.Cells.Add(MakeCell(row.Stock.ToString()));
            line.Cells.Add(MakeCell(row.Value.ToString("N0")));
            group.Rows.Add(line);
            index++;
        }

        doc.Blocks.Add(table);

        doc.Blocks.Add(new Paragraph(new Run("ملاحظات:"))
        {
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 12, 0, 0)
        });

        doc.Blocks.Add(new Paragraph(new Run(
            "إمضاء المسؤول: ______________________              التاريخ: ____ / ____ / ________"))
        {
            FontSize = 12,
            Margin = new Thickness(0, 24, 0, 0)
        });

        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true)
        {
            var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
            dialog.PrintDocument(paginator, "تقرير الجرد");
        }
    }

    /// <summary>يطبع فاتورة بيع (إيصال) للمنتجات المباعة.</summary>
    public static void PrintSaleReceipt(string companyName, Sale sale)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            FlowDirection = FlowDirection.RightToLeft,
            PagePadding = new Thickness(48, 36, 48, 36),
            ColumnWidth = double.PositiveInfinity
        };

        doc.Blocks.Add(new Paragraph(new Run(companyName))
        {
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center
        });

        doc.Blocks.Add(new Paragraph(new Run($"فاتورة بيع رقم: {sale.InvoiceNumber}"))
        {
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center
        });

        doc.Blocks.Add(new Paragraph(new Run(
            $"التاريخ: {sale.Date:yyyy-MM-dd HH:mm}    العميل: {sale.Customer?.Name ?? "عميل نقدي"}"))
        {
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 8)
        });

        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 8, 0, 8) };
        table.Columns.Add(new TableColumn { Width = new GridLength(40) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(70) });
        table.Columns.Add(new TableColumn { Width = new GridLength(95) });
        table.Columns.Add(new TableColumn { Width = new GridLength(95) });

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        var header = new TableRow();
        header.Cells.Add(MakeCell("الرقم", true));
        header.Cells.Add(MakeCell("المنتج", true));
        header.Cells.Add(MakeCell("الكمية", true));
        header.Cells.Add(MakeCell("سعر الوحدة", true));
        header.Cells.Add(MakeCell("الإجمالي", true));
        group.Rows.Add(header);

        var index = 1;
        foreach (var it in sale.Items)
        {
            var name = it.ProductName + (string.IsNullOrWhiteSpace(it.Imei) ? "" : $" (IMEI: {it.Imei})");
            var line = new TableRow();
            line.Cells.Add(MakeCell(index.ToString()));
            line.Cells.Add(MakeCell(name));
            line.Cells.Add(MakeCell(it.Quantity.ToString()));
            line.Cells.Add(MakeCell(it.UnitPrice.ToString("N0")));
            line.Cells.Add(MakeCell((it.Quantity * it.UnitPrice).ToString("N0")));
            group.Rows.Add(line);
            index++;
        }

        doc.Blocks.Add(table);

        if (sale.Discount > 0)
        {
            doc.Blocks.Add(new Paragraph(new Run($"الخصم: {sale.Discount:N0} د.ع"))
            {
                FontSize = 12,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        doc.Blocks.Add(new Paragraph(new Run($"الإجمالي: {sale.Total:N0} د.ع"))
        {
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0)
        });

        doc.Blocks.Add(new Paragraph(new Run($"طريقة الدفع: {PaymentMethodName(sale.PaymentMethod)}"))
        {
            FontSize = 12,
            TextAlignment = TextAlignment.Right
        });

        doc.Blocks.Add(new Paragraph(new Run("شكراً لتعاملكم معنا"))
        {
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0)
        });

        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true)
        {
            var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
            dialog.PrintDocument(paginator, "فاتورة بيع " + sale.InvoiceNumber);
        }
    }

    private static string PaymentMethodName(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "نقدي",
        PaymentMethod.Card => "بطاقة",
        PaymentMethod.Credit => "آجل",
        _ => method.ToString()
    };

    private static TableCell MakeCell(string text, bool bold = false)
    {
        var paragraph = new Paragraph(new Run(text))
        {
            FontSize = 11,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Margin = new Thickness(4, 2, 4, 2),
            TextAlignment = TextAlignment.Right
        };

        return new TableCell(paragraph)
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xED)),
            BorderThickness = new Thickness(0.75),
            Padding = new Thickness(4)
        };
    }
}
