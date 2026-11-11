using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Removes duplicate AudioListeners across scenes and prefabs in the project.
    ///
    /// If your project uses <see cref="BootstrapSettings"/>, it identifies your persistent audio
    /// listener (or preferred persistent prefab) and removes duplicate listeners from scenes and prefabs.
    /// </summary>
    public static class AudioListenerCleaner
    {
        public struct ScanResult
        {
            public int DuplicateCount;
            public int AffectedScenesCount;
            public int AffectedPrefabsCount;
            public List<string> Details;
            public List<CleanupIssueItem> Items;
        }

        #region Menu Items

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Audio Listeners/Remove Duplicate Audio Listeners (Active Scene Only)", priority = 10)]
        public static void RemoveDuplicateAudioListenersInActiveScene()
        {
            var preferredPrefabPath = GetPreferredPrefabPath();
            var scene = EditorSceneManager.GetActiveScene();

            var removed = RemoveDuplicatesInScene(scene, preferredPrefabPath, removeAllNonPersistent: false);
            if (removed > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            var msg = $"Removed {removed} duplicate AudioListener(s) from '{scene.name}'.";
            Debug.Log($"[AudioListenerCleaner] {msg}");
            EditorUtility.DisplayDialog("Remove Duplicate Audio Listeners", msg, "OK");
        }

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Audio Listeners/Remove Duplicate Audio Listeners (All Scenes)", priority = 11)]
        public static void RemoveDuplicatesInAllScenes()
        {
            if (!EditorUtility.DisplayDialog("Remove Duplicate Audio Listeners (All Scenes)",
                    "This opens every scene in the project and removes duplicate AudioListeners.\n\n" +
                    "Make sure your work is saved before proceeding.", "Proceed", "Cancel"))
                return;

            var result = CleanAllScenes(removeAllNonPersistent: false);
            var msg = $"Removed {result.DuplicatesRemoved} duplicate AudioListener(s) across {result.ScenesModified} scene(s).";
            Debug.Log($"[AudioListenerCleaner] {msg}");
            EditorUtility.DisplayDialog("Remove Duplicate Audio Listeners", msg, "OK");
        }

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Audio Listeners/Remove Duplicate Audio Listeners (All Prefabs)", priority = 12)]
        public static void RemoveDuplicatesInAllPrefabs()
        {
            if (!EditorUtility.DisplayDialog("Remove Duplicate Audio Listeners (All Prefabs)",
                    "This scans every prefab in the project and removes redundant AudioListeners.\n\n" +
                    "Make sure your work is saved before proceeding.", "Proceed", "Cancel"))
                return;

            var result = CleanAllPrefabs(removeAllNonPersistent: false);
            var msg = $"Removed {result.DuplicatesRemoved} duplicate AudioListener(s) across {result.PrefabsModified} prefab(s).";
            Debug.Log($"[AudioListenerCleaner] {msg}");
            EditorUtility.DisplayDialog("Remove Duplicate Audio Listeners", msg, "OK");
        }

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Audio Listeners/Remove Duplicate Audio Listeners (All Scenes && Prefabs)", priority = 13)]
        public static void RemoveDuplicatesInAllScenesAndPrefabs()
        {
            if (!EditorUtility.DisplayDialog("Remove Duplicate Audio Listeners",
                    "This scans every scene and prefab in the project, removing duplicate AudioListeners.\n\n" +
                    "Make sure your work is saved before proceeding.", "Proceed", "Cancel"))
                return;

            var sceneRes = CleanAllScenes(removeAllNonPersistent: false);
            var prefabRes = CleanAllPrefabs(removeAllNonPersistent: false);

            var totalRemoved = sceneRes.DuplicatesRemoved + prefabRes.DuplicatesRemoved;
            var msg = $"Removed {totalRemoved} duplicate AudioListener(s) ({sceneRes.ScenesModified} scene(s), {prefabRes.PrefabsModified} prefab(s) modified).";
            Debug.Log($"[AudioListenerCleaner] {msg}");
            EditorUtility.DisplayDialog("Remove Duplicate Audio Listeners", msg, "OK");
        }

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Audio Listeners/Remove ALL Audio Listeners From Non-Bootstrap Scenes", priority = 14)]
        public static void RemoveAllNonBootstrapListeners()
        {
            var settings = BootstrapSceneTools.FindSettings();
            var bootstrapSceneName = settings?.BootstrapSceneName ?? "bootstrap";

            if (!EditorUtility.DisplayDialog("Remove All Non-Bootstrap Audio Listeners",
                    $"This will remove ALL AudioListeners from every scene except the bootstrap scene ('{bootstrapSceneName}').\n\n" +
                    "Use this if your persistent audio manager (loaded in the bootstrap scene) handles all audio listening.\n\n" +
                    "Make sure your work is saved before proceeding.", "Proceed", "Cancel"))
                return;

            var result = CleanAllScenes(removeAllNonPersistent: true, bootstrapSceneNameToSkip: bootstrapSceneName);
            var msg = $"Removed {result.DuplicatesRemoved} AudioListener(s) across {result.ScenesModified} scene(s).";
            Debug.Log($"[AudioListenerCleaner] {msg}");
            EditorUtility.DisplayDialog("Remove Audio Listeners", msg, "OK");
        }

        #endregion

        #region Core Scan & Clean Logic

        public struct CleanResult
        {
            public int DuplicatesRemoved;
            public int ScenesModified;
            public int PrefabsModified;
            public List<CleanupModificationRecord> Modifications;
        }

        /// <summary>Synchronous scan for menu items and API callers (modal progress bar with Cancel).</summary>
        public static ScanResult ScanProject()
        {
            var scan = default(ScanResult);
            CleanupRunner.RunBlocking("Scanning AudioListeners", p => ScanProjectRoutine(p, r => scan = r));
            return scan;
        }

        /// <summary>Time-sliced scan: run it with <see cref="CleanupRunner.Start"/>; yields after every asset.</summary>
        public static System.Collections.IEnumerator ScanProjectRoutine(CleanupProgress progress, System.Action<ScanResult> done)
        {
            var scan = new ScanResult
            {
                Details = new List<string>(),
                Items = new List<CleanupIssueItem>()
            };

            yield return ProjectSweep.Run(progress, "Scanning AudioListeners",
                (scenePath, scene) =>
                {
                    var listeners = scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<AudioListener>(true))
                        .ToList();

                    if (listeners.Count > 1)
                    {
                        var duplicates = listeners.Count - 1;
                        scan.DuplicateCount += duplicates;
                        scan.AffectedScenesCount++;
                        scan.Details.Add($"[Scene] {scenePath}: {listeners.Count} AudioListeners found ({duplicates} duplicate(s))");

                        var gameObjectsSummary = string.Join(", ", listeners.Select(l => l != null && l.gameObject != null ? l.gameObject.name : "null"));
                        scan.Items.Add(new CleanupIssueItem
                        {
                            AssetPath = scenePath,
                            TargetName = scene.name,
                            Description = $"{listeners.Count} AudioListeners found ({duplicates} duplicate(s)) on: {gameObjectsSummary}",
                            Category = "AudioListener",
                            IsScene = true,
                            IsPrefab = false,
                            Count = duplicates
                        });
                    }

                    return false;
                },
                prefabPath =>
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (prefab == null) return;

                    var listeners = prefab.GetComponentsInChildren<AudioListener>(true);
                    if (listeners.Length <= 1) return;

                    var duplicates = listeners.Length - 1;
                    scan.DuplicateCount += duplicates;
                    scan.AffectedPrefabsCount++;
                    scan.Details.Add($"[Prefab] {prefabPath}: {listeners.Length} AudioListeners found ({duplicates} duplicate(s))");

                    var gameObjectsSummary = string.Join(", ", listeners.Select(l => l != null && l.gameObject != null ? l.gameObject.name : "null"));
                    scan.Items.Add(new CleanupIssueItem
                    {
                        AssetPath = prefabPath,
                        TargetName = prefab.name,
                        Description = $"{listeners.Length} AudioListeners found ({duplicates} duplicate(s)) on: {gameObjectsSummary}",
                        Category = "AudioListener",
                        IsScene = false,
                        IsPrefab = true,
                        Count = duplicates
                    });
                },
                saveScenes: false);

            done(scan);
        }

        public static CleanResult CleanAllScenes(bool removeAllNonPersistent, string bootstrapSceneNameToSkip = null)
        {
            var result = default(CleanResult);
            CleanupRunner.RunBlocking("Removing duplicate AudioListeners (scenes)",
                p => CleanAllScenesRoutine(p, removeAllNonPersistent, bootstrapSceneNameToSkip, r => result = r));
            return result;
        }

        public static System.Collections.IEnumerator CleanAllScenesRoutine(CleanupProgress progress,
            bool removeAllNonPersistent, string bootstrapSceneNameToSkip, System.Action<CleanResult> done)
        {
            var result = new CleanResult { Modifications = new List<CleanupModificationRecord>() };
            var preferredPrefabPath = GetPreferredPrefabPath();

            yield return ProjectSweep.Run(progress, "Removing duplicate AudioListeners (scenes)",
                (scenePath, scene) =>
                {
                    var removed = RemoveDuplicatesInScene(scene, preferredPrefabPath, removeAllNonPersistent);
                    if (removed <= 0) return false;

                    result.DuplicatesRemoved += removed;
                    result.ScenesModified++;
                    result.Modifications.Add(new CleanupModificationRecord
                    {
                        AssetPath = scenePath,
                        ActionTaken = $"Removed {removed} duplicate AudioListener(s)",
                        ItemsFixed = removed,
                        IsScene = true,
                        IsPrefab = false
                    });
                    return true;
                },
                onPrefab: null,
                saveScenes: true,
                skipScene: scenePath =>
                    !string.IsNullOrEmpty(bootstrapSceneNameToSkip) &&
                    string.Equals(System.IO.Path.GetFileNameWithoutExtension(scenePath), bootstrapSceneNameToSkip,
                        StringComparison.OrdinalIgnoreCase));

            done(result);
        }

        public static CleanResult CleanAllPrefabs(bool removeAllNonPersistent)
        {
            var result = default(CleanResult);
            CleanupRunner.RunBlocking("Removing duplicate AudioListeners (prefabs)",
                p => CleanAllPrefabsRoutine(p, removeAllNonPersistent, r => result = r));
            return result;
        }

        public static System.Collections.IEnumerator CleanAllPrefabsRoutine(CleanupProgress progress,
            bool removeAllNonPersistent, System.Action<CleanResult> done)
        {
            var result = new CleanResult { Modifications = new List<CleanupModificationRecord>() };
            var preferredPrefabPath = GetPreferredPrefabPath();

            yield return ProjectSweep.Run(progress, "Removing duplicate AudioListeners (prefabs)",
                onScene: null,
                onPrefab: prefabPath =>
                {
                    var removed = RemoveDuplicatesInPrefab(prefabPath, preferredPrefabPath, removeAllNonPersistent);
                    if (removed <= 0) return;

                    result.DuplicatesRemoved += removed;
                    result.PrefabsModified++;
                    result.Modifications.Add(new CleanupModificationRecord
                    {
                        AssetPath = prefabPath,
                        ActionTaken = $"Removed {removed} duplicate AudioListener(s)",
                        ItemsFixed = removed,
                        IsScene = false,
                        IsPrefab = true
                    });
                },
                saveScenes: false);

            done(result);
        }

        public static int RemoveDuplicatesInScene(Scene scene, string preferredPrefabPath, bool removeAllNonPersistent)
        {
            var listeners = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<AudioListener>(true))
                .ToList();

            if (listeners.Count == 0)
                return 0;

            if (removeAllNonPersistent)
            {
                var removedCount = 0;
                foreach (var listener in listeners)
                {
                    if (listener == null) continue;
                    if (CleanupAssetUtility.TryDestroyComponent(listener))
                        removedCount++;
                }
                return removedCount;
            }

            if (listeners.Count <= 1)
                return 0;

            var keeper = FindPreferredListener(listeners, preferredPrefabPath);
            if (keeper == null)
            {
                // Prefer Camera.main or listener on Camera component
                keeper = listeners.FirstOrDefault(l => l.GetComponent<Camera>() != null && l.GetComponent<Camera>().CompareTag("MainCamera"))
                         ?? listeners.FirstOrDefault(l => l.GetComponent<Camera>() != null)
                         ?? listeners[0];

                Debug.LogWarning($"[AudioListenerCleaner] {scene.path}: preferred bootstrap prefab not found, keeping '{keeper.gameObject.name}'.");
            }

            var removed = 0;
            foreach (var listener in listeners)
            {
                if (listener == null || listener == keeper)
                    continue;

                if (CleanupAssetUtility.TryDestroyComponent(listener))
                    removed++;
            }

            return removed;
        }

        public static int RemoveDuplicatesInPrefab(string prefabPath, string preferredPrefabPath, bool removeAllNonPersistent)
        {
            var root = CleanupAssetUtility.LoadPrefabContents(prefabPath);
            if (root == null) return 0;

            try
            {
                var listeners = root.GetComponentsInChildren<AudioListener>(true).ToList();
                if (listeners.Count <= 1 && !removeAllNonPersistent)
                    return 0;

                var isPreferredPrefab = !string.IsNullOrEmpty(preferredPrefabPath) && prefabPath == preferredPrefabPath;

                if (removeAllNonPersistent && !isPreferredPrefab)
                {
                    var count = 0;
                    foreach (var l in listeners)
                    {
                        if (l != null && CleanupAssetUtility.TryDestroyComponent(l))
                            count++;
                    }
                    if (count > 0)
                        if (!CleanupAssetUtility.TrySavePrefabContents(root, prefabPath))
                            return 0;
                    return count;
                }

                if (listeners.Count <= 1)
                    return 0;

                var keeper = listeners.FirstOrDefault(l => l.GetComponent<Camera>() != null) ?? listeners[0];
                var removed = 0;

                foreach (var listener in listeners)
                {
                    if (listener == null || listener == keeper)
                        continue;

                    if (CleanupAssetUtility.TryDestroyComponent(listener))
                        removed++;
                }

                if (removed > 0)
                    if (!CleanupAssetUtility.TrySavePrefabContents(root, prefabPath))
                        return 0;

                return removed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static string GetPreferredPrefabPath()
        {
            var settings = BootstrapSceneTools.FindSettings();
            var preferred = settings?.PersistentPrefabs?.FirstOrDefault(p => p != null);
            return preferred == null ? null : AssetDatabase.GetAssetPath(preferred);
        }

        private static AudioListener FindPreferredListener(List<AudioListener> listeners, string preferredPrefabPath)
        {
            if (string.IsNullOrEmpty(preferredPrefabPath))
                return null;

            foreach (var listener in listeners)
            {
                if (listener == null) continue;
                var root = PrefabUtility.GetNearestPrefabInstanceRoot(listener.gameObject);
                if (root == null)
                    continue;

                var sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                if (sourcePath == preferredPrefabPath)
                    return listener;
            }

            return null;
        }

        #endregion
    }
}
