using System;
using System.IO;
using System.Reflection;
using Moon_WiiVC_Injector.Services;

namespace Moon_WiiVC_Injector.Core;

public static class BaseIsoTemplate
{
    private static readonly Assembly CurrentAssembly = typeof(BaseIsoTemplate).Assembly;
    private const string ResourcePrefix = "Moon_WiiVC_Injector.Resources";

    private static readonly (string ResourceName, string RelativePath)[] BaseFiles =
    [
        ($"{ResourcePrefix}.Base.disc.header.bin", Path.Combine("disc", "header.bin")),
        ($"{ResourcePrefix}.Base.disc.region.bin", Path.Combine("disc", "region.bin")),
        ($"{ResourcePrefix}.Base.setup.txt", "setup.txt"),
        ($"{ResourcePrefix}.Base.sys.apploader.img", Path.Combine("sys", "apploader.img")),
        ($"{ResourcePrefix}.Base.sys.bi2.bin", Path.Combine("sys", "bi2.bin")),
        ($"{ResourcePrefix}.Base.sys.boot.bin", Path.Combine("sys", "boot.bin"))
    ];

    public static void CreateBaseDirectory(string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        Directory.CreateDirectory(Path.Combine(destinationDir, "disc"));
        Directory.CreateDirectory(Path.Combine(destinationDir, "sys"));
        Directory.CreateDirectory(Path.Combine(destinationDir, "files"));

        foreach (var (resourceName, relativePath) in BaseFiles)
        {
            string targetPath = Path.Combine(destinationDir, relativePath);
            ExtractResource(resourceName, targetPath);
        }
    }

    public static void CopyNintendontDol(string destinationFilePath, bool disableAutoboot = false)
    {
        string resourceName = disableAutoboot
            ? $"{ResourcePrefix}.Dol.nintendont_forwarder.dol"
            : $"{ResourcePrefix}.Dol.nintendont_default_autobooter.dol";

        string? dir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        ExtractResource(resourceName, destinationFilePath);
    }

    private static void ExtractResource(string resourceName, string targetPath)
    {
        using var stream = CurrentAssembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            AppLogger.Error($"[BaseIsoTemplate] Embedded resource '{resourceName}' not found!");
            throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
        }

        using var fileStream = File.Create(targetPath);
        stream.CopyTo(fileStream);
    }
}
