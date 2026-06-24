using Avalonia;

namespace Moon_WiiVC_Injector;
static class Program
{
    public static readonly HttpClient Client = new HttpClient();

    [STAThread]
    static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    public static async Task<bool> CheckForInternetConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using (var response = await Client.GetAsync("http://clients3.google.com/generate_204", HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                return response.IsSuccessStatusCode;
            }
        }
        catch
        {
            return false;
        }
    }
}
