using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Wagenheimer.UnityUtils
{
    /// <summary>
    /// Press feedback for any button: pressing shrinks it slightly and releasing restores it. The rest
    /// scale is captured on Awake, so it composes with any per-instance size. Needs no tween library.
    /// </summary>
    [AddComponentMenu("Wagenheimer/UI/Button Press Scale")]
    [DisallowMultipleComponent]
    public sealed class ButtonPressScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Tooltip("Scale multiplier while pressed (1 = no change).")]
        [Range(0.5f, 1.2f)] [SerializeField] private float pressedScale = 0.94f;

        [Tooltip("Seconds to reach the pressed or the rest scale.")]
        [Min(0.01f)] [SerializeField] private float duration = 0.08f;

        private Vector3 restScale = Vector3.one;
        private Selectable selectable;
        private float target = 1f;
        private float current = 1f;

        private void Awake()
        {
            restScale = transform.localScale;
            selectable = GetComponent<Selectable>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (selectable != null && !selectable.IsInteractable()) return;
            target = pressedScale;
        }

        public void OnPointerUp(PointerEventData eventData) => target = 1f;

        public void OnPointerExit(PointerEventData eventData)
        {
            // Only cancel a press that is still held; a plain hover exit has nothing to restore.
            if (eventData.pointerPress != null) target = 1f;
        }

        private void Update()
        {
            if (Mathf.Approximately(current, target)) return;

            var span = Mathf.Max(0.0001f, Mathf.Abs(1f - pressedScale));
            current = Mathf.MoveTowards(current, target, Time.unscaledDeltaTime / duration * span);
            transform.localScale = restScale * current;
        }

        private void OnDisable()
        {
            current = target = 1f;
            transform.localScale = restScale;
        }
    }
}
