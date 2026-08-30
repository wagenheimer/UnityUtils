using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wagenheimer.UnityUtils
{
    /// <summary>
    /// Enforces a single active <see cref="AudioListener"/> at runtime across all loaded scenes,
    /// preventing Unity's "There are 2 audio listeners in the scene..." spam when additive scenes
    /// or camera prefabs are loaded.
    ///
    /// Runs automatically on scene load. You can also optionally attach this component to your
    /// canonical audio GameObject to guarantee it retains priority over scene-specific cameras.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [AddComponentMenu("Wagenheimer/Audio/Single Audio Listener")]
    [DisallowMultipleComponent]
    public class SingleAudioListener : MonoBehaviour
    {
        [Tooltip("If true, this AudioListener is guaranteed priority over any non-explicit listeners in other scenes.")]
        [SerializeField] private bool isCanonical = true;

        public bool IsCanonical => isCanonical;

        private static bool _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitRuntimeEnforcer()
        {
            if (_initialized) return;
            _initialized = true;

            SceneManager.sceneLoaded += OnSceneLoaded;
            EnforceSingleListener();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnforceSingleListener();
        }

        private void OnEnable()
        {
            EnforceSingleListener();
        }

        /// <summary>
        /// Scans all active AudioListeners in all loaded scenes and disables duplicates,
        /// keeping the most appropriate one (Canonical > Persistent / Main Camera > First found).
        /// </summary>
        public static void EnforceSingleListener()
        {
#if UNITY_2023_1_OR_NEWER
            var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            var listeners = Object.FindObjectsOfType<AudioListener>();
#endif
            if (listeners == null || listeners.Length <= 1)
                return;

            AudioListener keeper = null;

            // 1. Look for explicit SingleAudioListener marked as canonical
            foreach (var listener in listeners)
            {
                if (listener == null || !listener.enabled || !listener.gameObject.activeInHierarchy)
                    continue;

                var singleComp = listener.GetComponent<SingleAudioListener>();
                if (singleComp != null && singleComp.IsCanonical)
                {
                    keeper = listener;
                    break;
                }
            }

            // 2. Look for persistent object (scene.buildIndex == -1 / DontDestroyOnLoad)
            if (keeper == null)
            {
                foreach (var listener in listeners)
                {
                    if (listener == null || !listener.enabled || !listener.gameObject.activeInHierarchy)
                        continue;

                    if (listener.gameObject.scene.name == "DontDestroyOnLoad" || listener.gameObject.scene.buildIndex < 0)
                    {
                        keeper = listener;
                        break;
                    }
                }
            }

            // 3. Look for MainCamera
            if (keeper == null)
            {
                var mainCam = Camera.main;
                if (mainCam != null)
                {
                    var camListener = mainCam.GetComponent<AudioListener>();
                    if (camListener != null && camListener.enabled && camListener.gameObject.activeInHierarchy)
                    {
                        keeper = camListener;
                    }
                }
            }

            // 4. Fallback to first active listener
            if (keeper == null)
            {
                foreach (var listener in listeners)
                {
                    if (listener != null && listener.enabled && listener.gameObject.activeInHierarchy)
                    {
                        keeper = listener;
                        break;
                    }
                }
            }

            if (keeper == null)
                return;

            // Disable all other active listeners
            var disabledCount = 0;
            foreach (var listener in listeners)
            {
                if (listener == null || listener == keeper || !listener.enabled)
                    continue;

                listener.enabled = false;
                disabledCount++;
            }

            if (disabledCount > 0)
            {
                Debug.Log($"[UnityUtils] Disabled {disabledCount} duplicate AudioListener(s) at runtime. " +
                          $"Keeping '{keeper.gameObject.name}' in scene '{keeper.gameObject.scene.name}'.");
            }
        }
    }
}
