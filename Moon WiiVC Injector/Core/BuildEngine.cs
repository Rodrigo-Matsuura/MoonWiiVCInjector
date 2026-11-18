using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moon_WiiVC_Injector.Core;
using Moon_WiiVC_Injector.Core.NusPacker;
using Moon_WiiVC_Injector.Services;
using Moon_WiiVC_Injector.Utilities;

namespace Moon_WiiVC_Injector;

internal sealed class BuildOptions
{
    public string SystemType { get; set; } = "wii";
    public string SelectedGamePath { get; set; } = string.Empty;
    public string SelectedOutputPath { get; set; } = string.Empty;

    public string WiiUCommonKey { get; set; } = string.Empty;
    public string TitleKey { get; set; } = string.Empty;
    public string BaseTitleId { get; set; } = "00050000101B0700";
    public string BaseFolderName { get; set; } = "Rhythm Heaven Fever [VAKE01]";
    public string BaseHtkHash { get; set; } = "C99CAF5995E395F39C3FCAB4A8AF20E0";
    public string AncastKey { get; set; } = string.Empty;
    public string PackedTitleIDLine { get; set; } = string.Empty;
    public string PackedTitleLine1 { get; set; } = string.Empty;
    public string PackedTitleLine2 { get; set; } = string.Empty;
    public bool EnablePackedLine2 { get; set; }

    public bool Wiimmfi { get; set; }
    public VideoPatchMode WiiVmcMode { get; set; } = VideoPatchMode.None;
    public bool DisableTrimming { get; set; }
    public bool DisableNintendontAutoboot { get; set; }
    public bool C2WPatch { get; set; }
    public bool LRPatch { get; set; }

    public bool FlagBootSoundSpecified { get; set; }
    public bool FlagLogoSpecified { get; set; }
    public bool FlagDrcSpecified { get; set; }
    public string SoundPath { get; set; } = string.Empty;
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

internal sealed class BuildEngine(BuildOptions options, IProgress<(string Message, double Progress)> progress, Action<string>? onLogMessage = null)
{
    private static readonly string[] LanguageSuffixes =
    [
        "ja", "en", "fr", "de", "it", "es", "zhs", "ko", "nl", "pt", "ru", "zht"
    ];

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
        Log($"Status update: {message} ({progressValue:F0}%)");
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

            // For Wii games, NKit and NASOS (.iso.dec) images must be unscrubbed to standard ISO for partition extraction & NFS packaging.
            // For GameCube games, Nintendont natively boots NKit images, while NASOS (GCML .iso.dec) must be converted to ISO.
            if (_options.SystemType == "wii" && (IsNkitFile(ogFilePath) || IsNasosFile(ogFilePath)))
            {
                recoveredMainIso = await ConvertCompressedToIsoAsync(ogFilePath, cancellationToken);
                ogFilePath = recoveredMainIso;
            }
            else if (_options.SystemType == "gcn")
            {
                if (IsNasosFile(ogFilePath))
                {
                    recoveredMainIso = await ConvertCompressedToIsoAsync(ogFilePath, cancellationToken);
                    ogFilePath = recoveredMainIso;
                }

                if (_options.FlagGc2Specified && !string.IsNullOrEmpty(gc2Path) && IsNasosFile(gc2Path))
                {
                    recoveredGc2Iso = await ConvertCompressedToIsoAsync(gc2Path, cancellationToken);
                    gc2Path = recoveredGc2Iso;
                }
            }

            // 1. Download & decrypt base files directly from Nintendo NUS using native C# NusDownloader (Zero Java!)
            string baseFolder = !string.IsNullOrWhiteSpace(_options.BaseFolderName)
                ? _options.BaseFolderName
                : "Rhythm Heaven Fever [VAKE01]";
            string baseTitleId = !string.IsNullOrWhiteSpace(_options.BaseTitleId)
                ? _options.BaseTitleId
                : "00050000101B0700";
            string baseHtkHash = !string.IsNullOrWhiteSpace(_options.BaseHtkHash)
                ? _options.BaseHtkHash
                : "C99CAF5995E395F39C3FCAB4A8AF20E0";

