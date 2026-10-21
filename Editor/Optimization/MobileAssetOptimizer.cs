using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Mobile asset optimizer: audits the textures and audio clips that actually ship in the player build
    /// (Build Settings scenes + Resources folders, via the AssetDatabase dependency graph) against mobile best
    /// practices for Android and iOS, and fixes them through the importer API (SaveAndReimport) — never by
    /// editing .meta files.
    ///
    /// Rules distilled from real projects: crunch is decompressed on the CPU at load time (slow scene loads);
    /// textures without a mobile override fall back to the project default (often legacy ETC); 3D textures
    /// above 1024 px rarely pay off on a phone screen; long audio should stream; ETC1 is useless on min SDK 24+.
    /// </summary>
    public static class MobileAssetOptimizer
    {
        public const string AndroidPlatform = "Android";
        public const string IOSPlatform = "iPhone";

        public static readonly string[] MobilePlatforms = { AndroidPlatform, IOSPlatform };

        /// <summary>Clips at least this long stream from disk instead of living in memory.</summary>
        public const float StreamingThresholdSeconds = 20f;

        /// <summary>Decompress On Load above this PCM size wastes memory (unity-performance / optimize-audio guidance).</summary>
        public const long DecompressOnLoadMaxBytes = 1024 * 1024;

        /// <summary>Short sound effects do not need more than this on mobile.</summary>
        public const int MobileSfxSampleRate = 22050;

        public sealed class Options
        {
            public int MaxTextureSize = 1024;
            public bool Android = true;
            public bool IOS = true;

            public IEnumerable<string> Platforms()
            {
                if (Android) yield return AndroidPlatform;
                if (IOS) yield return IOSPlatform;
            }
        }

        // ---------- What ships ----------

        /// <summary>Every asset path the player build pulls in: enabled Build Settings scenes, Resources content and their dependencies.</summary>
        public static string[] CollectBuildAssetPaths()
        {
            var roots = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Resources/") && !path.Contains("/Editor/") && !AssetDatabase.IsValidFolder(path))
                    roots.Add(path);
            }
            return AssetDatabase.GetDependencies(roots.ToArray(), true);
        }

        public static DiagnosticReport Scan(Options options)
        {
            var report = new DiagnosticReport();
            var paths = CollectBuildAssetPaths();
            var textures = paths.Select(p => (path: p, importer: AssetImporter.GetAtPath(p) as TextureImporter))
                .Where(t => t.importer != null && !IsHdr(t.path))
                .ToList();
            var audio = paths.Select(p => (path: p, importer: AssetImporter.GetAtPath(p) as AudioImporter))
                .Where(a => a.importer != null)
                .ToList();

            foreach (var platform in options.Platforms())
            {
                MobileTextureRules.AddChecks(report, textures, platform, options);
                MobileAudioRules.AddChecks(report, audio, platform);
            }

            MobileProjectRules.AddChecks(report, options);

            if (!report.HasIssues)
            {
                report.Items.Add(new DiagnosticItem
                {
                    Id = "all-good",
                    Title = "Build assets follow the mobile best practices",
                    Description = $"{textures.Count} textures and {audio.Count} audio clips in the build were checked.",
                    Severity = DiagnosticSeverity.Pass,
                    Category = "Summary"
                });
            }
            return report;
        }

        // ---------- Shared helpers ----------

        internal static bool IsHdr(string path)
        {
            var lower = path.ToLowerInvariant();
            return lower.EndsWith(".exr") || lower.EndsWith(".hdr"); // probes / lightmaps: owned by Unity
        }

        internal static string PlatformLabel(string platform) => platform == IOSPlatform ? "iOS" : platform;

        /// <summary>"N assets — a.png, b.png, c.png …" for item descriptions.</summary>
        internal static string Summarize(IReadOnlyCollection<string> paths, string what)
        {
            var sample = string.Join(", ", paths.Take(4).Select(System.IO.Path.GetFileName));
            return $"{paths.Count} {what}: {sample}{(paths.Count > 4 ? " …" : string.Empty)}";
        }

        /// <summary>Runs <paramref name="apply"/> for each path inside one asset-editing batch with a cancelable progress bar.</summary>
        internal static int ApplyBatch(string title, IReadOnlyList<string> paths, Func<string, bool> apply)
        {
            var changed = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                for (var i = 0; i < paths.Count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(title, paths[i], (float)i / paths.Count)) break;
                    try
                    {
                        if (apply(paths[i])) changed++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[MobileOptimizer] {title} failed for '{paths[i]}': {ex.Message}");
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }
            Debug.Log($"[MobileOptimizer] {title}: {changed} asset(s) updated.");
            return changed;
        }
    }
}
