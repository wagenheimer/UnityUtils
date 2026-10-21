using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Texture checks for one mobile platform. The fix is the same for every texture rule: a platform override
    /// with ASTC (4x4 UI sprites, 5x5 normal maps, 6x6 colour/mask maps), crunch off and the size capped —
    /// sizes are only ever lowered, never raised.
    /// </summary>
    internal static class MobileTextureRules
    {
        public static void AddChecks(DiagnosticReport report, List<(string path, TextureImporter importer)> textures,
            string platform, MobileAssetOptimizer.Options options)
        {
            var label = MobileAssetOptimizer.PlatformLabel(platform);
            var materialOrUi = textures.Where(t => IsTarget(t.importer)).ToList();

            AddTextureItem(report, $"tex-crunch-{platform}", $"{label}: crunched textures",
                "Crunch is decompressed on the CPU when a scene loads — the biggest cause of slow loading on mobile. " +
                "Switching to ASTC without crunch costs a little disk size and makes loads much faster.",
                DiagnosticSeverity.Error,
                materialOrUi.Where(t => EffectiveSettings(t.importer, platform).crunchedCompression).Select(t => t.path).ToList(),
                platform, options);

            AddTextureItem(report, $"tex-override-{platform}", $"{label}: textures without a {label} override",
                "They use the project default format (often legacy ETC on Android) instead of ASTC.",
                DiagnosticSeverity.Warning,
                materialOrUi.Where(t => !t.importer.GetPlatformTextureSettings(platform).overridden).Select(t => t.path).ToList(),
                platform, options);

            AddTextureItem(report, $"tex-size-{platform}", $"{label}: textures above {options.MaxTextureSize}px",
                "Large textures inflate the download, memory and load time with little visible gain on a phone screen.",
                DiagnosticSeverity.Warning,
                materialOrUi.Where(t => EffectiveSettings(t.importer, platform).maxTextureSize > options.MaxTextureSize
                                        && SourceSize(t.importer) > options.MaxTextureSize).Select(t => t.path).ToList(),
                platform, options);

            if (platform == MobileAssetOptimizer.AndroidPlatform)
                AddSpriteMipmapItem(report, textures);
        }

        private static bool IsTarget(TextureImporter importer) =>
            importer.textureType is TextureImporterType.Default or TextureImporterType.NormalMap or TextureImporterType.Sprite;

        private static TextureImporterPlatformSettings EffectiveSettings(TextureImporter importer, string platform)
        {
            var settings = importer.GetPlatformTextureSettings(platform);
            return settings.overridden ? settings : importer.GetDefaultPlatformTextureSettings();
        }

        private static int SourceSize(TextureImporter importer)
        {
            importer.GetSourceTextureWidthAndHeight(out var width, out var height);
            return System.Math.Max(width, height);
        }

        public static TextureImporterFormat RecommendedFormat(TextureImporter importer) => importer.textureType switch
        {
            TextureImporterType.Sprite => TextureImporterFormat.ASTC_4x4,
            TextureImporterType.NormalMap => TextureImporterFormat.ASTC_5x5,
            _ => TextureImporterFormat.ASTC_6x6
        };

        /// <summary>Platform override with ASTC, no crunch and the size capped. Returns false when nothing changed.</summary>
        public static bool ApplyMobileSettings(string path, string platform, int maxSize)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return false;

            var current = EffectiveSettings(importer, platform);
            var settings = importer.GetPlatformTextureSettings(platform);
            var target = System.Math.Min(maxSize, current.maxTextureSize);
            var format = RecommendedFormat(importer);
            if (settings.overridden && !settings.crunchedCompression && settings.format == format && settings.maxTextureSize == target)
                return false;

            settings.overridden = true;
            settings.format = format;
            settings.crunchedCompression = false;
            settings.maxTextureSize = target;
            settings.compressionQuality = 50;
            importer.SetPlatformTextureSettings(settings);
            importer.SaveAndReimport();
            return true;
        }

        private static void AddTextureItem(DiagnosticReport report, string id, string title, string why,
            DiagnosticSeverity severity, List<string> paths, string platform, MobileAssetOptimizer.Options options)
        {
            if (paths.Count == 0) return;
            var maxSize = options.MaxTextureSize;
            report.Items.Add(new DiagnosticItem
            {
                Id = id,
                Title = title,
                Description = $"{MobileAssetOptimizer.Summarize(paths, "texture(s)")}\n{why}",
                Severity = severity,
                Category = "Textures",
                CanFix = true,
                FixAction = () => MobileAssetOptimizer.ApplyBatch(title, paths,
                    path => ApplyMobileSettings(path, platform, maxSize))
            });
        }

        private static void AddSpriteMipmapItem(DiagnosticReport report, List<(string path, TextureImporter importer)> textures)
        {
            var paths = textures.Where(t => t.importer.textureType == TextureImporterType.Sprite && t.importer.mipmapEnabled)
                .Select(t => t.path).ToList();
            if (paths.Count == 0) return;

            report.Items.Add(new DiagnosticItem
            {
                Id = "tex-sprite-mipmaps",
                Title = "Sprites with mipmaps enabled",
                Description = $"{MobileAssetOptimizer.Summarize(paths, "sprite(s)")}\nUI/2D sprites are drawn at screen size: mipmaps only add ~33% memory.",
                Severity = DiagnosticSeverity.Warning,
                Category = "Textures",
                CanFix = true,
                FixAction = () => MobileAssetOptimizer.ApplyBatch("Disable sprite mipmaps", paths, path =>
                {
                    if (AssetImporter.GetAtPath(path) is not TextureImporter importer || !importer.mipmapEnabled) return false;
                    importer.mipmapEnabled = false;
                    importer.SaveAndReimport();
                    return true;
                })
            });
        }
    }
}
