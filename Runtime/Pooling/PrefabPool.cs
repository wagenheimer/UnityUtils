using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Wagenheimer.UnityUtils.Pooling
{
    /// <summary>
    /// Per-prefab pool settings and storage. Serialized field names intentionally match the
    /// original Path-o-logical PoolManager so legacy scenes/prefabs migrate without data loss.
    ///
    /// The public API is also compatible with the original class; the new additions are
    /// <see cref="reuseMode"/>, counters, snapshots and allocation-free bookkeeping.
    /// </summary>
    [Serializable]
    public class PrefabPool
    {
        #region Serialized settings

        [Tooltip("Prefab used to create the instances of this pool.")]
        public Transform prefab;

        /// <summary>Cached GameObject of <see cref="prefab"/> (not serialized).</summary>
        internal GameObject prefabGO;

        [Tooltip("How many instances to create during warm-up.")]
        public int preloadAmount = 1;

        [Tooltip("Spread preloading across frames instead of doing it all at once.")]
        public bool preloadTime = false;

        [Tooltip("Number of frames used to spread the preload.")]
        public int preloadFrames = 2;

        [Tooltip("Seconds to wait before the over-time preload starts.")]
        public float preloadDelay = 0;

        [Tooltip("Stop creating instances after limitAmount.")]
        public bool limitInstances = false;

        [Tooltip("Maximum number of instances (spawned + despawned) when limitInstances is on.")]
        public int limitAmount = 100;

        [Tooltip("When the limit is reached, despawn the oldest spawned instance and reuse it instead of returning null.")]
        public bool limitFIFO = false;

        [Tooltip("Destroy extra despawned instances when the pool grows above cullAbove.")]
        public bool cullDespawned = false;

        [Tooltip("Total instances (spawned + despawned) to keep when culling.")]
        public int cullAbove = 50;

        [Tooltip("Seconds between culling passes.")]
        public int cullDelay = 60;

        [Tooltip("Maximum number of instances destroyed per culling pass.")]
        public int cullMaxPerPass = 5;

        [Tooltip("Print this prefab's spawn/despawn/culling information. The SpawnPool 'Log Messages' also enables it.")]
        public bool _logMessages = false;

        [Tooltip("FIFO reuses the oldest instance (original behaviour). LIFO reuses the newest (better cache locality).")]
        public PoolReuseMode reuseMode = PoolReuseMode.Fifo;

        #endregion

        #region Runtime state

        /// <summary>Owner pool. Not serialized: the old asset stored a null reference here.</summary>
        [NonSerialized] public SpawnPool spawnPool;

        internal List<Transform> _spawned = new List<Transform>();
        internal List<Transform> _despawned = new List<Transform>();

        private readonly HashSet<Transform> _spawnedSet = new HashSet<Transform>();
        private readonly HashSet<Transform> _despawnedSet = new HashSet<Transform>();
        private readonly Dictionary<Transform, IPoolable[]> _poolableCache = new Dictionary<Transform, IPoolable[]>();

        private int _despawnedHead;
        private int _nextInstanceNumber;
        private bool? _hasLegacyMessages;
        private bool _cullingActive;
        private bool _forceLoggingSilent;
        private bool _preloaded;

        internal int PeakSpawned;
        internal long Instantiated;
        internal long Reused;
        internal long LimitHits;
        internal long Culled;
        internal long SpawnTicks;
        internal long SpawnsMeasured;

        #endregion

        #region Construction

        /// <summary>Constructor for runtime-created pools.</summary>
        public PrefabPool(Transform prefab)
        {
            this.prefab = prefab;
            prefabGO = prefab != null ? prefab.gameObject : null;
        }

        /// <summary>Constructor used by Unity's serializer.</summary>
        public PrefabPool() { }

        /// <summary>
        /// Initializes the runtime-only state of a PrefabPool that came from the inspector
        /// (Unity does not run constructors for serialized classes).
        /// </summary>
        internal void InspectorInstanceConstructor()
        {
            prefabGO = prefab != null ? prefab.gameObject : null;

            if (_spawned == null) _spawned = new List<Transform>();
            if (_despawned == null) _despawned = new List<Transform>();
        }

        #endregion

        #region Public read-only state

        public bool logMessages
        {
            get
            {
                if (_forceLoggingSilent) return false;
                if (spawnPool != null && spawnPool.logMessages) return true;
                return _logMessages;
            }
        }

        /// <summary>Active instances created by this prefab.</summary>
        public List<Transform> spawned => new List<Transform>(_spawned);

        /// <summary>Inactive instances available for reuse (a copy, safe to iterate).</summary>
        public List<Transform> despawned => GetDespawnedCopy();

        public int totalCount => _spawned.Count + DespawnedCount;

        public int spawnedCount => _spawned.Count;

        public int despawnedCount => DespawnedCount;

        private int DespawnedCount => _despawned.Count - _despawnedHead;

        public double AverageSpawnMilliseconds =>
            SpawnsMeasured == 0 ? 0d : SpawnTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / SpawnsMeasured;

        /// <summary>True after <see cref="PreloadInstances"/> ran (or was requested) once.</summary>
        internal bool preloaded => _preloaded;

        #endregion

        #region Spawning

        /// <summary>
        /// Reuses a despawned instance or creates a new one. Returns null when
        /// <see cref="limitInstances"/> is on, the limit was reached and FIFO is off.
        /// </summary>
        internal Transform SpawnInstance(Vector3 pos, Quaternion rot)
        {
            // Old behaviour: FIFO limits recycle the oldest spawned instance.
            if (limitInstances && limitFIFO && _spawned.Count >= limitAmount && _spawned.Count > 0)
            {
                var firstIn = _spawned[0];
                if (logMessages)
                    spawnPool.LogInfo($"SpawnPool {spawnPool.poolName} ({prefab.name}): LIMIT REACHED! FIFO=True. Despawning '{firstIn.name}'...");

                spawnPool.DespawnInternal(firstIn, true);
            }

            if (DespawnedCount == 0)
                return SpawnNew(pos, rot);

            var reuseStart = System.Diagnostics.Stopwatch.GetTimestamp();
            var inst = TakeDespawned();
            if (inst == null)
                throw new MissingReferenceException(
                    $"SpawnPool {spawnPool.poolName} ({prefab.name}): a despawned instance was destroyed directly. " +
                    "Never Destroy() a pooled instance; despawn it instead.");

            _spawned.Add(inst);
            _spawnedSet.Add(inst);
            PeakSpawned = Mathf.Max(PeakSpawned, _spawned.Count);

            inst.position = pos;
            inst.rotation = rot;
            inst.gameObject.SetActive(true);

            Reused++;
            spawnPool.NotifyReused();
            RecordSpawnTicks(reuseStart);

            if (logMessages)
                spawnPool.LogVerbose($"SpawnPool {spawnPool.poolName} ({prefab.name}): respawning '{inst.name}'.");

            return inst;
        }

        /// <summary>Creates a brand-new instance of the prefab.</summary>
        public Transform SpawnNew() => SpawnNew(Vector3.zero, Quaternion.identity);

        /// <summary>Creates a brand-new instance of the prefab at the given position/rotation.</summary>
        public Transform SpawnNew(Vector3 pos, Quaternion rot)
        {
            if (spawnPool == null)
            {
                Debug.LogError($"[SpawnPool] PrefabPool '{prefab?.name}' has no owner pool. Was it created with SpawnPool.CreatePrefabPool?");
                return null;
            }

            if (limitInstances && totalCount >= limitAmount)
            {
                LimitHits++;
                spawnPool.NotifyLimitHit();

                if (logMessages)
                    spawnPool.LogWarning($"SpawnPool {spawnPool.poolName} ({prefab.name}): LIMIT REACHED! Not creating new instances (returning null).");

                return null;
            }

            if (prefabGO == null)
            {
                spawnPool.LogError($"SpawnPool {spawnPool.poolName}: prefab is null; cannot spawn.");
                return null;
            }

            // Match the original defaults: zero/identity means "use the pool group's transform".
            if (pos == Vector3.zero) pos = spawnPool.group.position;
            if (rot == Quaternion.identity) rot = spawnPool.group.rotation;

            var spawnStart = System.Diagnostics.Stopwatch.GetTimestamp();
            var instGO = spawnPool.InstantiatePrefab(prefabGO, pos, rot);
            if (instGO == null)
            {
                spawnPool.LogError($"SpawnPool {spawnPool.poolName}: Instantiate returned null for '{prefab.name}'.");
                return null;
            }

            var inst = instGO.transform;
            NameInstance(inst);

            if (!spawnPool.dontReparent)
            {
                var worldPositionStays = !(inst is RectTransform);
                inst.SetParent(spawnPool.group, worldPositionStays);
            }

            if (spawnPool.matchPoolScale)
                inst.localScale = Vector3.one;

            if (spawnPool.matchPoolLayer)
                SetRecursively(inst, spawnPool.gameObject.layer);

            _spawned.Add(inst);
            _spawnedSet.Add(inst);
            PeakSpawned = Mathf.Max(PeakSpawned, _spawned.Count);

            Instantiated++;
            spawnPool.NotifyInstantiated();
            RecordSpawnTicks(spawnStart);

            if (logMessages)
                spawnPool.LogVerbose($"SpawnPool {spawnPool.poolName} ({prefab.name}): spawned new instance '{inst.name}'.");

            return inst;
        }

        private void RecordSpawnTicks(long startTicks)
        {
            SpawnTicks += System.Diagnostics.Stopwatch.GetTimestamp() - startTicks;
            SpawnsMeasured++;
        }

        private void NameInstance(Transform instance)
        {
            if (instance == null) return;

            var baseName = prefab != null ? prefab.name.Replace("(Clone)", "") : instance.name;
            instance.name = baseName + "(Clone)" + (++_nextInstanceNumber).ToString("000");
        }

        private static void SetRecursively(Transform xform, int layer)
        {
            xform.gameObject.layer = layer;
            foreach (Transform child in xform)
                SetRecursively(child, layer);
        }

        #endregion

        #region Despawning / culling

        internal bool DespawnInstance(Transform xform) => DespawnInstance(xform, true);

        internal bool DespawnInstance(Transform xform, bool sendEventMessage)
        {
            if (xform == null) return false;

            if (!_spawnedSet.Remove(xform))
            {
                if (_despawnedSet.Contains(xform))
                    spawnPool.LogError($"SpawnPool {spawnPool.poolName}: '{xform.name}' has already been despawned. You cannot despawn something more than once!");

                return false;
            }

            _spawned.Remove(xform);
            AddDespawned(xform);

            // Notify before deactivating, exactly like the original implementation, so scripts
            // can still read their state/components in OnDespawned.
            if (sendEventMessage)
                spawnPool.NotifyDespawned(xform, this);

            xform.gameObject.SetActive(false);

            if (!_cullingActive && cullDespawned && totalCount > cullAbove)
            {
                _cullingActive = true;
                spawnPool.StartCoroutine(CullDespawned());
            }

            return true;
        }

        private void AddDespawned(Transform inst)
        {
            if (_despawnedHead >= _despawned.Count)
            {
                _despawned.Clear();
                _despawnedHead = 0;
            }

            _despawned.Add(inst);
            _despawnedSet.Add(inst);
        }

        /// <summary>Destroys extra despawned instances immediately, keeping at most <paramref name="keep"/>.</summary>
        public void TrimDespawned(int keep)
        {
            keep = Mathf.Max(0, keep);
            while (DespawnedCount > keep)
            {
                var inst = TakeDespawned();
                if (inst == null) break;

                Culled++;
                spawnPool.DestroyPooledInstance(inst, this);
            }
        }

        /// <summary>Waits <see cref="cullDelay"/> and destroys despawned instances until the pool is at <see cref="cullAbove"/>.</summary>
        internal IEnumerator CullDespawned()
        {
            if (logMessages)
                spawnPool.LogInfo($"SpawnPool {spawnPool.poolName} ({prefab.name}): CULLING TRIGGERED! Waiting {cullDelay}sec...");

            yield return new WaitForSeconds(Mathf.Max(0f, cullDelay));

            while (totalCount > cullAbove)
            {
                var destroyedThisPass = 0;
                var perPass = Mathf.Max(1, cullMaxPerPass);

                for (var i = 0; i < perPass && totalCount > cullAbove; i++)
                {
                    var inst = TakeDespawned();
                    if (inst == null) break;

                    Culled++;
                    destroyedThisPass++;
                    spawnPool.DestroyPooledInstance(inst, this);
                }

                if (destroyedThisPass == 0)
                    break;

                if (logMessages)
                    spawnPool.LogInfo($"SpawnPool {spawnPool.poolName} ({prefab.name}): CULLING to {cullAbove} instances. Now at {totalCount}.");

                yield return new WaitForSeconds(Mathf.Max(0f, cullDelay));
            }

            if (logMessages)
                spawnPool.LogInfo($"SpawnPool {spawnPool.poolName} ({prefab.name}): CULLING FINISHED. Stopping.");

            _cullingActive = false;
        }

        #endregion

        #region Preloading

        /// <summary>
        /// Preloads <see cref="preloadAmount"/> instances if they do not already exist.
        /// Safe to call from Awake; the over-time mode spreads the work across frames.
        /// </summary>
        internal void PreloadInstances()
        {
            if (_preloaded)
            {
                spawnPool.LogWarning($"SpawnPool {spawnPool.poolName} ({prefab.name}): already preloaded. " +
                                     "Preloading twice is not allowed (check the inspector and your code).");
                return;
            }

            _preloaded = true;

            if (prefab == null)
            {
                spawnPool.LogError($"SpawnPool {spawnPool.poolName}: prefab cannot be null.");
                return;
            }

            if (limitInstances && preloadAmount > limitAmount)
            {
                spawnPool.LogWarning($"SpawnPool {spawnPool.poolName} ({prefab.name}): 'Limit Instances' is ON and " +
                                     "'Preload Amount' is greater than 'Limit Amount'. Clamping preload to the limit.");
                preloadAmount = limitAmount;
            }

            if (cullDespawned && preloadAmount > cullAbove)
            {
                spawnPool.LogWarning($"SpawnPool {spawnPool.poolName} ({prefab.name}): culling is ON and 'Cull Above' is " +
                                     "smaller than 'Preload Amount'. Culling will trigger immediately; raise 'Cull Above' " +
                                     "above the preload amount.");
            }

            if (preloadAmount <= 0)
                return;

            if (preloadTime)
            {
                if (preloadFrames > preloadAmount)
                {
                    spawnPool.LogWarning($"SpawnPool {spawnPool.poolName} ({prefab.name}): preload frames ({preloadFrames}) " +
                                         $"is greater than the preload amount ({preloadAmount}). Clamping it.");
                    preloadFrames = Mathf.Max(1, preloadAmount);
                }

                spawnPool.StartCoroutine(PreloadOverTime());
                return;
            }

            // Silence per-instance logs while warming up.
            _forceLoggingSilent = true;
            try
            {
                var guard = 0;
                while (totalCount < preloadAmount && guard++ < 100000)
                {
                    var inst = SpawnNew();
                    if (inst == null) break;
                    DespawnInstance(inst, false);
                }
            }
            finally
            {
                _forceLoggingSilent = false;
            }
        }

        private IEnumerator PreloadOverTime()
        {
            if (preloadDelay > 0f)
                yield return new WaitForSeconds(preloadDelay);

            var amount = preloadAmount - totalCount;
            if (amount <= 0)
                yield break;

            var frames = Mathf.Max(1, Mathf.Min(preloadFrames, amount));
            var perFrame = amount / frames;
            var remainder = amount % frames;

            _forceLoggingSilent = true;
            try
            {
                for (var frame = 0; frame < frames; frame++)
                {
                    var count = perFrame + (frame == frames - 1 ? remainder : 0);
                    for (var i = 0; i < count; i++)
                    {
                        var inst = SpawnNew();
                        if (inst == null) yield break;

                        DespawnInstance(inst, false);

                        // One instance per frame keeps the warm-up smooth, like the original.
                        yield return null;
                    }
                }
            }
            finally
            {
                _forceLoggingSilent = false;
            }
        }

        #endregion

        #region Adding existing instances

        /// <summary>Adds an instance that was not created by this pool (scene-placed objects).</summary>
        internal void AddUnpooled(Transform inst, bool despawn)
        {
            if (inst == null) return;

            NameInstance(inst);

            if (despawn)
            {
                AddDespawned(inst);
                inst.gameObject.SetActive(false);
                return;
            }

            _spawned.Add(inst);
            _spawnedSet.Add(inst);
            PeakSpawned = Mathf.Max(PeakSpawned, _spawned.Count);
        }

        #endregion

        #region Queries / utilities

        public bool Contains(Transform transform)
        {
            return transform != null && (_spawnedSet.Contains(transform) || _despawnedSet.Contains(transform));
        }

        internal bool IsSpawned(Transform transform) => transform != null && _spawnedSet.Contains(transform);

        internal bool IsDespawned(Transform transform) => transform != null && _despawnedSet.Contains(transform);

        internal PrefabPoolSnapshot CreateSnapshot()
        {
            return new PrefabPoolSnapshot
            {
                PrefabName = prefab != null ? prefab.name : "(missing prefab)",
                Spawned = _spawned.Count,
                Despawned = DespawnedCount,
                Total = totalCount,
                PeakSpawned = PeakSpawned,
                Instantiated = Instantiated,
                Reused = Reused,
                LimitHits = LimitHits,
                Culled = Culled,
                AverageSpawnMilliseconds = AverageSpawnMilliseconds
            };
        }

        /// <summary>Returns the IPoolable components of an instance, scanning each instance only once.</summary>
        internal IPoolable[] GetPoolables(Transform inst)
        {
            if (inst == null) return null;
            if (_poolableCache.TryGetValue(inst, out var cached)) return cached;

            var behaviours = inst.GetComponentsInChildren<MonoBehaviour>(true);
            List<IPoolable> found = null;

            foreach (var behaviour in behaviours)
            {
                if (behaviour is IPoolable poolable)
                    (found ??= new List<IPoolable>()).Add(poolable);
            }

            var result = found?.ToArray();
            _poolableCache[inst] = result;
            return result;
        }

        /// <summary>
        /// True when the prefab has any legacy OnSpawned/OnDespawned method. When the setting
        /// 'Detect Legacy Messages' is off this always returns true (the original behaviour).
        /// </summary>
        internal bool HasLegacyMessages()
        {
            if (!SpawnManager.Settings.detectLegacyMessages) return true;
            if (_hasLegacyMessages.HasValue) return _hasLegacyMessages.Value;

            var found = false;
            if (prefabGO != null)
            {
                var behaviours = prefabGO.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (var behaviour in behaviours)
                {
                    if (behaviour == null) continue;

                    var type = behaviour.GetType();
                    if (HasMethod(type, "OnSpawned") || HasMethod(type, "OnDespawned"))
                    {
                        found = true;
                        break;
                    }
                }
            }

            _hasLegacyMessages = found;
            return found;
        }

        private static bool HasMethod(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var current = type; current != null && current != typeof(MonoBehaviour); current = current.BaseType)
            {
                if (current.GetMethod(name, flags) != null)
                    return true;
            }

            return false;
        }

        internal void ForgetInstance(Transform inst)
        {
            if (inst == null) return;
            _poolableCache.Remove(inst);
        }

        /// <summary>Destroys every instance and clears the pool. Called by SpawnPool.OnDestroy.</summary>
        internal void SelfDestruct()
        {
            if (spawnPool == null) return;

            if (logMessages)
                spawnPool.LogInfo($"SpawnPool {spawnPool.poolName}: cleaning up PrefabPool for {(prefabGO != null ? prefabGO.name : prefab != null ? prefab.name : "?")}...");

            for (var i = _spawned.Count - 1; i >= 0; i--)
            {
                var inst = _spawned[i];
                if (inst != null)
                    spawnPool.DestroyInstance(inst.gameObject);
            }

            for (var i = _despawnedHead; i < _despawned.Count; i++)
            {
                var inst = _despawned[i];
                if (inst != null)
                    spawnPool.DestroyInstance(inst.gameObject);
            }

            _spawned.Clear();
            _despawned.Clear();
            _spawnedSet.Clear();
            _despawnedSet.Clear();
            _poolableCache.Clear();
            _despawnedHead = 0;

            prefab = null;
            prefabGO = null;
            spawnPool = null;
        }

        #endregion

        #region Reuse queue

        private Transform TakeDespawned()
        {
            if (DespawnedCount <= 0) return null;

            Transform inst;
            if (reuseMode == PoolReuseMode.Lifo)
            {
                var last = _despawned.Count - 1;
                inst = _despawned[last];
                _despawned.RemoveAt(last);
            }
            else
            {
                inst = _despawned[_despawnedHead++];
                CompactDespawned();
            }

            _despawnedSet.Remove(inst);
            return inst;
        }

        private void CompactDespawned()
        {
            if (_despawnedHead <= 0) return;

            if (_despawnedHead >= _despawned.Count)
            {
                _despawned.Clear();
                _despawnedHead = 0;
                return;
            }

            if (_despawnedHead > 64 && _despawnedHead * 2 >= _despawned.Count)
            {
                _despawned.RemoveRange(0, _despawnedHead);
                _despawnedHead = 0;
            }
        }

        private List<Transform> GetDespawnedCopy()
        {
            var copy = new List<Transform>(DespawnedCount);
            for (var i = _despawnedHead; i < _despawned.Count; i++)
                copy.Add(_despawned[i]);

            return copy;
        }

        #endregion
    }
}
