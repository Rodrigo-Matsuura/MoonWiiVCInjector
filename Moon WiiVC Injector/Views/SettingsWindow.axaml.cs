using System;
using System.IO;
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

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void LoadSettings()
        {
            var bannersRepo = this.FindControl<TextBox>("BannersRepository");
            if (bannersRepo != null)
                bannersRepo.Text = Settings.Default.BannersRepository;

            var outputDir = this.FindControl<TextBox>("OutputDir");
            if (outputDir != null)
                outputDir.Text = Settings.Default.OutputPathFixed;
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
                var outputDir = this.FindControl<TextBox>("OutputDir");
                if (outputDir != null)
                {
                    outputDir.Text = folder.Path.LocalPath;
                }
            }
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            var bannersRepo = this.FindControl<TextBox>("BannersRepository");
            if (bannersRepo != null)
                Settings.Default.BannersRepository = bannersRepo.Text ?? string.Empty;

            var outputDir = this.FindControl<TextBox>("OutputDir");
            if (outputDir != null)
                Settings.Default.OutputPathFixed = outputDir.Text ?? string.Empty;

            Settings.Default.Save();
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
