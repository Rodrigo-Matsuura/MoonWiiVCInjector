using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Media;
using Microsoft.Win32;
using System.Security.Cryptography;
using System.Net;
using System.Net.Http;
using System.IO.Compression;
using System.Diagnostics;
using System.Reflection;
using System.Buffers.Binary;
using System.Threading.Tasks;

namespace Moon_WiiVC_Injector
{
    public partial class WiiVcInjector : Form
    {
        public WiiVcInjector()
        {
            InitializeComponent();
            this.Text = string.Format(this.Text, Assembly.GetExecutingAssembly().GetName().Version.ToString());
            AncastKey.Visible = false;
            SaveAncastKeyButton.Visible = false;

            // Check for if .Net v3.5 component is installed
            CheckForNet35();

            // Delete Temporary Root Folder if it exists
            if (Directory.Exists(TempRootPath))
            {
                try
                {
                    Directory.Delete(TempRootPath, true);
                }
                catch { }
            }
            try
            {
                Directory.CreateDirectory(TempRootPath);
            }
            catch { }

            // Extract Tools to temp folder
            string toolZipPath = Path.Combine(TempRootPath, "TOOLDIR.zip");
            File.WriteAllBytes(toolZipPath, Properties.Resources.TOOLDIR);
            ZipFile.ExtractToDirectory(toolZipPath, TempRootPath);
            File.Delete(toolZipPath);

            // Create Source and Build directories
            Directory.CreateDirectory(TempSourcePath);
            Directory.CreateDirectory(TempBuildPath);
        }

        //Specify constants for magic numbers
        private const long WiiGameType = 2745048157;
        private const long GCGameType = 4440324665927270400;

        // Specify private fields for internal state
        private string _systemType = "wii";
        private string _titleIdHex = string.Empty;
        private string _titleIdText = string.Empty;
        private string _internalGameName = string.Empty;
        private bool _flagWbfs;
        private bool _flagNkit;
        private bool _flagNasos;
        private bool _flagGameSpecified;
        private bool _flagGc2Specified;
        private bool _flagIconSpecified;
        private bool _flagBannerSpecified;
        private bool _flagDrcSpecified;
        private bool _flagLogoSpecified;
        private bool _flagBootSoundSpecified;
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
        private string _drcuse = "1";
        private string _pngTempPath = string.Empty;
        private string _loopString = " -noLoop";
        private string _nfsPatchFlag = "";
        private string _passPatch = " -passthrough";
        private string _wiimmfiOption = " --wiimmfi";

        static readonly string JNUSToolDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "JNUSToolDownloads") + Path.DirectorySeparatorChar;
        static readonly string TempRootPath = Path.Combine(Path.GetTempPath(), "Moon WiiVC Injector") + Path.DirectorySeparatorChar;
        static readonly string TempSourcePath = Path.Combine(TempRootPath, "SOURCETEMP") + Path.DirectorySeparatorChar;
        static readonly string TempBuildPath = Path.Combine(TempRootPath, "BUILDDIR") + Path.DirectorySeparatorChar;
        static readonly string TempToolsPath = Path.Combine(TempRootPath, "TOOLDIR") + Path.DirectorySeparatorChar;

        static readonly string TempIconPath = Path.Combine(TempSourcePath, "iconTex.png");
        static readonly string TempBannerPath = Path.Combine(TempSourcePath, "bootTvTex.png");
        static readonly string TempDrcPath = Path.Combine(TempSourcePath, "bootDrcTex.png");
        static readonly string TempLogoPath = Path.Combine(TempSourcePath, "bootLogoTex.png");
        static readonly string TempSoundPath = Path.Combine(TempSourcePath, "bootSound.wav");

        private string _ogFilePath = string.Empty;
        private string _selectedOutputPath = string.Empty;

