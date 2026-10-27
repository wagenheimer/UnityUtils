using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Wagenheimer.UnityUtils.Pooling
{
    /// <summary>
    /// Modern entry point for the pooling system. Keeps the familiar
    /// <c>SpawnManager.Pools["level"]</c> lookup while adding diagnostics, settings and
    /// project-wide actions.
    ///
    /// <see cref="PoolManager"/> is a source-compatible alias, so migrating a project only
    /// requires replacing <c>using PathologicalGames;</c> with
    /// <c>using Wagenheimer.UnityUtils.Pooling;</c>.
    /// </summary>
    public static class SpawnManager
    {
        /// <summary>All registered pools, by <see cref="SpawnPool.poolName"/>.</summary>
        public static readonly SpawnPoolsDict Pools = new SpawnPoolsDict();

        private static SpawnManagerSettings _settings;

        /// <summary>
        /// Project settings from <c>Resources/Wagenheimer/SpawnManagerSettings</c>, or a transient
        /// default instance when the asset does not exist.
        /// </summary>
        public static SpawnManagerSettings Settings
        {
            get
            {
                if (_settings != null) return _settings;

                _settings = Resources.Load<SpawnManagerSettings>("Wagenheimer/SpawnManagerSettings");
                if (_settings == null)
                {
                    _settings = ScriptableObject.CreateInstance<SpawnManagerSettings>();
                    _settings.name = "SpawnManagerSettings (defaults)";
                    _settings.hideFlags = HideFlags.DontSave;
                }

                return _settings;
            }
        }

        /// <summary>Shortcut for <see cref="SpawnManagerSettings.logLevel"/>.</summary>
        public static PoolLogLevel LogLevel
        {
            get => Settings.logLevel;
            set => Settings.logLevel = value;
        }

        /// <summary>True when a message at <paramref name="level"/> should be written.</summary>
        public static bool ShouldLog(PoolLogLevel level, bool force)
        {
            if (force) return true;
            return Settings.logLevel >= level;
        }

        /// <summary>Returns the pool with the given name, or null.</summary>
        public static SpawnPool GetPool(string poolName)
        {
            return poolName != null && Pools.TryGetValue(poolName, out var pool) ? pool : null;
        }

        /// <summary>Returns the pool with the given name.</summary>
        public static bool TryGetPool(string poolName, out SpawnPool pool)
        {
            return Pools.TryGetValue(poolName, out pool);
        }

        /// <summary>Despawns every instance of every pool.</summary>
        public static void DespawnAll()
        {
            var pools = new List<SpawnPool>(Pools.Values);
            for (var i = 0; i < pools.Count; i++)
                pools[i]?.DespawnAll();
        }

        /// <summary>Snapshot of every registered pool.</summary>
        public static PoolReport GetReport()
        {
            var report = new PoolReport();
            var pools = new List<SpawnPool>(Pools.Values);

            for (var i = 0; i < pools.Count; i++)
            {
                var pool = pools[i];
                if (pool == null) continue;

                var snapshot = pool.GetSnapshot();
                report.Pools.Add(snapshot);
                report.PrefabCount += snapshot.PrefabCount;
                report.Spawned += snapshot.Spawned;
                report.Despawned += snapshot.Despawned;
                report.Total += snapshot.Total;
                report.Instantiated += snapshot.Instantiated;
                report.Reused += snapshot.Reused;
                report.LimitHits += snapshot.LimitHits;
                report.Culled += snapshot.Culled;
            }

            report.PoolCount = report.Pools.Count;
            report.AverageSpawnMilliseconds = 0d;

            if (report.Pools.Count > 0)
            {
                var sum = 0d;
                for (var i = 0; i < report.Pools.Count; i++)
                    sum += report.Pools[i].AverageSpawnMilliseconds;

                report.AverageSpawnMilliseconds = sum / report.Pools.Count;
            }

            return report;
        }
    }

    /// <summary>
    /// Source-compatible alias for projects migrating from Path-o-logical PoolManager.
    /// Prefer <see cref="SpawnManager"/> in new code.
    /// </summary>
    public static class PoolManager
    {
        public static SpawnPoolsDict Pools => SpawnManager.Pools;
    }

    /// <summary>Dictionary of every registered SpawnPool, keyed by pool name.</summary>
    public class SpawnPoolsDict : IDictionary<string, SpawnPool>
    {
        public delegate void OnCreatedDelegate(SpawnPool pool);

        private readonly Dictionary<string, SpawnPool> _pools = new Dictionary<string, SpawnPool>();
        private readonly Dictionary<string, OnCreatedDelegate> _onCreatedDelegates = new Dictionary<string, OnCreatedDelegate>();

        /// <summary>Raised after a pool registers itself.</summary>
        public event Action<SpawnPool> PoolAdded;

        /// <summary>Raised after a pool unregisters itself.</summary>
        public event Action<SpawnPool> PoolRemoved;

        #region Events

        public void AddOnCreatedDelegate(string poolName, OnCreatedDelegate createdDelegate)
        {
            if (string.IsNullOrEmpty(poolName) || createdDelegate == null) return;

            if (_onCreatedDelegates.TryGetValue(poolName, out var existing))
                _onCreatedDelegates[poolName] = existing + createdDelegate;
            else
                _onCreatedDelegates[poolName] = createdDelegate;
        }

        public void RemoveOnCreatedDelegate(string poolName, OnCreatedDelegate createdDelegate)
        {
            if (string.IsNullOrEmpty(poolName) || createdDelegate == null) return;

            if (!_onCreatedDelegates.TryGetValue(poolName, out var existing))
                throw new KeyNotFoundException($"No OnCreatedDelegates found for pool name '{poolName}'.");

            var updated = existing - createdDelegate;
            if (updated == null)
                _onCreatedDelegates.Remove(poolName);
            else
                _onCreatedDelegates[poolName] = updated;
        }

        #endregion

        #region Creation / destruction

        /// <summary>Creates a new GameObject with a SpawnPool component.</summary>
        public SpawnPool Create(string poolName)
        {
            var owner = new GameObject(poolName + "Pool");
            return owner.AddComponent<SpawnPool>();
        }

        /// <summary>Adds a SpawnPool component to an existing GameObject.</summary>
        public SpawnPool Create(string poolName, GameObject owner)
        {
            if (owner == null)
            {
                Debug.LogError("[SpawnManager] Create(poolName, owner) received a null owner.");
                return null;
            }

            if (!AssertValidPoolName(poolName))
                return null;

            var ownerName = owner.name;
            try
            {
                // SpawnPool.Awake derives its name from the GameObject, so rename temporarily.
                owner.name = poolName;
                return owner.AddComponent<SpawnPool>();
            }
            finally
            {
                owner.name = ownerName;
            }
        }

        /// <summary>Destroys a pool and its GameObject (including every instance).</summary>
        public bool Destroy(string poolName)
        {
            if (!_pools.TryGetValue(poolName, out var pool))
            {
                Debug.LogError($"[SpawnManager] Unable to destroy '{poolName}'. It is not registered.");
                return false;
            }

            Object.Destroy(pool.gameObject);
            _pools.Remove(poolName);
            return true;
        }

        /// <summary>Destroys every pool and its GameObject.</summary>
        public void DestroyAll()
        {
            var pools = new List<SpawnPool>(_pools.Values);
            for (var i = 0; i < pools.Count; i++)
            {
                if (pools[i] != null)
                    Object.Destroy(pools[i].gameObject);
            }

            _pools.Clear();
        }

        private bool AssertValidPoolName(string poolName)
        {
            if (string.IsNullOrEmpty(poolName))
            {
                Debug.LogError("[SpawnManager] Pool name cannot be empty.");
                return false;
            }

            var sanitized = poolName.Replace("Pool", "");
            if (sanitized != poolName)
            {
                Debug.LogWarning($"[SpawnManager] '{poolName}' contains the reserved word 'Pool'. Use '{sanitized}' instead.");
                poolName = sanitized;
            }

            if (ContainsKey(poolName))
            {
                Debug.LogError($"[SpawnManager] A pool named '{poolName}' already exists.");
                return false;
            }

            return true;
        }

        #endregion

        #region Registration

        /// <summary>Used internally by SpawnPool.Awake. Returns false when the name is already taken.</summary>
        internal bool Add(SpawnPool spawnPool)
        {
            if (spawnPool == null) return false;

            if (string.IsNullOrEmpty(spawnPool.poolName))
            {
                Debug.LogError("[SpawnManager] A SpawnPool has an empty pool name and was not registered.");
                return false;
            }

            if (_pools.ContainsKey(spawnPool.poolName))
            {
                Debug.LogError($"[SpawnManager] A pool named '{spawnPool.poolName}' already exists. " +
                               "This usually means the same SpawnPool was added to a scene twice.");
                return false;
            }

            _pools.Add(spawnPool.poolName, spawnPool);

            if (SpawnManager.Settings.logLevel >= PoolLogLevel.Info)
                Debug.Log($"[SpawnManager] Added pool '{spawnPool.poolName}'.");

            if (_onCreatedDelegates.TryGetValue(spawnPool.poolName, out var created))
                created?.Invoke(spawnPool);

            PoolAdded?.Invoke(spawnPool);
            return true;
        }

        /// <summary>Used internally by SpawnPool.OnDestroy.</summary>
        internal bool Remove(SpawnPool spawnPool)
        {
            if (spawnPool == null) return false;
            if (!_pools.TryGetValue(spawnPool.poolName, out var registered) || registered != spawnPool)
                return false;

            _pools.Remove(spawnPool.poolName);
            PoolRemoved?.Invoke(spawnPool);
            return true;
        }

        #endregion

        #region Dictionary members

        public int Count => _pools.Count;

        public bool ContainsKey(string key) => key != null && _pools.ContainsKey(key);

        public bool ContainsValue(SpawnPool pool) => pool != null && _pools.ContainsValue(pool);

        public bool TryGetValue(string key, out SpawnPool value)
        {
            if (key == null)
            {
                value = null;
                return false;
            }

            return _pools.TryGetValue(key, out value);
        }

        public SpawnPool this[string key]
        {
            get
            {
                if (key != null && _pools.TryGetValue(key, out var pool))
                    return pool;

                throw new KeyNotFoundException($"A pool with the name '{key}' was not found. Pools={this}");
            }
            set => throw new NotImplementedException("Pools register themselves. Use SpawnManager.Pools.Create() or add a SpawnPool component.");
        }

        public ICollection<string> Keys => _pools.Keys;

        public ICollection<SpawnPool> Values => _pools.Values;

        public bool IsReadOnly => true;

        public void Add(string key, SpawnPool value) =>
            throw new NotImplementedException("Pools register themselves. Use SpawnManager.Pools.Create() or add a SpawnPool component.");

        public bool Remove(string key) =>
            throw new NotImplementedException("Pools can only be destroyed. Use SpawnManager.Pools.Destroy(poolName).");

        public void Add(KeyValuePair<string, SpawnPool> item) =>
            throw new NotImplementedException("Pools register themselves. Use SpawnManager.Pools.Create() or add a SpawnPool component.");

        public void Clear() =>
            throw new NotImplementedException("Use SpawnManager.Pools.DestroyAll() instead.");

        public bool Contains(KeyValuePair<string, SpawnPool> item) =>
            ((ICollection<KeyValuePair<string, SpawnPool>>)_pools).Contains(item);

        public void CopyTo(KeyValuePair<string, SpawnPool>[] array, int arrayIndex) =>
            ((ICollection<KeyValuePair<string, SpawnPool>>)_pools).CopyTo(array, arrayIndex);

        public bool Remove(KeyValuePair<string, SpawnPool> item) =>
            throw new NotImplementedException("Pools can only be destroyed. Use SpawnManager.Pools.Destroy(poolName).");

        public IEnumerator<KeyValuePair<string, SpawnPool>> GetEnumerator() => _pools.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _pools.GetEnumerator();

        public override string ToString()
        {
            var keys = new string[_pools.Count];
            _pools.Keys.CopyTo(keys, 0);
            return "[" + string.Join(", ", keys) + "]";
        }

        #endregion
    }

    /// <summary>Read-only dictionary of prefab name -> prefab Transform.</summary>
    public class PrefabsDict : IDictionary<string, Transform>
    {
        private readonly Dictionary<string, Transform> _prefabs = new Dictionary<string, Transform>();

        internal void _Add(string prefabName, Transform prefab) => _prefabs.Add(prefabName, prefab);

        internal bool _Remove(string prefabName) => _prefabs.Remove(prefabName);

        internal void _Clear() => _prefabs.Clear();

        public int Count => _prefabs.Count;

        public bool ContainsKey(string prefabName) => prefabName != null && _prefabs.ContainsKey(prefabName);

        public bool TryGetValue(string prefabName, out Transform prefab)
        {
            if (prefabName == null)
            {
                prefab = null;
                return false;
            }

            return _prefabs.TryGetValue(prefabName, out prefab);
        }

        public Transform this[string key]
        {
            get
            {
                if (key != null && _prefabs.TryGetValue(key, out var prefab))
                    return prefab;

                throw new KeyNotFoundException($"A prefab with the name '{key}' was not found. Prefabs={this}");
            }
            set => throw new NotImplementedException("Read-only.");
        }

        public ICollection<string> Keys => _prefabs.Keys;

        public ICollection<Transform> Values => _prefabs.Values;

        public bool IsReadOnly => true;

        public void Add(string key, Transform value) => throw new NotImplementedException("Read-only.");

        public bool Remove(string key) => throw new NotImplementedException("Read-only.");

        public void Add(KeyValuePair<string, Transform> item) => throw new NotImplementedException("Read-only.");

        public bool Remove(KeyValuePair<string, Transform> item) => throw new NotImplementedException("Read-only.");

        public void Clear() => throw new NotImplementedException("Read-only.");

        public bool Contains(KeyValuePair<string, Transform> item) =>
            ((ICollection<KeyValuePair<string, Transform>>)_prefabs).Contains(item);

        public void CopyTo(KeyValuePair<string, Transform>[] array, int arrayIndex) =>
            ((ICollection<KeyValuePair<string, Transform>>)_prefabs).CopyTo(array, arrayIndex);

        public IEnumerator<KeyValuePair<string, Transform>> GetEnumerator() => _prefabs.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _prefabs.GetEnumerator();

        public override string ToString()
        {
            var keys = new string[_prefabs.Count];
            _prefabs.Keys.CopyTo(keys, 0);
            return "[" + string.Join(", ", keys) + "]";
        }
    }

    /// <summary>
    /// Global Instantiate/Destroy interception, kept for compatibility with the original
    /// PoolManager. A SpawnPool can override these per pool with its own delegates.
    /// </summary>
    public static class InstanceHandler
    {
        public delegate GameObject InstantiateDelegate(GameObject prefab, Vector3 pos, Quaternion rot);
        public delegate void DestroyDelegate(GameObject instance);

        public static InstantiateDelegate InstantiateDelegates;
        public static DestroyDelegate DestroyDelegates;

        internal static GameObject InstantiatePrefab(GameObject prefab, Vector3 pos, Quaternion rot)
        {
            if (InstantiateDelegates != null)
                return InstantiateDelegates(prefab, pos, rot);

            return Object.Instantiate(prefab, pos, rot);
        }

        internal static void DestroyInstance(GameObject instance)
        {
            if (instance == null) return;

            if (DestroyDelegates != null)
                DestroyDelegates(instance);
            else
                Object.Destroy(instance);
        }
    }

    /// <summary>Simple read-only wrapper kept for API compatibility.</summary>
    public class ReadOnlyDictionary<TKey, TValue> : IDictionary<TKey, TValue>
    {
        private readonly IDictionary<TKey, TValue> _dictionary;

        public ReadOnlyDictionary(IDictionary<TKey, TValue> dictionary)
        {
            _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        }

        public bool ContainsKey(TKey key) => _dictionary.ContainsKey(key);

        public ICollection<TKey> Keys => _dictionary.Keys;

        public bool TryGetValue(TKey key, out TValue value) => _dictionary.TryGetValue(key, out value);

        public ICollection<TValue> Values => _dictionary.Values;

        public TValue this[TKey key]
        {
            get => _dictionary[key];
            set => throw ReadOnlyException();
        }

        public int Count => _dictionary.Count;

        public bool IsReadOnly => true;

        public bool Contains(KeyValuePair<TKey, TValue> item) => _dictionary.Contains(item);

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) => _dictionary.CopyTo(array, arrayIndex);

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _dictionary.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _dictionary.GetEnumerator();

        void IDictionary<TKey, TValue>.Add(TKey key, TValue value) => throw ReadOnlyException();

        bool IDictionary<TKey, TValue>.Remove(TKey key) => throw ReadOnlyException();

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => throw ReadOnlyException();

        void ICollection<KeyValuePair<TKey, TValue>>.Clear() => throw ReadOnlyException();

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) => throw ReadOnlyException();

        private static Exception ReadOnlyException() => new NotSupportedException("This dictionary is read-only.");
    }
}
