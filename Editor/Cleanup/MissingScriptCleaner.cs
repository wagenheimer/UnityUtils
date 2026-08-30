using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Finds and removes missing MonoBehaviour script components from GameObjects
    /// across active scenes, all scenes, and all prefabs in the project.
    ///
    /// Fixes "The referenced script on this Behaviour is missing!" and
    /// "Component at index X could not be loaded" console warnings.
    /// </summary>
    public static class MissingScriptCleaner
    {
        public struct ScanResult
        {
            public int MissingScriptCount;
            public int AffectedScenesCount;
            public int AffectedPrefabsCount;
            public List<string> Details;
        }

        public struct CleanResult
        {
            public int MissingScriptsRemoved;
            public int ScenesModified;
            public int PrefabsModified;
        }

        #region Menu Items

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Missing Scripts/Remove Missing Scripts (Active Scene Only)", priority = 20)]
        public static void RunOnActiveScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var removed = CleanScene(scene);

            if (removed > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            var msg = $"Removed {removed} missing script(s) from '{scene.name}'.";
            Debug.Log($"[MissingScriptCleaner] {msg}");
            EditorUtility.DisplayDialog("Remove Missing Scripts", msg, "OK");
        }

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Missing Scripts/Remove Missing Scripts (All Scenes)", priority = 21)]
        public static void RunOnAllScenes()
        {
            if (!EditorUtility.DisplayDialog("Remove Missing Scripts (All Scenes)",
                    "This opens every scene in the project and removes all missing MonoBehaviour scripts.\n\n" +
                    "Make sure your work is saved before proceeding.", "Proceed", "Cancel"))
                return;

            var result = CleanAllScenes();
            var msg = $"Removed {result.MissingScriptsRemoved} missing script(s) across {result.ScenesModified} scene(s).";
            Debug.Log($"[MissingScriptCleaner] {msg}");
            EditorUtility.DisplayDialog("Remove Missing Scripts", msg, "OK");
        }

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Missing Scripts/Remove Missing Scripts (All Prefabs)", priority = 22)]
        public static void RunOnAllPrefabs()
        {
            if (!EditorUtility.DisplayDialog("Remove Missing Scripts (All Prefabs)",
                    "This scans every prefab in the project and removes all missing MonoBehaviour scripts.\n\n" +
                    "Make sure your work is saved before proceeding.", "Proceed", "Cancel"))
                return;

            var result = CleanAllPrefabs();
            var msg = $"Removed {result.MissingScriptsRemoved} missing script(s) across {result.PrefabsModified} prefab(s).";
            Debug.Log($"[MissingScriptCleaner] {msg}");
            EditorUtility.DisplayDialog("Remove Missing Scripts", msg, "OK");
        }

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Missing Scripts/Remove Missing Scripts (All Scenes && Prefabs)", priority = 23)]
        public static void RunOnAllScenesAndPrefabs()
        {
            if (!EditorUtility.DisplayDialog("Remove Missing Scripts",
                    "This opens every scene and prefab in the project, removes all missing MonoBehaviour scripts, and saves modified assets.\n\n" +
                    "Make sure your work is saved before proceeding.", "Proceed", "Cancel"))
                return;

            var sceneRes = CleanAllScenes();
            var prefabRes = CleanAllPrefabs();

            var totalRemoved = sceneRes.MissingScriptsRemoved + prefabRes.MissingScriptsRemoved;
            var msg = $"Removed {totalRemoved} missing script(s) ({sceneRes.ScenesModified} scene(s), {prefabRes.PrefabsModified} prefab(s) cleaned).";
            Debug.Log($"[MissingScriptCleaner] {msg}");
            EditorUtility.DisplayDialog("Remove Missing Scripts", msg, "OK");
        }

        #endregion

        #region Core Scan & Clean Logic

        public static ScanResult ScanProject()
        {
            var scan = new ScanResult
            {
                Details = new List<string>()
            };

            var originalSetup = EditorSceneManager.GetSceneManagerSetup();
            var sceneGuids = AssetDatabase.FindAssets("t:Scene");

            try
            {
                for (int i = 0; i < sceneGuids.Length; i++)
                {
                    var scenePath = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                    var count = CountMissingInScene(scene);
                    if (count > 0)
                    {
                        scan.MissingScriptCount += count;
                        scan.AffectedScenesCount++;
                        scan.Details.Add($"[Scene] {scenePath}: {count} missing script(s)");
                    }
                }
            }
            finally
            {
                if (originalSetup != null && originalSetup.Length > 0)
                    EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
            }

            var prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                var prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) continue;

                var count = CountMissingInHierarchy(prefab);
                if (count > 0)
                {
                    scan.MissingScriptCount += count;
                    scan.AffectedPrefabsCount++;
                    scan.Details.Add($"[Prefab] {prefabPath}: {count} missing script(s)");
                }
            }

            return scan;
        }

        public static CleanResult CleanAllScenes()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var sceneGuids = AssetDatabase.FindAssets("t:Scene");
            var result = new CleanResult();

            try
            {
                for (var i = 0; i < sceneGuids.Length; i++)
                {
                    var scenePath = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);

                    if (EditorUtility.DisplayCancelableProgressBar("Removing Missing Scripts (Scenes)",
                            scenePath, (float)i / sceneGuids.Length))
                        break;

                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    var removed = CleanScene(scene);
                    if (removed <= 0)
                        continue;

                    result.MissingScriptsRemoved += removed;
                    result.ScenesModified++;
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (setup != null && setup.Length > 0)
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
            }

            return result;
        }

        public static CleanResult CleanAllPrefabs()
        {
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            var result = new CleanResult();

            try
            {
                for (var i = 0; i < prefabGuids.Length; i++)
                {
                    var prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                    if (EditorUtility.DisplayCancelableProgressBar("Removing Missing Scripts (Prefabs)",
                            prefabPath, (float)i / prefabGuids.Length))
                        break;

                    var removed = CleanPrefab(prefabPath);
                    if (removed <= 0)
                        continue;

                    result.MissingScriptsRemoved += removed;
                    result.PrefabsModified++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return result;
        }

        public static int CleanScene(Scene scene)
        {
            var count = 0;
            var roots = scene.GetRootGameObjects();
            foreach (var root in roots)
            {
                count += CleanGameObjectRecursive(root);
            }
            return count;
        }

        public static int CleanPrefab(string prefabPath)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var count = CleanGameObjectRecursive(root);
                if (count > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                return count;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static int CleanGameObjectRecursive(GameObject go)
        {
            var count = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);

            for (int i = 0; i < go.transform.childCount; i++)
            {
                count += CleanGameObjectRecursive(go.transform.GetChild(i).gameObject);
            }

            return count;
        }

        private static int CountMissingInScene(Scene scene)
        {
            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                count += CountMissingInHierarchy(root);
            }
            return count;
        }

        private static int CountMissingInHierarchy(GameObject go)
        {
            var count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            for (int i = 0; i < go.transform.childCount; i++)
            {
                count += CountMissingInHierarchy(go.transform.GetChild(i).gameObject);
            }
            return count;
        }

        #endregion
    }
}
