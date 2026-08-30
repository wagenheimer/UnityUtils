using System.Collections.Generic;
using System.Linq;
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
        }

        public struct CleanResult
        {
            public int ObjectsFixed;
            public int ScenesChanged;
            public int PrefabsChanged;
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
                    var count = CountInCurrentScene();
                    if (count > 0)
                    {
                        scan.ObsoleteContainerCount += count;
                        scan.AffectedScenesCount++;
                        scan.Details.Add($"[Scene] {path}: {count} obsolete TextContainer(s)");
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
                    var count = CountInPrefab(path);
                    if (count > 0)
                    {
                        scan.ObsoleteContainerCount += count;
                        scan.AffectedPrefabsCount++;
                        scan.Details.Add($"[Prefab] {path}: {count} obsolete TextContainer(s)");
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

                bool cancelled = false;

                for (int i = 0; i < scenePaths.Length && !cancelled; i++)
                {
                    var path = scenePaths[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Removing obsolete TMP TextContainer",
                            $"Scene: {path}", (float)i / (scenePaths.Length * 2)))
                    {
                        cancelled = true;
                        break;
                    }

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

                var prefabPaths = cancelled
                    ? System.Array.Empty<string>()
                    : AssetDatabase.FindAssets("t:Prefab")
                        .Select(AssetDatabase.GUIDToAssetPath)
                        .Distinct()
                        .OrderBy(p => p)
                        .ToArray();

                for (int i = 0; i < prefabPaths.Length && !cancelled; i++)
                {
                    var path = prefabPaths[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Removing obsolete TMP TextContainer",
                            $"Prefab: {path}", 0.5f + (float)i / (prefabPaths.Length * 2)))
                    {
                        cancelled = true;
                        break;
                    }

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
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                return root.GetComponentsInChildren<TextContainer>(true).Length;
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
                foreach (var container in root.GetComponentsInChildren<TextContainer>(true))
                    fixedCount += RemoveContainer(container);

                if (fixedCount > 0)
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

                return fixedCount;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static int RemoveContainer(TextContainer container)
        {
            if (container == null) return 0;

            Object.DestroyImmediate(container, true);
            return 1;
        }

        #endregion
    }
}
