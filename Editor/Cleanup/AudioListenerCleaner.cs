using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Removes duplicate AudioListeners across every scene in the project, keeping the one that
    /// belongs to the first entry in your <see cref="BootstrapSettings"/>' Persistent Prefabs list
    /// (the "canonical" persistent object) wherever that prefab instance is present in a scene.
    /// Falls back to keeping the first AudioListener found (with a warning) when no
    /// BootstrapSettings asset exists or that prefab instance isn't present in a given scene.
    /// </summary>
    public static class AudioListenerCleaner
    {
        [MenuItem("Tools/Unity Utils/Cleanup/Remove Duplicate Audio Listeners (Active Scene Only)")]
        private static void RemoveDuplicateAudioListenersInActiveScene()
        {
            var preferredPrefabPath = GetPreferredPrefabPath();
            var scene = EditorSceneManager.GetActiveScene();

            var removed = RemoveDuplicatesInScene(scene, preferredPrefabPath);
            if (removed > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            EditorUtility.DisplayDialog("Remove Duplicate Audio Listeners",
                $"Removed {removed} duplicate AudioListener(s) from '{scene.name}'.", "OK");
        }

        [MenuItem("Tools/Unity Utils/Cleanup/Remove Duplicate Audio Listeners (All Scenes)")]
        private static void RemoveDuplicateAudioListeners()
        {
            var preferredPrefabPath = GetPreferredPrefabPath();

            var setup = EditorSceneManager.GetSceneManagerSetup();
            var sceneGuids = AssetDatabase.FindAssets("t:Scene");
            var changedScenes = 0;
            var removedListeners = 0;

            try
            {
                for (var i = 0; i < sceneGuids.Length; i++)
                {
                    var scenePath = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);

                    if (EditorUtility.DisplayCancelableProgressBar("Removing Duplicate Audio Listeners",
                            scenePath, (float)i / sceneGuids.Length))
                        break;

                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                    var removed = RemoveDuplicatesInScene(scene, preferredPrefabPath);
                    if (removed <= 0)
                        continue;

                    removedListeners += removed;
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    changedScenes++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (setup != null && setup.Length > 0)
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
            }

            EditorUtility.DisplayDialog("Remove Duplicate Audio Listeners",
                $"Removed {removedListeners} duplicate AudioListener(s) across {changedScenes} scene(s).", "OK");
        }

        private static int RemoveDuplicatesInScene(UnityEngine.SceneManagement.Scene scene, string preferredPrefabPath)
        {
            var listeners = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<AudioListener>(true))
                .ToList();

            if (listeners.Count <= 1)
                return 0;

            var keeper = FindPreferredListener(listeners, preferredPrefabPath);
            if (keeper == null)
            {
                keeper = listeners[0];
                Debug.LogWarning($"[AudioListenerCleaner] {scene.path}: preferred prefab not found among " +
                                  $"AudioListeners, keeping '{keeper.gameObject.name}' arbitrarily.");
            }

            var removed = 0;
            foreach (var listener in listeners)
            {
                if (listener == keeper)
                    continue;

                Object.DestroyImmediate(listener);
                removed++;
            }

            return removed;
        }

        private static string GetPreferredPrefabPath()
        {
            var settings = BootstrapSceneTools.FindSettings();
            var preferred = settings?.PersistentPrefabs?.FirstOrDefault(p => p != null);
            return preferred == null ? null : AssetDatabase.GetAssetPath(preferred);
        }

        private static AudioListener FindPreferredListener(System.Collections.Generic.List<AudioListener> listeners, string preferredPrefabPath)
        {
            if (string.IsNullOrEmpty(preferredPrefabPath))
                return null;

            foreach (var listener in listeners)
            {
                var root = PrefabUtility.GetNearestPrefabInstanceRoot(listener.gameObject);
                if (root == null)
                    continue;

                var sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                if (sourcePath == preferredPrefabPath)
                    return listener;
            }

            return null;
        }
    }
}
