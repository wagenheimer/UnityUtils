using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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
        [MenuItem("Tools/Unity Utils/Cleanup/Remove Redundant TMP CanvasRenderer (Active Scene Only)")]
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

        [MenuItem("Tools/Unity Utils/Cleanup/Remove Redundant TMP CanvasRenderer (All Scenes && Prefabs)")]
        public static void RunOnProject()
        {
            if (!EditorUtility.DisplayDialog("Remove Redundant TMP CanvasRenderer",
                    "This opens every scene and prefab in the project, removes any CanvasRenderer " +
                    "sitting on the same GameObject as a world-space TextMeshPro component, and saves " +
                    "anything that changed.\n\nMake sure your work is saved/committed first — this can't " +
                    "be undone with Ctrl+Z once a scene or prefab is saved.",
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
                    if (EditorUtility.DisplayCancelableProgressBar("Removing redundant TMP CanvasRenderer",
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
                    if (EditorUtility.DisplayCancelableProgressBar("Removing redundant TMP CanvasRenderer",
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

            var message = $"Removed {objectsFixed} redundant CanvasRenderer(s) — {scenesChanged} scene(s), {prefabsChanged} prefab(s) changed.";
            Debug.Log($"[TMPCanvasRendererCleaner] {message}");
            EditorUtility.DisplayDialog("Remove Redundant TMP CanvasRenderer", message, "OK");
        }

        private static int FixInCurrentScene()
        {
            int fixedCount = 0;
            foreach (var tmp in Object.FindObjectsOfType<TextMeshPro>(true))
                fixedCount += RemoveIfRedundant(tmp.gameObject);
            return fixedCount;
        }

        private static int FixInPrefab(string prefabPath)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                int fixedCount = 0;
                foreach (var tmp in root.GetComponentsInChildren<TextMeshPro>(true))
                    fixedCount += RemoveIfRedundant(tmp.gameObject);

                if (fixedCount > 0)
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

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

            Object.DestroyImmediate(renderer, true);
            return 1;
        }
    }
}
