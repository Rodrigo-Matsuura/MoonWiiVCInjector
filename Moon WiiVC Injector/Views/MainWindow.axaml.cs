using System;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Media.Imaging;

namespace Moon_WiiVC_Injector
{
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

        public MainWindow()
        {
            InitializeComponent();
            SetupEnvironment();
            WireEvents();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void SetupEnvironment()
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
            }
            catch { }

            // Create Source and Build directories
            try
            {
                Directory.CreateDirectory(TempSourcePath);
                Directory.CreateDirectory(TempBuildPath);
            }
            catch { }
        }

        private void WireEvents()
        {
            var wiiRetail = this.FindControl<RadioButton>("WiiRetail");
            var wiiHomebrew = this.FindControl<RadioButton>("WiiHomebrew");
            var wiiNAND = this.FindControl<RadioButton>("WiiNAND");
            var gcRetail = this.FindControl<RadioButton>("GCRetail");

            if (wiiRetail != null) wiiRetail.IsCheckedChanged += WiiRetail_Checked;
            if (wiiHomebrew != null) wiiHomebrew.IsCheckedChanged += WiiHomebrew_Checked;
            if (wiiNAND != null) wiiNAND.IsCheckedChanged += WiiNAND_Checked;
            if (gcRetail != null) gcRetail.IsCheckedChanged += GCRetail_Checked;

            // Trigger initial selection updates
            UpdateUIForSystemType();
        }

        private void WiiRetail_Checked(object? sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.IsChecked == true)
            {
                _systemType = "wii";
                UpdateUIForSystemType();
            }
        }

        private void WiiHomebrew_Checked(object? sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.IsChecked == true)
            {
                _systemType = "dol";
                UpdateUIForSystemType();
            }
        }

        private async void WiiNAND_Checked(object? sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.IsChecked == true)
            {
                _systemType = "wiiware";
                UpdateUIForSystemType();

                // Prompt for Title ID
                string inputId = await GuiUtil.PromptInputAsync(this, 
                    "Enter your installed Wii Channel's 4-letter Title ID. If you don't know it, open a WAD for the channel in something like ShowMiiWads to view it.",
                    "Enter your WAD's Title ID", "XXXX");

                var gameSourceDir = this.FindControl<TextBox>("GameSourceDirectory");
                var packedTitleId = this.FindControl<TextBox>("PackedTitleIDLine");

                if (string.IsNullOrEmpty(inputId))
                {
                    if (gameSourceDir != null) gameSourceDir.Text = "Title ID specification cancelled, reselect vWii NAND Title Launcher to specify";
                    _flagGameSpecified = false;
                    return;
                }

                if (inputId.Length == 4)
                {
                    if (gameSourceDir != null) gameSourceDir.Text = inputId.ToUpper();
                    _flagGameSpecified = true;
                    _titleIdText = inputId.ToUpper();
                    _cucholixRepoId = inputId.ToUpper();

                    StringBuilder sb = new StringBuilder();
                    foreach (char c in _titleIdText)
                    {
                        sb.Append(((short)c).ToString("X2"));
                    }
                    if (packedTitleId != null) packedTitleId.Text = "00050002" + sb.ToString();
                }
                else
                {
                    if (gameSourceDir != null) gameSourceDir.Text = "Invalid Title ID";
                    _flagGameSpecified = false;
                    await MessageBoxWindow.Show(this, 
                        "Only 4 characters can be used, try again. Example: The Star Fox 64 (USA) Channel's Title ID is NADE01, so you would specify NADE as the Title ID",
                        "Invalid Title ID", MessageBoxButtons.Ok);
                }
            }
        }

        private void GCRetail_Checked(object? sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.IsChecked == true)
            {
                _systemType = "gcn";
                UpdateUIForSystemType();
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

            var gameSourceDir = this.FindControl<TextBox>("GameSourceDirectory");
            if (gameSourceDir != null) gameSourceDir.Text = "Game file has not been specified";

            var gameNameTxt = this.FindControl<TextBox>("InternalGameName");
            if (gameNameTxt != null) gameNameTxt.Text = string.Empty;

            var gameIdTxt = this.FindControl<TextBox>("InternalGameID");
            if (gameIdTxt != null) gameIdTxt.Text = string.Empty;

            var packedTitleLine1 = this.FindControl<TextBox>("PackedTitleLine1");
            if (packedTitleLine1 != null) packedTitleLine1.Text = string.Empty;

            var packedTitleIDLine = this.FindControl<TextBox>("PackedTitleIDLine");
            if (packedTitleIDLine != null) packedTitleIDLine.Text = string.Empty;

            // Enable/disable components based on type
            var gc2Button = this.FindControl<Button>("GC2SourceButton");
            var gc2Dir = this.FindControl<TextBox>("GC2SourceDirectory");
            if (gc2Button != null) gc2Button.IsEnabled = (_systemType == "gcn");
            if (gc2Dir != null) gc2Dir.Text = (_systemType == "gcn") ? "2nd GameCube Disc Image has not been specified" : "N/A";

            var repoBtn = this.FindControl<Button>("RepoDownload");
            if (repoBtn != null) repoBtn.IsEnabled = (_systemType == "wii" || _systemType == "gcn" || _systemType == "wiiware");

            var gameBtn = this.FindControl<Button>("GameSourceButton");
            if (gameBtn != null) gameBtn.IsEnabled = (_systemType != "wiiware");
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

        private async void OnGameSourceClick(object sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            string title = "Select game file";
            var filters = new List<FilePickerFileType>();

            if (_systemType == "wii")
            {
                title = "Select Wii game dump (ISO, WBFS)";
                filters.Add(new FilePickerFileType("Wii Game Dumps") { Patterns = new[] { "*.iso", "*.wbfs" } });
            }
            else if (_systemType == "gcn")
            {
                title = "Select GameCube game dump (ISO, GCM)";
                filters.Add(new FilePickerFileType("GameCube Game Dumps") { Patterns = new[] { "*.iso", "*.gcm" } });
            }
            else if (_systemType == "dol")
            {
                title = "Select Homebrew executable (DOL)";
                filters.Add(new FilePickerFileType("DOL Executable") { Patterns = new[] { "*.dol" } });
            }

            var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = filters
            });

            if (result != null && result.Count > 0)
            {
                _selectedGamePath = result[0].Path.LocalPath;
                CleanUp();

                var gameSourceDir = this.FindControl<TextBox>("GameSourceDirectory");
                if (gameSourceDir != null) gameSourceDir.Text = _selectedGamePath;

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

                    var gameNameTxt = this.FindControl<TextBox>("InternalGameName");
                    if (gameNameTxt != null) gameNameTxt.Text = _internalGameName;

                    var dbName = StringUtil.RemoveSpecialChars(GameTdb.GetName(_cucholixRepoId) ?? string.Empty);
                    var packedTitle1 = this.FindControl<TextBox>("PackedTitleLine1");
                    if (packedTitle1 != null) packedTitle1.Text = !string.IsNullOrEmpty(dbName) ? dbName : _internalGameName;

                    byte[] titleIdBytes = BitConverter.GetBytes(_titleIdInt);
                    if (!BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(titleIdBytes);
                    }
                    _titleIdHex = BitConverter.ToString(titleIdBytes).Replace("-", "");

                    var gameIdTxt = this.FindControl<TextBox>("InternalGameID");
                    var packedTitleIDLine = this.FindControl<TextBox>("PackedTitleIDLine");

                    if (_systemType == "dol")
                    {
                        if (gameIdTxt != null) gameIdTxt.Text = _titleIdHex;
                        if (packedTitleIDLine != null) packedTitleIDLine.Text = $"00050002{_titleIdHex}";
                        _titleIdText = "BOOT";
                    }
                    else
                    {
                        _titleIdText = string.Join("", System.Text.RegularExpressions.Regex.Split(_titleIdHex, "(?<=\\G..)(?!$)").Select(x => (char)Convert.ToByte(x, 16)));
                        if (gameIdTxt != null) gameIdTxt.Text = $"{_titleIdText} / {_titleIdHex}";
                        if (packedTitleIDLine != null) packedTitleIDLine.Text = $"00050002{_titleIdHex}";
                    }
                }
                catch (Exception ex)
                {
                    await MessageBoxWindow.Show(this, $"Failed to read game file metadata: {ex.Message}", "Error", MessageBoxButtons.Ok);
                    UpdateUIForSystemType();
                }
            }
        }

        private string ReadNullTerminatedString(Stream stream)
        {
            StringBuilder sb = new StringBuilder();
            int b;
            while (stream.Position < stream.Length && (b = stream.ReadByte()) > 0)
            {
                sb.Append((char)b);
            }
            return sb.ToString();
        }

        private void CleanUp()
        {
            try
            {
                if (Directory.Exists(TempSourcePath))
                {
                    var sourceFiles = Directory.EnumerateFiles(TempSourcePath, "*.*", SearchOption.AllDirectories);
                    foreach (var file in sourceFiles) File.Delete(file);
                }
                if (Directory.Exists(TempBuildPath))
                {
                    var buildFiles = Directory.EnumerateFiles(TempBuildPath, "*.*", SearchOption.AllDirectories);
                    foreach (var file in buildFiles) File.Delete(file);
                }
            }
            catch { }

            var iconBox = this.FindControl<Image>("IconPreviewBox");
            if (iconBox != null) iconBox.Source = null;

            var bannerBox = this.FindControl<Image>("BannerPreviewBox");
            if (bannerBox != null) bannerBox.Source = null;

            _flagIconSpecified = false;
            _flagBannerSpecified = false;

            var iconDir = this.FindControl<TextBox>("IconSourceDirectory");
            if (iconDir != null) iconDir.Text = "Icon has not been specified";

            var bannerDir = this.FindControl<TextBox>("BannerSourceDirectory");
            if (bannerDir != null) bannerDir.Text = "Banner has not been specified";
        }

        private async void OnIconSourceClick(object sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            await MessageBoxWindow.Show(this, "Make sure your icon is 128x128 (1:1) to prevent distortion", "Icon Size Information", MessageBoxButtons.Ok);

            var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Icon PNG or TGA",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.tga" } } }
            });

            if (result != null && result.Count > 0)
            {
                var path = result[0].Path.LocalPath;

                try
                {
                    if (File.Exists(TempIconPath)) { File.Delete(TempIconPath); }

                    if (Path.GetExtension(path).Equals(".tga", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var bmp = TgaReader.LoadTga(path))
                        {
                            TgaReader.SaveAsTga(bmp, TempIconPath, 128, 128, 32);
                        }
                    }
                    else
                    {
                        File.Copy(path, TempIconPath, true);
                    }

                    var iconBox = this.FindControl<Image>("IconPreviewBox");
                    if (iconBox != null)
                    {
                        using (var stream = File.OpenRead(TempIconPath))
                        {
                            iconBox.Source = new Bitmap(stream);
                        }
                    }

                    var iconDir = this.FindControl<TextBox>("IconSourceDirectory");
                    if (iconDir != null) iconDir.Text = path;
                    _flagIconSpecified = true;
                }
                catch (Exception ex)
                {
                    await MessageBoxWindow.Show(this, $"Failed to load icon: {ex.Message}", "Error", MessageBoxButtons.Ok);
                }
            }
        }

        private async void OnBannerSourceClick(object sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            await MessageBoxWindow.Show(this, "Make sure your Banner is 1280x720 (16:9) to prevent distortion", "Banner Size Information", MessageBoxButtons.Ok);

            var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Banner PNG or TGA",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.tga" } } }
            });

            if (result != null && result.Count > 0)
            {
                var path = result[0].Path.LocalPath;

                try
                {
                    if (File.Exists(TempBannerPath)) { File.Delete(TempBannerPath); }

                    if (Path.GetExtension(path).Equals(".tga", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var bmp = TgaReader.LoadTga(path))
                        {
                            TgaReader.SaveAsTga(bmp, TempBannerPath, 1280, 720, 32);
                        }
                    }
                    else
                    {
                        File.Copy(path, TempBannerPath, true);
                    }

                    var bannerBox = this.FindControl<Image>("BannerPreviewBox");
                    if (bannerBox != null)
                    {
                        using (var stream = File.OpenRead(TempBannerPath))
                        {
                            bannerBox.Source = new Bitmap(stream);
                        }
                    }

                    var bannerDir = this.FindControl<TextBox>("BannerSourceDirectory");
                    if (bannerDir != null) bannerDir.Text = path;
                    _flagBannerSpecified = true;
                }
                catch (Exception ex)
                {
                    await MessageBoxWindow.Show(this, $"Failed to load banner: {ex.Message}", "Error", MessageBoxButtons.Ok);
                }
            }
        }

        private async void OnRepoDownloadClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_cucholixRepoId))
            {
                await MessageBoxWindow.Show(this, "Please select your game before using this option", "No game specified", MessageBoxButtons.Ok);
                return;
            }

            if (!await TryDownloadImagesAsync(_cucholixRepoId))
            {
                var dialogResult = await MessageBoxWindow.Show(this, 
                    "Cucholix's Repo does not have assets for your game. You will need to provide your own. Would you like to visit the GBAtemp request thread?",
                    "Game not found on Repo", 
                    MessageBoxButtons.YesNo);
                if (dialogResult == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo("https://gbatemp.net/threads/483080/") { UseShellExecute = true });
                }
            }
        }

        private async Task<bool> TryDownloadImagesAsync(string cucholixRepoID)
        {
            var ids = GameTdb.GetAlternativeIds(cucholixRepoID);
            foreach (var id in ids)
            {
                string url = Properties.Settings.Default.BannersRepository + _systemType + "/" + id + "/iconTex.png";
                if (await RemoteFileExistsAsync(url))
                {
                    await DownloadFromRepoAsync(id);
                    return true;
                }
            }
            return false;
        }

        public async Task DownloadFromRepoAsync(string cucholixRepoID)
        {
            string baseUrl = Properties.Settings.Default.BannersRepository;
            string iconUrl = $"{baseUrl}{_systemType}/{cucholixRepoID}/iconTex.png";
            string bannerUrl = $"{baseUrl}{_systemType}/{cucholixRepoID}/bootTvTex.png";

            try
            {
                if (File.Exists(TempIconPath)) { File.Delete(TempIconPath); }
                var iconBytes = await Program.Client.GetByteArrayAsync(iconUrl);
                await File.WriteAllBytesAsync(TempIconPath, iconBytes);

                var iconBox = this.FindControl<Image>("IconPreviewBox");
                if (iconBox != null)
                {
                    using (var stream = File.OpenRead(TempIconPath))
                    {
                        iconBox.Source = new Bitmap(stream);
                    }
                }

                var iconDir = this.FindControl<TextBox>("IconSourceDirectory");
                if (iconDir != null) iconDir.Text = "iconTex.png downloaded from Cucholix's Repo";
                _flagIconSpecified = true;
            }
            catch (Exception ex)
            {
                await MessageBoxWindow.Show(this, $"Failed to download icon from repo: {ex.Message}", "Error", MessageBoxButtons.Ok);
            }

            try
            {
                if (File.Exists(TempBannerPath)) { File.Delete(TempBannerPath); }
                var bannerBytes = await Program.Client.GetByteArrayAsync(bannerUrl);
                await File.WriteAllBytesAsync(TempBannerPath, bannerBytes);

                var bannerBox = this.FindControl<Image>("BannerPreviewBox");
                if (bannerBox != null)
                {
                    using (var stream = File.OpenRead(TempBannerPath))
                    {
                        bannerBox.Source = new Bitmap(stream);
                    }
                }

                var bannerDir = this.FindControl<TextBox>("BannerSourceDirectory");
                if (bannerDir != null) bannerDir.Text = "bootTvTex.png downloaded from Cucholix's Repo";
                _flagBannerSpecified = true;
            }
            catch (Exception ex)
            {
                await MessageBoxWindow.Show(this, $"Failed to download banner from repo: {ex.Message}", "Error", MessageBoxButtons.Ok);
            }
        }

        private async Task<bool> RemoteFileExistsAsync(string url)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Head, url))
                using (var response = await Program.Client.SendAsync(request))
                {
                    return response != null && response.StatusCode == System.Net.HttpStatusCode.OK;
                }
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
                FileTypeFilter = new[] { new FilePickerFileType("GameCube Disc") { Patterns = new[] { "*.iso", "*.gcm" } } }
            });

            if (result != null && result.Count > 0)
            {
                var path = result[0].Path.LocalPath;
                try
                {
                    using (var fs = File.OpenRead(path))
                    {
                        fs.Position = 0x18;
                        byte[] typeBytes = new byte[8];
                        fs.ReadExactly(typeBytes);
                        long gc2GameType = BitConverter.ToInt64(typeBytes);
                        
                        var gc2Dir = this.FindControl<TextBox>("GC2SourceDirectory");

                        if (gc2GameType != 4440324665927270400)
                        {
                            await MessageBoxWindow.Show(this, "This is not a GameCube image. It will not be loaded.", "Error", MessageBoxButtons.Ok);
                            if (gc2Dir != null) gc2Dir.Text = "2nd GameCube Disc Image has not been specified";
                            _flagGc2Specified = false;
                        }
                        else
                        {
                            if (gc2Dir != null) gc2Dir.Text = path;
                            _flagGc2Specified = true;
                        }
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
                FileTypeFilter = new[] { new FilePickerFileType("WAV Audio") { Patterns = new[] { "*.wav" } } }
            });

            if (result != null && result.Count > 0)
            {
                var path = result[0].Path.LocalPath;
                try
                {
                    using (var fs = File.OpenRead(path))
                    {
                        byte[] headerBytes = new byte[4];
                        fs.Position = 0x00;
                        fs.ReadExactly(headerBytes);
                        int wavHeader1 = BitConverter.ToInt32(headerBytes);

                        fs.Position = 0x08;
                        fs.ReadExactly(headerBytes);
                        int wavHeader2 = BitConverter.ToInt32(headerBytes);

                        var soundDir = this.FindControl<TextBox>("BootSoundDirectory");

                        if (wavHeader1 == 1179011410 && wavHeader2 == 1163280727)
                        {
                            if (soundDir != null) soundDir.Text = path;
                        }
                        else
                        {
                            await MessageBoxWindow.Show(this, "This is not a valid WAV file. It will not be loaded.", "Not a WAV File", MessageBoxButtons.Ok);
                            if (soundDir != null) soundDir.Text = "Boot Sound has not been specified";
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
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            await MessageBoxWindow.Show(this, "Make sure your Logo is 170x42 to prevent distortion", "Logo Information", MessageBoxButtons.Ok);

            var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Boot Logo PNG or TGA",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.tga" } } }
            });

            if (result != null && result.Count > 0)
            {
                var path = result[0].Path.LocalPath;
                try
                {
                    if (File.Exists(TempLogoPath)) { File.Delete(TempLogoPath); }

                    if (Path.GetExtension(path).Equals(".tga", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var bmp = TgaReader.LoadTga(path))
                        {
                            TgaReader.SaveAsTga(bmp, TempLogoPath, 170, 42, 32);
                        }
                    }
                    else
                    {
                        File.Copy(path, TempLogoPath, true);
                    }

                    var logoBox = this.FindControl<Image>("LogoPreviewBox");
                    if (logoBox != null)
                    {
                        using (var stream = File.OpenRead(TempLogoPath))
                        {
                            logoBox.Source = new Bitmap(stream);
                        }
                    }

                    var logoDir = this.FindControl<TextBox>("LogoSourceDirectory");
                    if (logoDir != null) logoDir.Text = path;
                    // _flagLogoSpecified = true;
                }
                catch (Exception ex)
                {
                    await MessageBoxWindow.Show(this, $"Failed to load logo: {ex.Message}", "Error", MessageBoxButtons.Ok);
                }
            }
        }

        private async void OnDrcSourceClick(object sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            await MessageBoxWindow.Show(this, "Make sure your GamePad Banner is 854x480 (16:9) to prevent distortion", "Banner Information", MessageBoxButtons.Ok);

            var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select GamePad Banner PNG or TGA",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.tga" } } }
            });

            if (result != null && result.Count > 0)
            {
                var path = result[0].Path.LocalPath;
                try
                {
                    if (File.Exists(TempDrcPath)) { File.Delete(TempDrcPath); }

                    if (Path.GetExtension(path).Equals(".tga", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var bmp = TgaReader.LoadTga(path))
                        {
                            TgaReader.SaveAsTga(bmp, TempDrcPath, 854, 480, 32);
                        }
                    }
                    else
                    {
                        File.Copy(path, TempDrcPath, true);
                    }

                    var drcBox = this.FindControl<Image>("DrcPreviewBox");
                    if (drcBox != null)
                    {
                        using (var stream = File.OpenRead(TempDrcPath))
                        {
                            drcBox.Source = new Bitmap(stream);
                        }
                    }

                    var drcDir = this.FindControl<TextBox>("DrcSourceDirectory");
                    if (drcDir != null) drcDir.Text = path;
                    // _flagDrcSpecified = true;
                }
                catch (Exception ex)
                {
                    await MessageBoxWindow.Show(this, $"Failed to load gamepad banner: {ex.Message}", "Error", MessageBoxButtons.Ok);
                }
            }
        }

        private void OnMainTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl)
            {
                var tabControl = sender as TabControl;
                var selectedTab = tabControl?.SelectedItem as TabItem;
                if (selectedTab != null && selectedTab.Header?.ToString() == "Build Title")
                {
                    // Update key text boxes from settings
                    var wiiUCommonKey = this.FindControl<TextBox>("WiiUCommonKey");
                    var titleKey = this.FindControl<TextBox>("TitleKey");
                    var ancastKey = this.FindControl<TextBox>("AncastKey");

                    if (wiiUCommonKey != null)
                        wiiUCommonKey.Text = string.IsNullOrEmpty(Properties.Settings.Default.WiiUCommonKey)
                            ? ""
                            : Properties.Settings.Default.WiiUCommonKey.ToUpper();

                    if (titleKey != null)
                        titleKey.Text = string.IsNullOrEmpty(Properties.Settings.Default.TitleKey)
                            ? ""
                            : Properties.Settings.Default.TitleKey.ToUpper();

                    if (ancastKey != null)
                        ancastKey.Text = string.IsNullOrEmpty(Properties.Settings.Default.AncastKey)
                            ? ""
                            : Properties.Settings.Default.AncastKey.ToUpper();

                    _commonKeyGood = SetKeyStatus(wiiUCommonKey, "35-AC-59-94-97-22-79-33-1D-97-09-4F-A2-FB-97-FC");
                    _titleKeyGood = SetKeyStatus(titleKey, "F9-4B-D8-8E-BB-7A-A9-38-67-E6-30-61-5F-27-1C-9F");
                    _ancastKeyGood = SetKeyStatus(ancastKey, "31-8D-1F-9D-98-FB-08-E7-7C-7F-E1-77-AA-49-05-43");

                    // Check checklist
                    _buildFlagSource = _flagGameSpecified && _flagIconSpecified && _flagBannerSpecified;
                    var sourceCheck = this.FindControl<CheckBox>("SourceCheck");
                    if (sourceCheck != null) sourceCheck.IsChecked = _buildFlagSource;

                    var packedTitle1 = this.FindControl<TextBox>("PackedTitleLine1");
                    var packedTitleIDLine = this.FindControl<TextBox>("PackedTitleIDLine");
                    _buildFlagMeta = packedTitle1 != null && !string.IsNullOrEmpty(packedTitle1.Text) && 
                                     packedTitleIDLine != null && packedTitleIDLine.Text?.Length == 16;
                    var metaCheck = this.FindControl<CheckBox>("MetaCheck");
                    if (metaCheck != null) metaCheck.IsChecked = _buildFlagMeta;

                    _buildFlagAdvance = true; // Simpler default
                    var advanceCheck = this.FindControl<CheckBox>("AdvanceCheck");
                    if (advanceCheck != null) advanceCheck.IsChecked = _buildFlagAdvance;

                    var lrPatch = this.FindControl<CheckBox>("LRPatch");
                    bool skipAncast = lrPatch == null || lrPatch.IsChecked != true;
                    _buildFlagKeys = skipAncast ? (_commonKeyGood && _titleKeyGood) : (_commonKeyGood && _titleKeyGood && _ancastKeyGood);
                    
                    var keysCheck = this.FindControl<CheckBox>("KeysCheck");
                    if (keysCheck != null) keysCheck.IsChecked = _buildFlagKeys;

                    UpdateBuildButtonState();
                }
            }
        }

        private bool SetKeyStatus(TextBox? keyTextBox, string expectedHash)
        {
            if (keyTextBox == null || string.IsNullOrEmpty(keyTextBox.Text)) return false;
            keyTextBox.Text = keyTextBox.Text.ToUpper();
            using (var md5 = MD5.Create())
            {
                byte[] data = md5.ComputeHash(Encoding.ASCII.GetBytes(keyTextBox.Text));
                string hash = BitConverter.ToString(data);
                bool isValid = string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase);
                keyTextBox.IsReadOnly = isValid;
                keyTextBox.Background = isValid ? Avalonia.Media.Brush.Parse("Lime") : Avalonia.Media.Brush.Parse("White");

                // Disable button if key is valid
                string buttonName = keyTextBox.Name switch
                {
                    "WiiUCommonKey" => "SaveCommonKeyButton",
                    "TitleKey" => "SaveTitleKeyButton",
                    "AncastKey" => "SaveAncastKeyButton",
                    _ => ""
                };
                if (!string.IsNullOrEmpty(buttonName))
                {
                    var button = this.FindControl<Button>(buttonName);
                    if (button != null)
                    {
                        button.IsEnabled = !isValid;
                    }
                }

                return isValid;
            }
        }

        private void OnKeyTextBoxDoubleTapped(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.IsReadOnly = false;
                textBox.Background = Avalonia.Media.Brush.Parse("White");

                // Enable corresponding save button
                string buttonName = textBox.Name switch
                {
                    "WiiUCommonKey" => "SaveCommonKeyButton",
                    "TitleKey" => "SaveTitleKeyButton",
                    "AncastKey" => "SaveAncastKeyButton",
                    _ => ""
                };
                if (!string.IsNullOrEmpty(buttonName))
                {
                    var button = this.FindControl<Button>(buttonName);
                    if (button != null)
                    {
                        button.IsEnabled = true;
                    }
                }
            }
        }

        private void OnSaveCommonKeyClick(object sender, RoutedEventArgs e)
        {
            var wiiUCommonKey = this.FindControl<TextBox>("WiiUCommonKey");
            if (wiiUCommonKey != null && !string.IsNullOrEmpty(wiiUCommonKey.Text))
            {
                Properties.Settings.Default.WiiUCommonKey = wiiUCommonKey.Text;
                Properties.Settings.Default.Save();
                _commonKeyGood = SetKeyStatus(wiiUCommonKey, "35-AC-59-94-97-22-79-33-1D-97-09-4F-A2-FB-97-FC");
                UpdateBuildButtonState();
            }
        }

        private void OnSaveTitleKeyClick(object sender, RoutedEventArgs e)
        {
            var titleKey = this.FindControl<TextBox>("TitleKey");
            if (titleKey != null && !string.IsNullOrEmpty(titleKey.Text))
            {
                Properties.Settings.Default.TitleKey = titleKey.Text;
                Properties.Settings.Default.Save();
                _titleKeyGood = SetKeyStatus(titleKey, "F9-4B-D8-8E-BB-7A-A9-38-67-E6-30-61-5F-27-1C-9F");
                UpdateBuildButtonState();
            }
        }

        private void OnSaveAncastKeyClick(object sender, RoutedEventArgs e)
        {
            var ancastKey = this.FindControl<TextBox>("AncastKey");
            if (ancastKey != null && !string.IsNullOrEmpty(ancastKey.Text))
            {
                Properties.Settings.Default.AncastKey = ancastKey.Text;
                Properties.Settings.Default.Save();
                _ancastKeyGood = SetKeyStatus(ancastKey, "31-8D-1F-9D-98-FB-08-E7-7C-7F-E1-77-AA-49-05-43");
                UpdateBuildButtonState();
            }
        }

        private void UpdateBuildButtonState()
        {
            // 1. Source files checklist
            _buildFlagSource = _flagGameSpecified && _flagIconSpecified && _flagBannerSpecified;
            var sourceCheck = this.FindControl<CheckBox>("SourceCheck");
            if (sourceCheck != null) sourceCheck.IsChecked = _buildFlagSource;

            // 2. Metadata checklist
            var packedTitle1 = this.FindControl<TextBox>("PackedTitleLine1");
            var packedTitleIDLine = this.FindControl<TextBox>("PackedTitleIDLine");
            _buildFlagMeta = packedTitle1 != null && !string.IsNullOrEmpty(packedTitle1.Text) && 
                             packedTitleIDLine != null && packedTitleIDLine.Text?.Length == 16;
            var metaCheck = this.FindControl<CheckBox>("MetaCheck");
            if (metaCheck != null) metaCheck.IsChecked = _buildFlagMeta;

            // 3. Advanced checklist
            _buildFlagAdvance = true;
            var advanceCheck = this.FindControl<CheckBox>("AdvanceCheck");
            if (advanceCheck != null) advanceCheck.IsChecked = _buildFlagAdvance;

            // 4. Keys checklist
            var c2wPatch = this.FindControl<CheckBox>("C2WPatchFlag");
            var ancastKeyBorder = this.FindControl<Border>("AncastKeyBorder");
            if (ancastKeyBorder != null)
            {
                ancastKeyBorder.IsVisible = c2wPatch != null && c2wPatch.IsChecked == true;
            }

            bool skipAncast = c2wPatch == null || c2wPatch.IsChecked != true;
            _buildFlagKeys = skipAncast ? (_commonKeyGood && _titleKeyGood) : (_commonKeyGood && _titleKeyGood && _ancastKeyGood);
            var keysCheck = this.FindControl<CheckBox>("KeysCheck");
            if (keysCheck != null) keysCheck.IsChecked = _buildFlagKeys;

            // 5. Main Build Button
            var theBigOneTM = this.FindControl<Button>("TheBigOneTM");
            if (theBigOneTM != null)
            {
                theBigOneTM.IsEnabled = _buildFlagSource && _buildFlagMeta && _buildFlagAdvance && _buildFlagKeys;
            }
        }

        private void OnC2WPatchFlagClick(object sender, RoutedEventArgs e)
        {
            UpdateBuildButtonState();
        }

        private bool IsCommandAvailable(string cmd)
        {
            try
            {
                using (var p = new Process())
                {
                    p.StartInfo.FileName = "which";
                    p.StartInfo.Arguments = cmd;
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.CreateNoWindow = true;
                    p.Start();
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
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

            ProcessStartInfo launcher = new ProcessStartInfo(targetExe);
            launcher.Arguments = targetArgs;
            launcher.UseShellExecute = false;
            launcher.WorkingDirectory = Directory.GetCurrentDirectory();
            if (hideProcess)
            {
                launcher.WindowStyle = ProcessWindowStyle.Hidden;
                launcher.CreateNoWindow = true;
            }
            using (Process? process = Process.Start(launcher))
            {
                process?.WaitForExit();
            }
        }

        private async void OnBuildClick(object sender, RoutedEventArgs e)
        {
            var mainTabs = this.FindControl<TabControl>("MainTabs");
            var buildStatus = this.FindControl<TextBlock>("BuildStatus");
            var buildProgress = this.FindControl<ProgressBar>("BuildProgress");

            if (mainTabs != null) mainTabs.IsEnabled = false;
            if (buildProgress != null) buildProgress.Value = 0;
            if (buildStatus != null) buildStatus.Text = "Initializing Build Process...";

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
                            if (mainTabs != null) mainTabs.IsEnabled = true;
                            if (buildStatus != null) buildStatus.Text = "";
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
                        if (mainTabs != null) mainTabs.IsEnabled = true;
                        if (buildStatus != null) buildStatus.Text = "";
                        return;
                    }
                    selectedOutputPath = folders[0].Path.LocalPath;
                    Properties.Settings.Default.OutputPath = selectedOutputPath;
                    Properties.Settings.Default.Save();
                }
            }

            if (buildProgress != null) buildProgress.Value = 2;

            // Retrieve control values on UI Thread
            var wiiUCommonKey = this.FindControl<TextBox>("WiiUCommonKey")?.Text ?? "";
            var titleKey = this.FindControl<TextBox>("TitleKey")?.Text ?? "";
            var ancastKey = this.FindControl<TextBox>("AncastKey")?.Text ?? "";
            var packedTitleIDLine = this.FindControl<TextBox>("PackedTitleIDLine")?.Text ?? "";
            var packedTitleLine1 = this.FindControl<TextBox>("PackedTitleLine1")?.Text ?? "";
            var packedTitleLine2 = this.FindControl<TextBox>("PackedTitleLine2")?.Text ?? "";
            var enablePackedLine2 = this.FindControl<CheckBox>("EnablePackedLine2")?.IsChecked == true;
            var wiimmfi = this.FindControl<CheckBox>("Wiimmfi")?.IsChecked == true;
            var wiiVMC = this.FindControl<CheckBox>("WiiVMC")?.IsChecked == true;
            var disableTrimming = this.FindControl<CheckBox>("DisableTrimming")?.IsChecked == true;
            var disableNintendontAutoboot = this.FindControl<CheckBox>("DisableNintendontAutoboot")?.IsChecked == true;
            var c2wPatch = this.FindControl<CheckBox>("C2WPatchFlag")?.IsChecked == true;
            var lrPatch = this.FindControl<CheckBox>("LRPatch")?.IsChecked == true;
            
            // Resolve optional paths
            var soundDir = this.FindControl<TextBox>("BootSoundDirectory")?.Text ?? "";
            bool flagBootSoundSpecified = File.Exists(soundDir);
            
            var logoDir = this.FindControl<TextBox>("LogoSourceDirectory")?.Text ?? "";
            bool flagLogoSpecified = File.Exists(logoDir);

            var drcDir = this.FindControl<TextBox>("DrcSourceDirectory")?.Text ?? "";
            bool flagDrcSpecified = File.Exists(drcDir);

            // Resolve GC2 path if GC retail
            string gc2Path = "";
            if (_systemType == "gcn" && _flagGc2Specified)
            {
                gc2Path = this.FindControl<TextBox>("GC2SourceDirectory")?.Text ?? "";
            }

            // Resolve boot sound loop option
            bool toggleBootSoundLoop = this.FindControl<CheckBox>("ToggleBootSoundLoop")?.IsChecked == true;
            string loopString = toggleBootSoundLoop ? "" : " -noLoop";

            // Resolve gamepad emulation flags
            string nfsPatchFlag = "";
            var horWiiMote = this.FindControl<RadioButton>("HorWiiMote")?.IsChecked == true;
            var verWiiMote = this.FindControl<RadioButton>("VerWiiMote")?.IsChecked == true;
            var ccemu = this.FindControl<RadioButton>("CCEmu")?.IsChecked == true;
            var forceCC = this.FindControl<RadioButton>("ForceCC")?.IsChecked == true;
            var forceNoCC = this.FindControl<RadioButton>("ForceNoCC")?.IsChecked == true;

            if (horWiiMote) nfsPatchFlag = " -horizontal";
            else if (verWiiMote) nfsPatchFlag = " -wiimote";
            else if (ccemu) nfsPatchFlag = " -nocc";
            else if (forceCC) nfsPatchFlag = " -instantcc";
            else if (forceNoCC) nfsPatchFlag = " -nocc";

            string drcuse = "1";
            var disableGamePad = this.FindControl<CheckBox>("DisableGamePad")?.IsChecked == true;
            if (disableGamePad) drcuse = "65537";

            // We will run the CPU and I/O intensive parts in a Task.Run
            string ogFilePath = _selectedGamePath;
            bool success = false;
            string finalOutputPath = "";
            string errorMsg = "";

            await Task.Run(() =>
            {
                try
                {
                    // 1. Download base files with JNUSTool if not present
                    string[] downloadedFiles = new[]
                    {
                        Path.Combine(JNUSToolDownloads, "0005001010004000", "code", "deint.txt"),
                        Path.Combine(JNUSToolDownloads, "0005001010004000", "code", "font.bin"),
                        Path.Combine(JNUSToolDownloads, "0005001010004001", "code", "c2w.img"),
                        Path.Combine(JNUSToolDownloads, "0005001010004001", "code", "boot.bin"),
                        Path.Combine(JNUSToolDownloads, "0005001010004001", "code", "dmcu.d.hex"),
                        Path.Combine(JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "code", "cos.xml"),
                        Path.Combine(JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "code", "frisbiiU.rpx"),
                        Path.Combine(JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "code", "fw.img"),
                        Path.Combine(JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "code", "fw.tmd"),
                        Path.Combine(JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "code", "htk.bin"),
                        Path.Combine(JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "code", "nn_hai_user.rpl"),
                        Path.Combine(JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "content", "assets", "shaders", "cafe", "banner.gsh"),
                        Path.Combine(JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "content", "assets", "shaders", "cafe", "fade.gsh"),
                        Path.Combine(JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "meta", "bootMovie.h264"),
                        Path.Combine(JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "meta", "bootLogoTex.tga"),
                        Path.Combine(JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "meta", "bootSound.btsnd")
                    };

                    string[] fileHashes = new string[]
                    {
                        "E707A62EE5491DD16E5494631EA9870A",
                        "CDDAC70FDDB9428F220B048102DAAD40",
                        "FC5EE480F58796C3681BEE78BD3E5D1C",
                        "F4D5F095CBA9504A5CB8A94A4781114C",
                        "E32FCBCC817C443E0832DE5CA9032808",
                        "42215713D951C2023F90164ED9DF900F",
                        "69E191E8B0DF1D5304B36F1375C4F127",
                        "3CAF52A9A440EEE4F125A3AD22E305C8",
                        "AE4E06CAD3BEF60AE5C49E22CCDC3254",
                        "C99CAF5995E395F39C3FCAB4A8AF20E0",
                        "C4BF586BA0071BD8477986C1AA37E1F1",
                        "5F2FA196DFC158F0FCC69272073AE07E",
                        "307221985A7B46F0386A2637DC15DA3E",
                        "CA0DAC3E3C5654209C754357EF5A2507",
                        "67B312145ECB70514D5BD36FCAAE0193",
                        "43CD445B8569A445F97ECCC098C93B38"
                    };

                    string[] filesToDownload = new string[]
                    {
                        "0005001010004000 -file /code/deint.txt",
                        "0005001010004000 -file /code/font.bin",
                        "0005001010004001 -file /code/c2w.img",
                        "0005001010004001 -file /code/boot.bin",
                        "0005001010004001 -file /code/dmcu.d.hex",
                        "00050000101b0700 " + titleKey + " -file /code/cos.xml",
                        "00050000101b0700 " + titleKey + " -file /code/frisbiiU.rpx",
                        "00050000101b0700 " + titleKey + " -file /code/fw.img",
                        "00050000101b0700 " + titleKey + " -file /code/fw.tmd",
                        "00050000101b0700 " + titleKey + " -file /code/htk.bin",
                        "00050000101b0700 " + titleKey + " -file /code/nn_hai_user.rpl",
                        "00050000101b0700 " + titleKey + " -file /content/assets/shaders/cafe/banner.gsh",
                        "00050000101b0700 " + titleKey + " -file /content/assets/shaders/cafe/fade.gsh*",
                        "00050000101b0700 " + titleKey + " -file /meta/bootMovie.h264",
                        "00050000101b0700 " + titleKey + " -file /meta/bootLogoTex.tga",
                        "00050000101b0700 " + titleKey + " -file /meta/bootSound.btsnd"
                    };

                    UpdateStatus("Checking if the necessary files are present...", 10);

                    // Create config for JNUSTool
                    string jnusConfigPath = Path.Combine(TempToolsPath, "JAR", "config");
                    string[] jnusToolConfig = { "http://ccs.cdn.wup.shop.nintendo.net/ccs/download", wiiUCommonKey };
                    File.WriteAllLines(jnusConfigPath, jnusToolConfig);

                    // Create downloads directory if not exists
                    Directory.CreateDirectory(JNUSToolDownloads);

                    string currentDir = Directory.GetCurrentDirectory();
                    Directory.SetCurrentDirectory(Path.Combine(TempToolsPath, "JAR"));

                    bool hasDownloadedAnything = false;
                    for (int i = 0; i < downloadedFiles.Length; i++)
                    {
                        if (File.Exists(downloadedFiles[i]) && GetMD5Checksum(downloadedFiles[i]) == fileHashes[i])
                        {
                            continue;
                        }

                        // Download it
                        UpdateStatus("(One-Time Download) Downloading base files from Nintendo...", 12 + i * 2);
                        LaunchProgram("JNUSTool.exe", filesToDownload[i], true);
                        hasDownloadedAnything = true;
                    }

                    if (hasDownloadedAnything)
                    {
                        UpdateStatus("Saving files from Nintendo for future use...", 45);
                        if (Directory.Exists("Rhythm Heaven Fever [VAKE01]"))
                        {
                            FileUtil.CopyDirectory("Rhythm Heaven Fever [VAKE01]", Path.Combine(JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]"));
                            Directory.Delete("Rhythm Heaven Fever [VAKE01]", true);
                        }
                        if (Directory.Exists("0005001010004000"))
                        {
                            FileUtil.CopyDirectory("0005001010004000", Path.Combine(JNUSToolDownloads, "0005001010004000"));
                            Directory.Delete("0005001010004000", true);
                        }
                        if (Directory.Exists("0005001010004001"))
                        {
                            FileUtil.CopyDirectory("0005001010004001", Path.Combine(JNUSToolDownloads, "0005001010004001"));
                            Directory.Delete("0005001010004001", true);
                        }

                        // Verify
                        bool jnusFail = false;
                        for (int i = 0; i < downloadedFiles.Length; i++)
                        {
                            if (!File.Exists(downloadedFiles[i]) || GetMD5Checksum(downloadedFiles[i]) != fileHashes[i])
                            {
                                jnusFail = true;
                                break;
                            }
                        }

                        if (jnusFail)
                        {
                            throw new Exception("Failed to download or verify base files using JNUSTool.");
                        }
                    }

                    if (File.Exists("config")) File.Delete("config");
                    Directory.SetCurrentDirectory(TempRootPath);

                    // Copy downloaded files to build directory
                    UpdateStatus("Copying base files to temporary build directory...", 48);
                    
                    if (Directory.Exists(TempBuildPath)) Directory.Delete(TempBuildPath, true);
                    Directory.CreateDirectory(TempBuildPath);
                    Directory.CreateDirectory(Path.Combine(TempBuildPath, "code"));
                    Directory.CreateDirectory(Path.Combine(TempBuildPath, "meta"));
                    Directory.CreateDirectory(Path.Combine(TempBuildPath, "content"));

                    FileUtil.CopyDirectory(Path.Combine(JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]"), TempBuildPath);
                    if (c2wPatch)
                    {
                        FileUtil.CopyDirectory(Path.Combine(JNUSToolDownloads, "0005001010004000"), TempBuildPath);
                        FileUtil.CopyDirectory(Path.Combine(JNUSToolDownloads, "0005001010004001"), TempBuildPath);
                        string[] ancastKeyCopy = { ancastKey };
                        File.WriteAllLines(Path.Combine(TempToolsPath, "C2W", "starbuck_key.txt"), ancastKeyCopy);
                        File.Copy(Path.Combine(TempBuildPath, "code", "c2w.img"), Path.Combine(TempToolsPath, "C2W", "c2w.img"), true);
                        Directory.SetCurrentDirectory(Path.Combine(TempToolsPath, "C2W"));
                        LaunchProgram("c2w_patcher.exe", "-nc", true);
                        File.Delete(Path.Combine(TempBuildPath, "code", "c2w.img"));
                        File.Copy(Path.Combine(TempToolsPath, "C2W", "c2p.img"), Path.Combine(TempBuildPath, "code", "c2w.img"), true);
                        File.Delete(Path.Combine(TempToolsPath, "C2W", "c2p.img"));
                        File.Delete(Path.Combine(TempToolsPath, "C2W", "c2w.img"));
                        File.Delete(Path.Combine(TempToolsPath, "C2W", "starbuck_key.txt"));
                        Directory.SetCurrentDirectory(TempRootPath);
                    }

                    UpdateStatus("Generating app.xml and meta.xml...", 50);

                    // Generate app.xml and meta.xml
                    string[] appXml = { "<?xml version=\"1.0\" encoding=\"utf-8\"?>", "<app type=\"complex\" access=\"777\">", "  <version type=\"unsignedInt\" length=\"4\">16</version>", "  <os_version type=\"hexBinary\" length=\"8\">000500101000400A</os_version>", "  <title_id type=\"hexBinary\" length=\"8\">" + packedTitleIDLine + "</title_id>", "  <title_version type=\"hexBinary\" length=\"2\">0000</title_version>", "  <sdk_version type=\"unsignedInt\" length=\"4\">21204</sdk_version>", "  <app_type type=\"hexBinary\" length=\"4\">8000002E</app_type>", "  <group_id type=\"hexBinary\" length=\"4\">" + _titleIdHex + "</group_id>", "  <os_mask type=\"hexBinary\" length=\"32\">0000000000000000000000000000000000000000000000000000000000000000</os_mask>", "  <common_id type=\"hexBinary\" length=\"8\">0000000000000000</common_id>", "</app>" };
                    File.WriteAllLines(Path.Combine(TempBuildPath, "code", "app.xml"), appXml);

                    string line2Text = enablePackedLine2 ? packedTitleLine2 : "";
                    List<string> metaXml = new List<string>
                    {
                        "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
                        "<menu type=\"complex\" access=\"777\">",
                        "  <version type=\"unsignedInt\" length=\"4\">33</version>",
                        "  <product_code type=\"string\" length=\"32\">WUP-N-" + _titleIdText + "</product_code>",
                        "  <content_platform type=\"string\" length=\"32\">WUP</content_platform>",
                        "  <company_code type=\"string\" length=\"8\">0001</company_code>",
                        "  <mastering_date type=\"string\" length=\"32\"></mastering_date>",
                        "  <logo_type type=\"unsignedInt\" length=\"4\">0</logo_type>",
                        "  <app_launch_type type=\"hexBinary\" length=\"4\">00000000</app_launch_type>",
                        "  <invisible_flag type=\"hexBinary\" length=\"4\">00000000</invisible_flag>",
                        "  <no_managed_flag type=\"hexBinary\" length=\"4\">00000000</no_managed_flag>",
                        "  <no_event_log type=\"hexBinary\" length=\"4\">00000002</no_event_log>",
                        "  <no_icon_database type=\"hexBinary\" length=\"4\">00000000</no_icon_database>",
                        "  <launching_flag type=\"hexBinary\" length=\"4\">00000004</launching_flag>",
                        "  <install_flag type=\"hexBinary\" length=\"4\">00000000</install_flag>",
                        "  <closing_msg type=\"unsignedInt\" length=\"4\">0</closing_msg>",
                        "  <title_version type=\"unsignedInt\" length=\"4\">0</title_version>",
                        "  <title_id type=\"hexBinary\" length=\"8\">" + packedTitleIDLine + "</title_id>",
                        "  <group_id type=\"hexBinary\" length=\"4\">" + _titleIdHex + "</group_id>",
                        "  <boss_id type=\"hexBinary\" length=\"8\">0000000000000000</boss_id>",
                        "  <os_version type=\"hexBinary\" length=\"8\">000500101000400A</os_version>",
                        "  <app_size type=\"hexBinary\" length=\"8\">0000000000000000</app_size>",
                        "  <common_save_size type=\"hexBinary\" length=\"8\">0000000000000000</common_save_size>",
                        "  <account_save_size type=\"hexBinary\" length=\"8\">0000000000000000</account_save_size>",
                        "  <common_boss_size type=\"hexBinary\" length=\"8\">0000000000000000</common_boss_size>",
                        "  <account_boss_size type=\"hexBinary\" length=\"8\">0000000000000000</account_boss_size>",
                        "  <save_no_rollback type=\"unsignedInt\" length=\"4\">0</save_no_rollback>",
                        "  <join_game_id type=\"hexBinary\" length=\"4\">00000000</join_game_id>",
                        "  <join_game_mode_mask type=\"hexBinary\" length=\"8\">0000000000000000</join_game_mode_mask>",
                        "  <bg_daemon_enable type=\"unsignedInt\" length=\"4\">0</bg_daemon_enable>",
                        "  <olv_accesskey type=\"unsignedInt\" length=\"4\">3921400692</olv_accesskey>",
                        "  <wood_tin type=\"unsignedInt\" length=\"4\">0</wood_tin>",
                        "  <e_manual type=\"unsignedInt\" length=\"4\">0</e_manual>",
                        "  <e_manual_version type=\"unsignedInt\" length=\"4\">0</e_manual_version>",
                        "  <region type=\"hexBinary\" length=\"4\">00000002</region>",
                        "  <pc_cero type=\"unsignedInt\" length=\"4\">128</pc_cero>",
                        "  <pc_esrb type=\"unsignedInt\" length=\"4\">6</pc_esrb>",
                        "  <pc_bbfc type=\"unsignedInt\" length=\"4\">192</pc_bbfc>",
                        "  <pc_usk type=\"unsignedInt\" length=\"4\">128</pc_usk>",
                        "  <pc_pegi_gen type=\"unsignedInt\" length=\"4\">128</pc_pegi_gen>",
                        "  <pc_pegi_fin type=\"unsignedInt\" length=\"4\">192</pc_pegi_fin>",
                        "  <pc_pegi_prt type=\"unsignedInt\" length=\"4\">128</pc_pegi_prt>",
                        "  <pc_pegi_bbfc type=\"unsignedInt\" length=\"4\">128</pc_pegi_bbfc>",
                        "  <pc_cob type=\"unsignedInt\" length=\"4\">128</pc_cob>",
                        "  <pc_grb type=\"unsignedInt\" length=\"4\">128</pc_grb>",
                        "  <pc_cgsrr type=\"unsignedInt\" length=\"4\">128</pc_cgsrr>",
                        "  <pc_oflc type=\"unsignedInt\" length=\"4\">128</pc_oflc>",
                        "  <pc_reserved0 type=\"unsignedInt\" length=\"4\">192</pc_reserved0>",
                        "  <pc_reserved1 type=\"unsignedInt\" length=\"4\">192</pc_reserved1>",
                        "  <pc_reserved2 type=\"unsignedInt\" length=\"4\">192</pc_reserved2>",
                        "  <pc_reserved3 type=\"unsignedInt\" length=\"4\">192</pc_reserved3>",
                        "  <ext_dev_nunchaku type=\"unsignedInt\" length=\"4\">0</ext_dev_nunchaku>",
                        "  <ext_dev_classic type=\"unsignedInt\" length=\"4\">0</ext_dev_classic>",
                        "  <ext_dev_urcc type=\"unsignedInt\" length=\"4\">0</ext_dev_urcc>",
                        "  <ext_dev_board type=\"unsignedInt\" length=\"4\">0</ext_dev_board>",
                        "  <ext_dev_usb_keyboard type=\"unsignedInt\" length=\"4\">0</ext_dev_usb_keyboard>",
                        "  <ext_dev_etc type=\"unsignedInt\" length=\"4\">0</ext_dev_etc>",
                        "  <ext_dev_etc_name type=\"string\" length=\"512\"></ext_dev_etc_name>",
                        "  <eula_version type=\"unsignedInt\" length=\"4\">0</eula_version>",
                        "  <drc_use type=\"unsignedInt\" length=\"4\">" + drcuse + "</drc_use>",
                        "  <network_use type=\"unsignedInt\" length=\"4\">0</network_use>",
                        "  <online_account_use type=\"unsignedInt\" length=\"4\">0</online_account_use>",
                        "  <direct_boot type=\"unsignedInt\" length=\"4\">0</direct_boot>",
                        "  <reserved_flag0 type=\"hexBinary\" length=\"4\">00010001</reserved_flag0>",
                        "  <reserved_flag1 type=\"hexBinary\" length=\"4\">00080023</reserved_flag1>",
                        "  <reserved_flag2 type=\"hexBinary\" length=\"4\">" + _titleIdHex + "</reserved_flag2>",
                        "  <reserved_flag3 type=\"hexBinary\" length=\"4\">00000000</reserved_flag3>",
                        "  <reserved_flag4 type=\"hexBinary\" length=\"4\">00000000</reserved_flag4>",
                        "  <reserved_flag5 type=\"hexBinary\" length=\"4\">00000000</reserved_flag5>",
                        "  <reserved_flag6 type=\"hexBinary\" length=\"4\">00000003</reserved_flag6>",
                        "  <reserved_flag7 type=\"hexBinary\" length=\"4\">00000005</reserved_flag7>"
                    };

                    string longName = string.IsNullOrEmpty(line2Text) ? packedTitleLine1 : $"{packedTitleLine1}\n{line2Text}";
                    for (int i = 0; i < 11; i++) // for all languages
                    {
                        metaXml.Add($"  <longname_{GetLanguageSuffix(i)} type=\"string\" length=\"512\">{longName}</longname_{GetLanguageSuffix(i)}>");
                    }
                    for (int i = 0; i < 11; i++)
                    {
                        metaXml.Add($"  <shortname_{GetLanguageSuffix(i)} type=\"string\" length=\"512\">{packedTitleLine1}</shortname_{GetLanguageSuffix(i)}>");
                    }
                    for (int i = 0; i < 11; i++)
                    {
                        metaXml.Add($"  <publisher_{GetLanguageSuffix(i)} type=\"string\" length=\"256\"></publisher_{GetLanguageSuffix(i)}>");
                    }
                    for (int i = 0; i < 32; i++)
                    {
                        metaXml.Add($"  <add_on_unique_id{i} type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id{i}>");
                    }
                    metaXml.Add("</menu>");
                    File.WriteAllLines(Path.Combine(TempBuildPath, "meta", "meta.xml"), metaXml);

                    UpdateStatus("Converting all image sources to expected TGA specification...", 52);

                    // Convert images to TGA using our native SkiaSharp reader/converter
                    using (var bmp = SkiaSharp.SKBitmap.Decode(TempIconPath))
                    {
                        TgaReader.SaveAsTga(bmp, Path.Combine(TempBuildPath, "meta", "iconTex.tga"), 128, 128, 32);
                    }
                    using (var bmp = SkiaSharp.SKBitmap.Decode(TempBannerPath))
                    {
                        TgaReader.SaveAsTga(bmp, Path.Combine(TempBuildPath, "meta", "bootTvTex.tga"), 1280, 720, 24);
                    }
                    
                    if (!flagDrcSpecified)
                    {
                        using (var bmp = SkiaSharp.SKBitmap.Decode(TempBannerPath))
                        {
                            TgaReader.SaveAsTga(bmp, Path.Combine(TempBuildPath, "meta", "bootDrcTex.tga"), 854, 480, 24);
                        }
                    }
                    else
                    {
                        using (var bmp = SkiaSharp.SKBitmap.Decode(TempDrcPath))
                        {
                            TgaReader.SaveAsTga(bmp, Path.Combine(TempBuildPath, "meta", "bootDrcTex.tga"), 854, 480, 24);
                        }
                    }

                    if (flagLogoSpecified)
                    {
                        using (var bmp = SkiaSharp.SKBitmap.Decode(TempLogoPath))
                        {
                            TgaReader.SaveAsTga(bmp, Path.Combine(TempBuildPath, "meta", "bootLogoTex.tga"), 170, 42, 32);
                        }
                    }

                    UpdateStatus("Processing game for NFS Conversion...", 55);

                    // Convert sound if specified
                    if (flagBootSoundSpecified)
                    {
                        UpdateStatus("Converting user-provided sound to btsnd format...", 60);
                        string tempSoundWav = Path.Combine(TempSourcePath, "temp_sound.wav");
                        string finalSoundBtsnd = Path.Combine(TempBuildPath, "meta", "bootSound.btsnd");

                        // SOX to normalize/resample audio
                        LaunchProgram(Path.Combine(TempToolsPath, "SOX", "sox.exe"), $"\"{soundDir}\" -b 16 \"{tempSoundWav}\" channels 2 rate 48k trim 0 6", true);
                        if (File.Exists(finalSoundBtsnd)) File.Delete(finalSoundBtsnd);

                        // wav2btsnd to convert to btsnd
                        LaunchProgram(Path.Combine(TempToolsPath, "JAR", "wav2btsnd.exe"), $"-in \"{tempSoundWav}\" -out \"{finalSoundBtsnd}\"{loopString}", true);
                        if (File.Exists(tempSoundWav)) File.Delete(tempSoundWav);
                    }

                    UpdateStatus("Building game ISO image...", 65);
                    string gameIsoPath = Path.Combine(TempSourcePath, "game.iso");

                    if (_systemType == "wii")
                    {
                        string currentWiiGame = ogFilePath;
                        if (_flagWbfs)
                        {
                            string convertedIso = Path.Combine(TempSourcePath, "wbfsconvert.iso");
                            LaunchProgram(Path.Combine(TempToolsPath, "EXE", "wbfs_file.exe"), $"\"{ogFilePath}\" convert \"{convertedIso}\"", true);
                            currentWiiGame = convertedIso;
                        }

                        // Wii retail extract & patch
                        if (!disableTrimming)
                        {
                            string isoExtractDir = Path.Combine(TempSourcePath, "ISOEXTRACT");
                            if (Directory.Exists(isoExtractDir)) Directory.Delete(isoExtractDir, true);

                            LaunchProgram(Path.Combine(TempToolsPath, "WIT", "wit.exe"), $"extract \"{currentWiiGame}\" --DEST \"{isoExtractDir}\" --psel data,-update -ovv", true);
                            
                            if (forceCC)
                            {
                                LaunchProgram(Path.Combine(TempToolsPath, "EXE", "GetExtTypePatcher.exe"), $"\"{Path.Combine(isoExtractDir, "sys", "main.dol")}\" -nc", true);
                            }

                            // Wii VMC / Video mode changer (Not handled interactively on Linux, skipped/run natively if required)
                            if (wiiVMC)
                            {
                                LaunchProgram(Path.Combine(TempToolsPath, "EXE", "wii-vmc.exe"), $"\"{Path.Combine(isoExtractDir, "sys", "main.dol")}\"", true);
                            }

                            string wiimmfiOption = wiimmfi ? " --wiimmfi" : "";
                            LaunchProgram(Path.Combine(TempToolsPath, "WIT", "wit.exe"), $"copy \"{isoExtractDir}\" --DEST \"{gameIsoPath}\" -ovv --links --iso{wiimmfiOption}", true);
                            if (Directory.Exists(isoExtractDir)) Directory.Delete(isoExtractDir, true);
                        }
                        else
                        {
                            File.Copy(currentWiiGame, gameIsoPath, true);
                        }

                        if (File.Exists(Path.Combine(TempSourcePath, "wbfsconvert.iso")))
                            File.Delete(Path.Combine(TempSourcePath, "wbfsconvert.iso"));
                    }
                    else if (_systemType == "dol")
                    {
                        string tempIsoBase = Path.Combine(TempSourcePath, "TEMPISOBASE");
                        if (Directory.Exists(tempIsoBase)) Directory.Delete(tempIsoBase, true);
                        FileUtil.CopyDirectory(Path.Combine(TempToolsPath, "BASE"), tempIsoBase);
                        File.Copy(ogFilePath, Path.Combine(tempIsoBase, "sys", "main.dol"), true);
                        LaunchProgram(Path.Combine(TempToolsPath, "WIT", "wit.exe"), $"copy \"{tempIsoBase}\" --DEST \"{gameIsoPath}\" -ovv --links --iso", true);
                        Directory.Delete(tempIsoBase, true);
                    }
                    else if (_systemType == "gcn")
                    {
                        string tempIsoBase = Path.Combine(TempSourcePath, "TEMPISOBASE");
                        if (Directory.Exists(tempIsoBase)) Directory.Delete(tempIsoBase, true);
                        FileUtil.CopyDirectory(Path.Combine(TempToolsPath, "BASE"), tempIsoBase);

                        // Default forwarder or Nintendont boot dol selection
                        string mainDolSrc = Path.Combine(TempToolsPath, "DOL", "nintendont_default_autobooter.dol");
                        if (disableNintendontAutoboot)
                            mainDolSrc = Path.Combine(TempToolsPath, "DOL", "nintendont_forwarder.dol");

                        File.Copy(mainDolSrc, Path.Combine(tempIsoBase, "sys", "main.dol"), true);
                        File.Copy(ogFilePath, Path.Combine(tempIsoBase, "files", "game.iso"), true);

                        if (!string.IsNullOrEmpty(gc2Path) && File.Exists(gc2Path))
                        {
                            File.Copy(gc2Path, Path.Combine(tempIsoBase, "files", "disc2.iso"), true);
                        }

                        LaunchProgram(Path.Combine(TempToolsPath, "WIT", "wit.exe"), $"copy \"{tempIsoBase}\" --DEST \"{gameIsoPath}\" -ovv --links --iso", true);
                        Directory.Delete(tempIsoBase, true);
                    }

                    // Extract ticket and TMD for encrypting content
                    UpdateStatus("Extracting game tickets and TMD information...", 75);
                    string tikTempDir = Path.Combine(TempSourcePath, "TIKTEMP");
                    if (Directory.Exists(tikTempDir)) Directory.Delete(tikTempDir, true);
                    LaunchProgram(Path.Combine(TempToolsPath, "WIT", "wit.exe"), $"extract \"{gameIsoPath}\" --psel data --psel -update --files +tmd.bin --files +ticket.bin --dest \"{tikTempDir}\" -vv1", true);
                    
                    File.Copy(Path.Combine(tikTempDir, "tmd.bin"), Path.Combine(TempBuildPath, "code", "rvlt.tmd"), true);
                    File.Copy(Path.Combine(tikTempDir, "ticket.bin"), Path.Combine(TempBuildPath, "code", "rvlt.tik"), true);
                    Directory.Delete(tikTempDir, true);

                    // Convert ISO to NFS format
                    UpdateStatus("Converting game ISO to NFS content format...", 80);
                    
                    List<string> nfsArgs = new List<string> { "-enc" };
                    if (_systemType == "dol" || _systemType == "wiiware" || _systemType == "gcn")
                    {
                        nfsArgs.Add("-homebrew");
                    }
                    if (_systemType == "gcn")
                    {
                        nfsArgs.Add("-passthrough");
                    }

                    if (nfsPatchFlag.Contains("-horizontal")) nfsArgs.Add("-horizontal");
                    else if (nfsPatchFlag.Contains("-wiimote")) nfsArgs.Add("-wiimote");
                    else if (nfsPatchFlag.Contains("-instantcc")) nfsArgs.Add("-instantcc");
                    else if (nfsPatchFlag.Contains("-nocc")) nfsArgs.Add("-nocc");

                    if (lrPatch) nfsArgs.Add("-lrpatch");

                    nfsArgs.Add("-iso");
                    nfsArgs.Add(gameIsoPath);

                    Directory.SetCurrentDirectory(Path.Combine(TempBuildPath, "content"));
                    
                    // Convert in-process
                    int nfsResult = Nfs2Iso2Nfs.ConvertNfs(nfsArgs.ToArray());
                    if (nfsResult != 0)
                    {
                        throw new Exception("Nfs2Iso2Nfs conversion failed. Please verify that the Wii Common Key is correct and the source game ISO is not corrupted.");
                    }
                    
                    Directory.SetCurrentDirectory(TempRootPath);
                    if (File.Exists(gameIsoPath)) File.Delete(gameIsoPath);

                    // Encrypt package with NUSPacker
                    UpdateStatus("Encrypting contents into installable WUP package...", 90);
                    string sanitizedGameName = SanitizeFilename(packedTitleLine1);
                    finalOutputPath = Path.Combine(selectedOutputPath, sanitizedGameName + " WUP-N-" + _titleIdText + "_" + packedTitleIDLine);
                    
                    LaunchProgram(Path.Combine(TempToolsPath, "JAR", "NUSPacker.exe"), $"-in BUILDDIR -out \"{finalOutputPath}\" -encryptKeyWith {wiiUCommonKey}", true);
                    
                    // Cleanup
                    UpdateStatus("Cleaning up temporary directories...", 98);
                    if (Directory.Exists(TempBuildPath)) Directory.Delete(TempBuildPath, true);
                    if (Directory.Exists(Path.Combine(TempRootPath, "output"))) Directory.Delete(Path.Combine(TempRootPath, "output"), true);
                    if (Directory.Exists(Path.Combine(TempRootPath, "tmp"))) Directory.Delete(Path.Combine(TempRootPath, "tmp"), true);
                    Directory.CreateDirectory(TempBuildPath);

                    if (Directory.Exists(finalOutputPath))
                    {
                        success = true;
                    }
                    else
                    {
                        errorMsg = "WUP package directory was not created. Verify Java installation.";
                    }
                }
                catch (Exception ex)
                {
                    errorMsg = ex.Message;
                }
            });

            // Update UI thread after complete
            if (mainTabs != null) mainTabs.IsEnabled = true;
            if (buildProgress != null) buildProgress.Value = success ? 100 : 0;
            if (buildStatus != null) buildStatus.Text = success ? "Conversion complete!" : "Conversion failed.";

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
            else
            {
                await MessageBoxWindow.Show(this, $"Conversion Failed!\n{errorMsg}", "Conversion Failed", MessageBoxButtons.Ok);
            }
        }

        private void UpdateStatus(string message, double progressValue)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var buildStatus = this.FindControl<TextBlock>("BuildStatus");
                var buildProgress = this.FindControl<ProgressBar>("BuildProgress");
                if (buildStatus != null) buildStatus.Text = message;
                if (buildProgress != null) buildProgress.Value = progressValue;
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
            using (var stream = File.OpenRead(file))
            using (var md5 = MD5.Create())
            {
                byte[] checksum = md5.ComputeHash(stream);
                return BitConverter.ToString(checksum).Replace("-", string.Empty);
            }
        }
    }
}
