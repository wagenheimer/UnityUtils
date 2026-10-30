using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace Wagenheimer.UnityUtils
{
    /// <summary>
    /// Shared runtime extension methods and small helpers used across Green Sauce /
    /// Wagenheimer games: Vector tweaks, list shuffling, layer/ particle helpers,
    /// alpha fading, reflection copy and time formatting.
    /// </summary>
    public static class UnityExtensions
    {
        public static float PingPong(this float value, float min, float max)
        {
            return Mathf.PingPong(value, max - min) + min;
        }

        public static Vector3 ChangeZ(this Vector3 value, float z)
        {
            return new Vector3(value.x, value.y, z);
        }

        public static Vector3 Translate(this Vector3 position, int x, int y, int z)
        {
            return new Vector3(position.x + x, position.y + y, position.z + z);
        }

        public static Vector2 Translate(this Vector2 position, int x, int y)
        {
            return new Vector2(position.x + x, position.y + y);
        }

        public static void Shuffle<T>(this IList<T> list)
        {
            for (var i = 0; i < list.Count; i++)
                list.Swap(i, Random.Range(i, list.Count));
        }

        public static void Swap<T>(this IList<T> list, int i, int j)
        {
            var temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }

        public static void SetLayer(this GameObject parent, int layer, bool includeChildren = true)
        {
            parent.layer = layer;
            if (!includeChildren) return;

            foreach (var trans in parent.transform.GetComponentsInChildren<Transform>(true))
                trans.gameObject.layer = layer;
        }

        public static void StopAllParticles(this GameObject parent)
        {
            if (parent.GetComponent<ParticleSystem>()) parent.GetComponent<ParticleSystem>().Stop(true);
            foreach (var p in parent.transform.GetComponentsInChildren<ParticleSystem>(true))
                p.Stop(true);
        }

        public static void PlayAllParticles(this GameObject parent)
        {
            if (parent.GetComponent<ParticleSystem>()) parent.GetComponent<ParticleSystem>().Play(true);
            foreach (var p in parent.transform.GetComponentsInChildren<ParticleSystem>(true))
                p.Play(true);
        }

        public static void ChangeAlpha<T>(this T whatToChange, float alpha)
        {
            var itemType = typeof(T);

            if (itemType == typeof(SpriteRenderer))
            {
                var spriteRenderer = whatToChange as SpriteRenderer;
                if (spriteRenderer != null) spriteRenderer.color = new Color(spriteRenderer.color.r, spriteRenderer.color.g, spriteRenderer.color.b, alpha);
            }

            if (typeof(TMP_Text).IsAssignableFrom(itemType))
            {
                var tmpText = whatToChange as TMP_Text;
                if (tmpText != null) tmpText.color = new Color(tmpText.color.r, tmpText.color.g, tmpText.color.b, alpha);
            }
        }

        /// <summary>
        /// Copies all public property and field values from <paramref name="source"/> to the target object.
        /// </summary>
        public static void CopyFrom<T>(this T target, T source)
        {
            var type = typeof(T);
            foreach (var sourceProperty in type.GetProperties())
            {
                var targetProperty = type.GetProperty(sourceProperty.Name);
                targetProperty.SetValue(target, sourceProperty.GetValue(source, null), null);
            }

            foreach (var sourceField in type.GetFields())
            {
                var targetField = type.GetField(sourceField.Name);
                targetField.SetValue(target, sourceField.GetValue(source));
            }
        }

        public static string SecondsToTimeText(this int seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            return $"{t.Minutes:D1}:{t.Seconds:D2}";
        }

        public static string SecondsToTimeTextWithOutLabels(this int seconds)
        {
            var sec = Mathf.Abs(seconds);
            var t = TimeSpan.FromSeconds(sec);
            if (t.Hours > 0 && t.Minutes > 0) return $"{t.Hours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
            return seconds >= 0 ? $"{t.Minutes:D2}:{t.Seconds:D2}" : $"-{t.Minutes:D2}:{t.Seconds:D2}";
        }

        public static Vector2 GetSnapToPositionToBringChildIntoView(this ScrollRect instance, RectTransform child)
        {
            Canvas.ForceUpdateCanvases();
            var viewportLocalPosition = instance.viewport.localPosition;
            var childLocalPosition = child.localPosition;
            var result = new Vector2(0 - (viewportLocalPosition.x + childLocalPosition.x), 0 - (viewportLocalPosition.y + childLocalPosition.y));
            return result;
        }

        // -----------------------------------------------------------------------------------------
        // Vector / color helpers
        // -----------------------------------------------------------------------------------------

        /// <summary>Returns the vector moved by the given (floating point) offset.</summary>
        public static Vector3 Translate(this Vector3 position, float x, float y, float z)
        {
            return new Vector3(position.x + x, position.y + y, position.z + z);
        }

        /// <summary>Returns the color with the given alpha (0-1).</summary>
        public static Color ChangeAlpha(this Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        /// <summary>Returns the color with the given alpha (0-1). Same as <see cref="ChangeAlpha(Color,float)"/>.</summary>
        public static Color SetAlpha(this Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }

        /// <summary>Sets the horizontal offsets (left/right) of a RectTransform and returns it.</summary>
        public static RectTransform SetLeftRight(this RectTransform rect, float left, float right)
        {
            rect.offsetMin = new Vector2(left, rect.offsetMin.y);
            rect.offsetMax = new Vector2(right, rect.offsetMax.y);
            return rect;
        }

        /// <summary>True when a random roll between 0 and 100 is less than or equal to <paramref name="probability"/>.</summary>
        public static bool CheckRandom(float probability)
        {
            return Random.Range(0, 100) <= probability;
        }

        // -----------------------------------------------------------------------------------------
        // Collections / formatting helpers
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Sorts the list in place by the given selector (ascending). Unlike
        /// <see cref="List{T}.Sort(Comparison{T})"/> it accepts a single-argument projection:
        /// <c>list.SortBy(x => x.New);</c>
        /// </summary>
        public static void SortBy<T, U>(this List<T> list, Func<T, U> selector) where U : IComparable<U>
        {
            list.Sort((x, y) => selector(x).CompareTo(selector(y)));
        }

        /// <summary>
        /// Human readable duration using localized unit abbreviations, e.g. "2 d 3 h 4 min 5 s".
        /// Pass the game's translation callback (e.g. <c>LocalizationManager.GetTranslation</c>) to localize
        /// the <c>Global/*abrev</c> keys; when omitted, English abbreviations are used.
        /// </summary>
        public static string AsReadableString(this TimeSpan span, Func<string, string> translate = null)
        {
            string T(string key) => translate != null ? translate(key) : DefaultTimeUnit(key);

            var duration = span.Duration();
            var formatted = string.Format("{0}{1}{2}{3}",
                duration.Days > 0 ? $"{span.Days:0} {T("Global/daysabrev")} " : string.Empty,
                duration.Hours > 0 ? $"{span.Hours:0} {T("Global/hoursabrev")} " : string.Empty,
                duration.Minutes > 0 ? $"{span.Minutes:0} {T("Global/minutesabrev")} " : string.Empty,
                duration.Seconds > 0 ? $"{span.Seconds:0} {T("Global/secondsabrev")}" : string.Empty);

            return string.IsNullOrEmpty(formatted) ? $"0 {T("Global/secondsabrev")}" : formatted;
        }

        private static string DefaultTimeUnit(string key) => key switch
        {
            "Global/daysabrev" => "d",
            "Global/hoursabrev" => "h",
            "Global/minutesabrev" => "min",
            "Global/secondsabrev" => "s",
            _ => ""
        };
    }
}
