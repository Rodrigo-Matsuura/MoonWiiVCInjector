using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moon_WiiVC_Injector.Services;

namespace Moon_WiiVC_Injector.ViewModels;

public class OptionItem(string name, bool isChecked = false) : ObservableObject
{
    public string Name { get; } = name;

    private bool _isChecked = isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}

public partial class SdCardViewModel : ViewModelBase
{
    // ==========================================
    // 1. Services & Dependencies
    // ==========================================
    private readonly IDialogService _dialogService;

    // ==========================================
    // 2. Observable Properties (UI State)
    // ==========================================
    private ObservableCollection<string> _drives = [];
    public ObservableCollection<string> Drives
    {
        get => _drives;
        set => SetProperty(ref _drives, value);
    }

    private string? _selectedDrive;
    public string? SelectedDrive
    {
        get => _selectedDrive;
        set => SetProperty(ref _selectedDrive, value);
    }

    private int _selectedDriveIndex = -1;
    public int SelectedDriveIndex
    {
        get => _selectedDriveIndex;
        set => SetProperty(ref _selectedDriveIndex, value);
    }

    private int _memcardBlocksIndex = 0;
    public int MemcardBlocksIndex
    {
        get => _memcardBlocksIndex;
        set => SetProperty(ref _memcardBlocksIndex, value);
    }

    private int _videoForceModeIndex = 0;
    public int VideoForceModeIndex
    {
        get => _videoForceModeIndex;
        set => SetProperty(ref _videoForceModeIndex, value);
    }

    private int _videoTypeModeIndex = 0;
    public int VideoTypeModeIndex
    {
        get => _videoTypeModeIndex;
        set => SetProperty(ref _videoTypeModeIndex, value);
    }

    private int _languageIndex = 0;
    public int LanguageIndex
    {
        get => _languageIndex;
        set => SetProperty(ref _languageIndex, value);
    }

    private int _wiiUGamepadSlotIndex = 0;
    public int WiiUGamepadSlotIndex
    {
        get => _wiiUGamepadSlotIndex;
        set => SetProperty(ref _wiiUGamepadSlotIndex, value);
    }

    private double _videoWidth = 652;
    public double VideoWidth
    {
        get => _videoWidth;
        set
        {
            if (SetProperty(ref _videoWidth, value))
            {
                OnPropertyChanged(nameof(VideoWidthText));
            }
        }
    }

    private string _actionStatus = string.Empty;
    public string ActionStatus
    {
        get => _actionStatus;
        set => SetProperty(ref _actionStatus, value);
    }

    public ObservableCollection<OptionItem> Options { get; } = [];

    // ==========================================
    // 3. Computed Properties (Get-only)
    // ==========================================
    public string VideoWidthText => ((int)VideoWidth).ToString();

    // ==========================================
    // 4. Constructor
    // ==========================================
    public SdCardViewModel(IDialogService dialogService)
    {
        _dialogService = dialogService;
        InitializeOptions();
        ReloadDrives();
    }

    // ==========================================
    // 5. Commands (UI Actions)
    // ==========================================
    [RelayCommand]
    public void ReloadDrives()
    {
        try
        {
            var driveList = new List<string>();

            if (OperatingSystem.IsMacOS())
            {
                if (Directory.Exists("/Volumes"))
                {
                    foreach (string dir in Directory.GetDirectories("/Volumes"))
                    {
                        string name = Path.GetFileName(dir);
                        if (!string.Equals(name, "Macintosh HD", StringComparison.OrdinalIgnoreCase))
                        {
                            driveList.Add(dir);
                        }
                    }
                }
            }
            else
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && (d.DriveType == DriveType.Removable ||
                                (OperatingSystem.IsLinux() && (d.RootDirectory.FullName.StartsWith("/media") || d.RootDirectory.FullName.StartsWith("/run/media")))))
                    .Select(d => string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.Name : $"{d.Name} ({d.VolumeLabel})")
                    .ToList();
                driveList.AddRange(drives);
            }

