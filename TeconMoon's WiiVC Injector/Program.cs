using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace TeconMoon_s_WiiVC_Injector
{
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

        public static bool CheckForInternetConnection()
        {
            try
            {
                var response = Client.GetAsync("http://clients3.google.com/generate_204").Result;
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
