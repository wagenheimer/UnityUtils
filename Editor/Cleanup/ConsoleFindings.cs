using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wagenheimer.UnityUtils.Editor
{
    public enum ConsoleFindingKind
    {
        /// <summary>"Serialized file X contains a Y object at version N, below the supported minimum".</summary>
        OutdatedSerialization,

        /// <summary>"Component GUI Layer in X for Scene Y is no longer available".</summary>
        LegacyComponent,

        /// <summary>"Component at index N could not be loaded ... Removing it".</summary>
        MissingComponentRemoved,

        /// <summary>"Creating missing CanvasGroup component for Dialog in X".</summary>
        MissingComponentCreated,

        /// <summary>"Parameter 'X' does not exist" (an Animator is asked for a parameter it does not have).</summary>
        MissingAnimatorParameter
    }

    /// <summary>One problem Unity reported on the console while a cleanup task was running.</summary>
    public sealed class ConsoleFinding
    {
        public ConsoleFindingKind Kind;

        /// <summary>The asset the message is about (or the asset being processed when it was logged).</summary>
        public string AssetPath;

        public string Message;

        /// <summary>True when re-saving the asset in the current Unity version makes the message go away.</summary>
        public bool FixableByResave => Kind != ConsoleFindingKind.MissingAnimatorParameter;
    }

    /// <summary>
    /// Listens to the console while cleanup tasks run and turns the warnings Unity prints when it opens
    /// old assets into structured findings with an explanation and, where possible, an automatic fix.
    /// Scanning scenes makes Unity print a lot of "outdated file" noise; instead of leaving it as a wall of
    /// console text, the Project Cleanup tab lists it, groups it and offers to re-save the affected assets.
    /// </summary>
    public static class ConsoleFindings
    {
        private static readonly List<ConsoleFinding> Found = new List<ConsoleFinding>();
        private static readonly HashSet<string> Keys = new HashSet<string>();
        private static int _captureDepth;

        private static readonly Regex SerializedVersion = new Regex(
            "Serialized file \"(?<path>[^\"]+)\" contains an? (?<type>\\w+) object at version (?<v>\\d+), below the supported minimum",
            RegexOptions.CultureInvariant);

        private static readonly Regex LegacyComponentInScene = new Regex(
            "Component (?<name>.+?) in (?<obj>.+?) for Scene (?<path>.+?\\.unity) is no longer available",
            RegexOptions.CultureInvariant);

        private static readonly Regex ComponentCouldNotLoad = new Regex(
            "Component at index \\d+ could not be loaded when loading game object '(?<obj>[^']+)'",
            RegexOptions.CultureInvariant);

        private static readonly Regex CreatingMissing = new Regex(
            "Creating missing (?<name>\\w+) component for (?<what>.+?) in (?<obj>\\S+)\\.",
            RegexOptions.CultureInvariant);

        private static readonly Regex AnimatorParameter = new Regex(
            "^Parameter '(?<name>[^']+)' does not exist\\.",
            RegexOptions.CultureInvariant);

        /// <summary>Everything captured so far (until fixed or cleared).</summary>
        public static IReadOnlyList<ConsoleFinding> All => Found;

        /// <summary>Raised whenever the list changes.</summary>
        public static event Action Changed;

        public static void Clear()
        {
            Found.Clear();
            Keys.Clear();
            Changed?.Invoke();
        }

        internal static void BeginCapture()
        {
            if (_captureDepth++ == 0)
                Application.logMessageReceived += OnLog;
        }

        internal static void EndCapture()
        {
            if (_captureDepth > 0 && --_captureDepth == 0)
                Application.logMessageReceived -= OnLog;
        }

        public static string Title(ConsoleFindingKind kind)
        {
            switch (kind)
            {
                case ConsoleFindingKind.OutdatedSerialization: return "Assets saved with an outdated serialization version";
                case ConsoleFindingKind.LegacyComponent: return "Scenes with a removed legacy component (for example GUI Layer)";
                case ConsoleFindingKind.MissingComponentRemoved: return "Objects with a component Unity could not load";
                case ConsoleFindingKind.MissingComponentCreated: return "Objects missing a required component";
                default: return "Animator parameters that do not exist";
            }
        }

        public static string Explanation(ConsoleFindingKind kind)
        {
            switch (kind)
            {
                case ConsoleFindingKind.OutdatedSerialization:
                    return "Unity upgrades the file in memory every time it is opened and warns about it. Re-saving writes the current format once.";
                case ConsoleFindingKind.LegacyComponent:
                    return "The component no longer exists in this Unity version. It is dropped the next time the scene is saved.";
                case ConsoleFindingKind.MissingComponentRemoved:
                    return "Unity removes the broken component on load. Re-saving makes the removal permanent so the message stops.";
                case ConsoleFindingKind.MissingComponentCreated:
                    return "Unity recreates the component on load. Re-saving stores it so it is no longer recreated every time.";
                default:
                    return "A script or state machine asks an Animator for a parameter that is not defined. Needs manual attention: add the parameter or fix the name.";
            }
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (string.IsNullOrEmpty(condition)) return;

            Match match;

            if ((match = SerializedVersion.Match(condition)).Success)
            {
                Add(ConsoleFindingKind.OutdatedSerialization, match.Groups["path"].Value, condition);
            }
            else if ((match = LegacyComponentInScene.Match(condition)).Success)
            {
                Add(ConsoleFindingKind.LegacyComponent, match.Groups["path"].Value, condition);
            }
            else if (ComponentCouldNotLoad.IsMatch(condition))
            {
                Add(ConsoleFindingKind.MissingComponentRemoved, CurrentAssetPath(), condition);
            }
            else if (CreatingMissing.IsMatch(condition))
            {
                Add(ConsoleFindingKind.MissingComponentCreated, CurrentAssetPath(), condition);
            }
            else if (AnimatorParameter.IsMatch(condition))
            {
                Add(ConsoleFindingKind.MissingAnimatorParameter, CurrentAssetPath(), condition);
            }
        }

        // Some messages do not name the asset; the sweep that opened it set its path as the current detail.
        private static string CurrentAssetPath()
        {
            var detail = CleanupRunner.ActiveDetail;
            return !string.IsNullOrEmpty(detail) && detail.StartsWith("Assets/", StringComparison.Ordinal) ? detail : string.Empty;
        }

        private static void Add(ConsoleFindingKind kind, string assetPath, string message)
        {
            var text = FirstLine(message);
            var key = kind + "|" + assetPath + "|" + (kind == ConsoleFindingKind.OutdatedSerialization ? text : string.Empty);
            if (!Keys.Add(key)) return;

            Found.Add(new ConsoleFinding { Kind = kind, AssetPath = assetPath, Message = text });
            Changed?.Invoke();
        }

        private static string FirstLine(string message)
        {
            var newline = message.IndexOf('\n');
            return newline < 0 ? message : message.Substring(0, newline).TrimEnd('\r');
        }

        /// <summary>
        /// Re-saves every asset a fixable finding points at: scenes are opened and saved, everything else
        /// (materials, render textures, prefabs...) goes through ForceReserializeAssets in small batches.
        /// Time-sliced: run it with <see cref="CleanupRunner.Start"/>. Reports how many assets were re-saved.
        /// </summary>
        public static IEnumerator FixRoutine(CleanupProgress progress, Action<int> done)
        {
            var targets = Found
                .Where(f => f.FixableByResave && !string.IsNullOrEmpty(f.AssetPath))
                .Select(f => f.AssetPath)
                .Distinct()
                .Where(path => !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                .ToList();

            var scenes = targets.Where(p => p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)).ToList();
            var others = targets.Except(scenes).ToList();
            var fixedPaths = new HashSet<string>();

            if (scenes.Count > 0)
            {
                if (!CleanupAssetUtility.SaveCurrentScenesIfNeeded())
                {
                    progress.RequestCancel();
                    done(0);
                    yield break;
                }

                progress.NextStage("Re-saving scenes reported by Unity", scenes.Count);
                var setup = EditorSceneManager.GetSceneManagerSetup();

                try
                {
                    foreach (var path in scenes)
                    {
                        if (progress.CancelRequested) break;

                        progress.Advance(path);

                        if (CleanupAssetUtility.TryOpenScene(path, out var scene))
                        {
                            try
                            {
                                EditorSceneManager.MarkSceneDirty(scene);
                                if (EditorSceneManager.SaveScene(scene))
                                    fixedPaths.Add(path);
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[Cleanup] Could not re-save scene '{path}': {ex.Message}");
                            }
                        }

                        yield return null;
                    }
                }
                finally
                {
                    if (setup != null && setup.Length > 0)
                        EditorSceneManager.RestoreSceneManagerSetup(setup);
                }
            }

            if (others.Count > 0 && !progress.CancelRequested)
            {
                const int batchSize = 10;
                progress.NextStage("Re-saving other assets reported by Unity", others.Count);

                for (var i = 0; i < others.Count; i += batchSize)
                {
                    if (progress.CancelRequested) break;

                    var batch = others.Skip(i).Take(batchSize).ToArray();
                    progress.AdvanceBy(batch.Length, batch[0]);
                    AssetDatabase.ForceReserializeAssets(batch);

                    foreach (var path in batch)
                        fixedPaths.Add(path);

                    yield return null;
                }

                AssetDatabase.SaveAssets();
            }

            // Opening the old scenes logged their warnings again while fixing: drop everything for fixed assets.
            Found.RemoveAll(f => fixedPaths.Contains(f.AssetPath));
            RebuildKeys();
            Changed?.Invoke();

            done(fixedPaths.Count);
        }

        private static void RebuildKeys()
        {
            Keys.Clear();
            foreach (var f in Found)
                Keys.Add(f.Kind + "|" + f.AssetPath + "|" + (f.Kind == ConsoleFindingKind.OutdatedSerialization ? f.Message : string.Empty));
        }
    }
}
