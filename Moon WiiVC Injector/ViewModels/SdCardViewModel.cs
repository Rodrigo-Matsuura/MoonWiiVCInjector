using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moon_WiiVC_Injector.Properties;
using Moon_WiiVC_Injector.Services;

namespace Moon_WiiVC_Injector.ViewModels;

internal sealed class OptionItem(string name, bool isChecked = false, Action? onChanged = null) : ObservableObject
{
    private readonly Action? _onChanged = onChanged;

    public string Name { get; } = name;

    public bool IsChecked
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                _onChanged?.Invoke();
            }
        }
    } = isChecked;
}

internal sealed partial class SdCardViewModel : ViewModelBase
{
    [Flags]
    private enum NinConfigFlags : uint
    {
        None = 0,
        Cheats = 1u << 0,
        Debugger = 1u << 1,
        DebugWait = 1u << 2,
        MemcardEmu = 1u << 3,
        CheatPath = 1u << 4,
        ForceWide = 1u << 5,
        ForceProg = 1u << 6,
        AutoBoot = 1u << 7,
        RemLimit = 1u << 8,
        OsReport = 1u << 9,
        Usb = 1u << 10,
        Led = 1u << 11,
        Log = 1u << 12,
        McMulti = 1u << 13,
        NativeSi = 1u << 14,
        WiiUWide = 1u << 15,
        ArcadeMode = 1u << 16,
        CcRumble = 1u << 17,
        SkipIpl = 1u << 18,
        BbaEmu = 1u << 19
    }

    [Flags]
    private enum NinVideoModeFlags : uint
    {
        Auto = 0u << 16,
        Force = 1u << 16,
        None = 2u << 16,
        ForceDeflicker = 4u << 16,

        ForcePal50 = 1u << 0,
        ForcePal60 = 1u << 1,
        ForceNtsc = 1u << 2,
        ForceMpal = 1u << 3,

        Prog = 1u << 4,
        PatchPal50 = 1u << 5
    }

    private readonly IDialogService _dialogService;

    public ObservableCollection<string> Drives
    {
        get;
        set => SetProperty(ref field, value);
    } = [];

    public string? SelectedDrive
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int SelectedDriveIndex
    {
        get;
        set => SetProperty(ref field, value);
    } = -1;

