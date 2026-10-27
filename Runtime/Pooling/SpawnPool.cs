using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Wagenheimer.UnityUtils.Pooling
{
    /// <summary>
    /// Modern, allocation-conscious replacement for the Path-o-logical PoolManager
    /// <c>SpawnPool</c>. The serialized fields and the public API match the original, so a
    /// migrated scene keeps its data and existing code keeps compiling.
    ///
    /// Improvements over the original:
    /// <list type="bullet">
    /// <item>O(1) prefab and instance lookup (the old code scanned lists).</item>
    /// <item>No per-instance coroutine for delayed despawn, audio or particles.</item>
    /// <item>Legacy OnSpawned/OnDespawned broadcasts are detected once per prefab and skipped when absent.</item>
    /// <item>Optional <see cref="IPoolable"/> callbacks without reflection.</item>
    /// <item>Counters, snapshots, a runtime UI Toolkit overlay and a UI Toolkit inspector.</item>
    /// <item>FIFO/LIFO reuse and an allocation-free despawned queue.</item>
    /// </list>
    /// </summary>
    [AddComponentMenu("Wagenheimer/Unity Utils/Pooling/Spawn Pool")]
    [DisallowMultipleComponent]
    public sealed class SpawnPool : MonoBehaviour, IList<Transform>
    {
        #region Inspector parameters

        [Tooltip("Name used by SpawnManager.Pools[\"...\"]. Empty derives it from the GameObject name (strips 'Pool').")]
        public string poolName = "";

        [Tooltip("Match new instances to the pool GameObject's scale.")]
        public bool matchPoolScale = false;

        [Tooltip("Match new instances to the pool GameObject's layer.")]
        public bool matchPoolLayer = false;

        [Tooltip("Do not reparent new instances under this pool's Transform.")]
        public bool dontReparent = false;

        [Tooltip("Call DontDestroyOnLoad on the pool GameObject.")]
        public bool _dontDestroyOnLoad = false;

        [Tooltip("Print spawn/despawn/culling information to the console.")]
        public bool logMessages = false;

        [Tooltip("Broadcast the legacy OnSpawned/OnDespawned messages to prefab scripts.")]
        public bool sendLegacyMessages = true;

        [Tooltip("Use unscaled time for Despawn(instance, seconds).")]
        public bool despawnUsesUnscaledTime = false;

        [Tooltip("Per-prefab pool settings. Prefabs not listed here are created automatically on first Spawn().")]
        public List<PrefabPool> _perPrefabPoolOptions = new List<PrefabPool>();

        /// <summary>Used by the inspector to remember foldout states.</summary>
        [NonSerialized] public Dictionary<object, bool> prefabsFoldOutStates = new Dictionary<object, bool>();

        /// <summary>Legacy inspector foldout state storage.</summary>
        [NonSerialized] public Dictionary<object, bool> _editorListItemStates = new Dictionary<object, bool>();

        #endregion

        #region Public code-only parameters

        [Tooltip("Safety timeout (seconds) while waiting for a particle system to finish. A warning is logged once.")]
        public float maxParticleDespawnTime = 300f;

        /// <summary>Empty GameObject used as the parent of the pool's instances.</summary>
        public Transform group { get; private set; }

        /// <summary>Prefabs registered in this pool, by prefab name.</summary>
        [NonSerialized] public PrefabsDict prefabs = new PrefabsDict();

        public bool dontDestroyOnLoad
        {
            get => _dontDestroyOnLoad;
            set
            {
                _dontDestroyOnLoad = value;
                if (group != null && value)
                    Object.DontDestroyOnLoad(group.gameObject);
            }
        }

        /// <summary>Read-only dictionary of prefab name -> PrefabPool.</summary>
        public Dictionary<string, PrefabPool> prefabPools
        {
            get
            {
                var dict = new Dictionary<string, PrefabPool>(_prefabPools.Count);
                for (var i = 0; i < _prefabPools.Count; i++)
                {
                    var pool = _prefabPools[i];
                    if (pool != null && pool.prefab != null)
                        dict[pool.prefab.name] = pool;
                }

                return dict;
            }
        }

        #endregion

        #region Delegates

        public delegate GameObject InstantiateDelegate(GameObject prefab, Vector3 pos, Quaternion rot);
        public delegate void DestroyDelegate(GameObject instance);

        /// <summary>Optional per-pool override for Instantiate.</summary>
        public InstantiateDelegate instantiateDelegates;

        /// <summary>Optional per-pool override for Destroy.</summary>
        public DestroyDelegate destroyDelegates;

        #endregion

        #region Counters

        public int SpawnedCount => _spawned.Count;
        public int PeakSpawned { get; private set; }
        public long InstantiatedTotal { get; private set; }
        public long ReusedTotal { get; private set; }
        public long SpawnedTotal { get; private set; }
        public long DespawnedTotal { get; private set; }
        public long DestroyedTotal { get; private set; }
        public long LimitHits { get; private set; }

        public double AverageSpawnMilliseconds =>
            SpawnedTotal == 0 ? 0d : _spawnTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / SpawnedTotal;

        #endregion

        #region Private state

        private readonly List<PrefabPool> _prefabPools = new List<PrefabPool>();
        private readonly Dictionary<GameObject, PrefabPool> _prefabLookup = new Dictionary<GameObject, PrefabPool>();
        private readonly Dictionary<Transform, PrefabPool> _instanceOwners = new Dictionary<Transform, PrefabPool>();
        internal readonly List<Transform> _spawned = new List<Transform>();

        private readonly List<PendingDespawn> _pendingDespawns = new List<PendingDespawn>();
        private readonly List<PendingAudio> _pendingAudio = new List<PendingAudio>();
        private readonly List<PendingParticle> _pendingParticles = new List<PendingParticle>();

        private long _spawnTicks;
        private bool _registered;

        private struct PendingDespawn
        {
            public Transform Instance;
            public float Deadline;
            public Transform Parent;
            public bool UseParent;
            public bool Unscaled;
        }

        private struct PendingAudio
        {
            public AudioSource Source;
            public Transform Instance;
            public float ReadyAt;
        }

        private struct PendingParticle
        {
            public ParticleSystem Emitter;
            public Transform Instance;
            public float ReadyAt;
            public float Deadline;
        }

        #endregion

        #region Lifecycle

        private void EnsureInitialized()
        {
            if (group == null)
                group = transform;

            if (_perPrefabPoolOptions == null)
                _perPrefabPoolOptions = new List<PrefabPool>();

            if (prefabs == null)
                prefabs = new PrefabsDict();

            if (string.IsNullOrEmpty(poolName) && group != null)
            {
                // Original naming convention: "EnemiesPool" -> "Enemies".
                poolName = group.name.Replace("Pool", "").Replace("(Clone)", "");
            }
        }

        private void Awake()
        {
            if (_dontDestroyOnLoad)
                Object.DontDestroyOnLoad(gameObject);

            EnsureInitialized();

            if (SpawnManager.Settings.validateOnAwake)
                ValidateConfiguration();

            for (var i = 0; i < _perPrefabPoolOptions.Count; i++)
            {
                var option = _perPrefabPoolOptions[i];
                if (option == null) continue;

                if (option.prefab == null)
                {
                    LogWarning($"Initialization warning: pool '{poolName}' contains a PrefabPool with no prefab reference. Skipping.");
                    continue;
                }

                // A script that ran Spawn() from its own Awake may have created this pool first.
                if (GetPrefabPool(option.prefab) != null)
                    continue;

                option.InspectorInstanceConstructor();
                CreatePrefabPool(option);
            }

            _registered = SpawnManager.Pools.Add(this);
        }

        private void OnDestroy()
        {
            if (_registered || SpawnManager.Pools.ContainsValue(this))
                SpawnManager.Pools.Remove(this);

            _pendingDespawns.Clear();
            _pendingAudio.Clear();
            _pendingParticles.Clear();
            _spawned.Clear();

            for (var i = 0; i < _prefabPools.Count; i++)
                _prefabPools[i]?.SelfDestruct();

            _prefabPools.Clear();
            _prefabLookup.Clear();
            _instanceOwners.Clear();
            prefabs?._Clear();
        }

        private void Update()
        {
            if (_pendingDespawns.Count > 0)
                ProcessPendingDespawns();

            if (_pendingAudio.Count > 0)
                ProcessPendingAudio();

            if (_pendingParticles.Count > 0)
                ProcessPendingParticles();
        }

        #endregion

        #region Creation / registration

        /// <summary>
        /// Adds a PrefabPool to this SpawnPool. Use this only when you need to configure
        /// preload/culling before the first Spawn(); otherwise Spawn() creates it automatically.
        /// </summary>
        public void CreatePrefabPool(PrefabPool prefabPool)
        {
            EnsureInitialized();

            if (prefabPool == null || prefabPool.prefab == null)
            {
                LogError("CreatePrefabPool received a null prefab.");
                return;
            }

            if (GetPrefabPool(prefabPool.prefab) != null)
            {
                throw new InvalidOperationException(
                    $"Prefab '{prefabPool.prefab.name}' is already in SpawnPool '{poolName}'. " +
                    "A prefab can be in several pools, but not twice in the same pool.");
            }

            prefabPool.spawnPool = this;
            prefabPool.InspectorInstanceConstructor();

            if (prefabPool.prefabGO == null)
            {
                LogError($"Prefab '{prefabPool.prefab.name}' has no GameObject; cannot add it to pool '{poolName}'.");
                return;
            }

            _prefabPools.Add(prefabPool);
            _prefabLookup[prefabPool.prefabGO] = prefabPool;

            if (prefabs.ContainsKey(prefabPool.prefab.name))
                LogWarning($"A prefab named '{prefabPool.prefab.name}' is already registered in this pool. " +
                           "The prefabs dictionary keeps the first one; use the prefab reference to spawn the other.");
            else
                prefabs._Add(prefabPool.prefab.name, prefabPool.prefab);

            if (!prefabPool.preloaded)
            {
                if (logMessages)
                    LogInfo($"Preloading {prefabPool.preloadAmount} {prefabPool.prefab.name}");

                prefabPool.PreloadInstances();
            }
        }

        /// <summary>
        /// Adds an existing scene instance to a prefab pool.
        /// </summary>
        public void Add(Transform instance, string prefabName, bool despawn, bool parent)
        {
            EnsureInitialized();

            if (instance == null)
            {
                LogError("Add() received a null instance.");
                return;
            }

            for (var i = 0; i < _prefabPools.Count; i++)
            {
                var pool = _prefabPools[i];
                if (pool.prefabGO == null)
                {
                    LogError("Unexpected error: PrefabPool.prefabGO is null.");
                    return;
                }

                if (pool.prefabGO.name != prefabName) continue;

                pool.AddUnpooled(instance, despawn);

                if (parent)
                {
                    var worldPositionStays = !(instance is RectTransform);
                    instance.SetParent(group, worldPositionStays);
                }

                _instanceOwners[instance] = pool;

                if (!despawn)
                    _spawned.Add(instance);

                if (logMessages)
                    LogInfo($"Adding previously unpooled instance {instance.name}");

                return;
            }

            LogError($"PrefabPool '{prefabName}' not found in pool '{poolName}'.");
        }

        #endregion

        #region Spawn

        /// <summary>
        /// Spawns an instance (reusing a despawned one when possible). Returns null only when the
        /// prefab pool has <see cref="PrefabPool.limitInstances"/> on, the limit was reached and
        /// FIFO is disabled.
        /// </summary>
        public Transform Spawn(Transform prefab, Vector3 pos, Quaternion rot, Transform parent)
        {
            if (prefab == null)
            {
                LogError("Spawn() received a null prefab.");
                return null;
            }

            EnsureInitialized();

            var startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            var prefabPool = GetOrCreatePrefabPool(prefab);
            if (prefabPool == null) return null;

            var inst = prefabPool.SpawnInstance(pos, rot);
            if (inst == null) return null;

            ReparentInstance(inst, parent);

            _spawned.Add(inst);
            _instanceOwners[inst] = prefabPool;

            SpawnedTotal++;
            _spawnTicks += System.Diagnostics.Stopwatch.GetTimestamp() - startTicks;
            if (_spawned.Count > PeakSpawned)
                PeakSpawned = _spawned.Count;

            NotifySpawned(inst, prefabPool);
            return inst;
        }

        public Transform Spawn(Transform prefab, Vector3 pos, Quaternion rot) =>
            Spawn(prefab, pos, rot, null);

        public Transform Spawn(Transform prefab) =>
            Spawn(prefab, Vector3.zero, Quaternion.identity, null);

        public Transform Spawn(Transform prefab, Transform parent) =>
            Spawn(prefab, Vector3.zero, Quaternion.identity, parent);

        #region GameObject overloads

        public Transform Spawn(GameObject prefab, Vector3 pos, Quaternion rot, Transform parent) =>
            Spawn(prefab != null ? prefab.transform : null, pos, rot, parent);

        public Transform Spawn(GameObject prefab, Vector3 pos, Quaternion rot) =>
            Spawn(prefab != null ? prefab.transform : null, pos, rot, null);

        public Transform Spawn(GameObject prefab) =>
            Spawn(prefab != null ? prefab.transform : null, Vector3.zero, Quaternion.identity, null);

        public Transform Spawn(GameObject prefab, Transform parent) =>
            Spawn(prefab != null ? prefab.transform : null, Vector3.zero, Quaternion.identity, parent);

        #endregion

        #region Name overloads

        public Transform Spawn(string prefabName) =>
            Spawn(GetPrefabByName(prefabName), Vector3.zero, Quaternion.identity, null);

        public Transform Spawn(string prefabName, Transform parent) =>
            Spawn(GetPrefabByName(prefabName), Vector3.zero, Quaternion.identity, parent);

        public Transform Spawn(string prefabName, Vector3 pos, Quaternion rot) =>
            Spawn(GetPrefabByName(prefabName), pos, rot, null);

        public Transform Spawn(string prefabName, Vector3 pos, Quaternion rot, Transform parent) =>
            Spawn(GetPrefabByName(prefabName), pos, rot, parent);

        #endregion

        #region AudioSource overloads

        public AudioSource Spawn(AudioSource prefab, Vector3 pos, Quaternion rot) =>
            Spawn(prefab, pos, rot, null);

        public AudioSource Spawn(AudioSource prefab) =>
            Spawn(prefab, Vector3.zero, Quaternion.identity, null);

        public AudioSource Spawn(AudioSource prefab, Transform parent) =>
            Spawn(prefab, Vector3.zero, Quaternion.identity, parent);

        public AudioSource Spawn(AudioSource prefab, Vector3 pos, Quaternion rot, Transform parent)
        {
            if (prefab == null)
            {
                LogError("Spawn(AudioSource) received a null prefab.");
                return null;
            }

            var inst = Spawn(prefab.transform, pos, rot, parent);
            if (inst == null) return null;

            var src = inst.GetComponent<AudioSource>();
            if (src == null) src = inst.GetComponentInChildren<AudioSource>(true);
            if (src == null)
            {
                LogError($"Prefab '{prefab.name}' has no AudioSource; the instance will not auto-despawn.");
                return null;
            }

            src.Play();
            _pendingAudio.Add(new PendingAudio
            {
                Source = src,
                Instance = inst,
                // Wait one frame, like the original: isPlaying can be false on the activation frame.
                ReadyAt = Time.time + Mathf.Max(Time.deltaTime, 0.01f)
            });

            return src;
        }

        #endregion

        #region ParticleSystem overloads

        public ParticleSystem Spawn(ParticleSystem prefab, Vector3 pos, Quaternion rot) =>
            Spawn(prefab, pos, rot, null);

        public ParticleSystem Spawn(ParticleSystem prefab, Vector3 pos, Quaternion rot, Transform parent)
        {
            if (prefab == null)
            {
                LogError("Spawn(ParticleSystem) received a null prefab.");
                return null;
            }

            var inst = Spawn(prefab.transform, pos, rot, parent);
            if (inst == null) return null;

            var emitter = inst.GetComponent<ParticleSystem>();
            if (emitter == null) emitter = inst.GetComponentInChildren<ParticleSystem>(true);
            if (emitter == null)
            {
                LogError($"Prefab '{prefab.name}' has no ParticleSystem; the instance will not auto-despawn.");
                return null;
            }

            var startDelay = emitter.main.startDelay.constantMax;
            var now = Time.time;

            _pendingParticles.Add(new PendingParticle
            {
                Emitter = emitter,
                Instance = inst,
                ReadyAt = now + Mathf.Max(0f, startDelay) + 0.25f,
                Deadline = now + Mathf.Max(0.01f, maxParticleDespawnTime)
            });

            return emitter;
        }

        #endregion

        private PrefabPool GetOrCreatePrefabPool(Transform prefab)
        {
            if (prefab == null) return null;

            var prefabGO = prefab.gameObject;
            if (_prefabLookup.TryGetValue(prefabGO, out var existing))
                return existing;

            var created = new PrefabPool(prefab)
            {
                preloadAmount = Mathf.Max(0, SpawnManager.Settings.defaultPreloadAmount)
            };

            CreatePrefabPool(created);
            return created;
        }

        private Transform GetPrefabByName(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                LogError("Spawn(string) received an empty prefab name.");
                return null;
            }

            return prefabs[prefabName];
        }

        private void ReparentInstance(Transform inst, Transform parent)
        {
            if (inst == null) return;

            var worldPositionStays = !(inst is RectTransform);
            if (parent != null)
                inst.SetParent(parent, worldPositionStays);
            else if (!dontReparent && inst.parent != group)
                inst.SetParent(group, worldPositionStays);
        }

        #endregion

        #region Despawn

        /// <summary>Deactivates an instance and makes it available for reuse.</summary>
        public void Despawn(Transform instance) => DespawnInternal(instance, true);

        /// <summary>Reparents and then despawns an instance.</summary>
        public void Despawn(Transform instance, Transform parent)
        {
            if (instance == null) return;

            var worldPositionStays = !(instance is RectTransform);
            instance.SetParent(parent, worldPositionStays);
            DespawnInternal(instance, true);
        }

        /// <summary>Despawns after a delay (one central scheduler, no coroutine per instance).</summary>
        public void Despawn(Transform instance, float seconds)
        {
            if (instance == null) return;

            if (seconds <= 0f)
            {
                Despawn(instance);
                return;
            }

            _pendingDespawns.Add(new PendingDespawn
            {
                Instance = instance,
                Deadline = (despawnUsesUnscaledTime ? Time.unscaledTime : Time.time) + seconds,
                Unscaled = despawnUsesUnscaledTime
            });
        }

        /// <summary>Reparents and despawns after a delay.</summary>
        public void Despawn(Transform instance, float seconds, Transform parent)
        {
            if (instance == null) return;

            if (seconds <= 0f)
            {
                Despawn(instance, parent);
                return;
            }

            _pendingDespawns.Add(new PendingDespawn
            {
                Instance = instance,
                Deadline = (despawnUsesUnscaledTime ? Time.unscaledTime : Time.time) + seconds,
                Parent = parent,
                UseParent = true,
                Unscaled = despawnUsesUnscaledTime
            });
        }

        /// <summary>Despawns every active instance of this pool.</summary>
        public void DespawnAll()
        {
            var copy = new List<Transform>(_spawned);
            for (var i = 0; i < copy.Count; i++)
                Despawn(copy[i]);
        }

        /// <summary>Destroys extra despawned instances, keeping at most <paramref name="keepDespawnedPerPrefab"/> per prefab.</summary>
        public void Trim(int keepDespawnedPerPrefab = 0)
        {
            for (var i = 0; i < _prefabPools.Count; i++)
                _prefabPools[i]?.TrimDespawned(keepDespawnedPerPrefab);
        }

        internal bool DespawnInternal(Transform instance, bool notify)
        {
            if (instance == null)
            {
                LogWarning("Despawn() received a null instance.");
                return false;
            }

            if (!_instanceOwners.TryGetValue(instance, out var prefabPool))
            {
                prefabPool = FindPoolContaining(instance);
                if (prefabPool == null)
                {
                    LogError($"'{instance.name}' was not found in SpawnPool '{poolName}'. " +
                             "Only instances created by this pool (or added with Add()) can be despawned.");
                    return false;
                }

                _instanceOwners[instance] = prefabPool;
            }

            if (!prefabPool.DespawnInstance(instance, notify))
                return false;

            _spawned.Remove(instance);
            DespawnedTotal++;
            return true;
        }

        /// <summary>True when the transform is currently spawned (active) in this pool.</summary>
        public bool IsSpawned(Transform instance)
        {
            return instance != null &&
                   _instanceOwners.TryGetValue(instance, out var prefabPool) &&
                   prefabPool.IsSpawned(instance);
        }

        private PrefabPool FindPoolContaining(Transform instance)
        {
            for (var i = 0; i < _prefabPools.Count; i++)
            {
                if (_prefabPools[i].Contains(instance))
                    return _prefabPools[i];
            }

            return null;
        }

        #endregion

        #region Queries

        /// <summary>Returns the PrefabPool for a prefab.</summary>
        public PrefabPool GetPrefabPool(Transform prefab) =>
            prefab != null ? GetPrefabPool(prefab.gameObject) : null;

        /// <summary>Returns the PrefabPool for a prefab.</summary>
        public PrefabPool GetPrefabPool(GameObject prefab)
        {
            if (prefab == null) return null;
            return _prefabLookup.TryGetValue(prefab, out var prefabPool) ? prefabPool : null;
        }

        /// <summary>Returns the prefab that created an instance (works for spawned and despawned instances).</summary>
        public Transform GetPrefab(Transform instance)
        {
            if (instance == null) return null;

            if (_instanceOwners.TryGetValue(instance, out var prefabPool))
                return prefabPool.prefab;

            for (var i = 0; i < _prefabPools.Count; i++)
            {
                if (_prefabPools[i].Contains(instance))
                    return _prefabPools[i].prefab;
            }

            return null;
        }

        /// <summary>Returns the prefab that created an instance.</summary>
        public GameObject GetPrefab(GameObject instance) =>
            instance != null ? GetPrefab(instance.transform)?.gameObject : null;

        /// <summary>Snapshot of this pool, used by the inspector and the runtime overlay.</summary>
        public PoolSnapshot GetSnapshot()
        {
            var snapshot = new PoolSnapshot
            {
                PoolName = poolName,
                PrefabCount = _prefabPools.Count,
                Spawned = _spawned.Count,
                Despawned = 0,
                Total = 0,
                PeakSpawned = PeakSpawned,
                Instantiated = InstantiatedTotal,
                Reused = ReusedTotal,
                LimitHits = LimitHits,
                Culled = 0,
                AverageSpawnMilliseconds = AverageSpawnMilliseconds
            };

            long culled = 0;
            for (var i = 0; i < _prefabPools.Count; i++)
            {
                var prefab = _prefabPools[i];
                if (prefab == null) continue;

                var prefabSnapshot = prefab.CreateSnapshot();
                snapshot.Prefabs.Add(prefabSnapshot);
                snapshot.Despawned += prefabSnapshot.Despawned;
                culled += prefabSnapshot.Culled;
            }

            snapshot.Total = snapshot.Spawned + snapshot.Despawned;
            snapshot.Culled = culled;
            return snapshot;
        }

        #endregion

        #region Notifications

        internal void NotifyInstantiated() => InstantiatedTotal++;

        internal void NotifyReused() => ReusedTotal++;

        internal void NotifyLimitHit() => LimitHits++;

        internal void NotifySpawned(Transform inst, PrefabPool prefabPool)
        {
            var poolables = prefabPool.GetPoolables(inst);
            if (poolables != null)
            {
                for (var i = 0; i < poolables.Length; i++)
                    poolables[i].OnSpawned(this);
            }

            if (sendLegacyMessages && SpawnManager.Settings.sendLegacyMessages && prefabPool.HasLegacyMessages())
                inst.gameObject.BroadcastMessage("OnSpawned", this, SendMessageOptions.DontRequireReceiver);
        }

        internal void NotifyDespawned(Transform inst, PrefabPool prefabPool)
        {
            var poolables = prefabPool.GetPoolables(inst);
            if (poolables != null)
            {
                for (var i = 0; i < poolables.Length; i++)
                    poolables[i].OnDespawned(this);
            }

            if (sendLegacyMessages && SpawnManager.Settings.sendLegacyMessages && prefabPool.HasLegacyMessages())
                inst.gameObject.BroadcastMessage("OnDespawned", this, SendMessageOptions.DontRequireReceiver);
        }

        #endregion

        #region Pending work

        private void ProcessPendingDespawns()
        {
            var now = despawnUsesUnscaledTime ? Time.unscaledTime : Time.time;

            for (var i = _pendingDespawns.Count - 1; i >= 0; i--)
            {
                var pending = _pendingDespawns[i];

                if (pending.Instance == null || !IsSpawned(pending.Instance))
                {
                    _pendingDespawns.RemoveAt(i);
                    continue;
                }

                if (now < pending.Deadline)
                    continue;

                _pendingDespawns.RemoveAt(i);

                if (pending.UseParent)
                    Despawn(pending.Instance, pending.Parent);
                else
                    Despawn(pending.Instance);
            }
        }

        private void ProcessPendingAudio()
        {
            for (var i = _pendingAudio.Count - 1; i >= 0; i--)
            {
                var pending = _pendingAudio[i];

                if (pending.Source == null || pending.Instance == null || !pending.Instance.gameObject.activeInHierarchy)
                {
                    if (pending.Source != null)
                        pending.Source.Stop();

                    _pendingAudio.RemoveAt(i);
                    continue;
                }

                if (Time.time < pending.ReadyAt)
                    continue;

                if (pending.Source.isPlaying)
                    continue;

                _pendingAudio.RemoveAt(i);
                Despawn(pending.Instance);
            }
        }

        private void ProcessPendingParticles()
        {
            for (var i = _pendingParticles.Count - 1; i >= 0; i--)
            {
                var pending = _pendingParticles[i];

                if (pending.Emitter == null || pending.Instance == null || !pending.Instance.gameObject.activeInHierarchy)
                {
                    _pendingParticles.RemoveAt(i);
                    continue;
                }

                if (Time.time < pending.ReadyAt)
                    continue;

                if (pending.Emitter.IsAlive(true))
                {
                    if (Time.time < pending.Deadline)
                        continue;

                    // The original logged this every frame and never stopped; log once and recover.
                    LogWarning($"Timed out waiting for particles of '{pending.Instance.name}' to die " +
                               $"({maxParticleDespawnTime:0.#}s). Despawning the instance anyway.");
                }

                _pendingParticles.RemoveAt(i);
                Despawn(pending.Instance);
                pending.Emitter.Clear(true);
            }
        }

        #endregion

        #region Destroy / instantiate

        internal GameObject InstantiatePrefab(GameObject prefab, Vector3 pos, Quaternion rot)
        {
            if (instantiateDelegates != null)
                return instantiateDelegates(prefab, pos, rot);

            return InstanceHandler.InstantiatePrefab(prefab, pos, rot);
        }

        internal void DestroyInstance(GameObject instance)
        {
            if (instance == null) return;

            if (destroyDelegates != null)
                destroyDelegates(instance);
            else
                InstanceHandler.DestroyInstance(instance);
        }

        /// <summary>Destroys a despawned instance and cleans the bookkeeping.</summary>
        internal void DestroyPooledInstance(Transform inst, PrefabPool prefabPool)
        {
            if (inst == null) return;

            _instanceOwners.Remove(inst);
            prefabPool?.ForgetInstance(inst);
            DestroyedTotal++;
            DestroyInstance(inst.gameObject);
        }

        #endregion

        #region Validation / logging

        private void ValidateConfiguration()
        {
            var seen = new HashSet<GameObject>();

            for (var i = 0; i < _perPrefabPoolOptions.Count; i++)
            {
                var option = _perPrefabPoolOptions[i];
                if (option == null) continue;

                if (option.prefab == null)
                {
                    LogWarning($"Pool '{poolName}': PrefabPool at index {i} has no prefab.");
                    continue;
                }

                if (!seen.Add(option.prefab.gameObject))
                    LogWarning($"Pool '{poolName}': prefab '{option.prefab.name}' is listed more than once.");

                if (option.limitInstances && option.limitAmount <= 0)
                    LogWarning($"Pool '{poolName}' ({option.prefab.name}): 'Limit Amount' must be greater than zero.");

                if (option.limitInstances && option.preloadAmount > option.limitAmount)
                    LogWarning($"Pool '{poolName}' ({option.prefab.name}): preload amount is greater than the limit; it will be clamped.");

                if (option.cullDespawned && option.preloadAmount > option.cullAbove)
                    LogWarning($"Pool '{poolName}' ({option.prefab.name}): 'Cull Above' should be greater than 'Preload Amount'.");

                if (option.preloadTime && option.preloadFrames > option.preloadAmount)
                    LogWarning($"Pool '{poolName}' ({option.prefab.name}): preload frames is greater than the preload amount; it will be clamped.");
            }
        }

        internal void LogError(string message)
        {
            if (SpawnManager.ShouldLog(PoolLogLevel.Errors, logMessages))
                Debug.LogError($"[SpawnPool:{poolName}] {message}", this);
        }

        internal void LogWarning(string message)
        {
            if (SpawnManager.ShouldLog(PoolLogLevel.Warnings, logMessages))
                Debug.LogWarning($"[SpawnPool:{poolName}] {message}", this);
        }

        internal void LogInfo(string message)
        {
            if (SpawnManager.ShouldLog(PoolLogLevel.Info, logMessages))
                Debug.Log($"[SpawnPool:{poolName}] {message}", this);
        }

        internal void LogVerbose(string message)
        {
            if (SpawnManager.ShouldLog(PoolLogLevel.Verbose, logMessages))
                Debug.Log($"[SpawnPool:{poolName}] {message}", this);
        }

        #endregion

        #region IList<Transform>

        /// <summary>Read-only index access to the spawned instances.</summary>
        public Transform this[int index]
        {
            get => _spawned[index];
            set => throw new NotImplementedException("SpawnPool is read-only. Use Spawn()/Despawn().");
        }

        public int Count => _spawned.Count;

        public bool IsReadOnly => true;

        /// <summary>Not implemented: use Spawn() so the prefab is registered.</summary>
        public void Add(Transform item) =>
            throw new NotImplementedException("Use SpawnPool.Spawn() to add items to the pool.");

        /// <summary>Not implemented: use Despawn() so the instance stays available for reuse.</summary>
        public void Remove(Transform item) =>
            throw new NotImplementedException("Use SpawnPool.Despawn() to deactivate an item; it stays in the pool.");

        public bool Contains(Transform item) =>
            throw new NotImplementedException("Use IsSpawned(Transform instance) instead.");

        public void CopyTo(Transform[] array, int arrayIndex) => _spawned.CopyTo(array, arrayIndex);

        public int IndexOf(Transform item) => throw new NotImplementedException();

        public void Insert(int index, Transform item) => throw new NotImplementedException();

        public void RemoveAt(int index) => throw new NotImplementedException();

        public void Clear() => throw new NotImplementedException();

        bool ICollection<Transform>.Remove(Transform item) => throw new NotImplementedException();

        public IEnumerator<Transform> GetEnumerator()
        {
            for (var i = 0; i < _spawned.Count; i++)
                yield return _spawned[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString()
        {
            var names = new StringBuilder();
            for (var i = 0; i < _spawned.Count; i++)
            {
                if (i > 0) names.Append(", ");
                names.Append(_spawned[i] != null ? _spawned[i].name : "null");
            }

            return names.ToString();
        }

        #endregion
    }
}
