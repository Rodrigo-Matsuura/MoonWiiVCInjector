using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace Moon_WiiVC_Injector
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
                using (var request = new HttpRequestMessage(HttpMethod.Get, "http://clients3.google.com/generate_204"))
                using (var response = Client.Send(request))
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
}
