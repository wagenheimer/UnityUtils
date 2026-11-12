using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

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

        private const string IncludeSamplesPreference = "WagenheimerUnityUtils.Cleanup.IncludeSamples";

        // Folders that hold example content shipped with third-party assets. Scanning them mostly produces
        // console noise ("outdated serialization", "GUI Layer no longer available") about files the project
        // never ships, so they are skipped unless the user opts in.
        private static readonly string[] SampleFolderNames =
        {
            "examples", "example", "examplescenes", "samples", "sample", "demo", "demos", "devtools", "documentation"
        };

        /// <summary>When false (default), scenes, prefabs and assets inside sample/demo folders are skipped.</summary>
        public static bool IncludeSamples
        {
            get => EditorPrefs.GetBool(IncludeSamplesPreference, false);
            set => EditorPrefs.SetBool(IncludeSamplesPreference, value);
        }

        /// <summary>True when a single folder name is one of the sample/demo names.</summary>
        public static bool IsSampleFolderName(string folderName)
        {
            return !string.IsNullOrEmpty(folderName) && Array.IndexOf(SampleFolderNames, folderName.ToLowerInvariant()) >= 0;
        }

        /// <summary>True when any folder in the path is named like a sample or demo folder.</summary>
        public static bool IsSampleOrDemoPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;

            var parts = assetPath.Split('/');
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (Array.IndexOf(SampleFolderNames, parts[i].ToLowerInvariant()) >= 0)
                    return true;
            }

            return false;
        }

        private static string[] FindProjectAssets(string filter)
        {
            var includeSamples = IncludeSamples;

            return AssetDatabase.FindAssets(filter, new[] { ProjectAssetsRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(IsEditableProjectAsset)
                .Where(path => includeSamples || !IsSampleOrDemoPath(path))
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

        /// <summary>
        /// Saves edited prefab contents back to the asset. Returns true only when Unity reports the save
        /// succeeded — callers must not count fixes as applied when this returns false.
        /// </summary>
        public static bool TrySavePrefabContents(GameObject root, string prefabPath)
        {
            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out var success);
                if (!success)
                    Debug.LogWarning($"[Cleanup] Unity did not save prefab '{prefabPath}' (locked, read-only or invalid).");
                return success;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Cleanup] Could not save prefab '{prefabPath}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Destroys a component and verifies it is really gone. Returns false (and logs) when Unity refuses:
        /// components inherited from a prefab cannot be destroyed on an instance or variant — the source
        /// prefab asset is what must be fixed. Cleaners must only count a removal when this returns true.
        /// </summary>
        public static bool TryDestroyComponent(Component component)
        {
            if (component == null) return false;

            var owner = component.gameObject;
            var typeName = component.GetType().Name;

            if (PrefabUtility.IsPartOfPrefabInstance(component) && !PrefabUtility.IsAddedComponentOverride(component))
            {
                Debug.LogWarning($"[Cleanup] {typeName} on '{owner.name}' comes from a prefab asset; " +
                                 "skipped here (fix the source prefab instead).", owner);
                return false;
            }

            try
            {
                Object.DestroyImmediate(component, true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Cleanup] Could not remove {typeName} from '{owner.name}': {ex.Message}", owner);
                return false;
            }

            // Unity's overloaded == treats a destroyed component as null.
            if (component != null)
            {
                Debug.LogWarning($"[Cleanup] {typeName} on '{owner.name}' is still present after removal.", owner);
                return false;
            }

            return true;
        }

        public static void PingAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            var obj = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (obj != null)
            {
                EditorGUIUtility.PingObject(obj);
                Selection.activeObject = obj;
            }
        }
    }

    /// <summary>
    /// Represents an individual issue discovered during a diagnostic scan.
    /// </summary>
    public struct CleanupIssueItem
    {
        public string AssetPath;
        public string TargetName;
        public string Description;
        public string Category;
        public bool IsScene;
        public bool IsPrefab;
        public int Count;
    }

    /// <summary>
    /// Records a modification or repair applied to an asset.
    /// </summary>
    public struct CleanupModificationRecord
    {
        public string AssetPath;
        public string ActionTaken;
        public int ItemsFixed;
        public bool IsScene;
        public bool IsPrefab;
    }
}
