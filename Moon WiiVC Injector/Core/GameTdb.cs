using System.Reflection;

namespace Moon_WiiVC_Injector;
public class GameTdb
{
    private const string ResourcePath = "Moon_WiiVC_Injector.Resources.wiitdb.txt";
    private static readonly Assembly CurrentAssembly = Assembly.GetExecutingAssembly();

    // Caches estáticos em memória
    private static readonly Dictionary<string, string> NameById = new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<string>> IdsByName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    private static readonly List<string> SortedIds = new List<string>();

    static GameTdb()
    {
        try
        {
            using var stream = CurrentAssembly.GetManifestResourceStream(ResourcePath);
            if (stream == null)
            {
                System.Diagnostics.Debug.WriteLine($"[GameTdb] Warning: Embedded resource '{ResourcePath}' not found.");
                return;
            }

            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 65536);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                ReadOnlySpan<char> span = line.AsSpan().Trim();
                if (span.IsEmpty || span.StartsWith("TITLES =".AsSpan(), StringComparison.Ordinal))
                    continue;

                int idx = span.IndexOf(" = ".AsSpan(), StringComparison.Ordinal);
                if (idx <= 0) continue;

                string id = span.Slice(0, idx).ToString();
                string name = span.Slice(idx + 3).ToString();

                NameById[id] = name;

                if (!IdsByName.TryGetValue(name, out var list))
                {
                    list = new List<string>(1);
                    IdsByName[name] = list;
                }
                list.Add(id);

                SortedIds.Add(id);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameTdb] Error loading database: {ex.Message}");
        }
    }

    public static string? GetName(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return NameById.TryGetValue(id, out var name) ? name : null;
    }

    public static List<string> GetIds(string name)
    {
        if (string.IsNullOrEmpty(name)) return new List<string>();
        return IdsByName.TryGetValue(name, out var ids) ? new List<string>(ids) : new List<string>();
    }

    public static List<string> GetIdsStartingWith(string idStart)
    {
        var ids = new List<string>();
        if (string.IsNullOrEmpty(idStart)) return ids;

        var idStartSpan = idStart.AsSpan();

        foreach (var id in SortedIds)
        {
            if (id.StartsWith(idStart, StringComparison.Ordinal))
            {
                ids.Add(id);
            }
            else if (id.Length >= idStart.Length && idStartSpan.CompareTo(id.AsSpan(0, idStart.Length), StringComparison.Ordinal) < 0)
            {
                break;
            }
        }
        return ids;
    }

    internal static IEnumerable<string> GetAlternativeIds(string initialId)
    {
        if (string.IsNullOrEmpty(initialId) || initialId.Length < 4)
        {
            if (!string.IsNullOrEmpty(initialId)) yield return initialId;
            yield break;
        }

        var tried = new HashSet<string>
        {
            initialId,
            initialId.ReplaceAt(3, 'E'),
            initialId.ReplaceAt(3, 'P'),
        };

        foreach (var id in tried)
        {
            yield return id;
        }

        var gameName = GetName(initialId);
        if (!string.IsNullOrEmpty(gameName))
        {
            var ids = GetIds(gameName).Where(id => !tried.Contains(id));

            foreach (var id in ids)
            {
                yield return id;
                tried.Add(id);
            }
        }

        // as last resort, try a match on only the 3 first characters of
        // the key (e.g. for Obscure 2)
        var moreIds = GetIdsStartingWith(initialId.Substring(0, 3))
            .Where(id => !tried.Contains(id));

        foreach (var id in moreIds)
        {
            yield return id;
        }
    }
}

internal static class StringExtensions
{
    public static string ReplaceAt(this string input, int index, char newChar)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        char[] chars = input.ToCharArray();
        chars[index] = newChar;
        return new string(chars);
    }
}
