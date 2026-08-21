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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureBootstrapLoaded()
        {
            var settings = Resources.Load<BootstrapSettings>(SettingsResourcePath);
            if (settings == null || string.IsNullOrEmpty(settings.BootstrapSceneName))
                return;

            var activeScene = SceneManager.GetActiveScene();

#if UNITY_EDITOR
            // Pressing Play while already inside the bootstrap scene would leave the game stuck in
            // an empty persistent-objects scene with no content loaded. Jump into the first enabled
            // Build Settings scene instead, keeping the bootstrap (and its persistent objects) alive.
            // Editor-only: in a build the active scene is whatever is first in Build Settings, and
            // re-loading it additively here would just duplicate it.
            if (string.Equals(activeScene.name, settings.BootstrapSceneName, System.StringComparison.OrdinalIgnoreCase))
            {
                LoadFirstBuildSceneAdditively();
                return;
            }
#endif

            if (SceneManager.GetSceneByName(settings.BootstrapSceneName).isLoaded)
                return;

            SceneManager.LoadScene(settings.BootstrapSceneName, LoadSceneMode.Additive);
        }

        private static bool IsBootstrapSceneInBuild(string bootstrapSceneName)
        {
            // In builds, scenes are identified by buildIndex; in the Editor, the active scene may not
            // even be in Build Settings. Treat "bootstrap is the only loaded scene and it's build index 0"
            // as playing-from-bootstrap.
            var activeScene = SceneManager.GetActiveScene();
            return activeScene.buildIndex >= 0;
        }

        private static void LoadFirstBuildSceneAdditively()
        {
            if (UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings <= 0)
            {
                Debug.LogWarning("[UnityUtils] Bootstrap scene played directly, but no scenes are added to Build Settings.");
                return;
            }

            Debug.Log("[UnityUtils] Bootstrap scene played directly — loading first Build Settings scene.");
            var operation = SceneManager.LoadSceneAsync(0, LoadSceneMode.Additive);
            if (operation == null) return;

            operation.completed += _ =>
            {
                var firstScene = SceneManager.GetSceneByBuildIndex(0);
                if (firstScene.IsValid()) SceneManager.SetActiveScene(firstScene);
            };
        }
    }
}
