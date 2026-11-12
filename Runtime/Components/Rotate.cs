using UnityEngine;
using UnityEngine.Serialization;

namespace Wagenheimer.UnityUtils
{
    /// <summary>Spins a transform continuously. Handy for loading icons, coins, rays and decorations.</summary>
    [AddComponentMenu("Wagenheimer/Animation/Rotate")]
    [DisallowMultipleComponent]
    public class Rotate : MonoBehaviour
    {
        [Header("Rotation")]
        [Tooltip("Degrees per second around each axis. Use a negative value to spin the other way.")]
        [FormerlySerializedAs("RotateDir")]
        public Vector3 degreesPerSecond = new Vector3(0f, 0f, 90f);

        [Tooltip("World: the angle is added to the world rotation. Self: spins around the object's own axes.")]
        public Space space = Space.World;

        [Header("Time")]
        [Tooltip("Keep spinning while the game is paused (Time.timeScale = 0), e.g. on a loading screen or pause menu.")]
        public bool useUnscaledTime;

        private void Update()
        {
            var dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            transform.Rotate(degreesPerSecond * dt, space);
        }
    }
}
