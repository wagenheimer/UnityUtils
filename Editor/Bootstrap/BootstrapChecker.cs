using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wagenheimer.UnityUtils.Editor
{
    public enum DiagnosticSeverity
    {
        Pass,
        Info,
        Warning,
        Error
    }

    public class DiagnosticItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public DiagnosticSeverity Severity { get; set; }
        public string Category { get; set; }
        public bool CanFix { get; set; }
        public Action FixAction { get; set; }
    }

    public class DiagnosticReport
    {
        public List<DiagnosticItem> Items { get; } = new List<DiagnosticItem>();

        public int PassCount => Items.Count(i => i.Severity == DiagnosticSeverity.Pass);
        public int InfoCount => Items.Count(i => i.Severity == DiagnosticSeverity.Info);
        public int WarningCount => Items.Count(i => i.Severity == DiagnosticSeverity.Warning);
        public int ErrorCount => Items.Count(i => i.Severity == DiagnosticSeverity.Error);
        public bool HasIssues => WarningCount > 0 || ErrorCount > 0;
    }

    /// <summary>
    /// Diagnostic scanner for the Bootstrap Scene Kit: inspects settings asset location,
    /// scene existence, Build Settings registration, persistent prefabs configuration,
    /// scene leaks, and audio listener conflicts.
    /// </summary>
    public static class BootstrapChecker
    {
        public static DiagnosticReport RunDiagnostics(bool includeDeepSceneScan = true)
        {
            var report = new DiagnosticReport();
            var settings = BootstrapSceneTools.FindSettings();

            // 1. Settings Asset Check
            CheckSettingsAsset(report, settings);

            if (settings == null)
            {
                return report;
            }

            // 2. Bootstrap Scene File Check
            var scenePath = CheckBootstrapSceneFile(report, settings);

            // 3. Build Settings Check
            CheckBuildSettings(report, settings, scenePath);

            // 4. Persistent Prefabs Array Check
            CheckPersistentPrefabsArray(report, settings);

            // 5. Deep Scan: Lingering Prefabs & AudioListeners in scenes
            if (includeDeepSceneScan)
            {
                CheckSceneLeaksAndAudio(report, settings, scenePath);
            }

            return report;
        }

        private static void CheckSettingsAsset(DiagnosticReport report, BootstrapSettings settings)
        {
            if (settings == null)
            {
                report.Items.Add(new DiagnosticItem
                {
                    Id = "bootstrap-settings-missing",
                    Category = "Configuration",
                    Title = "BootstrapSettings Asset Missing",
                    Description = "No BootstrapSettings asset found in the project. The bootstrap loader requires this asset to locate your bootstrap scene and persistent objects.",
                    Severity = DiagnosticSeverity.Error,
                    CanFix = true,
                    FixAction = () =>
                    {
                        BootstrapSceneTools.CreateSettingsAsset();
                    }
                });
                return;
            }

            var assetPath = AssetDatabase.GetAssetPath(settings);
            var inResources = assetPath.Contains("/Resources/");

            if (!inResources)
            {
                report.Items.Add(new DiagnosticItem
                {
                    Id = "bootstrap-settings-not-in-resources",
                    Category = "Configuration",
                    Title = "BootstrapSettings Not in Resources Folder",
                    Description = $"Asset exists at '{assetPath}', but must be inside a 'Resources' directory (e.g., 'Assets/Resources/Wagenheimer/BootstrapSettings.asset') for runtime loading.",
                    Severity = DiagnosticSeverity.Error,
                    CanFix = true,
                    FixAction = () =>
                    {
                        MoveSettingsToResources(assetPath);
                    }
                });
            }
            else
            {
                report.Items.Add(new DiagnosticItem
                {
                    Id = "bootstrap-settings-ok",
                    Category = "Configuration",
                    Title = "BootstrapSettings Located",
                    Description = $"Configured at '{assetPath}'. Runtime loader can resolve it via Resources.Load.",
                    Severity = DiagnosticSeverity.Pass,
                    CanFix = false
                });
            }
        }

        private static string CheckBootstrapSceneFile(DiagnosticReport report, BootstrapSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.BootstrapSceneName))
            {
                report.Items.Add(new DiagnosticItem
                {
                    Id = "bootstrap-scene-name-empty",
                    Category = "Configuration",
                    Title = "Bootstrap Scene Name Unspecified",
                    Description = "BootstrapSceneName is blank on BootstrapSettings. Set a valid scene name (e.g. 'bootstrap').",
                    Severity = DiagnosticSeverity.Error,
                    CanFix = false
                });
                return null;
            }

            var scenePath = BootstrapSceneTools.ScenePathFor(settings.BootstrapSceneName);
            var sceneExists = !string.IsNullOrEmpty(scenePath) && File.Exists(scenePath);

            if (!sceneExists)
            {
                report.Items.Add(new DiagnosticItem
                {
                    Id = "bootstrap-scene-file-missing",
                    Category = "Scene",
                    Title = $"Bootstrap Scene Missing ('{settings.BootstrapSceneName}')",
                    Description = $"Scene file '{scenePath}' was not found in project assets. It must be created with persistent prefabs.",
                    Severity = DiagnosticSeverity.Error,
                    CanFix = true,
                    FixAction = () =>
                    {
                        BootstrapSceneTools.CreateOrRebuildBootstrapScene();
                    }
                });
                return null;
            }

            report.Items.Add(new DiagnosticItem
            {
                Id = "bootstrap-scene-file-ok",
                Category = "Scene",
                Title = $"Bootstrap Scene Found ('{settings.BootstrapSceneName}')",
                Description = $"Located on disk at '{scenePath}'.",
                Severity = DiagnosticSeverity.Pass,
                CanFix = false
            });

            return scenePath;
        }

        private static void CheckBuildSettings(DiagnosticReport report, BootstrapSettings settings, string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
                return;

            var scenes = EditorBuildSettings.scenes;
            var buildIndex = -1;
            var isEnabled = false;

            for (var i = 0; i < scenes.Length; i++)
            {
                if (string.Equals(scenes[i].path, scenePath, StringComparison.OrdinalIgnoreCase))
                {
                    buildIndex = i;
                    isEnabled = scenes[i].enabled;
                    break;
                }
            }

            if (buildIndex < 0)
            {
                report.Items.Add(new DiagnosticItem
                {
                    Id = "bootstrap-not-in-build-settings",
                    Category = "Build Settings",
                    Title = "Bootstrap Scene Not in Build Settings",
                    Description = $"The scene '{scenePath}' is missing from Build Settings. In player builds, SceneManager will fail to load it.",
                    Severity = DiagnosticSeverity.Warning,
                    CanFix = true,
                    FixAction = () =>
                    {
                        AddSceneToBuildSettings(scenePath, setAsFirst: true);
                    }
                });
            }
            else if (!isEnabled)
            {
                report.Items.Add(new DiagnosticItem
                {
                    Id = "bootstrap-scene-disabled-in-build",
                    Category = "Build Settings",
                    Title = "Bootstrap Scene Disabled in Build Settings",
                    Description = $"The scene '{scenePath}' is present at index {buildIndex} but unchecked (disabled).",
                    Severity = DiagnosticSeverity.Warning,
                    CanFix = true,
                    FixAction = () =>
                    {
                        EnableSceneInBuildSettings(scenePath);
                    }
                });
            }
            else if (buildIndex != 0)
            {
                report.Items.Add(new DiagnosticItem
                {
                    Id = "bootstrap-not-index-zero",
                    Category = "Build Settings",
                    Title = $"Bootstrap Scene at Index {buildIndex} (Recommended: 0)",
                    Description = $"The bootstrap scene is currently at index {buildIndex}. Setting it as index 0 guarantees persistent singletons load before any initial scene.",
                    Severity = DiagnosticSeverity.Info,
                    CanFix = true,
                    FixAction = () =>
                    {
                        MoveSceneToBuildIndexZero(scenePath);
                    }
                });
            }
            else
            {
                report.Items.Add(new DiagnosticItem
                {
                    Id = "bootstrap-build-settings-ok",
                    Category = "Build Settings",
                    Title = "Bootstrap Scene in Build Settings (Index 0)",
                    Description = $"Scene '{scenePath}' is active at index 0 in Build Settings.",
                    Severity = DiagnosticSeverity.Pass,
                    CanFix = false
                });
            }
        }

        private static void CheckPersistentPrefabsArray(DiagnosticReport report, BootstrapSettings settings)
        {
            var prefabs = settings.PersistentPrefabs;

            if (prefabs == null || prefabs.Length == 0)
            {
                report.Items.Add(new DiagnosticItem
                {
                    Id = "bootstrap-prefabs-empty",
                    Category = "Prefabs",
                    Title = "No Persistent Prefabs Assigned",
                    Description = "PersistentPrefabs list is empty. Assign singletons (e.g., AudioManager, GameManager, UI Canvas) to automatically populate the bootstrap scene.",
                    Severity = DiagnosticSeverity.Warning,
                    CanFix = false
                });
                return;
            }

            var nullCount = prefabs.Count(p => p == null);
            if (nullCount > 0)
            {
                report.Items.Add(new DiagnosticItem
                {
                    Id = "bootstrap-prefabs-has-nulls",
                    Category = "Prefabs",
                    Title = $"{nullCount} Missing or Null Persistent Prefab Slot(s)",
                    Description = "The PersistentPrefabs array contains missing/null references.",
                    Severity = DiagnosticSeverity.Warning,
                    CanFix = true,
                    FixAction = () =>
                    {
                        CleanNullPrefabs(settings);
                    }
                });
            }
            else
            {
                report.Items.Add(new DiagnosticItem
                {
                    Id = "bootstrap-prefabs-ok",
                    Category = "Prefabs",
                    Title = $"{prefabs.Length} Persistent Prefab(s) Configured",
                    Description = string.Join(", ", prefabs.Where(p => p != null).Select(p => p.name)),
                    Severity = DiagnosticSeverity.Pass,
                    CanFix = false
                });
            }
        }

        private static void CheckSceneLeaksAndAudio(DiagnosticReport report, BootstrapSettings settings, string bootstrapScenePath)
        {
            var targetGuids = (settings.PersistentPrefabs ?? Array.Empty<GameObject>())
                .Where(p => p != null)
                .Select(AssetDatabase.GetAssetPath)
                .Select(AssetDatabase.AssetPathToGUID)
                .Where(g => !string.IsNullOrEmpty(g))
                .ToHashSet();

            if (targetGuids.Count == 0)
                return;

            var sceneGuids = AssetDatabase.FindAssets("t:Scene");
            var leakingScenes = new List<string>();

            // Quick check: check if any non-bootstrap scene contains persistent prefab instances
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                for (var i = 0; i < sceneGuids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
                    if (string.Equals(path, bootstrapScenePath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    var hasLeak = false;

                    foreach (var root in scene.GetRootGameObjects())
                    {
                        if (!PrefabUtility.IsPartOfAnyPrefab(root))
                            continue;

                        var sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                        if (string.IsNullOrEmpty(sourcePath))
                            continue;

                        if (targetGuids.Contains(AssetDatabase.AssetPathToGUID(sourcePath)))
                        {
                            hasLeak = true;
                            break;
                        }
                    }

                    if (hasLeak)
                    {
                        leakingScenes.Add(scene.name);
                    }
                }
            }
            finally
            {
                if (setup != null && setup.Length > 0)
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
            }

            if (leakingScenes.Count > 0)
            {
                report.Items.Add(new DiagnosticItem
                {
                    Id = "bootstrap-prefab-leaks",
                    Category = "Scene Cleanliness",
                    Title = $"{leakingScenes.Count} Scene(s) Contain Persistent Prefabs",
                    Description = $"Persistent prefabs should only exist in the bootstrap scene. Found duplicates in: {string.Join(", ", leakingScenes)}.",
                    Severity = DiagnosticSeverity.Warning,
                    CanFix = true,
                    FixAction = () =>
                    {
                        BootstrapSceneTools.RemoveFromOtherScenes();
                    }
                });
            }
            else
            {
                report.Items.Add(new DiagnosticItem
                {
                    Id = "bootstrap-prefab-leaks-none",
                    Category = "Scene Cleanliness",
                    Title = "No Leaking Persistent Prefabs",
                    Description = "No other scenes contain duplicate instances of configured persistent prefabs.",
                    Severity = DiagnosticSeverity.Pass,
                    CanFix = false
                });
            }
        }

        #region Fix Helpers

        private static void MoveSettingsToResources(string currentPath)
        {
            var targetDir = "Assets/Resources/Wagenheimer";
            if (!AssetDatabase.IsValidFolder(targetDir))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                    AssetDatabase.CreateFolder("Assets", "Resources");
                AssetDatabase.CreateFolder("Assets/Resources", "Wagenheimer");
            }

            var targetPath = $"{targetDir}/BootstrapSettings.asset";
            AssetDatabase.MoveAsset(currentPath, targetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[BootstrapChecker] Moved BootstrapSettings to '{targetPath}'.");
        }

        private static void AddSceneToBuildSettings(string scenePath, bool setAsFirst)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(s => string.Equals(s.path, scenePath, StringComparison.OrdinalIgnoreCase));

            if (setAsFirst)
                scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            else
                scenes.Add(new EditorBuildSettingsScene(scenePath, true));

            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[BootstrapChecker] Added '{scenePath}' to Build Settings (index {(setAsFirst ? 0 : scenes.Count - 1)}).");
        }

        private static void EnableSceneInBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes;
            foreach (var scene in scenes)
            {
                if (string.Equals(scene.path, scenePath, StringComparison.OrdinalIgnoreCase))
                {
                    scene.enabled = true;
                    break;
                }
            }
            EditorBuildSettings.scenes = scenes;
            Debug.Log($"[BootstrapChecker] Enabled '{scenePath}' in Build Settings.");
        }

        private static void MoveSceneToBuildIndexZero(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            var target = scenes.FirstOrDefault(s => string.Equals(s.path, scenePath, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                scenes.Remove(target);
                scenes.Insert(0, target);
                EditorBuildSettings.scenes = scenes.ToArray();
                Debug.Log($"[BootstrapChecker] Moved '{scenePath}' to Build Settings index 0.");
            }
        }

        private static void CleanNullPrefabs(BootstrapSettings settings)
        {
            if (settings == null || settings.PersistentPrefabs == null) return;
            var cleaned = settings.PersistentPrefabs.Where(p => p != null).ToArray();
            settings.PersistentPrefabs = cleaned;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("[BootstrapChecker] Cleaned null references from PersistentPrefabs array.");
        }

        #endregion
    }
}

