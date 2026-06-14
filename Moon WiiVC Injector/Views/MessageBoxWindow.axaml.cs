using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Moon_WiiVC_Injector
{
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
            var messageText = this.FindControl<SelectableTextBlock>("MessageText");
            if (messageText != null)
                messageText.Text = text;

            var okButton = this.FindControl<Button>("OkButton");
            var yesButton = this.FindControl<Button>("YesButton");
            var noButton = this.FindControl<Button>("NoButton");

            if (buttons == MessageBoxButtons.Ok)
            {
                if (okButton != null) okButton.IsVisible = true;
            }
            else if (buttons == MessageBoxButtons.YesNo)
            {
                if (yesButton != null) yesButton.IsVisible = true;
                if (noButton != null) noButton.IsVisible = true;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
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
}
