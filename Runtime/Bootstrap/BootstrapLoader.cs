using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wagenheimer.UnityUtils
{
    /// <summary>
    /// Additively loads the bootstrap scene named in your <see cref="BootstrapSettings"/> asset
    /// before any other scene's Awake runs — regardless of which scene Play (or the build) actually
    /// starts from. See the package README for the full rationale.
    /// </summary>
    public static class BootstrapLoader
    {
        private const string SettingsResourcePath = "Wagenheimer/BootstrapSettings";
        private const string FallbackSettingsResourcePath = "BootstrapSettings";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureBootstrapLoaded()
        {
            var settings = Resources.Load<BootstrapSettings>(SettingsResourcePath)
                        ?? Resources.Load<BootstrapSettings>(FallbackSettingsResourcePath);
            if (settings == null || string.IsNullOrEmpty(settings.BootstrapSceneName))
                return;

            var activeScene = SceneManager.GetActiveScene();

            // When launching directly from the bootstrap scene (in the Editor or standalone player),
            // automatically load the first non-bootstrap content scene additively so the game does
            // not remain stuck on an empty persistent-objects scene.
            if (string.Equals(activeScene.name, settings.BootstrapSceneName, System.StringComparison.OrdinalIgnoreCase))
            {
                LoadFirstContentSceneAdditively(settings.BootstrapSceneName);
                return;
            }

            if (SceneManager.GetSceneByName(settings.BootstrapSceneName).isLoaded)
                return;

            SceneManager.LoadScene(settings.BootstrapSceneName, LoadSceneMode.Additive);
        }

        private static void LoadFirstContentSceneAdditively(string bootstrapSceneName)
        {
            var sceneCount = SceneManager.sceneCountInBuildSettings;
            if (sceneCount <= 0)
            {
                Debug.LogWarning("[UnityUtils] Bootstrap scene played directly, but no scenes are configured in Build Settings.");
                return;
            }

            var targetIndex = -1;
            string targetName = null;

            for (var i = 0; i < sceneCount; i++)
            {
                var scenePath = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.IsNullOrEmpty(scenePath)) continue;

                var sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
                if (!string.Equals(sceneName, bootstrapSceneName, System.StringComparison.OrdinalIgnoreCase))
                {
                    targetIndex = i;
                    targetName = sceneName;
                    break;
                }
            }

            if (targetIndex < 0)
            {
                Debug.LogWarning("[UnityUtils] Bootstrap scene played directly, but no non-bootstrap content scenes found in Build Settings.");
                return;
            }

            Debug.Log($"[UnityUtils] Bootstrap scene is active — auto-loading first content scene '{targetName}' (build index {targetIndex}) additively.");
            var operation = SceneManager.LoadSceneAsync(targetIndex, LoadSceneMode.Additive);
            if (operation == null) return;

            operation.completed += _ =>
            {
                var loadedScene = SceneManager.GetSceneByBuildIndex(targetIndex);
                if (loadedScene.IsValid())
                {
                    SceneManager.SetActiveScene(loadedScene);
                }
            };
        }
    }
}
