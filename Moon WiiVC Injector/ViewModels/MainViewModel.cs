using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moon_WiiVC_Injector.Properties;
using Moon_WiiVC_Injector.Services;

namespace Moon_WiiVC_Injector.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private const long WiiGameType = 2745048157;
    private const long GCGameType = 4440324665927270400;

    private const string CommonKeyExpectedHash = "35-AC-59-94-97-22-79-33-1D-97-09-4F-A2-FB-97-FC";
    private const string TitleKeyExpectedHash = "F9-4B-D8-8E-BB-7A-A9-38-67-E6-30-61-5F-27-1C-9F";
    private const string AncastKeyExpectedHash = "31-8D-1F-9D-98-FB-08-E7-7C-7F-E1-77-AA-49-05-43";

    private static readonly string TempRootPath = Path.Combine(Path.GetTempPath(), "Moon WiiVC Injector") + Path.DirectorySeparatorChar;
    private static readonly string TempSourcePath = Path.Combine(TempRootPath, "SOURCETEMP") + Path.DirectorySeparatorChar;
    private static readonly string TempBuildPath = Path.Combine(TempRootPath, "BUILDDIR") + Path.DirectorySeparatorChar;
    private static readonly string TempToolsPath = Path.Combine(TempRootPath, "TOOLDIR") + Path.DirectorySeparatorChar;
    private static readonly string JNUSToolDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "JNUSToolDownloads") + Path.DirectorySeparatorChar;

    private static readonly string TempIconPath = Path.Combine(TempSourcePath, "iconTex.png");
    private static readonly string TempBannerPath = Path.Combine(TempSourcePath, "bootTvTex.png");
    private static readonly string TempDrcPath = Path.Combine(TempSourcePath, "bootDrcTex.png");
    private static readonly string TempLogoPath = Path.Combine(TempSourcePath, "bootLogoTex.png");
    private static readonly string TempSoundPath = Path.Combine(TempSourcePath, "bootSound.wav");

    private readonly IDialogService _dialogService;
    private readonly Task _setupTask;
    private CancellationTokenSource? _buildCts;

    // Internal resolution fields
    private string _systemType = "wii";
    private string _titleIdHex = string.Empty;
    private string _titleIdText = string.Empty;
    private string _internalGameName = string.Empty;
    private string _cucholixRepoId = string.Empty;
    private string _selectedGamePath = string.Empty;
    private int _titleIdInt;
    private long _gameType;
    private bool _flagGameSpecified;
    private bool _flagIconSpecified;
    private bool _flagBannerSpecified;
    private bool _flagGc2Specified;
    private bool _flagWbfs;

    // Observable Properties: System Type Selection
    [ObservableProperty]
    private bool _isWiiRetail = true;

    [ObservableProperty]
    private bool _isWiiHomebrew;

    [ObservableProperty]
    private bool _isGCRetail;

    [ObservableProperty]
    private bool _isWiiNAND;

    // Observable Properties: Source Files
    [ObservableProperty]
    private string _gameSourceText = "Game file has not been specified";

    [ObservableProperty]
    private string _iconSourceText = "Icon has not been specified";

    [ObservableProperty]
    private string _bannerSourceText = "Banner has not been specified";

    [ObservableProperty]
    private string _gC2SourceText = "2nd GameCube Disc Image has not been specified";

    [ObservableProperty]
    private string _bootSoundText = "Boot Sound has not been specified";

    [ObservableProperty]
    private string _logoSourceText = "Boot Logo has not been specified";

    [ObservableProperty]
    private string _drcSourceText = "GamePad Banner has not been specified";

    // Observable Properties: Previews
    [ObservableProperty]
    private Bitmap? _iconPreview;

    [ObservableProperty]
    private Bitmap? _bannerPreview;

    [ObservableProperty]
    private Bitmap? _logoPreview;

    [ObservableProperty]
    private Bitmap? _drcPreview;

    // Observable Properties: Game Metadata
    [ObservableProperty]
    private string _internalGameNameDisplay = string.Empty;

    [ObservableProperty]
    private string _internalGameIDDisplay = string.Empty;

    [ObservableProperty]
    private string _packedTitleLine1 = string.Empty;

    [ObservableProperty]
    private string _packedTitleLine2 = string.Empty;

    [ObservableProperty]
    private bool _enablePackedLine2;

    [ObservableProperty]
    private string _packedTitleIDLine = string.Empty;

    // Observable Properties: Emulation / Controller
    [ObservableProperty]
    private bool _noGamePadEmu = true;

    [ObservableProperty]
    private bool _forceCC;

    [ObservableProperty]
    private bool _ccEmu;

    [ObservableProperty]
    private bool _forceNoCC;

    [ObservableProperty]
    private bool _horWiiMote;

    [ObservableProperty]
    private bool _verWiiMote;

    [ObservableProperty]
    private bool _lrPatch;

    // Observable Properties: Advanced Options
    [ObservableProperty]
    private bool _wiimmfi;

    [ObservableProperty]
    private bool _wiiVMC;

    [ObservableProperty]
    private bool _disableGamePad;

    [ObservableProperty]
    private bool _disableTrimming;

    [ObservableProperty]
    private bool _disableNintendontAutoboot;

    [ObservableProperty]
    private bool _c2WPatchFlag;

    [ObservableProperty]
    private bool _toggleBootSoundLoop;

    // Observable Properties: Keys
    [ObservableProperty]
    private string _wiiUCommonKey = string.Empty;

    [ObservableProperty]
    private string _titleKey = string.Empty;

    [ObservableProperty]
    private string _ancastKey = string.Empty;

    [ObservableProperty]
    private bool _isCommonKeyValid;

    [ObservableProperty]
    private bool _isTitleKeyValid;

    [ObservableProperty]
    private bool _isAncastKeyValid;

    [ObservableProperty]
    private bool _isCommonKeyReadOnly;

    [ObservableProperty]
    private bool _isTitleKeyReadOnly;

    [ObservableProperty]
    private bool _isAncastKeyReadOnly;

    // Observable Properties: Checklist & Build Status
    [ObservableProperty]
    private bool _sourceCheck;

    [ObservableProperty]
    private bool _metaCheck;

    [ObservableProperty]
    private bool _keysCheck;

    [ObservableProperty]
    private bool _advanceCheck = true;

    [ObservableProperty]
    private bool _canBuild;

    [ObservableProperty]
    private bool _isBuilding;

    [ObservableProperty]
    private bool _canCancel;

    [ObservableProperty]
    private double _buildProgress;

    [ObservableProperty]
    private string _buildStatus = "Ready";

    [ObservableProperty]
    private string _logOutput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetryDownloadRepo))]
    [NotifyPropertyChangedFor(nameof(RepoDownloadButtonText))]
    private bool _isRepoDownloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetryDownloadRepo))]
    [NotifyPropertyChangedFor(nameof(RepoDownloadButtonText))]
    private bool _repoDownloadFailed;

    // UI Capability bindings
    public bool CanSelectGame => !IsWiiNAND;
    public bool IsGCSelected => IsGCRetail;
    public bool CanDownloadRepo => IsWiiRetail || IsGCRetail || IsWiiNAND;
    public bool CanRetryDownloadRepo => (IsWiiRetail || IsGCRetail || IsWiiNAND) && _flagGameSpecified && (RepoDownloadFailed || !_flagIconSpecified || !_flagBannerSpecified) && !IsRepoDownloading;
    public string RepoDownloadButtonText
    {
        get
        {
            if (IsRepoDownloading) return "Downloading...";
            if (RepoDownloadFailed || !_flagIconSpecified || !_flagBannerSpecified) return "Retry Download";
            return "Assets Loaded";
        }
    }
    public bool IsAncastKeyVisible => C2WPatchFlag;

    public MainViewModel(IDialogService dialogService)
    {
        _dialogService = dialogService;
        AppLogger.SetListener(AppendLogMessage);
        AppLogger.Info("Moon WiiVC Injector initialized.");
        _setupTask = SetupEnvironmentAsync();
        LoadStoredKeys();
    }

    private async Task SetupEnvironmentAsync()
    {
        await Task.Run(() =>
        {
            AppLogger.DebugLog($"Setting up temporary environment in: {TempRootPath}");
            if (Directory.Exists(TempRootPath))
            {
                FileUtil.SafeDeleteDirectory(TempRootPath);
            }
            try { Directory.CreateDirectory(TempRootPath); } catch (Exception ex) { AppLogger.Error("Failed to create TempRootPath", ex); }

            try
            {
                string toolZipPath = Path.Combine(TempRootPath, "TOOLDIR.zip");
                File.WriteAllBytes(toolZipPath, Properties.Resources.TOOLDIR);
                ZipFile.ExtractToDirectory(toolZipPath, TempRootPath);
                File.Delete(toolZipPath);

                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
                {
                    string witLinuxPath = Path.Combine(TempToolsPath, "WIT", "wit");
                    if (File.Exists(witLinuxPath))
                    {
                        try
                        {
                            File.SetUnixFileMode(witLinuxPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                                               UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                                               UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                        }
                        catch
                        {
                            try { Process.Start("chmod", $"+x \"{witLinuxPath}\"")?.WaitForExit(); } catch { }
                        }
                    }
                }

                AppLogger.DebugLog("Tool directory extracted.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to extract tools from resources", ex);
            }

            try
            {
                Directory.CreateDirectory(TempSourcePath);
                Directory.CreateDirectory(TempBuildPath);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to create Source/Build directories", ex);
            }
        });
    }

    private void LoadStoredKeys()
    {
        WiiUCommonKey = Settings.Default.WiiUCommonKey?.ToUpperInvariant() ?? string.Empty;
        TitleKey = Settings.Default.TitleKey?.ToUpperInvariant() ?? string.Empty;
        AncastKey = Settings.Default.AncastKey?.ToUpperInvariant() ?? string.Empty;

        ValidateKeys();
    }

    private void ValidateKeys()
    {
        IsCommonKeyValid = ValidateKeyHash(WiiUCommonKey, CommonKeyExpectedHash);
        IsCommonKeyReadOnly = IsCommonKeyValid;

        IsTitleKeyValid = ValidateKeyHash(TitleKey, TitleKeyExpectedHash);
        IsTitleKeyReadOnly = IsTitleKeyValid;

        IsAncastKeyValid = ValidateKeyHash(AncastKey, AncastKeyExpectedHash);
        IsAncastKeyReadOnly = IsAncastKeyValid;

        UpdateChecklist();
    }

    private static bool ValidateKeyHash(string key, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        byte[] data = MD5.HashData(Encoding.ASCII.GetBytes(key.Trim().ToUpperInvariant()));
        string hash = BitConverter.ToString(data);
        return string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    public void UpdateChecklist()
    {
        SourceCheck = _flagGameSpecified && _flagIconSpecified && _flagBannerSpecified;
        MetaCheck = !string.IsNullOrEmpty(PackedTitleLine1) && PackedTitleIDLine?.Length == 16;
        AdvanceCheck = true;

        bool skipAncast = !C2WPatchFlag;
        KeysCheck = skipAncast ? (IsCommonKeyValid && IsTitleKeyValid) : (IsCommonKeyValid && IsTitleKeyValid && IsAncastKeyValid);

        CanBuild = SourceCheck && MetaCheck && AdvanceCheck && KeysCheck && !IsBuilding;
    }

    partial void OnIsWiiRetailChanged(bool value)
    {
        if (value) SetSystemType("wii");
    }

    partial void OnIsWiiHomebrewChanged(bool value)
    {
        if (value) SetSystemType("dol");
    }

    partial void OnIsGCRetailChanged(bool value)
    {
        if (value) SetSystemType("gcn");
    }

    partial void OnIsWiiNANDChanged(bool value)
    {
        if (value)
        {
            _systemType = "wiiware";
            NotifySystemTypeChanged();
            _ = HandleWiiNandSelectionAsync();
        }
    }

    private void SetSystemType(string systemType)
    {
        _systemType = systemType;
        NotifySystemTypeChanged();
        ResetGameSelection();
    }

    private void NotifySystemTypeChanged()
    {
        OnPropertyChanged(nameof(CanSelectGame));
        OnPropertyChanged(nameof(IsGCSelected));
        OnPropertyChanged(nameof(CanDownloadRepo));
        OnPropertyChanged(nameof(CanRetryDownloadRepo));
        OnPropertyChanged(nameof(RepoDownloadButtonText));
    }

    private void ResetGameSelection()
    {
        _flagGameSpecified = false;
        _selectedGamePath = string.Empty;
        _titleIdInt = 0;
        _titleIdHex = string.Empty;
        _gameType = 0;
        _cucholixRepoId = string.Empty;
        RepoDownloadFailed = false;
        IsRepoDownloading = false;

        GameSourceText = "Game file has not been specified";
        InternalGameNameDisplay = string.Empty;
        InternalGameIDDisplay = string.Empty;
        PackedTitleLine1 = string.Empty;
        PackedTitleIDLine = string.Empty;

        GC2SourceText = (_systemType == "gcn") ? "2nd GameCube Disc Image has not been specified" : "N/A";
        UpdateChecklist();
    }

    private async Task HandleWiiNandSelectionAsync()
    {
        ResetGameSelection();

        var inputId = await _dialogService.PromptAsync(
            "Enter your installed Wii Channel's 4-letter Title ID. If you don't know it, open a WAD for the channel in something like ShowMiiWads to view it.",
            "Enter your WAD's Title ID", "XXXX");

        if (string.IsNullOrEmpty(inputId))
        {
            GameSourceText = "Title ID specification cancelled, reselect vWii NAND Title Launcher to specify";
            _flagGameSpecified = false;
            UpdateChecklist();
            return;
        }

        inputId = inputId.Trim().ToUpperInvariant();
        if (inputId.Length == 4)
        {
            GameSourceText = inputId;
            _flagGameSpecified = true;
            _titleIdText = inputId;
            _cucholixRepoId = inputId;

            StringBuilder sb = new();
            foreach (char c in _titleIdText)
            {
                sb.Append(((short)c).ToString("X2"));
            }
            PackedTitleIDLine = "00050002" + sb.ToString();
            PackedTitleLine1 = RemoveSpecialChars(GameTdb.GetName(_cucholixRepoId) ?? string.Empty);
            InternalGameNameDisplay = PackedTitleLine1;
            InternalGameIDDisplay = inputId;
            _ = FetchRepoAssetsAsync(silent: true);
        }
        else
        {
            GameSourceText = "Invalid Title ID";
            _flagGameSpecified = false;
            await _dialogService.ShowMessageAsync(
                "Only 4 characters can be used, try again. Example: The Star Fox 64 (USA) Channel's Title ID is NADE01, so you would specify NADE as the Title ID",
                "Invalid Title ID", MessageBoxButtons.Ok);
        }
        UpdateChecklist();
    }

    partial void OnPackedTitleLine1Changed(string value) => UpdateChecklist();
    partial void OnPackedTitleIDLineChanged(string value) => UpdateChecklist();
    partial void OnC2WPatchFlagChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAncastKeyVisible));
        UpdateChecklist();
    }

    [RelayCommand]
    public async Task SelectGameAsync()
    {
        string title = "Select game file";
        var filters = new List<FilePickerFileType>();

        if (_systemType == "wii")
        {
            title = "Select Wii game dump (ISO, WBFS)";
            filters.Add(new FilePickerFileType("Wii Game Dumps") { Patterns = ["*.iso", "*.wbfs"] });
        }
        else if (_systemType == "gcn")
        {
            title = "Select GameCube game dump (ISO, GCM)";
            filters.Add(new FilePickerFileType("GameCube Game Dumps") { Patterns = ["*.iso", "*.gcm"] });
        }
        else if (_systemType == "dol")
        {
            title = "Select Homebrew executable (DOL)";
            filters.Add(new FilePickerFileType("DOL Executable") { Patterns = ["*.dol"] });
        }

        var path = await _dialogService.OpenFileDialogAsync(title, [.. filters]);
        if (!string.IsNullOrEmpty(path))
        {
            await LoadGameFromFileAsync(path);
        }
    }

    public async Task LoadGameFromFileAsync(string gamePath)
    {
        _selectedGamePath = gamePath;
        CleanUpImages();

        GameSourceText = _selectedGamePath;
        _flagGameSpecified = true;
        byte[] idBytes = new byte[4];

        try
        {
            using (var fs = File.OpenRead(_selectedGamePath))
            {
                fs.Position = 0;
                fs.ReadExactly(idBytes);
                _titleIdInt = BitConverter.ToInt32(idBytes);
                string idString = Encoding.ASCII.GetString(idBytes);

                if (idString == "WBFS")
                {
                    _flagWbfs = true;
                    fs.Position = 0x200;
                    fs.ReadExactly(idBytes);
                    _titleIdInt = BitConverter.ToInt32(idBytes);

                    fs.Position = 0x218;
                    byte[] tempLong = new byte[8];
                    fs.ReadExactly(tempLong);
                    _gameType = BitConverter.ToInt64(tempLong);

                    fs.Position = 0x220;
                    _internalGameName = ReadNullTerminatedString(fs);

                    fs.Position = 0x200;
                    _cucholixRepoId = ReadNullTerminatedString(fs);
                }
                else
                {
                    _flagWbfs = false;
                    if (_titleIdInt == 65536 || _systemType == "dol")
                    {
                        fs.Position = 0x2A0;
                        fs.ReadExactly(idBytes);
                        _titleIdInt = BitConverter.ToInt32(idBytes);
                        _internalGameName = "N/A";
                    }
                    else
                    {
                        uint startOffset = 0;
                        if (idString == "WII5") startOffset = 0x1182800;
                        else if (idString == "WII9") startOffset = 0x1FB5000;

                        fs.Position = startOffset;
                        fs.ReadExactly(idBytes);
                        _titleIdInt = BitConverter.ToInt32(idBytes);

                        fs.Position = startOffset + 0x18;
                        byte[] tempLong = new byte[8];
                        fs.ReadExactly(tempLong);
                        _gameType = BitConverter.ToInt64(tempLong);

                        fs.Position = startOffset + 0x20;
                        _internalGameName = ReadNullTerminatedString(fs);

                        fs.Position = startOffset + 0x00;
                        _cucholixRepoId = ReadNullTerminatedString(fs);
                    }
                }
            }

            if ((_systemType == "wii" && _gameType != WiiGameType) || (_systemType == "gcn" && _gameType != GCGameType))
            {
                string err = _systemType == "wii" ? "This is not a Wii image. It will not be loaded." : "This is not a GameCube image. It will not be loaded.";
                ResetGameSelection();
                await _dialogService.ShowMessageAsync(err, "Error", MessageBoxButtons.Ok);
                return;
            }

            InternalGameNameDisplay = _internalGameName;
            var dbName = RemoveSpecialChars(GameTdb.GetName(_cucholixRepoId) ?? string.Empty);
            PackedTitleLine1 = !string.IsNullOrEmpty(dbName) ? dbName : _internalGameName;

            byte[] titleIdBytes = BitConverter.GetBytes(_titleIdInt);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(titleIdBytes);
            }
            _titleIdHex = BitConverter.ToString(titleIdBytes).Replace("-", "");

            if (_systemType == "dol")
            {
                InternalGameIDDisplay = _titleIdHex;
                PackedTitleIDLine = $"00050002{_titleIdHex}";
                _titleIdText = "BOOT";
            }
            else
            {
                _titleIdText = Encoding.ASCII.GetString(Convert.FromHexString(_titleIdHex));
                InternalGameIDDisplay = $"{_titleIdText} / {_titleIdHex}";
                PackedTitleIDLine = $"00050002{_titleIdHex}";
            }

            UpdateChecklist();

            // Auto-fetch assets from repo in the background
            _ = FetchRepoAssetsAsync(silent: true);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync($"Failed to read game file metadata: {ex.Message}", "Error", MessageBoxButtons.Ok);
            ResetGameSelection();
        }

        UpdateChecklist();
    }

    private void CleanUpImages()
    {
        FileUtil.SafeDeleteDirectory(TempSourcePath);
        FileUtil.SafeDeleteDirectory(TempBuildPath);
        try
        {
            Directory.CreateDirectory(TempSourcePath);
            Directory.CreateDirectory(TempBuildPath);
        }
        catch { }

        IconPreview = null;
        BannerPreview = null;
        _flagIconSpecified = false;
        _flagBannerSpecified = false;
        IconSourceText = "Icon has not been specified";
        BannerSourceText = "Banner has not been specified";
    }

    private async Task<bool> ProcessImageFileAsync(string imageType, string path, string tempPath, int width, int height, Action<Bitmap?> setPreview, Action<string> setSourceText)
    {
        try
        {
            FileUtil.SafeDeleteFile(tempPath);

            if (Path.GetExtension(path).Equals(".tga", StringComparison.OrdinalIgnoreCase))
            {
                using var bmp = TgaReader.LoadTga(path);
                TgaReader.SaveAsTga(bmp, tempPath, width, height, 32);
            }
            else
            {
                File.Copy(path, tempPath, true);
            }

            using (var stream = File.OpenRead(tempPath))
            {
                setPreview(new Bitmap(stream));
            }

            setSourceText(path);
            return true;
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync($"Failed to load {imageType.ToLowerInvariant()}: {ex.Message}", "Error", MessageBoxButtons.Ok);
            return false;
        }
    }

    [RelayCommand]
    public async Task SelectIconAsync()
    {
        await _dialogService.ShowMessageAsync("Make sure your Icon is 128x128 to prevent distortion", "Icon Size Information", MessageBoxButtons.Ok);
        var path = await _dialogService.OpenFileDialogAsync("Select Icon PNG or TGA", [new FilePickerFileType("Images") { Patterns = ["*.png", "*.tga"] }]);
        if (!string.IsNullOrEmpty(path))
        {
            if (await ProcessImageFileAsync("Icon", path, TempIconPath, 128, 128, b => IconPreview = b, s => IconSourceText = s))
            {
                _flagIconSpecified = true;
                UpdateChecklist();
            }
        }
    }

    [RelayCommand]
    public async Task SelectBannerAsync()
    {
        await _dialogService.ShowMessageAsync("Make sure your Banner is 1280x720 to prevent distortion", "Banner Size Information", MessageBoxButtons.Ok);
        var path = await _dialogService.OpenFileDialogAsync("Select Banner PNG or TGA", [new FilePickerFileType("Images") { Patterns = ["*.png", "*.tga"] }]);
        if (!string.IsNullOrEmpty(path))
        {
            if (await ProcessImageFileAsync("Banner", path, TempBannerPath, 1280, 720, b => BannerPreview = b, s => BannerSourceText = s))
            {
                _flagBannerSpecified = true;
                UpdateChecklist();
            }
        }
    }

    [RelayCommand]
    public async Task SelectGC2Async()
    {
        var path = await _dialogService.OpenFileDialogAsync("Select 2nd GameCube Disc Image", [new FilePickerFileType("GameCube Disc") { Patterns = ["*.iso", "*.gcm"] }]);
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                using var fs = File.OpenRead(path);
                fs.Position = 0x18;
                byte[] typeBytes = new byte[8];
                fs.ReadExactly(typeBytes);
                long gc2GameType = BitConverter.ToInt64(typeBytes);

                if (gc2GameType != GCGameType)
                {
                    await _dialogService.ShowMessageAsync("This is not a GameCube image. It will not be loaded.", "Error", MessageBoxButtons.Ok);
                    GC2SourceText = "2nd GameCube Disc Image has not been specified";
                    _flagGc2Specified = false;
                }
                else
                {
                    GC2SourceText = path;
                    _flagGc2Specified = true;
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowMessageAsync($"Failed to read 2nd disc: {ex.Message}", "Error", MessageBoxButtons.Ok);
            }
        }
    }

    [RelayCommand]
    public async Task SelectBootSoundAsync()
    {
        await _dialogService.ShowMessageAsync("Your sound file will be cut off if it's longer than 6 seconds to prevent the Wii U from not loading it.", "Boot Sound Information", MessageBoxButtons.Ok);
        var path = await _dialogService.OpenFileDialogAsync("Select Boot Sound (WAV)", [new FilePickerFileType("WAV Audio") { Patterns = ["*.wav"] }]);
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                using var fs = File.OpenRead(path);
                byte[] headerBytes = new byte[4];
                fs.Position = 0x00;
                fs.ReadExactly(headerBytes);
                int wavHeader1 = BitConverter.ToInt32(headerBytes);

                fs.Position = 0x08;
                fs.ReadExactly(headerBytes);
                int wavHeader2 = BitConverter.ToInt32(headerBytes);

                if (wavHeader1 == 1179011410 && wavHeader2 == 1163280727)
                {
                    BootSoundText = path;
                }
                else
                {
                    await _dialogService.ShowMessageAsync("This is not a valid WAV file. It will not be loaded.", "Not a WAV File", MessageBoxButtons.Ok);
                    BootSoundText = "Boot Sound has not been specified";
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowMessageAsync($"Failed to read WAV: {ex.Message}", "Error", MessageBoxButtons.Ok);
            }
        }
    }

    [RelayCommand]
    public async Task SelectLogoAsync()
    {
        await _dialogService.ShowMessageAsync("Make sure your Boot Logo is 170x42 to prevent distortion", "Logo Size Information", MessageBoxButtons.Ok);
        var path = await _dialogService.OpenFileDialogAsync("Select Boot Logo PNG or TGA", [new FilePickerFileType("Images") { Patterns = ["*.png", "*.tga"] }]);
        if (!string.IsNullOrEmpty(path))
        {
            await ProcessImageFileAsync("Logo", path, TempLogoPath, 170, 42, b => LogoPreview = b, s => LogoSourceText = s);
        }
    }

    [RelayCommand]
    public async Task SelectDrcAsync()
    {
        await _dialogService.ShowMessageAsync("Make sure your GamePad Banner is 854x480 to prevent distortion", "Banner Size Information", MessageBoxButtons.Ok);
        var path = await _dialogService.OpenFileDialogAsync("Select GamePad Banner PNG or TGA", [new FilePickerFileType("Images") { Patterns = ["*.png", "*.tga"] }]);
        if (!string.IsNullOrEmpty(path))
        {
            await ProcessImageFileAsync("GamePad Banner", path, TempDrcPath, 854, 480, b => DrcPreview = b, s => DrcSourceText = s);
        }
    }

    [RelayCommand]
    public async Task DownloadRepoAsync() => await FetchRepoAssetsAsync(silent: false);

    public async Task FetchRepoAssetsAsync(bool silent = false)
    {
        if (string.IsNullOrEmpty(_cucholixRepoId))
        {
            if (!silent)
            {
                await _dialogService.ShowMessageAsync("Could not identify game to download repository files for", "Error", MessageBoxButtons.Ok);
            }
            return;
        }

        string rawBaseUrl = "https://raw.githubusercontent.com/cucholix/wiivc-bis/master";

        string[] platforms = _systemType switch
        {
            "wii" => ["wii", "wiiware"],
            "gcn" => ["gcn"],
            _ => ["wii", "gcn", "wiiware"]
        };

        // Collect candidate IDs to probe (e.g. SF8P01, SF8E01, SF8J01, SF8E, SF8P, etc.)
        List<string> candidateIds = [];
        foreach (var id in GameTdb.GetAlternativeIds(_cucholixRepoId))
        {
            if (!candidateIds.Contains(id)) candidateIds.Add(id);
            if (id.Length > 4)
            {
                string id4 = id[..4];
                if (!candidateIds.Contains(id4)) candidateIds.Add(id4);
            }
        }

        bool downloadedIcon = false;
        bool downloadedBanner = false;
        bool downloadedDrc = false;
        bool downloadedLogo = false;
        bool downloadedSound = false;

        static async Task<byte[]?> DownloadIfAvailableAsync(string url)
        {
            try
            {
                using var response = await Program.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsByteArrayAsync();
                }
            }
            catch { }
            return null;
        }

        IsRepoDownloading = true;
        try
        {
            // 1. Search for game-specific assets across platforms and candidate IDs
            foreach (var platform in platforms)
            {
                foreach (var id in candidateIds)
                {
                    string gameBaseUrl = $"{rawBaseUrl}/{platform}/image/{id}";

                    if (!downloadedIcon)
                    {
                        byte[]? iconBytes = await DownloadIfAvailableAsync($"{gameBaseUrl}/iconTex.png");
                        if (iconBytes != null)
                        {
                            await File.WriteAllBytesAsync(TempIconPath, iconBytes);
                            using var ms = new MemoryStream(iconBytes);
                            IconPreview = new Bitmap(ms);
                            IconSourceText = $"Downloaded from cucholix ({id})";
                            _flagIconSpecified = true;
                            downloadedIcon = true;
                            AppLogger.DebugLog($"[AutoDownload] Icon downloaded for {id}");
                        }
                    }

                    if (!downloadedBanner)
                    {
                        byte[]? bannerBytes = await DownloadIfAvailableAsync($"{gameBaseUrl}/bootTvTex.png");
                        if (bannerBytes != null)
                        {
                            await File.WriteAllBytesAsync(TempBannerPath, bannerBytes);
                            using var ms = new MemoryStream(bannerBytes);
                            BannerPreview = new Bitmap(ms);
                            BannerSourceText = $"Downloaded from cucholix ({id})";
                            _flagBannerSpecified = true;
                            downloadedBanner = true;
                            AppLogger.DebugLog($"[AutoDownload] TV Banner downloaded for {id}");
                        }
                    }

                    if (!downloadedDrc)
                    {
                        byte[]? drcBytes = await DownloadIfAvailableAsync($"{gameBaseUrl}/bootDrcTex.png");
                        if (drcBytes != null)
                        {
                            await File.WriteAllBytesAsync(TempDrcPath, drcBytes);
                            using var ms = new MemoryStream(drcBytes);
                            DrcPreview = new Bitmap(ms);
                            DrcSourceText = $"Downloaded from cucholix ({id})";
                            downloadedDrc = true;
                            AppLogger.DebugLog($"[AutoDownload] GamePad Banner downloaded for {id}");
                        }
                    }

                    if (!downloadedLogo)
                    {
                        byte[]? logoBytes = await DownloadIfAvailableAsync($"{gameBaseUrl}/bootLogoTex.png");
                        if (logoBytes != null)
                        {
                            await File.WriteAllBytesAsync(TempLogoPath, logoBytes);
                            using var ms = new MemoryStream(logoBytes);
                            LogoPreview = new Bitmap(ms);
                            LogoSourceText = $"Downloaded from cucholix ({id})";
                            downloadedLogo = true;
                            AppLogger.DebugLog($"[AutoDownload] Logo downloaded for {id}");
                        }
                    }

                    if (!downloadedSound)
                    {
                        byte[]? soundBytes = await DownloadIfAvailableAsync($"{gameBaseUrl}/bootSound.btsnd")
                                          ?? await DownloadIfAvailableAsync($"{gameBaseUrl}/bootSound.wav");
                        if (soundBytes != null)
                        {
                            await File.WriteAllBytesAsync(TempSoundPath, soundBytes);
                            BootSoundText = $"Downloaded from cucholix ({id})";
                            downloadedSound = true;
                            AppLogger.DebugLog($"[AutoDownload] Boot Sound downloaded for {id}");
                        }
                    }

                    if (downloadedIcon && downloadedBanner)
                    {
                        break;
                    }
                }

                if (downloadedIcon && downloadedBanner)
                {
                    break;
                }
            }

            // 2. Fallback for platform default bootSound (e.g. GameCube or platform jingle)
            if (!downloadedSound)
            {
                foreach (var platform in platforms)
                {
                    byte[]? soundBytes = await DownloadIfAvailableAsync($"{rawBaseUrl}/{platform}/sound/bootSound.btsnd")
                                      ?? await DownloadIfAvailableAsync($"{rawBaseUrl}/{platform}/sound/gcnboot/bootSound.wav");
                    if (soundBytes != null)
                    {
                        await File.WriteAllBytesAsync(TempSoundPath, soundBytes);
                        BootSoundText = $"Downloaded default from cucholix ({platform})";
                        downloadedSound = true;
                        AppLogger.DebugLog($"[AutoDownload] Default boot sound downloaded for {platform}");
                        break;
                    }
                }
            }

            bool anyDownloaded = downloadedIcon || downloadedBanner || downloadedDrc || downloadedLogo || downloadedSound;
            RepoDownloadFailed = !downloadedIcon || !downloadedBanner;

            if (!anyDownloaded && !silent)
            {
                await _dialogService.ShowMessageAsync(
                    $"Could not find any files matching '{_cucholixRepoId}' (or regional alternatives) in cucholix's repository.",
                    "Error",
                    MessageBoxButtons.Ok);
            }
        }
        catch (Exception ex)
        {
            RepoDownloadFailed = true;
            AppLogger.Error("[AutoDownload] Error downloading assets from repository", ex);
            if (!silent)
            {
                await _dialogService.ShowMessageAsync($"Failed to download from repository: {ex.Message}", "Error", MessageBoxButtons.Ok);
            }
        }
        finally
        {
            IsRepoDownloading = false;
            UpdateChecklist();
        }
    }

    [RelayCommand]
    public void SaveCommonKey()
    {
        if (string.IsNullOrWhiteSpace(WiiUCommonKey)) return;
        WiiUCommonKey = WiiUCommonKey.Trim().ToUpperInvariant();
        Settings.Default.WiiUCommonKey = WiiUCommonKey;
        Settings.Default.Save();
        ValidateKeys();
    }

    [RelayCommand]
    public void SaveTitleKey()
    {
        if (string.IsNullOrWhiteSpace(TitleKey)) return;
        TitleKey = TitleKey.Trim().ToUpperInvariant();
        Settings.Default.TitleKey = TitleKey;
        Settings.Default.Save();
        ValidateKeys();
    }

    [RelayCommand]
    public void SaveAncastKey()
    {
        if (string.IsNullOrWhiteSpace(AncastKey)) return;
        AncastKey = AncastKey.Trim().ToUpperInvariant();
        Settings.Default.AncastKey = AncastKey;
        Settings.Default.Save();
        ValidateKeys();
    }

    public void UnlockKey(string keyName)
    {
        switch (keyName)
        {
            case "WiiUCommonKey":
                IsCommonKeyReadOnly = false;
                break;
            case "TitleKey":
                IsTitleKeyReadOnly = false;
                break;
            case "AncastKey":
                IsAncastKeyReadOnly = false;
                break;
        }
    }

    [RelayCommand]
    public async Task BuildAsync()
    {
        IsBuilding = true;
        CanCancel = true;
        BuildProgress = 0;
        BuildStatus = "Initializing Build Process...";

        await _setupTask;

        if (_systemType == "wii" || _systemType == "gcn")
        {
            long gamesize = 0;
            try
            {
                if (File.Exists(_selectedGamePath))
                    gamesize = new FileInfo(_selectedGamePath).Length;
            }
            catch { }

            try
            {
                var drive = new DriveInfo(TempRootPath);
                long freeSpaceInBytes = drive.AvailableFreeSpace;
                long limit = _systemType == "wii" ? (gamesize * 2 + 5000000000) : (gamesize * 2 + 6000000000);
                if (freeSpaceInBytes < limit)
                {
                    var res = await _dialogService.ShowMessageAsync(
                        "Your hard drive may be low on space. The conversion process involves temporary files that can amount to more than double the size of your game. Do you want to continue anyway?",
                        "Check your hard drive space", MessageBoxButtons.YesNo);
                    if (res == MessageBoxResult.No)
                    {
                        IsBuilding = false;
                        CanCancel = false;
                        BuildStatus = "Ready";
                        return;
                    }
                }
            }
            catch { }
        }

        string selectedOutputPath = "";
        if (!string.IsNullOrEmpty(Settings.Default.OutputPathFixed))
        {
            selectedOutputPath = Settings.Default.OutputPathFixed;
        }
        else
        {
            var folder = await _dialogService.OpenFolderDialogAsync("Specify your output folder");
            if (string.IsNullOrEmpty(folder))
            {
                await _dialogService.ShowMessageAsync("Output folder selection has been cancelled, conversion will not continue.", "Cancelled", MessageBoxButtons.Ok);
                IsBuilding = false;
                CanCancel = false;
                BuildStatus = "Ready";
                return;
            }
            selectedOutputPath = folder;
            Settings.Default.OutputPath = selectedOutputPath;
            Settings.Default.Save();
        }

        BuildProgress = 2;

        string gc2Path = (_systemType == "gcn" && _flagGc2Specified) ? GC2SourceText : string.Empty;

        string nfsPatchFlag = "";
        if (HorWiiMote) nfsPatchFlag = " -horizontal";
        else if (VerWiiMote) nfsPatchFlag = " -wiimote";
        else if (CcEmu) nfsPatchFlag = " -nocc";
        else if (ForceCC) nfsPatchFlag = " -instantcc";
        else if (ForceNoCC) nfsPatchFlag = " -nocc";

        string drcuse = DisableGamePad ? "65537" : "1";

        var options = new BuildOptions
        {
            SystemType = _systemType,
            SelectedGamePath = _selectedGamePath,
            SelectedOutputPath = selectedOutputPath,
            WiiUCommonKey = WiiUCommonKey,
            TitleKey = TitleKey,
            AncastKey = AncastKey,
            PackedTitleIDLine = PackedTitleIDLine,
            PackedTitleLine1 = PackedTitleLine1,
            PackedTitleLine2 = PackedTitleLine2,
            EnablePackedLine2 = EnablePackedLine2,
            Wiimmfi = Wiimmfi,
            WiiVMC = WiiVMC,
            DisableTrimming = DisableTrimming,
            DisableNintendontAutoboot = DisableNintendontAutoboot,
            C2WPatch = C2WPatchFlag,
            LRPatch = LrPatch,
            SoundDir = BootSoundText,
            LogoDir = LogoSourceText,
            DrcDir = DrcSourceText,
            Gc2Path = gc2Path,
            ToggleBootSoundLoop = ToggleBootSoundLoop,
            NfsPatchFlag = nfsPatchFlag,
            DrcUse = drcuse,
            TitleIdHex = _titleIdHex,
            TitleIdText = _titleIdText,
            FlagGc2Specified = _flagGc2Specified,
            FlagWbfs = _flagWbfs,
            TempRootPath = TempRootPath,
            TempSourcePath = TempSourcePath,
            TempBuildPath = TempBuildPath,
            TempToolsPath = TempToolsPath,
            JNUSToolDownloads = JNUSToolDownloads,
            TempIconPath = TempIconPath,
            TempBannerPath = TempBannerPath,
            TempDrcPath = TempDrcPath,
            TempLogoPath = TempLogoPath
        };

        bool success = false;
        string finalOutputPath = "";
        string errorMsg = "";

        var progress = new Progress<(string Message, double Progress)>(update =>
        {
            BuildStatus = update.Message;
            BuildProgress = update.Progress;
        });

        _buildCts = new CancellationTokenSource();
        var builder = new BuildEngine(options, progress, onLogMessage: AppendLogMessage);

        try
        {
            finalOutputPath = await Task.Run(() => builder.RunAsync(_buildCts.Token));
            success = true;
        }
        catch (OperationCanceledException)
        {
            errorMsg = "Build operation was cancelled by user.";
        }
        catch (Exception ex)
        {
            errorMsg = ex.Message;
        }
        finally
        {
            CanCancel = false;
            await builder.SaveLogAsync(selectedOutputPath);
            await builder.SaveLogAsync(TempRootPath);
        }

        IsBuilding = false;
        BuildProgress = success ? 100 : 0;
        BuildStatus = success ? "Conversion complete!" : (_buildCts?.IsCancellationRequested == true ? "Conversion cancelled." : "Conversion failed.");

        if (success)
        {
            var res = await _dialogService.ShowMessageAsync(
                $"Conversion Complete! Your packed game can be found here:\n{finalOutputPath}\n\nInstall your title using WUP Installer GX2 with signature patches enabled.\n\nOpen the output folder now?",
                "Conversion Complete", MessageBoxButtons.YesNo);
            if (res == MessageBoxResult.Yes)
            {
                _dialogService.OpenPathWithDefaultApp(finalOutputPath);
            }
        }
        else if (_buildCts?.IsCancellationRequested == true)
        {
            await _dialogService.ShowMessageAsync("The build operation was cancelled. Temporary files were cleaned up.", "Operation Cancelled", MessageBoxButtons.Ok);
        }
        else
        {
            await _dialogService.ShowMessageAsync($"Conversion Failed!\n{errorMsg}\n\nA detailed log has been saved in the output directory (and temporary folder).", "Conversion Failed", MessageBoxButtons.Ok);
        }

        UpdateChecklist();
    }

    [RelayCommand]
    public void CancelBuild()
    {
        if (_buildCts != null && !_buildCts.IsCancellationRequested)
        {
            _buildCts.Cancel();
            BuildStatus = "Cancelling build...";
            CanCancel = false;
            AppendLogMessage("[USER] Build cancellation requested.");
        }
    }

    private void AppendLogMessage(string message)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LogOutput = string.IsNullOrEmpty(LogOutput) ? message : $"{LogOutput}{Environment.NewLine}{message}";
        });
    }

    [RelayCommand]
    public async Task CopyLogsAsync()
    {
        if (!string.IsNullOrEmpty(LogOutput))
        {
            await _dialogService.SetClipboardTextAsync(LogOutput);
            await _dialogService.ShowMessageAsync("Logs copied to clipboard.", "Logs Copied", MessageBoxButtons.Ok);
        }
    }

    [RelayCommand]
    public void ClearLogs()
    {
        LogOutput = string.Empty;
    }

    [RelayCommand]
    public async Task OpenLogFolderAsync()
    {
        string logFolder = !string.IsNullOrEmpty(Settings.Default.OutputPath) && Directory.Exists(Settings.Default.OutputPath)
            ? Settings.Default.OutputPath
            : TempRootPath;

        if (Directory.Exists(logFolder))
        {
            _dialogService.OpenPathWithDefaultApp(logFolder);
        }
        else
        {
            await _dialogService.ShowMessageAsync($"Log folder '{logFolder}' does not exist.", "Notice", MessageBoxButtons.Ok);
        }
    }

    [RelayCommand]
    public async Task OpenSettingsAsync()
    {
        await _dialogService.ShowSettingsDialogAsync();
    }

    [RelayCommand]
    public async Task OpenSdCardMenuAsync()
    {
        await _dialogService.ShowSdCardMenuDialogAsync();
    }

    public async Task LoadDroppedFilesAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".iso" or ".wbfs" or ".gcm" or ".dol")
            {
                await LoadGameFromFileAsync(path);
                break;
            }
            else if (ext is ".png" or ".tga" or ".jpg" or ".jpeg" or ".bmp")
            {
                if (!_flagIconSpecified)
                {
                    if (await ProcessImageFileAsync("Icon", path, TempIconPath, 128, 128, b => IconPreview = b, s => IconSourceText = s))
                    {
                        _flagIconSpecified = true;
                        UpdateChecklist();
                    }
                }
                else if (!_flagBannerSpecified)
                {
                    if (await ProcessImageFileAsync("Banner", path, TempBannerPath, 1280, 720, b => BannerPreview = b, s => BannerSourceText = s))
                    {
                        _flagBannerSpecified = true;
                        UpdateChecklist();
                    }
                }
                else
                {
                    await ProcessImageFileAsync("Banner", path, TempBannerPath, 1280, 720, b => BannerPreview = b, s => BannerSourceText = s);
                }
            }
            else if (ext is ".wav" or ".btsnd")
            {
                BootSoundText = path;
                try
                {
                    File.Copy(path, TempSoundPath, true);
                }
                catch { }
            }
        }
    }

    private static string ReadNullTerminatedString(Stream stream)
    {
        List<byte> bytes = [];
        int b;
        while (stream.Position < stream.Length && (b = stream.ReadByte()) > 0)
        {
            bytes.Add((byte)b);
        }
        return Encoding.UTF8.GetString([.. bytes]);
    }

    private static string RemoveSpecialChars(string v)
    {
        if (string.IsNullOrEmpty(v)) return v;
        string s = RemoveDiacritics(v);
        return new string([.. s.Where(c => c < 128)]);
    }

    private static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(capacity: normalizedString.Length);
        for (int i = 0; i < normalizedString.Length; i++)
        {
            char c = normalizedString[i];
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }
        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }
}
