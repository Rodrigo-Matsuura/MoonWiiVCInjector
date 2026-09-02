using System;
using System.IO;

namespace NKit;

public enum NkitFormatType { Iso, Gcz }

public class Settings
{
    public string Path { get; set; }
    public string TempPath { get; set; }
    public bool EnableSummaryLog { get; set; }
    public string SummaryLog { get; set; }
    public bool FullVerify { get; set; }
    public bool CalculateHashes { get; set; }
    public bool DeleteSource { get; set; }
    public int OutputLevel { get; set; } = 1;
    public bool TestMode { get; set; }
    public bool MaskRename { get; set; }
    public NkitFormatType NkitFormat { get; set; } = NkitFormatType.Iso;
    public bool NkitReencode { get; set; }
    public bool NkitUpdatePartitionRemoval { get; set; }

    public DiscType DiscType { get; }

    public Settings(DiscType type) : this(type, null, true)
    {
    }

    public Settings(DiscType type, string overridePath, bool createPaths)
    {
        DiscType = type;
        Path = overridePath ?? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Processed");
        TempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NKit");

        if (createPaths)
        {
            try
            {
                if (!Directory.Exists(TempPath)) Directory.CreateDirectory(TempPath);
            }
            catch { }
        }
    }
}
