using UnityEngine;
using UnityEngine.Serialization;

namespace Wagenheimer.UnityUtils
{
    /// <summary>
    /// Breathing / pulse effect: the uniform scale ping-pongs between a minimum and a maximum.
    /// Disabling the component stops the pulse and restores the scale it had before.
    /// </summary>
    [AddComponentMenu("Wagenheimer/Animation/Scale Up Down (Pulse)")]
    [DisallowMultipleComponent]
    public class ScaleUpDown : MonoBehaviour
    {
        [Header("Range")]
        [Tooltip("Smallest uniform scale of the pulse.")]
        [FormerlySerializedAs("MinValue")]
        [Min(0f)] public float minScale = 0.9f;

        [Tooltip("Largest uniform scale of the pulse.")]
        [FormerlySerializedAs("MaxValue")]
        [Min(0f)] public float maxScale = 1f;

        [Header("Motion")]
        [Tooltip("Scale units per second (the pulse covers Max - Min at this rate, so a smaller range pulses faster).")]
        [FormerlySerializedAs("Speed")]
        [Min(0f)] public float speed = 1f;

        [Tooltip("Shapes the motion. A straight line is constant speed; an ease curve slows down at both ends.")]
        public AnimationCurve easing = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Keep pulsing while the game is paused.")]
        public bool useUnscaledTime;

        [Header("Idle")]
        [Tooltip("When off, the pulse stops and the object rests at its maximum (or minimum) scale.")]
        [FormerlySerializedAs("Active")]
        public bool active = true;

        [Tooltip("When the pulse is off, rest at the minimum scale instead of the maximum.")]
        [FormerlySerializedAs("PadraoMin")]
        public bool restAtMin;

        private Vector3 initialScale = Vector3.one;
        private float phase; // 0..1 growing, 1..2 shrinking

        private void Awake() => initialScale = transform.localScale;

        private void OnEnable() => initialScale = transform.localScale;

        private void OnDisable() => transform.localScale = initialScale;

        private void Update()
        {
            if (!active)
            {
                transform.localScale = Vector3.one * (restAtMin ? minScale : maxScale);
                return;
            }

            var dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            var range = Mathf.Max(0.0001f, maxScale - minScale);
            phase = Mathf.Repeat(phase + dt * speed / range, 2f);

            var t = phase <= 1f ? phase : 2f - phase;
            var eased = easing != null && easing.length > 0 ? easing.Evaluate(t) : t;
            transform.localScale = Vector3.one * Mathf.LerpUnclamped(minScale, maxScale, eased);
        }

        private void OnValidate()
        {
            if (maxScale < minScale) maxScale = minScale;
        }
    }
}
