using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moon_WiiVC_Injector.Core;
using Moon_WiiVC_Injector.Services;

namespace Moon_WiiVC_Injector;

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

public class BuildEngine(BuildOptions options, IProgress<(string Message, double Progress)> progress, Action<string>? onLogMessage = null)
{
    private readonly BuildOptions _options = options;
    private readonly IProgress<(string Message, double Progress)> _progress = progress;
    private readonly Action<string>? _onLogMessage = onLogMessage;
    private readonly List<string> _logLines = [];

    private void Log(string message)
    {
        string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        _logLines.Add(logLine);
        _onLogMessage?.Invoke(logLine);
        AppLogger.Info(message);
    }

    public async Task SaveLogAsync(string outputPath)
    {
        try
        {
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }
            string logFilePath = Path.Combine(outputPath, "conversion.log");
            await File.WriteAllLinesAsync(logFilePath, _logLines);
            Log($"Log saved successfully to: {logFilePath}");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to write log file to {outputPath}", ex);
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
        string gc2Path = _options.Gc2Path;
        string? recoveredMainIso = null;
        string? recoveredGc2Iso = null;

        try
        {
            ValidatePrerequisites();

            // For Wii games, NKit images must be converted to standard ISO for partition extraction & NFS packaging.
            // For GameCube games, Nintendont natively boots NKit compressed dumps directly from files/game.iso!
            if (_options.SystemType == "wii" && IsNkitFile(ogFilePath))
            {
                recoveredMainIso = await ConvertNkitToIsoAsync(ogFilePath, cancellationToken);
                ogFilePath = recoveredMainIso;
            }

            // 1. Download base files with JNUSTool if not present
            string[] downloadedFiles =
            [
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
            ];

            string[] fileHashes =
            [
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
            ];

            string[] filesToDownload =
            [
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
            ];

            UpdateStatus("Checking if the necessary base files are present...", 3);

            string jarDir = Path.Combine(_options.TempToolsPath, "JAR");

            // Create config for JNUSTool
            string jnusConfigPath = Path.Combine(jarDir, "config");
            string[] jnusToolConfig = ["http://ccs.cdn.wup.shop.nintendo.net/ccs/download", _options.WiiUCommonKey];
            await File.WriteAllLinesAsync(jnusConfigPath, jnusToolConfig);

            // Create downloads directory if not exists
            Directory.CreateDirectory(_options.JNUSToolDownloads);

            bool hasDownloadedAnything = false;
            for (int i = 0; i < downloadedFiles.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(downloadedFiles[i]) && GetMD5Checksum(downloadedFiles[i]) == fileHashes[i])
                {
                    continue;
                }

                // Download it
                double dlProgress = 3.0 + (i * 6.0 / downloadedFiles.Length);
                UpdateStatus($"(One-Time Download) Downloading base files ({i + 1}/{downloadedFiles.Length})...", dlProgress);
                await LaunchProgramAsync("JNUSTool.exe", filesToDownload[i], true, cancellationToken, workingDirectory: jarDir);
                hasDownloadedAnything = true;
            }

            if (hasDownloadedAnything)
            {
                UpdateStatus("Saving files from Nintendo for future use...", 9);
                string jnusVake = Path.Combine(jarDir, "Rhythm Heaven Fever [VAKE01]");
                string jnus4000 = Path.Combine(jarDir, "0005001010004000");
                string jnus4001 = Path.Combine(jarDir, "0005001010004001");

                if (Directory.Exists(jnusVake))
                {
                    FileUtil.CopyDirectory(jnusVake, Path.Combine(_options.JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]"));
                    FileUtil.SafeDeleteDirectory(jnusVake);
                }
                if (Directory.Exists(jnus4000))
                {
                    FileUtil.CopyDirectory(jnus4000, Path.Combine(_options.JNUSToolDownloads, "0005001010004000"));
                    FileUtil.SafeDeleteDirectory(jnus4000);
                }
                if (Directory.Exists(jnus4001))
                {
                    FileUtil.CopyDirectory(jnus4001, Path.Combine(_options.JNUSToolDownloads, "0005001010004001"));
                    FileUtil.SafeDeleteDirectory(jnus4001);
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

            FileUtil.SafeDeleteFile(jnusConfigPath);

            // Copy downloaded files to build directory
            UpdateStatus("Copying base files to temporary build directory...", 10);

            FileUtil.SafeDeleteDirectory(_options.TempBuildPath);
            Directory.CreateDirectory(_options.TempBuildPath);
            Directory.CreateDirectory(Path.Combine(_options.TempBuildPath, "code"));
            Directory.CreateDirectory(Path.Combine(_options.TempBuildPath, "meta"));
            Directory.CreateDirectory(Path.Combine(_options.TempBuildPath, "content"));

            FileUtil.CopyDirectory(Path.Combine(_options.JNUSToolDownloads, "Rhythm Heaven Fever [VAKE01]"), _options.TempBuildPath);
            if (_options.C2WPatch)
            {
                FileUtil.CopyDirectory(Path.Combine(_options.JNUSToolDownloads, "0005001010004000"), _options.TempBuildPath);
                FileUtil.CopyDirectory(Path.Combine(_options.JNUSToolDownloads, "0005001010004001"), _options.TempBuildPath);
                UpdateStatus("Applying cafe2wii (c2w) patch...", 12);
                string c2wBuildImg = Path.Combine(_options.TempBuildPath, "code", "c2w.img");
                C2wPatcher.PatchC2wImage(c2wBuildImg, _options.AncastKey);
            }

            UpdateStatus("Generating app.xml and meta.xml...", 14);

            // Generate app.xml and meta.xml
            string appXml = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <app type="complex" access="777">
                  <version type="unsignedInt" length="4">16</version>
                  <os_version type="hexBinary" length="8">000500101000400A</os_version>
                  <title_id type="hexBinary" length="8">{_options.PackedTitleIDLine}</title_id>
                  <title_version type="hexBinary" length="2">0000</title_version>
                  <sdk_version type="unsignedInt" length="4">21204</sdk_version>
                  <app_type type="hexBinary" length="4">8000002E</app_type>
                  <group_id type="hexBinary" length="4">{_options.TitleIdHex}</group_id>
                  <os_mask type="hexBinary" length="32">0000000000000000000000000000000000000000000000000000000000000000</os_mask>
                  <common_id type="hexBinary" length="8">0000000000000000</common_id>
                </app>
                """;
            await File.WriteAllTextAsync(Path.Combine(_options.TempBuildPath, "code", "app.xml"), appXml);

            string line2Text = _options.EnablePackedLine2 ? _options.PackedTitleLine2 : "";
            var metaXmlBuilder = new StringBuilder();
            metaXmlBuilder.AppendLine($"""
                <?xml version="1.0" encoding="utf-8"?>
                <menu type="complex" access="777">
                  <version type="unsignedInt" length="4">33</version>
                  <product_code type="string" length="32">WUP-N-{_options.TitleIdText}</product_code>
                  <content_platform type="string" length="32">WUP</content_platform>
                  <company_code type="string" length="8">0001</company_code>
                  <mastering_date type="string" length="32"></mastering_date>
                  <logo_type type="unsignedInt" length="4">0</logo_type>
                  <app_launch_type type="hexBinary" length="4">00000000</app_launch_type>
                  <invisible_flag type="hexBinary" length="4">00000000</invisible_flag>
                  <no_managed_flag type="hexBinary" length="4">00000000</no_managed_flag>
                  <no_event_log type="hexBinary" length="4">00000000</no_event_log>
                  <no_icon_database type="hexBinary" length="4">00000000</no_icon_database>
                  <launch_switch type="hexBinary" length="4">00000000</launch_switch>
                  <add_on_unique_id0 type="hexBinary" length="4">00000000</add_on_unique_id0>
                  <add_on_unique_id1 type="hexBinary" length="4">00000000</add_on_unique_id1>
                  <add_on_unique_id2 type="hexBinary" length="4">00000000</add_on_unique_id2>
                  <add_on_unique_id3 type="hexBinary" length="4">00000000</add_on_unique_id3>
                  <add_on_unique_id4 type="hexBinary" length="4">00000000</add_on_unique_id4>
                  <add_on_unique_id5 type="hexBinary" length="4">00000000</add_on_unique_id5>
                  <add_on_unique_id6 type="hexBinary" length="4">00000000</add_on_unique_id6>
                  <add_on_unique_id7 type="hexBinary" length="4">00000000</add_on_unique_id7>
                """);

            for (int i = 0; i <= 11; i++)
            {
                string suffix = GetLanguageSuffix(i);
                metaXmlBuilder.AppendLine($"  <longname_{suffix} type=\"string\" length=\"512\">{System.Security.SecurityElement.Escape(_options.PackedTitleLine1)}</longname_{suffix}>");
                metaXmlBuilder.AppendLine($"  <shortname_{suffix} type=\"string\" length=\"256\">{System.Security.SecurityElement.Escape(line2Text)}</shortname_{suffix}>");
                metaXmlBuilder.AppendLine($"  <publisher_{suffix} type=\"string\" length=\"256\"></publisher_{suffix}>");
            }

            metaXmlBuilder.AppendLine("</menu>");
            await File.WriteAllTextAsync(Path.Combine(_options.TempBuildPath, "meta", "meta.xml"), metaXmlBuilder.ToString());

            UpdateStatus("Converting all image sources to expected TGA specification...", 16);

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
                using var bmp = SkiaSharp.SKBitmap.Decode(_options.TempBannerPath);
                TgaReader.SaveAsTga(bmp, Path.Combine(_options.TempBuildPath, "meta", "bootDrcTex.tga"), 854, 480, 24);
            }
            else
            {
                using var bmp = SkiaSharp.SKBitmap.Decode(_options.TempDrcPath);
                TgaReader.SaveAsTga(bmp, Path.Combine(_options.TempBuildPath, "meta", "bootDrcTex.tga"), 854, 480, 24);
            }

            bool flagLogoSpecified = File.Exists(_options.LogoDir);
            if (flagLogoSpecified)
            {
                using var bmp = SkiaSharp.SKBitmap.Decode(_options.TempLogoPath);
                TgaReader.SaveAsTga(bmp, Path.Combine(_options.TempBuildPath, "meta", "bootLogoTex.tga"), 170, 42, 32);
            }

            // Convert sound if specified
            bool flagBootSoundSpecified = File.Exists(_options.SoundDir);
            if (flagBootSoundSpecified)
            {
                UpdateStatus("Converting user-provided sound to btsnd format...", 18);
                string finalSoundBtsnd = Path.Combine(_options.TempBuildPath, "meta", "bootSound.btsnd");
                FileUtil.SafeDeleteFile(finalSoundBtsnd);

                string ext = Path.GetExtension(_options.SoundDir).ToLowerInvariant();
                if (ext == ".btsnd")
                {
                    File.Copy(_options.SoundDir, finalSoundBtsnd, true);
                }
                else
                {
                    Utilities.BtsndConverter.ConvertWavToBtsnd(_options.SoundDir, finalSoundBtsnd, _options.ToggleBootSoundLoop);
                }
            }

            UpdateStatus("Building game ISO image...", 20);
            string gameIsoPath = Path.Combine(_options.TempSourcePath, "game.iso");

            if (_options.SystemType == "wii")
            {
                string currentWiiGame = ogFilePath;
                if (_options.FlagWbfs)
                {
                    UpdateStatus("Converting WBFS file to ISO format...", 22);
                    string convertedIso = Path.Combine(_options.TempSourcePath, "wbfsconvert.iso");
                    await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "WIT", "wit.exe"), $"copy \"{ogFilePath}\" --DEST \"{convertedIso}\" --iso", true, cancellationToken);
                    currentWiiGame = convertedIso;
                }

                // Wii retail extract & patch
                if (!_options.DisableTrimming)
                {
                    string isoExtractDir = Path.Combine(_options.TempSourcePath, "ISOEXTRACT");
                    if (Directory.Exists(isoExtractDir)) Directory.Delete(isoExtractDir, true);

                    UpdateStatus("Extracting game ISO partitions...", 25);
                    await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "WIT", "wit.exe"), $"extract \"{currentWiiGame}\" --DEST \"{isoExtractDir}\" --psel data,-update -ovv", true, cancellationToken);

                    bool forceCC = _options.NfsPatchFlag.Contains("-instantcc");
                    if (forceCC)
                    {
                        UpdateStatus("Patching game main.dol for Classic Controller...", 30);
                        string mainDolPath = Path.Combine(isoExtractDir, "sys", "main.dol");
                        DolPatcher.PatchClassicController(mainDolPath);
                    }

                    string wiimmfiOption = _options.Wiimmfi ? " --wiimmfi" : "";
                    UpdateStatus("Rebuilding patched game ISO...", 33);
                    await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "WIT", "wit.exe"), $"copy \"{isoExtractDir}\" --DEST \"{gameIsoPath}\" -ovv --links --iso{wiimmfiOption}", true, cancellationToken);
                    if (Directory.Exists(isoExtractDir)) Directory.Delete(isoExtractDir, true);
                }
                else
                {
                    UpdateStatus("Copying untrimmed game ISO...", 33);
                    File.Copy(currentWiiGame, gameIsoPath, true);
                }

                if (File.Exists(Path.Combine(_options.TempSourcePath, "wbfsconvert.iso")))
                    File.Delete(Path.Combine(_options.TempSourcePath, "wbfsconvert.iso"));
            }
            else if (_options.SystemType == "dol")
            {
                string tempIsoBase = Path.Combine(_options.TempSourcePath, "TEMPISOBASE");
                if (Directory.Exists(tempIsoBase)) Directory.Delete(tempIsoBase, true);
                Core.BaseIsoTemplate.CreateBaseDirectory(tempIsoBase);
                File.Copy(ogFilePath, Path.Combine(tempIsoBase, "sys", "main.dol"), true);

                UpdateStatus("Rebuilding Homebrew game ISO...", 30);
                await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "WIT", "wit.exe"), $"copy \"{tempIsoBase}\" --DEST \"{gameIsoPath}\" -ovv --links --iso", true, cancellationToken);
                Directory.Delete(tempIsoBase, true);
            }
            else if (_options.SystemType == "gcn")
            {
                string tempIsoBase = Path.Combine(_options.TempSourcePath, "TEMPISOBASE");
                if (Directory.Exists(tempIsoBase)) Directory.Delete(tempIsoBase, true);
                Core.BaseIsoTemplate.CreateBaseDirectory(tempIsoBase);

                // Default forwarder or Nintendont boot dol selection
                Core.BaseIsoTemplate.CopyNintendontDol(Path.Combine(tempIsoBase, "sys", "main.dol"), _options.DisableNintendontAutoboot);

                UpdateStatus("Copying GameCube disc image...", 25);
                File.Copy(ogFilePath, Path.Combine(tempIsoBase, "files", "game.iso"), true);

                if (_options.FlagGc2Specified && !string.IsNullOrEmpty(gc2Path) && File.Exists(gc2Path))
                {
                    UpdateStatus("Copying GameCube Disc 2...", 28);
                    File.Copy(gc2Path, Path.Combine(tempIsoBase, "files", "disc2.iso"), true);
                }

                UpdateStatus("Rebuilding GameCube game ISO...", 32);
                await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "WIT", "wit.exe"), $"copy \"{tempIsoBase}\" --DEST \"{gameIsoPath}\" -ovv --links --iso", true, cancellationToken);
                Directory.Delete(tempIsoBase, true);
            }

            // Extract ticket and TMD for encrypting content
            UpdateStatus("Extracting game tickets and TMD information...", 38);
            string tikTempDir = Path.Combine(_options.TempSourcePath, "TIKTEMP");
            if (Directory.Exists(tikTempDir)) Directory.Delete(tikTempDir, true);
            await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "WIT", "wit.exe"), $"extract \"{gameIsoPath}\" --psel data --psel -update --files +tmd.bin --files +ticket.bin --dest \"{tikTempDir}\" -vv1", true, cancellationToken);

            File.Copy(Path.Combine(tikTempDir, "tmd.bin"), Path.Combine(_options.TempBuildPath, "code", "rvlt.tmd"), true);
            File.Copy(Path.Combine(tikTempDir, "ticket.bin"), Path.Combine(_options.TempBuildPath, "code", "rvlt.tik"), true);
            Directory.Delete(tikTempDir, true);

            // Convert ISO to NFS format (Smoothly mapped across 40% - 85%)
            UpdateStatus("Converting game ISO to NFS content format...", 40);

            List<string> nfsArgs = ["-enc"];
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

            string contentDir = Path.Combine(_options.TempBuildPath, "content");

            var nfsProgress = new Progress<(string Message, double Progress)>(update =>
            {
                double mapped = 40.0 + (Math.Clamp(update.Progress, 0.0, 1.0) * 45.0); // 40% to 85%
                UpdateStatus(update.Message, mapped);
            });

            // Convert in-process with real-time logging, cancellation and progress
            int nfsResult = Nfs2Iso2Nfs.ConvertNfs(
                [.. nfsArgs],
                baseDirectory: contentDir,
                onLog: Log,
                progress: nfsProgress,
                cancellationToken: cancellationToken);

            if (nfsResult != 0)
            {
                throw new Exception("Nfs2Iso2Nfs conversion failed. Please verify that the Wii Common Key is correct and the source game ISO is not corrupted.");
            }

            FileUtil.SafeDeleteFile(gameIsoPath);

            // Encrypt package with NUSPacker (Mapped across 85% - 98%)
            UpdateStatus("Encrypting contents into installable WUP package...", 85);
            string sanitizedGameName = SanitizeFilename(_options.PackedTitleLine1);
            finalOutputPath = Path.Combine(_options.SelectedOutputPath, sanitizedGameName + " WUP-N-" + _options.TitleIdText + "_" + _options.PackedTitleIDLine);

            string nusPackerExe = Path.Combine(_options.TempToolsPath, "JAR", "NUSPacker.exe");
            int nusChunkCount = 0;
            await LaunchProgramAsync(
                nusPackerExe,
                $"-in BUILDDIR -out \"{finalOutputPath}\" -encryptKeyWith {_options.WiiUCommonKey}",
                true,
                cancellationToken,
                workingDirectory: _options.TempRootPath,
                onOutputLine: line =>
                {
                    if (line.Contains(".app", StringComparison.OrdinalIgnoreCase) || line.Contains("Packing", StringComparison.OrdinalIgnoreCase) || line.Contains("Processing", StringComparison.OrdinalIgnoreCase))
                    {
                        nusChunkCount++;
                        double nusProgress = Math.Min(97.0, 85.0 + (nusChunkCount * 1.5));
                        UpdateStatus($"Packing WUP package ({line.Trim()})...", nusProgress);
                    }
                });

            // Cleanup
            UpdateStatus("Cleaning up temporary directories...", 98);
            FileUtil.SafeDeleteDirectory(_options.TempBuildPath);
            FileUtil.SafeDeleteDirectory(Path.Combine(_options.TempRootPath, "output"));
            FileUtil.SafeDeleteDirectory(Path.Combine(_options.TempRootPath, "tmp"));
            Directory.CreateDirectory(_options.TempBuildPath);

            if (Directory.Exists(finalOutputPath))
            {
                UpdateStatus("Build completed successfully!", 100);
                Log("Build completed successfully!");
                return finalOutputPath;
            }
            else
            {
                throw new Exception("WUP package directory was not created. Verify Java installation.");
            }
        }
        finally
        {
            if (!string.IsNullOrEmpty(recoveredMainIso) && File.Exists(recoveredMainIso))
            {
                try { File.Delete(recoveredMainIso); } catch { }
            }
            if (!string.IsNullOrEmpty(recoveredGc2Iso) && File.Exists(recoveredGc2Iso))
            {
                try { File.Delete(recoveredGc2Iso); } catch { }
            }
        }
    }

    private async Task LaunchProgramAsync(
        string exeFile,
        string arguments = "",
        bool hideProcess = true,
        CancellationToken cancellationToken = default,
        int[]? allowedExitCodes = null,
        string? workingDirectory = null,
        Action<string>? onOutputLine = null)
    {
        string targetExe = exeFile;
        string targetArgs = arguments;

        Log($"Launching program: {exeFile} {arguments}");

        string fileName = Path.GetFileName(exeFile).ToLowerInvariant();
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
                string fallbackJar = Path.Combine(!string.IsNullOrWhiteSpace(workingDirectory) ? workingDirectory : _options.TempRootPath, Path.ChangeExtension(Path.GetFileName(exeFile), ".jar"));
                if (File.Exists(fallbackJar))
                {
                    jarFile = fallbackJar;
                }
            }
        }

        if (File.Exists(jarFile))
        {
            targetExe = "java";
            targetArgs = $"-jar \"{jarFile}\" {arguments}";
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
            if (fileName is "wit.exe" or "wit")
            {
                string localWit = Path.Combine(Path.GetDirectoryName(exeFile) ?? string.Empty, "wit");
                if (File.Exists(localWit))
                {
                    targetExe = localWit;
                }
                else if (IsCommandAvailable("wit"))
                {
                    targetExe = "wit";
                }
                else
                {
                    targetExe = "wine";
                    targetArgs = $"\"{exeFile}\" {arguments}";
                }
            }
            else if (exeFile.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || exeFile.Contains("/TOOLDIR/"))
            {
                targetExe = "wine";
                targetArgs = $"\"{exeFile}\" {arguments}";
            }
        }

        string effectiveWorkingDir = !string.IsNullOrWhiteSpace(workingDirectory) ? workingDirectory : _options.TempRootPath;
        if (!Directory.Exists(effectiveWorkingDir))
        {
            Directory.CreateDirectory(effectiveWorkingDir);
        }

        ProcessStartInfo launcher = new(targetExe)
        {
            Arguments = targetArgs,
            UseShellExecute = false,
            WorkingDirectory = effectiveWorkingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = hideProcess
        };

        if (hideProcess)
        {
            launcher.WindowStyle = ProcessWindowStyle.Hidden;
        }

        using Process? process = Process.Start(launcher);
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
                    onOutputLine?.Invoke(e.Data);
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
            int[] validCodes = allowedExitCodes ?? [0];
            bool isCodeValid = false;
            foreach (int code in validCodes)
            {
                if (process.ExitCode == code)
                {
                    isCodeValid = true;
                    break;
                }
            }

            if (!isCodeValid)
            {
                var sbErr = new StringBuilder();
                sbErr.AppendLine($"Program {Path.GetFileName(exeFile)} exited with non-zero exit code: {process.ExitCode}");
                if (!string.IsNullOrWhiteSpace(stdOut))
                {
                    sbErr.AppendLine("\nOutput:");
                    sbErr.AppendLine(stdOut);
                }
                if (!string.IsNullOrWhiteSpace(stdErr))
                {
                    sbErr.AppendLine("\nError:");
                    sbErr.AppendLine(stdErr);
                }
                throw new Exception(sbErr.ToString());
            }
        }
        else
        {
            throw new Exception($"Failed to start process for {exeFile}");
        }
    }

    private void ValidatePrerequisites()
    {
        // 1. Verify Java runtime for JNUSTool and NUSPacker
        if (!IsCommandAvailable("java"))
        {
            throw new InvalidOperationException(
                "Java runtime (JRE/JDK) was not found in your system PATH.\n\n" +
                "Java is required by JNUSTool and NUSPacker to download base files and pack the Wii U WUP package.\n" +
                (OperatingSystem.IsLinux()
                    ? "Please install Java (e.g. 'sudo apt install default-jre' on Debian/Ubuntu or 'sudo pacman -S jre-openjdk' on Arch Linux)."
                    : "Please install 64-bit Java from https://adoptium.net or https://java.com and ensure 'java' is added to your PATH."));
        }

        // 2. On Linux/Unix, verify WIT (Wiimms ISO Tools) or Wine availability for disc image operations
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
            string localWit = Path.Combine(_options.TempToolsPath, "WIT", "wit");
            bool hasWit = File.Exists(localWit) || IsCommandAvailable("wit");
            bool hasWine = IsCommandAvailable("wine");

            if (!hasWit && !hasWine)
            {
                throw new InvalidOperationException(
                    "Neither 'wit' (Wiimms ISO Tools) nor 'wine' was found on your system.\n\n" +
                    "To process Wii and GameCube disc images on Linux, please install 'wit':\n" +
                    "  • Debian / Ubuntu: sudo apt install wit\n" +
                    "  • Arch Linux: sudo pacman -S wit\n" +
                    "  • Or install Wine: sudo apt install wine");
            }
        }
    }

    private static bool IsCommandAvailable(string cmd)
    {
        if (File.Exists(cmd)) return true;

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return false;

        string[] extensions = OperatingSystem.IsWindows()
            ? [".exe", ".cmd", ".bat", ""]
            : [""];

        foreach (string pathDir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (string ext in extensions)
            {
                try
                {
                    string fullPath = Path.Combine(pathDir, cmd + ext);
                    if (File.Exists(fullPath))
                        return true;
                }
                catch
                {
                    // Ignore invalid path characters in PATH entries
                }
            }
        }
        return false;
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

    private static bool IsNkitFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;
            using var fs = File.OpenRead(filePath);
            if (fs.Length < 0x204) return false;
            fs.Position = 0x200;
            byte[] idBytes = new byte[4];
            fs.ReadExactly(idBytes);
            return Encoding.ASCII.GetString(idBytes) == "NKIT";
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> ConvertNkitToIsoAsync(string nkitPath, CancellationToken cancellationToken)
    {
        UpdateStatus("Unscrubbing NKit game image...", 20);

        return await Task.Run(() =>
        {
            var src = NKit.SourceFiles.OpenFile(nkitPath);
            var converter = new NKit.Converter(src, true);

            converter.LogMessage += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Message))
                {
                    Log($"[NKit] {e.Message}");
                }
            };

            converter.LogProgress += (s, e) =>
            {
                double prg = 20.0 + (Math.Clamp(e.Progress, 0.0, 1.0) * 15.0);
                UpdateStatus($"[NKit] {e.StartMessage ?? "Unscrubbing"} ({(int)(e.Progress * 100)}%)...", prg);
            };

            NKit.OutputResults results = converter.ConvertToIso();

            if (!string.IsNullOrEmpty(results.ErrorMessage))
            {
                throw new InvalidOperationException($"NKit conversion failed: {results.ErrorMessage}");
            }

            if (!string.IsNullOrEmpty(results.OutputFileName) && File.Exists(results.OutputFileName))
            {
                string destFile = Path.Combine(_options.TempSourcePath, "nkit_recovered_" + Guid.NewGuid().ToString("N") + ".iso");
                File.Move(results.OutputFileName, destFile);
                Log($"Successfully recovered NKit file {nkitPath} to {destFile}");
                return destFile;
            }

            // Fallback search in Processed directory if output path is default
            string defaultOutDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Processed");
            if (Directory.Exists(defaultOutDir))
            {
                var files = Directory.GetFiles(defaultOutDir, "*.*", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    string sourceFile = files[0];
                    string destFile = Path.Combine(_options.TempSourcePath, "nkit_recovered_" + Guid.NewGuid().ToString("N") + ".iso");
                    File.Move(sourceFile, destFile);
                    Log($"Successfully recovered NKit file {nkitPath} to {destFile}");
                    FileUtil.SafeDeleteDirectory(defaultOutDir);
                    return destFile;
                }
            }

            throw new FileNotFoundException("Failed to locate converted ISO file after NKit processing.");
        }, cancellationToken);
    }
}
