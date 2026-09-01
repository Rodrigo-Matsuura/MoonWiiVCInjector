using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moon_WiiVC_Injector.Properties;
using Moon_WiiVC_Injector.Services;

namespace Moon_WiiVC_Injector.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly Action _closeAction;

    [ObservableProperty]
    private string _bannersRepository = string.Empty;

    [ObservableProperty]
    private string _outputDir = string.Empty;

    public SettingsViewModel(IDialogService dialogService, Action closeAction)
    {
        _dialogService = dialogService;
        _closeAction = closeAction;
        LoadSettings();
    }

    private void LoadSettings()
    {
        BannersRepository = Settings.Default.BannersRepository;
        OutputDir = Settings.Default.OutputPathFixed;
    }

    [RelayCommand]
    private async Task BrowseOutputFolderAsync()
    {
        var path = await _dialogService.OpenFolderDialogAsync("Specify your output folder");
        if (!string.IsNullOrEmpty(path))
        {
            OutputDir = path;
        }
    }

    [RelayCommand]
    private void Save()
    {
        Settings.Default.BannersRepository = BannersRepository ?? string.Empty;
        Settings.Default.OutputPathFixed = OutputDir ?? string.Empty;
        Settings.Default.Save();
        _closeAction();
    }

    [RelayCommand]
    private void Cancel()
    {
        _closeAction();
    }
}
