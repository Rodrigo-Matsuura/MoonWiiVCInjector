using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Moon_WiiVC_Injector
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnSettingsClick(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var settingsWin = new SettingsWindow();
            settingsWin.ShowDialog(this);
        }
    }
}