            Drives = new ObservableCollection<string>(driveList);
            if (Drives.Count > 0)
            {
                SelectedDriveIndex = 0;
                SelectedDrive = Drives[0];
            }
            else
            {
                SelectedDriveIndex = -1;
                SelectedDrive = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SdCardViewModel] Failed to reload drives: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task UpdateNintendontAsync()
    {
        string? driveLetter = GetSelectedDriveLetter();
        bool driveSpecified = !string.IsNullOrEmpty(driveLetter);

        string downloadPath = Path.Combine(Path.GetTempPath(), "Moon WiiVC Injector", "SOURCETEMP", "Download");
        string tempPath = Path.Combine(downloadPath, "apps", "nintendont");
        string sdPath = driveSpecified ? Path.Combine(driveLetter!, "apps", "nintendont") : string.Empty;

        if (!await Program.CheckForInternetConnectionAsync())
        {
            var res = await _dialogService.ShowMessageAsync(
                "Your internet connection could not be verified, do you wish to try and download Nintendont anyway?",
                "Internet Connection Verification Failed",
                MessageBoxButtons.YesNo);
            if (res == MessageBoxResult.No) return;
        }

        ActionStatus = "Downloading...";
        try
        {
            await Task.Run(async () =>
            {
                Directory.CreateDirectory(tempPath);
                var client = Program.Client;
                async Task DownloadFileAsync(string url, string path)
                {
                    using var stream = await client.GetStreamAsync(url);
                    using var file = File.Create(path);
                    await stream.CopyToAsync(file);
                }

                await DownloadFileAsync("https://raw.githubusercontent.com/FIX94/Nintendont/master/loader/loader.dol", Path.Combine(tempPath, "boot.dol"));
                await DownloadFileAsync("https://raw.githubusercontent.com/FIX94/Nintendont/master/nintendont/meta.xml", Path.Combine(tempPath, "meta.xml"));
                await DownloadFileAsync("https://raw.githubusercontent.com/FIX94/Nintendont/master/nintendont/icon.png", Path.Combine(tempPath, "icon.png"));
            });
        }
        catch (Exception ex)
        {
            ActionStatus = string.Empty;
            await _dialogService.ShowMessageAsync($"Failed to download Nintendont: {ex.Message}", "Error", MessageBoxButtons.Ok);
            return;
        }
        ActionStatus = string.Empty;

        if (driveSpecified)
        {
            try
            {
                if (Directory.Exists(sdPath)) Directory.Delete(sdPath, true);
                Directory.CreateDirectory(sdPath);
                var dir = new DirectoryInfo(tempPath);
                foreach (var file in dir.GetFiles())
                {
                    var outPath = Path.Combine(sdPath, file.Name);
                    file.CopyTo(outPath, true);
                }

                await _dialogService.ShowMessageAsync("Download complete.", "Success", MessageBoxButtons.Ok);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowMessageAsync($"Failed to copy files to SD card: {ex.Message}", "Error", MessageBoxButtons.Ok);
            }
        }
        else
        {
            var dialogResult = await _dialogService.ShowMessageAsync(
                "SD Card not specified.\nDo you wish to save Nintendont somewhere else?",
                "Drive not specified",
                MessageBoxButtons.YesNo);

            if (dialogResult == MessageBoxResult.Yes)
            {
                var folder = await _dialogService.OpenFolderDialogAsync("Select folder to save Nintendont");
                if (!string.IsNullOrEmpty(folder))
                {
                    DateTime dateTime = DateTime.UtcNow.Date;
                    string zipPath = Path.Combine(folder, $"Nintendont-{dateTime:dd.MMM.yyyy}.zip");
                    if (File.Exists(zipPath)) File.Delete(zipPath);
                    ZipFile.CreateFromDirectory(downloadPath, zipPath);
                    await _dialogService.ShowMessageAsync("Download complete.", "Success", MessageBoxButtons.Ok);
                }
            }
        }
    }