        //call options
        public void LaunchProgram(string exeFile, string arguments = "", bool hideProcess = true)
        {
            string targetExe = exeFile;
            string targetArgs = arguments;

            // Detect if running on Linux or macOS (Unix)
            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                // Route Windows executables or control panels through Wine
                if (exeFile.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                    exeFile.Contains("/TOOLDIR/") ||
                    exeFile.EndsWith(".cpl", StringComparison.OrdinalIgnoreCase))
                {
                    targetExe = "wine";
                    targetArgs = $"\"{exeFile}\" {arguments}";
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
            using (Process process = Process.Start(launcher))
            {
                process?.WaitForExit();
            }
        }

        public void CleanUp()
        {
            var sourceFilesToDelete = Directory.EnumerateFiles(TempSourcePath, "*.*", System.IO.SearchOption.AllDirectories);
            var buildFilesToDelete = Directory.EnumerateFiles(TempBuildPath, "*.*", System.IO.SearchOption.AllDirectories);
            foreach (var file in sourceFilesToDelete)
                File.Delete(file);
            foreach (var file in buildFilesToDelete)
                File.Delete(file);

            IconPreviewBox.Image = null;
            BannerPreviewBox.Image = null;
            _flagIconSpecified = false;
            _flagBannerSpecified = false;
            IconSourceDirectory.Text = "Icon file has not been specified";
            IconSourceDirectory.ForeColor = Color.Red;
            BannerSourceDirectory.Text = "Banner file has not been specified";
            BannerSourceDirectory.ForeColor = Color.Red;
        }

        public async Task DownloadFromRepoAsync(string cucholixRepoID)
        {
            string baseUrl = Properties.Settings.Default.BannersRepository;
            string iconUrl = $"{baseUrl}{_systemType}/{cucholixRepoID}/iconTex.png";
            string bannerUrl = $"{baseUrl}{_systemType}/{cucholixRepoID}/bootTvTex.png";

            IconPreviewBox.Load(iconUrl);
            if (File.Exists(TempIconPath)) { File.Delete(TempIconPath); }
            var iconBytes = await Program.Client.GetByteArrayAsync(iconUrl);
            await File.WriteAllBytesAsync(TempIconPath, iconBytes);

            IconSourceDirectory.Text = "iconTex.png downloaded from Cucholix's Repo";
            IconSourceDirectory.ForeColor = Color.Black;
            _flagIconSpecified = true;

            BannerPreviewBox.Load(bannerUrl);
            if (File.Exists(TempBannerPath)) { File.Delete(TempBannerPath); }
            var bannerBytes = await Program.Client.GetByteArrayAsync(bannerUrl);
            await File.WriteAllBytesAsync(TempBannerPath, bannerBytes);

            BannerSourceDirectory.Text = "bootTvTex.png downloaded from Cucholix's Repo";
            BannerSourceDirectory.ForeColor = Color.Black;
            _flagBannerSpecified = true;
        }

        //Called from RepoDownload_Click to check if files exist before downloading
        private bool RemoteFileExists(string url)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Head, url))
                using (var response = Program.Client.Send(request))
                {
                    return response != null && response.StatusCode == HttpStatusCode.OK;
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> RemoteFileExistsAsync(string url)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Head, url))
                using (var response = await Program.Client.SendAsync(request))
                {
                    return response != null && response.StatusCode == HttpStatusCode.OK;
                }
            }
            catch
            {
                return false;
            }
        }

        private void CheckForNet35()
        {
            if (Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v3.5") == null)
            {
                MessageBox.Show(".NET Framework 3.5 was not detected on your machine, which is required by programs used during the build process." +
                                "\n\nYou should be able to enable this in \"Programs and Features\" under \"Turn Windows features on or off\", or download it from Microsoft." +
                                "\n\nClick OK to close the injector and open \"Programs and Features\"...", ".NET Framework v3.5 not found..."
                                , MessageBoxButtons.OK
                                , MessageBoxIcon.Exclamation
                                , MessageBoxDefaultButton.Button1
                                , (MessageBoxOptions)0x40000);
                LaunchProgram("appwiz.cpl", "", false);
                Environment.Exit(0);
            }
        }

        //Cleanup when program is closed
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            if (e.CloseReason == CloseReason.WindowsShutDown)
            {
                try { Directory.Delete(TempRootPath, true); } catch { }
                return;
            }

            // Confirm user wants to close
            switch (MessageBox.Show(this, "Are you sure you want to close?"
                    , "Closing"
                    , MessageBoxButtons.YesNo
                    , MessageBoxIcon.Question
                    , MessageBoxDefaultButton.Button1
                    , (MessageBoxOptions)0x40000))
            {
                case DialogResult.No:
                    e.Cancel = true;
                    break;
                default:
                    try { Directory.Delete(TempRootPath, true); } catch { }
                    break;
            }
        }

        //Radio Buttons for desired injection type
        private void WiiRetail_CheckedChanged(object sender, EventArgs e)
        {
            if (WiiRetail.Checked)
            {
                WiiVMC.Enabled = true;
                Wiimmfi.Enabled = true;
                RepoDownload.Enabled = true;
                GameSourceButton.Enabled = true;
                GameSourceButton.Text = "Game...";
                OpenGame.FileName = "game";
                OpenGame.Filter = "Wii Dumps (*.iso,*.wbfs,*.iso.dec)|*.iso;*.wbfs;*.iso.dec";
                GameSourceDirectory.Text = "Game file has not been specified";
                GameSourceDirectory.ForeColor = Color.Red;
                _flagGameSpecified = false;
                _systemType = "wii";
                GameNameLabel.Text = "";
                TitleIDLabel.Text = "";
                _titleIdInt = 0;
                _titleIdHex = "";
                _gameType = 0;
                _cucholixRepoId = "";
                PackedTitleLine1.Text = "";
                PackedTitleIDLine.Text = "";
                GC2SourceButton.Enabled = false;
                GC2SourceDirectory.Text = "2nd GameCube Disc Image has not been specified";
                GC2SourceDirectory.ForeColor = Color.Red;
                _flagGc2Specified = false;
                if (!NoGamePadEmu.Checked && !CCEmu.Checked && !HorWiiMote.Checked && !VerWiiMote.Checked && !ForceCC.Checked && !ForceNoCC.Checked)
                {
                    NoGamePadEmu.Checked = true;
                    GamePadEmuLayout.Enabled = true;
                    _drcuse = "1";
                }
                Force43NINTENDONT.Checked = false;
                Force43NINTENDONT.Enabled = false;
                ForceInterlacedNINTENDONT.Checked = false;
                ForceInterlacedNINTENDONT.Enabled = false;
                CustomMainDol.Checked = false;
                CustomMainDol.Enabled = false;
                DisableNintendontAutoboot.Checked = false;
                DisableNintendontAutoboot.Enabled = false;
                DisablePassthrough.Checked = false;
                DisablePassthrough.Enabled = false;
                DisableGamePad.Checked = false;
                DisableGamePad.Enabled = false;
                C2WPatchFlag.Checked = false;
                C2WPatchFlag.Enabled = false;
                if (ForceCC.Checked) { DisableTrimming.Checked = false; DisableTrimming.Enabled = false; } else { DisableTrimming.Enabled = true; }
                Force43NAND.Checked = false;
                Force43NAND.Enabled = false;
            }
        }
        private void WiiHomebrew_CheckedChanged(object sender, EventArgs e)
        {
            if (WiiHomebrew.Checked)
            {
                WiiVMC.Checked = false;
                WiiVMC.Enabled = false;
                Wiimmfi.Checked = false;
                Wiimmfi.Enabled = false;
                RepoDownload.Enabled = false;
                GameSourceButton.Enabled = true;
                GameSourceButton.Text = "Game...";
                OpenGame.FileName = "boot.dol";
                OpenGame.Filter = "DOL Files (*.dol)|*.dol";
                GameSourceDirectory.Text = "Game file has not been specified";
                GameSourceDirectory.ForeColor = Color.Red;
                _flagGameSpecified = false;
                _systemType = "dol";
                GameNameLabel.Text = "";
                TitleIDLabel.Text = "";
                _titleIdInt = 0;
                _titleIdHex = "";
                _gameType = 0;
                _cucholixRepoId = "";
                PackedTitleLine1.Text = "";
                PackedTitleIDLine.Text = "";
                _drcuse = "65537";
                GC2SourceButton.Enabled = false;
                GC2SourceDirectory.Text = "2nd GameCube Disc Image has not been specified";
                GC2SourceDirectory.ForeColor = Color.Red;
                _flagGc2Specified = false;
                NoGamePadEmu.Checked = false;
                CCEmu.Checked = false;
                HorWiiMote.Checked = false;
                VerWiiMote.Checked = false;
                ForceCC.Checked = false;
                ForceNoCC.Checked = false;
                GamePadEmuLayout.Enabled = false;
                LRPatch.Checked = false;
                LRPatch.Enabled = false;
                Force43NINTENDONT.Checked = false;
                Force43NINTENDONT.Enabled = false;
                ForceInterlacedNINTENDONT.Checked = false;
                ForceInterlacedNINTENDONT.Enabled = false;
                CustomMainDol.Checked = false;
                CustomMainDol.Enabled = false;
                DisableNintendontAutoboot.Checked = false;
                DisableNintendontAutoboot.Enabled = false;
                DisablePassthrough.Enabled = true;
                DisableGamePad.Enabled = true;
                C2WPatchFlag.Enabled = true;
                DisableTrimming.Checked = false;
                DisableTrimming.Enabled = false;
                Force43NAND.Checked = false;
                Force43NAND.Enabled = false;
            }
        }
        private void WiiNAND_CheckedChanged(object sender, EventArgs e)
        {
            if (!WiiNAND.Checked) return;

            bool validId = false;
            while (!validId)
            {
                WiiVMC.Checked = false;
                WiiVMC.Enabled = false;
                Wiimmfi.Checked = false;
                Wiimmfi.Enabled = false;
                RepoDownload.Enabled = true;
                GameSourceButton.Enabled = false;
                GameSourceButton.Text = "TitleID...";
                OpenGame.FileName = "NULL";
                GameNameLabel.Text = "";
                TitleIDLabel.Text = "";
                _titleIdInt = 0;
                _titleIdHex = "";
                _gameType = 0;
                _cucholixRepoId = "";
                PackedTitleLine1.Text = "";
                PackedTitleIDLine.Text = "";
                GC2SourceButton.Enabled = false;
                GC2SourceDirectory.Text = "2nd GameCube Disc Image has not been specified";
                GC2SourceDirectory.ForeColor = Color.Red;
                _flagGc2Specified = false;
                Force43NINTENDONT.Checked = false;
                Force43NINTENDONT.Enabled = false;
                ForceInterlacedNINTENDONT.Checked = false;
                ForceInterlacedNINTENDONT.Enabled = false;
                CustomMainDol.Checked = false;
                CustomMainDol.Enabled = false;
                DisableNintendontAutoboot.Checked = false;
                DisableNintendontAutoboot.Enabled = false;
                DisablePassthrough.Checked = false;
                DisablePassthrough.Enabled = false;
                DisableGamePad.Checked = false;
                DisableGamePad.Enabled = false;
                C2WPatchFlag.Checked = false;
                C2WPatchFlag.Enabled = false;
                DisableTrimming.Checked = false;
                DisableTrimming.Enabled = false;
                Force43NAND.Enabled = true;

                if (!NoGamePadEmu.Checked && !CCEmu.Checked && !HorWiiMote.Checked && !VerWiiMote.Checked && !ForceCC.Checked && !ForceNoCC.Checked)
                {
                    NoGamePadEmu.Checked = true;
                    GamePadEmuLayout.Enabled = true;
                    _drcuse = "1";
                }

                string inputId = GuiUtil.PromptInput("Enter your installed Wii Channel's 4-letter Title ID. If you don't know it, open a WAD for the channel in something like ShowMiiWads to view it.", "Enter your WAD's Title ID", "XXXX");

                if (string.IsNullOrEmpty(inputId))
                {
                    GameSourceDirectory.ForeColor = Color.Red;
                    GameSourceDirectory.Text = "Title ID specification cancelled, reselect vWii NAND Title Launcher to specify";
                    _flagGameSpecified = false;
                    break;
                }

                if (inputId.Length == 4)
                {
                    GameSourceDirectory.Text = inputId.ToUpper();
                    GameSourceDirectory.ForeColor = Color.Black;
                    _flagGameSpecified = true;
                    _systemType = "wiiware";
                    GameNameLabel.Text = "N/A";
                    TitleIDLabel.Text = "N/A";
                    _titleIdText = GameSourceDirectory.Text;
                    _cucholixRepoId = GameSourceDirectory.Text;

                    StringBuilder stringBuilder = new StringBuilder();
                    foreach (char c in GameSourceDirectory.Text)
                    {
                        stringBuilder.Append(((short)c).ToString("X2"));
                    }
                    PackedTitleIDLine.Text = "00050002" + stringBuilder.ToString();
                    validId = true;
                }
                else
                {
                    GameSourceDirectory.ForeColor = Color.Red;
                    GameSourceDirectory.Text = "Invalid Title ID";
                    _flagGameSpecified = false;
                    MessageBox.Show("Only 4 characters can be used, try again. Example: The Star Fox 64 (USA) Channel's Title ID is NADE01, so you would specify NADE as the Title ID",
                        "Invalid Title ID",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button1,
                        (MessageBoxOptions)0x40000);
                }
            }
        }
        private void GCRetail_CheckedChanged(object sender, EventArgs e)
        {
            if (GCRetail.Checked)
            {
                WiiVMC.Checked = false;
                WiiVMC.Enabled = false;
                Wiimmfi.Checked = false;
                Wiimmfi.Enabled = false;
                RepoDownload.Enabled = true;
                GameSourceButton.Enabled = true;
                GameSourceButton.Text = "Game...";
                OpenGame.FileName = "game";
                OpenGame.Filter = "GameCube Dumps (*.gcm,*.iso)|*.gcm;*.iso";
                GameSourceDirectory.Text = "Game file has not been specified";
                GameSourceDirectory.ForeColor = Color.Red;
                _flagGameSpecified = false;
                _systemType = "gcn";
                GameNameLabel.Text = "";
                TitleIDLabel.Text = "";
                _titleIdInt = 0;
                _titleIdHex = "";
                _gameType = 0;
                _cucholixRepoId = "";
                PackedTitleLine1.Text = "";
                PackedTitleIDLine.Text = "";
                _drcuse = "65537";
                GC2SourceButton.Enabled = true;
                NoGamePadEmu.Checked = false;
                CCEmu.Checked = false;
                HorWiiMote.Checked = false;
                VerWiiMote.Checked = false;
                ForceCC.Checked = false;
                ForceNoCC.Checked = false;
                GamePadEmuLayout.Enabled = false;
                LRPatch.Checked = false;
                LRPatch.Enabled = false;
                Force43NINTENDONT.Enabled = true;
                ForceInterlacedNINTENDONT.Enabled = true;
                CustomMainDol.Enabled = true;
                DisableNintendontAutoboot.Enabled = true;
                DisablePassthrough.Checked = false;
                DisablePassthrough.Enabled = false;
                DisableGamePad.Enabled = true;
                C2WPatchFlag.Checked = false;
                C2WPatchFlag.Enabled = false;
                DisableTrimming.Checked = false;
                DisableTrimming.Enabled = false;
                Force43NAND.Checked = false;
                Force43NAND.Enabled = false;
            }
        }
        private void SettingsButton_Click(object sender, EventArgs e)
        {
            using (var settingsForm = new SettingsForm())
            {
                settingsForm.ShowDialog(this);
            }
        }
        private void SDCardStuff_Click(object sender, EventArgs e)
        {
            new SdCardMenuAvalonia().Show();
        }

        //Performs actions when switching tabs
        private void MainTabs_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Disables Radio buttons when switching away from the main tab
            bool isSourceFilesTab = MainTabs.SelectedTab == SourceFilesTab;
            WiiRetail.Enabled = isSourceFilesTab;
            WiiHomebrew.Enabled = isSourceFilesTab;
            WiiNAND.Enabled = isSourceFilesTab;
            GCRetail.Enabled = isSourceFilesTab;

            // Check for building requirements when switching to the Build tab
            if (MainTabs.SelectedTab == BuildTab)
            {
                WiiUCommonKey.Text = string.IsNullOrEmpty(Properties.Settings.Default.WiiUCommonKey)
                    ? "00000000000000000000000000000000"
                    : Properties.Settings.Default.WiiUCommonKey.ToUpper();
                TitleKey.Text = string.IsNullOrEmpty(Properties.Settings.Default.TitleKey)
                    ? "00000000000000000000000000000000"
                    : Properties.Settings.Default.TitleKey.ToUpper();
                AncastKey.Text = string.IsNullOrEmpty(Properties.Settings.Default.AncastKey)
                    ? "00000000000000000000000000000000"
                    : Properties.Settings.Default.AncastKey.ToUpper();

                _commonKeyGood = SetKeyStatus(WiiUCommonKey, "35-AC-59-94-97-22-79-33-1D-97-09-4F-A2-FB-97-FC");
                _titleKeyGood = SetKeyStatus(TitleKey, "F9-4B-D8-8E-BB-7A-A9-38-67-E6-30-61-5F-27-1C-9F");
                _ancastKeyGood = SetKeyStatus(AncastKey, "31-8D-1F-9D-98-FB-08-E7-7C-7F-E1-77-AA-49-05-43");

                // Final check for if all requirements are good
                _buildFlagSource = _flagGameSpecified && _flagIconSpecified && _flagBannerSpecified;
                SourceCheck.ForeColor = _buildFlagSource ? Color.Green : Color.Red;

                _buildFlagMeta = !string.IsNullOrEmpty(PackedTitleLine1.Text) && PackedTitleIDLine.TextLength == 16;
                MetaCheck.ForeColor = _buildFlagMeta ? Color.Green : Color.Red;

                if (!CustomMainDol.Checked)
                {
                    AdvanceCheck.ForeColor = Color.Green;
                    _buildFlagAdvance = true;
                }
                else
                {
                    _buildFlagAdvance = Path.GetExtension(OpenMainDol.FileName).Equals(".dol", StringComparison.OrdinalIgnoreCase);
                    AdvanceCheck.ForeColor = _buildFlagAdvance ? Color.Green : Color.Red;
                }

                // Skip Ancast Key if box not checked in advanced
                if (!C2WPatchFlag.Checked)
                {
                    _buildFlagKeys = _commonKeyGood && _titleKeyGood;
                }
                else
                {
                    _buildFlagKeys = _commonKeyGood && _titleKeyGood && _ancastKeyGood;
                }
                KeysCheck.ForeColor = _buildFlagKeys ? Color.Green : Color.Red;

                // Enable Build Button
                TheBigOneTM.Enabled = _buildFlagSource && _buildFlagMeta && _buildFlagAdvance && _buildFlagKeys;
            }
        }

        private string GetMd5Hash(MD5 md5Hash, string input)
        {
            byte[] data = md5Hash.ComputeHash(Encoding.ASCII.GetBytes(input));
            return BitConverter.ToString(data);
        }

        private bool SetKeyStatus(TextBox keyTextBox, string expectedHash)
        {
            keyTextBox.Text = keyTextBox.Text.ToUpper();
            using (var md5 = MD5.Create())
            {
                bool isValid = string.Equals(GetMd5Hash(md5, keyTextBox.Text), expectedHash, StringComparison.OrdinalIgnoreCase);
                keyTextBox.ReadOnly = isValid;
                keyTextBox.BackColor = isValid ? Color.Lime : Color.White;
                return isValid;
            }
        }

        //Events for the "Required Source Files" Tab
        private void GameSourceButton_Click(object sender, EventArgs e)
        {
            if (OpenGame.ShowDialog() != DialogResult.OK) return;

            // delete any previous files
            CleanUp();

            GameSourceDirectory.Text = OpenGame.FileName;
            GameSourceDirectory.ForeColor = Color.Black;
            _flagGameSpecified = true;
            byte[] idBytes = new byte[4];

            // Get values from game file
            using (var fs = File.OpenRead(OpenGame.FileName))
            {
                fs.Position = 0;
                fs.ReadExactly(idBytes);
                _titleIdInt = BitConverter.ToInt32(idBytes);
                string idString = Encoding.ASCII.GetString(idBytes);

                // WBFS Check
                if (idString == "WBFS") // Performs actions if the header indicates a WBFS file
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
                    if (_titleIdInt == 65536) // Performs actions if the header indicates a DOL file
                    {
                        fs.Position = 0x2A0;
                        fs.ReadExactly(idBytes);
                        _titleIdInt = BitConverter.ToInt32(idBytes);
                        _internalGameName = "N/A";
                    }
                    else // Performs actions if the header indicates a normal Wii / GC iso
                    {
                        _flagWbfs = false;
                        _flagNkit = false;
                        _flagNasos = false;
                        uint startOffset = 0;

                        // NASOS check
                        if (idString == "WII5")
                        {
                            _flagNasos = true;
                            startOffset = 0x1182800;
                        }
                        else if (idString == "WII9")
                        {
                            _flagNasos = true;
                            startOffset = 0x1FB5000;
                        }

                        // read game info
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

                        // NKIT check
                        if (!_flagNasos)
                        {
                            fs.Position = 0x200;
                            fs.ReadExactly(idBytes);
                            idString = Encoding.ASCII.GetString(idBytes);
                            if (idString == "NKIT")
                            {
                                _flagNkit = true;
                            }
                        }
                    }
                }
            }

            // Flag if GameType Int doesn't match current SystemType
            if ((_systemType == "wii" && _gameType != WiiGameType) || (_systemType == "gcn" && _gameType != GCGameType))
            {
                string errorMsg = _systemType == "wii" ? "This is not a Wii image. It will not be loaded." : "This is not a GameCube image. It will not be loaded.";

                GameSourceDirectory.Text = "Game file has not been specified";
                GameSourceDirectory.ForeColor = Color.Red;
                _flagGameSpecified = false;
                GameNameLabel.Text = "";
                TitleIDLabel.Text = "";
                _titleIdInt = 0;
                _titleIdHex = "";
                _gameType = 0;
                _cucholixRepoId = "";
                PackedTitleLine1.Text = "";
                PackedTitleIDLine.Text = "";

                MessageBox.Show(errorMsg, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, (MessageBoxOptions)0x40000);
                return;
            }

            GameNameLabel.Text = _internalGameName;
            var GameTitle = StringUtil.RemoveSpecialChars(GameTdb.GetName(_cucholixRepoId));
            PackedTitleLine1.Text = !string.IsNullOrEmpty(GameTitle) ? GameTitle : _internalGameName;

            // Convert pulled Title ID Int to Hex for use with Wii U Title ID
            byte[] titleIdBytes = BitConverter.GetBytes(_titleIdInt);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(titleIdBytes);
            }
            _titleIdHex = BitConverter.ToString(titleIdBytes).Replace("-", "");

            if (_systemType == "dol")
            {
                TitleIDLabel.Text = _titleIdHex;
                PackedTitleIDLine.Text = $"00050002{_titleIdHex}";
                _titleIdText = "BOOT";
            }
            else
            {
                _titleIdText = string.Join("", System.Text.RegularExpressions.Regex.Split(_titleIdHex, "(?<=\\G..)(?!$)").Select(x => (char)Convert.ToByte(x, 16)));
                TitleIDLabel.Text = $"{_titleIdText} / {_titleIdHex}";
                PackedTitleIDLine.Text = $"00050002{_titleIdHex}";
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

        private void IconSourceButton_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Make sure your icon is 128x128 (1:1) to prevent distortion",
                "Icon Size Information",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1,
                (MessageBoxOptions)0x40000);

            if (OpenIcon.ShowDialog() == DialogResult.OK)
            {
                _pngTempPath = TempIconPath;
                if (File.Exists(_pngTempPath)) { File.Delete(_pngTempPath); }

                if (Path.GetExtension(OpenIcon.FileName).Equals(".tga", StringComparison.OrdinalIgnoreCase))
                {
                    using (Bitmap bmp = TgaReader.LoadTga(OpenIcon.FileName))
                    {
                        bmp.Save(_pngTempPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                else
                {
                    using (var img = Image.FromFile(OpenIcon.FileName))
                    {
                        img.Save(_pngTempPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }

                using (var tempstream = new FileStream(_pngTempPath, FileMode.Open, FileAccess.Read))
                {
                    IconPreviewBox.Image = Image.FromStream(tempstream);
                }

                IconSourceDirectory.Text = OpenIcon.FileName;
                IconSourceDirectory.ForeColor = Color.Black;
                _flagIconSpecified = true;
            }
            else
            {
                IconPreviewBox.Image = null;
                IconSourceDirectory.Text = "Icon has not been specified";
                IconSourceDirectory.ForeColor = Color.Red;
                _flagIconSpecified = false;
                _pngTempPath = "";
            }
        }
        private void BannerSourceButton_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Make sure your Banner is 1280x720 (16:9) to prevent distortion",
                "Banner Size Information",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1,
                (MessageBoxOptions)0x40000);

            if (OpenBanner.ShowDialog() == DialogResult.OK)
            {
                _pngTempPath = TempBannerPath;
                if (File.Exists(_pngTempPath)) { File.Delete(_pngTempPath); }

                if (Path.GetExtension(OpenBanner.FileName).Equals(".tga", StringComparison.OrdinalIgnoreCase))
                {
                    using (Bitmap bmp = TgaReader.LoadTga(OpenBanner.FileName))
                    {
                        bmp.Save(_pngTempPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                else
                {
                    using (var img = Image.FromFile(OpenBanner.FileName))
                    {
                        img.Save(_pngTempPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }

                using (var tempstream = new FileStream(_pngTempPath, FileMode.Open, FileAccess.Read))
                {
                    BannerPreviewBox.Image = Image.FromStream(tempstream);
                }

                BannerSourceDirectory.Text = OpenBanner.FileName;
                BannerSourceDirectory.ForeColor = Color.Black;
                _flagBannerSpecified = true;
            }
            else
            {
                BannerPreviewBox.Image = null;
                BannerSourceDirectory.Text = "Banner has not been specified";
                BannerSourceDirectory.ForeColor = Color.Red;
                _flagBannerSpecified = false;
                _pngTempPath = "";
            }
        }
        private async void RepoDownload_Click(object sender, EventArgs e)
        {
            if (_cucholixRepoId == "")
            {
                MessageBox.Show("Please select your game before using this option"
                                , "No game specified"
                                , MessageBoxButtons.OK
                                , MessageBoxIcon.Information
                                , MessageBoxDefaultButton.Button1
                                , (MessageBoxOptions)0x40000);
            }
            else
            {
                if (!await TryDownloadImagesAsync(_cucholixRepoId))
                {
                    if (MessageBox.Show("Cucholix's Repo does not have assets for your game. You will need to provide your own. Would you like to visit the GBAtemp request thread?"
                                        , "Game not found on Repo"
                                        , MessageBoxButtons.YesNo
                                        , MessageBoxIcon.Asterisk
                                        , MessageBoxDefaultButton.Button1,
                                        (MessageBoxOptions)0x40000) == DialogResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo("https://gbatemp.net/threads/483080/") { UseShellExecute = true });
                    }
                }
            }
        }

        private async Task<bool> TryDownloadImagesAsync(string cucholixRepoID)
        {
            IEnumerable<string> ids = GameTdb.GetAlternativeIds(cucholixRepoID);
            foreach (var id in ids)
            {
                if (await RemoteFileExistsAsync(Properties.Settings.Default.BannersRepository + _systemType + "/" + id + "/iconTex.png"))
                {
                    await DownloadFromRepoAsync(id);
                    return true;
                }
            }
            return false;
        }

        //Events for the "Optional Source Files" Tab
        private void GC2SourceButton_Click(object sender, EventArgs e)
        {
            if (OpenGC2.ShowDialog() == DialogResult.OK)
            {
                using (var fs = File.OpenRead(OpenGC2.FileName))
                {
                    fs.Position = 0x18;
                    byte[] typeBytes = new byte[8];
                    fs.ReadExactly(typeBytes);
                    long gc2GameType = BitConverter.ToInt64(typeBytes);
                    if (gc2GameType != 4440324665927270400)
                    {
                        MessageBox.Show("This is not a GameCube image. It will not be loaded."
                                        , "Error"
                                        , MessageBoxButtons.OK
                                        , MessageBoxIcon.Error
                                        , MessageBoxDefaultButton.Button1
                                        , (MessageBoxOptions)0x40000);
                        GC2SourceDirectory.Text = "2nd GameCube Disc Image has not been specified";
                        GC2SourceDirectory.ForeColor = Color.Red;
                        _flagGc2Specified = false;
                    }
                    else
                    {
                        GC2SourceDirectory.Text = OpenGC2.FileName;
                        GC2SourceDirectory.ForeColor = Color.Black;
                        _flagGc2Specified = true;
                    }
                }
            }
            else
            {
                GC2SourceDirectory.Text = "2nd GameCube Disc Image has not been specified";
                GC2SourceDirectory.ForeColor = Color.Red;
                _flagGc2Specified = false;
            }
        }
        private void DrcSourceButton_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Make sure your GamePad Banner is 854x480 (16:9) to prevent distortion"
                            , "Banner Information"
                            , MessageBoxButtons.OK
                            , MessageBoxIcon.Information
                            , MessageBoxDefaultButton.Button1
                            , (MessageBoxOptions)0x40000);
            if (OpenDrc.ShowDialog() == DialogResult.OK)
            {
                _pngTempPath = TempDrcPath;
                if (File.Exists(_pngTempPath)) { File.Delete(_pngTempPath); }

                if (Path.GetExtension(OpenDrc.FileName).Equals(".tga", StringComparison.OrdinalIgnoreCase))
                {
                    using (Bitmap bmp = TgaReader.LoadTga(OpenDrc.FileName))
                    {
                        bmp.Save(_pngTempPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                else
                {
                    using (var img = Image.FromFile(OpenDrc.FileName))
                    {
                        img.Save(_pngTempPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                using (FileStream tempstream = new FileStream(_pngTempPath, FileMode.Open, FileAccess.Read))
                {
                    DrcPreviewBox.Image = Image.FromStream(tempstream);
                }
                DrcSourceDirectory.Text = OpenDrc.FileName;
                DrcSourceDirectory.ForeColor = Color.Black;
                _flagDrcSpecified = true;
            }
            else
            {
                DrcPreviewBox.Image = null;
                DrcSourceDirectory.Text = "GamePad Banner has not been specified";
                DrcSourceDirectory.ForeColor = Color.Red;
                _pngTempPath = "";
            }
        }
        private void LogoSourceButton_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Make sure your Logo is 170x42 to prevent distortion"
                            , "Logo Information"
                            , MessageBoxButtons.OK
                            , MessageBoxIcon.Information
                            , MessageBoxDefaultButton.Button1
                            , (MessageBoxOptions)0x40000);
            if (OpenLogo.ShowDialog() == DialogResult.OK)
            {
                _pngTempPath = TempLogoPath;
                if (File.Exists(_pngTempPath)) { File.Delete(_pngTempPath); }

                if (Path.GetExtension(OpenLogo.FileName).Equals(".tga", StringComparison.OrdinalIgnoreCase))
                {
                    using (Bitmap bmp = TgaReader.LoadTga(OpenLogo.FileName))
                    {
                        bmp.Save(_pngTempPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                else
                {
                    using (var img = Image.FromFile(OpenLogo.FileName))
                    {
                        img.Save(_pngTempPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                using (FileStream tempstream = new FileStream(_pngTempPath, FileMode.Open, FileAccess.Read))
                {
                    LogoPreviewBox.Image = Image.FromStream(tempstream);
                }
                LogoSourceDirectory.Text = OpenLogo.FileName;
                LogoSourceDirectory.ForeColor = Color.Black;
                _flagLogoSpecified = true;
            }
            else
            {
                LogoPreviewBox.Image = null;
                LogoSourceDirectory.Text = "Boot Logo has not been specified";
                LogoSourceDirectory.ForeColor = Color.Red;
                _pngTempPath = "";
            }
        }
        private void BootSoundButton_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Your sound file will be cut off if it's longer than 6 seconds to prevent the Wii U from not loading it. When the Wii U plays the boot sound, it will fade out once it's done loading the game (usually after about 5 seconds). You can not change this."
                            , "Boot Sound Information"
                            , MessageBoxButtons.OK
                            , MessageBoxIcon.Information
                            , MessageBoxDefaultButton.Button1
                            , (MessageBoxOptions)0x40000);
            if (OpenBootSound.ShowDialog() == DialogResult.OK)
            {
                using (var fs = File.OpenRead(OpenBootSound.FileName))
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
                        BootSoundDirectory.Text = OpenBootSound.FileName;
                        BootSoundDirectory.ForeColor = Color.Black;
                        BootSoundPreviewButton.Enabled = true;
                        _flagBootSoundSpecified = true;
                    }
                    else
                    {
                        MessageBox.Show("This is not a valid WAV file. It will not be loaded. \nConsider converting it with something like Audacity."
                            , "Not a WAV File"
                            , MessageBoxButtons.OK
                            , MessageBoxIcon.Error
                            , MessageBoxDefaultButton.Button1
                            , (MessageBoxOptions)0x40000);
                        BootSoundDirectory.Text = "Boot Sound has not been specified";
                        BootSoundDirectory.ForeColor = Color.Red;
                        BootSoundPreviewButton.Enabled = false;
                        _flagBootSoundSpecified = false;
                    }
                }
            }
            else
            {
                if (BootSoundPreviewButton.Text != "Stop Sound")
                {
                    BootSoundDirectory.Text = "Boot Sound has not been specified";
                    BootSoundDirectory.ForeColor = Color.Red;
                    BootSoundPreviewButton.Enabled = false;
                    _flagBootSoundSpecified = false;
                }
            }
        }
        private void ToggleBootSoundLoop_CheckedChanged(object sender, EventArgs e)
        {
            if (ToggleBootSoundLoop.Checked)
            {
                _loopString = "";
            }
            else
            {
                _loopString = " -noLoop";
            }
        }
        private void BootSoundPreviewButton_Click(object sender, EventArgs e)
        {
            var simpleSound = new SoundPlayer(OpenBootSound.FileName);
            if (BootSoundPreviewButton.Text == "Stop Sound")
            {
                simpleSound.Stop();
                BootSoundPreviewButton.Text = "Play Sound";
            }
            else
            {
                if (ToggleBootSoundLoop.Checked)
                {
                    simpleSound.PlayLooping();
                    BootSoundPreviewButton.Text = "Stop Sound";
                }
                else
                {
                    simpleSound.Play();
                }
            }
        }

        //Events for the "GamePad/Meta Options" Tab
        private void EnablePackedLine2_CheckedChanged(object sender, EventArgs e)
        {
            if (EnablePackedLine2.Checked)
            {
                PackedTitleLine2.Text = "";
                PackedTitleLine2.BackColor = Color.White;
                PackedTitleLine2.ReadOnly = false;
            }
            else
            {
                PackedTitleLine2.Text = "(Optional) Line 2";
                PackedTitleLine2.BackColor = Color.Silver;
                PackedTitleLine2.ReadOnly = true;
            }

        }
        //Radio Buttons for GamePad Emulation Mode
        private void NoGamePadEmu_CheckedChanged(object sender, EventArgs e)
        {
            if (NoGamePadEmu.Checked)
            {
                _drcuse = "1";
                _nfsPatchFlag = "";
                LRPatch.Checked = false;
                LRPatch.Enabled = false;
            }
        }
        private void CCEmu_CheckedChanged(object sender, EventArgs e)
        {
            if (CCEmu.Checked)
            {
                _drcuse = "65537";
                _nfsPatchFlag = "";
                LRPatch.Enabled = true;
            }
        }
        private void HorWiiMote_CheckedChanged(object sender, EventArgs e)
        {
            if (HorWiiMote.Checked)
            {
                _drcuse = "65537";
                _nfsPatchFlag = " -horizontal";
                LRPatch.Checked = false;
                LRPatch.Enabled = false;
            }
        }
        private void VerWiiMote_CheckedChanged(object sender, EventArgs e)
        {
            if (VerWiiMote.Checked)
            {
                _drcuse = "65537";
                _nfsPatchFlag = " -wiimote";
                LRPatch.Checked = false;
                LRPatch.Enabled = false;
            }
        }
        private void ForceCC_CheckedChanged(object sender, EventArgs e)
        {
            if (ForceCC.Checked)
            {
                _drcuse = "65537";
                _nfsPatchFlag = " -instantcc";
                DisableTrimming.Checked = false;
                DisableTrimming.Enabled = false;
                LRPatch.Enabled = true;
            }
        }
        private void ForceWiiMote_CheckedChanged(object sender, EventArgs e)
        {
            if (ForceNoCC.Checked)
            {
                _drcuse = "65537";
                _nfsPatchFlag = " -nocc";
                LRPatch.Checked = false;
                LRPatch.Enabled = false;
            }
        }
        private void TutorialLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("http://www.google.com") { UseShellExecute = true });
        }

        //Events for the Advanced Tab
        private void Force43NINTENDONT_CheckedChanged(object sender, EventArgs e)
        {
            if (Force43NINTENDONT.Checked || ForceInterlacedNINTENDONT.Checked)
            {
                CustomMainDol.Checked = false;
                CustomMainDol.Enabled = false;
                DisableNintendontAutoboot.Checked = false;
                DisableNintendontAutoboot.Enabled = false;
            }
            else
            {
                CustomMainDol.Enabled = true;
                DisableNintendontAutoboot.Enabled = true;
            }
        }
        private void ForceInterlacedNINTENDONT_CheckedChanged(object sender, EventArgs e)
        {
            if (ForceInterlacedNINTENDONT.Checked || Force43NINTENDONT.Checked)
            {
                CustomMainDol.Checked = false;
                CustomMainDol.Enabled = false;
                DisableNintendontAutoboot.Checked = false;
                DisableNintendontAutoboot.Enabled = false;
            }
            else
            {
                CustomMainDol.Enabled = true;
                DisableNintendontAutoboot.Enabled = true;
            }
        }
        private void CustomMainDol_CheckedChanged(object sender, EventArgs e)
        {
            if (CustomMainDol.Checked)
            {
                MainDolSourceButton.Enabled = true;
                Force43NINTENDONT.Checked = false;
                Force43NINTENDONT.Enabled = false;
                ForceInterlacedNINTENDONT.Checked = false;
                ForceInterlacedNINTENDONT.Enabled = false;
                DisableNintendontAutoboot.Checked = false;
                DisableNintendontAutoboot.Enabled = false;

            }
            else
            {
                MainDolSourceButton.Enabled = false;
                MainDolLabel.Text = "<- Specify custom main.dol file";
                Force43NINTENDONT.Enabled = true;
                ForceInterlacedNINTENDONT.Enabled = true;
                DisableNintendontAutoboot.Enabled = true;
                OpenMainDol.FileName = null;
            }
        }
        private void NintendontAutoboot_CheckedChanged(object sender, EventArgs e)
        {
            if (DisableNintendontAutoboot.Checked)
            {
                Force43NINTENDONT.Checked = false;
                Force43NINTENDONT.Enabled = false;
                ForceInterlacedNINTENDONT.Checked = false;
                ForceInterlacedNINTENDONT.Enabled = false;
                CustomMainDol.Checked = false;
                CustomMainDol.Enabled = false;
            }
            else
            {
                Force43NINTENDONT.Enabled = true;
                ForceInterlacedNINTENDONT.Enabled = true;
                CustomMainDol.Enabled = true;
            }
        }
        private void MainDolSourceButton_Click(object sender, EventArgs e)
        {
            if (OpenMainDol.ShowDialog() == DialogResult.OK)
            {
                MainDolLabel.Text = OpenMainDol.FileName;
            }
            else
            {
                MainDolLabel.Text = "<- Specify custom main.dol file";
            }
        }
        private void DisablePassthrough_CheckedChanged(object sender, EventArgs e)
        {
            if (DisablePassthrough.Checked)
            {
                _passPatch = "";
            }
            else
            {
                _passPatch = " -passthrough";
            }
        }
        private void DisableGamePad_CheckedChanged(object sender, EventArgs e)
        {
            if (DisableGamePad.Checked)
            {
                if (_systemType == "gcn" || _systemType == "dol")
                {
                    _drcuse = "1";
                }
            }
            else
            {
                if (_systemType == "gcn" || _systemType == "dol")
                {
                    _drcuse = "65537";
                }
            }
        }
        private void C2WPatchFlag_CheckedChanged(object sender, EventArgs e)
        {
            AncastKey.Visible = C2WPatchFlag.Checked;
            SaveAncastKeyButton.Visible = C2WPatchFlag.Checked;

            if (C2WPatchFlag.Checked)
            {
                AncastKey.ReadOnly = false;
                AncastKey.BackColor = Color.White;
                SaveAncastKeyButton.Enabled = true;

                if (string.IsNullOrEmpty(Properties.Settings.Default.AncastKey))
                {
                    Properties.Settings.Default.AncastKey = "00000000000000000000000000000000";
                    Properties.Settings.Default.Save();
                }
                AncastKey.Text = Properties.Settings.Default.AncastKey.ToUpper();
                SetKeyStatus(AncastKey, "31-8D-1F-9D-98-FB-08-E7-7C-7F-E1-77-AA-49-05-43");
            }
            else
            {
                AncastKey.BackColor = Color.Silver;
                AncastKey.ReadOnly = true;
                SaveAncastKeyButton.Enabled = false;
            }
        }
        private void SaveAncastKeyButton_Click(object sender, EventArgs e)
        {
            AncastKey.Text = AncastKey.Text.ToUpper();
            if (SetKeyStatus(AncastKey, "31-8D-1F-9D-98-FB-08-E7-7C-7F-E1-77-AA-49-05-43"))
            {
                Properties.Settings.Default.AncastKey = AncastKey.Text;
                Properties.Settings.Default.Save();
                MessageBox.Show("The Wii U Starbuck Ancast Key has been verified."
                                , "Success"
                                , MessageBoxButtons.OK
                                , MessageBoxIcon.Information
                                , MessageBoxDefaultButton.Button1
                                , (MessageBoxOptions)0x40000);
            }
            else
            {
                MessageBox.Show("The Wii U Starbuck Ancast Key you have provided is incorrect" + "\n" + "(MD5 Hash verification failed)"
                                , "Invalid Starbuck Ancast Key"
                                , MessageBoxButtons.OK
                                , MessageBoxIcon.Error
                                , MessageBoxDefaultButton.Button1
                                , (MessageBoxOptions)0x40000);
            }
        }
        private void sign_c2w_patcher_link_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://github.com/FIX94/sign_c2w_patcher") { UseShellExecute = true });
        }
        private void DisableTrimming_CheckedChanged(object sender, EventArgs e)
        {
            if (DisableTrimming.Checked)
            {
                WiiVMC.Checked = false;
                WiiVMC.Enabled = false;
                Wiimmfi.Checked = false;
                Wiimmfi.Enabled = false;
            }
            else
            {
                if (_systemType == "wii")
                {
                    WiiVMC.Enabled = true;
                    Wiimmfi.Enabled = true;
                }
                else
                {
                    WiiVMC.Checked = false;
                    WiiVMC.Enabled = false;
                    Wiimmfi.Checked = false;
                    Wiimmfi.Enabled = false;
                }
            }
        }

        //Events for the "Build Title" Tab
        private void SaveCommonKeyButton_Click(object sender, EventArgs e)
        {
            WiiUCommonKey.Text = WiiUCommonKey.Text.ToUpper();
            if (SetKeyStatus(WiiUCommonKey, "35-AC-59-94-97-22-79-33-1D-97-09-4F-A2-FB-97-FC"))
            {
                Properties.Settings.Default.WiiUCommonKey = WiiUCommonKey.Text;
                Properties.Settings.Default.Save();
                MessageBox.Show("The Wii U Common Key has been verified."
                                , "Success"
                                , MessageBoxButtons.OK
                                , MessageBoxIcon.Information
                                , MessageBoxDefaultButton.Button1
                                , (MessageBoxOptions)0x40000);
                MainTabs.SelectedTab = AdvancedTab;
                MainTabs.SelectedTab = BuildTab;
            }
            else
            {
                MessageBox.Show("The Wii U Common Key you have provided is incorrect" + "\n" + "(MD5 Hash verification failed)"
                                , "Invalid Wii U Common Key"
                                , MessageBoxButtons.OK
                                , MessageBoxIcon.Error
                                , MessageBoxDefaultButton.Button1
                                , (MessageBoxOptions)0x40000);
            }
        }
        private void SaveTitleKeyButton_Click(object sender, EventArgs e)
        {
            TitleKey.Text = TitleKey.Text.ToUpper();
            if (SetKeyStatus(TitleKey, "F9-4B-D8-8E-BB-7A-A9-38-67-E6-30-61-5F-27-1C-9F"))
            {
                Properties.Settings.Default.TitleKey = TitleKey.Text;
                Properties.Settings.Default.Save();
                MessageBox.Show("The Title Key has been verified."
                                , "Success"
                                , MessageBoxButtons.OK
                                , MessageBoxIcon.Information
                                , MessageBoxDefaultButton.Button1
                                , (MessageBoxOptions)0x40000);
                MainTabs.SelectedTab = AdvancedTab;
                MainTabs.SelectedTab = BuildTab;
            }
            else
            {
                MessageBox.Show("The Title Key you have provided is incorrect" + "\n" + "(MD5 Hash verification failed)"
                                , "Invalid Title Key"
                                , MessageBoxButtons.OK
                                , MessageBoxIcon.Error
                                , MessageBoxDefaultButton.Button1
                                , (MessageBoxOptions)0x40000);
            }
        }

        //Events for the actual "Build" Button
        private void TheBigOneTM_Click(object sender, EventArgs e)
        {
            //Initialize Build Process
            //Disable form elements so navigation can't be attempted during build process
            MainTabs.Enabled = false;
            //Check for free space
            if (_systemType == "wii")
            {
                long gamesize = new FileInfo(OpenGame.FileName).Length;
                var drive = new DriveInfo(TempRootPath);
                long freeSpaceInBytes = drive.AvailableFreeSpace;
                if (freeSpaceInBytes < gamesize * 2 + 5000000000)
                {
                    DialogResult dialogResult = MessageBox.Show("Your hard drive may be low on space. The conversion process involves temporary files" +
                                                                "that can amount to more than double the size of your game. If you continue without" +
                                                                "clearing some hard drive space, the conversion may fail. Do you want to continue anyway?",
                                                                "Check your hard drive space"
                                                                , MessageBoxButtons.YesNo
                                                                , MessageBoxIcon.Warning
                                                                , MessageBoxDefaultButton.Button1
                                                                , (MessageBoxOptions)0x40000);
                    if (dialogResult == DialogResult.No)
                    {
                        MainTabs.Enabled = true;
                        BuildStatus.Text = "";
                        BuildStatus.Refresh();
                        BuildProgress.Value = 0;
                        return;
                    }
                }
            }
            if (_systemType == "dol")
            {
                var drive = new DriveInfo(TempRootPath);
                long freeSpaceInBytes = drive.AvailableFreeSpace;
                if (freeSpaceInBytes < 6000000000)
                {
                    DialogResult dialogResult = MessageBox.Show("Your hard drive may be low on space. Even for small programs," +
                                                                "the conversion process can use almost 5 GB of temporary storage." +
                                                                "If you continue without clearing some hard drive space, the conversion may fail." +
                                                                "Do you want to continue anyway?"
                                                                , "Check your hard drive space"
                                                                , MessageBoxButtons.YesNo
                                                                , MessageBoxIcon.Warning
                                                                , MessageBoxDefaultButton.Button1
                                                                , (MessageBoxOptions)0x40000);
                    if (dialogResult == DialogResult.No)
                    {
                        MainTabs.Enabled = true;
                        BuildStatus.Text = "";
                        BuildStatus.Refresh();
                        BuildProgress.Value = 0;
                        return;
                    }
                }
            }
            if (_systemType == "wiiware")
            {
                var drive = new DriveInfo(TempRootPath);
                long freeSpaceInBytes = drive.AvailableFreeSpace;
                if (freeSpaceInBytes < 6000000000)
                {
                    DialogResult dialogResult = MessageBox.Show("Your hard drive may be low on space. Even for small programs," +
                                                                "the conversion process can use almost 5 GB of temporary storage." +
                                                                "If you continue without clearing some hard drive space, the conversion may fail." +
                                                                "Do you want to continue anyway?"
                                                                , "Check your hard drive space"
                                                                , MessageBoxButtons.YesNo
                                                                , MessageBoxIcon.Warning
                                                                , MessageBoxDefaultButton.Button1
                                                                , (MessageBoxOptions)0x40000);
                    if (dialogResult == DialogResult.No)
                    {
                        MainTabs.Enabled = true;
                        BuildStatus.Text = "";
                        BuildStatus.Refresh();
                        BuildProgress.Value = 0;
                        return;
                    }
                }
            }
            if (_systemType == "gcn")
            {
                long gamesize = new FileInfo(OpenGame.FileName).Length;
                var drive = new DriveInfo(TempRootPath);
                long freeSpaceInBytes = drive.AvailableFreeSpace;
                if (freeSpaceInBytes < gamesize * 2 + 6000000000)
                {
                    DialogResult dialogResult = MessageBox.Show("Your hard drive may be low on space. The conversion process involves temporary files" +
                                                                "that can amount to more than double the size of your game. If you continue without" +
                                                                "clearing some hard drive space, the conversion may fail. Do you want to continue anyway?",
                                                                "Check your hard drive space"
                                                                , MessageBoxButtons.YesNo
                                                                , MessageBoxIcon.Warning
                                                                , MessageBoxDefaultButton.Button1
                                                                , (MessageBoxOptions)0x40000);
                    if (dialogResult == DialogResult.No)
                    {
                        MainTabs.Enabled = true;
                        BuildStatus.Text = string.Empty;
                        BuildStatus.Refresh();
                        BuildProgress.Value = 0;
                        return;
                    }
                }
            }

            if (!string.IsNullOrEmpty(Properties.Settings.Default.OutputPathFixed))
            {
                _selectedOutputPath = Properties.Settings.Default.OutputPathFixed;
            }
            else
            {
                using (var outputFolderSelect = new FolderBrowserDialog())
                {
                    outputFolderSelect.Description = "Specify your output folder";
                    outputFolderSelect.SelectedPath = Properties.Settings.Default.OutputPath;
                    outputFolderSelect.ShowNewFolderButton = true;

                    if (outputFolderSelect.ShowDialog() == DialogResult.Cancel)
                    {
                        MessageBox.Show("Output folder selection has been cancelled, conversion will not continue.",
                            "Cancelled",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning,
                            MessageBoxDefaultButton.Button1,
                            (MessageBoxOptions)0x40000);
                        MainTabs.Enabled = true;
                        return;
                    }
                    _selectedOutputPath = outputFolderSelect.SelectedPath;
                    Properties.Settings.Default.OutputPath = _selectedOutputPath;
                    Properties.Settings.Default.Save();
                }
            }
            BuildProgress.Value = 2;
            //////////////////////////

            //Download base files with JNUSTool, store them for future use

            var downloadedFiles = new[]
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

            var fileHashes = new string[]
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

            var filesToDownload = new string[]
            {
                "0005001010004000 -file /code/deint.txt",
                "0005001010004000 -file /code/font.bin",
                "0005001010004001 -file /code/c2w.img",
                "0005001010004001 -file /code/boot.bin",
                "0005001010004001 -file /code/dmcu.d.hex",
                "00050000101b0700 " + TitleKey.Text + " -file /code/cos.xml",
                "00050000101b0700 " + TitleKey.Text + " -file /code/frisbiiU.rpx",
                "00050000101b0700 " + TitleKey.Text + " -file /code/fw.img",
                "00050000101b0700 " + TitleKey.Text + " -file /code/fw.tmd",
                "00050000101b0700 " + TitleKey.Text + " -file /code/htk.bin",
                "00050000101b0700 " + TitleKey.Text + " -file /code/nn_hai_user.rpl",
                "00050000101b0700 " + TitleKey.Text + " -file /content/assets/shaders/cafe/banner.gsh",
                "00050000101b0700 " + TitleKey.Text + " -file /content/assets/shaders/cafe/fade.gsh*",
                "00050000101b0700 " + TitleKey.Text + " -file /meta/bootMovie.h264",
                "00050000101b0700 " + TitleKey.Text + " -file /meta/bootLogoTex.tga",
                "00050000101b0700 " + TitleKey.Text + " -file /meta/bootSound.btsnd"
            };

            BuildStatus.Text = "Checking if the necessary files are present...";
            BuildStatus.Refresh();
            BuildProgress.Value = 10;

            // create config file for jnustool
            string[] jnusToolConfig = { "http://ccs.cdn.wup.shop.nintendo.net/ccs/download", WiiUCommonKey.Text };
            string jnusConfigPath = Path.Combine(TempToolsPath, "JAR", "config");
            File.WriteAllLines(jnusConfigPath, jnusToolConfig);
            Directory.SetCurrentDirectory(Path.Combine(TempToolsPath, "JAR"));

            bool internetPresent = Program.CheckForInternetConnection();

            for (int i = 0; i < downloadedFiles.Length; i++)
            {
                // check if file exists and is correct
                if (File.Exists(downloadedFiles[i]) && GetMD5Checksum(downloadedFiles[i]) == fileHashes[i])
                {
                    continue;
                }

                if (!internetPresent)
                {
                    DialogResult dialogResult = MessageBox.Show("Your internet connection could not be verified, do you wish to try and download the necessary base files from Nintendo anyway? (This is a one-time download)",
                        "Internet Connection Verification Failed",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button1,
                        (MessageBoxOptions)0x40000);
                    if (dialogResult == DialogResult.No)
                    {
                        MainTabs.Enabled = true;
                        BuildStatus.Text = "";
                        BuildStatus.Refresh();
                        BuildProgress.Value = 0;
                        return;
                    }
                }

                // if not, download it
                BuildStatus.Text = "(One-Time Download) Downloading base files from Nintendo...";
                BuildStatus.Refresh();
                LaunchProgram("JNUSTool.exe", filesToDownload[i], true);
                BuildProgress.Value += 2;

            }

            // if any files were downloaded, store them in ProgramData
            if (BuildProgress.Value > 10)
            {
                BuildStatus.Text = "Saving files from Nintendo for future use...";
                BuildStatus.Refresh();

                if (Directory.Exists("Rhythm Heaven Fever [VAKE01]"))
                {
                    FileUtil.CopyDirectory("Rhythm Heaven Fever [VAKE01]", JNUSToolDownloads + "Rhythm Heaven Fever [VAKE01]");
                    Directory.Delete("Rhythm Heaven Fever [VAKE01]", true);
                }
                if (Directory.Exists("0005001010004000"))
                {
                    FileUtil.CopyDirectory("0005001010004000", JNUSToolDownloads + "0005001010004000");
                    Directory.Delete("0005001010004000", true);
                }
                if (Directory.Exists("0005001010004001"))
                {
                    FileUtil.CopyDirectory("0005001010004001", JNUSToolDownloads + "0005001010004001");
                    Directory.Delete("0005001010004001", true);
                }

                // repeat loop to check if all files were downloaded properly
                bool JNUSFail = false;
                for (int i = 0; i < downloadedFiles.Length; i++)
                {
                    // check if file exists and is correct
                    if (File.Exists(downloadedFiles[i]) && GetMD5Checksum(downloadedFiles[i]) == fileHashes[i])
                    {
                        continue;
                    }
                    JNUSFail = true;
                }

                if (JNUSFail)
                {
                    MessageBox.Show("Failed to download base files using JNUSTool, conversion will not continue",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error,
                        MessageBoxDefaultButton.Button1,
                        (MessageBoxOptions)0x40000);
                    MainTabs.Enabled = true;
                    BuildStatus.Text = "";
                    BuildProgress.Value = 0;
                    return;
                }
            }
            File.Delete("config");
            Directory.SetCurrentDirectory(TempRootPath);
            ///////////////////////////////////

            //Copy downloaded files to the build directory
            BuildStatus.Text = "Copying base files to temporary build directory...";
            BuildStatus.Refresh();
            FileUtil.CopyDirectory(JNUSToolDownloads + "Rhythm Heaven Fever [VAKE01]", TempBuildPath);
            if (C2WPatchFlag.Checked)
            {
                FileUtil.CopyDirectory(JNUSToolDownloads + "0005001010004000", TempBuildPath);
                FileUtil.CopyDirectory(JNUSToolDownloads + "0005001010004001", TempBuildPath);
                string[] AncastKeyCopy = { AncastKey.Text };
                File.WriteAllLines(Path.Combine(TempToolsPath, "C2W", "starbuck_key.txt"), AncastKeyCopy);
                File.Copy(Path.Combine(TempBuildPath, "code", "c2w.img"), Path.Combine(TempToolsPath, "C2W", "c2w.img"));
                Directory.SetCurrentDirectory(Path.Combine(TempToolsPath, "C2W"));
                LaunchProgram("c2w_patcher.exe", "-nc", true);
                File.Delete(Path.Combine(TempBuildPath, "code", "c2w.img"));
                File.Copy(Path.Combine(TempToolsPath, "C2W", "c2p.img"), Path.Combine(TempBuildPath, "code", "c2w.img"), true);
                File.Delete(Path.Combine(TempToolsPath, "C2W", "c2p.img"));
                File.Delete(Path.Combine(TempToolsPath, "C2W", "c2w.img"));
                File.Delete(Path.Combine(TempToolsPath, "C2W", "starbuck_key.txt"));
            }
            BuildProgress.Value = 50;
            //////////////////////////////////////////////

            //Generate app.xml & meta.xml
            BuildStatus.Text = "Generating app.xml and meta.xml";
            BuildStatus.Refresh();
            string[] AppXML = { "<?xml version=\"1.0\" encoding=\"utf-8\"?>", "<app type=\"complex\" access=\"777\">", "  <version type=\"unsignedInt\" length=\"4\">16</version>", "  <os_version type=\"hexBinary\" length=\"8\">000500101000400A</os_version>", "  <title_id type=\"hexBinary\" length=\"8\">" + PackedTitleIDLine.Text + "</title_id>", "  <title_version type=\"hexBinary\" length=\"2\">0000</title_version>", "  <sdk_version type=\"unsignedInt\" length=\"4\">21204</sdk_version>", "  <app_type type=\"hexBinary\" length=\"4\">8000002E</app_type>", "  <group_id type=\"hexBinary\" length=\"4\">" + _titleIdHex + "</group_id>", "  <os_mask type=\"hexBinary\" length=\"32\">0000000000000000000000000000000000000000000000000000000000000000</os_mask>", "  <common_id type=\"hexBinary\" length=\"8\">0000000000000000</common_id>", "</app>" };
            File.WriteAllLines(Path.Combine(TempBuildPath, "code", "app.xml"), AppXML);
            if (EnablePackedLine2.Checked)
            {
                string[] MetaXML = { "<?xml version=\"1.0\" encoding=\"utf-8\"?>", "<menu type=\"complex\" access=\"777\">", "  <version type=\"unsignedInt\" length=\"4\">33</version>", "  <product_code type=\"string\" length=\"32\">WUP-N-" + _titleIdText + "</product_code>", "  <content_platform type=\"string\" length=\"32\">WUP</content_platform>", "  <company_code type=\"string\" length=\"8\">0001</company_code>", "  <mastering_date type=\"string\" length=\"32\"></mastering_date>", "  <logo_type type=\"unsignedInt\" length=\"4\">0</logo_type>", "  <app_launch_type type=\"hexBinary\" length=\"4\">00000000</app_launch_type>", "  <invisible_flag type=\"hexBinary\" length=\"4\">00000000</invisible_flag>", "  <no_managed_flag type=\"hexBinary\" length=\"4\">00000000</no_managed_flag>", "  <no_event_log type=\"hexBinary\" length=\"4\">00000002</no_event_log>", "  <no_icon_database type=\"hexBinary\" length=\"4\">00000000</no_icon_database>", "  <launching_flag type=\"hexBinary\" length=\"4\">00000004</launching_flag>", "  <install_flag type=\"hexBinary\" length=\"4\">00000000</install_flag>", "  <closing_msg type=\"unsignedInt\" length=\"4\">0</closing_msg>", "  <title_version type=\"unsignedInt\" length=\"4\">0</title_version>", "  <title_id type=\"hexBinary\" length=\"8\">" + PackedTitleIDLine.Text + "</title_id>", "  <group_id type=\"hexBinary\" length=\"4\">" + _titleIdHex + "</group_id>", "  <boss_id type=\"hexBinary\" length=\"8\">0000000000000000</boss_id>", "  <os_version type=\"hexBinary\" length=\"8\">000500101000400A</os_version>", "  <app_size type=\"hexBinary\" length=\"8\">0000000000000000</app_size>", "  <common_save_size type=\"hexBinary\" length=\"8\">0000000000000000</common_save_size>", "  <account_save_size type=\"hexBinary\" length=\"8\">0000000000000000</account_save_size>", "  <common_boss_size type=\"hexBinary\" length=\"8\">0000000000000000</common_boss_size>", "  <account_boss_size type=\"hexBinary\" length=\"8\">0000000000000000</account_boss_size>", "  <save_no_rollback type=\"unsignedInt\" length=\"4\">0</save_no_rollback>", "  <join_game_id type=\"hexBinary\" length=\"4\">00000000</join_game_id>", "  <join_game_mode_mask type=\"hexBinary\" length=\"8\">0000000000000000</join_game_mode_mask>", "  <bg_daemon_enable type=\"unsignedInt\" length=\"4\">0</bg_daemon_enable>", "  <olv_accesskey type=\"unsignedInt\" length=\"4\">3921400692</olv_accesskey>", "  <wood_tin type=\"unsignedInt\" length=\"4\">0</wood_tin>", "  <e_manual type=\"unsignedInt\" length=\"4\">0</e_manual>", "  <e_manual_version type=\"unsignedInt\" length=\"4\">0</e_manual_version>", "  <region type=\"hexBinary\" length=\"4\">00000002</region>", "  <pc_cero type=\"unsignedInt\" length=\"4\">128</pc_cero>", "  <pc_esrb type=\"unsignedInt\" length=\"4\">6</pc_esrb>", "  <pc_bbfc type=\"unsignedInt\" length=\"4\">192</pc_bbfc>", "  <pc_usk type=\"unsignedInt\" length=\"4\">128</pc_usk>", "  <pc_pegi_gen type=\"unsignedInt\" length=\"4\">128</pc_pegi_gen>", "  <pc_pegi_fin type=\"unsignedInt\" length=\"4\">192</pc_pegi_fin>", "  <pc_pegi_prt type=\"unsignedInt\" length=\"4\">128</pc_pegi_prt>", "  <pc_pegi_bbfc type=\"unsignedInt\" length=\"4\">128</pc_pegi_bbfc>", "  <pc_cob type=\"unsignedInt\" length=\"4\">128</pc_cob>", "  <pc_grb type=\"unsignedInt\" length=\"4\">128</pc_grb>", "  <pc_cgsrr type=\"unsignedInt\" length=\"4\">128</pc_cgsrr>", "  <pc_oflc type=\"unsignedInt\" length=\"4\">128</pc_oflc>", "  <pc_reserved0 type=\"unsignedInt\" length=\"4\">192</pc_reserved0>", "  <pc_reserved1 type=\"unsignedInt\" length=\"4\">192</pc_reserved1>", "  <pc_reserved2 type=\"unsignedInt\" length=\"4\">192</pc_reserved2>", "  <pc_reserved3 type=\"unsignedInt\" length=\"4\">192</pc_reserved3>", "  <ext_dev_nunchaku type=\"unsignedInt\" length=\"4\">0</ext_dev_nunchaku>", "  <ext_dev_classic type=\"unsignedInt\" length=\"4\">0</ext_dev_classic>", "  <ext_dev_urcc type=\"unsignedInt\" length=\"4\">0</ext_dev_urcc>", "  <ext_dev_board type=\"unsignedInt\" length=\"4\">0</ext_dev_board>", "  <ext_dev_usb_keyboard type=\"unsignedInt\" length=\"4\">0</ext_dev_usb_keyboard>", "  <ext_dev_etc type=\"unsignedInt\" length=\"4\">0</ext_dev_etc>", "  <ext_dev_etc_name type=\"string\" length=\"512\"></ext_dev_etc_name>", "  <eula_version type=\"unsignedInt\" length=\"4\">0</eula_version>", "  <drc_use type=\"unsignedInt\" length=\"4\">" + _drcuse + "</drc_use>", "  <network_use type=\"unsignedInt\" length=\"4\">0</network_use>", "  <online_account_use type=\"unsignedInt\" length=\"4\">0</online_account_use>", "  <direct_boot type=\"unsignedInt\" length=\"4\">0</direct_boot>", "  <reserved_flag0 type=\"hexBinary\" length=\"4\">00010001</reserved_flag0>", "  <reserved_flag1 type=\"hexBinary\" length=\"4\">00080023</reserved_flag1>", "  <reserved_flag2 type=\"hexBinary\" length=\"4\">" + _titleIdHex + "</reserved_flag2>", "  <reserved_flag3 type=\"hexBinary\" length=\"4\">00000000</reserved_flag3>", "  <reserved_flag4 type=\"hexBinary\" length=\"4\">00000000</reserved_flag4>", "  <reserved_flag5 type=\"hexBinary\" length=\"4\">00000000</reserved_flag5>", "  <reserved_flag6 type=\"hexBinary\" length=\"4\">00000003</reserved_flag6>", "  <reserved_flag7 type=\"hexBinary\" length=\"4\">00000005</reserved_flag7>", "  <longname_ja type=\"string\" length=\"512\">" + PackedTitleLine1.Text, PackedTitleLine2.Text + "</longname_ja>", "  <longname_en type=\"string\" length=\"512\">" + PackedTitleLine1.Text, PackedTitleLine2.Text + "</longname_en>", "  <longname_fr type=\"string\" length=\"512\">" + PackedTitleLine1.Text, PackedTitleLine2.Text + "</longname_fr>", "  <longname_de type=\"string\" length=\"512\">" + PackedTitleLine1.Text, PackedTitleLine2.Text + "</longname_de>", "  <longname_it type=\"string\" length=\"512\">" + PackedTitleLine1.Text, PackedTitleLine2.Text + "</longname_it>", "  <longname_es type=\"string\" length=\"512\">" + PackedTitleLine1.Text, PackedTitleLine2.Text + "</longname_es>", "  <longname_zhs type=\"string\" length=\"512\">" + PackedTitleLine1.Text, PackedTitleLine2.Text + "</longname_zhs>", "  <longname_ko type=\"string\" length=\"512\">" + PackedTitleLine1.Text, PackedTitleLine2.Text + "</longname_ko>", "  <longname_nl type=\"string\" length=\"512\">" + PackedTitleLine1.Text, PackedTitleLine2.Text + "</longname_nl>", "  <longname_pt type=\"string\" length=\"512\">" + PackedTitleLine1.Text, PackedTitleLine2.Text + "</longname_pt>", "  <longname_ru type=\"string\" length=\"512\">" + PackedTitleLine1.Text, PackedTitleLine2.Text + "</longname_ru>", "  <longname_zht type=\"string\" length=\"512\">" + PackedTitleLine1.Text, PackedTitleLine2.Text + "</longname_zht>", "  <shortname_ja type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_ja>", "  <shortname_en type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_en>", "  <shortname_fr type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_fr>", "  <shortname_de type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_de>", "  <shortname_it type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_it>", "  <shortname_es type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_es>", "  <shortname_zhs type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_zhs>", "  <shortname_ko type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_ko>", "  <shortname_nl type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_nl>", "  <shortname_pt type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_pt>", "  <shortname_ru type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_ru>", "  <shortname_zht type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_zht>", "  <publisher_ja type=\"string\" length=\"256\"></publisher_ja>", "  <publisher_en type=\"string\" length=\"256\"></publisher_en>", "  <publisher_fr type=\"string\" length=\"256\"></publisher_fr>", "  <publisher_de type=\"string\" length=\"256\"></publisher_de>", "  <publisher_it type=\"string\" length=\"256\"></publisher_it>", "  <publisher_es type=\"string\" length=\"256\"></publisher_es>", "  <publisher_zhs type=\"string\" length=\"256\"></publisher_zhs>", "  <publisher_ko type=\"string\" length=\"256\"></publisher_ko>", "  <publisher_nl type=\"string\" length=\"256\"></publisher_nl>", "  <publisher_pt type=\"string\" length=\"256\"></publisher_pt>", "  <publisher_ru type=\"string\" length=\"256\"></publisher_ru>", "  <publisher_zht type=\"string\" length=\"256\"></publisher_zht>", "  <add_on_unique_id0 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id0>", "  <add_on_unique_id1 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id1>", "  <add_on_unique_id2 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id2>", "  <add_on_unique_id3 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id3>", "  <add_on_unique_id4 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id4>", "  <add_on_unique_id5 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id5>", "  <add_on_unique_id6 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id6>", "  <add_on_unique_id7 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id7>", "  <add_on_unique_id8 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id8>", "  <add_on_unique_id9 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id9>", "  <add_on_unique_id10 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id10>", "  <add_on_unique_id11 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id11>", "  <add_on_unique_id12 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id12>", "  <add_on_unique_id13 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id13>", "  <add_on_unique_id14 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id14>", "  <add_on_unique_id15 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id15>", "  <add_on_unique_id16 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id16>", "  <add_on_unique_id17 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id17>", "  <add_on_unique_id18 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id18>", "  <add_on_unique_id19 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id19>", "  <add_on_unique_id20 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id20>", "  <add_on_unique_id21 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id21>", "  <add_on_unique_id22 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id22>", "  <add_on_unique_id23 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id23>", "  <add_on_unique_id24 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id24>", "  <add_on_unique_id25 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id25>", "  <add_on_unique_id26 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id26>", "  <add_on_unique_id27 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id27>", "  <add_on_unique_id28 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id28>", "  <add_on_unique_id29 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id29>", "  <add_on_unique_id30 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id30>", "  <add_on_unique_id31 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id31>", "</menu>" };
                File.WriteAllLines(Path.Combine(TempBuildPath, "meta", "meta.xml"), MetaXML);
            }
            else
            {
                string[] MetaXML = { "<?xml version=\"1.0\" encoding=\"utf-8\"?>", "<menu type=\"complex\" access=\"777\">", "  <version type=\"unsignedInt\" length=\"4\">33</version>", "  <product_code type=\"string\" length=\"32\">WUP-N-" + _titleIdText + "</product_code>", "  <content_platform type=\"string\" length=\"32\">WUP</content_platform>", "  <company_code type=\"string\" length=\"8\">0001</company_code>", "  <mastering_date type=\"string\" length=\"32\"></mastering_date>", "  <logo_type type=\"unsignedInt\" length=\"4\">0</logo_type>", "  <app_launch_type type=\"hexBinary\" length=\"4\">00000000</app_launch_type>", "  <invisible_flag type=\"hexBinary\" length=\"4\">00000000</invisible_flag>", "  <no_managed_flag type=\"hexBinary\" length=\"4\">00000000</no_managed_flag>", "  <no_event_log type=\"hexBinary\" length=\"4\">00000002</no_event_log>", "  <no_icon_database type=\"hexBinary\" length=\"4\">00000000</no_icon_database>", "  <launching_flag type=\"hexBinary\" length=\"4\">00000004</launching_flag>", "  <install_flag type=\"hexBinary\" length=\"4\">00000000</install_flag>", "  <closing_msg type=\"unsignedInt\" length=\"4\">0</closing_msg>", "  <title_version type=\"unsignedInt\" length=\"4\">0</title_version>", "  <title_id type=\"hexBinary\" length=\"8\">" + PackedTitleIDLine.Text + "</title_id>", "  <group_id type=\"hexBinary\" length=\"4\">" + _titleIdHex + "</group_id>", "  <boss_id type=\"hexBinary\" length=\"8\">0000000000000000</boss_id>", "  <os_version type=\"hexBinary\" length=\"8\">000500101000400A</os_version>", "  <app_size type=\"hexBinary\" length=\"8\">0000000000000000</app_size>", "  <common_save_size type=\"hexBinary\" length=\"8\">0000000000000000</common_save_size>", "  <account_save_size type=\"hexBinary\" length=\"8\">0000000000000000</account_save_size>", "  <common_boss_size type=\"hexBinary\" length=\"8\">0000000000000000</common_boss_size>", "  <account_boss_size type=\"hexBinary\" length=\"8\">0000000000000000</account_boss_size>", "  <save_no_rollback type=\"unsignedInt\" length=\"4\">0</save_no_rollback>", "  <join_game_id type=\"hexBinary\" length=\"4\">00000000</join_game_id>", "  <join_game_mode_mask type=\"hexBinary\" length=\"8\">0000000000000000</join_game_mode_mask>", "  <bg_daemon_enable type=\"unsignedInt\" length=\"4\">0</bg_daemon_enable>", "  <olv_accesskey type=\"unsignedInt\" length=\"4\">3921400692</olv_accesskey>", "  <wood_tin type=\"unsignedInt\" length=\"4\">0</wood_tin>", "  <e_manual type=\"unsignedInt\" length=\"4\">0</e_manual>", "  <e_manual_version type=\"unsignedInt\" length=\"4\">0</e_manual_version>", "  <region type=\"hexBinary\" length=\"4\">00000002</region>", "  <pc_cero type=\"unsignedInt\" length=\"4\">128</pc_cero>", "  <pc_esrb type=\"unsignedInt\" length=\"4\">6</pc_esrb>", "  <pc_bbfc type=\"unsignedInt\" length=\"4\">192</pc_bbfc>", "  <pc_usk type=\"unsignedInt\" length=\"4\">128</pc_usk>", "  <pc_pegi_gen type=\"unsignedInt\" length=\"4\">128</pc_pegi_gen>", "  <pc_pegi_fin type=\"unsignedInt\" length=\"4\">192</pc_pegi_fin>", "  <pc_pegi_prt type=\"unsignedInt\" length=\"4\">128</pc_pegi_prt>", "  <pc_pegi_bbfc type=\"unsignedInt\" length=\"4\">128</pc_pegi_bbfc>", "  <pc_cob type=\"unsignedInt\" length=\"4\">128</pc_cob>", "  <pc_grb type=\"unsignedInt\" length=\"4\">128</pc_grb>", "  <pc_cgsrr type=\"unsignedInt\" length=\"4\">128</pc_cgsrr>", "  <pc_oflc type=\"unsignedInt\" length=\"4\">128</pc_oflc>", "  <pc_reserved0 type=\"unsignedInt\" length=\"4\">192</pc_reserved0>", "  <pc_reserved1 type=\"unsignedInt\" length=\"4\">192</pc_reserved1>", "  <pc_reserved2 type=\"unsignedInt\" length=\"4\">192</pc_reserved2>", "  <pc_reserved3 type=\"unsignedInt\" length=\"4\">192</pc_reserved3>", "  <ext_dev_nunchaku type=\"unsignedInt\" length=\"4\">0</ext_dev_nunchaku>", "  <ext_dev_classic type=\"unsignedInt\" length=\"4\">0</ext_dev_classic>", "  <ext_dev_urcc type=\"unsignedInt\" length=\"4\">0</ext_dev_urcc>", "  <ext_dev_board type=\"unsignedInt\" length=\"4\">0</ext_dev_board>", "  <ext_dev_usb_keyboard type=\"unsignedInt\" length=\"4\">0</ext_dev_usb_keyboard>", "  <ext_dev_etc type=\"unsignedInt\" length=\"4\">0</ext_dev_etc>", "  <ext_dev_etc_name type=\"string\" length=\"512\"></ext_dev_etc_name>", "  <eula_version type=\"unsignedInt\" length=\"4\">0</eula_version>", "  <drc_use type=\"unsignedInt\" length=\"4\">" + _drcuse + "</drc_use>", "  <network_use type=\"unsignedInt\" length=\"4\">0</network_use>", "  <online_account_use type=\"unsignedInt\" length=\"4\">0</online_account_use>", "  <direct_boot type=\"unsignedInt\" length=\"4\">0</direct_boot>", "  <reserved_flag0 type=\"hexBinary\" length=\"4\">00010001</reserved_flag0>", "  <reserved_flag1 type=\"hexBinary\" length=\"4\">00080023</reserved_flag1>", "  <reserved_flag2 type=\"hexBinary\" length=\"4\">" + _titleIdHex + "</reserved_flag2>", "  <reserved_flag3 type=\"hexBinary\" length=\"4\">00000000</reserved_flag3>", "  <reserved_flag4 type=\"hexBinary\" length=\"4\">00000000</reserved_flag4>", "  <reserved_flag5 type=\"hexBinary\" length=\"4\">00000000</reserved_flag5>", "  <reserved_flag6 type=\"hexBinary\" length=\"4\">00000003</reserved_flag6>", "  <reserved_flag7 type=\"hexBinary\" length=\"4\">00000005</reserved_flag7>", "  <longname_ja type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</longname_ja>", "  <longname_en type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</longname_en>", "  <longname_fr type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</longname_fr>", "  <longname_de type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</longname_de>", "  <longname_it type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</longname_it>", "  <longname_es type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</longname_es>", "  <longname_zhs type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</longname_zhs>", "  <longname_ko type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</longname_ko>", "  <longname_nl type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</longname_nl>", "  <longname_pt type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</longname_pt>", "  <longname_ru type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</longname_ru>", "  <longname_zht type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</longname_zht>", "  <shortname_ja type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_ja>", "  <shortname_en type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_en>", "  <shortname_fr type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_fr>", "  <shortname_de type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_de>", "  <shortname_it type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_it>", "  <shortname_es type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_es>", "  <shortname_zhs type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_zhs>", "  <shortname_ko type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_ko>", "  <shortname_nl type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_nl>", "  <shortname_pt type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_pt>", "  <shortname_ru type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_ru>", "  <shortname_zht type=\"string\" length=\"512\">" + PackedTitleLine1.Text + "</shortname_zht>", "  <publisher_ja type=\"string\" length=\"256\"></publisher_ja>", "  <publisher_en type=\"string\" length=\"256\"></publisher_en>", "  <publisher_fr type=\"string\" length=\"256\"></publisher_fr>", "  <publisher_de type=\"string\" length=\"256\"></publisher_de>", "  <publisher_it type=\"string\" length=\"256\"></publisher_it>", "  <publisher_es type=\"string\" length=\"256\"></publisher_es>", "  <publisher_zhs type=\"string\" length=\"256\"></publisher_zhs>", "  <publisher_ko type=\"string\" length=\"256\"></publisher_ko>", "  <publisher_nl type=\"string\" length=\"256\"></publisher_nl>", "  <publisher_pt type=\"string\" length=\"256\"></publisher_pt>", "  <publisher_ru type=\"string\" length=\"256\"></publisher_ru>", "  <publisher_zht type=\"string\" length=\"256\"></publisher_zht>", "  <add_on_unique_id0 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id0>", "  <add_on_unique_id1 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id1>", "  <add_on_unique_id2 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id2>", "  <add_on_unique_id3 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id3>", "  <add_on_unique_id4 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id4>", "  <add_on_unique_id5 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id5>", "  <add_on_unique_id6 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id6>", "  <add_on_unique_id7 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id7>", "  <add_on_unique_id8 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id8>", "  <add_on_unique_id9 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id9>", "  <add_on_unique_id10 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id10>", "  <add_on_unique_id11 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id11>", "  <add_on_unique_id12 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id12>", "  <add_on_unique_id13 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id13>", "  <add_on_unique_id14 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id14>", "  <add_on_unique_id15 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id15>", "  <add_on_unique_id16 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id16>", "  <add_on_unique_id17 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id17>", "  <add_on_unique_id18 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id18>", "  <add_on_unique_id19 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id19>", "  <add_on_unique_id20 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id20>", "  <add_on_unique_id21 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id21>", "  <add_on_unique_id22 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id22>", "  <add_on_unique_id23 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id23>", "  <add_on_unique_id24 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id24>", "  <add_on_unique_id25 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id25>", "  <add_on_unique_id26 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id26>", "  <add_on_unique_id27 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id27>", "  <add_on_unique_id28 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id28>", "  <add_on_unique_id29 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id29>", "  <add_on_unique_id30 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id30>", "  <add_on_unique_id31 type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id31>", "</menu>" };
                File.WriteAllLines(Path.Combine(TempBuildPath, "meta", "meta.xml"), MetaXML);
            }
            BuildProgress.Value = 52;
            /////////////////////////////

            //Convert PNG files to TGA
            BuildStatus.Text = "Converting all image sources to expected TGA specification...";
            BuildStatus.Refresh();
            using (Image img = Image.FromFile(TempIconPath))
            {
                TgaReader.SaveAsTga(img, Path.Combine(TempBuildPath, "meta", "iconTex.tga"), 128, 128, 32);
            }
            using (Image img = Image.FromFile(TempBannerPath))
            {
                TgaReader.SaveAsTga(img, Path.Combine(TempBuildPath, "meta", "bootTvTex.tga"), 1280, 720, 24);
            }
            if (_flagDrcSpecified == false)
            {
                File.Copy(TempBannerPath, TempDrcPath);
            }
            using (Image img = Image.FromFile(TempDrcPath))
            {
                TgaReader.SaveAsTga(img, Path.Combine(TempBuildPath, "meta", "bootDrcTex.tga"), 854, 480, 24);
            }
            if (_flagLogoSpecified)
            {
                using (Image img = Image.FromFile(TempLogoPath))
                {
                    TgaReader.SaveAsTga(img, Path.Combine(TempBuildPath, "meta", "bootLogoTex.tga"), 170, 42, 32);
                }
            }
            if (_flagDrcSpecified == false) { File.Delete(TempDrcPath); }
            BuildProgress.Value = 55;
            //////////////////////////

            //Convert Boot Sound if provided by user
            if (_flagBootSoundSpecified)
            {
                BuildStatus.Text = "Converting user-provided sound to btsnd format...";
                BuildStatus.Refresh();
                LaunchProgram(Path.Combine(TempToolsPath, "SOX", "sox.exe"), "\"" + OpenBootSound.FileName + "\" -b 16 \"" + TempSoundPath + "\" channels 2 rate 48k trim 0 6", true);
                File.Delete(Path.Combine(TempBuildPath, "meta", "bootSound.btsnd"));
                LaunchProgram(Path.Combine(TempToolsPath, "JAR", "wav2btsnd.exe"), "-in \"" + TempSoundPath + "\" -out \"" + Path.Combine(TempBuildPath, "meta", "bootSound.btsnd") + "\"" + _loopString, true);
                File.Delete(TempSoundPath);
            }
            BuildProgress.Value = 60;
            ////////////////////////////////////////

            //Build ISO based on type and user specification
            BuildStatus.Text = "Processing game for NFS Conversion...";
            BuildStatus.Refresh();
            if (OpenGame.FileName != null) { _ogFilePath = OpenGame.FileName; }
            if (_systemType == "wii")
            {
                if (_flagWbfs)
                {
                    LaunchProgram(Path.Combine(TempToolsPath, "EXE", "wbfs_file.exe"), "\"" + OpenGame.FileName + "\" convert \"" + Path.Combine(TempSourcePath, "wbfsconvert.iso") + "\"", true);
                    OpenGame.FileName = Path.Combine(TempSourcePath, "wbfsconvert.iso");
                }
                if (_flagNkit || _flagNasos)
                {
                    if (Directory.Exists(Path.Combine(TempToolsPath, "NKIT", "Processed")))
                    {
                        Directory.Delete(Path.Combine(TempToolsPath, "NKIT", "Processed"), true);
                    }
                    BuildStatus.Text = "Unscrubbing game for NFS Conversion...";
                    BuildStatus.Refresh();
                    LaunchProgram(Path.Combine(TempToolsPath, "NKIT", "ConvertToISO.exe"), "\"" + OpenGame.FileName + "\"", true);
                    OpenGame.FileName = Path.Combine(TempSourcePath, "game.iso");
                    if (_flagNkit)
                        File.Move(Directory.GetFiles(Path.Combine(TempToolsPath, "NKIT", "Processed", "Temp"), "*.tmp")[0], OpenGame.FileName);
                    else
                        File.Move(Directory.GetFiles(Path.Combine(TempToolsPath, "NKIT", "Processed", "Wii_MatchFail"), "*.iso")[0], OpenGame.FileName);

                }
                if (DisableTrimming.Checked == false)
                {
                    BuildStatus.Text = "Extracting game for NFS Conversion...";
                    BuildStatus.Refresh();
                    LaunchProgram(Path.Combine(TempToolsPath, "WIT", "wit.exe"), "extract " + "\"" + OpenGame.FileName + "\"" + " --DEST " + Path.Combine(TempSourcePath, "ISOEXTRACT") + " --psel data,-update -ovv", true); // EXTRACT WII ISO
                    if (ForceCC.Checked)
                    {
                        LaunchProgram(Path.Combine(TempToolsPath, "EXE", "GetExtTypePatcher.exe"), "\"" + Path.Combine(TempSourcePath, "ISOEXTRACT", "sys", "main.dol") + "\" -nc", true);
                    }
                    if (WiiVMC.Checked)
                    {
                        MessageBox.Show("The Wii Video Mode Changer will now be launched. I recommend using the Smart Patcher option. \n\n" +
                                        "If you're scared and don't know what you're doing, close the patcher window and nothing will be patched." +
                                        "\n\nClick OK to continue..."
                                        , "Information"
                                        , MessageBoxButtons.OK
                                        , MessageBoxIcon.Information
                                        , MessageBoxDefaultButton.Button1
                                        , (MessageBoxOptions)0x40000);
                        LaunchProgram(Path.Combine(TempToolsPath, "EXE", "wii-vmc.exe"), "\"" + Path.Combine(TempSourcePath, "ISOEXTRACT", "sys", "main.dol") + "\"", false);
                        MessageBox.Show("Conversion will now continue..."
                                        , "Information"
                                        , MessageBoxButtons.OK
                                        , MessageBoxIcon.Information
                                        , MessageBoxDefaultButton.Button1
                                        , (MessageBoxOptions)0x40000);
                    }
                    BuildStatus.Text = "Rebuilding iso for NFS Conversion...";
                    BuildStatus.Refresh();
                    if (!Wiimmfi.Checked)
                    {
                        _wiimmfiOption = "";
                    }
                    LaunchProgram(Path.Combine(TempToolsPath, "WIT", "wit.exe"), "copy " + Path.Combine(TempSourcePath, "ISOEXTRACT") + " --DEST " + Path.Combine(TempSourcePath, "game.iso") + " -ovv --links --iso" + _wiimmfiOption, true); // REBUILD WII ISO
                    if (File.Exists(Path.Combine(TempSourcePath, "wbfsconvert.iso"))) { File.Delete(Path.Combine(TempSourcePath, "wbfsconvert.iso")); }
                    OpenGame.FileName = Path.Combine(TempSourcePath, "game.iso");
                }
            }
            if (_systemType == "dol")
            {
                Directory.CreateDirectory(Path.Combine(TempSourcePath, "TEMPISOBASE"));
                FileUtil.CopyDirectory(Path.Combine(TempToolsPath, "BASE"), Path.Combine(TempSourcePath, "TEMPISOBASE"));
                File.Copy(OpenGame.FileName, Path.Combine(TempSourcePath, "TEMPISOBASE", "sys", "main.dol"));
                LaunchProgram(Path.Combine(TempToolsPath, "WIT", "wit.exe"), "copy " + Path.Combine(TempSourcePath, "TEMPISOBASE") + " --DEST " + Path.Combine(TempSourcePath, "game.iso") + " -ovv --links --iso", true);
                Directory.Delete(Path.Combine(TempSourcePath, "TEMPISOBASE"), true);
                OpenGame.FileName = Path.Combine(TempSourcePath, "game.iso");
            }
            if (_systemType == "wiiware")
            {
                Directory.CreateDirectory(Path.Combine(TempSourcePath, "TEMPISOBASE"));
                FileUtil.CopyDirectory(Path.Combine(TempToolsPath, "BASE"), Path.Combine(TempSourcePath, "TEMPISOBASE"));
                if (Force43NAND.Checked)
                {
                    File.Copy(Path.Combine(TempToolsPath, "DOL", "FIX94_wiivc_chan_booter_force43.dol"), Path.Combine(TempSourcePath, "TEMPISOBASE", "sys", "main.dol"));
                }
                else
                {
                    File.Copy(Path.Combine(TempToolsPath, "DOL", "FIX94_wiivc_chan_booter.dol"), Path.Combine(TempSourcePath, "TEMPISOBASE", "sys", "main.dol"));
                }
                string[] TitleTXT = { GameSourceDirectory.Text };
                File.WriteAllLines(Path.Combine(TempSourcePath, "TEMPISOBASE", "files", "title.txt"), TitleTXT);
                LaunchProgram(Path.Combine(TempToolsPath, "WIT", "wit.exe"), "copy " + Path.Combine(TempSourcePath, "TEMPISOBASE") + " --DEST " + Path.Combine(TempSourcePath, "game.iso") + " -ovv --links --iso", true);
                Directory.Delete(Path.Combine(TempSourcePath, "TEMPISOBASE"), true);
                OpenGame.FileName = Path.Combine(TempSourcePath, "game.iso");
            }
            if (_systemType == "gcn")
            {
                Directory.CreateDirectory(Path.Combine(TempSourcePath, "TEMPISOBASE"));
                FileUtil.CopyDirectory(Path.Combine(TempToolsPath, "BASE"), Path.Combine(TempSourcePath, "TEMPISOBASE"));
                if (Force43NINTENDONT.Checked)
                {
                    if (ForceInterlacedNINTENDONT.Checked)
                    {
                        File.Copy(Path.Combine(TempToolsPath, "DOL", "nintendont_force_43_interlaced_autobooter.dol"), Path.Combine(TempSourcePath, "TEMPISOBASE", "sys", "main.dol"));
                    }
                    else
                    {
                        File.Copy(Path.Combine(TempToolsPath, "DOL", "nintendont_force_4_by_3_autobooter.dol"), Path.Combine(TempSourcePath, "TEMPISOBASE", "sys", "main.dol"));
                    }
                }

                else if (ForceInterlacedNINTENDONT.Checked)
                {
                    File.Copy(Path.Combine(TempToolsPath, "DOL", "nintendont_force_interlaced_autobooter.dol"), Path.Combine(TempSourcePath, "TEMPISOBASE", "sys", "main.dol"));
                }
                else if (CustomMainDol.Checked)
                {
                    File.Copy(OpenMainDol.FileName, Path.Combine(TempSourcePath, "TEMPISOBASE", "sys", "main.dol"));
                }
                else if (DisableNintendontAutoboot.Checked)
                {
                    File.Copy(Path.Combine(TempToolsPath, "DOL", "nintendont_forwarder.dol"), Path.Combine(TempSourcePath, "TEMPISOBASE", "sys", "main.dol"));
                }
                else
                {
                    File.Copy(Path.Combine(TempToolsPath, "DOL", "nintendont_default_autobooter.dol"), Path.Combine(TempSourcePath, "TEMPISOBASE", "sys", "main.dol"));
                }

                if (_flagNkit)
                {
                    if (Directory.Exists(Path.Combine(TempToolsPath, "NKIT", "Processed", "Temp")))
                    {
                        Directory.Delete(Path.Combine(TempToolsPath, "NKIT", "Processed", "Temp"), true);
                    }
                    BuildStatus.Text = "Unscrubbing game for NFS Conversion...";
                    BuildStatus.Refresh();
                    LaunchProgram(Path.Combine(TempToolsPath, "NKIT", "ConvertToISO.exe"), "\"" + OpenGame.FileName, true); // CONVERT TO ISO
                    File.Move(Directory.GetFiles(Path.Combine(TempToolsPath, "NKIT", "Processed", "GameCube_MatchFail"), "*.iso")[0], Path.Combine(TempSourcePath, "TEMPISOBASE", "files", "game.iso"));
                }
                else
                {
                    File.Copy(OpenGame.FileName, Path.Combine(TempSourcePath, "TEMPISOBASE", "files", "game.iso"));
                }

                if (_flagGc2Specified)
                {
                    if (_flagNkit)
                    {
                        if (Directory.Exists(Path.Combine(TempToolsPath, "NKIT", "Processed", "Temp")))
                        {
                            Directory.Delete(Path.Combine(TempToolsPath, "NKIT", "Processed", "Temp"), true);
                        }
                        BuildStatus.Text = "Unscrubbing second disc for NFS Conversion...";
                        BuildStatus.Refresh();
                        LaunchProgram(Path.Combine(TempToolsPath, "NKIT", "ConvertToISO.exe"), "\"" + OpenGC2.FileName + "\"", true); // CONVERT DISC 2 TO ISO
                        File.Move(Directory.GetFiles(Path.Combine(TempToolsPath, "NKIT", "Processed", "GameCube_MatchFail"), "*.iso")[0], Path.Combine(TempSourcePath, "TEMPISOBASE", "files", "disc2.iso"));
                    }
                    else
                    {
                        File.Copy(OpenGC2.FileName, Path.Combine(TempSourcePath, "TEMPISOBASE", "files", "disc2.iso"));
                    }
                }
                LaunchProgram(Path.Combine(TempToolsPath, "WIT", "wit.exe"), "copy " + Path.Combine(TempSourcePath, "TEMPISOBASE") + " --DEST " + Path.Combine(TempSourcePath, "game.iso") + " -ovv --links --iso", true); // BUILD FINAL GAMECUBE ISO
                Directory.Delete(Path.Combine(TempSourcePath, "TEMPISOBASE"), true);
                OpenGame.FileName = Path.Combine(TempSourcePath, "game.iso");
            }
            LaunchProgram(Path.Combine(TempToolsPath, "WIT", "wit.exe"), "extract " + OpenGame.FileName + " --psel data --psel -update --files +tmd.bin --files +ticket.bin --dest " + Path.Combine(TempSourcePath, "TIKTEMP") + " -vv1", true);
            File.Copy(Path.Combine(TempSourcePath, "TIKTEMP", "tmd.bin"), Path.Combine(TempBuildPath, "code", "rvlt.tmd"));
            File.Copy(Path.Combine(TempSourcePath, "TIKTEMP", "ticket.bin"), Path.Combine(TempBuildPath, "code", "rvlt.tik"));
            Directory.Delete(Path.Combine(TempSourcePath, "TIKTEMP"), true);
            BuildProgress.Value = 70;
            ////////////////////////////////////////////////

            //Convert ISO to NFS format
            BuildStatus.Text = "Converting processed game to NFS format...";
            BuildStatus.Refresh();
            Directory.SetCurrentDirectory(Path.Combine(TempBuildPath, "content"));

            // Build arguments array for in-process Nfs2Iso2Nfs conversion
            List<string> nfsArgs = new List<string> { "-enc" };

            if (_systemType == "dol" || _systemType == "wiiware" || _systemType == "gcn")
            {
                nfsArgs.Add("-homebrew");
            }

            if (_systemType == "gcn")
            {
                nfsArgs.Add("-passthrough");
            }
            else if (_systemType == "dol")
            {
                if (_passPatch.Contains("-passthrough"))
                {
                    nfsArgs.Add("-passthrough");
                }
            }

            if (_nfsPatchFlag.Contains("-horizontal"))
            {
                nfsArgs.Add("-horizontal");
            }
            else if (_nfsPatchFlag.Contains("-wiimote"))
            {
                nfsArgs.Add("-wiimote");
            }
            else if (_nfsPatchFlag.Contains("-instantcc"))
            {
                nfsArgs.Add("-instantcc");
            }
            else if (_nfsPatchFlag.Contains("-nocc"))
            {
                nfsArgs.Add("-nocc");
            }

            if (LRPatch.Checked)
            {
                nfsArgs.Add("-lrpatch");
            }

            nfsArgs.Add("-iso");
            nfsArgs.Add(OpenGame.FileName);

            // Execute in-process conversion using native C# class instead of executing external nfs2iso2nfs.exe
            Nfs2Iso2Nfs.ConvertNfs(nfsArgs.ToArray());

            if (DisableTrimming.Checked == false)
            {
                File.Delete(OpenGame.FileName);
            }
            else if (_flagWbfs)
            {
                File.Delete(OpenGame.FileName);
            }
            BuildProgress.Value = 85;
            ///////////////////////////

            //Encrypt contents with NUSPacker
            BuildStatus.Text = "Encrypting contents into installable WUP Package...";
            BuildStatus.Refresh();
            Directory.SetCurrentDirectory(TempRootPath);
            string sanitizedGameName = SanitizeFilename(PackedTitleLine1.Text);
            string outputPath = Path.Combine(_selectedOutputPath, sanitizedGameName + " WUP-N-" + _titleIdText + "_" + PackedTitleIDLine.Text);
            LaunchProgram(Path.Combine(TempToolsPath, "JAR", "NUSPacker.exe"), "-in BUILDDIR -out \"" + outputPath + "\" -encryptKeyWith " + WiiUCommonKey.Text, true);
            BuildProgress.Value = 100;
            /////////////////////////////////

            //Delete Temp Directories
            Directory.SetCurrentDirectory(Application.StartupPath);
            DeleteFolder(TempBuildPath, true);
            DeleteFolder(Path.Combine(TempRootPath, "output"), true);
            DeleteFolder(Path.Combine(TempRootPath, "tmp"), true);
            Directory.CreateDirectory(TempBuildPath);
            /////////////////////////

            if (!Directory.Exists(outputPath))
            {
                MessageBox.Show("Conversion Failed! The output folder could not be created:\n" + outputPath + "\n\n" +
                                "Please make sure that Java (JRE) is installed on your computer, as it is required by NUSPacker to encrypt the WUP package.",
                                "Conversion Failed",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error,
                                MessageBoxDefaultButton.Button1,
                                (MessageBoxOptions)0x40000);
            }
            else
            {
                //END
                BuildStatus.Text = "Conversion complete...";
                BuildStatus.Refresh();

                DialogResult finalDialogResult = MessageBox.Show("Conversion Complete! Your packed game can be found here:\n" + outputPath + "\n\n" +
                                                                "Install your title using WUP Installer GX2 with signature patches enabled (CBHC, Haxchi, etc)." +
                                                                "Make sure you have signature patches enabled when launching your title.\n\n" +
                                                                "Open the output folder now?"
                                                                , PackedTitleLine1.Text + "Conversion Complete"
                                                                , MessageBoxButtons.YesNo
                                                                , MessageBoxIcon.Information
                                                                , MessageBoxDefaultButton.Button1
                                                                , (MessageBoxOptions)0x40000);

                if (finalDialogResult == DialogResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(outputPath) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Could not open the output folder:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }

            if (_ogFilePath != null) { OpenGame.FileName = _ogFilePath; }
            BuildStatus.Text = "";
            BuildStatus.Refresh();
            MainTabs.Enabled = true;
            MainTabs.SelectedTab = SourceFilesTab;
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

        private static void DeleteFolder(string path, bool recursive)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive);
            }
        }
    }
}
