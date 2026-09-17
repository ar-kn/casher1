using System.Windows.Controls;

namespace PhoneAccounting.App.Views;

public partial class PlaceholderView : UserControl
{
    public PlaceholderView(string title, string message)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
    }
}