    [RelayCommand]
    public async Task GenerateConfigAsync()
    {
        string? driveLetter = GetSelectedDriveLetter();
        bool driveSpecified = !string.IsNullOrEmpty(driveLetter);

        string savePath = driveSpecified ? Path.Combine(driveLetter!, "nincfg.bin") : string.Empty;

        if (!driveSpecified)
        {
            var dialogResult = await _dialogService.ShowMessageAsync(
                "SD card not specified.\nDo you wish to save the file somewhere else?",
                "Drive not specified",
                MessageBoxButtons.YesNo);

            if (dialogResult == MessageBoxResult.Yes)
            {
                var folder = await _dialogService.OpenFolderDialogAsync("Select folder to save nincfg.bin");
                if (!string.IsNullOrEmpty(folder))
                {
                    savePath = Path.Combine(folder, "nincfg.bin");
                }
                else return;
            }
            else return;
        }

        try
        {
            uint configFlags = 0;
            if (Options.Count > 0 && Options[0].IsChecked) configFlags |= 1u << 3; // NIN_CFG_MEMCARDEMU
            if (Options.Count > 1 && Options[1].IsChecked) configFlags |= 1u;      // NIN_CFG_CHEATS
            if (Options.Count > 9 && Options[9].IsChecked) configFlags |= 1u << 6; // FORCE_WIDE

            sbyte videoScale = 0;
            if (Options.Count > 7 && !Options[7].IsChecked)
            {
                videoScale = (sbyte)(VideoWidth - 600);
            }

            using var cfgFile = new BinaryWriter(File.Open(savePath, FileMode.Create));
            byte[] magicBytes = BitConverter.GetBytes(0x01070CF6u);
            byte[] version = BitConverter.GetBytes(10u);
            byte[] config = BitConverter.GetBytes(configFlags);
            byte[] videoMode = BitConverter.GetBytes((uint)VideoForceModeIndex);
            byte[] language = BitConverter.GetBytes((uint)LanguageIndex);
            byte[] maxPads = BitConverter.GetBytes(4u);
            byte[] gameID = BitConverter.GetBytes(0u);
            byte[] wiiuGamepadSlot = BitConverter.GetBytes((uint)WiiUGamepadSlotIndex);

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(magicBytes);
                Array.Reverse(version);
                Array.Reverse(config);
                Array.Reverse(videoMode);
                Array.Reverse(language);
                Array.Reverse(maxPads);
                Array.Reverse(gameID);
                Array.Reverse(wiiuGamepadSlot);
            }

            cfgFile.Write(magicBytes);
            cfgFile.Write(version);
            cfgFile.Write(config);
            cfgFile.Write(videoMode);
            cfgFile.Write(language);
            cfgFile.Write(new byte[256]); // gamePath
            cfgFile.Write(new byte[256]); // cheatPath
            cfgFile.Write(maxPads);
            cfgFile.Write(gameID);
            cfgFile.Write((byte)MemcardBlocksIndex);
            cfgFile.Write(videoScale);
            cfgFile.Write((sbyte)0); // videoOffset
            cfgFile.Write((byte)0);  // networkProfile
            cfgFile.Write(wiiuGamepadSlot);

            await _dialogService.ShowMessageAsync("Config generation complete.", "Information", MessageBoxButtons.Ok);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync($"Failed to save config: {ex.Message}", "Error", MessageBoxButtons.Ok);
        }
    }

    // ==========================================
    // 6. Private Helper Methods
    // ==========================================
    private void InitializeOptions()
    {
        string[] opts =
        [
            "Memcard Emulation", "Cheats", "Cheat Path", "Unlock Read Speed", "Wiimote CC Rumble",
            "TRI Arcade Mode", "BBA Emulation", "Auto Video Width", "Patch PAL50", "Force Widescreen",
            "Force Progressive", "Skip IPL", "OSReport", "Log"
        ];

        Options.Clear();
        for (int i = 0; i < opts.Length; i++)
        {
            bool isChecked = (i == 0 || i == 7); // Default Memcard Emulation & Auto Video Width
            Options.Add(new OptionItem(opts[i], isChecked));
        }
    }

    private string? GetSelectedDriveLetter()
    {
        if (string.IsNullOrWhiteSpace(SelectedDrive)) return null;

        if (OperatingSystem.IsWindows())
        {
            if (SelectedDrive.Length >= 3 && SelectedDrive[1] == ':' && (SelectedDrive[2] == '\\' || SelectedDrive[2] == '/'))
            {
                return SelectedDrive.Substring(0, 3);
            }
            return SelectedDrive;
        }

        int parenIdx = SelectedDrive.IndexOf(" (");
        string path = parenIdx > 0 ? SelectedDrive.Substring(0, parenIdx).Trim() : SelectedDrive.Trim();
        return Directory.Exists(path) ? path : null;
    }
}
