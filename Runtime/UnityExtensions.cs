using System;
using System.Collections.Generic;
using DG.Tweening;
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
    }
}
