using System.Windows;
using System.Windows.Input;
using PhoneAccounting.App.Infrastructure;

namespace PhoneAccounting.App.Views;

public partial class InputDialog : Window
{
    public string Value => InputValue.Text.Trim();

    public InputDialog(string title, string prompt, string initialValue)
    {
        InitializeComponent();
        TitleText.Text = title;
        PromptText.Text = prompt;
        InputValue.Text = initialValue;
        Loaded += (_, _) =>
        {
            InputValue.SelectAll();
            InputValue.Focus();
        };
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        WindowEffects.Apply(this);
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void InputValue_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Ok_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            Cancel_Click(sender, e);
        }
    }
}