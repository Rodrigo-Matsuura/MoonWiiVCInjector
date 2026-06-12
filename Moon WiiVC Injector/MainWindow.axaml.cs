using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
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

        static readonly string TempIconPath = Path.Combine(TempSourcePath, "iconTex.png");
        static readonly string TempBannerPath = Path.Combine(TempSourcePath, "bootTvTex.png");
        static readonly string TempDrcPath = Path.Combine(TempSourcePath, "bootDrcTex.png");
        static readonly string TempLogoPath = Path.Combine(TempSourcePath, "bootLogoTex.png");

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

            // Simple status display or download trigger
            await MessageBoxWindow.Show(this, "Downloading from repo is currently a placeholder logic in the ported window.", "Information", MessageBoxButtons.Ok);
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
                            // _flagGc2Specified = false;
                        }
                        else
                        {
                            if (gc2Dir != null) gc2Dir.Text = path;
                            // _flagGc2Specified = true;
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
                            ? "00000000000000000000000000000000"
                            : Properties.Settings.Default.WiiUCommonKey.ToUpper();

                    if (titleKey != null)
                        titleKey.Text = string.IsNullOrEmpty(Properties.Settings.Default.TitleKey)
                            ? "00000000000000000000000000000000"
                            : Properties.Settings.Default.TitleKey.ToUpper();

                    if (ancastKey != null)
                        ancastKey.Text = string.IsNullOrEmpty(Properties.Settings.Default.AncastKey)
                            ? "00000000000000000000000000000000"
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
                string hash = BitConverter.ToString(data).Replace("-", "");
                bool isValid = string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase);
                keyTextBox.IsReadOnly = isValid;
                
                // Color formatting can be implemented or handled through standard Avalonia properties
                return isValid;
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
            var theBigOneTM = this.FindControl<Button>("TheBigOneTM");
            if (theBigOneTM != null)
            {
                theBigOneTM.IsEnabled = _buildFlagSource && _buildFlagMeta && _buildFlagAdvance && _buildFlagKeys;
            }
        }

        private void OnBuildClick(object sender, RoutedEventArgs e)
        {
            // Placeholder for build logic
        }
    }
}
