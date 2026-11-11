using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// TMPro.TextContainer is obsolete — TextMeshPro objects created with older package versions
    /// carry one, and it logs "The Text Container component is now Obsolete and can safely be
    /// removed from [X]." on every Awake. This scans every scene and prefab in the project and
    /// removes it wherever it's still attached.
    /// </summary>
    public static class TMPTextContainerCleaner
    {
        public struct ScanResult
        {
            public int ObsoleteContainerCount;
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

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/TextMesh Pro/Remove Obsolete TMP TextContainer (Active Scene Only)", priority = 52)]
        public static void RunOnActiveScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var fixedInScene = FixInCurrentScene();

            if (fixedInScene > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            var message = $"Removed {fixedInScene} obsolete TextContainer(s) from '{scene.name}'.";
            Debug.Log($"[TMPTextContainerCleaner] {message}");
            EditorUtility.DisplayDialog("Remove Obsolete TMP TextContainer", message, "OK");
        }

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/TextMesh Pro/Remove Obsolete TMP TextContainer (All Scenes && Prefabs)", priority = 53)]
        public static void RunOnProjectMenuItem()
        {
            if (!EditorUtility.DisplayDialog("Remove Obsolete TMP TextContainer",
                    "This opens every scene and prefab in the project, removes any obsolete " +
                    "TMPro.TextContainer component it finds, and saves anything that changed.\n\n" +
                    "Make sure your work is saved/committed first.",
                    "Proceed", "Cancel"))
                return;

            var result = CleanAll();
            var message = $"Removed {result.ObjectsFixed} obsolete TextContainer(s) — {result.ScenesChanged} scene(s), {result.PrefabsChanged} prefab(s) changed.";
            Debug.Log($"[TMPTextContainerCleaner] {message}");
            EditorUtility.DisplayDialog("Remove Obsolete TMP TextContainer", message, "OK");
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
            CleanupRunner.RunBlocking("Scanning obsolete TMP TextContainers", p => ScanProjectRoutine(p, r => scan = r));
            return scan;
        }

        /// <summary>Time-sliced scan: run it with <see cref="CleanupRunner.Start"/>; yields after every asset.</summary>
        public static System.Collections.IEnumerator ScanProjectRoutine(CleanupProgress progress, System.Action<ScanResult> done)
        {
            var sweep = new SweepScan();
            yield return ProjectSweep.Scan(progress, "Scanning obsolete TMP TextContainers", sweep,
                CountInCurrentScene, CountInPrefab,
                "TMP TextContainer", "obsolete TextContainer(s)",
                count => $"{count} obsolete TextContainer(s) on TextMeshPro objects");

            done(new ScanResult
            {
                ObsoleteContainerCount = sweep.Total,
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
            CleanupRunner.RunBlocking("Removing obsolete TMP TextContainers", p => CleanAllRoutine(p, r => result = r));
            return result;
        }

        /// <summary>Time-sliced fix: run it with <see cref="CleanupRunner.Start"/>; yields after every asset.</summary>
        public static System.Collections.IEnumerator CleanAllRoutine(CleanupProgress progress, System.Action<CleanResult> done)
        {
            var sweep = new SweepFix();
            yield return ProjectSweep.Fix(progress, "Removing obsolete TMP TextContainers", sweep,
                FixInCurrentScene, CountInPrefab, FixInPrefab, "obsolete TextContainer(s)");

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
            var containers = Object.FindObjectsByType<TextContainer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var containers = Object.FindObjectsOfType<TextContainer>(true);
#endif
            foreach (var container in containers)
                fixedCount += RemoveContainer(container);
            return fixedCount;
        }

        private static int CountInCurrentScene()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindObjectsByType<TextContainer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
#else
            return Object.FindObjectsOfType<TextContainer>(true).Length;
#endif
        }

        private static int CountInPrefab(string prefabPath)
        {
            // Inspect the asset directly so scans do not run user Awake/OnEnable methods.
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            return root == null ? 0 : root.GetComponentsInChildren<TextContainer>(true).Length;
        }

        private static int FixInPrefab(string prefabPath)
        {
            var root = CleanupAssetUtility.LoadPrefabContents(prefabPath);
            if (root == null) return 0;

            try
            {
                int fixedCount = 0;
                foreach (var container in root.GetComponentsInChildren<TextContainer>(true))
                    fixedCount += RemoveContainer(container);

                if (fixedCount > 0)
                    if (!CleanupAssetUtility.TrySavePrefabContents(root, prefabPath))
                        return 0;

                return fixedCount;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static int RemoveContainer(TextContainer container)
        {
            return CleanupAssetUtility.TryDestroyComponent(container) ? 1 : 0;
        }

        #endregion
    }
}
