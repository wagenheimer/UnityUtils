using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Wagenheimer.UnityUtils
{
    public enum GlowStatus
    {
        [InspectorName("Fading in")] Indo,
        [InspectorName("Fading out")] Vindo
    }

    /// <summary>
    /// Pulsing glow: the alpha of an <see cref="Image"/> or <see cref="SpriteRenderer"/> on this object
    /// ping-pongs between a minimum and a maximum. Alpha is expressed from 0 to 255.
    /// </summary>
    [AddComponentMenu("Wagenheimer/Animation/Glow (Alpha Pulse)")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class Glow : MonoBehaviour
    {
        [Header("Alpha range (0-255)")]
        [FormerlySerializedAs("Min")]
        [Range(0f, 255f)] public float minAlpha = 0f;

        [FormerlySerializedAs("Max")]
        [Range(0f, 255f)] public float maxAlpha = 255f;

        [Header("Motion")]
        [Tooltip("Seconds for a full 0-255 sweep in one direction.")]
        [FormerlySerializedAs("GlowTime")]
        [Min(0.01f)] public float glowTime = 1f;

        [Tooltip("-1 = glow forever. A positive value glows for that many seconds, then hides.")]
        [FormerlySerializedAs("Duration")]
        public float duration = -1f;

        [Tooltip("Current alpha.")]
        [FormerlySerializedAs("Value")]
        public float value;

        [FormerlySerializedAs("Status")]
        public GlowStatus status = GlowStatus.Indo;

        [Header("Targets (auto-detected when empty)")]
        [FormerlySerializedAs("Image")]
        public Image image;

        private SpriteRenderer sprite;

        private void Awake()
        {
            Resolve();
            value = minAlpha;
        }

        private void Reset() => Resolve();

        private void Resolve()
        {
            if (image == null) image = GetComponent<Image>();
            if (sprite == null) sprite = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            Resolve();

            if (duration == 0f)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            if (duration > 0f) duration = Mathf.Max(0f, duration - Time.deltaTime);

            var step = Time.deltaTime * 255f / glowTime;
            if (status == GlowStatus.Indo)
            {
                value = Mathf.Clamp(value + step, minAlpha, maxAlpha);
                if (value >= maxAlpha) status = GlowStatus.Vindo;
            }
            else
            {
                value = Mathf.Clamp(value - step, minAlpha, maxAlpha);
                if (value <= minAlpha) status = GlowStatus.Indo;
            }

            ApplyAlpha(value / 255f);
        }

        private void SetVisible(bool visible)
        {
            if (image != null) image.enabled = visible;
            if (sprite != null) sprite.enabled = visible;
        }

        private void ApplyAlpha(float alpha)
        {
            if (image != null)
            {
                var c = image.color;
                image.color = new Color(c.r, c.g, c.b, alpha);
            }

            if (sprite != null)
            {
                var c = sprite.color;
                sprite.color = new Color(c.r, c.g, c.b, alpha);
            }
        }

        /// <summary>Starts the glow from the minimum alpha. Pass a duration in seconds, or -1 to glow forever.</summary>
        public void Play(float seconds = -1f)
        {
            duration = seconds;
            value = minAlpha;
            status = GlowStatus.Indo;
        }

        /// <summary>Stops the glow and hides the target.</summary>
        public void Stop() => duration = 0f;

        private void OnValidate()
        {
            if (maxAlpha < minAlpha) maxAlpha = minAlpha;
        }
    }
}
