using System.IO.Compression;
using System.Diagnostics;
using System.Text;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Media.Imaging;

namespace Moon_WiiVC_Injector;
public partial class MainWindow : Window
{
    // Specify constants for magic numbers
    private const long WiiGameType = 2745048157;
    private const long GCGameType = 4440324665927270400;

    // Path constants
    static readonly string TempRootPath = Path.Combine(Path.GetTempPath(), "Moon WiiVC Injector") + Path.DirectorySeparatorChar;
    static readonly string TempSourcePath = Path.Combine(TempRootPath, "SOURCETEMP") + Path.DirectorySeparatorChar;
    static readonly string TempBuildPath = Path.Combine(TempRootPath, "BUILDDIR") + Path.DirectorySeparatorChar;
    static readonly string TempToolsPath = Path.Combine(TempRootPath, "TOOLDIR") + Path.DirectorySeparatorChar;
    static readonly string JNUSToolDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "JNUSToolDownloads") + Path.DirectorySeparatorChar;

    static readonly string TempIconPath = Path.Combine(TempSourcePath, "iconTex.png");
    static readonly string TempBannerPath = Path.Combine(TempSourcePath, "bootTvTex.png");
    static readonly string TempDrcPath = Path.Combine(TempSourcePath, "bootDrcTex.png");
    static readonly string TempLogoPath = Path.Combine(TempSourcePath, "bootLogoTex.png");
    static readonly string TempSoundPath = Path.Combine(TempSourcePath, "bootSound.wav");

    // State fields
    private readonly Task _setupTask;
    private string _systemType = "wii";
    private string _titleIdHex = string.Empty;
    private string _titleIdText = string.Empty;
    private string _internalGameName = string.Empty;

    // Checklist flags
    private bool _flagGameSpecified;
    private bool _flagIconSpecified;
    private bool _flagBannerSpecified;

    private bool _buildFlagSource;
    private bool _buildFlagMeta;
    private bool _buildFlagAdvance = true;
    private bool _buildFlagKeys;
    private bool _commonKeyGood;
    private bool _titleKeyGood;
    private bool _ancastKeyGood;
    private bool _flagGc2Specified;
    private bool _flagWbfs;
    private int _titleIdInt;
    private long _gameType;
    private string _cucholixRepoId = "";
    private string _selectedGamePath = string.Empty;
    private CancellationTokenSource? _buildCts;
    private Button? _cancelBuildButton;
    private TextBox? _logOutputBox;

    public MainWindow()
    {
        InitializeComponent();
        _setupTask = SetupEnvironmentAsync();
        WireEvents();
        SetupDragAndDrop();
    }

    private void SetupDragAndDrop()
    {
        AddHandler(DragDrop.DragOverEvent, OnWindowDragOver);
        AddHandler(DragDrop.DropEvent, OnWindowDrop);
    }

    private async Task SetupEnvironmentAsync()
    {
        await Task.Run(() =>
        {
            // Delete Temporary Root Folder if it exists
            if (Directory.Exists(TempRootPath))
            {
                try { Directory.Delete(TempRootPath, true); } catch { }
            }
            try { Directory.CreateDirectory(TempRootPath); } catch { }

            // Extract Tools to temp folder
            try
            {
                string toolZipPath = Path.Combine(TempRootPath, "TOOLDIR.zip");
                File.WriteAllBytes(toolZipPath, Properties.Resources.TOOLDIR);
                ZipFile.ExtractToDirectory(toolZipPath, TempRootPath);
                File.Delete(toolZipPath);

                if (!OperatingSystem.IsWindows())
                {
                    string c2wPath = Path.Combine(TempToolsPath, "C2W", "c2w_patcher");
                    if (File.Exists(c2wPath))
                    {
                        File.SetUnixFileMode(c2wPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    }
                }
            }
            catch { }

            // Create Source and Build directories
            try
            {
                Directory.CreateDirectory(TempSourcePath);
                Directory.CreateDirectory(TempBuildPath);
            }
            catch { }
        });
    }

    private void WireEvents()
    {
        _cancelBuildButton = this.FindControl<Button>("CancelBuildButton");
        _logOutputBox = this.FindControl<TextBox>("LogOutputBox");

        WiiRetail.IsCheckedChanged += SystemType_Checked;
        WiiHomebrew.IsCheckedChanged += SystemType_Checked;
        WiiNAND.IsCheckedChanged += WiiNAND_Checked;
        GCRetail.IsCheckedChanged += SystemType_Checked;

        // Trigger initial selection updates
        UpdateUIForSystemType();
    }

    private void SystemType_Checked(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.IsChecked == true)
        {
            _systemType = rb.Name switch
            {
                "WiiRetail" => "wii",
                "WiiHomebrew" => "dol",
                "GCRetail" => "gcn",
                _ => _systemType
            };
            UpdateUIForSystemType();
        }
    }

    private async void WiiNAND_Checked(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.IsChecked == true)
        {
            _systemType = "wiiware";
            UpdateUIForSystemType();

            var prompt = new PromptWindow("Enter your installed Wii Channel's 4-letter Title ID. If you don't know it, open a WAD for the channel in something like ShowMiiWads to view it.", "Enter your WAD's Title ID", "XXXX");
            var isOk = await prompt.ShowDialog<bool>(this);
            string inputId = isOk ? prompt.Result : string.Empty;

            if (string.IsNullOrEmpty(inputId))
            {
                GameSourceDirectory.Text = "Title ID specification cancelled, reselect vWii NAND Title Launcher to specify";
                _flagGameSpecified = false;
                return;
            }

            if (inputId.Length == 4)
            {
                GameSourceDirectory.Text = inputId.ToUpper();
                _flagGameSpecified = true;
                _titleIdText = inputId.ToUpper();
                _cucholixRepoId = inputId.ToUpper();

                StringBuilder sb = new();
                foreach (char c in _titleIdText)
                {
                    sb.Append(((short)c).ToString("X2"));
                }
                PackedTitleIDLine.Text = "00050002" + sb.ToString();
            }
            else
            {
                GameSourceDirectory.Text = "Invalid Title ID";
                _flagGameSpecified = false;
                await MessageBoxWindow.Show(this,
                    "Only 4 characters can be used, try again. Example: The Star Fox 64 (USA) Channel's Title ID is NADE01, so you would specify NADE as the Title ID",
                    "Invalid Title ID", MessageBoxButtons.Ok);
            }
        }
    }

    private void UpdateUIForSystemType()
    {
        // Reset fields
        _flagGameSpecified = false;
        _selectedGamePath = string.Empty;
        _titleIdInt = 0;
        _titleIdHex = string.Empty;
        _gameType = 0;
        _cucholixRepoId = string.Empty;

        GameSourceDirectory.Text = "Game file has not been specified";
        InternalGameName.Text = string.Empty;
        InternalGameID.Text = string.Empty;
        PackedTitleLine1.Text = string.Empty;
        PackedTitleIDLine.Text = string.Empty;

        // Enable/disable components based on type
        GC2SourceButton.IsEnabled = (_systemType == "gcn");
        GC2SourceDirectory.Text = (_systemType == "gcn") ? "2nd GameCube Disc Image has not been specified" : "N/A";

        RepoDownload.IsEnabled = (_systemType == "wii" || _systemType == "gcn" || _systemType == "wiiware");

        GameSourceButton.IsEnabled = (_systemType != "wiiware");
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var settingsWin = new SettingsWindow();
        settingsWin.ShowDialog(this);
    }

    private void OnSdCardMenuClick(object sender, RoutedEventArgs e)
    {
        var sdMenu = new SdCardMenuAvalonia();
        sdMenu.ShowDialog(this);
    }

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Items.Count > 0)
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void OnWindowDrop(object? sender, DragEventArgs e)
    {
        IEnumerable<IStorageItem>? files = null;
        if (e.DataTransfer is IAsyncDataTransfer asyncDt)
        {
            files = await asyncDt.TryGetFilesAsync();
        }

        if (files == null) return;

        foreach (var file in files)
        {
            string path = file.Path.LocalPath;
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
                    if (await ProcessImageFileAsync("Icon", path, TempIconPath, 128, 128, IconPreviewBox, IconSourceDirectory))
                        _flagIconSpecified = true;
                }
                else if (!_flagBannerSpecified)
                {
                    if (await ProcessImageFileAsync("Banner", path, TempBannerPath, 1280, 720, BannerPreviewBox, BannerSourceDirectory))
                        _flagBannerSpecified = true;
                }
                else
                {
                    await ProcessImageFileAsync("Banner", path, TempBannerPath, 1280, 720, BannerPreviewBox, BannerSourceDirectory);
                }
            }
            else if (ext is ".wav" or ".btsnd")
            {
                BootSoundDirectory.Text = path;
                try
                {
                    File.Copy(path, TempSoundPath, true);
                }
                catch { }
            }
        }
    }

    private async void OnGameSourceClick(object sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

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

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = filters
        });

        if (result != null && result.Count > 0)
        {
            await LoadGameFromFileAsync(result[0].Path.LocalPath);
        }
    }

    private async Task LoadGameFromFileAsync(string gamePath)
    {
        _selectedGamePath = gamePath;
        CleanUp();

        GameSourceDirectory.Text = _selectedGamePath;
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

            // Check gametype
            if ((_systemType == "wii" && _gameType != WiiGameType) || (_systemType == "gcn" && _gameType != GCGameType))
            {
                string err = _systemType == "wii" ? "This is not a Wii image. It will not be loaded." : "This is not a GameCube image. It will not be loaded.";
                UpdateUIForSystemType();
                await MessageBoxWindow.Show(this, err, "Error", MessageBoxButtons.Ok);
                return;
            }

            InternalGameName.Text = _internalGameName;

            var dbName = RemoveSpecialChars(GameTdb.GetName(_cucholixRepoId) ?? string.Empty);
            PackedTitleLine1.Text = !string.IsNullOrEmpty(dbName) ? dbName : _internalGameName;

            byte[] titleIdBytes = BitConverter.GetBytes(_titleIdInt);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(titleIdBytes);
            }
            _titleIdHex = BitConverter.ToString(titleIdBytes).Replace("-", "");

            if (_systemType == "dol")
            {
                InternalGameID.Text = _titleIdHex;
                PackedTitleIDLine.Text = $"00050002{_titleIdHex}";
                _titleIdText = "BOOT";
            }
            else
            {
                _titleIdText = Encoding.ASCII.GetString(Convert.FromHexString(_titleIdHex));
                InternalGameID.Text = $"{_titleIdText} / {_titleIdHex}";
                PackedTitleIDLine.Text = $"00050002{_titleIdHex}";
            }
        }
        catch (Exception ex)
        {
            await MessageBoxWindow.Show(this, $"Failed to read game file metadata: {ex.Message}", "Error", MessageBoxButtons.Ok);
            UpdateUIForSystemType();
        }
    }

    private string ReadNullTerminatedString(Stream stream)
    {
        List<byte> bytes = [];
        int b;
        while (stream.Position < stream.Length && (b = stream.ReadByte()) > 0)
        {
            bytes.Add((byte)b);
        }
        return Encoding.UTF8.GetString([.. bytes]);
    }

    private void CleanUp()
    {
        FileUtil.SafeDeleteDirectory(TempSourcePath);
        FileUtil.SafeDeleteDirectory(TempBuildPath);
        try
        {
            Directory.CreateDirectory(TempSourcePath);
            Directory.CreateDirectory(TempBuildPath);
        }
        catch { }

        IconPreviewBox.Source = null;
        BannerPreviewBox.Source = null;

        _flagIconSpecified = false;
        _flagBannerSpecified = false;

        IconSourceDirectory.Text = "Icon has not been specified";
        BannerSourceDirectory.Text = "Banner has not been specified";
    }

    private async Task<bool> ProcessImageFileAsync(string imageType, string path, string tempPath, int width, int height, Image previewBox, TextBox sourceDirBox)
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
                previewBox.Source = new Bitmap(stream);
            }

            sourceDirBox.Text = path;
            return true;
        }
        catch (Exception ex)
        {
            await MessageBoxWindow.Show(this, $"Failed to load {imageType.ToLower()}: {ex.Message}", "Error", MessageBoxButtons.Ok);
            return false;
        }
    }

    private async Task<bool> SelectAndProcessImageAsync(string imageType, string tempPath, int width, int height, Image previewBox, TextBox sourceDirBox)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return false;

        await MessageBoxWindow.Show(this, $"Make sure your {imageType} is {width}x{height} to prevent distortion", $"{imageType} Size Information", MessageBoxButtons.Ok);

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select {imageType} PNG or TGA",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.tga"] }]
        });

        if (result != null && result.Count > 0)
        {
            return await ProcessImageFileAsync(imageType, result[0].Path.LocalPath, tempPath, width, height, previewBox, sourceDirBox);
        }
        return false;
    }

    private async void OnIconSourceClick(object sender, RoutedEventArgs e)
    {
        if (await SelectAndProcessImageAsync("Icon", TempIconPath, 128, 128, IconPreviewBox, IconSourceDirectory))
        {
            _flagIconSpecified = true;
        }
    }

    private async void OnBannerSourceClick(object sender, RoutedEventArgs e)
    {
        if (await SelectAndProcessImageAsync("Banner", TempBannerPath, 1280, 720, BannerPreviewBox, BannerSourceDirectory))
        {
            _flagBannerSpecified = true;
        }
    }

    private async void OnRepoDownloadClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_cucholixRepoId))
        {
            await MessageBoxWindow.Show(this, "Could not identify game to download repository files for", "Error", MessageBoxButtons.Ok);
            return;
        }

        string baseUrl = "https://raw.githubusercontent.com/cucholix/wii-u-virtual-console-autoboot-icons/master/";
        string iconUrl = $"{baseUrl}iconTex/{_cucholixRepoId}.png";
        string bannerUrl = $"{baseUrl}bootTvTex/{_cucholixRepoId}.png";
        string drcUrl = $"{baseUrl}bootDrcTex/{_cucholixRepoId}.png";
        string logoUrl = $"{baseUrl}bootLogoTex/{_cucholixRepoId}.png";
        string soundUrl = $"{baseUrl}bootSound/{_cucholixRepoId}.btsnd";

        bool anyDownloaded = false;

        if (await UrlExistsAsync(iconUrl))
        {
            byte[] iconBytes = await Program.Client.GetByteArrayAsync(iconUrl);
            await File.WriteAllBytesAsync(TempIconPath, iconBytes);
            using (var stream = new MemoryStream(iconBytes))
            {
                IconPreviewBox.Source = new Bitmap(stream);
            }
            IconSourceDirectory.Text = "Downloaded from cucholix's repository";
            _flagIconSpecified = true;
            anyDownloaded = true;
        }

        if (await UrlExistsAsync(bannerUrl))
        {
            byte[] bannerBytes = await Program.Client.GetByteArrayAsync(bannerUrl);
            await File.WriteAllBytesAsync(TempBannerPath, bannerBytes);
            using (var stream = new MemoryStream(bannerBytes))
            {
                BannerPreviewBox.Source = new Bitmap(stream);
            }
            BannerSourceDirectory.Text = "Downloaded from cucholix's repository";
            _flagBannerSpecified = true;
            anyDownloaded = true;
        }

        if (await UrlExistsAsync(drcUrl))
        {
            byte[] drcBytes = await Program.Client.GetByteArrayAsync(drcUrl);
            await File.WriteAllBytesAsync(TempDrcPath, drcBytes);
            using (var stream = new MemoryStream(drcBytes))
            {
                DrcPreviewBox.Source = new Bitmap(stream);
            }
            DrcSourceDirectory.Text = "Downloaded from cucholix's repository";
            anyDownloaded = true;
        }

        if (await UrlExistsAsync(logoUrl))
        {
            byte[] logoBytes = await Program.Client.GetByteArrayAsync(logoUrl);
            await File.WriteAllBytesAsync(TempLogoPath, logoBytes);
            using (var stream = new MemoryStream(logoBytes))
            {
                LogoPreviewBox.Source = new Bitmap(stream);
            }
            LogoSourceDirectory.Text = "Downloaded from cucholix's repository";
            anyDownloaded = true;
        }

        if (await UrlExistsAsync(soundUrl))
        {
            byte[] soundBytes = await Program.Client.GetByteArrayAsync(soundUrl);
            await File.WriteAllBytesAsync(TempSoundPath, soundBytes);
            BootSoundDirectory.Text = "Downloaded from cucholix's repository";
            anyDownloaded = true;
        }

        if (!anyDownloaded)
        {
            await MessageBoxWindow.Show(this, "Could not find any files matching the specified Game Title ID in cucholix's repository", "Error", MessageBoxButtons.Ok);
        }
    }

    private async Task<bool> UrlExistsAsync(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await Program.Client.SendAsync(request);
            return response != null && response.StatusCode == System.Net.HttpStatusCode.OK;
        }
        catch
        {
            return false;
        }
    }

    private async void OnGC2SourceClick(object sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select 2nd GameCube Disc Image",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("GameCube Disc") { Patterns = ["*.iso", "*.gcm"] }]
        });

        if (result != null && result.Count > 0)
        {
            var path = result[0].Path.LocalPath;
            try
            {
                using var fs = File.OpenRead(path);
                fs.Position = 0x18;
                byte[] typeBytes = new byte[8];
                fs.ReadExactly(typeBytes);
                long gc2GameType = BitConverter.ToInt64(typeBytes);

                if (gc2GameType != 4440324665927270400)
                {
                    await MessageBoxWindow.Show(this, "This is not a GameCube image. It will not be loaded.", "Error", MessageBoxButtons.Ok);
                    GC2SourceDirectory.Text = "2nd GameCube Disc Image has not been specified";
                    _flagGc2Specified = false;
                }
                else
                {
                    GC2SourceDirectory.Text = path;
                    _flagGc2Specified = true;
                }
            }
            catch (Exception ex)
            {
                await MessageBoxWindow.Show(this, $"Failed to read 2nd disc: {ex.Message}", "Error", MessageBoxButtons.Ok);
            }
        }
    }

    private async void OnBootSoundClick(object sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        await MessageBoxWindow.Show(this, "Your sound file will be cut off if it's longer than 6 seconds to prevent the Wii U from not loading it.", "Boot Sound Information", MessageBoxButtons.Ok);

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Boot Sound (WAV)",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("WAV Audio") { Patterns = ["*.wav"] }]
        });

        if (result != null && result.Count > 0)
        {
            var path = result[0].Path.LocalPath;
            try
            {
                using var fs = File.OpenRead(path);
                {
                    byte[] headerBytes = new byte[4];
                    fs.Position = 0x00;
                    fs.ReadExactly(headerBytes);
                    int wavHeader1 = BitConverter.ToInt32(headerBytes);

                    fs.Position = 0x08;
                    fs.ReadExactly(headerBytes);
                    int wavHeader2 = BitConverter.ToInt32(headerBytes);

                    if (wavHeader1 == 1179011410 && wavHeader2 == 1163280727)
                    {
                        BootSoundDirectory.Text = path;
                    }
                    else
                    {
                        await MessageBoxWindow.Show(this, "This is not a valid WAV file. It will not be loaded.", "Not a WAV File", MessageBoxButtons.Ok);
                        BootSoundDirectory.Text = "Boot Sound has not been specified";
                    }
                }
            }
            catch (Exception ex)
            {
                await MessageBoxWindow.Show(this, $"Failed to read WAV: {ex.Message}", "Error", MessageBoxButtons.Ok);
            }
        }
    }

    private void OnBootSoundPreviewClick(object sender, RoutedEventArgs e)
    {
        // Placeholder for sound playing preview
    }

    private async void OnLogoSourceClick(object sender, RoutedEventArgs e)
    {
        await SelectAndProcessImageAsync("Logo", TempLogoPath, 170, 42, LogoPreviewBox, LogoSourceDirectory);
    }

    private async void OnDrcSourceClick(object sender, RoutedEventArgs e)
    {
        await SelectAndProcessImageAsync("GamePad Banner", TempDrcPath, 854, 480, DrcPreviewBox, DrcSourceDirectory);
    }

    private void OnMainTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl tabControl)
        {
            var selectedTab = tabControl.SelectedItem as TabItem;
            if (selectedTab is { Header: not null } && selectedTab.Header.ToString() == "Build Title")
            {
                // Update key text boxes from settings
                WiiUCommonKey.Text = string.IsNullOrEmpty(Properties.Settings.Default.WiiUCommonKey)
                    ? ""
                    : Properties.Settings.Default.WiiUCommonKey.ToUpper();

                TitleKey.Text = string.IsNullOrEmpty(Properties.Settings.Default.TitleKey)
                    ? ""
                    : Properties.Settings.Default.TitleKey.ToUpper();

                AncastKey.Text = string.IsNullOrEmpty(Properties.Settings.Default.AncastKey)
                    ? ""
                    : Properties.Settings.Default.AncastKey.ToUpper();

                _commonKeyGood = SetKeyStatus(WiiUCommonKey, "35-AC-59-94-97-22-79-33-1D-97-09-4F-A2-FB-97-FC");
                _titleKeyGood = SetKeyStatus(TitleKey, "F9-4B-D8-8E-BB-7A-A9-38-67-E6-30-61-5F-27-1C-9F");
                _ancastKeyGood = SetKeyStatus(AncastKey, "31-8D-1F-9D-98-FB-08-E7-7C-7F-E1-77-AA-49-05-43");

                // Check checklist
                _buildFlagSource = _flagGameSpecified && _flagIconSpecified && _flagBannerSpecified;
                SourceCheck.IsChecked = _buildFlagSource;

                _buildFlagMeta = !string.IsNullOrEmpty(PackedTitleLine1.Text) && PackedTitleIDLine.Text?.Length == 16;
                MetaCheck.IsChecked = _buildFlagMeta;

                _buildFlagAdvance = true; // Simpler default
                AdvanceCheck.IsChecked = _buildFlagAdvance;

                bool skipAncast = LRPatch.IsChecked != true;
                _buildFlagKeys = skipAncast ? (_commonKeyGood && _titleKeyGood) : (_commonKeyGood && _titleKeyGood && _ancastKeyGood);

                KeysCheck.IsChecked = _buildFlagKeys;

                UpdateBuildButtonState();
            }
        }
    }

    private bool SetKeyStatus(TextBox keyTextBox, string expectedHash)
    {
        if (string.IsNullOrEmpty(keyTextBox.Text)) return false;
        keyTextBox.Text = keyTextBox.Text.ToUpper();
        byte[] data = MD5.HashData(Encoding.ASCII.GetBytes(keyTextBox.Text));
        string hash = BitConverter.ToString(data);
        bool isValid = string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase);
        keyTextBox.IsReadOnly = isValid;
        keyTextBox.Background = isValid ? Avalonia.Media.Brush.Parse("#c8e6c9") : Avalonia.Media.Brush.Parse("White");
        keyTextBox.Foreground = isValid ? Avalonia.Media.Brush.Parse("#1b4332") : Avalonia.Media.Brush.Parse("Black");

        // Disable button if key is valid
        Button? button = keyTextBox.Name switch
        {
            "WiiUCommonKey" => SaveCommonKeyButton,
            "TitleKey" => SaveTitleKeyButton,
            "AncastKey" => SaveAncastKeyButton,
            _ => null
        };
        if (button != null)
        {
            button.IsEnabled = !isValid;
        }

        return isValid;
    }

    private void OnKeyTextBoxDoubleTapped(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.IsReadOnly = false;
            textBox.Background = Avalonia.Media.Brush.Parse("White");
            textBox.Foreground = Avalonia.Media.Brush.Parse("Black");

            // Enable corresponding save button
            Button? button = textBox.Name switch
            {
                "WiiUCommonKey" => SaveCommonKeyButton,
                "TitleKey" => SaveTitleKeyButton,
                "AncastKey" => SaveAncastKeyButton,
                _ => null
            };
            if (button != null)
            {
                button.IsEnabled = true;
            }
        }
    }

    private bool SaveKey(TextBox textBox, string hash, Action<string> saveSetting)
    {
        if (string.IsNullOrEmpty(textBox.Text)) return false;
        saveSetting(textBox.Text);
        Properties.Settings.Default.Save();
        return SetKeyStatus(textBox, hash);
    }

    private void OnSaveCommonKeyClick(object sender, RoutedEventArgs e)
    {
        _commonKeyGood = SaveKey(WiiUCommonKey, "35-AC-59-94-97-22-79-33-1D-97-09-4F-A2-FB-97-FC", text => Properties.Settings.Default.WiiUCommonKey = text);
        UpdateBuildButtonState();
    }

    private void OnSaveTitleKeyClick(object sender, RoutedEventArgs e)
    {
        _titleKeyGood = SaveKey(TitleKey, "F9-4B-D8-8E-BB-7A-A9-38-67-E6-30-61-5F-27-1C-9F", text => Properties.Settings.Default.TitleKey = text);
        UpdateBuildButtonState();
    }

    private void OnSaveAncastKeyClick(object sender, RoutedEventArgs e)
    {
        _ancastKeyGood = SaveKey(AncastKey, "31-8D-1F-9D-98-FB-08-E7-7C-7F-E1-77-AA-49-05-43", text => Properties.Settings.Default.AncastKey = text);
        UpdateBuildButtonState();
    }

    private void UpdateBuildButtonState()
    {
        // 1. Source files checklist
        _buildFlagSource = _flagGameSpecified && _flagIconSpecified && _flagBannerSpecified;
        SourceCheck.IsChecked = _buildFlagSource;

        // 2. Metadata checklist
        _buildFlagMeta = !string.IsNullOrEmpty(PackedTitleLine1.Text) && PackedTitleIDLine.Text?.Length == 16;
        MetaCheck.IsChecked = _buildFlagMeta;

        // 3. Advanced checklist
        _buildFlagAdvance = true;
        AdvanceCheck.IsChecked = _buildFlagAdvance;

        // 4. Keys checklist
        AncastKeyBorder.IsVisible = C2WPatchFlag.IsChecked == true;

        bool skipAncast = C2WPatchFlag.IsChecked != true;
        _buildFlagKeys = skipAncast ? (_commonKeyGood && _titleKeyGood) : (_commonKeyGood && _titleKeyGood && _ancastKeyGood);
        KeysCheck.IsChecked = _buildFlagKeys;

        // 5. Main Build Button
        TheBigOneTM.IsEnabled = _buildFlagSource && _buildFlagMeta && _buildFlagAdvance && _buildFlagKeys;
    }

    private void OnC2WPatchFlagClick(object sender, RoutedEventArgs e)
    {
        UpdateBuildButtonState();
    }

    private bool IsCommandAvailable(string cmd)
    {
        try
        {
            using var p = new Process();
            p.StartInfo.FileName = "which";
            p.StartInfo.Arguments = cmd;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.Start();
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public void LaunchProgram(string exeFile, string arguments = "", bool hideProcess = true)
    {
        string targetExe = exeFile;
        string targetArgs = arguments;

        if (Environment.OSVersion.Platform == PlatformID.Unix)
        {
            string fileName = Path.GetFileName(exeFile).ToLowerInvariant();

            if (fileName == "wit.exe" && IsCommandAvailable("wit"))
            {
                targetExe = "wit";
            }
            else if (fileName == "sox.exe" && IsCommandAvailable("sox"))
            {
                targetExe = "sox";
            }
            else
            {
                string jarFile = Path.ChangeExtension(exeFile, ".jar");
                if (!File.Exists(jarFile))
                {
                    string localJar = Path.Combine(TempToolsPath, "JAR", Path.ChangeExtension(Path.GetFileName(exeFile), ".jar"));
                    if (File.Exists(localJar))
                    {
                        jarFile = localJar;
                    }
                    else
                    {
                        string currentDirJar = Path.Combine(Directory.GetCurrentDirectory(), Path.ChangeExtension(Path.GetFileName(exeFile), ".jar"));
                        if (File.Exists(currentDirJar))
                        {
                            jarFile = currentDirJar;
                        }
                    }
                }

                if (File.Exists(jarFile))
                {
                    targetExe = "java";
                    targetArgs = $"-jar \"{jarFile}\" {arguments}";
                }
                else if (exeFile.Contains("/JAR/") && fileName.EndsWith(".exe"))
                {
                    targetExe = "wine";
                    targetArgs = $"\"{exeFile}\" {arguments}";
                }
                else if (exeFile.EndsWith(".exe") || exeFile.Contains("/TOOLDIR/"))
                {
                    targetExe = "wine";
                    targetArgs = $"\"{exeFile}\" {arguments}";
                }
            }
        }

        ProcessStartInfo launcher = new(targetExe)
        {
            Arguments = targetArgs,
            UseShellExecute = false,
            WorkingDirectory = Directory.GetCurrentDirectory()
        };
        if (hideProcess)
        {
            launcher.WindowStyle = ProcessWindowStyle.Hidden;
            launcher.CreateNoWindow = true;
        }
        using var process = Process.Start(launcher);
        process?.WaitForExit();
    }

    private async void OnBuildClick(object sender, RoutedEventArgs e)
    {
        MainTabs.IsEnabled = false;
        BuildProgress.Value = 0;
        BuildStatus.Text = "Initializing Build Process...";

        await _setupTask;

        // Check drive space (simulated/implemented on Linux)
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
                    var res = await MessageBoxWindow.Show(this,
                        "Your hard drive may be low on space. The conversion process involves temporary files that can amount to more than double the size of your game. Do you want to continue anyway?",
                        "Check your hard drive space", MessageBoxButtons.YesNo);
                    if (res == MessageBoxResult.No)
                    {
                        MainTabs.IsEnabled = true;
                        BuildStatus.Text = "";
                        return;
                    }
                }
            }
            catch { }
        }

        // Get selected output path
        string selectedOutputPath = "";
        if (!string.IsNullOrEmpty(Properties.Settings.Default.OutputPathFixed))
        {
            selectedOutputPath = Properties.Settings.Default.OutputPathFixed;
        }
        else
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Specify your output folder",
                });
                if (folders == null || folders.Count == 0)
                {
                    await MessageBoxWindow.Show(this, "Output folder selection has been cancelled, conversion will not continue.", "Cancelled", MessageBoxButtons.Ok);
                    MainTabs.IsEnabled = true;
                    BuildStatus.Text = "";
                    return;
                }
                selectedOutputPath = folders[0].Path.LocalPath;
                Properties.Settings.Default.OutputPath = selectedOutputPath;
                Properties.Settings.Default.Save();
            }
        }

        BuildProgress.Value = 2;

        // Retrieve control values on UI Thread
        var wiiUCommonKey = WiiUCommonKey.Text ?? "";
        var titleKey = TitleKey.Text ?? "";
        var ancastKey = AncastKey.Text ?? "";
        var packedTitleIDLine = PackedTitleIDLine.Text ?? "";
        var packedTitleLine1 = PackedTitleLine1.Text ?? "";
        var packedTitleLine2 = PackedTitleLine2.Text ?? "";
        var enablePackedLine2 = EnablePackedLine2.IsChecked == true;
        var wiimmfi = Wiimmfi.IsChecked == true;
        var wiiVMC = WiiVMC.IsChecked == true;
        var disableTrimming = DisableTrimming.IsChecked == true;
        var disableNintendontAutoboot = DisableNintendontAutoboot.IsChecked == true;
        var c2wPatch = C2WPatchFlag.IsChecked == true;
        var lrPatch = LRPatch.IsChecked == true;

        // Resolve optional paths
        var soundDir = BootSoundDirectory.Text ?? "";
        var logoDir = LogoSourceDirectory.Text ?? "";
        var drcDir = DrcSourceDirectory.Text ?? "";

        // Resolve GC2 path if GC retail
        string gc2Path = "";
        if (_systemType == "gcn" && _flagGc2Specified)
        {
            gc2Path = GC2SourceDirectory.Text ?? "";
        }

        // Resolve boot sound loop option
        bool toggleBootSoundLoop = ToggleBootSoundLoop.IsChecked == true;

        // Resolve gamepad emulation flags
        string nfsPatchFlag = "";
        var horWiiMote = HorWiiMote.IsChecked == true;
        var verWiiMote = VerWiiMote.IsChecked == true;
        var ccemu = CCEmu.IsChecked == true;
        var forceCC = ForceCC.IsChecked == true;
        var forceNoCC = ForceNoCC.IsChecked == true;

        if (horWiiMote) nfsPatchFlag = " -horizontal";
        else if (verWiiMote) nfsPatchFlag = " -wiimote";
        else if (ccemu) nfsPatchFlag = " -nocc";
        else if (forceCC) nfsPatchFlag = " -instantcc";
        else if (forceNoCC) nfsPatchFlag = " -nocc";

        string drcuse = "1";
        var disableGamePad = DisableGamePad.IsChecked == true;
        if (disableGamePad) drcuse = "65537";

        var options = new BuildOptions
        {
            SystemType = _systemType,
            SelectedGamePath = _selectedGamePath,
            SelectedOutputPath = selectedOutputPath,
            WiiUCommonKey = wiiUCommonKey,
            TitleKey = titleKey,
            AncastKey = ancastKey,
            PackedTitleIDLine = packedTitleIDLine,
            PackedTitleLine1 = packedTitleLine1,
            PackedTitleLine2 = packedTitleLine2,
            EnablePackedLine2 = enablePackedLine2,
            Wiimmfi = wiimmfi,
            WiiVMC = wiiVMC,
            DisableTrimming = disableTrimming,
            DisableNintendontAutoboot = disableNintendontAutoboot,
            C2WPatch = c2wPatch,
            LRPatch = lrPatch,
            SoundDir = soundDir,
            LogoDir = logoDir,
            DrcDir = drcDir,
            Gc2Path = gc2Path,
            ToggleBootSoundLoop = toggleBootSoundLoop,
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
            UpdateStatus(update.Message, update.Progress);
        });

        _buildCts = new CancellationTokenSource();
        if (_cancelBuildButton != null)
        {
            _cancelBuildButton.IsVisible = true;
            _cancelBuildButton.IsEnabled = true;
        }

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
            if (_cancelBuildButton != null)
            {
                _cancelBuildButton.IsVisible = false;
            }
            // Save conversion log
            await builder.SaveLogAsync(selectedOutputPath);
            await builder.SaveLogAsync(TempRootPath);
        }

        // Update UI thread after complete
        MainTabs.IsEnabled = true;
        BuildProgress.Value = success ? 100 : 0;
        BuildStatus.Text = success ? "Conversion complete!" : (_buildCts?.IsCancellationRequested == true ? "Conversion cancelled." : "Conversion failed.");

        if (success)
        {
            var res = await MessageBoxWindow.Show(this,
                $"Conversion Complete! Your packed game can be found here:\n{finalOutputPath}\n\nInstall your title using WUP Installer GX2 with signature patches enabled.\n\nOpen the output folder now?",
                "Conversion Complete", MessageBoxButtons.YesNo);
            if (res == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(finalOutputPath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    await MessageBoxWindow.Show(this, $"Could not open the output folder:\n{ex.Message}", "Error", MessageBoxButtons.Ok);
                }
            }
        }
        else if (_buildCts?.IsCancellationRequested == true)
        {
            await MessageBoxWindow.Show(this, "The build operation was cancelled. Temporary files were cleaned up.", "Operation Cancelled", MessageBoxButtons.Ok);
        }
        else
        {
            await MessageBoxWindow.Show(this, $"Conversion Failed!\n{errorMsg}\n\nA detailed log has been saved in the output directory (and temporary folder).", "Conversion Failed", MessageBoxButtons.Ok);
        }
    }

    private void OnCancelBuildClick(object? sender, RoutedEventArgs e)
    {
        if (_buildCts != null && !_buildCts.IsCancellationRequested)
        {
            _buildCts.Cancel();
            BuildStatus.Text = "Cancelling build...";
            if (_cancelBuildButton != null)
            {
                _cancelBuildButton.IsEnabled = false;
            }
            AppendLogMessage("[USER] Build cancellation requested.");
        }
    }

    private void AppendLogMessage(string message)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_logOutputBox != null)
            {
                _logOutputBox.Text = string.IsNullOrEmpty(_logOutputBox.Text)
                    ? message
                    : $"{_logOutputBox.Text}{Environment.NewLine}{message}";
                _logOutputBox.CaretIndex = _logOutputBox.Text?.Length ?? 0;
            }
        });
    }

    private async void OnCopyLogsClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var logText = _logOutputBox?.Text;
        if (topLevel?.Clipboard != null && !string.IsNullOrEmpty(logText))
        {
            var dataTransfer = new DataTransfer();
            dataTransfer.Add(DataTransferItem.CreateText(logText));
            await topLevel.Clipboard.SetDataAsync(dataTransfer);
            await MessageBoxWindow.Show(this, "Logs copied to clipboard.", "Logs Copied", MessageBoxButtons.Ok);
        }
    }

    private void OnClearLogsClick(object? sender, RoutedEventArgs e)
    {
        if (_logOutputBox != null)
        {
            _logOutputBox.Text = string.Empty;
        }
    }

    private async void OnOpenLogFolderClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            string logFolder = !string.IsNullOrEmpty(Properties.Settings.Default.OutputPath) && Directory.Exists(Properties.Settings.Default.OutputPath)
                ? Properties.Settings.Default.OutputPath
                : TempRootPath;

            if (Directory.Exists(logFolder))
            {
                Process.Start(new ProcessStartInfo(logFolder) { UseShellExecute = true });
            }
            else
            {
                await MessageBoxWindow.Show(this, $"Log folder '{logFolder}' does not exist.", "Notice", MessageBoxButtons.Ok);
            }
        }
        catch (Exception ex)
        {
            await MessageBoxWindow.Show(this, $"Failed to open folder: {ex.Message}", "Error", MessageBoxButtons.Ok);
        }
    }

    private void UpdateStatus(string message, double progressValue)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            BuildStatus.Text = message;
            BuildProgress.Value = progressValue;
        });
    }

    private string GetLanguageSuffix(int index)
    {
        return index switch
        {
            0 => "ja",
            1 => "en",
            2 => "fr",
            3 => "de",
            4 => "it",
            5 => "es",
            6 => "zhs",
            7 => "ko",
            8 => "nl",
            9 => "pt",
            10 => "ru",
            _ => "en"
        };
    }

    private string SanitizeFilename(string str)
    {
        var invalids = Path.GetInvalidFileNameChars();
        return string.Join("_", str.Split(invalids, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
    }

    private static string GetMD5Checksum(string file)
    {
        using var stream = File.OpenRead(file);
        byte[] checksum = MD5.HashData(stream);
        return Convert.ToHexString(checksum);
    }

    private static string RemoveSpecialChars(string v)
    {
        if (string.IsNullOrEmpty(v))
            return v;

        string s = RemoveDiacritics(v);
        return new string([.. s.Where(c => c < 128)]);
    }

    private static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

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

        return stringBuilder
            .ToString()
            .Normalize(NormalizationForm.FormC);
    }
}
