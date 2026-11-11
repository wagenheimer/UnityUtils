using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

#if WAGENHEIMER_UNITYUTILS_IAP
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine.Events;
using UnityEngine.Purchasing;
#endif

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// If your purchase flow talks to <c>UnityIAPServices.StoreController()</c> directly and never
    /// wires up Codeless IAPButton events, leftover IAPButton components (e.g. from an old codeless
    /// setup) still trigger CodelessIAPStoreListener's automatic RuntimeInitializeOnLoadMethod init,
    /// producing "IStoreService.Connect called without a callback defined..." warnings on every
    /// play. This removes IAPButton components whose UnityEvent fields (onPurchaseComplete,
    /// onOrderConfirmed, onPurchaseFailed, ... — inspected via reflection so this keeps working
    /// across com.unity.purchasing versions that rename/add/remove event fields) are all empty —
    /// i.e. ones doing nothing — and leaves any IAPButton with real listeners untouched.
    /// </summary>
    public static class UnusedIAPButtonCleaner
    {
        public struct ScanResult
        {
            public int UnusedComponentCount;
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

#if WAGENHEIMER_UNITYUTILS_IAP
        #region Menu Items

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/In-App Purchasing/Remove Unused IAPButton Components (Active Scene Only)", priority = 60)]
        public static void RunOnActiveScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var fixedInScene = FixInCurrentScene();

            if (fixedInScene > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            var message = $"Removed {fixedInScene} unused IAPButton component(s) from '{scene.name}'.";
            Debug.Log($"[UnusedIAPButtonCleaner] {message}");
            EditorUtility.DisplayDialog("Remove Unused IAPButton Components", message, "OK");
        }

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/In-App Purchasing/Remove Unused IAPButton Components (All Scenes && Prefabs)", priority = 61)]
        public static void RunOnProjectMenuItem()
        {
            if (!EditorUtility.DisplayDialog("Remove Unused IAPButton Components",
                    "This opens every scene and prefab in the project and removes any IAPButton " +
                    "component whose events are all empty (i.e. not actually wired to anything).\n\n" +
                    "Make sure your work is saved/committed first.",
                    "Proceed", "Cancel"))
                return;

            var result = CleanAll();
            var message = $"Removed {result.ObjectsFixed} unused IAP component(s) — {result.ScenesChanged} scene(s), {result.PrefabsChanged} prefab(s) changed.";
            Debug.Log($"[UnusedIAPButtonCleaner] {message}");
            EditorUtility.DisplayDialog("Remove Unused IAPButton Components", message, "OK");
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
            CleanupRunner.RunBlocking("Scanning unused IAP buttons", p => ScanProjectRoutine(p, r => scan = r));
            return scan;
        }

        /// <summary>Time-sliced scan: run it with <see cref="CleanupRunner.Start"/>; yields after every asset.</summary>
        public static System.Collections.IEnumerator ScanProjectRoutine(CleanupProgress progress, System.Action<ScanResult> done)
        {
            var sweep = new SweepScan();
            yield return ProjectSweep.Scan(progress, "Scanning unused IAP buttons", sweep,
                CountUnusedInCurrentScene, CountUnusedInPrefab,
                "IAP", "unused IAP component(s)",
                count => $"{count} unused Codeless IAPButton component(s)");

            done(new ScanResult
            {
                UnusedComponentCount = sweep.Total,
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
            CleanupRunner.RunBlocking("Removing unused IAP buttons", p => CleanAllRoutine(p, r => result = r));
            return result;
        }

        /// <summary>Time-sliced fix: run it with <see cref="CleanupRunner.Start"/>; yields after every asset.</summary>
        public static System.Collections.IEnumerator CleanAllRoutine(CleanupProgress progress, System.Action<CleanResult> done)
        {
            var sweep = new SweepFix();
            yield return ProjectSweep.Fix(progress, "Removing unused IAP buttons", sweep,
                FixInCurrentScene, CountUnusedInPrefab, FixInPrefab, "unused IAPButton component(s)");

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
            var buttons = Object.FindObjectsByType<IAPButton>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var buttons = Object.FindObjectsOfType<IAPButton>(true);
#endif
            foreach (var button in buttons)
                fixedCount += RemoveIfUnused(button);
            return fixedCount;
        }

        private static int CountUnusedInCurrentScene()
        {
            int count = 0;
#if UNITY_2023_1_OR_NEWER
            var buttons = Object.FindObjectsByType<IAPButton>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var buttons = Object.FindObjectsOfType<IAPButton>(true);
#endif
            foreach (var button in buttons)
            {
                if (IsUnused(button)) count++;
            }
            return count;
        }

        private static int CountUnusedInPrefab(string prefabPath)
        {
            // Inspect the asset directly so scans do not run user Awake/OnEnable methods.
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (root == null) return 0;

            int count = 0;
            foreach (var button in root.GetComponentsInChildren<IAPButton>(true))
            {
                if (IsUnused(button)) count++;
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
                foreach (var button in root.GetComponentsInChildren<IAPButton>(true))
                    fixedCount += RemoveIfUnused(button);

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

        private static readonly FieldInfo[] EventFields = typeof(IAPButton)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => typeof(UnityEventBase).IsAssignableFrom(f.FieldType))
            .ToArray();

        private static bool IsUnused(IAPButton button)
        {
            if (button == null) return false;
            var so = new SerializedObject(button);
            var hasListeners = EventFields.Any(field =>
            {
                var property = so.FindProperty($"{field.Name}.m_PersistentCalls.m_Calls");
                return property is { arraySize: > 0 };
            });
            return !hasListeners;
        }

        private static int RemoveIfUnused(IAPButton button)
        {
            if (button == null) return 0;

            if (!IsUnused(button))
            {
                Debug.LogWarning($"[UnusedIAPButtonCleaner] Skipped IAPButton on '{button.gameObject.name}' " +
                                  $"(productId: {button.productId}) — it has wired UnityEvent listeners.");
                return 0;
            }

            return CleanupAssetUtility.TryDestroyComponent(button) ? 1 : 0;
        }

        #endregion
#else
        public static ScanResult ScanProject()
        {
            return new ScanResult { Details = new List<string>() };
        }

        public static CleanResult CleanAll()
        {
            return new CleanResult();
        }
#endif
    }
}
