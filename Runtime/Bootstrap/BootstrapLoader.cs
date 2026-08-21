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

            if (SceneManager.GetSceneByName(settings.BootstrapSceneName).isLoaded)
                return;

            SceneManager.LoadScene(settings.BootstrapSceneName, LoadSceneMode.Additive);
        }
    }
}
