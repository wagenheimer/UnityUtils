using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// World-space TextMeshPro (TMPro.TextMeshPro, not TextMeshProUGUI) doesn't use a
    /// CanvasRenderer — only the UGUI variant does. If one ends up on the same GameObject
    /// anyway (usually from copy/pasting a UI text object and swapping its component), TMP logs:
    /// "Please remove the CanvasRenderer component from the [X] GameObject as this component is
    /// no longer necessary." every time that object wakes up.
    ///
    /// This scans every scene and prefab in the project and removes the redundant CanvasRenderer
    /// wherever it's sitting next to a world-space TextMeshPro.
    /// </summary>
    public static class TMPCanvasRendererCleaner
    {
        public struct ScanResult
        {
            public int RedundantRendererCount;
            public int AffectedScenesCount;
            public int AffectedPrefabsCount;
            public List<string> Details;
        }

        public struct CleanResult
        {
            public int ObjectsFixed;
            public int ScenesChanged;
            public int PrefabsChanged;
        }

        #region Menu Items

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/TextMesh Pro/Remove Redundant TMP CanvasRenderer (Active Scene Only)", priority = 50)]
        public static void RunOnActiveScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var fixedInScene = FixInCurrentScene();

            if (fixedInScene > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            var message = $"Removed {fixedInScene} redundant CanvasRenderer(s) from '{scene.name}'.";
            Debug.Log($"[TMPCanvasRendererCleaner] {message}");
            EditorUtility.DisplayDialog("Remove Redundant TMP CanvasRenderer", message, "OK");
        }

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/TextMesh Pro/Remove Redundant TMP CanvasRenderer (All Scenes && Prefabs)", priority = 51)]
        public static void RunOnProjectMenuItem()
        {
            if (!EditorUtility.DisplayDialog("Remove Redundant TMP CanvasRenderer",
                    "This opens every scene and prefab in the project, removes any CanvasRenderer " +
                    "sitting on the same GameObject as a world-space TextMeshPro component, and saves " +
                    "anything that changed.\n\nMake sure your work is saved/committed first.",
                    "Proceed", "Cancel"))
                return;

            var result = CleanAll();
            var message = $"Removed {result.ObjectsFixed} redundant CanvasRenderer(s) — {result.ScenesChanged} scene(s), {result.PrefabsChanged} prefab(s) changed.";
            Debug.Log($"[TMPCanvasRendererCleaner] {message}");
            EditorUtility.DisplayDialog("Remove Redundant TMP CanvasRenderer", message, "OK");
        }

        #endregion

        #region Scan & Clean Logic

        public static ScanResult ScanProject()
        {
            var scan = new ScanResult
            {
                Details = new List<string>()
            };

            if (!CleanupAssetUtility.SaveCurrentScenesIfNeeded())
                return scan;

            var originalSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scenePaths = CleanupAssetUtility.GetProjectScenePaths();

                for (int i = 0; i < scenePaths.Length; i++)
                {
                    var path = scenePaths[i];
                    if (!CleanupAssetUtility.TryOpenScene(path, out _))
                        continue;

                    var count = CountInCurrentScene();
                    if (count > 0)
                    {
                        scan.RedundantRendererCount += count;
                        scan.AffectedScenesCount++;
                        scan.Details.Add($"[Scene] {path}: {count} redundant CanvasRenderer(s)");
                    }
                }

                var prefabPaths = CleanupAssetUtility.GetProjectPrefabPaths();

                for (int i = 0; i < prefabPaths.Length; i++)
                {
                    var path = prefabPaths[i];
                    var count = CountInPrefab(path);
                    if (count > 0)
                    {
                        scan.RedundantRendererCount += count;
                        scan.AffectedPrefabsCount++;
                        scan.Details.Add($"[Prefab] {path}: {count} redundant CanvasRenderer(s)");
                    }
                }
            }
            finally
            {
                if (originalSetup != null && originalSetup.Length > 0)
                    EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
            }

            return scan;
        }

        public static CleanResult CleanAll()
        {
            var result = new CleanResult();
            if (!CleanupAssetUtility.SaveCurrentScenesIfNeeded())
                return result;

            var originalSetup = EditorSceneManager.GetSceneManagerSetup();

            try
            {
                var scenePaths = CleanupAssetUtility.GetProjectScenePaths();

                bool cancelled = false;

                for (int i = 0; i < scenePaths.Length && !cancelled; i++)
                {
                    var path = scenePaths[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Removing redundant TMP CanvasRenderer",
                            $"Scene: {path}", (float)i / (scenePaths.Length * 2)))
                    {
                        cancelled = true;
                        break;
                    }

                    if (!CleanupAssetUtility.TryOpenScene(path, out var scene))
                        continue;

                    var fixedInScene = FixInCurrentScene();
                    if (fixedInScene > 0)
                    {
                        result.ObjectsFixed += fixedInScene;
                        result.ScenesChanged++;
                        EditorSceneManager.MarkSceneDirty(scene);
                        EditorSceneManager.SaveScene(scene);
                    }
                }

                var prefabPaths = cancelled
                    ? System.Array.Empty<string>()
                    : CleanupAssetUtility.GetProjectPrefabPaths();

                for (int i = 0; i < prefabPaths.Length && !cancelled; i++)
                {
                    var path = prefabPaths[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Removing redundant TMP CanvasRenderer",
                            $"Prefab: {path}", 0.5f + (float)i / (prefabPaths.Length * 2)))
                    {
                        cancelled = true;
                        break;
                    }

                    // Inspect the prefab asset directly first. Loading every prefab's contents
                    // runs ExecuteInEditMode Awake methods (and logs whatever they log), which
                    // makes a no-op clean extremely noisy.
                    if (CountInPrefab(path) == 0)
                        continue;

                    var fixedInPrefab = FixInPrefab(path);
                    if (fixedInPrefab > 0)
                    {
                        result.ObjectsFixed += fixedInPrefab;
                        result.PrefabsChanged++;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (originalSetup != null && originalSetup.Length > 0)
                    EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
            }

            return result;
        }

        private static int FixInCurrentScene()
        {
            int fixedCount = 0;
#if UNITY_2023_1_OR_NEWER
            var tmps = Object.FindObjectsByType<TextMeshPro>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var tmps = Object.FindObjectsOfType<TextMeshPro>(true);
#endif
            foreach (var tmp in tmps)
                fixedCount += RemoveIfRedundant(tmp.gameObject);
            return fixedCount;
        }

        private static int CountInCurrentScene()
        {
            int count = 0;
#if UNITY_2023_1_OR_NEWER
            var tmps = Object.FindObjectsByType<TextMeshPro>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var tmps = Object.FindObjectsOfType<TextMeshPro>(true);
#endif
            foreach (var tmp in tmps)
            {
                if (tmp.GetComponent<CanvasRenderer>() != null && !RequiresCanvasRenderer(tmp.gameObject))
                    count++;
            }
            return count;
        }

        private static int CountInPrefab(string prefabPath)
        {
            // Read the prefab asset itself (not an instantiated copy) so scanning never runs
            // Awake/OnEnable on user scripts.
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (root == null) return 0;

            int count = 0;
            foreach (var tmp in root.GetComponentsInChildren<TextMeshPro>(true))
            {
                if (tmp.GetComponent<CanvasRenderer>() != null && !RequiresCanvasRenderer(tmp.gameObject))
                    count++;
            }
            return count;
        }

        private static int FixInPrefab(string prefabPath)
        {
            var root = CleanupAssetUtility.LoadPrefabContents(prefabPath);
            if (root == null) return 0;

            try
            {
                int fixedCount = 0;
                foreach (var tmp in root.GetComponentsInChildren<TextMeshPro>(true))
                    fixedCount += RemoveIfRedundant(tmp.gameObject);

                if (fixedCount > 0)
                    CleanupAssetUtility.TrySavePrefabContents(root, prefabPath);

                return fixedCount;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static int RemoveIfRedundant(GameObject go)
        {
            var renderer = go.GetComponent<CanvasRenderer>();
            if (renderer == null) return 0;

            // A CanvasRenderer is only redundant when nothing else on this GameObject renders
            // through the Canvas. Never strip it from a UGUI element that also happens to carry
            // a world-space TextMeshPro component.
            if (RequiresCanvasRenderer(go)) return 0;

            Object.DestroyImmediate(renderer, true);
            return 1;
        }

        private static bool RequiresCanvasRenderer(GameObject go)
        {
            return go.GetComponent<TextMeshProUGUI>() != null || go.GetComponent<Graphic>() != null;
        }

        #endregion
    }
}