            var nusRequests = new List<NusFileRequest>
            {
                new(baseTitleId, _options.TitleKey, "/code/cos.xml", Path.Combine(_options.JNUSToolDownloads, baseFolder, "code", "cos.xml"), "42215713D951C2023F90164ED9DF900F"),
                new(baseTitleId, _options.TitleKey, "/code/frisbiiU.rpx", Path.Combine(_options.JNUSToolDownloads, baseFolder, "code", "frisbiiU.rpx"), "69E191E8B0DF1D5304B36F1375C4F127"),
                new(baseTitleId, _options.TitleKey, "/code/fw.img", Path.Combine(_options.JNUSToolDownloads, baseFolder, "code", "fw.img"), "3CAF52A9A440EEE4F125A3AD22E305C8"),
                new(baseTitleId, _options.TitleKey, "/code/fw.tmd", Path.Combine(_options.JNUSToolDownloads, baseFolder, "code", "fw.tmd"), "AE4E06CAD3BEF60AE5C49E22CCDC3254"),
                new(baseTitleId, _options.TitleKey, "/code/htk.bin", Path.Combine(_options.JNUSToolDownloads, baseFolder, "code", "htk.bin"), baseHtkHash),
                new(baseTitleId, _options.TitleKey, "/code/nn_hai_user.rpl", Path.Combine(_options.JNUSToolDownloads, baseFolder, "code", "nn_hai_user.rpl"), "C4BF586BA0071BD8477986C1AA37E1F1"),
                new(baseTitleId, _options.TitleKey, "/content/assets/shaders/cafe/banner.gsh", Path.Combine(_options.JNUSToolDownloads, baseFolder, "content", "assets", "shaders", "cafe", "banner.gsh"), "5F2FA196DFC158F0FCC69272073AE07E"),
                new(baseTitleId, _options.TitleKey, "/content/assets/shaders/cafe/fade.gsh", Path.Combine(_options.JNUSToolDownloads, baseFolder, "content", "assets", "shaders", "cafe", "fade.gsh"), "307221985A7B46F0386A2637DC15DA3E"),
                new(baseTitleId, _options.TitleKey, "/meta/bootMovie.h264", Path.Combine(_options.JNUSToolDownloads, baseFolder, "meta", "bootMovie.h264"), "CA0DAC3E3C5654209C754357EF5A2507"),
                new(baseTitleId, _options.TitleKey, "/meta/bootLogoTex.tga", Path.Combine(_options.JNUSToolDownloads, baseFolder, "meta", "bootLogoTex.tga"), "67B312145ECB70514D5BD36FCAAE0193"),
                new(baseTitleId, _options.TitleKey, "/meta/bootSound.btsnd", Path.Combine(_options.JNUSToolDownloads, baseFolder, "meta", "bootSound.btsnd"), "43CD445B8569A445F97ECCC098C93B38")
            };

            if (_options.C2WPatch)
            {
                nusRequests.Add(new("0005001010004000", null, "/code/deint.txt", Path.Combine(_options.JNUSToolDownloads, "0005001010004000", "code", "deint.txt"), "E707A62EE5491DD16E5494631EA9870A"));
                nusRequests.Add(new("0005001010004000", null, "/code/font.bin", Path.Combine(_options.JNUSToolDownloads, "0005001010004000", "code", "font.bin"), "CDDAC70FDDB9428F220B048102DAAD40"));
                nusRequests.Add(new("0005001010004001", null, "/code/c2w.img", Path.Combine(_options.JNUSToolDownloads, "0005001010004001", "code", "c2w.img"), "FC5EE480F58796C3681BEE78BD3E5D1C"));
                nusRequests.Add(new("0005001010004001", null, "/code/boot.bin", Path.Combine(_options.JNUSToolDownloads, "0005001010004001", "code", "boot.bin"), "F4D5F095CBA9504A5CB8A94A4781114C"));
                nusRequests.Add(new("0005001010004001", null, "/code/dmcu.d.hex", Path.Combine(_options.JNUSToolDownloads, "0005001010004001", "code", "dmcu.d.hex"), "E32FCBCC817C443E0832DE5CA9032808"));
            }

            UpdateStatus("Checking if the necessary base files are present...", 3);
            Directory.CreateDirectory(_options.JNUSToolDownloads);

            var nusDownloader = new NusDownloader(
                httpClient: Program.Client,
                commonKeyHex: _options.WiiUCommonKey,
                onLog: Log);

            var nusProgress = new Progress<(int Completed, int Total, string CurrentFile)>(p =>
            {
                double pct = 3.0 + ((double)p.Completed / Math.Max(1, p.Total) * 7.0);
                UpdateStatus($"(One-Time Download) Downloading NUS base files ({Math.Min(p.Completed + 1, p.Total)}/{p.Total}): {Path.GetFileName(p.CurrentFile)}...", pct);
            });

            await nusDownloader.DownloadFilesAsync(nusRequests, nusProgress, cancellationToken);

            // Copy downloaded files to build directory
            UpdateStatus("Copying base files to temporary build directory...", 10);

            FileUtil.SafeDeleteDirectory(_options.TempBuildPath);
            Directory.CreateDirectory(_options.TempBuildPath);
            Directory.CreateDirectory(Path.Combine(_options.TempBuildPath, "code"));
            Directory.CreateDirectory(Path.Combine(_options.TempBuildPath, "meta"));
            Directory.CreateDirectory(Path.Combine(_options.TempBuildPath, "content"));

            FileUtil.CopyDirectory(Path.Combine(_options.JNUSToolDownloads, baseFolder), _options.TempBuildPath);
            if (_options.C2WPatch)
            {
                FileUtil.CopyDirectory(Path.Combine(_options.JNUSToolDownloads, "0005001010004000"), _options.TempBuildPath);
                FileUtil.CopyDirectory(Path.Combine(_options.JNUSToolDownloads, "0005001010004001"), _options.TempBuildPath);
                UpdateStatus("Applying cafe2wii (c2w) patch...", 12);
                string c2wBuildImg = Path.Combine(_options.TempBuildPath, "code", "c2w.img");
                if (!C2wPatcher.PatchC2wImage(c2wBuildImg, _options.AncastKey))
                {
                    throw new InvalidOperationException("Failed to patch c2w.img! Please check your Ancast Key and verify base files.");
                }
            }

            UpdateStatus("Generating app.xml and meta.xml...", 14);

            string groupIdHex = (!string.IsNullOrWhiteSpace(_options.TitleIdHex) && _options.TitleIdHex.Length == 8)
                ? _options.TitleIdHex
                : (_options.PackedTitleIDLine.Length == 16 ? _options.PackedTitleIDLine[8..] : "00000001");

