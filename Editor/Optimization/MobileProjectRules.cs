using System.Linq;
using UnityEditor;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Project-wide Android settings that dominate build time and package size. Settings that trade device reach
    /// (ARMv7) are reported as Info and ask for confirmation before changing.
    /// </summary>
    internal static class MobileProjectRules
    {
        public static void AddChecks(DiagnosticReport report, MobileAssetOptimizer.Options options)
        {
            if (!options.Android) return;
            AddTextureCompressionFormats(report);
            AddArmv7(report);
        }

        private static void AddTextureCompressionFormats(DiagnosticReport report)
        {
#if UNITY_2023_1_OR_NEWER
            var formats = PlayerSettings.Android.textureCompressionFormats;
            var hasEtc1 = formats.Contains(TextureCompressionFormat.ETC);
            if (!hasEtc1 || (int)PlayerSettings.Android.minSdkVersion < 24) return;

            report.Items.Add(new DiagnosticItem
            {
                Id = "project-android-tcf",
                Title = "Android: Texture Compression Formats include ETC (ETC1)",
                Description = $"Current: {string.Join(", ", formats)}. With min SDK {(int)PlayerSettings.Android.minSdkVersion} " +
                              "every device supports ETC2, so ETC1 only adds a full extra texture import per build " +
                              "(Texture Compression Targeting) and lower quality. Recommended: ASTC + ETC2.",
                Severity = DiagnosticSeverity.Warning,
                Category = "Project",
                CanFix = true,
                FixAction = () =>
                {
                    PlayerSettings.Android.textureCompressionFormats = new[] { TextureCompressionFormat.ASTC, TextureCompressionFormat.ETC2 };
                    AssetDatabase.SaveAssets();
                }
            });
#endif
        }

        private static void AddArmv7(DiagnosticReport report)
        {
            if ((PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARMv7) == 0) return;

            report.Items.Add(new DiagnosticItem
            {
                Id = "project-android-armv7",
                Title = "Android: ARMv7 (32-bit) enabled",
                Description = "IL2CPP compiles every C++ file twice (arm64 + armv7), almost doubling that build phase and the AAB. " +
                              "Google Play requires 64-bit; 32-bit only serves very old devices — check your Play Console audience first.",
                Severity = DiagnosticSeverity.Info,
                Category = "Project",
                CanFix = true,
                FixAction = () =>
                {
                    if (!EditorUtility.DisplayDialog("Remove ARMv7",
                            "Build arm64 only? Devices that run only 32-bit apps will no longer be able to install the game.",
                            "Remove ARMv7", "Cancel"))
                        return;
                    PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                    AssetDatabase.SaveAssets();
                }
            });
        }
    }
}
