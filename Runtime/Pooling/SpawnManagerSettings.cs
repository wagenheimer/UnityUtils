using UnityEngine;

namespace Wagenheimer.UnityUtils.Pooling
{
    /// <summary>
    /// Project-wide defaults for the pooling system. Create one at
    /// <c>Assets/Resources/Wagenheimer/SpawnManagerSettings.asset</c> to override the defaults.
    /// The runtime and the editor both fall back to a transient instance when the asset is absent.
    /// </summary>
    [CreateAssetMenu(menuName = "Wagenheimer/Unity Utils/Pooling Settings", fileName = "SpawnManagerSettings")]
    public class SpawnManagerSettings : ScriptableObject
    {
        [Header("Logging")]
        [Tooltip("Minimum severity written to the Unity console. Per-pool 'Log Messages' can still enable Info logs.")]
        public PoolLogLevel logLevel = PoolLogLevel.Warnings;

        [Tooltip("Keep broadcasting the legacy OnSpawned/OnDespawned messages to prefab scripts.")]
        public bool sendLegacyMessages = true;

        [Tooltip("Scan each prefab once and skip BroadcastMessage when no script implements the legacy methods.")]
        public bool detectLegacyMessages = true;

        [Header("Runtime Debug Overlay")]
        [Tooltip("Auto-create the in-game pool overlay in the Editor and Development Builds.")]
        public bool enableRuntimeDebugOverlay = true;

        [Tooltip("Key used to open/close the runtime pool overlay.")]
        public KeyCode debugOverlayToggleKey = KeyCode.F8;

        [Tooltip("Also allow the overlay in non-development builds. Keep off for production.")]
        public bool debugOverlayInReleaseBuilds = false;

        [Header("Defaults")]
        [Tooltip("Preload amount used when a prefab pool is created automatically by Spawn().")]
        public int defaultPreloadAmount = 1;

        [Tooltip("Validate duplicate prefabs, preload/limit conflicts and null references when a pool wakes up.")]
        public bool validateOnAwake = true;
    }
}
