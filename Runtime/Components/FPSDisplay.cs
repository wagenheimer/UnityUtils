using UnityEngine;

namespace Wagenheimer.UnityUtils
{
    /// <summary>Small on-screen FPS / frame-time overlay for development builds.</summary>
    [AddComponentMenu("Wagenheimer/Debug/FPS Display")]
    [DisallowMultipleComponent]
    public class FPSDisplay : MonoBehaviour
    {
        public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

        [Header("Layout")]
        public Corner corner = Corner.TopLeft;

        [Tooltip("Font size as a fraction of the screen height.")]
        [Range(0.01f, 0.06f)] public float size = 0.02f;

        public Color color = Color.white;

        [Header("Behaviour")]
        [Tooltip("Only draw in the Editor and in Development Builds.")]
        public bool developmentOnly = true;

        [Tooltip("Smoothing of the frame time (0 = none, higher = steadier).")]
        [Range(0f, 0.95f)] public float smoothing = 0.9f;

        public bool showTimeScale = true;

        private float deltaTime;
        private GUIStyle style;

        private void Update() => deltaTime = Mathf.Lerp(Time.unscaledDeltaTime, deltaTime, smoothing);

        private void OnGUI()
        {
            if (developmentOnly && !Debug.isDebugBuild && !Application.isEditor) return;

            style ??= new GUIStyle(GUI.skin.label);
            style.fontSize = Mathf.Max(10, Mathf.RoundToInt(Screen.height * size));
            style.normal.textColor = color;
            style.alignment = corner switch
            {
                Corner.TopLeft => TextAnchor.UpperLeft,
                Corner.TopRight => TextAnchor.UpperRight,
                Corner.BottomLeft => TextAnchor.LowerLeft,
                _ => TextAnchor.LowerRight
            };

            var ms = deltaTime * 1000f;
            var fps = deltaTime > 0f ? 1f / deltaTime : 0f;
            var text = $"{ms:0.0} ms ({fps:0.} fps)" + (showTimeScale ? $"  x{Time.timeScale:0.##}" : "");
            GUI.Label(new Rect(8f, 4f, Screen.width - 16f, Screen.height - 8f), text, style);
        }
    }
}
