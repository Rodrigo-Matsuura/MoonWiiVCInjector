using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Moon_WiiVC_Injector;

public partial class MessageBoxWindow : Window
{
    public MessageBoxWindow()
    {
        InitializeComponent();
    }

    public MessageBoxWindow(string text, string title, MessageBoxButtons buttons)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = text;

        if (buttons == MessageBoxButtons.Ok)
        {
            OkButton.IsVisible = true;
        }
        else if (buttons == MessageBoxButtons.YesNo)
        {
            YesButton.IsVisible = true;
            NoButton.IsVisible = true;
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        Close(MessageBoxResult.Ok);
    }

    private void OnYesClick(object sender, RoutedEventArgs e)
    {
        Close(MessageBoxResult.Yes);
    }

    private void OnNoClick(object sender, RoutedEventArgs e)
    {
        Close(MessageBoxResult.No);
    }

    public static Task<MessageBoxResult> Show(Window parent, string text, string title, MessageBoxButtons buttons)
    {
        var msgBox = new MessageBoxWindow(text, title, buttons);
        return msgBox.ShowDialog<MessageBoxResult>(parent);
    }
}

public enum MessageBoxResult
{
    Ok,
    Yes,
    No
}

public enum MessageBoxButtons
{
    Ok,
    YesNo
}
