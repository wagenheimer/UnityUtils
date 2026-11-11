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
            public List<CleanupIssueItem> Items;
        }

        public struct CleanResult
        {
            public int ObjectsFixed;
            public int ScenesChanged;
            public int PrefabsChanged;
            public List<CleanupModificationRecord> Modifications;
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

        /// <summary>
        /// Synchronous scan for menu items and API callers (shows a modal progress bar with Cancel).
        /// Prefer <see cref="ScanProjectRoutine"/> from UI code so the editor stays responsive.
        /// </summary>
        public static ScanResult ScanProject()
        {
            var scan = default(ScanResult);
            CleanupRunner.RunBlocking("Scanning redundant TMP CanvasRenderers", p => ScanProjectRoutine(p, r => scan = r));
            return scan;
        }

        /// <summary>Time-sliced scan: run it with <see cref="CleanupRunner.Start"/>; yields after every asset.</summary>
        public static System.Collections.IEnumerator ScanProjectRoutine(CleanupProgress progress, System.Action<ScanResult> done)
        {
            var sweep = new SweepScan();
            yield return ProjectSweep.Scan(progress, "Scanning redundant TMP CanvasRenderers", sweep,
                CountInCurrentScene, CountInPrefab,
                "TMP CanvasRenderer", "redundant CanvasRenderer(s)",
                count => $"{count} redundant CanvasRenderer(s) on world-space TextMeshPro");

            done(new ScanResult
            {
                RedundantRendererCount = sweep.Total,
                AffectedScenesCount = sweep.Scenes,
                AffectedPrefabsCount = sweep.Prefabs,
                Details = sweep.Details,
                Items = sweep.Items
            });
        }

        /// <summary>
        /// Synchronous fix for menu items and API callers (shows a modal progress bar with Cancel).
        /// Prefer <see cref="CleanAllRoutine"/> from UI code so the editor stays responsive.
        /// </summary>
        public static CleanResult CleanAll()
        {
            var result = default(CleanResult);
            CleanupRunner.RunBlocking("Removing redundant TMP CanvasRenderers", p => CleanAllRoutine(p, r => result = r));
            return result;
        }

        /// <summary>Time-sliced fix: run it with <see cref="CleanupRunner.Start"/>; yields after every asset.</summary>
        public static System.Collections.IEnumerator CleanAllRoutine(CleanupProgress progress, System.Action<CleanResult> done)
        {
            var sweep = new SweepFix();
            yield return ProjectSweep.Fix(progress, "Removing redundant TMP CanvasRenderers", sweep,
                FixInCurrentScene, CountInPrefab, FixInPrefab, "redundant CanvasRenderer(s)");

            done(new CleanResult
            {
                ObjectsFixed = sweep.Fixed,
                ScenesChanged = sweep.Scenes,
                PrefabsChanged = sweep.Prefabs,
                Modifications = sweep.Modifications
            });
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
                {
                    if (!CleanupAssetUtility.TrySavePrefabContents(root, prefabPath))
                        return 0;

                    // Verify what was written: reload the asset and confirm none are left.
                    var remaining = CountInPrefab(prefabPath);
                    if (remaining > 0)
                    {
                        Debug.LogWarning($"[TMPCanvasRendererCleaner] '{prefabPath}': {remaining} CanvasRenderer(s) " +
                                         "still present after saving (the prefab may be locked or read-only).");
                        return fixedCount - remaining;
                    }
                }

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

            // Only counts as removed when the component is really gone (TryDestroyComponent verifies it).
            return CleanupAssetUtility.TryDestroyComponent(renderer) ? 1 : 0;
        }

        private static bool RequiresCanvasRenderer(GameObject go)
        {
            // Note: TMPro.TextMeshPro (3D) inherits TMP_Text -> MaskableGraphic -> Graphic. A plain
            // GetComponent<Graphic>() used to find the TextMeshPro itself and never removed anything.
            // Only a Graphic that is NOT the world-space TextMeshPro keeps the CanvasRenderer.
            foreach (var graphic in go.GetComponents<Graphic>())
            {
                if (graphic == null) continue;          // missing script
                if (graphic is TextMeshPro) continue;   // world-space text: does not use a Canvas
                return true;                            // Image, RawImage, TextMeshProUGUI, Text...
            }

            return false;
        }

        #endregion
    }
}
