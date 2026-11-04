// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)
// Adapted and modernized for MoonWiiVCInjector (.NET 9, Avalonia UI, async/await, cancellation)

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moon_WiiVC_Injector.Core.NusPacker.FileSystem;
using Moon_WiiVC_Injector.Core.NusPacker.Metadata;
using Moon_WiiVC_Injector.Core.NusPacker.Packaging;
using Moon_WiiVC_Injector.Core.NusPacker.Utils;

namespace Moon_WiiVC_Injector.Core.NusPacker;

public static class NusPackerSettings
{
    public const short GroupIdCode = 0x0000;
    public const short GroupIdMeta = 0x0400;

    public const short FstFlagsCode = 0x0000;
    public const short FstFlagsMeta = 0x0040;
    public const short FstFlagsContent = 0x0400;

    public const string DefaultEncryptionKey = "13371337133713371337133713371337";
    public const string DefaultEncryptWithKey = "00000000000000000000000000000000";

    public static readonly string PathToAppXml = Path.Combine("code", "app.xml");
}

public static class NusPackerEngine
{
    /// <summary>
    /// Packs the given decrypted code, content, and meta folders into an installable Wii U WUP package.
    /// </summary>
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

            string appXmlPath = Path.Combine(cleanInput, NusPackerSettings.PathToAppXml);
            if (!File.Exists(appXmlPath))
            {
                throw new FileNotFoundException($"app.xml was not found at '{appXmlPath}'.");
            }

            progress?.Report(("Parsing app.xml...", 0.02));
            AppXmlInfo appInfo = AppXmlParser.Parse(appXmlPath);

            short contentGroup = appInfo.GroupId;
            long titleId = appInfo.TitleId;
            long parentId = titleId & ~0x0000000F00000000L;

            string encKey = string.IsNullOrWhiteSpace(encryptKey) || encryptKey.Trim().Length != 32
                ? NusPackerSettings.DefaultEncryptionKey
                : encryptKey.Trim();

            string encKeyWith = string.IsNullOrWhiteSpace(commonKey) || commonKey.Trim().Length != 32
                ? NusPackerSettings.DefaultEncryptWithKey
                : commonKey.Trim();

            string stagingTemp = string.IsNullOrWhiteSpace(tempDirectory)
                ? Path.Combine(Path.GetTempPath(), "MoonWiiVC_NusPacker_" + Guid.NewGuid().ToString("N"))
                : tempDirectory;

            Directory.CreateDirectory(stagingTemp);
            Directory.CreateDirectory(cleanOutput);

            void ReportSubProgress(string status, double percent)
            {
                progress?.Report((status, percent));
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReportSubProgress("Scanning files and building file table...", 0.0);

                NusContentsCollection contents = new();
                FstTable fst = new(contents);

                FstEntry root = fst.FileEntries.GetRootEntry();
                root.SetContent(contents.FstContent);

                PopulateFstEntries(cleanInput, root, cancellationToken);

                ReportSubProgress("Applying Wii U content packaging rules...", 0.1);
                List<ContentRule> rules = ContentRule.GetCommonRules(contentGroup, parentId);
                ContentRulesService.ApplyRules(root, contents, rules);

                ReportSubProgress("Generating file system table (FST)...", 0.15);
                fst.Update();

                ReportSubProgress("Generating ticket...", 0.18);
                TitleTicket ticket = new(appInfo.TitleId, Convert.FromHexString(encKey), Convert.FromHexString(encKeyWith));

                ReportSubProgress("Generating title metadata (TMD)...", 0.2);
                TitleTmd tmd = new(appInfo, fst, ticket);

                NusPackage package = new(fst, ticket, tmd, stagingTemp);
                package.Pack(cleanOutput, ReportSubProgress, cancellationToken);
            }
            catch
            {
                if (Directory.Exists(cleanOutput))
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(cleanOutput).Any())
                        {
                            Directory.Delete(cleanOutput, true);
                        }
                    }
                    catch
                    {
                        // Ignore cleanup error
                    }
                }
                throw;
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

    private static void PopulateFstEntries(string directory, FstEntry parent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (string file in Directory.EnumerateFiles(directory))
        {
            FstEntry newFile = new(file);
            parent.AddChild(newFile);
        }

        foreach (string dir in Directory.EnumerateDirectories(directory))
        {
            FstEntry newDir = new(dir);
            parent.AddChild(newDir);
            PopulateFstEntries(dir, newDir, cancellationToken);
        }
    }
}

