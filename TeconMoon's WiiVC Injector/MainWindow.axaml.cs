using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TeconMoon_s_WiiVC_Injector
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
    }
}
