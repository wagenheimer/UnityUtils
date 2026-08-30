using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Re-saves and modernizes scenes, prefabs, and project assets to strip legacy components
    /// (such as deprecated GUILayer, GUIText, GUITexture, obsolete serialized component indices)
    /// and eliminate persistent console warnings when opening older assets in newer Unity versions.
    /// </summary>
    public static class LegacyComponentCleaner
    {
        public struct CleanResult
        {
            public int ScenesUpdated;
            public int PrefabsUpdated;
            public int AssetsReserialized;
        }

        #region Menu Items

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Legacy Components/Resave All Scenes (Upgrade Serialization)", priority = 30)]
        public static void ResaveAllScenes()
        {
            if (!EditorUtility.DisplayDialog("Resave All Scenes",
                    "This opens and resaves every scene in the project to strip legacy components (e.g. GUILayer) " +
                    "and update their serialization to your current Unity version.\n\n" +
                    "Make sure your work is saved/committed first.", "Proceed", "Cancel"))
                return;

            var updated = ResaveScenes();
            var msg = $"Resaved and upgraded {updated} scene(s).";
            Debug.Log($"[LegacyComponentCleaner] {msg}");
            EditorUtility.DisplayDialog("Resave All Scenes", msg, "OK");
        }

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Legacy Components/Resave All Prefabs (Upgrade Serialization)", priority = 31)]
        public static void ResaveAllPrefabs()
        {
            if (!EditorUtility.DisplayDialog("Resave All Prefabs",
                    "This loads and resaves every prefab in the project to strip obsolete component references " +
                    "and modernize serialization.\n\n" +
                    "Make sure your work is saved/committed first.", "Proceed", "Cancel"))
                return;

            var updated = ResavePrefabs();
            var msg = $"Resaved and upgraded {updated} prefab(s).";
            Debug.Log($"[LegacyComponentCleaner] {msg}");
            EditorUtility.DisplayDialog("Resave All Prefabs", msg, "OK");
        }

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Legacy Components/Resave All Scenes && Prefabs (Full Project Modernize)", priority = 32)]
        public static void ResaveAllScenesAndPrefabs()
        {
            if (!EditorUtility.DisplayDialog("Full Project Modernize",
                    "This will resave and upgrade all Scenes, Prefabs, and Assets in your project to clear " +
                    "legacy GUILayer warnings and broken component index references.\n\n" +
                    "Make sure your work is saved/committed first.", "Proceed", "Cancel"))
                return;

            var scenesUpdated = ResaveScenes();
            var prefabsUpdated = ResavePrefabs();
            ForceReserializeProject();

            var msg = $"Project modernize complete: {scenesUpdated} scene(s) and {prefabsUpdated} prefab(s) upgraded.";
            Debug.Log($"[LegacyComponentCleaner] {msg}");
            EditorUtility.DisplayDialog("Project Modernize", msg, "OK");
        }

        #endregion

        #region Core Logic

        public static int ResaveScenes()
        {
            var originalSetup = EditorSceneManager.GetSceneManagerSetup();
            var sceneGuids = AssetDatabase.FindAssets("t:Scene");
            var updated = 0;

            try
            {
                for (int i = 0; i < sceneGuids.Length; i++)
                {
                    var scenePath = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
                    if (EditorUtility.DisplayCancelableProgressBar("Resaving Scenes", scenePath, (float)i / sceneGuids.Length))
                        break;

                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    updated++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (originalSetup != null && originalSetup.Length > 0)
                    EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
            }

            return updated;
        }

        public static int ResavePrefabs()
        {
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            var updated = 0;

            try
            {
                for (int i = 0; i < prefabGuids.Length; i++)
                {
                    var prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                    if (EditorUtility.DisplayCancelableProgressBar("Resaving Prefabs", prefabPath, (float)i / prefabGuids.Length))
                        break;

                    var root = PrefabUtility.LoadPrefabContents(prefabPath);
                    try
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                        updated++;
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return updated;
        }

        public static void ForceReserializeProject()
        {
            var allAssets = AssetDatabase.FindAssets("t:Scene")
                .Concat(AssetDatabase.FindAssets("t:Prefab"))
                .Concat(AssetDatabase.FindAssets("t:ScriptableObject"))
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .ToArray();

            AssetDatabase.ForceReserializeAssets(allAssets);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        #endregion
    }
}
