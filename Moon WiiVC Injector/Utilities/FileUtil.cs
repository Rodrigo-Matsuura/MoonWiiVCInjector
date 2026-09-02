using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moon_WiiVC_Injector.Services;

namespace Moon_WiiVC_Injector;

public static class FileUtil
{
    public static void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;

        Directory.CreateDirectory(destinationDir);

        foreach (FileInfo file in dir.GetFiles())
        {
            file.CopyTo(Path.Combine(destinationDir, file.Name), true);
        }

        foreach (DirectoryInfo subDir in dir.GetDirectories())
        {
            CopyDirectory(subDir.FullName, Path.Combine(destinationDir, subDir.Name));
        }
    }

    public static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"[FileUtil] Failed to delete directory '{path}': {ex.Message}");
        }
    }

    public static void SafeDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"[FileUtil] Failed to delete file '{path}': {ex.Message}");
        }
    }
}
