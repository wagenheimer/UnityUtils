using UnityEngine;
using UnityEngine.Serialization;

namespace Wagenheimer.UnityUtils
{
    /// <summary>
    /// Remembers where a UI panel started (captured on Awake) so animations can send it back with
    /// <see cref="BackToOriginalPosition"/>. On phones it can use a different home position.
    /// </summary>
    [AddComponentMenu("Wagenheimer/UI/Save Original Position")]
    [DisallowMultipleComponent]
    public class UISaveOriginalPosition : MonoBehaviour
    {
        [Tooltip("World position captured on Awake. Edit it only to override the starting point.")]
        [FormerlySerializedAs("OriginalPosition")]
        public Vector3 originalPosition;

        [Header("Mobile")]
        [Tooltip("Use Mobile Original Position as the home position on iOS and Android.")]
        [FormerlySerializedAs("UseMobile")]
        public bool useMobile;

        [FormerlySerializedAs("MobileOriginalPosition")]
        public Vector3 mobileOriginalPosition;

        private void Awake() => originalPosition = transform.position;

        /// <summary>Moves the object back to its (mobile or default) home position.</summary>
        public void BackToOriginalPosition()
        {
#if UNITY_IOS || UNITY_ANDROID
            if (useMobile) originalPosition = mobileOriginalPosition;
#endif
            transform.position = originalPosition;
        }
    }
}
