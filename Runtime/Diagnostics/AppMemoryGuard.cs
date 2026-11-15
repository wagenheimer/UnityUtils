using System;
using UnityEngine;
using UnityEngine.Profiling;

namespace Wagenheimer.UnityUtils
{
    /// <summary>
    /// Reacts to the OS "low memory" warning and records memory use, so a game that gets killed on a small device
    /// (e.g. a 2 GB iPad) leaves evidence in the log. Starts by itself, no setup.
    /// <list type="bullet">
    /// <item><description>On <see cref="Application.lowMemory"/>: logs a warning with the numbers, then unloads unused
    /// assets and collects garbage (the cheap things that can still free memory while a full-screen ad is open).</description></item>
    /// <item><description>In development builds and the Editor: logs a one-line memory summary every 30 s.</description></item>
    /// </list>
    /// </summary>
    public sealed class AppMemoryGuard : MonoBehaviour
    {
        private const float SummaryIntervalSeconds = 30f;
        private const float MinSecondsBetweenCleanups = 5f;

        private static AppMemoryGuard _instance;
        private float _nextSummary;
        private float _lastCleanup = float.NegativeInfinity;

        /// <summary>One-line description: system RAM, Unity allocated/reserved, managed heap, graphics driver.</summary>
        public static string Describe()
        {
            const double mb = 1024d * 1024d;
            return "ram=" + SystemInfo.systemMemorySize + "MB" +
                   " unityAlloc=" + (Profiler.GetTotalAllocatedMemoryLong() / mb).ToString("0") + "MB" +
                   " unityReserved=" + (Profiler.GetTotalReservedMemoryLong() / mb).ToString("0") + "MB" +
                   " mono=" + (GC.GetTotalMemory(false) / mb).ToString("0") + "MB" +
                   " gfx=" + (Profiler.GetAllocatedMemoryForGraphicsDriver() / mb).ToString("0") + "MB";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;

            var go = new GameObject("[AppMemoryGuard]");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AppMemoryGuard>();
        }

        private void OnEnable() => Application.lowMemory += OnLowMemory;

        private void OnDisable() => Application.lowMemory -= OnLowMemory;

        private void Update()
        {
            if (!(Debug.isDebugBuild || Application.isEditor)) return;
            if (Time.unscaledTime < _nextSummary) return;

            _nextSummary = Time.unscaledTime + SummaryIntervalSeconds;
            Debug.Log("[Memory] " + Describe());
        }

        private void OnLowMemory()
        {
            Debug.LogWarning("[Memory] LOW MEMORY warning from the OS: " + Describe());

            if (Time.unscaledTime - _lastCleanup < MinSecondsBetweenCleanups) return;
            _lastCleanup = Time.unscaledTime;

            Resources.UnloadUnusedAssets();
            GC.Collect();
        }
    }
}
