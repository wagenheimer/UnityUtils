using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Paths the cleanup suite must leave alone, kept in <c>ProjectSettings/WagenheimerCleanupIgnore.txt</c> so the
    /// whole team shares them through version control. One entry per line: <c>scope|Assets/Some/Path</c>.
    /// A folder entry ignores everything under it. Scopes: <c>textures</c>, <c>samples</c>.
    /// </summary>
    public static class CleanupIgnoreList
    {
        public const string Textures = "textures";
        public const string Samples = "samples";

        private const string FilePath = "ProjectSettings/WagenheimerCleanupIgnore.txt";

        private static List<KeyValuePair<string, string>> _entries;
        private static DateTime _loadedAt;

        public static string AbsolutePath { get { return Path.GetFullPath(FilePath); } }

        private static List<KeyValuePair<string, string>> Entries()
        {
            var stamp = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
            if (_entries != null && stamp == _loadedAt) return _entries;

            _entries = new List<KeyValuePair<string, string>>();
            _loadedAt = stamp;

            if (!File.Exists(FilePath)) return _entries;

            foreach (var raw in File.ReadAllLines(FilePath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                var bar = line.IndexOf('|');
                if (bar <= 0) continue;

                _entries.Add(new KeyValuePair<string, string>(line.Substring(0, bar).Trim().ToLowerInvariant(),
                    Normalize(line.Substring(bar + 1))));
            }

            return _entries;
        }

        public static bool IsIgnored(string scope, string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;

            var path = Normalize(assetPath);
            foreach (var entry in Entries())
            {
                if (entry.Key != scope && entry.Key != "*") continue;
                if (path.Equals(entry.Value, StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(entry.Value + "/", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static IReadOnlyList<string> List(string scope)
        {
            return Entries().Where(e => e.Key == scope).Select(e => e.Value).ToList();
        }

        public static void Add(string scope, string assetPath)
        {
            var path = Normalize(assetPath);
            if (string.IsNullOrEmpty(path) || IsIgnored(scope, path)) return;

            var line = scope + "|" + path;
            try
            {
                if (!File.Exists(FilePath))
                    File.WriteAllText(FilePath, "# Paths the UnityUtils Project Cleanup leaves alone. Format: scope|path (a folder covers everything under it)." + Environment.NewLine);

                File.AppendAllText(FilePath, line + Environment.NewLine);
                _entries = null;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Cleanup] Could not save the ignore list: {ex.Message}");
            }
        }

        /// <summary>Opens the ignore file in the default editor (created when missing).</summary>
        public static void OpenFile()
        {
            if (!File.Exists(FilePath))
                File.WriteAllText(FilePath, "# Paths the UnityUtils Project Cleanup leaves alone. Format: scope|path (a folder covers everything under it)." + Environment.NewLine +
                                            "# Example: textures|Assets/_Game/Art/Atlased" + Environment.NewLine);

            EditorUtility.OpenWithDefaultApp(AbsolutePath);
        }

        private static string Normalize(string path)
        {
            return (path ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');
        }
    }
}
