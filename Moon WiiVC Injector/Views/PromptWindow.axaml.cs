using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Moon_WiiVC_Injector;

public partial class PromptWindow : Window
{
    public string Result { get; private set; } = string.Empty;

    public PromptWindow()
    {
        InitializeComponent();
    }

    public PromptWindow(string text, string title, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = text;
        InputTextBox.Text = defaultValue;
        InputTextBox.Focus();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        Result = InputTextBox.Text ?? string.Empty;
        Close(true);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = string.Empty;
        Close(false);
    }
}
