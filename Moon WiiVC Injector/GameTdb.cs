using System.Reflection;

namespace Moon_WiiVC_Injector
{
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
                using (var stream = CurrentAssembly.GetManifestResourceStream(ResourcePath))
                {
                    if (stream == null) return;
                    using (var reader = new StreamReader(stream))
                    {
                        string? line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (line.StartsWith("TITLES =", StringComparison.Ordinal))
                                continue;

                            int idx = line.IndexOf(" = ", StringComparison.Ordinal);
                            if (idx < 0) continue;

                            string id = line.Substring(0, idx);
                            string name = line.Substring(idx + 3);

                            NameById[id] = name;

                            if (!IdsByName.TryGetValue(name, out var list))
                            {
                                list = new List<string>();
                                IdsByName[name] = list;
                            }
                            list.Add(id);

                            SortedIds.Add(id);
                        }
                    }
                }
            }
            catch
            {
                // Fallback silencioso em caso de falha de carregamento
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
}
