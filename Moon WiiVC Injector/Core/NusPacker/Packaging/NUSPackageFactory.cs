// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;
using System.IO;
using System.Threading;
using Moon_WiiVC_Injector.Core.NusPacker.Contents;
using Moon_WiiVC_Injector.Core.NusPacker.Fst;

namespace Moon_WiiVC_Injector.Core.NusPacker.Packaging;

public static class NUSPackageFactory
{
    public static NUSpackage CreateNewPackage(NusPackageConfiguration config, Action<string, double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        onProgress?.Invoke("Scanning files and building file table...", 0.0);

        Contents.Contents contents = new Contents.Contents();
        FST fst = new FST(contents);

        FSTEntry root = fst.fileEntries.GetRootEntry();
        root.SetContent(contents.fstContent);

        PopulateFSTEntries(config.dir, root, cancellationToken);

        onProgress?.Invoke("Applying Wii U content packaging rules...", 0.1);
        ContentRulesService.ApplyRules(root, contents, config.rules);

        onProgress?.Invoke("Generating file system table (FST)...", 0.15);
        fst.Update();

        onProgress?.Invoke("Generating ticket...", 0.18);
        Ticket ticket = new Ticket(config.appInfo.titleID, config.encryptionKey, config.encryptKeyWith);

        onProgress?.Invoke("Generating title metadata (TMD)...", 0.2);
        TMD tmd = new TMD(config.appInfo, fst, ticket);

        return new NUSpackage(fst, ticket, tmd, config.TempDirectory);
    }

    private static void PopulateFSTEntries(string directory, FSTEntry parent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (string file in Directory.EnumerateFiles(directory))
        {
            FSTEntry newFile = new FSTEntry(file);
            parent.AddChildren(newFile);
        }

        foreach (string dir in Directory.EnumerateDirectories(directory))
        {
            FSTEntry newDir = new FSTEntry(dir);
            parent.AddChildren(newDir);
            PopulateFSTEntries(dir, newDir, cancellationToken);
        }
    }
}

