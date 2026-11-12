using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Wagenheimer.UnityUtils
{
    /// <summary>
    /// Drives a loading bar fill (a stretched <see cref="RectTransform"/>) and an optional glowing "head"
    /// (the first child, with a <see cref="CanvasGroup"/>). <see cref="Track"/> follows an asynchronous
    /// scene load with a smooth, continuous motion: never stuck at 90 %, no sudden jumps, and a pleasant
    /// minimum duration.
    /// </summary>
    public class LoadingBar
    {
        /// <summary>Usable width of the bar track when it cannot be measured from the frame.</summary>
        public const float DefaultMaxWidth = 483f;

        private const float HeadFadeInEnd = 0.06f;  // glow fades in over the first 6 %
        private const float HeadFadeStart = 0.88f;  // ...and fades out between 88 %
        private const float HeadFadeEnd = 0.98f;    // ...and 98 %, before reaching the edge

        private readonly RectTransform fill;
        private readonly CanvasGroup head;
        private readonly float height;
        private readonly float maxWidth;

        /// <param name="fill">The stretched bar. Its parent is used as the frame to measure the track.</param>
        /// <param name="fallbackMaxWidth">Track width to use when the frame or sprite cannot be measured.</param>
        public LoadingBar(RectTransform fill, float fallbackMaxWidth = DefaultMaxWidth)
        {
            this.fill = fill;
            height = fill != null ? fill.sizeDelta.y : 56f;
            maxWidth = fallbackMaxWidth;

            if (fill != null)
            {
                var image = fill.GetComponent<Image>();
                var spriteWidth = image != null && image.sprite != null ? image.sprite.rect.width : fallbackMaxWidth;

                if (fill.parent is RectTransform frame && frame.rect.width > 0f)
                {
                    var leftInset = frame.rect.width * 0.5f + fill.anchoredPosition.x;
                    maxWidth = Mathf.Min(spriteWidth, frame.rect.width - 2f * leftInset);
                }

                if (maxWidth <= 0f) maxWidth = fallbackMaxWidth;

                if (fill.childCount > 0 && fill.GetChild(0) is RectTransform headRect)
                {
                    head = headRect.GetComponent<CanvasGroup>();
                    if (head == null) head = headRect.gameObject.AddComponent<CanvasGroup>();
                }
            }

            Set(0f);
        }

        /// <summary>Sets the fill to <paramref name="progress"/> (0-1).</summary>
        public void Set(float progress)
        {
            if (fill == null) return;

            var t = Mathf.Clamp01(progress);
            if (t <= 0.001f)
            {
                fill.sizeDelta = new Vector2(0f, height);
                if (head != null) head.alpha = 0f;
                return;
            }

            fill.sizeDelta = new Vector2(Mathf.Lerp(0f, maxWidth, t), height);
            if (head == null) return;

            var alpha = 1f;
            if (t < HeadFadeInEnd)
                alpha = Mathf.InverseLerp(0f, HeadFadeInEnd, t);
            else if (t > HeadFadeStart)
                alpha = 1f - Mathf.InverseLerp(HeadFadeStart, HeadFadeEnd, t);

            head.alpha = Mathf.Clamp01(alpha);
        }

        /// <summary>
        /// Follows an asynchronous load (with <c>allowSceneActivation = false</c>) until the bar is full and
        /// <paramref name="minDuration"/> has passed. Yield it from a coroutine.
        /// </summary>
        public static IEnumerator Track(LoadingBar bar, AsyncOperation async, float minDuration = 1.15f)
        {
            bar?.Set(0f);

            var elapsed = 0f;
            var current = 0f;

            while (current < 0.998f)
            {
                elapsed += Time.unscaledDeltaTime;

                // The engine reports 0..0.9 while activation is held back.
                var real = async != null ? Mathf.Clamp01(async.progress / 0.9f) : 1f;

                var timeRatio = Mathf.Clamp01(elapsed / minDuration);
                var smoothTime = timeRatio * timeRatio * (3f - 2f * timeRatio); // smoothstep

                float target;
                if (real < 1f)
                {
                    target = Mathf.Min(0.90f, Mathf.Max(real * 0.90f, smoothTime * 0.88f));
                }
                else
                {
                    target = elapsed >= minDuration ? 1f : Mathf.Lerp(0.90f, 1f, smoothTime);
                }

                // Exponential damping plus a minimum speed: never stalls, never jumps.
                var lerpSpeed = real >= 1f && elapsed >= minDuration * 0.8f ? 7f : 4.5f;
                current = Mathf.Lerp(current, target, 1f - Mathf.Exp(-lerpSpeed * Time.unscaledDeltaTime));
                current = Mathf.MoveTowards(current, target, Time.unscaledDeltaTime * 0.20f);

                bar?.Set(current);

                if (real >= 1f && current >= 0.995f && elapsed >= minDuration)
                    break;

                yield return null;
            }

            bar?.Set(1f);
            yield return new WaitForSecondsRealtime(0.15f); // let the player see 100 %
        }
    }
}
