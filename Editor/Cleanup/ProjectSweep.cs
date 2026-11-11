using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>Totals collected by <see cref="ProjectSweep.Scan"/>.</summary>
    internal sealed class SweepScan
    {
        public int Total;
        public int Scenes;
        public int Prefabs;
        public readonly List<string> Details = new List<string>();
        public readonly List<CleanupIssueItem> Items = new List<CleanupIssueItem>();
    }

    /// <summary>Totals collected by <see cref="ProjectSweep.Fix"/>.</summary>
    internal sealed class SweepFix
    {
        public int Fixed;
        public int Scenes;
        public int Prefabs;
        public readonly List<CleanupModificationRecord> Modifications = new List<CleanupModificationRecord>();
    }

    /// <summary>
    /// The scenes-then-prefabs loop shared by the cleaners, written as an iterator that yields after every
    /// asset so <see cref="CleanupRunner"/> can keep the editor responsive. It owns the boring parts that
    /// each cleaner used to repeat: asking to save open scenes, restoring the scene setup afterwards (even
    /// when cancelled), saving modified scenes, progress reporting and cancellation.
    /// </summary>
    internal static class ProjectSweep
    {
        /// <summary>
        /// Visits every project scene (when <paramref name="onScene"/> is set) and prefab (when
        /// <paramref name="onPrefab"/> is set). <paramref name="onScene"/> returns true when it modified the
        /// scene and, with <paramref name="saveScenes"/>, the scene is saved.
        /// </summary>
        public static IEnumerator Run(CleanupProgress progress, string label,
            Func<string, Scene, bool> onScene, Action<string> onPrefab, bool saveScenes,
            Func<string, bool> skipScene = null)
        {
            var scenePaths = onScene != null ? CleanupAssetUtility.GetProjectScenePaths() : Array.Empty<string>();
            var prefabPaths = onPrefab != null ? CleanupAssetUtility.GetProjectPrefabPaths() : Array.Empty<string>();

            if (scenePaths.Length > 0 && !CleanupAssetUtility.SaveCurrentScenesIfNeeded())
            {
                progress.RequestCancel(); // The user declined to save: nothing was touched.
                yield break;
            }

            progress.NextStage(label, scenePaths.Length + prefabPaths.Length);

            if (scenePaths.Length > 0)
            {
                var originalSetup = EditorSceneManager.GetSceneManagerSetup();
                try
                {
                    foreach (var path in scenePaths)
                    {
                        if (progress.CancelRequested) yield break;

                        progress.Advance(path);

                        if (skipScene != null && skipScene(path))
                        {
                            yield return null;
                            continue;
                        }

                        if (CleanupAssetUtility.TryOpenScene(path, out var scene))
                        {
                            var modified = onScene(path, scene);
                            if (modified && saveScenes)
                            {
                                try
                                {
                                    EditorSceneManager.MarkSceneDirty(scene);
                                    EditorSceneManager.SaveScene(scene);
                                }
                                catch (Exception ex)
                                {
                                    UnityEngine.Debug.LogWarning($"[Cleanup] Could not save scene '{path}': {ex.Message}");
                                }
                            }
                        }

                        yield return null;
                    }
                }
                finally
                {
                    if (originalSetup != null && originalSetup.Length > 0)
                        EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
                }
            }

            foreach (var path in prefabPaths)
            {
                if (progress.CancelRequested) yield break;

                progress.Advance(path);
                onPrefab(path);
                yield return null;
            }
        }

        /// <summary>Counts issues per scene and prefab without changing anything.</summary>
        public static IEnumerator Scan(CleanupProgress progress, string label, SweepScan output,
            Func<int> countInScene, Func<string, int> countInPrefab,
            string category, string detailNoun, Func<int, string> describe)
        {
            return Run(progress, label,
                (path, scene) =>
                {
                    var count = countInScene();
                    if (count > 0)
                    {
                        output.Total += count;
                        output.Scenes++;
                        output.Details.Add($"[Scene] {path}: {count} {detailNoun}");
                        output.Items.Add(new CleanupIssueItem
                        {
                            AssetPath = path,
                            TargetName = scene.name,
                            Description = describe(count),
                            Category = category,
                            IsScene = true,
                            IsPrefab = false,
                            Count = count
                        });
                    }

                    return false;
                },
                path =>
                {
                    var count = countInPrefab(path);
                    if (count <= 0) return;

                    output.Total += count;
                    output.Prefabs++;
                    output.Details.Add($"[Prefab] {path}: {count} {detailNoun}");
                    output.Items.Add(new CleanupIssueItem
                    {
                        AssetPath = path,
                        TargetName = System.IO.Path.GetFileNameWithoutExtension(path),
                        Description = describe(count),
                        Category = category,
                        IsScene = false,
                        IsPrefab = true,
                        Count = count
                    });
                },
                saveScenes: false);
        }

        /// <summary>
        /// Fixes issues per scene and prefab. <paramref name="countInPrefab"/> (optional) lets the sweep skip
        /// prefabs that have nothing to fix, so their contents are never loaded for editing.
        /// </summary>
        public static IEnumerator Fix(CleanupProgress progress, string label, SweepFix output,
            Func<int> fixInScene, Func<string, int> countInPrefab, Func<string, int> fixInPrefab, string actionNoun)
        {
            return Run(progress, label,
                (path, scene) =>
                {
                    var fixedCount = fixInScene();
                    if (fixedCount <= 0) return false;

                    output.Fixed += fixedCount;
                    output.Scenes++;
                    output.Modifications.Add(new CleanupModificationRecord
                    {
                        AssetPath = path,
                        ActionTaken = $"Removed {fixedCount} {actionNoun}",
                        ItemsFixed = fixedCount,
                        IsScene = true,
                        IsPrefab = false
                    });
                    return true;
                },
                path =>
                {
                    if (countInPrefab != null && countInPrefab(path) == 0) return;

                    var fixedCount = fixInPrefab(path);
                    if (fixedCount <= 0) return;

                    output.Fixed += fixedCount;
                    output.Prefabs++;
                    output.Modifications.Add(new CleanupModificationRecord
                    {
                        AssetPath = path,
                        ActionTaken = $"Removed {fixedCount} {actionNoun}",
                        ItemsFixed = fixedCount,
                        IsScene = false,
                        IsPrefab = true
                    });
                },
                saveScenes: true);
        }
    }
}
