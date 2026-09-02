using System.IO;
using System.Text.RegularExpressions;

namespace NKit;

public static partial class SourceFiles
{
    [GeneratedRegex(@"\.(nkit\.gcz|nkit\.iso|iso\.dec|part[0-9]\.rar|zip\.[0-9]+)(:?_[0-9]*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex NkitExtensionRegex();

    public static SourceFile OpenFile(string filePath)
    {
        return new SourceFile
        {
            Name = Path.GetFileName(filePath),
            Path = Path.GetDirectoryName(filePath),
            FilePath = filePath,
            AllFiles = [filePath],
            IsSplit = false,
            Length = new FileInfo(filePath).Length
        };
    }

    public static string CleanseFileName(string name)
    {
        if (name == null)
            return null;
        string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);

        return Regex.Replace(name, invalidRegStr, " ");
    }

    public static string ExtensionString(bool isIsoDec, bool isWbfs, bool isNkit, bool isGcz)
    {
        if (isIsoDec)
            return "ISO.Dec";
        if (isWbfs)
            return "WBFS";
        if (isNkit)
            return isGcz ? "NKit.GCZ" : "NKit.ISO";
        return isGcz ? "GCZ" : "ISO";
    }

    public static string RemoveExtension(string filename, bool filenameOnly, out string extension)
    {
        if (filenameOnly)
            filename = Path.GetFileName(filename);

        Match m = NkitExtensionRegex().Match(filename);
        if (m.Success)
            extension = m.Value;
        else
            extension = Path.GetExtension(filename);

        return filename[..^extension.Length];
    }

    public static string RemoveExtension(string filename, bool filenameOnly)
    {
        return RemoveExtension(filename, filenameOnly, out _);
    }

    public static string ChangeExtension(string filename, bool filenameOnly, string newExtension)
    {
        return $"{RemoveExtension(filename, filenameOnly, out _)}.{newExtension.TrimStart('.')}";
    }

    public static string GetUniqueName(string fullName)
    {
        string tmp = fullName;
        int i = 1;
        while (File.Exists(fullName))
            fullName = $"{tmp}_{i++}";
        return fullName;
    }
}