    public int MemcardBlocksIndex
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool MemcardMulti
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int VideoForceModeIndex
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                if (value is 0 or 3)
                {
                    VideoTypeModeIndex = 0;
                }
                else if (VideoTypeModeIndex == 0)
                {
                    VideoTypeModeIndex = 1;
                }
                OnPropertyChanged(nameof(IsVideoTypeEnabled));
            }
        }
    }

    public int VideoTypeModeIndex
    {
        get;
        set
        {
            int adjusted = (VideoForceModeIndex is not (0 or 3) && value == 0) ? 1 : value;
            SetProperty(ref field, adjusted);
        }
    }

    public int LanguageIndex
    {
        get;
        set => SetProperty(ref field, value);
    }

    public int WiiUGamepadSlotIndex
    {
        get;
        set => SetProperty(ref field, value);
    }

    public double VideoWidth
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(VideoWidthText));
            }
        }
    } = 652;

    public string ActionStatus
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public ObservableCollection<OptionItem> Options { get; } = [];

    public bool IsMemcardEnabled => Options.Count > 0 && Options[0].IsChecked;
    public bool IsCustomVideoWidthEnabled => Options.Count > 7 && !Options[7].IsChecked;
    public bool IsVideoTypeEnabled => VideoForceModeIndex is not (0 or 3);
    public string VideoWidthText => IsCustomVideoWidthEnabled ? ((int)VideoWidth).ToString() : "Auto";

    public SdCardViewModel(IDialogService dialogService)
    {
        _dialogService = dialogService;
        InitializeOptions();
        ReloadDrives();
    }

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
            AppLogger.Warn($"[SdCardViewModel] Failed to reload drives: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task UpdateNintendontAsync()
    {
        string? driveLetter = GetSelectedDriveLetter();
        bool driveSpecified = !string.IsNullOrEmpty(driveLetter);

        string downloadPath = Path.Combine(Settings.Default.GetEffectiveTempPath(), "SOURCETEMP", "Download");
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
                    using var stream = await client.GetStreamAsync(new Uri(url));
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
                    await Task.Run(() => ZipFile.CreateFromDirectory(downloadPath, zipPath));
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
            NinConfigFlags configFlags = NinConfigFlags.None;
            NinVideoModeFlags videoFlags = NinVideoModeFlags.Prog;

            if (IsOptionChecked(0)) configFlags |= NinConfigFlags.MemcardEmu;
            if (IsOptionChecked(1)) configFlags |= NinConfigFlags.Cheats;
            if (IsOptionChecked(2)) configFlags |= NinConfigFlags.CheatPath;
            if (IsOptionChecked(3)) configFlags |= NinConfigFlags.RemLimit;
            if (IsOptionChecked(4)) configFlags |= NinConfigFlags.CcRumble;
            if (IsOptionChecked(5)) configFlags |= NinConfigFlags.ArcadeMode;
            if (IsOptionChecked(6)) configFlags |= NinConfigFlags.BbaEmu;

            if (IsOptionChecked(8))
            {
                videoFlags |= NinVideoModeFlags.PatchPal50;
                if (VideoTypeModeIndex is 0 or 3)
                {
                    videoFlags |= NinVideoModeFlags.ForcePal50;
                }
            }

            if (IsOptionChecked(9))
            {
                configFlags |= NinConfigFlags.ForceWide | NinConfigFlags.WiiUWide;
            }

            if (IsOptionChecked(10)) configFlags |= NinConfigFlags.ForceProg;
            if (IsOptionChecked(11)) configFlags |= NinConfigFlags.SkipIpl;
            if (IsOptionChecked(12)) configFlags |= NinConfigFlags.OsReport;
            if (IsOptionChecked(13)) configFlags |= NinConfigFlags.Log;

            if (MemcardMulti)
            {
                configFlags |= NinConfigFlags.McMulti;
            }

            videoFlags |= VideoForceModeIndex switch
            {
                1 => NinVideoModeFlags.Force,
                2 => NinVideoModeFlags.ForceDeflicker,
                3 => NinVideoModeFlags.None,
                _ => NinVideoModeFlags.Auto
            };

            videoFlags |= VideoTypeModeIndex switch
            {
                1 => NinVideoModeFlags.ForceNtsc,
                2 => NinVideoModeFlags.ForceMpal,
                3 => NinVideoModeFlags.ForcePal50,
                4 => NinVideoModeFlags.ForcePal60,
                _ => 0
            };

            uint languageValue = LanguageIndex == 0 ? 0xFFFFFFFFu : (uint)(LanguageIndex - 1);
            sbyte videoScale = IsOptionChecked(7) ? (sbyte)0 : (sbyte)(VideoWidth - 600);

            // Nintendont ConfigFile v10 binary layout (540 bytes, Big-Endian fields)
            byte[] buffer = new byte[540];
            Span<byte> span = buffer.AsSpan();

            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), 0x01070CF6u); // magicBytes
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4, 4), 10u);         // version
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8, 4), (uint)configFlags);
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(12, 4), (uint)videoFlags);
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(16, 4), languageValue);
            // [20..275]: gamePath (256 zero bytes)
            // [276..531]: cheatPath (256 zero bytes)
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(532, 4), 4u);        // maxPads
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(536, 4), 0u);        // gameID

            await using var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await fs.WriteAsync(buffer);

            byte[] tail = new byte[8];
            tail[0] = (byte)MemcardBlocksIndex;
            tail[1] = unchecked((byte)videoScale);
            tail[2] = 0; // videoOffset
            tail[3] = 0; // networkProfile
            BinaryPrimitives.WriteUInt32BigEndian(tail.AsSpan(4, 4), (uint)WiiUGamepadSlotIndex);
            await fs.WriteAsync(tail);

            await _dialogService.ShowMessageAsync("Config generation complete.", "Information", MessageBoxButtons.Ok);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync($"Failed to save config: {ex.Message}", "Error", MessageBoxButtons.Ok);
        }
    }

    private bool IsOptionChecked(int index) => index >= 0 && index < Options.Count && Options[index].IsChecked;

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
            bool isChecked = i is 0 or 7;
            Action? callback = i switch
            {
                0 => () => OnPropertyChanged(nameof(IsMemcardEnabled)),
                7 => () =>
                {
                    OnPropertyChanged(nameof(IsCustomVideoWidthEnabled));
                    OnPropertyChanged(nameof(VideoWidthText));
                },
                _ => null
            };
            Options.Add(new OptionItem(opts[i], isChecked, callback));
        }
    }

    private string? GetSelectedDriveLetter()
    {
        if (string.IsNullOrWhiteSpace(SelectedDrive)) return null;

        if (OperatingSystem.IsWindows())
        {
            if (SelectedDrive.Length >= 3 && SelectedDrive[1] == ':' && (SelectedDrive[2] == '\\' || SelectedDrive[2] == '/'))
            {
                return SelectedDrive[..3];
            }
            return SelectedDrive;
        }

        int parenIdx = SelectedDrive.IndexOf(" (", StringComparison.Ordinal);
        string path = parenIdx > 0 ? SelectedDrive[..parenIdx].Trim() : SelectedDrive.Trim();
        return Directory.Exists(path) ? path : null;
    }
}
