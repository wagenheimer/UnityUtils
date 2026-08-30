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
        }

        public struct CleanResult
        {
            public int ObjectsFixed;
            public int ScenesChanged;
            public int PrefabsChanged;
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

        public static ScanResult ScanProject()
        {
            var scan = new ScanResult
            {
                Details = new List<string>()
            };

            var originalSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scenePaths = AssetDatabase.FindAssets("t:Scene")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Distinct()
                    .OrderBy(p => p)
                    .ToArray();

                for (int i = 0; i < scenePaths.Length; i++)
                {
                    var path = scenePaths[i];
                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    var count = CountUnusedInCurrentScene();
                    if (count > 0)
                    {
                        scan.UnusedComponentCount += count;
                        scan.AffectedScenesCount++;
                        scan.Details.Add($"[Scene] {path}: {count} unused IAP component(s)");
                    }
                }

                var prefabPaths = AssetDatabase.FindAssets("t:Prefab")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Distinct()
                    .OrderBy(p => p)
                    .ToArray();

                for (int i = 0; i < prefabPaths.Length; i++)
                {
                    var path = prefabPaths[i];
                    var count = CountUnusedInPrefab(path);
                    if (count > 0)
                    {
                        scan.UnusedComponentCount += count;
                        scan.AffectedPrefabsCount++;
                        scan.Details.Add($"[Prefab] {path}: {count} unused IAP component(s)");
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
            var originalSetup = EditorSceneManager.GetSceneManagerSetup();

            try
            {
                var scenePaths = AssetDatabase.FindAssets("t:Scene")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Distinct()
                    .OrderBy(p => p)
                    .ToArray();

                for (int i = 0; i < scenePaths.Length; i++)
                {
                    var path = scenePaths[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Removing unused IAP components",
                            $"Scene: {path}", (float)i / (scenePaths.Length * 2)))
                        break;

                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    var fixedInScene = FixInCurrentScene();
                    if (fixedInScene > 0)
                    {
                        result.ObjectsFixed += fixedInScene;
                        result.ScenesChanged++;
                        EditorSceneManager.MarkSceneDirty(scene);
                        EditorSceneManager.SaveScene(scene);
                    }
                }

                var prefabPaths = AssetDatabase.FindAssets("t:Prefab")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Distinct()
                    .OrderBy(p => p)
                    .ToArray();

                for (int i = 0; i < prefabPaths.Length; i++)
                {
                    var path = prefabPaths[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Removing unused IAP components",
                            $"Prefab: {path}", 0.5f + (float)i / (prefabPaths.Length * 2)))
                        break;

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
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                int count = 0;
                foreach (var button in root.GetComponentsInChildren<IAPButton>(true))
                {
                    if (IsUnused(button)) count++;
                }
                return count;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static int FixInPrefab(string prefabPath)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                int fixedCount = 0;
                foreach (var button in root.GetComponentsInChildren<IAPButton>(true))
                    fixedCount += RemoveIfUnused(button);

                if (fixedCount > 0)
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

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

            Object.DestroyImmediate(button, true);
            return 1;
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
