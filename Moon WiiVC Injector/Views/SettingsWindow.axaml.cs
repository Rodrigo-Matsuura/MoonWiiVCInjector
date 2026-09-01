using Avalonia.Controls;
using Moon_WiiVC_Injector.Services;
using Moon_WiiVC_Injector.ViewModels;

namespace Moon_WiiVC_Injector;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        var dialogService = new AvaloniaDialogService(() => this);
        DataContext = new SettingsViewModel(dialogService, Close);
    }
}
