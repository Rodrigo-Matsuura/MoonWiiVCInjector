using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Moon_WiiVC_Injector
{
    public class BuildOptions
    {
        public string SystemType { get; set; } = "wii";
        public string SelectedGamePath { get; set; } = string.Empty;
        public string SelectedOutputPath { get; set; } = string.Empty;
        
        public string WiiUCommonKey { get; set; } = string.Empty;
        public string TitleKey { get; set; } = string.Empty;
        public string AncastKey { get; set; } = string.Empty;
        public string PackedTitleIDLine { get; set; } = string.Empty;
        public string PackedTitleLine1 { get; set; } = string.Empty;
        public string PackedTitleLine2 { get; set; } = string.Empty;
        public bool EnablePackedLine2 { get; set; }
        
        public bool Wiimmfi { get; set; }
        public bool WiiVMC { get; set; }
        public bool DisableTrimming { get; set; }
        public bool DisableNintendontAutoboot { get; set; }
        public bool C2WPatch { get; set; }
        public bool LRPatch { get; set; }
        
        public string SoundDir { get; set; } = string.Empty;
        public string LogoDir { get; set; } = string.Empty;
        public string DrcDir { get; set; } = string.Empty;
        public string Gc2Path { get; set; } = string.Empty;
        
        public bool ToggleBootSoundLoop { get; set; }
        public string NfsPatchFlag { get; set; } = string.Empty;
        public string DrcUse { get; set; } = "1";
        
        // Metadata resolved from selected game
        public string TitleIdHex { get; set; } = string.Empty;
        public string TitleIdText { get; set; } = string.Empty;
        public bool FlagGc2Specified { get; set; }
        public bool FlagWbfs { get; set; }
        
        // Paths
        public string TempRootPath { get; set; } = string.Empty;
        public string TempSourcePath { get; set; } = string.Empty;
        public string TempBuildPath { get; set; } = string.Empty;
        public string TempToolsPath { get; set; } = string.Empty;
        public string JNUSToolDownloads { get; set; } = string.Empty;
        public string TempIconPath { get; set; } = string.Empty;
        public string TempBannerPath { get; set; } = string.Empty;
        public string TempDrcPath { get; set; } = string.Empty;
        public string TempLogoPath { get; set; } = string.Empty;
    }

    public class BuildEngine
    {
        private readonly BuildOptions _options;
        private readonly IProgress<(string Message, double Progress)> _progress;
        private readonly List<string> _logLines = new();

        public BuildEngine(BuildOptions options, IProgress<(string Message, double Progress)> progress)
        {
            _options = options;
            _progress = progress;
        }

        private void Log(string message)
        {
            string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            _logLines.Add(logLine);
            Debug.WriteLine(logLine);
        }

        public void SaveLog(string outputPath)
        {
            try
            {
                if (!Directory.Exists(outputPath))
                {
                    Directory.CreateDirectory(outputPath);
                }
                string logFilePath = Path.Combine(outputPath, "conversion.log");
                File.WriteAllLines(logFilePath, _logLines);
                Log($"Log saved successfully to: {logFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to write log file: {ex.Message}");
            }
        }

        private void UpdateStatus(string message, double progressValue)
        {
            Log($"Status update: {message} ({progressValue}%)");
            _progress.Report((message, progressValue));
        }

        public async Task<string> RunAsync(CancellationToken cancellationToken = default)
        {
            string finalOutputPath = "";
            string ogFilePath = _options.SelectedGamePath;

            // 1. Download base files with JNUSTool if not present
            string[] downloadedFiles = new[]
            {
                Path.Combine(_options.JNUSToolDownloads, "0005001010004000", "code", "deint.txt"),
                Path.Combine(_options.JNUSToolDownloads, "0005001010004000", "code", "font.bin"),
                Path.Combine(_options.JNUSToolDownloads, "0005001010004001", "code", "c2w.img"),
                Path.Combine(_options.JNUSToolDownloads, "0005001010004001", "code", "boot.bin"),
                Path.Combine(_options.JNUSToolDownloads, "0005001010004001", "code", "dmcu.d.hex"),
                Path.Combine(_options.JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "code", "cos.xml"),
                Path.Combine(_options.JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "code", "frisbiiU.rpx"),
                Path.Combine(_options.JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "code", "fw.img"),
                Path.Combine(_options.JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "code", "fw.tmd"),
                Path.Combine(_options.JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "code", "htk.bin"),
                Path.Combine(_options.JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "code", "nn_hai_user.rpl"),
                Path.Combine(_options.JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "content", "assets", "shaders", "cafe", "banner.gsh"),
                Path.Combine(_options.JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "content", "assets", "shaders", "cafe", "fade.gsh"),
                Path.Combine(_options.JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "meta", "bootMovie.h264"),
                Path.Combine(_options.JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "meta", "bootLogoTex.tga"),
                Path.Combine(_options.JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]", "meta", "bootSound.btsnd")
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
                "00050000101b0700 " + _options.TitleKey + " -file /code/cos.xml",
                "00050000101b0700 " + _options.TitleKey + " -file /code/frisbiiU.rpx",
                "00050000101b0700 " + _options.TitleKey + " -file /code/fw.img",
                "00050000101b0700 " + _options.TitleKey + " -file /code/fw.tmd",
                "00050000101b0700 " + _options.TitleKey + " -file /code/htk.bin",
                "00050000101b0700 " + _options.TitleKey + " -file /code/nn_hai_user.rpl",
                "00050000101b0700 " + _options.TitleKey + " -file /content/assets/shaders/cafe/banner.gsh",
                "00050000101b0700 " + _options.TitleKey + " -file /content/assets/shaders/cafe/fade.gsh*",
                "00050000101b0700 " + _options.TitleKey + " -file /meta/bootMovie.h264",
                "00050000101b0700 " + _options.TitleKey + " -file /meta/bootLogoTex.tga",
                "00050000101b0700 " + _options.TitleKey + " -file /meta/bootSound.btsnd"
            };

            UpdateStatus("Checking if the necessary files are present...", 10);

            // Create config for JNUSTool
            string jnusConfigPath = Path.Combine(_options.TempToolsPath, "JAR", "config");
            string[] jnusToolConfig = { "http://ccs.cdn.wup.shop.nintendo.net/ccs/download", _options.WiiUCommonKey };
            File.WriteAllLines(jnusConfigPath, jnusToolConfig);

            // Create downloads directory if not exists
            Directory.CreateDirectory(_options.JNUSToolDownloads);

            string currentDir = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(Path.Combine(_options.TempToolsPath, "JAR"));

            bool hasDownloadedAnything = false;
            for (int i = 0; i < downloadedFiles.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(downloadedFiles[i]) && GetMD5Checksum(downloadedFiles[i]) == fileHashes[i])
                {
                    continue;
                }

                // Download it
                UpdateStatus("(One-Time Download) Downloading base files from Nintendo...", 12 + i * 2);
                await LaunchProgramAsync("JNUSTool.exe", filesToDownload[i], true, cancellationToken);
                hasDownloadedAnything = true;
            }

            if (hasDownloadedAnything)
            {
                UpdateStatus("Saving files from Nintendo for future use...", 45);
                if (Directory.Exists("Rhythm Heaven Fever [VAKE01]"))
                {
                    FileUtil.CopyDirectory("Rhythm Heaven Fever [VAKE01]", Path.Combine(_options.JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]"));
                    Directory.Delete("Rhythm Heaven Fever [VAKE01]", true);
                }
                if (Directory.Exists("0005001010004000"))
                {
                    FileUtil.CopyDirectory("0005001010004000", Path.Combine(_options.JNUSToolDownloads, "0005001010004000"));
                    Directory.Delete("0005001010004000", true);
                }
                if (Directory.Exists("0005001010004001"))
                {
                    FileUtil.CopyDirectory("0005001010004001", Path.Combine(_options.JNUSToolDownloads, "0005001010004001"));
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
            Directory.SetCurrentDirectory(_options.TempRootPath);

            // Copy downloaded files to build directory
            UpdateStatus("Copying base files to temporary build directory...", 48);
            
            if (Directory.Exists(_options.TempBuildPath)) Directory.Delete(_options.TempBuildPath, true);
            Directory.CreateDirectory(_options.TempBuildPath);
            Directory.CreateDirectory(Path.Combine(_options.TempBuildPath, "code"));
            Directory.CreateDirectory(Path.Combine(_options.TempBuildPath, "meta"));
            Directory.CreateDirectory(Path.Combine(_options.TempBuildPath, "content"));

            FileUtil.CopyDirectory(Path.Combine(_options.JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]"), _options.TempBuildPath);
            if (_options.C2WPatch)
            {
                FileUtil.CopyDirectory(Path.Combine(_options.JNUSToolDownloads, "0005001010004000"), _options.TempBuildPath);
                FileUtil.CopyDirectory(Path.Combine(_options.JNUSToolDownloads, "0005001010004001"), _options.TempBuildPath);
                string[] ancastKeyCopy = { _options.AncastKey };
                File.WriteAllLines(Path.Combine(_options.TempToolsPath, "C2W", "starbuck_key.txt"), ancastKeyCopy);
                File.Copy(Path.Combine(_options.TempBuildPath, "code", "c2w.img"), Path.Combine(_options.TempToolsPath, "C2W", "c2w.img"), true);
                Directory.SetCurrentDirectory(Path.Combine(_options.TempToolsPath, "C2W"));
                await LaunchProgramAsync("c2w_patcher.exe", "-nc", true, cancellationToken);
                File.Delete(Path.Combine(_options.TempBuildPath, "code", "c2w.img"));
                File.Copy(Path.Combine(_options.TempToolsPath, "C2W", "c2p.img"), Path.Combine(_options.TempBuildPath, "code", "c2w.img"), true);
                File.Delete(Path.Combine(_options.TempToolsPath, "C2W", "c2p.img"));
                File.Delete(Path.Combine(_options.TempToolsPath, "C2W", "c2w.img"));
                File.Delete(Path.Combine(_options.TempToolsPath, "C2W", "starbuck_key.txt"));
                Directory.SetCurrentDirectory(_options.TempRootPath);
            }

            UpdateStatus("Generating app.xml and meta.xml...", 50);

            // Generate app.xml and meta.xml
            string[] appXml = { "<?xml version=\"1.0\" encoding=\"utf-8\"?>", "<app type=\"complex\" access=\"777\">", "  <version type=\"unsignedInt\" length=\"4\">16</version>", "  <os_version type=\"hexBinary\" length=\"8\">000500101000400A</os_version>", "  <title_id type=\"hexBinary\" length=\"8\">" + _options.PackedTitleIDLine + "</title_id>", "  <title_version type=\"hexBinary\" length=\"2\">0000</title_version>", "  <sdk_version type=\"unsignedInt\" length=\"4\">21204</sdk_version>", "  <app_type type=\"hexBinary\" length=\"4\">8000002E</app_type>", "  <group_id type=\"hexBinary\" length=\"4\">" + _options.TitleIdHex + "</group_id>", "  <os_mask type=\"hexBinary\" length=\"32\">0000000000000000000000000000000000000000000000000000000000000000</os_mask>", "  <common_id type=\"hexBinary\" length=\"8\">0000000000000000</common_id>", "</app>" };
            File.WriteAllLines(Path.Combine(_options.TempBuildPath, "code", "app.xml"), appXml);

            string line2Text = _options.EnablePackedLine2 ? _options.PackedTitleLine2 : "";
            List<string> metaXml = new List<string>
            {
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
                "<menu type=\"complex\" access=\"777\">",
                "  <version type=\"unsignedInt\" length=\"4\">33</version>",
                "  <product_code type=\"string\" length=\"32\">WUP-N-" + _options.TitleIdText + "</product_code>",
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
                "  <title_id type=\"hexBinary\" length=\"8\">" + _options.PackedTitleIDLine + "</title_id>",
                "  <group_id type=\"hexBinary\" length=\"4\">" + _options.TitleIdHex + "</group_id>",
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
                "  <drc_use type=\"unsignedInt\" length=\"4\">" + _options.DrcUse + "</drc_use>",
                "  <network_use type=\"unsignedInt\" length=\"4\">0</network_use>",
                "  <online_account_use type=\"unsignedInt\" length=\"4\">0</online_account_use>",
                "  <direct_boot type=\"unsignedInt\" length=\"4\">0</direct_boot>",
                "  <reserved_flag0 type=\"hexBinary\" length=\"4\">00010001</reserved_flag0>",
                "  <reserved_flag1 type=\"hexBinary\" length=\"4\">00080023</reserved_flag1>",
                "  <reserved_flag2 type=\"hexBinary\" length=\"4\">" + _options.TitleIdHex + "</reserved_flag2>",
                "  <reserved_flag3 type=\"hexBinary\" length=\"4\">00000000</reserved_flag3>",
                "  <reserved_flag4 type=\"hexBinary\" length=\"4\">00000000</reserved_flag4>",
                "  <reserved_flag5 type=\"hexBinary\" length=\"4\">00000000</reserved_flag5>",
                "  <reserved_flag6 type=\"hexBinary\" length=\"4\">00000003</reserved_flag6>",
                "  <reserved_flag7 type=\"hexBinary\" length=\"4\">00000005</reserved_flag7>"
            };

            string longName = string.IsNullOrEmpty(line2Text) ? _options.PackedTitleLine1 : $"{_options.PackedTitleLine1}\n{line2Text}";
            for (int i = 0; i < 11; i++) // for all languages
            {
                metaXml.Add($"  <longname_{GetLanguageSuffix(i)} type=\"string\" length=\"512\">{longName}</longname_{GetLanguageSuffix(i)}>");
            }
            for (int i = 0; i < 11; i++)
            {
                metaXml.Add($"  <shortname_{GetLanguageSuffix(i)} type=\"string\" length=\"512\">{_options.PackedTitleLine1}</shortname_{GetLanguageSuffix(i)}>");
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
            File.WriteAllLines(Path.Combine(_options.TempBuildPath, "meta", "meta.xml"), metaXml);

            UpdateStatus("Converting all image sources to expected TGA specification...", 52);

            // Convert images to TGA using our native SkiaSharp reader/converter
            using (var bmp = SkiaSharp.SKBitmap.Decode(_options.TempIconPath))
            {
                TgaReader.SaveAsTga(bmp, Path.Combine(_options.TempBuildPath, "meta", "iconTex.tga"), 128, 128, 32);
            }
            using (var bmp = SkiaSharp.SKBitmap.Decode(_options.TempBannerPath))
            {
                TgaReader.SaveAsTga(bmp, Path.Combine(_options.TempBuildPath, "meta", "bootTvTex.tga"), 1280, 720, 24);
            }
            
            bool flagDrcSpecified = File.Exists(_options.DrcDir);
            if (!flagDrcSpecified)
            {
                using (var bmp = SkiaSharp.SKBitmap.Decode(_options.TempBannerPath))
                {
                    TgaReader.SaveAsTga(bmp, Path.Combine(_options.TempBuildPath, "meta", "bootDrcTex.tga"), 854, 480, 24);
                }
            }
            else
            {
                using (var bmp = SkiaSharp.SKBitmap.Decode(_options.TempDrcPath))
                {
                    TgaReader.SaveAsTga(bmp, Path.Combine(_options.TempBuildPath, "meta", "bootDrcTex.tga"), 854, 480, 24);
                }
            }

            bool flagLogoSpecified = File.Exists(_options.LogoDir);
            if (flagLogoSpecified)
            {
                using (var bmp = SkiaSharp.SKBitmap.Decode(_options.TempLogoPath))
                {
                    TgaReader.SaveAsTga(bmp, Path.Combine(_options.TempBuildPath, "meta", "bootLogoTex.tga"), 170, 42, 32);
                }
            }

            UpdateStatus("Processing game for NFS Conversion...", 55);

            // Convert sound if specified
            bool flagBootSoundSpecified = File.Exists(_options.SoundDir);
            if (flagBootSoundSpecified)
            {
                UpdateStatus("Converting user-provided sound to btsnd format...", 60);
                string tempSoundWav = Path.Combine(_options.TempSourcePath, "temp_sound.wav");
                string finalSoundBtsnd = Path.Combine(_options.TempBuildPath, "meta", "bootSound.btsnd");

                // SOX to normalize/resample audio
                await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "SOX", "sox.exe"), $"\"{_options.SoundDir}\" -b 16 \"{tempSoundWav}\" channels 2 rate 48k trim 0 6", true, cancellationToken);
                if (File.Exists(finalSoundBtsnd)) File.Delete(finalSoundBtsnd);

                // wav2btsnd to convert to btsnd
                string loopString = _options.ToggleBootSoundLoop ? "" : " -noLoop";
                await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "JAR", "wav2btsnd.exe"), $"-in \"{tempSoundWav}\" -out \"{finalSoundBtsnd}\"{loopString}", true, cancellationToken);
                if (File.Exists(tempSoundWav)) File.Delete(tempSoundWav);
            }

            UpdateStatus("Building game ISO image...", 65);
            string gameIsoPath = Path.Combine(_options.TempSourcePath, "game.iso");

            if (_options.SystemType == "wii")
            {
                string currentWiiGame = ogFilePath;
                if (_options.FlagWbfs)
                {
                    UpdateStatus("Converting WBFS file to ISO format...", 66);
                    string convertedIso = Path.Combine(_options.TempSourcePath, "wbfsconvert.iso");
                    await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "EXE", "wbfs_file.exe"), $"\"{ogFilePath}\" convert \"{convertedIso}\"", true, cancellationToken);
                    currentWiiGame = convertedIso;
                }

                // Wii retail extract & patch
                if (!_options.DisableTrimming)
                {
                    string isoExtractDir = Path.Combine(_options.TempSourcePath, "ISOEXTRACT");
                    if (Directory.Exists(isoExtractDir)) Directory.Delete(isoExtractDir, true);

                    UpdateStatus("Extracting game ISO partitions (this may take a minute)...", 68);
                    await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "WIT", "wit.exe"), $"extract \"{currentWiiGame}\" --DEST \"{isoExtractDir}\" --psel data,-update -ovv", true, cancellationToken);
                    
                    bool forceCC = _options.NfsPatchFlag.Contains("-instantcc");
                    if (forceCC)
                    {
                        UpdateStatus("Patching game main.dol for Classic Controller...", 70);
                        await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "EXE", "GetExtTypePatcher.exe"), $"\"{Path.Combine(isoExtractDir, "sys", "main.dol")}\" -nc", true, cancellationToken);
                    }

                    // Wii VMC / Video mode changer (Not handled interactively on Linux, skipped/run natively if required)
                    if (_options.WiiVMC)
                    {
                        UpdateStatus("Applying Video Mode patch...", 71);
                        await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "EXE", "wii-vmc.exe"), $"\"{Path.Combine(isoExtractDir, "sys", "main.dol")}\"", true, cancellationToken);
                    }

                    string wiimmfiOption = _options.Wiimmfi ? " --wiimmfi" : "";
                    UpdateStatus("Rebuilding patched game ISO (this may take a minute)...", 72);
                    await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "WIT", "wit.exe"), $"copy \"{isoExtractDir}\" --DEST \"{gameIsoPath}\" -ovv --links --iso{wiimmfiOption}", true, cancellationToken);
                    if (Directory.Exists(isoExtractDir)) Directory.Delete(isoExtractDir, true);
                }
                else
                {
                    UpdateStatus("Copying untrimmed game ISO...", 73);
                    File.Copy(currentWiiGame, gameIsoPath, true);
                }

                if (File.Exists(Path.Combine(_options.TempSourcePath, "wbfsconvert.iso")))
                    File.Delete(Path.Combine(_options.TempSourcePath, "wbfsconvert.iso"));
            }
            else if (_options.SystemType == "dol")
            {
                string tempIsoBase = Path.Combine(_options.TempSourcePath, "TEMPISOBASE");
                if (Directory.Exists(tempIsoBase)) Directory.Delete(tempIsoBase, true);
                FileUtil.CopyDirectory(Path.Combine(_options.TempToolsPath, "BASE"), tempIsoBase);
                File.Copy(ogFilePath, Path.Combine(tempIsoBase, "sys", "main.dol"), true);
                
                UpdateStatus("Rebuilding Homebrew game ISO...", 70);
                await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "WIT", "wit.exe"), $"copy \"{tempIsoBase}\" --DEST \"{gameIsoPath}\" -ovv --links --iso", true, cancellationToken);
                Directory.Delete(tempIsoBase, true);
            }
            else if (_options.SystemType == "gcn")
            {
                string tempIsoBase = Path.Combine(_options.TempSourcePath, "TEMPISOBASE");
                if (Directory.Exists(tempIsoBase)) Directory.Delete(tempIsoBase, true);
                FileUtil.CopyDirectory(Path.Combine(_options.TempToolsPath, "BASE"), tempIsoBase);

                // Default forwarder or Nintendont boot dol selection
                string mainDolSrc = Path.Combine(_options.TempToolsPath, "DOL", "nintendont_default_autobooter.dol");
                if (_options.DisableNintendontAutoboot)
                    mainDolSrc = Path.Combine(_options.TempToolsPath, "DOL", "nintendont_forwarder.dol");

                File.Copy(mainDolSrc, Path.Combine(tempIsoBase, "sys", "main.dol"), true);
                
                UpdateStatus("Copying GameCube disc image...", 68);
                File.Copy(ogFilePath, Path.Combine(tempIsoBase, "files", "game.iso"), true);

                if (_options.FlagGc2Specified && !string.IsNullOrEmpty(_options.Gc2Path) && File.Exists(_options.Gc2Path))
                {
                    UpdateStatus("Copying GameCube Disc 2...", 69);
                    File.Copy(_options.Gc2Path, Path.Combine(tempIsoBase, "files", "disc2.iso"), true);
                }

                UpdateStatus("Rebuilding GameCube game ISO...", 70);
                await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "WIT", "wit.exe"), $"copy \"{tempIsoBase}\" --DEST \"{gameIsoPath}\" -ovv --links --iso", true, cancellationToken);
                Directory.Delete(tempIsoBase, true);
            }

            // Extract ticket and TMD for encrypting content
            UpdateStatus("Extracting game tickets and TMD information...", 75);
            string tikTempDir = Path.Combine(_options.TempSourcePath, "TIKTEMP");
            if (Directory.Exists(tikTempDir)) Directory.Delete(tikTempDir, true);
            await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "WIT", "wit.exe"), $"extract \"{gameIsoPath}\" --psel data --psel -update --files +tmd.bin --files +ticket.bin --dest \"{tikTempDir}\" -vv1", true, cancellationToken);
            
            File.Copy(Path.Combine(tikTempDir, "tmd.bin"), Path.Combine(_options.TempBuildPath, "code", "rvlt.tmd"), true);
            File.Copy(Path.Combine(tikTempDir, "ticket.bin"), Path.Combine(_options.TempBuildPath, "code", "rvlt.tik"), true);
            Directory.Delete(tikTempDir, true);

            // Convert ISO to NFS format
            UpdateStatus("Converting game ISO to NFS content format...", 80);
            
            List<string> nfsArgs = new List<string> { "-enc" };
            if (_options.SystemType == "dol" || _options.SystemType == "wiiware" || _options.SystemType == "gcn")
            {
                nfsArgs.Add("-homebrew");
            }
            if (_options.SystemType == "gcn")
            {
                nfsArgs.Add("-passthrough");
            }

            if (_options.NfsPatchFlag.Contains("-horizontal")) nfsArgs.Add("-horizontal");
            else if (_options.NfsPatchFlag.Contains("-wiimote")) nfsArgs.Add("-wiimote");
            else if (_options.NfsPatchFlag.Contains("-instantcc")) nfsArgs.Add("-instantcc");
            else if (_options.NfsPatchFlag.Contains("-nocc")) nfsArgs.Add("-nocc");

            if (_options.LRPatch) nfsArgs.Add("-lrpatch");

            nfsArgs.Add("-iso");
            nfsArgs.Add(gameIsoPath);

            Directory.SetCurrentDirectory(Path.Combine(_options.TempBuildPath, "content"));
            
            // Convert in-process (runs on current thread)
            int nfsResult = Nfs2Iso2Nfs.ConvertNfs(nfsArgs.ToArray());
            if (nfsResult != 0)
            {
                throw new Exception("Nfs2Iso2Nfs conversion failed. Please verify that the Wii Common Key is correct and the source game ISO is not corrupted.");
            }
            
            Directory.SetCurrentDirectory(_options.TempRootPath);
            if (File.Exists(gameIsoPath)) File.Delete(gameIsoPath);

            // Encrypt package with NUSPacker
            UpdateStatus("Encrypting contents into installable WUP package...", 90);
            string sanitizedGameName = SanitizeFilename(_options.PackedTitleLine1);
            finalOutputPath = Path.Combine(_options.SelectedOutputPath, sanitizedGameName + " WUP-N-" + _options.TitleIdText + "_" + _options.PackedTitleIDLine);
            
            await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "JAR", "NUSPacker.exe"), $"-in BUILDDIR -out \"{finalOutputPath}\" -encryptKeyWith {_options.WiiUCommonKey}", true, cancellationToken);
            
            // Cleanup
            UpdateStatus("Cleaning up temporary directories...", 98);
            if (Directory.Exists(_options.TempBuildPath)) Directory.Delete(_options.TempBuildPath, true);
            if (Directory.Exists(Path.Combine(_options.TempRootPath, "output"))) Directory.Delete(Path.Combine(_options.TempRootPath, "output"), true);
            if (Directory.Exists(Path.Combine(_options.TempRootPath, "tmp"))) Directory.Delete(Path.Combine(_options.TempRootPath, "tmp"), true);
            Directory.CreateDirectory(_options.TempBuildPath);

            if (Directory.Exists(finalOutputPath))
            {
                Log("Build completed successfully!");
                return finalOutputPath;
            }
            else
            {
                throw new Exception("WUP package directory was not created. Verify Java installation.");
            }
        }

        private async Task LaunchProgramAsync(string exeFile, string arguments = "", bool hideProcess = true, CancellationToken cancellationToken = default)
        {
            string targetExe = exeFile;
            string targetArgs = arguments;

            Log($"Launching program: {exeFile} {arguments}");

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
                        string localJar = Path.Combine(_options.TempToolsPath, "JAR", Path.ChangeExtension(Path.GetFileName(exeFile), ".jar"));
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

            ProcessStartInfo launcher = new ProcessStartInfo(targetExe)
            {
                Arguments = targetArgs,
                UseShellExecute = false,
                WorkingDirectory = Directory.GetCurrentDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = hideProcess
            };

            if (hideProcess)
            {
                launcher.WindowStyle = ProcessWindowStyle.Hidden;
            }

            using (Process? process = Process.Start(launcher))
            {
                if (process != null)
                {
                    // Start reading standard output and standard error asynchronously
                    var stdOutBuilder = new StringBuilder();
                    var stdErrBuilder = new StringBuilder();

                    process.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                        {
                            stdOutBuilder.AppendLine(e.Data);
                        }
                    };
                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                        {
                            stdErrBuilder.AppendLine(e.Data);
                        }
                    };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await process.WaitForExitAsync(cancellationToken);

                    string stdOut = stdOutBuilder.ToString().Trim();
                    string stdErr = stdErrBuilder.ToString().Trim();

                    if (!string.IsNullOrWhiteSpace(stdOut))
                    {
                        Log($"[STDOUT - {Path.GetFileName(exeFile)}]\n{stdOut}");
                    }
                    if (!string.IsNullOrWhiteSpace(stdErr))
                    {
                        Log($"[STDERR - {Path.GetFileName(exeFile)}]\n{stdErr}");
                    }

                    Log($"Program {exeFile} exited with code {process.ExitCode}");
                    if (process.ExitCode != 0)
                    {
                        throw new Exception($"Program {Path.GetFileName(exeFile)} exited with non-zero exit code: {process.ExitCode}");
                    }
                }
                else
                {
                    throw new Exception($"Failed to start process for {exeFile}");
                }
            }
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
            {
                byte[] checksum = MD5.HashData(stream);
                return Convert.ToHexString(checksum);
            }
        }
    }
}
