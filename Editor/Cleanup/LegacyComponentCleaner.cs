using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Re-saves and modernizes scenes, prefabs, and project assets to strip legacy components
    /// (such as deprecated GUILayer, GUIText, GUITexture, obsolete serialized component indices)
    /// and eliminate persistent console warnings when opening older assets in newer Unity versions.
    /// </summary>
    public static class LegacyComponentCleaner
    {
        private static readonly HashSet<string> PluginExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".dll", ".so", ".a", ".bundle", ".dylib", ".jar", ".aar", ".framework", ".xcframework"
        };

        public struct ScanResult
        {
            public int LegacyPluginCount;
            public int TotalScenesCount;
            public int TotalPrefabsCount;
            public List<string> LegacyPluginPaths;
            public List<CleanupIssueItem> Items;
            public List<string> Details;
        }

        public struct CleanResult
        {
            public int ScenesUpdated;
            public int PrefabsUpdated;
            public int PluginsUpdated;
            public int AssetsReserialized;
            public List<CleanupModificationRecord> Modifications;
        }

        public static ScanResult ScanProject()
        {
            var scan = new ScanResult
            {
                LegacyPluginPaths = new List<string>(),
                Items = new List<CleanupIssueItem>(),
                Details = new List<string>()
            };

            var legacyPlugins = FindLegacyPluginImporterPaths();
            scan.LegacyPluginCount = legacyPlugins.Length;
            scan.LegacyPluginPaths.AddRange(legacyPlugins);

            var scenePaths = CleanupAssetUtility.GetProjectScenePaths();
            scan.TotalScenesCount = scenePaths.Length;

            var prefabPaths = CleanupAssetUtility.GetProjectPrefabPaths();
            scan.TotalPrefabsCount = prefabPaths.Length;

            foreach (var plugin in legacyPlugins)
            {
                scan.Details.Add($"[Legacy Plugin Meta] {plugin}: PluginImporter serializedVersion < 2");
                scan.Items.Add(new CleanupIssueItem
                {
                    AssetPath = plugin,
                    TargetName = Path.GetFileName(plugin),
                    Description = "PluginImporter serializedVersion < 2 (triggers Unity 6 upgrade warning)",
                    Category = "Legacy Plugin",
                    IsScene = false,
                    IsPrefab = false,
                    Count = 1
                });
            }

            return scan;
        }

        #region Menu Items

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Legacy Components/Resave All Scenes (Upgrade Serialization)", priority = 30)]
        public static void ResaveAllScenes()
        {
            if (!EditorUtility.DisplayDialog("Resave All Scenes",
                    "This opens and resaves every scene in the project to strip legacy components (e.g. GUILayer) " +
                    "and update their serialization to your current Unity version.\n\n" +
                    "Make sure your work is saved/committed first.", "Proceed", "Cancel"))
                return;

            var updated = ResaveScenes();
            var msg = $"Resaved and upgraded {updated} scene(s).";
            Debug.Log($"[LegacyComponentCleaner] {msg}");
            EditorUtility.DisplayDialog("Resave All Scenes", msg, "OK");
        }

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Legacy Components/Resave All Prefabs (Upgrade Serialization)", priority = 31)]
        public static void ResaveAllPrefabs()
        {
            if (!EditorUtility.DisplayDialog("Resave All Prefabs",
                    "This loads and resaves every prefab in the project to strip obsolete component references " +
                    "and modernize serialization.\n\n" +
                    "Make sure your work is saved/committed first.", "Proceed", "Cancel"))
                return;

            var updated = ResavePrefabs();
            var msg = $"Resaved and upgraded {updated} prefab(s).";
            Debug.Log($"[LegacyComponentCleaner] {msg}");
            EditorUtility.DisplayDialog("Resave All Prefabs", msg, "OK");
        }

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Legacy Components/Resave All Scenes && Prefabs (Full Project Modernize)", priority = 32)]
        public static void ResaveAllScenesAndPrefabs()
        {
            if (!EditorUtility.DisplayDialog("Full Project Modernize",
                    "This will resave and upgrade all Scenes, Prefabs, and Assets in your project to clear " +
                    "legacy GUILayer warnings and broken component index references.\n\n" +
                    "Make sure your work is saved/committed first.", "Proceed", "Cancel"))
                return;

            var scenesUpdated = ResaveScenes();
            var prefabsUpdated = ResavePrefabs();
            var pluginsUpdated = ResaveLegacyPluginImporters();
            ForceReserializeProject();

            var msg = $"Project modernize complete: {scenesUpdated} scene(s), {prefabsUpdated} prefab(s) and " +
                      $"{pluginsUpdated} legacy plugin importer(s) upgraded.";
            Debug.Log($"[LegacyComponentCleaner] {msg}");
            EditorUtility.DisplayDialog("Project Modernize", msg, "OK");
        }

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Legacy Components/Resave Legacy Plugin Importers (Upgrade .meta Serialization)", priority = 33)]
        public static void ResaveAllLegacyPluginImporters()
        {
            if (!EditorUtility.DisplayDialog("Resave Legacy Plugin Importers",
                    "This re-saves .meta files whose PluginImporter is still on an old serialization " +
                    "version (Unity logs 'PluginImporter object at version 1, below the supported minimum (2)').\n\n" +
                    "Only plugin metadata is touched; the plugin binaries are not modified.", "Proceed", "Cancel"))
                return;

            var updated = ResaveLegacyPluginImporters();
            var msg = $"Re-saved {updated} legacy plugin importer(s).";
            Debug.Log($"[LegacyComponentCleaner] {msg}");
            EditorUtility.DisplayDialog("Resave Legacy Plugin Importers", msg, "OK");
        }

        #endregion

        #region Core Logic

        public static int ResaveScenes()
        {
            var updated = 0;
            CleanupRunner.RunBlocking("Resaving scenes", p => ResaveScenesRoutine(p, n => updated = n));
            return updated;
        }

        /// <summary>Time-sliced scene resave: run it with <see cref="CleanupRunner.Start"/>.</summary>
        public static System.Collections.IEnumerator ResaveScenesRoutine(CleanupProgress progress, Action<int> done)
        {
            var updated = 0;

            yield return ProjectSweep.Run(progress, "Resaving scenes",
                (path, scene) =>
                {
                    updated++;
                    return true; // Marking dirty and saving is what upgrades the serialization.
                },
                onPrefab: null,
                saveScenes: true);

            done(updated);
        }

        public static int ResavePrefabs()
        {
            var updated = 0;
            CleanupRunner.RunBlocking("Resaving prefabs", p => ResavePrefabsRoutine(p, n => updated = n));
            return updated;
        }

        /// <summary>Time-sliced prefab resave: run it with <see cref="CleanupRunner.Start"/>.</summary>
        public static System.Collections.IEnumerator ResavePrefabsRoutine(CleanupProgress progress, Action<int> done)
        {
            var updated = 0;

            yield return ProjectSweep.Run(progress, "Resaving prefabs",
                onScene: null,
                onPrefab: prefabPath =>
                {
                    var root = CleanupAssetUtility.LoadPrefabContents(prefabPath);
                    if (root == null) return;

                    try
                    {
                        if (CleanupAssetUtility.TrySavePrefabContents(root, prefabPath))
                            updated++;
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                },
                saveScenes: false);

            done(updated);
        }

        /// <summary>
        /// Re-saves .meta files whose PluginImporter still reports the legacy serialization version
        /// (Unity 6 warns: "contains a PluginImporter object at version 1, below the supported minimum (2)").
        /// Saving the importer is the supported way to upgrade the metadata without touching the binary.
        /// </summary>
        public static CleanResult ResaveLegacyPluginImportersWithResult()
        {
            var res = default(CleanResult);
            CleanupRunner.RunBlocking("Resaving legacy plugin importers",
                p => ResaveLegacyPluginImportersRoutine(p, r => res = r));
            return res;
        }

        /// <summary>Time-sliced plugin importer upgrade: run it with <see cref="CleanupRunner.Start"/>.</summary>
        public static System.Collections.IEnumerator ResaveLegacyPluginImportersRoutine(CleanupProgress progress,
            Action<CleanResult> done)
        {
            var res = new CleanResult { Modifications = new List<CleanupModificationRecord>() };

            progress.SetDetail("Looking for legacy plugin importers...");
            yield return null;

            var legacyPluginPaths = FindLegacyPluginImporterPaths();
            progress.NextStage("Resaving legacy plugin importers", legacyPluginPaths.Length);

            var updated = 0;
            foreach (var assetPath in legacyPluginPaths)
            {
                if (progress.CancelRequested) break;

                progress.Advance(assetPath);

                if (TryUpgradeAndRecord(assetPath, res))
                    updated++;

                yield return null;
            }

            res.PluginsUpdated = updated;
            done(res);
        }

        private static bool TryUpgradeAndRecord(string assetPath, CleanResult res)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
            if (importer == null) return false;

            try
            {
                // Count a plugin as upgraded only when its .meta really changed (verified on disk).
                if (!TryUpgradePluginImporter(importer, assetPath))
                {
                    Debug.LogWarning($"[LegacyComponentCleaner] '{assetPath}' is still on a legacy PluginImporter " +
                                     "version after re-saving. Check that the file is writable (version control lock?).");
                    return false;
                }

                res.Modifications.Add(new CleanupModificationRecord
                {
                    AssetPath = assetPath,
                    ActionTaken = "Upgraded PluginImporter serialization to version 2",
                    ItemsFixed = 1,
                    IsScene = false,
                    IsPrefab = false
                });
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LegacyComponentCleaner] Could not resave plugin importer '{assetPath}': {ex.Message}");
                return false;
            }
        }

        public static int ResaveLegacyPluginImporters()
        {
            return ResaveLegacyPluginImportersWithResult().PluginsUpdated;
        }

        private static string[] FindLegacyPluginImporterPaths()
        {
            if (!Directory.Exists("Assets"))
                return Array.Empty<string>();

            var paths = new List<string>();
            foreach (var metaPath in Directory.GetFiles("Assets", "*.meta", SearchOption.AllDirectories))
            {
                var assetPath = metaPath.Replace('\\', '/');
                assetPath = assetPath.Substring(0, assetPath.Length - ".meta".Length);
                if (!PluginExtensions.Contains(Path.GetExtension(assetPath)))
                    continue;

                string metaText;
                try
                {
                    metaText = File.ReadAllText(metaPath);
                }
                catch
                {
                    continue;
                }

                if (!metaText.Contains("PluginImporter:"))
                    continue;
                if (!HasLegacyPluginImporterVersion(metaText))
                    continue;

                paths.Add(assetPath);
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);
            return paths.ToArray();
        }

        private static bool HasLegacyPluginImporterVersion(string metaText)
        {
            // A missing serializedVersion means a very old importer that needs upgrading.
            return !TryReadPluginImporterVersion(metaText, out var version) || version < 2;
        }

        /// <summary>
        /// Reads the serializedVersion of the PluginImporter block. Current metas look like
        /// <c>PluginImporter:\n  externalObjects: {}\n  serializedVersion: 2</c>, so the version is NOT
        /// always the first key and must be read from the block rather than assumed to follow the header
        /// (the old check required it to be first, so every modern plugin was reported as legacy forever).
        /// </summary>
        private static bool TryReadPluginImporterVersion(string metaText, out int version)
        {
            version = 0;
            var inBlock = false;

            foreach (var rawLine in metaText.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');

                if (!inBlock)
                {
                    if (line.StartsWith("PluginImporter:", StringComparison.Ordinal))
                        inBlock = true;
                    continue;
                }

                // A non-indented line starts the next top-level key: the importer block is over.
                if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
                    break;

                // Only direct children (two-space indent) count; nested maps may have their own serializedVersion.
                const string key = "  serializedVersion:";
                if (line.StartsWith(key, StringComparison.Ordinal))
                    return int.TryParse(line.Substring(key.Length).Trim(), out version);
            }

            return false;
        }

        private static bool IsLegacyPluginMeta(string assetPath)
        {
            try
            {
                return HasLegacyPluginImporterVersion(File.ReadAllText(assetPath + ".meta"));
            }
            catch (Exception)
            {
                return true; // Unreadable meta: do not claim success.
            }
        }

        /// <summary>
        /// Upgrades one plugin .meta and verifies the result on disk. SaveAndReimport only rewrites the
        /// .meta when the importer is dirty, so it is marked dirty first; if the version is still old,
        /// ForceReserializeAssets (the supported way to rewrite metadata) is used as a fallback.
        /// </summary>
        private static bool TryUpgradePluginImporter(PluginImporter importer, string assetPath)
        {
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            if (!IsLegacyPluginMeta(assetPath))
                return true;

            AssetDatabase.ForceReserializeAssets(new[] { assetPath }, ForceReserializeAssetsOptions.ReserializeMetadata);
            return !IsLegacyPluginMeta(assetPath);
        }

        public static void ForceReserializeProject()
        {
            CleanupRunner.RunBlocking("Reserializing assets", ForceReserializeProjectRoutine);
        }

        /// <summary>Time-sliced reserialize (small batches): run it with <see cref="CleanupRunner.Start"/>.</summary>
        public static System.Collections.IEnumerator ForceReserializeProjectRoutine(CleanupProgress progress)
        {
            const int batchSize = 25;

            var allAssets = CleanupAssetUtility.GetProjectScenePaths()
                .Concat(CleanupAssetUtility.GetProjectPrefabPaths())
                .Concat(CleanupAssetUtility.GetProjectScriptableObjectPaths())
                .Distinct()
                .ToArray();

            if (allAssets.Length == 0)
                yield break;

            progress.NextStage("Reserializing assets", allAssets.Length);

            for (var i = 0; i < allAssets.Length; i += batchSize)
            {
                if (progress.CancelRequested) break;

                var batch = allAssets.Skip(i).Take(batchSize).ToArray();
                progress.AdvanceBy(batch.Length, batch[0]);
                AssetDatabase.ForceReserializeAssets(batch);
                yield return null;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// The whole "Full Project Modernize" operation as one cancellable task: scenes, prefabs, plugin
        /// importers, then a final reserialize. The result carries the per-step counts.
        /// </summary>
        public static System.Collections.IEnumerator ModernizeProjectRoutine(CleanupProgress progress,
            Action<CleanResult> done)
        {
            progress.SetStageCount(4);

            var result = new CleanResult { Modifications = new List<CleanupModificationRecord>() };

            yield return ResaveScenesRoutine(progress, n => result.ScenesUpdated = n);
            if (progress.CancelRequested) { done(result); yield break; }

            yield return ResavePrefabsRoutine(progress, n => result.PrefabsUpdated = n);
            if (progress.CancelRequested) { done(result); yield break; }

            yield return ResaveLegacyPluginImportersRoutine(progress, r =>
            {
                result.PluginsUpdated = r.PluginsUpdated;
                result.Modifications.AddRange(r.Modifications ?? new List<CleanupModificationRecord>());
            });
            if (progress.CancelRequested) { done(result); yield break; }

            yield return ForceReserializeProjectRoutine(progress);
            done(result);
        }

        #endregion
    }
}
