using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Moon_WiiVC_Injector.Services;

public class AvaloniaDialogService(Func<Window?> getActiveWindow) : IDialogService
{
    private readonly Func<Window?> _getActiveWindow = getActiveWindow;

    private Window GetRequiredWindow()
    {
        return _getActiveWindow() ?? throw new InvalidOperationException("Active window is not available.");
    }

    public async Task<MessageBoxResult> ShowMessageAsync(string message, string title, MessageBoxButtons buttons = MessageBoxButtons.Ok)
    {
        var window = GetRequiredWindow();
        return await MessageBoxWindow.Show(window, message, title, buttons);
    }

    public async Task<string?> PromptAsync(string message, string title, string defaultValue = "")
    {
        var window = GetRequiredWindow();
        var prompt = new PromptWindow(message, title, defaultValue);
        var isOk = await prompt.ShowDialog<bool>(window);
        return isOk ? prompt.Result : null;
    }

    public async Task<string?> OpenFileDialogAsync(string title, FilePickerFileType[]? filters = null)
    {
        var window = GetRequiredWindow();
        var storage = TopLevel.GetTopLevel(window)?.StorageProvider;
        if (storage == null) return null;

        var result = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = filters
        });

        return (result != null && result.Count > 0) ? result[0].Path.LocalPath : null;
    }

    public async Task<string?> OpenFolderDialogAsync(string title)
    {
        var window = GetRequiredWindow();
        var storage = TopLevel.GetTopLevel(window)?.StorageProvider;
        if (storage == null) return null;

        var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return (result != null && result.Count > 0) ? result[0].Path.LocalPath : null;
    }

    public async Task ShowSettingsDialogAsync()
    {
        var window = GetRequiredWindow();
        var settingsWin = new SettingsWindow();
        await settingsWin.ShowDialog(window);
    }

    public async Task ShowSdCardMenuDialogAsync()
    {
        var window = GetRequiredWindow();
        var sdMenu = new SdCardMenuAvalonia();
        await sdMenu.ShowDialog(window);
    }

    public async Task SetClipboardTextAsync(string text)
    {
        var window = GetRequiredWindow();
        var clipboard = TopLevel.GetTopLevel(window)?.Clipboard;
        if (clipboard != null && !string.IsNullOrEmpty(text))
        {
            var dataTransfer = new DataTransfer();
            dataTransfer.Add(DataTransferItem.CreateText(text));
            await clipboard.SetDataAsync(dataTransfer);
        }
    }

    public void OpenPathWithDefaultApp(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AvaloniaDialogService] Failed to open path '{path}': {ex.Message}");
        }
    }
}
