using UnityEngine;

namespace Wagenheimer.UnityUtils.Pooling
{
    /// <summary>
    /// Registers a scene-placed instance with a pool at startup. Compatible with the original
    /// Path-o-logical component (same serialized fields), so migrated components keep their data.
    /// </summary>
    [AddComponentMenu("Wagenheimer/Unity Utils/Pooling/Pre-Runtime Pool Item")]
    public class PreRuntimePoolItem : MonoBehaviour
    {
        [Tooltip("Name of the pool this instance belongs to.")]
        public string poolName = "";

        [Tooltip("Name of the prefab this instance came from (drag & drop would self-reference).")]
        public string prefabName = "";

        [Tooltip("Start the game with this instance despawned (inactive).")]
        public bool despawnOnStart = true;

        [Tooltip("Do not reparent the instance under the pool's group.")]
        public bool doNotReparent = false;

        private void Start()
        {
            if (!SpawnManager.Pools.TryGetValue(poolName, out var pool))
            {
                Debug.LogError($"[PreRuntimePoolItem] '{name}': no pool named '{poolName}' exists. " +
                               "Create one with SpawnManager.Pools.Create() or add a SpawnPool component.", this);
                return;
            }

            pool.Add(transform, prefabName, despawnOnStart, !doNotReparent);
        }
    }
}
