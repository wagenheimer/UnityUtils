using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Wagenheimer.UnityUtils
{
    /// <summary>
    /// Small non-blocking message at the bottom of the screen: <c>Toast.Show("Purchase failed")</c>. Built from code (no
    /// prefab, no scene setup), drawn above everything and never intercepts taps. Use it so a tap on a button that
    /// cannot do its job never ends in "nothing happened".
    /// </summary>
    public sealed class Toast : MonoBehaviour
    {
        private const float VisibleSeconds = 3.2f;
        private const float FadeSeconds = 0.35f;
        private const int SortingOrder = 32000;

        private static Toast _instance;

        private CanvasGroup _group;
        private TextMeshProUGUI _label;
        private Coroutine _running;

        public static void Show(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            Debug.Log("[Toast] " + message);
            Ensure().Display(message);
        }

        private static Toast Ensure()
        {
            if (_instance != null) return _instance;

            var root = new GameObject("[Toast]");
            DontDestroyOnLoad(root);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            _instance = root.AddComponent<Toast>();
            _instance.Build(root.transform);
            return _instance;
        }

        private void Build(Transform parent)
        {
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            panel.transform.SetParent(parent, false);

            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 70f);
            rect.sizeDelta = new Vector2(760f, 84f);

            var image = panel.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.82f);
            image.raycastTarget = false;

            _group = panel.GetComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var text = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            text.transform.SetParent(panel.transform, false);

            var textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(24f, 8f);
            textRect.offsetMax = new Vector2(-24f, -8f);

            _label = text.GetComponent<TextMeshProUGUI>();
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontSize = 30f;
            _label.color = Color.white;
            _label.raycastTarget = false;
        }

        private void Display(string message)
        {
            _label.text = message;
            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            _group.alpha = 1f;
            yield return new WaitForSecondsRealtime(VisibleSeconds);

            for (float t = 0f; t < FadeSeconds; t += Time.unscaledDeltaTime)
            {
                _group.alpha = 1f - t / FadeSeconds;
                yield return null;
            }

            _group.alpha = 0f;
            _running = null;
        }
    }
}
