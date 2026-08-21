#if WAGENHEIMER_UNITYUTILS_IAP
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Purchasing;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// If your purchase flow talks to <c>UnityIAPServices.StoreController()</c> directly and never
    /// wires up Codeless IAPButton events, leftover IAPButton components (e.g. from an old codeless
    /// setup) still trigger CodelessIAPStoreListener's automatic RuntimeInitializeOnLoadMethod init,
    /// producing "IStoreService.Connect called without a callback defined..." warnings on every
    /// play. This removes IAPButton components whose onPurchaseComplete/onPurchaseFailed/
    /// onTransactionsRestored are all empty — i.e. ones doing nothing — and leaves any IAPButton
    /// with real listeners untouched (logging a warning so you can double check).
    /// </summary>
    public static class UnusedIAPButtonCleaner
    {
        [MenuItem("Tools/Unity Utils/Cleanup/Remove Unused IAPButton Components (Active Scene Only)")]
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

        [MenuItem("Tools/Unity Utils/Cleanup/Remove Unused IAPButton Components (All Scenes && Prefabs)")]
        public static void RunOnProject()
        {
            if (!EditorUtility.DisplayDialog("Remove Unused IAPButton Components",
                    "This opens every scene and prefab in the project and removes any IAPButton " +
                    "component whose onPurchaseComplete/onPurchaseFailed/onTransactionsRestored events " +
                    "are all empty (i.e. not actually wired to anything).\n\nMake sure your work is " +
                    "saved/committed first.",
                    "Proceed", "Cancel"))
                return;

            int objectsFixed = 0;
            int scenesChanged = 0;
            int prefabsChanged = 0;

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
                    if (EditorUtility.DisplayCancelableProgressBar("Removing unused IAPButton components",
                            $"Scene: {path}", (float)i / (scenePaths.Length * 2)))
                        break;

                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    var fixedInScene = FixInCurrentScene();
                    if (fixedInScene > 0)
                    {
                        objectsFixed += fixedInScene;
                        scenesChanged++;
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
                    if (EditorUtility.DisplayCancelableProgressBar("Removing unused IAPButton components",
                            $"Prefab: {path}", 0.5f + (float)i / (prefabPaths.Length * 2)))
                        break;

                    var fixedInPrefab = FixInPrefab(path);
                    if (fixedInPrefab > 0)
                    {
                        objectsFixed += fixedInPrefab;
                        prefabsChanged++;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (originalSetup is { Length: > 0 })
                    EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
            }

            var message = $"Removed {objectsFixed} unused IAPButton component(s) — {scenesChanged} scene(s), {prefabsChanged} prefab(s) changed.";
            Debug.Log($"[UnusedIAPButtonCleaner] {message}");
            EditorUtility.DisplayDialog("Remove Unused IAPButton Components", message, "OK");
        }

        private static int FixInCurrentScene()
        {
            int fixedCount = 0;
            foreach (var button in Object.FindObjectsOfType<IAPButton>(true))
                fixedCount += RemoveIfUnused(button);
            return fixedCount;
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

        private static int RemoveIfUnused(IAPButton button)
        {
            if (button == null) return 0;

            var so = new SerializedObject(button);
            var hasListeners =
                so.FindProperty("onPurchaseComplete.m_PersistentCalls.m_Calls").arraySize > 0 ||
                so.FindProperty("onPurchaseFailed.m_PersistentCalls.m_Calls").arraySize > 0 ||
                so.FindProperty("onTransactionsRestored.m_PersistentCalls.m_Calls").arraySize > 0;

            if (hasListeners)
            {
                Debug.LogWarning($"[UnusedIAPButtonCleaner] Skipped IAPButton on '{button.gameObject.name}' " +
                                  "(productId: " + button.productId + ") — it has wired UnityEvent listeners.");
                return 0;
            }

            Object.DestroyImmediate(button, true);
            return 1;
        }
    }
}
#endif
