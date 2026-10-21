using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Audio checks for one mobile platform (optimize-audio guidance): every clip needs a mobile override;
    /// long clips stream with Load In Background; Decompress On Load only for small clips; short SFX at 22 kHz.
    /// Vorbis on both platforms — AAC is rejected by the importer API and Vorbis is the Android standard.
    /// </summary>
    internal static class MobileAudioRules
    {
        public static void AddChecks(DiagnosticReport report, List<(string path, AudioImporter importer)> clips, string platform)
        {
            var label = MobileAssetOptimizer.PlatformLabel(platform);
            var infos = clips.Select(c => (c.path, c.importer, clip: AssetDatabase.LoadAssetAtPath<AudioClip>(c.path)))
                .Where(c => c.clip != null)
                .ToList();

            AddAudioItem(report, $"audio-override-{platform}", $"{label}: audio clips without a {label} override",
                "Without an override the clip uses the default settings, usually tuned for desktop.",
                DiagnosticSeverity.Warning,
                infos.Where(c => !c.importer.ContainsSampleSettingsOverride(platform)).Select(c => c.path).ToList(),
                platform);

            AddAudioItem(report, $"audio-stream-{platform}", $"{label}: long clips not streaming",
                $"Clips of {MobileAssetOptimizer.StreamingThresholdSeconds:0}s or more (music, ambience, voice) should stream " +
                "with Load In Background instead of sitting decoded or compressed in memory.",
                DiagnosticSeverity.Warning,
                infos.Where(c => c.clip.length >= MobileAssetOptimizer.StreamingThresholdSeconds
                                 && Effective(c.importer, platform).loadType != AudioClipLoadType.Streaming)
                    .Select(c => c.path).ToList(),
                platform);

            AddAudioItem(report, $"audio-decompress-{platform}", $"{label}: Decompress On Load on heavy clips",
                "Decompress On Load keeps raw PCM in memory: fine for tiny SFX, wasteful above ~1 MB.",
                DiagnosticSeverity.Error,
                infos.Where(c => Effective(c.importer, platform).loadType == AudioClipLoadType.DecompressOnLoad
                                 && PcmBytes(c.clip) > MobileAssetOptimizer.DecompressOnLoadMaxBytes)
                    .Select(c => c.path).ToList(),
                platform);
        }

        private static AudioImporterSampleSettings Effective(AudioImporter importer, string platform) =>
            importer.ContainsSampleSettingsOverride(platform)
                ? importer.GetOverrideSampleSettings(platform)
                : importer.defaultSampleSettings;

        private static long PcmBytes(AudioClip clip) => (long)clip.samples * clip.channels * 2;

        /// <summary>Mobile override by clip length. Returns false when nothing changed.</summary>
        public static bool ApplyMobileSettings(string path, string platform)
        {
            if (AssetImporter.GetAtPath(path) is not AudioImporter importer) return false;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) return false;

            var isLong = clip.length >= MobileAssetOptimizer.StreamingThresholdSeconds;
            var settings = Effective(importer, platform);
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.loadType = isLong ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
            settings.quality = isLong ? 0.6f : 0.5f;
#if UNITY_2022_2_OR_NEWER
            settings.preloadAudioData = false; // loaded on first play (or explicitly) instead of with the scene
#endif
            if (!isLong && clip.frequency > MobileAssetOptimizer.MobileSfxSampleRate)
            {
                settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
                settings.sampleRateOverride = (uint)MobileAssetOptimizer.MobileSfxSampleRate;
            }

            importer.SetOverrideSampleSettings(platform, settings);
            if (isLong) importer.loadInBackground = true;
            importer.SaveAndReimport();
            return true;
        }

        private static void AddAudioItem(DiagnosticReport report, string id, string title, string why,
            DiagnosticSeverity severity, List<string> paths, string platform)
        {
            if (paths.Count == 0) return;
            report.Items.Add(new DiagnosticItem
            {
                Id = id,
                Title = title,
                Description = $"{MobileAssetOptimizer.Summarize(paths, "clip(s)")}\n{why}",
                Severity = severity,
                Category = "Audio",
                CanFix = true,
                FixAction = () => MobileAssetOptimizer.ApplyBatch(title, paths, path => ApplyMobileSettings(path, platform))
            });
        }
    }
}
