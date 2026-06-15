using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Moon_WiiVC_Injector.Properties;

namespace Moon_WiiVC_Injector
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            BannersRepository.Text = Settings.Default.BannersRepository;
            OutputDir.Text = Settings.Default.OutputPathFixed;
        }

        private async void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Specify your output folder",
                AllowMultiple = false
            });

            if (result != null && result.Count > 0)
            {
                var folder = result[0];
                OutputDir.Text = folder.Path.LocalPath;
            }
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Settings.Default.BannersRepository = BannersRepository.Text ?? string.Empty;
            Settings.Default.OutputPathFixed = OutputDir.Text ?? string.Empty;

            Settings.Default.Save();
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
