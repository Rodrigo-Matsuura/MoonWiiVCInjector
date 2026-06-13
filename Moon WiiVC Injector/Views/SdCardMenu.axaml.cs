using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Collections.Generic;
using System.Net;
using Avalonia.Platform.Storage;

namespace Moon_WiiVC_Injector
{
    public partial class SdCardMenuAvalonia : Window
    {
        private string SelectedDriveLetter = string.Empty;
        private bool DriveSpecified = false;
        private List<CheckBox> optionChecks = new List<CheckBox>();

        public SdCardMenuAvalonia()
        {
            InitializeComponent();

            // wire events
            ReloadDrives.Click += ReloadDrives_Click;
            NintendontUpdate.Click += async (_, __) => await NintendontUpdate_Click();
            GenerateConfig.Click += async (_, __) => await GenerateConfig_Click();
            VideoWidth.PropertyChanged += (s, e) => { WidthNumber.Text = VideoWidth.Value.ToString(); };

            // populate options similar to original CheckedListBox
            string[] opts = new[] {
                "Memcard Emulation","Cheats","Cheat Path","Unlock Read Speed","Wiimote CC Rumble",
                "TRI Arcade Mode","BBA Emulation","Auto Video Width","Patch PAL50","Force Widescreen",
                "Force Progressive","Skip IPL","OSReport","Log" };
            foreach (var o in opts)
            {
                var cb = new CheckBox { Content = o };
                optionChecks.Add(cb);
                OptionsPanel.Children.Add(cb);
            }

            this.Opened += (_, __) => {
                ReloadDriveList();
                SpecifyDrive();
                MemcardBlocks.SelectedIndex = 0;
                VideoForceMode.SelectedIndex = 0;
                VideoTypeMode.SelectedIndex = 0;
                LanguageBox.SelectedIndex = 0;
                wiiUGamepadSlotBox.SelectedIndex = 0;
                optionChecks[0].IsChecked = true;
                optionChecks[7].IsChecked = true;
            };
        }

        private void SpecifyDrive()
        {
            if (DriveBox.SelectedItem is string s && !string.IsNullOrEmpty(s))
            {
                SelectedDriveLetter = s.Substring(0, 3);
                DriveSpecified = true;
            }
            else
            {
                SelectedDriveLetter = string.Empty;
                DriveSpecified = false;
            }
        }

        private class NintendontConfig
        {
            public uint magicBytes { get; set; } = 0x01070CF6u;
            public uint version { get; set; } = 10u;
            public uint config { get; set; } = 0u;
            public uint videoMode { get; set; } = 0u;
            public uint language { get; set; } = 0u;
            public byte[] gamePath { get; set; } = new byte[256];
            public byte[] cheatPath { get; set; } = new byte[256];
            public uint maxPads { get; set; } = 4u;
            public uint gameID { get; set; } = 0u;
            public byte memCardBlocks { get; set; }
            public sbyte videoScale { get; set; }
            public sbyte videoOffset { get; set; }
            public byte networkProfile { get; set; }
            public uint wiiuGamepadSlot { get; set; }
        }

        private void ReloadDriveList()
        {
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Removable)
                .Select(d => d.Name + " (" + d.VolumeLabel + ")").ToList();
            DriveBox.ItemsSource = drives;
            if (drives.Count > 0) DriveBox.SelectedIndex = 0;
        }

        private void ReloadDrives_Click(object? sender, RoutedEventArgs e)
        {
            ReloadDriveList();
            SpecifyDrive();
        }

