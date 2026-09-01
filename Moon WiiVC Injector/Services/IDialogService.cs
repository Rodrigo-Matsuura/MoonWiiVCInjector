using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace Moon_WiiVC_Injector.Services;

public interface IDialogService
{
    Task<MessageBoxResult> ShowMessageAsync(string message, string title, MessageBoxButtons buttons = MessageBoxButtons.Ok);
    Task<string?> PromptAsync(string message, string title, string defaultValue = "");
    Task<string?> OpenFileDialogAsync(string title, FilePickerFileType[]? filters = null);
    Task<string?> OpenFolderDialogAsync(string title);
    Task ShowSettingsDialogAsync();
    Task ShowSdCardMenuDialogAsync();
    Task SetClipboardTextAsync(string text);
    void OpenPathWithDefaultApp(string path);
}
