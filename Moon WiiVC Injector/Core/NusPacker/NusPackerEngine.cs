// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)
// Adapted and modernized for MoonWiiVCInjector (.NET 9, Avalonia UI, async/await, cancellation)

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moon_WiiVC_Injector.Core.NusPacker.Crypto;
using Moon_WiiVC_Injector.Core.NusPacker.Packaging;
using Moon_WiiVC_Injector.Core.NusPacker.Utils;

namespace Moon_WiiVC_Injector.Core.NusPacker;

public static class NusPackerEngine
{
    /// <summary>
    /// Packs the given decrypted code, content, and meta folders into an installable Wii U WUP package.
    /// </summary>
    /// <param name="inputPath">Root directory containing code, content, and meta folders.</param>
    /// <param name="outputPath">Target directory where .app, title.tmd, title.tik, etc. will be written.</param>
    /// <param name="commonKey">The 32-character hexadecimal Wii U Common Key used to encrypt the title key.</param>
    /// <param name="encryptKey">Optional 32-character hexadecimal encryption key for content (defaults to 13371337133713371337133713371337).</param>
    /// <param name="tempDirectory">Optional staging folder for intermediate unencrypted/decrypted blocks.</param>
    /// <param name="progress">Progress reporter for UI status and percentages.</param>
    /// <param name="cancellationToken">Cancellation token to abort packaging.</param>
    public static async Task PackAsync(
        string inputPath,
        string outputPath,
        string commonKey,
        string? encryptKey = null,
        string? tempDirectory = null,
        IProgress<(string Status, double Percentage)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            string cleanInput = Path.GetFullPath(inputPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string cleanOutput = Path.GetFullPath(outputPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            string codeDir = Path.Combine(cleanInput, "code");
            string contentDir = Path.Combine(cleanInput, "content");
            string metaDir = Path.Combine(cleanInput, "meta");

            if (!Directory.Exists(codeDir) || !Directory.Exists(contentDir) || !Directory.Exists(metaDir))
            {
                throw new DirectoryNotFoundException(
                    $"Invalid input directory ({cleanInput}): missing code, content, or meta directory.");
            }

            string appXmlPath = Path.Combine(cleanInput, Settings.pathToAppXml);
            if (!File.Exists(appXmlPath))
            {
                throw new FileNotFoundException($"app.xml was not found at '{appXmlPath}'.");
            }

            progress?.Report(("Parsing app.xml...", 0.02));
            var xmlParser = new XMLParser();
            xmlParser.LoadDocument(appXmlPath);
            AppXMLInfo appInfo = xmlParser.GetAppXMLInfo();

            short contentGroup = appInfo.groupID;
            long titleID = appInfo.titleID;
            long parentID = titleID & ~0x0000000F00000000L;

            string encKey = string.IsNullOrWhiteSpace(encryptKey) || encryptKey.Trim().Length != 32
                ? Settings.defaultEncryptionKey
                : encryptKey.Trim();

            string encKeyWith = string.IsNullOrWhiteSpace(commonKey) || commonKey.Trim().Length != 32
                ? Settings.defaultEncryptWithKey
                : commonKey.Trim();

            string stagingTemp = string.IsNullOrWhiteSpace(tempDirectory)
                ? Path.Combine(Path.GetTempPath(), "MoonWiiVC_NusPacker_" + Guid.NewGuid().ToString("N"))
                : tempDirectory;

            Directory.CreateDirectory(stagingTemp);
            Directory.CreateDirectory(cleanOutput);

            List<ContentRule> rules = ContentRule.GetCommonRules(contentGroup, parentID);

            var config = new NusPackageConfiguration(
                cleanInput,
                appInfo,
                new Key(encKey),
                new Key(encKeyWith),
                rules,
                stagingTemp);

            void ReportSubProgress(string status, double percent)
            {
                progress?.Report((status, percent));
            }

            try
            {
                NUSpackage package = NUSPackageFactory.CreateNewPackage(config, ReportSubProgress, cancellationToken);
                package.PackContents(cleanOutput, ReportSubProgress, cancellationToken);
            }
            finally
            {
                try
                {
                    NusUtils.DeleteDir(stagingTemp);
                }
                catch
                {
                    // Ignore temp cleanup error
                }
            }
        }, cancellationToken);
    }
}
