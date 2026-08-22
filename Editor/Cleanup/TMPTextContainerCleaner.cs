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
        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Remove Obsolete TMP TextContainer (Active Scene Only)")]
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

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Remove Obsolete TMP TextContainer (All Scenes && Prefabs)")]
        public static void RunOnProject()
        {
            if (!EditorUtility.DisplayDialog("Remove Obsolete TMP TextContainer",
                    "This opens every scene and prefab in the project, removes any obsolete " +
                    "TMPro.TextContainer component it finds, and saves anything that changed.\n\n" +
                    "Make sure your work is saved/committed first — this can't be undone with " +
                    "Ctrl+Z once a scene or prefab is saved.",
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
                        objectsFixed += fixedInScene;
                        scenesChanged++;
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

            var message = $"Removed {objectsFixed} obsolete TextContainer(s) — {scenesChanged} scene(s), {prefabsChanged} prefab(s) changed.";
            Debug.Log($"[TMPTextContainerCleaner] {message}");
            EditorUtility.DisplayDialog("Remove Obsolete TMP TextContainer", message, "OK");
        }

        private static int FixInCurrentScene()
        {
            int fixedCount = 0;
            foreach (var container in Object.FindObjectsOfType<TextContainer>(true))
                fixedCount += RemoveContainer(container);
            return fixedCount;
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
    }
}

