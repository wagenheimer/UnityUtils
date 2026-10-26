using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Shared helpers for the cleanup suite.
    ///
    /// Every cleanup tool must only touch assets that live under <c>Assets/</c>. Package assets
    /// (<c>Packages/...</c>, <c>Library/PackageCache/...</c>) are read-only and some of them cannot
    /// be opened at all — for example URP ships <c>Editor/SceneTemplates/Basic.unity</c>, which
    /// <see cref="EditorSceneManager.OpenScene"/> rejects with an <see cref="ArgumentException"/>.
    /// Iterating <c>AssetDatabase.FindAssets("t:Scene")</c> without a root folder therefore aborted
    /// a whole project scan and left the editor on whatever scene was open when it failed.
    /// </summary>
    internal static class CleanupAssetUtility
    {
        private const string ProjectAssetsRoot = "Assets";

        public static string[] GetProjectScenePaths()
        {
            return FindProjectAssets("t:Scene");
        }

        public static string[] GetProjectPrefabPaths()
        {
            return FindProjectAssets("t:Prefab");
        }

        public static string[] GetProjectScriptableObjectPaths()
        {
            return FindProjectAssets("t:ScriptableObject");
        }

        private static string[] FindProjectAssets(string filter)
        {
            return AssetDatabase.FindAssets(filter, new[] { ProjectAssetsRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(IsEditableProjectAsset)
                .Distinct()
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// True when the path points at an asset inside the project's own <c>Assets/</c> folder.
        /// Package assets are read-only and must never be opened, modified or re-saved.
        /// </summary>
        public static bool IsEditableProjectAsset(string assetPath)
        {
            return !string.IsNullOrEmpty(assetPath) &&
                   assetPath.StartsWith(ProjectAssetsRoot + "/", StringComparison.Ordinal);
        }

        /// <summary>
        /// Prompts to save the currently open scenes before a project-wide operation starts
        /// opening scenes in <see cref="OpenSceneMode.Single"/> mode. Without this, Unity would
        /// silently discard unsaved scene changes. Returns false when the user cancels.
        /// </summary>
        public static bool SaveCurrentScenesIfNeeded()
        {
            return EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        }

        /// <summary>
        /// Opens a project scene, returning false instead of throwing when the asset is missing,
        /// lives in a package, or fails to load. A failed open must never abort a whole scan.
        /// </summary>
        public static bool TryOpenScene(string scenePath, out Scene scene)
        {
            scene = default;
            if (!IsEditableProjectAsset(scenePath))
            {
                Debug.LogWarning($"[Cleanup] Skipping scene outside Assets/: {scenePath}");
                return false;
            }

            try
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                return scene.IsValid() && scene.isLoaded;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Cleanup] Could not open scene '{scenePath}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Loads prefab contents for editing, returning null instead of throwing when the prefab
        /// cannot be loaded. Callers still own the matching <see cref="PrefabUtility.UnloadPrefabContents"/>.
        /// </summary>
        public static GameObject LoadPrefabContents(string prefabPath)
        {
            if (!IsEditableProjectAsset(prefabPath))
                return null;

            try
            {
                return PrefabUtility.LoadPrefabContents(prefabPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Cleanup] Could not load prefab '{prefabPath}': {ex.Message}");
                return null;
            }
        }

        public static bool TrySavePrefabContents(GameObject root, string prefabPath)
        {
            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Cleanup] Could not save prefab '{prefabPath}': {ex.Message}");
                return false;
            }
        }
    }
}
