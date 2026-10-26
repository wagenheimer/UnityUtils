using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

        public struct CleanResult
        {
            public int ScenesUpdated;
            public int PrefabsUpdated;
            public int AssetsReserialized;
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
            if (!CleanupAssetUtility.SaveCurrentScenesIfNeeded())
                return 0;

            var originalSetup = EditorSceneManager.GetSceneManagerSetup();
            var scenePaths = CleanupAssetUtility.GetProjectScenePaths();
            var updated = 0;

            try
            {
                for (int i = 0; i < scenePaths.Length; i++)
                {
                    var scenePath = scenePaths[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Resaving Scenes", scenePath, (float)i / scenePaths.Length))
                        break;

                    if (!CleanupAssetUtility.TryOpenScene(scenePath, out var scene))
                        continue;

                    try
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        EditorSceneManager.SaveScene(scene);
                        updated++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[LegacyComponentCleaner] Could not resave scene '{scenePath}': {ex.Message}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (originalSetup != null && originalSetup.Length > 0)
                    EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
            }

            return updated;
        }

        public static int ResavePrefabs()
        {
            var prefabPaths = CleanupAssetUtility.GetProjectPrefabPaths();
            var updated = 0;

            try
            {
                for (int i = 0; i < prefabPaths.Length; i++)
                {
                    var prefabPath = prefabPaths[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Resaving Prefabs", prefabPath, (float)i / prefabPaths.Length))
                        break;

                    var root = CleanupAssetUtility.LoadPrefabContents(prefabPath);
                    if (root == null) continue;

                    try
                    {
                        if (CleanupAssetUtility.TrySavePrefabContents(root, prefabPath))
                            updated++;
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return updated;
        }

        /// <summary>
        /// Re-saves .meta files whose PluginImporter still reports the legacy serialization version
        /// (Unity 6 warns: "contains a PluginImporter object at version 1, below the supported minimum (2)").
        /// Saving the importer is the supported way to upgrade the metadata without touching the binary.
        /// </summary>
        public static int ResaveLegacyPluginImporters()
        {
            var legacyPluginPaths = FindLegacyPluginImporterPaths();
            var updated = 0;

            try
            {
                for (int i = 0; i < legacyPluginPaths.Length; i++)
                {
                    var assetPath = legacyPluginPaths[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Resaving Legacy Plugin Importers",
                            assetPath, (float)i / legacyPluginPaths.Length))
                        break;

                    var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
                    if (importer == null) continue;

                    try
                    {
                        importer.SaveAndReimport();
                        updated++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[LegacyComponentCleaner] Could not resave plugin importer '{assetPath}': {ex.Message}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return updated;
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
            var match = Regex.Match(metaText, @"PluginImporter:\s*\r?\n\s*serializedVersion:\s*(\d+)");
            if (!match.Success)
                return true; // Missing serializedVersion means an old importer that needs upgrading.

            return int.TryParse(match.Groups[1].Value, out var version) && version < 2;
        }

        public static void ForceReserializeProject()
        {
            var allAssets = CleanupAssetUtility.GetProjectScenePaths()
                .Concat(CleanupAssetUtility.GetProjectPrefabPaths())
                .Concat(CleanupAssetUtility.GetProjectScriptableObjectPaths())
                .Distinct()
                .ToArray();

            if (allAssets.Length == 0)
                return;

            AssetDatabase.ForceReserializeAssets(allAssets);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        #endregion
    }
}
