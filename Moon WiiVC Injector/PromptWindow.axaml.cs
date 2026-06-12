using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Moon_WiiVC_Injector
{
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
            var promptText = this.FindControl<TextBlock>("PromptText");
            if (promptText != null)
                promptText.Text = text;

            var inputTextBox = this.FindControl<TextBox>("InputTextBox");
            if (inputTextBox != null)
            {
                inputTextBox.Text = defaultValue;
                inputTextBox.Focus();
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            var inputTextBox = this.FindControl<TextBox>("InputTextBox");
            Result = inputTextBox?.Text ?? string.Empty;
            Close(true);
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Result = string.Empty;
            Close(false);
        }
    }
}
