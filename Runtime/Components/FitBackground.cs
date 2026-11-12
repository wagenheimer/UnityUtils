using UnityEngine;
using UnityEngine.UI;

namespace Wagenheimer.UnityUtils
{
    /// <summary>
    /// Full-screen background that covers its parent without distorting the artwork (like CSS
    /// <c>background-size: cover</c>): an <see cref="AspectRatioFitter"/> in Envelope Parent mode that
    /// takes its ratio from the size of the background image.
    /// </summary>
    [AddComponentMenu("Wagenheimer/UI/Fit Background (Cover Parent)")]
    public class FitBackground : AspectRatioFitter
    {
        [Header("Artwork size (pixels)")]
        [Tooltip("Width of the background artwork. Only the width/height ratio matters.")]
        [Min(1f)] public float artworkWidth = 1430f;

        [Tooltip("Height of the background artwork.")]
        [Min(1f)] public float artworkHeight = 768f;

        protected override void Start()
        {
            ApplyRatio();
            base.Start();
        }

        /// <summary>Re-applies the ratio, e.g. after swapping the background sprite at runtime.</summary>
        public void ApplyRatio()
        {
            aspectMode = AspectMode.EnvelopeParent;
            aspectRatio = artworkWidth / Mathf.Max(1f, artworkHeight);
            SetDirty();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            aspectMode = AspectMode.EnvelopeParent;
            aspectRatio = artworkWidth / Mathf.Max(1f, artworkHeight);
        }
#endif
    }
}
