using System;
using System.Collections.Generic;
using System.Text;

namespace Wagenheimer.UnityUtils.Pooling
{
    /// <summary>How much the pooling system is allowed to write to the Unity console.</summary>
    public enum PoolLogLevel
    {
        None = 0,
        Errors = 1,
        Warnings = 2,
        Info = 3,
        Verbose = 4
    }

    /// <summary>Order in which despawned instances are reused.</summary>
    public enum PoolReuseMode
    {
        /// <summary>Oldest despawned instance first (the original PoolManager behaviour).</summary>
        Fifo = 0,

        /// <summary>Newest despawned instance first (better cache locality).</summary>
        Lifo = 1
    }

    /// <summary>
    /// Implement this on a pooled prefab to receive spawn/despawn callbacks without the
    /// reflection cost of <see cref="UnityEngine.BroadcastMessage"/>.
    /// </summary>
    public interface IPoolable
    {
        void OnSpawned(SpawnPool pool);
        void OnDespawned(SpawnPool pool);
    }

    /// <summary>Per-prefab counters exposed to the inspector and the runtime overlay.</summary>
    public sealed class PrefabPoolSnapshot
    {
        public string PrefabName;
        public int Spawned;
        public int Despawned;
        public int Total;
        public int PeakSpawned;
        public long Instantiated;
        public long Reused;
        public long LimitHits;
        public long Culled;
        public double AverageSpawnMilliseconds;
    }

    /// <summary>Per-pool counters exposed to the inspector and the runtime overlay.</summary>
    public sealed class PoolSnapshot
    {
        public string PoolName;
        public int PrefabCount;
        public int Spawned;
        public int Despawned;
        public int Total;
        public int PeakSpawned;
        public long Instantiated;
        public long Reused;
        public long LimitHits;
        public long Culled;
        public double AverageSpawnMilliseconds;
        public List<PrefabPoolSnapshot> Prefabs = new List<PrefabPoolSnapshot>();
    }

    /// <summary>Aggregated snapshot of every registered pool.</summary>
    public sealed class PoolReport
    {
        public List<PoolSnapshot> Pools = new List<PoolSnapshot>();
        public int PoolCount;
        public int PrefabCount;
        public int Spawned;
        public int Despawned;
        public int Total;
        public long Instantiated;
        public long Reused;
        public long LimitHits;
        public long Culled;
        public double AverageSpawnMilliseconds;

        /// <summary>Plain-text report, ready for the console or the clipboard.</summary>
        public string ToText()
        {
            var sb = new StringBuilder(512);
            sb.AppendLine("=== Pool Manager Report ===");
            sb.AppendLine($"Pools: {PoolCount}  Prefabs: {PrefabCount}  Spawned: {Spawned}  Despawned: {Despawned}  Total: {Total}");
            sb.AppendLine($"Instantiated: {Instantiated}  Reused: {Reused}  Limit hits: {LimitHits}  Culled: {Culled}  Avg spawn: {AverageSpawnMilliseconds:0.000} ms");
            sb.AppendLine();

            foreach (var pool in Pools)
            {
                sb.AppendLine($"[{pool.PoolName}]  spawned {pool.Spawned}  despawned {pool.Despawned}  total {pool.Total}  peak {pool.PeakSpawned}");
                foreach (var prefab in pool.Prefabs)
                {
                    sb.AppendLine($"  - {prefab.PrefabName}: spawned {prefab.Spawned}, despawned {prefab.Despawned}, " +
                                  $"total {prefab.Total}, peak {prefab.PeakSpawned}, instantiated {prefab.Instantiated}, " +
                                  $"reused {prefab.Reused}, limit hits {prefab.LimitHits}, culled {prefab.Culled}, " +
                                  $"avg {prefab.AverageSpawnMilliseconds:0.000} ms");
                }
            }

            return sb.ToString();
        }
    }
}
