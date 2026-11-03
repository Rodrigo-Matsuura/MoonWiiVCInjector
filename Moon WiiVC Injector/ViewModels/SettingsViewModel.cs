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

    public string BannersRepository
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string OutputDir
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string TempDir
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

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
        TempDir = Settings.Default.TempPath;
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
    private async Task BrowseTempFolderAsync()
    {
        var path = await _dialogService.OpenFolderDialogAsync("Specify your temporary build folder");
        if (!string.IsNullOrEmpty(path))
        {
            TempDir = path;
        }
    }

    [RelayCommand]
    private void Save()
    {
        Settings.Default.BannersRepository = BannersRepository ?? string.Empty;
        Settings.Default.OutputPathFixed = OutputDir ?? string.Empty;
        Settings.Default.TempPath = TempDir ?? string.Empty;
        Settings.Default.Save();
        _closeAction();
    }

    [RelayCommand]
    private void Cancel()
    {
        _closeAction();
    }
}