        private async Task NintendontUpdate_Click()
        {
            string downloadPath = Path.Combine(Path.GetTempPath(), "Moon WiiVC Injector", "SOURCETEMP", "Download");
            string tempPath = Path.Combine(downloadPath, "apps", "nintendont");
            string sdPath = Path.Combine(SelectedDriveLetter, "apps", "nintendont");

            if (!Program.CheckForInternetConnection())
            {
                var res = await MessageBoxWindow.Show(this, "Your internet connection could not be verified, do you wish to try and download Nintendont anyway?",
                    "Internet Connection Verification Failed",
                    MessageBoxButtons.YesNo);
                if (res == MessageBoxResult.No) return;
            }

            ActionStatus.Text = "Downloading...";
            await Task.Run(async () =>
            {
                Directory.CreateDirectory(tempPath);
                var client = Program.Client;
                File.WriteAllBytes(Path.Combine(tempPath, "boot.dol"), await client.GetByteArrayAsync("https://raw.githubusercontent.com/FIX94/Nintendont/master/loader/loader.dol"));
                File.WriteAllBytes(Path.Combine(tempPath, "meta.xml"), await client.GetByteArrayAsync("https://raw.githubusercontent.com/FIX94/Nintendont/master/nintendont/meta.xml"));
                File.WriteAllBytes(Path.Combine(tempPath, "icon.png"), await client.GetByteArrayAsync("https://raw.githubusercontent.com/FIX94/Nintendont/master/nintendont/icon.png"));
            });
            ActionStatus.Text = string.Empty;

            if (DriveSpecified)
            {
                if (Directory.Exists(sdPath)) Directory.Delete(sdPath, true);
                Directory.CreateDirectory(sdPath);
                var dir = new DirectoryInfo(tempPath);
                foreach (var file in dir.GetFiles())
                {
                    var outPath = Path.Combine(sdPath, file.Name);
                    file.CopyTo(outPath, true);
                }

                await MessageBoxWindow.Show(this, "Download complete.", "Success", MessageBoxButtons.Ok);
            }
            else
            {
                var dialogResult = await MessageBoxWindow.Show(this, "SD Card not specified.\nDo you wish to save Nintendont somewhere else?",
                    "Drive not specified",
                    MessageBoxButtons.YesNo);
                if (dialogResult == MessageBoxResult.Yes)
                {
                    DateTime dateTime = DateTime.UtcNow.Date;
                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel != null)
                    {
                        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                        {
                            Title = "Save Nintendont zip file",
                            DefaultExtension = "zip",
                            SuggestedFileName = $"Nintendont-{dateTime:dd.MMM.yyyy}.zip",
                            FileTypeChoices = new[]
                            {
                                new FilePickerFileType("Zip Files") { Patterns = new[] { "*.zip" } }
                            }
                        });
                        if (file != null)
                        {
                            var zipPath = file.Path.LocalPath;
                            if (File.Exists(zipPath)) File.Delete(zipPath);
                            ZipFile.CreateFromDirectory(downloadPath, zipPath);
                            await MessageBoxWindow.Show(this, "Download complete.", "Success", MessageBoxButtons.Ok);
                        }
                    }
                }
            }
        }

        private async Task GenerateConfig_Click()
        {
            // Build config similar to original; only core fields implemented
            var nintendontCfg = new NintendontConfig
            {
                magicBytes = 0x01070CF6u,
                version = 10u,
                config = 0u,
                videoMode = 0u,
                language = 0u,
                gamePath = new byte[256],
                cheatPath = new byte[256],
                maxPads = 4u,
                gameID = 0u,
                memCardBlocks = (byte)MemcardBlocks.SelectedIndex,
                videoScale = (sbyte)0,
                videoOffset = (sbyte)0,
                networkProfile = (byte)0,
                wiiuGamepadSlot = (uint)wiiUGamepadSlotBox.SelectedIndex
            };

            // options
            if (optionChecks[0].IsChecked == true) nintendontCfg.config |= 1u << 3; // NIN_CFG_MEMCARDEMU
            if (optionChecks[1].IsChecked == true) nintendontCfg.config |= 1u; //NIN_CFG_CHEATS
            if (optionChecks[9].IsChecked == true) nintendontCfg.config |= 1u << 6; // FORCE_WIDE
            // video width handling
            if (optionChecks[7].IsChecked == true)
            {
                nintendontCfg.videoScale = 0;
            }
            else
            {
                nintendontCfg.videoScale = (sbyte)(VideoWidth.Value - 600);
            }

            string savePath = Path.Combine(SelectedDriveLetter, "nincfg.bin");
            if (!DriveSpecified)
            {
                var dialogResult = await MessageBoxWindow.Show(this, "SD card not specified.\nDo you wish to save the file somewhere else?",
                    "Drive not specified",
                    MessageBoxButtons.YesNo);
                if (dialogResult == MessageBoxResult.Yes)
                {
                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel != null)
                    {
                        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                        {
                            Title = "Save nincfg.bin",
                            DefaultExtension = "bin",
                            SuggestedFileName = "nincfg.bin",
                            FileTypeChoices = new[]
                            {
                                new FilePickerFileType("nintendont config files") { Patterns = new[] { "*.bin" } }
                            }
                        });
                        if (file != null)
                        {
                            savePath = file.Path.LocalPath;
                        }
                        else
                        {
                            return;
                        }
                    }
                    else
                    {
                        return;
                    }
                }
                else return;
            }

            using (var cfgFile = new BinaryWriter(File.Open(savePath, FileMode.Create)))
            {
                byte[] magicBytes = BitConverter.GetBytes(nintendontCfg.magicBytes);
                byte[] version = BitConverter.GetBytes(nintendontCfg.version);
                byte[] config = BitConverter.GetBytes(nintendontCfg.config);
                byte[] videoMode = BitConverter.GetBytes(nintendontCfg.videoMode);
                byte[] language = BitConverter.GetBytes(nintendontCfg.language);
                byte[] maxPads = BitConverter.GetBytes(nintendontCfg.maxPads);
                byte[] gameID = BitConverter.GetBytes(nintendontCfg.gameID);
                byte[] wiiuGamepadSlot = BitConverter.GetBytes(nintendontCfg.wiiuGamepadSlot);

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
                cfgFile.Write(nintendontCfg.gamePath);
                cfgFile.Write(nintendontCfg.cheatPath);
                cfgFile.Write(maxPads);
                cfgFile.Write(gameID);
                cfgFile.Write(nintendontCfg.memCardBlocks);
                cfgFile.Write(nintendontCfg.videoScale);
                cfgFile.Write(nintendontCfg.videoOffset);
                cfgFile.Write(nintendontCfg.networkProfile);
                cfgFile.Write(wiiuGamepadSlot);
            }

            await MessageBoxWindow.Show(this, "Config generation complete.", "Information", MessageBoxButtons.Ok);
        }
    }
}
