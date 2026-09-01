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

    public static async Task CopyDirectoryAsync(string sourceDir, string destinationDir, CancellationToken cancellationToken = default)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;

        Directory.CreateDirectory(destinationDir);

        const int bufferSize = 128 * 1024; // 128 KB buffer
        byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

        try
        {
            foreach (FileInfo file in dir.GetFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destFile = Path.Combine(destinationDir, file.Name);

                await using var sourceStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var destStream = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous);

                int bytesRead;
                while ((bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, bufferSize), cancellationToken)) > 0)
                {
                    await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
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
