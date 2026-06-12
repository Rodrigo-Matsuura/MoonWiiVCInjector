using System;
using System.IO;
using System.Text.Json;

namespace Moon_WiiVC_Injector.Properties
{
    internal sealed class Settings
    {
        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Moon WiiVC Injector"
        );
        private static readonly string SettingsPath = Path.Combine(SettingsFolder, "settings.json");

        private static Settings? _defaultInstance;
        private static readonly object LockObj = new object();

        public static Settings Default
        {
            get
            {
                if (_defaultInstance == null)
                {
                    lock (LockObj)
                    {
                        if (_defaultInstance == null)
                        {
                            _defaultInstance = Load();
                        }
                    }
                }
                return _defaultInstance;
            }
        }

        public string OutputPath { get; set; } = string.Empty;
        public string GameFilePath { get; set; } = string.Empty;
        public string OutputPathFixed { get; set; } = string.Empty;
        public string WiiUCommonKey { get; set; } = string.Empty;
        public string TitleKey { get; set; } = string.Empty;
        public string AncastKey { get; set; } = string.Empty;
        public string BannersRepository { get; set; } = "https://raw.githubusercontent.com/UWUVCI-PRIME/UWUVCI-IMAGES/master/";

        public void Save()
        {
            try
            {
                if (!Directory.Exists(SettingsFolder))
                {
                    Directory.CreateDirectory(SettingsFolder);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        private static Settings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<Settings>(json);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading settings: {ex.Message}");
            }

            return new Settings();
        }
    }
}
