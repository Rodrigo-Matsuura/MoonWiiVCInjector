using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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

    public static async Task CopyDirectoryAsync(string sourceDir, string destinationDir, CancellationToken cancellationToken = default)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;

        Directory.CreateDirectory(destinationDir);

        foreach (FileInfo file in dir.GetFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destFile = Path.Combine(destinationDir, file.Name);

            const int bufferSize = 128 * 1024; // 128 KB buffer
            await using var sourceStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var destStream = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous);
            await sourceStream.CopyToAsync(destStream, bufferSize, cancellationToken);
        }

        foreach (DirectoryInfo subDir in dir.GetDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CopyDirectoryAsync(subDir.FullName, Path.Combine(destinationDir, subDir.Name), cancellationToken);
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
            Debug.WriteLine($"[FileUtil] Warning: Failed to delete directory '{path}': {ex.Message}");
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
            Debug.WriteLine($"[FileUtil] Warning: Failed to delete file '{path}': {ex.Message}");
        }
    }
}
