using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Moon_WiiVC_Injector.Services;

namespace Moon_WiiVC_Injector;

public class GameTdb
{
    private const string ResourcePath = "Moon_WiiVC_Injector.Resources.wiitdb.txt";
    private static readonly Assembly CurrentAssembly = Assembly.GetExecutingAssembly();

    // Caches estáticos em memória
    private static readonly Dictionary<string, string> NameById = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<string>> IdsByName = new(StringComparer.Ordinal);
    private static readonly List<string> SortedIds = [];

    static GameTdb()
    {
        try
        {
            using var stream = CurrentAssembly.GetManifestResourceStream(ResourcePath);
            if (stream == null)
            {
                AppLogger.Warning($"[GameTdb] Embedded resource '{ResourcePath}' not found.");
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

                string id = span[..idx].ToString();
                string name = span[(idx + 3)..].ToString();

                NameById[id] = name;

                if (!IdsByName.TryGetValue(name, out var list))
                {
                    list = new List<string>(1);
                    IdsByName[name] = list;
                }
                list.Add(id);

                SortedIds.Add(id);
            }

            // Ensure the list is sorted for binary search
            SortedIds.Sort(StringComparer.Ordinal);
            AppLogger.DebugLog($"[GameTdb] Database loaded successfully with {SortedIds.Count} entries.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("[GameTdb] Error loading database", ex);
        }
    }

    public static string? GetName(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return NameById.TryGetValue(id, out var name) ? name : null;
    }

    public static List<string> GetIds(string name)
    {
        if (string.IsNullOrEmpty(name)) return [];
        return IdsByName.TryGetValue(name, out var ids) ? [.. ids] : [];
    }

    public static List<string> GetIdsStartingWith(string idStart)
    {
        var ids = new List<string>();
        if (string.IsNullOrEmpty(idStart)) return ids;

        // O(log N) Binary Search to locate starting prefix
        int index = SortedIds.BinarySearch(idStart, StringComparer.Ordinal);
        if (index < 0)
        {
            index = ~index;
        }

        while (index < SortedIds.Count && SortedIds[index].StartsWith(idStart, StringComparison.Ordinal))
        {
            ids.Add(SortedIds[index]);
            index++;
        }

        return ids;
    }

    public static IEnumerable<string> GetAlternativeIds(string initialId)
    {
        if (string.IsNullOrEmpty(initialId) || initialId.Length < 4)
        {
            if (!string.IsNullOrEmpty(initialId)) yield return initialId;
            yield break;
        }

        var tried = new HashSet<string>(StringComparer.Ordinal)
        {
            initialId,
            initialId.ReplaceAt(3, 'E'),
            initialId.ReplaceAt(3, 'P')
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

        // as last resort, try a match on only the 3 first characters of the key
        var moreIds = GetIdsStartingWith(initialId[..3])
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
        if (index < 0 || index >= input.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        return string.Create(input.Length, (input, index, newChar), static (span, state) =>
        {
            state.input.AsSpan().CopyTo(span);
            span[state.index] = state.newChar;
        });
    }
}
