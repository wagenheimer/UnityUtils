using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Finds weight a project carries for nothing: sample/demo folders of third-party packs, textures nothing
    /// references, and what makes the build big. Every fix is reversible: files are MOVED to a folder outside
    /// Assets (<see cref="BackupRoot"/>), never deleted.
    /// </summary>
    public static class ProjectBloatAudit
    {
        public const string BackupRoot = "UnusedAssetsBackup";

        private const long LargeBuildAssetBytes = 1024 * 1024;
        private const long LargeResourcesBytes = 10L * 1024 * 1024;

        private static readonly string[] YamlExtensions =
        {
            ".unity", ".prefab", ".mat", ".anim", ".controller", ".overridecontroller", ".asset", ".playable",
            ".mixer", ".spriteatlas", ".spriteatlasv2", ".flare", ".cubemap", ".guiskin", ".lighting", ".shadergraph",
            ".shadersubgraph", ".vfx", ".uxml", ".uss", ".tss", ".inputactions", ".signal", ".brush", ".preset"
        };

        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".bmp", ".gif", ".exr", ".hdr" };

        private static readonly string[] AlwaysKeepFolders =
        {
            "resources", "streamingassets", "editor", "editor default resources", "gizmos", "plugins", "textmesh pro"
        };

        public sealed class Result
        {
            public readonly List<CleanupIssueItem> Items = new List<CleanupIssueItem>();
            public readonly List<string> MovablePaths = new List<string>();
            public long Bytes;
            public string Note;

            public int Count { get { return Items.Count; } }
        }

        private sealed class AssetText
        {
            public string Path;
            public string Text;
            public HashSet<string> Guids;
        }

        private static readonly Regex GuidRegex = new Regex("guid:\\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);
        private static readonly Regex QuotedRegex = new Regex("\"([^\"\\r\\n]{1,200})\"", RegexOptions.Compiled);

        #region Sample / demo folders of third-party packs

        public static Result ScanSampleFolders()
        {
            var result = new Result();
            var candidates = FindSampleFolders();
            if (candidates.Count == 0) return result;

            var yaml = ReadYamlAssets();
            var scripts = ReadScripts();
            var buildScenes = new HashSet<string>(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path), StringComparer.OrdinalIgnoreCase);

            foreach (var folder in candidates)
            {
                if (CleanupIgnoreList.IsIgnored(CleanupIgnoreList.Samples, folder)) continue;

                var blocker = FindBlocker(folder, yaml, scripts, buildScenes);
                if (blocker != null)
                {
                    result.Note = (result.Note ?? string.Empty) + $"Kept {folder}: {blocker}\n";
                    continue;
                }

                var size = FolderSize(folder);
                result.Bytes += size;
                result.MovablePaths.Add(folder);
                result.Items.Add(new CleanupIssueItem
                {
                    AssetPath = folder,
                    TargetName = folder,
                    Description = $"Sample/demo folder nothing in the project uses ({FormatBytes(size)})",
                    Category = "Samples",
                    IgnoreScope = CleanupIgnoreList.Samples,
                    Count = 1
                });
            }

            return result;
        }

        private static List<string> FindSampleFolders()
        {
            var found = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Folder", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (!CleanupAssetUtility.IsSampleOrDemoPath(path + "/x")) continue;
                if (!CleanupAssetUtility.IsSampleFolderName(Path.GetFileName(path))) continue; // only the topmost named folder
                if (path.StartsWith("Assets/_", StringComparison.Ordinal)) continue; // the project's own content
                if (found.Any(f => path.StartsWith(f + "/", StringComparison.Ordinal))) continue;
                found.Add(path);
            }

            // Keep only the topmost folders.
            found.Sort(StringComparer.Ordinal);
            return found.Where(f => !found.Any(o => o != f && f.StartsWith(o + "/", StringComparison.Ordinal))).ToList();
        }

        private static string FindBlocker(string folder, List<AssetText> yaml, List<AssetText> scripts, HashSet<string> buildScenes)
        {
            foreach (var scene in buildScenes)
            {
                if (IsUnder(scene, folder)) return "a scene in it is in the Build Settings";
            }

            var inside = AssetDatabase.FindAssets(string.Empty, new[] { folder }).ToList();
            if (inside.Count == 0) return null;

            foreach (var sub in AssetDatabase.GetSubFolders(folder))
            {
                if (string.Equals(Path.GetFileName(sub), "Resources", StringComparison.OrdinalIgnoreCase))
                    return "it has a Resources folder (loaded by name)";
            }

            var guids = new HashSet<string>(inside, StringComparer.OrdinalIgnoreCase);
            foreach (var asset in yaml)
            {
                if (IsUnder(asset.Path, folder)) continue;
                if (asset.Guids.Overlaps(guids)) return "referenced by " + asset.Path;
            }

            var classNames = inside
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileNameWithoutExtension)
                .ToList();

            foreach (var script in scripts)
            {
                if (IsUnder(script.Path, folder)) continue;
                foreach (var name in classNames)
                {
                    if (Regex.IsMatch(script.Text, "\\b" + Regex.Escape(name) + "\\b"))
                        return $"code in {script.Path} mentions {name}";
                }
            }

            return null;
        }

        #endregion

        #region Unused textures

        public static Result ScanUnusedTextures()
        {
            var result = new Result();
            var yaml = ReadYamlAssets();
            var scripts = ReadScripts();

            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in yaml) referenced.UnionWith(asset.Guids);

            // Shaders keep their default textures in the .meta file; player settings hold icons and the splash.
            foreach (var guid in AssetDatabase.FindAssets("t:Shader", new[] { "Assets" }))
                AddGuids(AssetDatabase.GUIDToAssetPath(guid) + ".meta", referenced);
            foreach (var file in Directory.GetFiles("ProjectSettings", "*.asset"))
                AddGuids(file, referenced);

            // A Sprite Atlas packs whole folders: referencing the folder GUID uses everything inside it.
            var referencedFolders = new List<string>();
            foreach (var guid in referenced)
            {
                var folderPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(folderPath) && AssetDatabase.IsValidFolder(folderPath))
                    referencedFolders.Add(folderPath);
            }

            var quoted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var script in scripts)
            {
                foreach (Match m in QuotedRegex.Matches(script.Text))
                    quoted.Add(m.Groups[1].Value);
            }

            var sampleFolders = FindSampleFolders();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var guid in AssetDatabase.FindAssets("t:Texture", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;
                if (!ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant())) continue;
                if (IsInProtectedFolder(path) || sampleFolders.Any(f => IsUnder(path, f))) continue;
                if (referenced.Contains(guid)) continue;
                if (CleanupIgnoreList.IsIgnored(CleanupIgnoreList.Textures, path)) continue;
                if (referencedFolders.Any(f => IsUnder(path, f))) continue;

                var name = Path.GetFileNameWithoutExtension(path);
                if (quoted.Contains(name) || quoted.Contains(path) || quoted.Contains(Path.GetFileName(path))) continue;

                var size = SafeSize(path);
                result.Bytes += size;
                result.MovablePaths.Add(path);
                result.Items.Add(new CleanupIssueItem
                {
                    AssetPath = path,
                    TargetName = Path.GetFileName(path),
                    Description = $"Texture no scene, prefab, material, animation or script references ({FormatBytes(size)})",
                    Category = "Unused texture",
                    IgnoreScope = CleanupIgnoreList.Textures,
                    Count = 1
                });
            }

            return result;
        }

        private static bool IsInProtectedFolder(string path)
        {
            foreach (var part in path.Split('/'))
            {
                if (AlwaysKeepFolders.Contains(part.ToLowerInvariant())) return true;
            }

            return false;
        }

        private static void AddGuids(string path, HashSet<string> into)
        {
            try
            {
                if (!File.Exists(path)) return;
                foreach (Match m in GuidRegex.Matches(File.ReadAllText(path)))
                    into.Add(m.Groups[1].Value);
            }
            catch (Exception)
            {
                // Unreadable file: its references simply are not counted.
            }
        }

        #endregion

        #region Build size

        public static Result ScanBuildSize()
        {
            var result = new Result();

            foreach (var folder in FindResourcesFolders())
            {
                var size = FolderSize(folder);
                if (size < LargeResourcesBytes) continue;

                result.Bytes += size;
                result.Items.Add(new CleanupIssueItem
                {
                    AssetPath = folder,
                    TargetName = folder,
                    Description = $"Resources folder of {FormatBytes(size)}: everything in it ships in the build even when unused",
                    Category = "Large Resources folder",
                    Count = 1
                });
            }

            BuildReport report = null;
            try { report = BuildReport.GetLatestReport(); }
            catch (Exception) { /* No report available. */ }

            if (report == null)
            {
                result.Note = "No build report yet: make a build to also list the biggest assets in it.";
                return result;
            }

            var bySource = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
            foreach (var packed in report.packedAssets)
            {
                foreach (var content in packed.contents)
                {
                    var source = content.sourceAssetPath;
                    if (string.IsNullOrEmpty(source) || !source.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                    bySource.TryGetValue(source, out var total);
                    bySource[source] = total + content.packedSize;
                }
            }

            foreach (var pair in bySource.Where(p => (long)p.Value >= LargeBuildAssetBytes).OrderByDescending(p => p.Value).Take(15))
            {
                result.Items.Add(new CleanupIssueItem
                {
                    AssetPath = pair.Key,
                    TargetName = Path.GetFileName(pair.Key),
                    Description = $"{FormatBytes((long)pair.Value)} in the last build",
                    Category = "Large build asset",
                    Count = 1
                });
            }

            return result;
        }

        private static IEnumerable<string> FindResourcesFolders()
        {
            foreach (var guid in AssetDatabase.FindAssets("Resources t:Folder", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(Path.GetFileName(path), "Resources", StringComparison.OrdinalIgnoreCase) &&
                    !path.Contains("/Editor/") && !CleanupAssetUtility.IsSampleOrDemoPath(path + "/x"))
                    yield return path;
            }
        }

        #endregion

        #region Reversible removal

        /// <summary>Moves assets (and their .meta) out of Assets into <see cref="BackupRoot"/>/date/. Returns how many were moved.</summary>
        public static int MoveToBackup(IEnumerable<string> assetPaths, out string backupFolder)
        {
            backupFolder = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            var moved = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var asset in assetPaths)
                {
                    try
                    {
                        var target = Path.Combine(backupFolder, asset);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));

                        if (Directory.Exists(asset)) Directory.Move(asset, target);
                        else if (File.Exists(asset)) File.Move(asset, target);
                        else continue;

                        if (File.Exists(asset + ".meta")) File.Move(asset + ".meta", target + ".meta");
                        moved++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Cleanup] Could not move '{asset}': {ex.Message}");
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }

            return moved;
        }

        #endregion

        #region Helpers

        private static List<AssetText> ReadYamlAssets()
        {
            var list = new List<AssetText>();
            foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!YamlExtensions.Contains(Path.GetExtension(path).ToLowerInvariant())) continue;

                try
                {
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (Match m in GuidRegex.Matches(File.ReadAllText(path)))
                        set.Add(m.Groups[1].Value);
                    list.Add(new AssetText { Path = path, Guids = set });
                }
                catch (Exception)
                {
                    // Binary or locked file: skipped.
                }
            }

            return list;
        }

        private static List<AssetText> ReadScripts()
        {
            var list = new List<AssetText>();
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;

                try { list.Add(new AssetText { Path = path, Text = File.ReadAllText(path) }); }
                catch (Exception) { /* Skipped. */ }
            }

            return list;
        }

        private static bool IsUnder(string path, string folder)
        {
            return path.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(folder.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static long SafeSize(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
            catch (Exception) { return 0; }
        }

        private static long FolderSize(string folder)
        {
            try
            {
                return Directory.Exists(folder)
                    ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                        .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                        .Sum(f => new FileInfo(f).Length)
                    : 0;
            }
            catch (Exception) { return 0; }
        }

        public static string FormatBytes(long bytes)
        {
            return ThirdPartySlimResult.FormatBytes(bytes);
        }

        #endregion
    }
}
