using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// One-time setup and maintenance menu commands for the Bootstrap Scene Kit. Everything here is
    /// driven by a <see cref="BootstrapSettings"/> asset you create in your own project — see
    /// <c>Tools &gt; Unity Utils &gt; Bootstrap &gt; Create Settings Asset</c>.
    /// </summary>
    public static class BootstrapSceneTools
    {
        private const string DefaultSettingsPath = "Assets/Resources/Wagenheimer/BootstrapSettings.asset";

        [MenuItem("Tools/Unity Utils/Bootstrap/1. Create Settings Asset")]
        private static void CreateSettingsAsset()
        {
            var existing = FindSettings();
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorUtility.DisplayDialog("Bootstrap Settings",
                    $"A BootstrapSettings asset already exists at:\n{AssetDatabase.GetAssetPath(existing)}",
                    "OK");
                return;
            }

            var directory = AssetDirectoryOf(DefaultSettingsPath);
            if (!AssetDatabase.IsValidFolder(directory))
                CreateFolderRecursive(directory);

            var settings = ScriptableObject.CreateInstance<BootstrapSettings>();
            AssetDatabase.CreateAsset(settings, DefaultSettingsPath);
            AssetDatabase.SaveAssets();

            Selection.activeObject = settings;
            EditorUtility.DisplayDialog("Bootstrap Settings",
                "Created a BootstrapSettings asset. Set the bootstrap scene name and drag in your " +
                "persistent prefabs (Main/Audio/Music/...), then run '2. Create/Rebuild Bootstrap Scene'.",
                "OK");
        }

        [MenuItem("Tools/Unity Utils/Bootstrap/2. Create-Rebuild Bootstrap Scene")]
        private static void CreateOrRebuildBootstrapScene()
        {
            var settings = RequireSettings();
            if (settings == null)
                return;

            if (settings.PersistentPrefabs == null || settings.PersistentPrefabs.Length == 0)
            {
                EditorUtility.DisplayDialog("Bootstrap Settings",
                    "Add at least one prefab to Persistent Prefabs on the BootstrapSettings asset first.",
                    "OK");
                return;
            }

            var scenePath = ScenePathFor(settings.BootstrapSceneName);

            var setup = EditorSceneManager.GetSceneManagerSetup();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            foreach (var prefab in settings.PersistentPrefabs)
            {
                if (prefab == null)
                    continue;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                if (instance != null)
                    EditorSceneManager.MoveGameObjectToScene(instance, scene);
            }

            var directory = AssetDirectoryOf(scenePath);
            if (!AssetDatabase.IsValidFolder(directory))
                CreateFolderRecursive(directory);

            EditorSceneManager.SaveScene(scene, scenePath);
            EnsureSceneInBuildSettings(scenePath);

            if (setup != null && setup.Length > 0)
                EditorSceneManager.RestoreSceneManagerSetup(setup);

            EditorUtility.DisplayDialog("Bootstrap Scene",
                $"Created/updated {scenePath} with {settings.PersistentPrefabs.Count(p => p != null)} prefab(s) and added it to Build Settings.",
                "OK");
        }

        [MenuItem("Tools/Unity Utils/Bootstrap/3. Remove Persistent Prefabs From Active Scene")]
        private static void RemoveFromActiveScene()
        {
            var settings = RequireSettings();
            if (settings == null)
                return;

            var targetGuids = TargetGuidsFor(settings);
            if (targetGuids == null)
                return;

            var scene = EditorSceneManager.GetActiveScene();
            var bootstrapScenePath = ScenePathFor(settings.BootstrapSceneName);
            if (scene.path == bootstrapScenePath)
            {
                EditorUtility.DisplayDialog("Remove Persistent Prefabs",
                    "The active scene is the bootstrap scene itself — nothing to remove.", "OK");
                return;
            }

            var removedAny = RemoveFromScene(scene, targetGuids);
            if (removedAny)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            EditorUtility.DisplayDialog("Remove Persistent Prefabs",
                removedAny
                    ? $"Removed persistent prefab instances from '{scene.name}'."
                    : $"'{scene.name}' had no persistent prefab instances.",
                "OK");
        }

        [MenuItem("Tools/Unity Utils/Bootstrap/4. Remove Persistent Prefabs From Other Scenes")]
        private static void RemoveFromOtherScenes()
        {
            var settings = RequireSettings();
            if (settings == null)
                return;

            var targetGuids = TargetGuidsFor(settings);
            if (targetGuids == null)
                return;

            var bootstrapScenePath = ScenePathFor(settings.BootstrapSceneName);
            var setup = EditorSceneManager.GetSceneManagerSetup();

            var sceneGuids = AssetDatabase.FindAssets("t:Scene");
            var changedScenes = 0;

            try
            {
                for (var i = 0; i < sceneGuids.Length; i++)
                {
                    var scenePath = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
                    if (scenePath == bootstrapScenePath)
                        continue;

                    if (EditorUtility.DisplayCancelableProgressBar("Removing Persistent Prefabs",
                            scenePath, (float)i / sceneGuids.Length))
                        break;

                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                    if (!RemoveFromScene(scene, targetGuids))
                        continue;

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

            EditorUtility.DisplayDialog("Remove Persistent Prefabs",
                $"Removed persistent prefab instances from {changedScenes} scene(s).", "OK");
        }

        private static System.Collections.Generic.HashSet<string> TargetGuidsFor(BootstrapSettings settings)
        {
            var targetGuids = (settings.PersistentPrefabs ?? System.Array.Empty<GameObject>())
                .Where(p => p != null)
                .Select(p => AssetDatabase.GetAssetPath(p))
                .Select(AssetDatabase.AssetPathToGUID)
                .ToHashSet();

            if (targetGuids.Count != 0)
                return targetGuids;

            EditorUtility.DisplayDialog("Bootstrap Settings",
                "Add at least one prefab to Persistent Prefabs on the BootstrapSettings asset first.",
                "OK");
            return null;
        }

        private static bool RemoveFromScene(Scene scene, System.Collections.Generic.HashSet<string> targetGuids)
        {
            var removedAny = false;

            foreach (var root in scene.GetRootGameObjects())
            {
                if (!PrefabUtility.IsPartOfAnyPrefab(root))
                    continue;

                var sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                if (string.IsNullOrEmpty(sourcePath))
                    continue;

                if (!targetGuids.Contains(AssetDatabase.AssetPathToGUID(sourcePath)))
                    continue;

                Object.DestroyImmediate(root);
                removedAny = true;
            }

            return removedAny;
        }

        internal static BootstrapSettings FindSettings()
        {
            var guids = AssetDatabase.FindAssets("t:BootstrapSettings");
            if (guids.Length == 0)
                return null;

            return AssetDatabase.LoadAssetAtPath<BootstrapSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static BootstrapSettings RequireSettings()
        {
            var settings = FindSettings();
            if (settings == null)
            {
                EditorUtility.DisplayDialog("Bootstrap Settings",
                    "No BootstrapSettings asset found. Run '1. Create Settings Asset' first.", "OK");
            }

            return settings;
        }

        private static string ScenePathFor(string sceneName)
        {
            var existing = AssetDatabase.FindAssets($"t:Scene {sceneName}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == sceneName);

            return existing ?? $"Assets/Scenes/{sceneName}.unity";
        }

        private static void EnsureSceneInBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == scenePath))
                return;

            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>
        /// Unlike <see cref="Path.GetDirectoryName(string)"/> — which normalizes '/' to '\' on
        /// Windows, breaking any subsequent '/'-based splitting of a Unity asset path — this stays
        /// on plain forward slashes throughout, matching what AssetDatabase always expects.
        /// </summary>
        private static string AssetDirectoryOf(string assetPath)
        {
            var lastSlash = assetPath.LastIndexOf('/');
            return lastSlash < 0 ? string.Empty : assetPath[..lastSlash];
        }

        private static void CreateFolderRecursive(string path)
        {
            var parts = path.Split('/');
            var current = parts[0];

            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    var createdGuid = AssetDatabase.CreateFolder(current, parts[i]);
                    if (string.IsNullOrEmpty(createdGuid))
                        Debug.LogError($"[BootstrapSceneTools] Failed to create folder '{next}'.");
                }

                current = next;
            }
        }
    }
}
