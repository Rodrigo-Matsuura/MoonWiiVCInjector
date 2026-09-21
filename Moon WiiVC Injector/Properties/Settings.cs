using System.Text.Json;

namespace Moon_WiiVC_Injector.Properties;

public sealed class Settings
{
    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Moon WiiVC Injector"
    );
    private static readonly string SettingsPath = Path.Combine(SettingsFolder, "settings.json");

    private static Settings? _defaultInstance;
    private static readonly Lock LockObj = new();

    public static Settings Default
    {
        get
        {
            if (_defaultInstance == null)
            {
                lock (LockObj)
                {
                    _defaultInstance ??= Load();
                }
            }
            return _defaultInstance;
        }
    }

    public string OutputPath { get; set; } = string.Empty;
    public string OutputPathFixed { get; set; } = string.Empty;
    public string TempPath { get; set; } = string.Empty;
    public string WiiUCommonKey { get; set; } = string.Empty;
    public string TitleKey { get; set; } = string.Empty;
    public string BaseRegion { get; set; } = "USA";
    public string TitleKeyUSA { get; set; } = string.Empty;
    public string TitleKeyEUR { get; set; } = string.Empty;
    public string AncastKey { get; set; } = string.Empty;
    public string BannersRepository { get; set; } = "https://raw.githubusercontent.com/UWUVCI-PRIME/UWUVCI-IMAGES/master/";

    public string GetTitleKeyForRegion(string region)
    {
        return region.ToUpperInvariant() switch
        {
            "EUR" => !string.IsNullOrEmpty(TitleKeyEUR) ? TitleKeyEUR : (string.Equals(BaseRegion, "EUR", StringComparison.OrdinalIgnoreCase) ? TitleKey : string.Empty),
            _ => !string.IsNullOrEmpty(TitleKeyUSA) ? TitleKeyUSA : TitleKey
        };
    }

    public void SetTitleKeyForRegion(string region, string key)
    {
        switch (region.ToUpperInvariant())
        {
            case "EUR":
                TitleKeyEUR = key;
                break;
            default:
                TitleKeyUSA = key;
                break;
        }

        if (string.Equals(BaseRegion, region, StringComparison.OrdinalIgnoreCase))
        {
            TitleKey = key;
        }
    }

    public static string GetDefaultTempPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Path.GetTempPath(), "Moon WiiVC Injector");
        }

        // On Linux / macOS, /tmp is typically a tmpfs (in-memory RAM disk with strict quota).
        // Use XDG cache directory which resides on physical persistent storage.
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            return Path.Combine(userProfile, ".cache", "Moon WiiVC Injector", "temp");
        }

        // Fallback to /var/tmp which is on physical disk on Linux, unlike /tmp
        return Path.Combine("/var", "tmp", "Moon WiiVC Injector");
    }

    public string GetEffectiveTempPath()
    {
        if (!string.IsNullOrWhiteSpace(TempPath))
        {
            return TempPath;
        }

        return GetDefaultTempPath();
    }

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