            // Generate app.xml
            string appXml = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <app type="complex" access="777">
                  <version type="unsignedInt" length="4">16</version>
                  <os_version type="hexBinary" length="8">000500101000400A</os_version>
                  <title_id type="hexBinary" length="8">{_options.PackedTitleIDLine}</title_id>
                  <title_version type="hexBinary" length="2">0000</title_version>
                  <sdk_version type="unsignedInt" length="4">21204</sdk_version>
                  <app_type type="hexBinary" length="4">8000002E</app_type>
                  <group_id type="hexBinary" length="4">{groupIdHex}</group_id>
                  <os_mask type="hexBinary" length="32">0000000000000000000000000000000000000000000000000000000000000000</os_mask>
                  <common_id type="hexBinary" length="8">0000000000000000</common_id>
                </app>
                """;
            await File.WriteAllTextAsync(Path.Combine(_options.TempBuildPath, "code", "app.xml"), appXml, cancellationToken);

            // Generate complete Wii U meta.xml (including drc_use, launching_flag, reserved flags, and all 12 official languages)
            string metaXml = BuildMetaXml(groupIdHex);
            await File.WriteAllTextAsync(Path.Combine(_options.TempBuildPath, "meta", "meta.xml"), metaXml, cancellationToken);

            UpdateStatus("Converting all image sources to expected TGA specification...", 16);

            // Convert images to TGA using SkiaSharp
            using (var bmp = DecodeBitmap(_options.TempIconPath, "icon"))
            {
                TgaReader.SaveAsTga(bmp, Path.Combine(_options.TempBuildPath, "meta", "iconTex.tga"), 128, 128, 32);
            }

            using (var bmp = DecodeBitmap(_options.TempBannerPath, "TV banner"))
            {
                TgaReader.SaveAsTga(bmp, Path.Combine(_options.TempBuildPath, "meta", "bootTvTex.tga"), 1280, 720, 24);
            }

            string drcSourcePath = (_options.FlagDrcSpecified && File.Exists(_options.TempDrcPath))
                ? _options.TempDrcPath
                : _options.TempBannerPath;
            using (var bmp = DecodeBitmap(drcSourcePath, "GamePad DRC banner"))
            {
                TgaReader.SaveAsTga(bmp, Path.Combine(_options.TempBuildPath, "meta", "bootDrcTex.tga"), 854, 480, 24);
            }

            if (_options.FlagLogoSpecified && File.Exists(_options.TempLogoPath))
            {
                using var bmp = DecodeBitmap(_options.TempLogoPath, "boot logo");
                TgaReader.SaveAsTga(bmp, Path.Combine(_options.TempBuildPath, "meta", "bootLogoTex.tga"), 170, 42, 32);
            }

            // Convert or apply boot sound if specified (user file or repository auto-download)
            if (_options.FlagBootSoundSpecified && !string.IsNullOrEmpty(_options.SoundPath) && File.Exists(_options.SoundPath))
            {
                UpdateStatus("Processing boot sound to Wii U btsnd format...", 18);
                string finalSoundBtsnd = Path.Combine(_options.TempBuildPath, "meta", "bootSound.btsnd");
                FileUtil.SafeDeleteFile(finalSoundBtsnd);
                BtsndConverter.ConvertSoundToBtsnd(_options.SoundPath, finalSoundBtsnd, _options.ToggleBootSoundLoop);
            }

            UpdateStatus("Building game ISO image...", 20);
            string gameIsoPath = Path.Combine(_options.TempSourcePath, "game.iso");

            if (_options.SystemType == "wii")
            {
                string currentWiiGame = ogFilePath;
                string convertedIso = Path.Combine(_options.TempSourcePath, "wbfsconvert.iso");
                if (_options.FlagWbfs)
                {
                    UpdateStatus("Converting WBFS file to ISO format...", 22);
                    await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "WIT", "wit.exe"), $"copy \"{ogFilePath}\" --DEST \"{convertedIso}\" --iso", cancellationToken: cancellationToken);
                    currentWiiGame = convertedIso;
                }

                bool forceCC = _options.NfsPatchFlag.Contains("-instantcc");
                bool requiresExtraction = !_options.DisableTrimming || forceCC || _options.WiiVmcMode != VideoPatchMode.None || _options.Wiimmfi;

                if (requiresExtraction)
                {
                    string isoExtractDir = Path.Combine(_options.TempSourcePath, "ISOEXTRACT");
                    if (Directory.Exists(isoExtractDir)) Directory.Delete(isoExtractDir, true);

                    UpdateStatus("Extracting game ISO partitions...", 25);
                    await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "WIT", "wit.exe"), $"extract \"{currentWiiGame}\" --DEST \"{isoExtractDir}\" --psel data,-update -ovv", cancellationToken: cancellationToken);

                    // Free up 4.8 - 8.5 GB immediately before rebuilding the ISO to avoid running out of disk space
                    if (_options.FlagWbfs && File.Exists(convertedIso))
                    {
                        FileUtil.SafeDeleteFile(convertedIso);
                    }

                    string mainDolPath = Path.Combine(isoExtractDir, "sys", "main.dol");
                    if (forceCC)
                    {
                        UpdateStatus("Patching game main.dol for Classic Controller...", 29);
                        DolPatcher.PatchClassicController(mainDolPath);
                    }

                    if (_options.WiiVmcMode != VideoPatchMode.None)
                    {
                        UpdateStatus($"Patching game main.dol video modes ({_options.WiiVmcMode})...", 31);
                        DolPatcher.PatchVideoMode(mainDolPath, _options.WiiVmcMode);
                    }

                    string wiimmfiOption = _options.Wiimmfi ? " --wiimmfi" : "";
                    UpdateStatus("Rebuilding patched game ISO...", 33);
                    await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "WIT", "wit.exe"), $"copy \"{isoExtractDir}\" --DEST \"{gameIsoPath}\" -ovv --links --iso{wiimmfiOption}", cancellationToken: cancellationToken);
                    if (Directory.Exists(isoExtractDir)) Directory.Delete(isoExtractDir, true);
                }
                else
                {
                    UpdateStatus("Copying untrimmed game ISO...", 33);
                    File.Copy(currentWiiGame, gameIsoPath, true);
                }

                if (File.Exists(convertedIso))
                    FileUtil.SafeDeleteFile(convertedIso);
            }
            else if (_options.SystemType == "dol")
            {
                string tempIsoBase = Path.Combine(_options.TempSourcePath, "TEMPISOBASE");
                if (Directory.Exists(tempIsoBase)) Directory.Delete(tempIsoBase, true);
                BaseIsoTemplate.CreateBaseDirectory(tempIsoBase);
                File.Copy(ogFilePath, Path.Combine(tempIsoBase, "sys", "main.dol"), true);

                UpdateStatus("Rebuilding Homebrew game ISO...", 30);
                await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "WIT", "wit.exe"), $"copy \"{tempIsoBase}\" --DEST \"{gameIsoPath}\" -ovv --links --iso", cancellationToken: cancellationToken);
                Directory.Delete(tempIsoBase, true);
            }
            else if (_options.SystemType == "gcn")
            {
                string tempIsoBase = Path.Combine(_options.TempSourcePath, "TEMPISOBASE");
                if (Directory.Exists(tempIsoBase)) Directory.Delete(tempIsoBase, true);
                BaseIsoTemplate.CreateBaseDirectory(tempIsoBase);

                BaseIsoTemplate.CopyNintendontDol(Path.Combine(tempIsoBase, "sys", "main.dol"), _options.DisableNintendontAutoboot);

                UpdateStatus("Copying GameCube disc image...", 25);
                File.Copy(ogFilePath, Path.Combine(tempIsoBase, "files", "game.iso"), true);

                if (_options.FlagGc2Specified && !string.IsNullOrEmpty(gc2Path) && File.Exists(gc2Path))
                {
                    UpdateStatus("Copying GameCube Disc 2...", 28);
                    File.Copy(gc2Path, Path.Combine(tempIsoBase, "files", "disc2.iso"), true);
                }

                UpdateStatus("Rebuilding GameCube game ISO...", 32);
                await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "WIT", "wit.exe"), $"copy \"{tempIsoBase}\" --DEST \"{gameIsoPath}\" -ovv --links --iso", cancellationToken: cancellationToken);
                Directory.Delete(tempIsoBase, true);
            }
            else if (_options.SystemType == "wiiware")
            {
                string tempIsoBase = Path.Combine(_options.TempSourcePath, "TEMPISOBASE");
                if (Directory.Exists(tempIsoBase)) Directory.Delete(tempIsoBase, true);
                BaseIsoTemplate.CreateBaseDirectory(tempIsoBase);

                BaseIsoTemplate.CopyChanBooterDol(Path.Combine(tempIsoBase, "sys", "main.dol"));

                string[] titleTxt = [_options.TitleIdText];
                await File.WriteAllLinesAsync(Path.Combine(tempIsoBase, "files", "title.txt"), titleTxt, cancellationToken);

                UpdateStatus("Rebuilding vWii NAND Title Launcher ISO...", 30);
                await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "WIT", "wit.exe"), $"copy \"{tempIsoBase}\" --DEST \"{gameIsoPath}\" -ovv --links --iso", cancellationToken: cancellationToken);
                Directory.Delete(tempIsoBase, true);
            }

            // Extract ticket and TMD for encrypting content
            UpdateStatus("Extracting game tickets and TMD information...", 38);
            string tikTempDir = Path.Combine(_options.TempSourcePath, "TIKTEMP");
            if (Directory.Exists(tikTempDir)) Directory.Delete(tikTempDir, true);
            await LaunchProgramAsync(Path.Combine(_options.TempToolsPath, "WIT", "wit.exe"), $"extract \"{gameIsoPath}\" --psel data --psel -update --files +tmd.bin --files +ticket.bin --dest \"{tikTempDir}\" -vv1", cancellationToken: cancellationToken);

            File.Copy(Path.Combine(tikTempDir, "tmd.bin"), Path.Combine(_options.TempBuildPath, "code", "rvlt.tmd"), true);
            File.Copy(Path.Combine(tikTempDir, "ticket.bin"), Path.Combine(_options.TempBuildPath, "code", "rvlt.tik"), true);
            Directory.Delete(tikTempDir, true);

            // Convert ISO to NFS format (Smoothly mapped across 40% - 85%)
            UpdateStatus("Converting game ISO to NFS content format...", 40);

            string contentDir = Path.Combine(_options.TempBuildPath, "content");

            var nfsOptions = new NfsConversionOptions
            {
                Encrypt = true,
                Decrypt = false,
                IsoFile = gameIsoPath,
                NfsDirectory = contentDir,
                Homebrew = _options.SystemType is "dol" or "wiiware" or "gcn",
                Passthrough = _options.SystemType == "gcn",
                HorizontalWiimote = _options.NfsPatchFlag.Contains("-horizontal"),
                VerticalWiimote = _options.NfsPatchFlag.Contains("-wiimote"),
                InstantCc = _options.NfsPatchFlag.Contains("-instantcc"),
                NoCc = _options.NfsPatchFlag.Contains("-nocc"),
                MapShoulderToTrigger = _options.LRPatch
            };

            var nfsProgress = new Progress<(string Message, double Progress)>(update =>
            {
                double mapped = 40.0 + (Math.Clamp(update.Progress, 0.0, 1.0) * 45.0);
                UpdateStatus(update.Message, mapped);
            });

            int nfsResult = Nfs2Iso2Nfs.ConvertNfs(
                nfsOptions,
                baseDirectory: contentDir,
                onLog: Log,
                progress: nfsProgress,
                cancellationToken: cancellationToken);

            if (nfsResult != 0)
            {
                throw new InvalidOperationException("Nfs2Iso2Nfs conversion failed. Please verify that the Wii U Common Key is correct and the source game ISO is not corrupted.");
            }

            FileUtil.SafeDeleteFile(gameIsoPath);

            // Encrypt package with native C# NusPacker (Mapped across 85% - 98%)
            UpdateStatus("Encrypting contents into installable WUP package...", 85);
            string sanitizedGameName = SanitizeFilename(_options.PackedTitleLine1);
            finalOutputPath = Path.Combine(_options.SelectedOutputPath, sanitizedGameName + " WUP-N-" + _options.TitleIdText + "_" + _options.PackedTitleIDLine);

            string cleanBuildPath = Path.GetFullPath(_options.TempBuildPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string cleanOutputPath = Path.GetFullPath(finalOutputPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            string codeDir = Path.Combine(cleanBuildPath, "code");
            string contentSubDir = Path.Combine(cleanBuildPath, "content");
            string metaDir = Path.Combine(cleanBuildPath, "meta");

            if (!Directory.Exists(codeDir) || !Directory.Exists(contentSubDir) || !Directory.Exists(metaDir))
            {
                throw new InvalidOperationException(
                    $"Temporary build directory '{cleanBuildPath}' is missing required folders (code, content, or meta) for packaging.");
            }

            var packProgress = new Progress<(string Status, double Percentage)>(p =>
            {
                double mappedProgress = 85.0 + (p.Percentage * 12.0);
                UpdateStatus($"Packing WUP package: {p.Status}", mappedProgress);
            });

            string stagingTemp = Path.Combine(_options.TempRootPath, "nus_tmp");
            await NusPackerEngine.PackAsync(
                inputPath: cleanBuildPath,
                outputPath: cleanOutputPath,
                commonKey: _options.WiiUCommonKey.Trim(),
                tempDirectory: stagingTemp,
                progress: packProgress,
                cancellationToken: cancellationToken);

            // Post-validation: verify that packaging produced a complete, valid WUP package.
            bool hasTmd = File.Exists(Path.Combine(cleanOutputPath, "title.tmd"));
            bool hasTik = File.Exists(Path.Combine(cleanOutputPath, "title.tik"));
            bool hasCert = File.Exists(Path.Combine(cleanOutputPath, "title.cert"));
            bool hasApp = Directory.Exists(cleanOutputPath) && Directory.EnumerateFiles(cleanOutputPath, "*.app").Any();

            if (!Directory.Exists(cleanOutputPath) || !hasTmd || !hasTik || !hasCert || !hasApp)
            {
                var sb = new StringBuilder();
                sb.AppendLine("WUP package creation failed: Package files (title.tmd, title.tik, title.cert, *.app) were not created properly.");
                if (Directory.Exists(cleanOutputPath))
                {
                    var existingFiles = Directory.GetFileSystemEntries(cleanOutputPath);
                    if (existingFiles.Length == 0)
                    {
                        sb.AppendLine($"The output directory '{cleanOutputPath}' was created but is completely empty (0 files).");
                    }
                    else
                    {
                        sb.AppendLine($"The output directory '{cleanOutputPath}' contains incomplete files: {string.Join(", ", existingFiles.Select(Path.GetFileName))}");
                    }
                }
                else
                {
                    sb.AppendLine($"The output directory '{cleanOutputPath}' was not created.");
                }

                throw new InvalidOperationException(sb.ToString().Trim());
            }

            // Cleanup intermediate build files after package integrity has been verified
            UpdateStatus("Cleaning up temporary directories...", 98);
            FileUtil.SafeDeleteDirectory(_options.TempBuildPath);
            FileUtil.SafeDeleteDirectory(Path.Combine(_options.TempRootPath, "output"));
            FileUtil.SafeDeleteDirectory(Path.Combine(_options.TempRootPath, "tmp"));
            FileUtil.SafeDeleteDirectory(stagingTemp);
            Directory.CreateDirectory(_options.TempBuildPath);

            UpdateStatus("Build completed successfully!", 100);
            Log("Build completed successfully!");
            return cleanOutputPath;
        }
        catch (OperationCanceledException)
        {
            Log("[CANCELLED] Build operation cancelled; cleaning up intermediate files...");
            FileUtil.SafeDeleteDirectory(_options.TempBuildPath);
            FileUtil.SafeDeleteDirectory(Path.Combine(_options.TempRootPath, "nus_tmp"));
            FileUtil.SafeDeleteFile(Path.Combine(_options.TempSourcePath, "game.iso"));
            FileUtil.SafeDeleteFile(Path.Combine(_options.TempSourcePath, "wbfsconvert.iso"));
            FileUtil.SafeDeleteDirectory(Path.Combine(_options.TempSourcePath, "ISOEXTRACT"));
            FileUtil.SafeDeleteDirectory(Path.Combine(_options.TempSourcePath, "TEMPISOBASE"));
            FileUtil.SafeDeleteDirectory(Path.Combine(_options.TempSourcePath, "TIKTEMP"));
            throw;
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Build process failed: {ex.Message}");
            Log($"[ERROR] Exception details: {ex}");
            throw;
        }
        finally
        {
            if (!string.IsNullOrEmpty(recoveredMainIso) && File.Exists(recoveredMainIso))
            {
                FileUtil.SafeDeleteFile(recoveredMainIso);
            }
            if (!string.IsNullOrEmpty(recoveredGc2Iso) && File.Exists(recoveredGc2Iso))
            {
                FileUtil.SafeDeleteFile(recoveredGc2Iso);
            }
        }
    }

    private string BuildMetaXml(string groupIdHex)
    {
        string escapedLine1 = System.Security.SecurityElement.Escape(_options.PackedTitleLine1) ?? string.Empty;
        string escapedLine2 = System.Security.SecurityElement.Escape(_options.PackedTitleLine2) ?? string.Empty;
        string longNameValue = (_options.EnablePackedLine2 && !string.IsNullOrEmpty(escapedLine2))
            ? $"{escapedLine1}\n{escapedLine2}"
            : escapedLine1;

        string drcUse = !string.IsNullOrWhiteSpace(_options.DrcUse) ? _options.DrcUse : "1";

        var sb = new StringBuilder(8192);
        sb.AppendLine($"""
            <?xml version="1.0" encoding="utf-8"?>
            <menu type="complex" access="777">
              <version type="unsignedInt" length="4">33</version>
              <product_code type="string" length="32">WUP-N-{System.Security.SecurityElement.Escape(_options.TitleIdText)}</product_code>
              <content_platform type="string" length="32">WUP</content_platform>
              <company_code type="string" length="8">0001</company_code>
              <mastering_date type="string" length="32"></mastering_date>
              <logo_type type="unsignedInt" length="4">0</logo_type>
              <app_launch_type type="hexBinary" length="4">00000000</app_launch_type>
              <invisible_flag type="hexBinary" length="4">00000000</invisible_flag>
              <no_managed_flag type="hexBinary" length="4">00000000</no_managed_flag>
              <no_event_log type="hexBinary" length="4">00000002</no_event_log>
              <no_icon_database type="hexBinary" length="4">00000000</no_icon_database>
              <launching_flag type="hexBinary" length="4">00000004</launching_flag>
              <install_flag type="hexBinary" length="4">00000000</install_flag>
              <closing_msg type="unsignedInt" length="4">0</closing_msg>
              <title_version type="unsignedInt" length="4">0</title_version>
              <title_id type="hexBinary" length="8">{_options.PackedTitleIDLine}</title_id>
              <group_id type="hexBinary" length="4">{groupIdHex}</group_id>
              <boss_id type="hexBinary" length="8">0000000000000000</boss_id>
              <os_version type="hexBinary" length="8">000500101000400A</os_version>
              <app_size type="hexBinary" length="8">0000000000000000</app_size>
              <common_save_size type="hexBinary" length="8">0000000000000000</common_save_size>
              <account_save_size type="hexBinary" length="8">0000000000000000</account_save_size>
              <common_boss_size type="hexBinary" length="8">0000000000000000</common_boss_size>
              <account_boss_size type="hexBinary" length="8">0000000000000000</account_boss_size>
              <save_no_rollback type="unsignedInt" length="4">0</save_no_rollback>
              <join_game_id type="hexBinary" length="4">00000000</join_game_id>
              <join_game_mode_mask type="hexBinary" length="8">0000000000000000</join_game_mode_mask>
              <bg_daemon_enable type="unsignedInt" length="4">0</bg_daemon_enable>
              <olv_accesskey type="unsignedInt" length="4">3921400692</olv_accesskey>
              <wood_tin type="unsignedInt" length="4">0</wood_tin>
              <e_manual type="unsignedInt" length="4">0</e_manual>
              <e_manual_version type="unsignedInt" length="4">0</e_manual_version>
              <region type="hexBinary" length="4">00000002</region>
              <pc_cero type="unsignedInt" length="4">128</pc_cero>
              <pc_esrb type="unsignedInt" length="4">6</pc_esrb>
              <pc_bbfc type="unsignedInt" length="4">192</pc_bbfc>
              <pc_usk type="unsignedInt" length="4">128</pc_usk>
              <pc_pegi_gen type="unsignedInt" length="4">128</pc_pegi_gen>
              <pc_pegi_fin type="unsignedInt" length="4">192</pc_pegi_fin>
              <pc_pegi_prt type="unsignedInt" length="4">128</pc_pegi_prt>
              <pc_pegi_bbfc type="unsignedInt" length="4">128</pc_pegi_bbfc>
              <pc_cob type="unsignedInt" length="4">128</pc_cob>
              <pc_grb type="unsignedInt" length="4">128</pc_grb>
              <pc_cgsrr type="unsignedInt" length="4">128</pc_cgsrr>
              <pc_oflc type="unsignedInt" length="4">128</pc_oflc>
              <pc_reserved0 type="unsignedInt" length="4">192</pc_reserved0>
              <pc_reserved1 type="unsignedInt" length="4">192</pc_reserved1>
              <pc_reserved2 type="unsignedInt" length="4">192</pc_reserved2>
              <pc_reserved3 type="unsignedInt" length="4">192</pc_reserved3>
              <ext_dev_nunchaku type="unsignedInt" length="4">0</ext_dev_nunchaku>
              <ext_dev_classic type="unsignedInt" length="4">0</ext_dev_classic>
              <ext_dev_urcc type="unsignedInt" length="4">0</ext_dev_urcc>
              <ext_dev_board type="unsignedInt" length="4">0</ext_dev_board>
              <ext_dev_usb_keyboard type="unsignedInt" length="4">0</ext_dev_usb_keyboard>
              <ext_dev_etc type="unsignedInt" length="4">0</ext_dev_etc>
              <ext_dev_etc_name type="string" length="512"></ext_dev_etc_name>
              <eula_version type="unsignedInt" length="4">0</eula_version>
              <drc_use type="unsignedInt" length="4">{drcUse}</drc_use>
              <network_use type="unsignedInt" length="4">0</network_use>
              <online_account_use type="unsignedInt" length="4">0</online_account_use>
              <direct_boot type="unsignedInt" length="4">0</direct_boot>
              <reserved_flag0 type="hexBinary" length="4">00010001</reserved_flag0>
              <reserved_flag1 type="hexBinary" length="4">00080023</reserved_flag1>
              <reserved_flag2 type="hexBinary" length="4">{groupIdHex}</reserved_flag2>
              <reserved_flag3 type="hexBinary" length="4">00000000</reserved_flag3>
              <reserved_flag4 type="hexBinary" length="4">00000000</reserved_flag4>
              <reserved_flag5 type="hexBinary" length="4">00000000</reserved_flag5>
              <reserved_flag6 type="hexBinary" length="4">00000003</reserved_flag6>
              <reserved_flag7 type="hexBinary" length="4">00000005</reserved_flag7>
            """);

        foreach (string lang in LanguageSuffixes)
        {
            sb.AppendLine($"  <longname_{lang} type=\"string\" length=\"512\">{longNameValue}</longname_{lang}>");
        }
        foreach (string lang in LanguageSuffixes)
        {
            sb.AppendLine($"  <shortname_{lang} type=\"string\" length=\"256\">{escapedLine1}</shortname_{lang}>");
        }
        foreach (string lang in LanguageSuffixes)
        {
            sb.AppendLine($"  <publisher_{lang} type=\"string\" length=\"256\"></publisher_{lang}>");
        }
        for (int i = 0; i < 32; i++)
        {
            sb.AppendLine($"  <add_on_unique_id{i} type=\"hexBinary\" length=\"4\">00000000</add_on_unique_id{i}>");
        }

        sb.AppendLine("</menu>");
        return sb.ToString();
    }

    private static SkiaSharp.SKBitmap DecodeBitmap(string filePath, string assetLabel)
    {
        if (Path.GetExtension(filePath).Equals(".tga", StringComparison.OrdinalIgnoreCase))
        {
            return TgaReader.LoadTga(filePath);
        }

        return SkiaSharp.SKBitmap.Decode(filePath)
            ?? throw new InvalidOperationException($"Failed to decode {assetLabel} image from '{filePath}'. The file may be corrupt or an unsupported format.");
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> LaunchProgramAsync(
        string exeFile,
        string arguments = "",
        bool hideProcess = true,
        int[]? allowedExitCodes = null,
        string? workingDirectory = null,
        Action<string>? onOutputLine = null,
        CancellationToken cancellationToken = default)
    {
        string targetExe = exeFile;
        string targetArgs = arguments;

        Log($"Launching program: {exeFile} {arguments}");

        string fileName = Path.GetFileName(exeFile).ToLowerInvariant();
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
            if (fileName is "wit.exe" or "wit" or "wit-mac")
            {
                string localWitName = OperatingSystem.IsMacOS() ? "wit-mac" : "wit";
                string localWit = Path.Combine(Path.GetDirectoryName(exeFile) ?? string.Empty, localWitName);
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

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
            launcher.EnvironmentVariables["LANG"] = "C.UTF-8";
            launcher.EnvironmentVariables["LC_ALL"] = "C.UTF-8";
        }

        if (hideProcess)
        {
            launcher.WindowStyle = ProcessWindowStyle.Hidden;
        }

        using Process process = Process.Start(launcher)
            ?? throw new InvalidOperationException($"Failed to start process for {exeFile}");

        var stdOutBuilder = new StringBuilder();
        var stdErrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stdOutBuilder.AppendLine(e.Data);
                onOutputLine?.Invoke(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                stdErrBuilder.AppendLine(e.Data);
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore cleanup failure when killing child process
            }
            throw;
        }

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
        if (!validCodes.Contains(process.ExitCode))
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
            throw new InvalidOperationException(sbErr.ToString());
        }

        return (process.ExitCode, stdOut, stdErr);
    }

    private void ValidatePrerequisites()
    {
        // 1. Verify Wii U Common Key is provided and has valid 32-hex character length
        if (string.IsNullOrWhiteSpace(_options.WiiUCommonKey) || _options.WiiUCommonKey.Trim().Length != 32)
        {
            throw new InvalidOperationException(
                "Wii U Common Key is missing or invalid. It must be a 32-character hexadecimal key.");
        }

        // 2. Verify Base Game Title Key is provided and has valid 32-hex character length
        if (string.IsNullOrWhiteSpace(_options.TitleKey) || _options.TitleKey.Trim().Length != 32)
        {
            throw new InvalidOperationException(
                "Base Game Title Key is missing or invalid. It must be a 32-character hexadecimal key.");
        }

        // 3. Verify output directory is specified
        if (string.IsNullOrWhiteSpace(_options.SelectedOutputPath))
        {
            throw new InvalidOperationException("Output directory is not specified.");
        }

        // 4. On Linux/Unix/macOS, verify WIT (Wiimms ISO Tools) or Wine availability for disc image operations
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
            string localWitName = OperatingSystem.IsMacOS() ? "wit-mac" : "wit";
            string localWit = Path.Combine(_options.TempToolsPath, "WIT", localWitName);
            bool hasWit = File.Exists(localWit) || IsCommandAvailable("wit");
            bool hasWine = IsCommandAvailable("wine");

            if (!hasWit && !hasWine)
            {
                throw new InvalidOperationException(
                    "Neither 'wit' (Wiimms ISO Tools) nor 'wine' was found on your system.\n\n" +
                    (OperatingSystem.IsMacOS()
                        ? "To process Wii and GameCube disc images on macOS, please install 'wit' via Homebrew:\n  • brew install wit"
                        : "To process Wii and GameCube disc images on Linux, please install 'wit':\n" +
                          "  • Debian / Ubuntu: sudo apt install wit\n" +
                          "  • Arch Linux: sudo pacman -S wit\n" +
                          "  • Or install Wine: sudo apt install wine"));
            }
        }
    }

    private static bool IsCommandAvailable(string cmd)
    {
        if (File.Exists(cmd)) return true;

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        var searchDirs = new List<string>();
        if (!string.IsNullOrEmpty(pathEnv))
        {
            searchDirs.AddRange(pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        if (OperatingSystem.IsMacOS())
        {
            if (!searchDirs.Contains("/opt/homebrew/bin")) searchDirs.Add("/opt/homebrew/bin");
            if (!searchDirs.Contains("/usr/local/bin")) searchDirs.Add("/usr/local/bin");
        }

        string[] extensions = OperatingSystem.IsWindows()
            ? [".exe", ".cmd", ".bat", ""]
            : [""];

        foreach (string pathDir in searchDirs)
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

    private static string SanitizeFilename(string str)
    {
        var invalids = Path.GetInvalidFileNameChars();
        return string.Join("_", str.Split(invalids, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
    }

    private static bool IsNkitFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;
            using var fs = File.OpenRead(filePath);
            if (fs.Length < 0x204) return false;
            fs.Position = 0x200;
            Span<byte> idBytes = stackalloc byte[4];
            fs.ReadExactly(idBytes);
            return idBytes.SequenceEqual("NKIT"u8);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsNasosFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;
            using var fs = File.OpenRead(filePath);
            if (fs.Length < 4) return false;
            Span<byte> idBytes = stackalloc byte[4];
            fs.ReadExactly(idBytes);
            return idBytes.SequenceEqual("WII5"u8) ||
                   idBytes.SequenceEqual("WII9"u8) ||
                   idBytes.SequenceEqual("GCML"u8);
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> ConvertCompressedToIsoAsync(string sourcePath, CancellationToken cancellationToken)
    {
        UpdateStatus("Unscrubbing compressed disc image to ISO...", 20);

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var src = NKit.SourceFiles.OpenFile(sourcePath);
            var converter = new NKit.Converter(src, true);

            converter.LogMessage += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Message))
                {
                    Log($"[NKit] {e.Message}");
                }
            };

            converter.LogProgress += (_, e) =>
            {
                double prg = 20.0 + (Math.Clamp(e.Progress, 0.0, 1.0) * 15.0);
                UpdateStatus($"[NKit] {e.StartMessage ?? "Unscrubbing"} ({(int)(e.Progress * 100)}%)...", prg);
            };

            NKit.OutputResults results = converter.ConvertToIso();

            if (!string.IsNullOrEmpty(results.ErrorMessage))
            {
                throw new InvalidOperationException($"Disc image recovery failed: {results.ErrorMessage}");
            }

            if (!string.IsNullOrEmpty(results.OutputFileName) && File.Exists(results.OutputFileName))
            {
                string destFile = Path.Combine(_options.TempSourcePath, "recovered_" + Guid.NewGuid().ToString("N") + ".iso");
                File.Move(results.OutputFileName, destFile);
                Log($"Successfully recovered image {sourcePath} to {destFile}");
                return destFile;
            }

            string defaultOutDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Processed");
            if (Directory.Exists(defaultOutDir))
            {
                var files = Directory.GetFiles(defaultOutDir, "*.*", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    string sourceFile = files[0];
                    string destFile = Path.Combine(_options.TempSourcePath, "recovered_" + Guid.NewGuid().ToString("N") + ".iso");
                    File.Move(sourceFile, destFile);
                    Log($"Successfully recovered image {sourcePath} to {destFile}");
                    FileUtil.SafeDeleteDirectory(defaultOutDir);
                    return destFile;
                }
            }

            throw new FileNotFoundException("Failed to locate converted ISO file after NKit/NASOS processing.");
        }, cancellationToken);
    }
}
