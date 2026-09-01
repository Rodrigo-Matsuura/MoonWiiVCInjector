using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Moon_WiiVC_Injector.Services;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public static class AppLogger
{
    private static readonly ConcurrentQueue<string> LogHistory = new();
    private static Action<string>? _logListener;

    public static void SetListener(Action<string>? listener)
    {
        _logListener = listener;
    }

    public static void Log(string message, LogLevel level = LogLevel.Info)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        string tag = level switch
        {
            LogLevel.Debug => "[DEBUG]",
            LogLevel.Warning => "[WARN] ",
            LogLevel.Error => "[ERROR]",
            _ => "[INFO] "
        };

        string formatted = $"[{timestamp}] {tag} {message}";
        LogHistory.Enqueue(formatted);

        // Keep last 5000 lines in memory
        while (LogHistory.Count > 5000)
        {
            LogHistory.TryDequeue(out _);
        }

        Debug.WriteLine(formatted);
        _logListener?.Invoke(formatted);
    }

    public static void Info(string message) => Log(message, LogLevel.Info);
    public static void Warning(string message) => Log(message, LogLevel.Warning);
    public static void Error(string message, Exception? ex = null)
    {
        string fullMessage = ex != null ? $"{message} | Details: {ex.Message}" : message;
        Log(fullMessage, LogLevel.Error);
        if (ex != null && !string.IsNullOrEmpty(ex.StackTrace))
        {
            Log($"[StackTrace] {ex.StackTrace}", LogLevel.Debug);
        }
    }
    public static void DebugLog(string message) => Log(message, LogLevel.Debug);

    public static async Task SaveToFileAsync(string filePath)
    {
        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            await File.WriteAllLinesAsync(filePath, LogHistory);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppLogger] Failed to save log file '{filePath}': {ex.Message}");
        }
    }

    public static void Clear()
    {
        LogHistory.Clear();
    }
}
