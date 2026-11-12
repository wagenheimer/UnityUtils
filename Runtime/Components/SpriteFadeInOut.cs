using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Wagenheimer.UnityUtils
{
    /// <summary>
    /// Fades a <see cref="SpriteRenderer"/> in or out and can call back when it finishes. The sprite
    /// starts invisible; <see cref="FadeIn()"/> reveals it up to the alpha it had in the scene and
    /// <see cref="FadeOut()"/> hides it and deactivates the object.
    /// <para>
    /// <see cref="FadeStarted"/> / <see cref="FadeFinished"/> let a game block its UI while any fade runs,
    /// without this component knowing about the game.
    /// </para>
    /// </summary>
    [AddComponentMenu("Wagenheimer/Animation/Sprite Fade In Out")]
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpriteFadeInOut : MonoBehaviour
    {
        private enum State { Idle, FadingIn, FadingOut }

        /// <summary>All active instances, so a scene can fade everything out at once.</summary>
        public static readonly List<SpriteFadeInOut> Active = new List<SpriteFadeInOut>();

        /// <summary>Raised when any instance starts a fade.</summary>
        public static event Action FadeStarted;

        /// <summary>Raised when any instance finishes a fade.</summary>
        public static event Action FadeFinished;

        [Header("Fade")]
        [Tooltip("Seconds the fade takes.")]
        [FormerlySerializedAs("FadeTime")]
        [Min(0.01f)] public float fadeTime = 1f;

        [Tooltip("Shapes the fade. A straight line is linear.")]
        public AnimationCurve easing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("Keep fading while the game is paused.")]
        public bool useUnscaledTime = true;

        [Header("Target (auto-detected when empty)")]
        [FormerlySerializedAs("_spriteNative")]
        public SpriteRenderer sprite;

        private float maxAlpha = 1f;
        private float elapsed;
        private State state;
        private Action onFadeIn;
        private Action onFadeOut;

        private void Awake()
        {
            if (sprite == null) sprite = GetComponent<SpriteRenderer>();
            maxAlpha = sprite.color.a;
            SetAlpha(0f);
        }

        private void OnEnable() => Active.Add(this);

        private void OnDisable()
        {
            Active.Remove(this);
            if (state != State.Idle)
            {
                state = State.Idle;
                FadeFinished?.Invoke();
            }
        }

        private void Update()
        {
            if (state == State.Idle) return;

            elapsed = Mathf.Min(elapsed + (useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime), fadeTime);
            var t = Mathf.Clamp01(elapsed / fadeTime);
            var eased = easing != null && easing.length > 0 ? easing.Evaluate(t) : t;

            if (state == State.FadingIn)
                SetAlpha(Mathf.Lerp(0f, maxAlpha, eased));
            else
                SetAlpha(Mathf.Lerp(maxAlpha, 0f, eased));

            if (elapsed < fadeTime) return;

            var wasFadingOut = state == State.FadingOut;
            state = State.Idle;
            FadeFinished?.Invoke();

            if (wasFadingOut)
            {
                var callback = onFadeOut;
                onFadeOut = null;
                callback?.Invoke();
                gameObject.SetActive(false);
            }
            else
            {
                var callback = onFadeIn;
                onFadeIn = null;
                callback?.Invoke();
            }
        }

        public void FadeIn()
        {
            Begin(State.FadingIn, 0f);
        }

        public void FadeOut()
        {
            Begin(State.FadingOut, maxAlpha);
        }

        public void FadeIn(Action whenDone)
        {
            onFadeIn = whenDone;
            FadeIn();
        }

        public void FadeOut(Action whenDone)
        {
            onFadeOut = whenDone;
            FadeOut();
        }

        /// <summary>Fades out every active instance.</summary>
        public static void FadeOutAll()
        {
            foreach (var instance in Active.ToArray())
                instance.FadeOut();
        }

        private void Begin(State next, float startAlpha)
        {
            gameObject.SetActive(true);
            if (sprite == null) sprite = GetComponent<SpriteRenderer>();

            SetAlpha(startAlpha);
            elapsed = 0f;

            var wasIdle = state == State.Idle;
            state = next;
            if (wasIdle) FadeStarted?.Invoke();
        }

        private void SetAlpha(float alpha)
        {
            if (sprite == null) return;
            var c = sprite.color;
            sprite.color = new Color(c.r, c.g, c.b, alpha);
        }
    }
}
