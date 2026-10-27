using UnityEditor;
using UnityEngine;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Detects a legacy Path-o-logical PoolManager as soon as the project loads and offers the
    /// one-click migration. Automatic conversion is opt-in (<see cref="AutoMigrateEnabled"/>);
    /// detection itself is on by default and only prompts once per project.
    /// </summary>
    [InitializeOnLoad]
    internal static class PoolingAutoDetector
    {
        private const string AutoDetectKey = "Wagenheimer.Pooling.AutoDetect";
        private const string AutoMigrateKey = "Wagenheimer.Pooling.AutoMigrate";
        private const string PromptedKey = "Wagenheimer.Pooling.PromptedFor";

        private static int _retries;

        public static bool AutoDetectEnabled
        {
            get => EditorPrefs.GetBool(AutoDetectKey, true);
            set => EditorPrefs.SetBool(AutoDetectKey, value);
        }

        public static bool AutoMigrateEnabled
        {
            get => EditorPrefs.GetBool(AutoMigrateKey, false);
            set => EditorPrefs.SetBool(AutoMigrateKey, value);
        }

        static PoolingAutoDetector()
        {
            EditorApplication.delayCall += Check;
        }

        private static void Check()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                // Wait for the editor to settle; do not spin forever.
                if (_retries++ < 20)
                    EditorApplication.delayCall += Check;

                return;
            }

            _retries = 0;

            if (!AutoDetectEnabled)
                return;

            // Cheap check first: only scan every scene/prefab when the legacy plugin is actually
            // present. Without this, every domain reload would read the whole project.
            var quick = LegacyPoolMigrator.Detect(includeAssets: false, includeScripts: false);
            if (!quick.HasLegacy)
                return;

            // Do not rescan the whole project on every domain reload after the user said "Not now".
            var projectKey = Application.dataPath;
            var alreadyPrompted = EditorPrefs.GetString(PromptedKey, "") == projectKey;
            if (alreadyPrompted && !AutoMigrateEnabled)
                return;

            var detection = LegacyPoolMigrator.Detect(includeAssets: true, includeScripts: false);
            if (!detection.HasLegacy)
                return;

            if (AutoMigrateEnabled)
            {
                EditorPrefs.SetString(PromptedKey, projectKey);
                Debug.Log("[PoolMigration] Legacy PoolManager detected and auto-migrate is enabled. Starting migration...");
                // Re-detect with scripts included: the quick scan above skipped the script rewrite.
                LegacyPoolMigrator.Migrate();
                return;
            }

            EditorPrefs.SetString(PromptedKey, projectKey);

            var message =
                "Legacy Path-o-logical PoolManager detected in this project.\n\n" +
                detection.Summary + "\n\n" +
                "UnityUtils can convert the SpawnPool components, rewrite the PoolManager usages and move " +
                "the legacy plugin out of Assets. A full backup is created first.\n\n" +
                "Migrate now?";

            var choice = EditorUtility.DisplayDialogComplex(
                "Legacy PoolManager detected",
                message,
                "Migrate now",
                "Not now",
                "Open dashboard");

            if (choice == 0)
            {
                // Re-detect with scripts included: the quick scan above skipped the script rewrite.
                var result = LegacyPoolMigrator.Migrate();
                EditorUtility.DisplayDialog(
                    "PoolManager migration",
                    result.Summary,
                    "OK");
            }
            else if (choice == 2)
            {
                UnityUtilsHubWindow.Open(UnityUtilsHubWindow.Tab.Pooling);
            }
        }

        /// <summary>Allows the user to be prompted again after choosing "Not now".</summary>
        public static void ResetPrompt()
        {
            EditorPrefs.DeleteKey(PromptedKey);
        }
    }
}
