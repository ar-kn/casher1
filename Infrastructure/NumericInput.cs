using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PhoneAccounting.App.Infrastructure;

/// <summary>
/// يمنع إدخال أي حرف غير رقمي في حقل النص.
/// استخدام: inf:NumericInput.IsNumeric="True" (أعداد صحيحة)
/// أو inf:NumericInput.IsDecimal="True" (أرقام عشرية)
/// </summary>
public static class NumericInput
{
    public static readonly DependencyProperty IsNumericProperty =
        DependencyProperty.RegisterAttached(
            "IsNumeric", typeof(bool), typeof(NumericInput),
            new PropertyMetadata(false, OnIsNumericChanged));

    public static readonly DependencyProperty IsDecimalProperty =
        DependencyProperty.RegisterAttached(
            "IsDecimal", typeof(bool), typeof(NumericInput),
            new PropertyMetadata(false, OnIsDecimalChanged));

    public static bool GetIsNumeric(DependencyObject obj) => (bool)obj.GetValue(IsNumericProperty);
    public static void SetIsNumeric(DependencyObject obj, bool value) => obj.SetValue(IsNumericProperty, value);

    public static bool GetIsDecimal(DependencyObject obj) => (bool)obj.GetValue(IsDecimalProperty);
    public static void SetIsDecimal(DependencyObject obj, bool value) => obj.SetValue(IsDecimalProperty, value);

    private static void OnIsNumericChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;
        if ((bool)e.NewValue)
        {
            box.PreviewTextInput -= BlockNonNumeric;
            box.PreviewTextInput += BlockNonNumeric;
            DataObject.RemovePastingHandler(box, BlockPasteInteger);
            DataObject.AddPastingHandler(box, BlockPasteInteger);
        }
    }

    private static void OnIsDecimalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;
        if ((bool)e.NewValue)
        {
            box.PreviewTextInput -= BlockNonDecimal;
            box.PreviewTextInput += BlockNonDecimal;
            DataObject.RemovePastingHandler(box, BlockPasteDecimal);
            DataObject.AddPastingHandler(box, BlockPasteDecimal);
        }
    }

    private static void BlockNonNumeric(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !IsIntegerInput(sender, e.Text);
    }

    private static void BlockNonDecimal(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !IsDecimalInput(sender, e.Text);
    }

    private static bool IsIntegerInput(object sender, string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var ch in text)
            if (!char.IsDigit(ch))
                return false;
        return true;
    }

    private static bool IsDecimalInput(object sender, string text)
    {
        if (text == "." || text == "،")
        {
            var box = (TextBox)sender;
            return !box.Text.Contains('.') && !box.Text.Contains('،');
        }

        foreach (var ch in text)
        {
            if (char.IsDigit(ch)) continue;
            if (ch is '.' or '،')
            {
                var box = (TextBox)sender;
                if (box.Text.Contains('.') || box.Text.Contains('،')) return false;
                continue;
            }
            return false;
        }
        return true;
    }

    private static void BlockPasteInteger(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(typeof(string))) { e.CancelCommand(); return; }
        var text = (string)e.DataObject.GetData(typeof(string));
        if (!IsIntegerInput(sender, text)) e.CancelCommand();
    }

    private static void BlockPasteDecimal(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(typeof(string))) { e.CancelCommand(); return; }
        var text = (string)e.DataObject.GetData(typeof(string));
        if (!IsDecimalInput(sender, text)) e.CancelCommand();
    }
}
