using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Wagenheimer.UnityUtils
{
    /// <summary>
    /// Full-screen "please wait" overlay: <c>BusyOverlay.Show("Loading ad")</c> ... <c>BusyOverlay.Hide()</c>. Dims the
    /// screen, shows the text with animated dots and swallows taps so the player cannot start a second request. It
    /// appears after a short delay (no flicker for quick operations) and hides itself after
    /// <see cref="FailsafeSeconds"/> if <see cref="Hide"/> is never called. Built from code, no prefab needed.
    /// </summary>
    public sealed class BusyOverlay : MonoBehaviour
    {
        public const float FailsafeSeconds = 70f;
        private const float ShowDelaySeconds = 0.2f;
        private const int SortingOrder = 31000;

        private static BusyOverlay _instance;

        private CanvasGroup _group;
        private TextMeshProUGUI _label;
        private string _text = "";
        private bool _wanted;
        private float _wantedSince;

        public static void Show(string text)
        {
            var overlay = Ensure();
            overlay._text = text ?? "";
            overlay._wanted = true;
            overlay._wantedSince = Time.unscaledTime;
        }

        public static void Hide()
        {
            if (_instance == null) return;

            _instance._wanted = false;
            _instance.SetVisible(false);
        }

        private static BusyOverlay Ensure()
        {
            if (_instance != null) return _instance;

            var root = new GameObject("[BusyOverlay]");
            DontDestroyOnLoad(root);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            root.AddComponent<GraphicRaycaster>();
            _instance = root.AddComponent<BusyOverlay>();
            _instance.Build(root.transform);
            return _instance;
        }

        private void Build(Transform parent)
        {
            var panel = new GameObject("Dimmer", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            panel.transform.SetParent(parent, false);

            var rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = panel.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.6f);
            image.raycastTarget = true;

            _group = panel.GetComponent<CanvasGroup>();
            SetVisible(false);

            var text = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            text.transform.SetParent(panel.transform, false);

            var textRect = (RectTransform)text.transform;
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.sizeDelta = new Vector2(700f, 90f);

            _label = text.GetComponent<TextMeshProUGUI>();
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontSize = 40f;
            _label.color = Color.white;
            _label.raycastTarget = false;
        }

        private void Update()
        {
            if (!_wanted) return;

            float waited = Time.unscaledTime - _wantedSince;
            if (waited > FailsafeSeconds)
            {
                Hide();
                return;
            }

            if (waited < ShowDelaySeconds) return;

            SetVisible(true);
            int dots = 1 + (int)(Time.unscaledTime * 2f) % 3;
            _label.text = _text + new string('.', dots);
        }

        private void SetVisible(bool visible)
        {
            _group.alpha = visible ? 1f : 0f;
            _group.blocksRaycasts = visible;
            _group.interactable = visible;
        }
    }
}
