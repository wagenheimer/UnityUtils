using UnityEngine;

namespace Wagenheimer.UnityUtils
{
    /// <summary>
    /// Project-specific configuration for the Bootstrap Scene Kit. Lives in your own project
    /// (create one via <c>Tools &gt; Unity Utils &gt; Bootstrap &gt; Create Settings Asset</c>), not in
    /// the package itself, since it references your project's own prefabs.
    ///
    /// Must be placed under a <c>Resources</c> folder (the creation menu command does this for you)
    /// so <see cref="BootstrapLoader"/> can load it at runtime with <see cref="Resources.Load"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "BootstrapSettings", menuName = "Wagenheimer/Unity Utils/Bootstrap Settings")]
    public class BootstrapSettings : ScriptableObject
    {
        [Tooltip("Name of the scene that holds your persistent objects (Main/Audio/Music/etc). " +
                 "Must be added to Build Settings for SceneManager.LoadScene to find it by name in a build.")]
        public string BootstrapSceneName = "bootstrap";

        [Tooltip("Prefabs to instantiate into the bootstrap scene by Tools > Unity Utils > Bootstrap > " +
                 "Create/Rebuild Bootstrap Scene, and to remove wherever else they're found by " +
                 "Remove Persistent Prefabs From Other Scenes. Each should already be a self-persisting " +
                 "DontDestroyOnLoad singleton that destroys itself if a duplicate already exists.\n\n" +
                 "The first entry is also treated as the \"canonical\" object for " +
                 "AudioListenerCleaner — if it (or one of its children) has an AudioListener, that's " +
                 "the one every other scene's duplicate gets removed in favor of.")]
        public GameObject[] PersistentPrefabs = System.Array.Empty<GameObject>();
    }
}
