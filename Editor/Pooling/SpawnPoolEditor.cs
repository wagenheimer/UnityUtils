using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Wagenheimer.UnityUtils.Pooling;
using Object = UnityEngine.Object;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// UI Toolkit inspector for <see cref="SpawnPool"/>. Shows the serialized settings, a
    /// per-prefab configuration card with validation, and live counters while playing.
    /// </summary>
    [CustomEditor(typeof(SpawnPool))]
    public class SpawnPoolEditor : UnityEditor.Editor
    {
        private static readonly Color Muted = new Color(0.65f, 0.68f, 0.75f);
        private static readonly Color Warning = new Color(0.95f, 0.72f, 0.20f);
        private static readonly Color Error = new Color(0.95f, 0.35f, 0.35f);
        private static readonly Color Success = new Color(0.35f, 0.85f, 0.55f);

        private SpawnPool _pool;
        private VisualElement _root;
        private VisualElement _prefabList;
        private VisualElement _liveContainer;
        private Label _statusLabel;
        private Label _spawnedMetric;
        private Label _despawnedMetric;
        private Label _instantiatedMetric;
        private Label _reusedMetric;

        private readonly List<Foldout> _prefabFoldouts = new List<Foldout>();
        private readonly List<PrefabInstancesView> _instanceViews = new List<PrefabInstancesView>();
        private readonly Dictionary<string, bool> _foldoutStates = new Dictionary<string, bool>();
        private bool _showDespawned;

        private sealed class PrefabInstancesView
        {
            public Transform Prefab;
            public Foldout Foldout;
            public VisualElement Container;
            public int LastSignature = int.MinValue;
        }

        public override VisualElement CreateInspectorGUI()
        {
            _pool = (SpawnPool)target;

            _root = new VisualElement();
            _root.style.paddingLeft = _root.style.paddingRight = 4;
            _root.style.paddingTop = _root.style.paddingBottom = 4;
            AddStyles(_root);

            serializedObject.Update();

            _root.Add(BuildHeader());
            _root.Add(BuildSettingsCard());
            _root.Add(BuildLiveCard());
            _root.Add(BuildPrefabPoolsCard());

            // PropertyFields write to the SerializedObject; persist them as soon as anything changes.
            _root.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                serializedObject.ApplyModifiedProperties();
                RefreshLive();
            });

            _root.schedule.Execute(RefreshLive).Every(250);
            RefreshLive();
            return _root;
        }

        private void OnDisable()
        {
            if (serializedObject != null && serializedObject.targetObject != null)
                serializedObject.ApplyModifiedProperties();
        }

        private static void AddStyles(VisualElement root)
        {
            var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.wagenheimer.unityutils/Editor/UI/UnityUtilsCommon.uss");

            if (stylesheet == null)
            {
                var guids = AssetDatabase.FindAssets("UnityUtilsCommon t:StyleSheet");
                if (guids.Length > 0)
                    stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            if (stylesheet != null)
                root.styleSheets.Add(stylesheet);
        }

        private VisualElement BuildHeader()
        {
            var card = UnityUtilsUIStyle.CreateCard(
                "Spawn Pool",
                "Modern PoolManager replacement. Existing Path-o-logical scenes can be migrated from the Pooling dashboard tab.",
                out var body);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var name = new Label(string.IsNullOrEmpty(_pool.poolName) ? "(derived from GameObject name)" : _pool.poolName);
            name.style.fontSize = 14;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.flexGrow = 1;
            row.Add(name);

            var status = UnityUtilsUIStyle.CreateBadge(
                Application.isPlaying ? "PLAYING" : "EDIT MODE",
                Application.isPlaying ? "badge-pass" : "badge-info");
            row.Add(status);

            var pingPool = UnityUtilsUIStyle.CreateButton("Ping Pool", "btn-secondary", () => Ping(_pool.gameObject));
            pingPool.tooltip = "Ping this pool's GameObject in the Hierarchy.";
            pingPool.style.marginLeft = 6;
            row.Add(pingPool);
            body.Add(row);

            _statusLabel = new Label(Application.isPlaying
                ? "Live counters update every 250 ms."
                : "Enter Play Mode to see live counters and use the spawn/despawn actions.");
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.color = Muted;
            _statusLabel.style.marginTop = 4;
            body.Add(_statusLabel);

            return card;
        }

        private VisualElement BuildSettingsCard()
        {
            var card = UnityUtilsUIStyle.CreateCard("Pool Settings", null, out var body);

            body.Add(Property("poolName"));
            body.Add(Property("matchPoolScale"));
            body.Add(Property("matchPoolLayer"));
            body.Add(Property("dontReparent"));
            body.Add(Property("_dontDestroyOnLoad"));
            body.Add(Property("logMessages"));
            body.Add(Property("sendLegacyMessages"));
            body.Add(Property("despawnUsesUnscaledTime"));
            body.Add(Property("maxParticleDespawnTime"));

            return card;
        }

        private VisualElement BuildLiveCard()
        {
            var card = UnityUtilsUIStyle.CreateCard(
                "Live Counters",
                "Spawned/despawned instances, instantiate vs. reuse and limit hits.",
                out var body);

            var metrics = new VisualElement();
            metrics.AddToClassList("metrics-row");
            metrics.Add(UnityUtilsUIStyle.CreateMetricCard("Spawned", "-", out _spawnedMetric));
            metrics.Add(UnityUtilsUIStyle.CreateMetricCard("Despawned", "-", out _despawnedMetric));
            metrics.Add(UnityUtilsUIStyle.CreateMetricCard("Instantiated", "-", out _instantiatedMetric));
            metrics.Add(UnityUtilsUIStyle.CreateMetricCard("Reused", "-", out _reusedMetric));
            body.Add(metrics);

            _liveContainer = new VisualElement();
            body.Add(_liveContainer);

            return card;
        }

        private VisualElement BuildPrefabPoolsCard()
        {
            var card = UnityUtilsUIStyle.CreateCard(
                "Per-Prefab Pools",
                "Prefabs not listed here are created automatically the first time Spawn() is called.",
                out var body);

            var toolbar = new VisualElement();
            toolbar.AddToClassList("action-toolbar");
            toolbar.style.marginTop = 0;
            toolbar.style.borderTopWidth = 0;
            toolbar.style.flexWrap = Wrap.Wrap;

            toolbar.Add(UnityUtilsUIStyle.CreateButton("Add Prefab Pool", "btn-primary", AddPrefabPool));
            toolbar.Add(UnityUtilsUIStyle.CreateButton("Expand All", "btn-secondary", () => SetAllFoldouts(true)));
            toolbar.Add(UnityUtilsUIStyle.CreateButton("Collapse All", "btn-secondary", () => SetAllFoldouts(false)));
            toolbar.Add(UnityUtilsUIStyle.CreateButton("Refresh Instances", "btn-secondary", () => RefreshInstanceViews(true)));
            toolbar.Add(UnityUtilsUIStyle.CreateButton("Validate", "btn-secondary", () =>
            {
                serializedObject.Update();
                RefreshPrefabList();
            }));

            var showDespawned = new Toggle("Show despawned") { value = _showDespawned };
            showDespawned.style.marginLeft = 10;
            showDespawned.style.marginRight = 4;
            showDespawned.RegisterValueChangedCallback(e =>
            {
                _showDespawned = e.newValue;
                RefreshInstanceViews(true);
            });
            toolbar.Add(showDespawned);

            body.Add(toolbar);

            _prefabList = new VisualElement();
            body.Add(_prefabList);

            RefreshPrefabList();
            return card;
        }

        private void RefreshPrefabList()
        {
            if (_prefabList == null) return;

            _prefabList.Clear();
            _prefabFoldouts.Clear();
            _instanceViews.Clear();

            var listProperty = serializedObject.FindProperty(nameof(SpawnPool._perPrefabPoolOptions));
            if (listProperty == null || listProperty.arraySize == 0)
            {
                var empty = new Label("No prefab pools configured. Spawn() will create them on demand.");
                empty.style.fontSize = 10;
                empty.style.color = Muted;
                empty.style.paddingTop = empty.style.paddingBottom = 6;
                _prefabList.Add(empty);
                return;
            }

            var live = GetLivePrefabMap();

            for (var i = 0; i < listProperty.arraySize; i++)
                _prefabList.Add(BuildPrefabPool(listProperty.GetArrayElementAtIndex(i), i, live));

            RefreshInstanceViews(true);
        }

        private VisualElement BuildPrefabPool(SerializedProperty element, int index, Dictionary<string, PrefabPoolSnapshot> live)
        {
            var prefabProperty = element.FindPropertyRelative(nameof(PrefabPool.prefab));
            var prefab = prefabProperty.objectReferenceValue as Transform;
            var prefabName = prefab != null ? prefab.name : $"Prefab Pool {index + 1}";

            live.TryGetValue(prefabName, out var snapshot);

            var container = new VisualElement();
            container.style.marginTop = 6;
            container.style.paddingTop = 6;
            container.style.borderTopWidth = 1;
            container.style.borderTopColor = new Color(0.24f, 0.25f, 0.30f);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;

            var foldoutKey = prefab != null ? "p" + prefab.GetHashCode() : "i" + index;
            var expanded = !_foldoutStates.TryGetValue(foldoutKey, out var storedExpanded) || storedExpanded;

            var foldout = new Foldout { text = prefabName, value = expanded };
            foldout.style.flexGrow = 1;
            foldout.style.fontSize = 11;
            foldout.RegisterValueChangedCallback(e =>
            {
                _foldoutStates[foldoutKey] = e.newValue;
                if (e.newValue)
                    RefreshInstanceViews(true);
            });
            _prefabFoldouts.Add(foldout);
            header.Add(foldout);

            if (snapshot != null)
            {
                var badge = UnityUtilsUIStyle.CreateBadge(
                    $"spawned {snapshot.Spawned} / {snapshot.Total}",
                    snapshot.LimitHits > 0 ? "badge-warning" : "badge-pass");
                badge.style.marginRight = 6;
                header.Add(badge);
            }

            if (prefab != null)
            {
                var pingPrefab = UnityUtilsUIStyle.CreateButton("Ping Prefab", "btn-secondary", () => Ping(prefab.gameObject));
                pingPrefab.tooltip = "Ping the prefab asset in the Project window.";
                pingPrefab.style.marginLeft = 4;
                header.Add(pingPrefab);
            }

            var remove = UnityUtilsUIStyle.CreateButton("Remove", "btn-danger", () => RemovePrefabPool(index));
            remove.style.marginLeft = 4;
            header.Add(remove);

            container.Add(header);

            var content = new VisualElement();
            content.style.paddingLeft = 12;
            content.style.paddingTop = 4;
            foldout.Add(content);

            content.Add(Property(prefabProperty));
            content.Add(PropertyFromParent(element, nameof(PrefabPool.preloadAmount)));
            content.Add(PropertyFromParent(element, nameof(PrefabPool.preloadTime)));
            content.Add(PropertyFromParent(element, nameof(PrefabPool.preloadFrames)));
            content.Add(PropertyFromParent(element, nameof(PrefabPool.preloadDelay)));
            content.Add(PropertyFromParent(element, nameof(PrefabPool.limitInstances)));
            content.Add(PropertyFromParent(element, nameof(PrefabPool.limitAmount)));
            content.Add(PropertyFromParent(element, nameof(PrefabPool.limitFIFO)));
            content.Add(PropertyFromParent(element, nameof(PrefabPool.reuseMode)));
            content.Add(PropertyFromParent(element, nameof(PrefabPool.cullDespawned)));
            content.Add(PropertyFromParent(element, nameof(PrefabPool.cullAbove)));
            content.Add(PropertyFromParent(element, nameof(PrefabPool.cullDelay)));
            content.Add(PropertyFromParent(element, nameof(PrefabPool.cullMaxPerPass)));
            content.Add(PropertyFromParent(element, nameof(PrefabPool._logMessages)));

            var warnings = ValidatePrefabPool(element);
            if (warnings.Count > 0)
            {
                var box = new VisualElement();
                box.style.marginTop = 4;
                box.style.paddingLeft = 6;
                box.style.paddingTop = 4;
                box.style.paddingBottom = 4;
                box.style.borderLeftWidth = 3;
                box.style.borderLeftColor = Warning;
                box.style.backgroundColor = new Color(0.95f, 0.72f, 0.20f, 0.08f);

                for (var i = 0; i < warnings.Count; i++)
                {
                    var warning = new Label(warnings[i]);
                    warning.style.fontSize = 10;
                    warning.style.color = Warning;
                    warning.style.whiteSpace = WhiteSpace.Normal;
                    box.Add(warning);
                }

                content.Add(box);
            }

            if (snapshot != null)
            {
                var stats = new Label(
                    $"total {snapshot.Total}  spawned {snapshot.Spawned}  despawned {snapshot.Despawned}  " +
                    $"peak {snapshot.PeakSpawned}  instantiated {snapshot.Instantiated}  reused {snapshot.Reused}  " +
                    $"limit hits {snapshot.LimitHits}  culled {snapshot.Culled}  avg {snapshot.AverageSpawnMilliseconds:0.000} ms");
                stats.style.fontSize = 10;
                stats.style.color = Muted;
                stats.style.whiteSpace = WhiteSpace.Normal;
                stats.style.marginTop = 4;
                content.Add(stats);
            }

            if (Application.isPlaying && prefab != null)
            {
                var actions = new VisualElement();
                actions.AddToClassList("action-toolbar");
                actions.style.marginTop = 4;

                actions.Add(UnityUtilsUIStyle.CreateButton("Spawn 1", "btn-secondary", () =>
                {
                    _pool.Spawn(prefab);
                    RefreshLive();
                }));

                actions.Add(UnityUtilsUIStyle.CreateButton("Despawn All", "btn-secondary", () =>
                {
                    var prefabPool = _pool.GetPrefabPool(prefab);
                    if (prefabPool == null) return;

                    var spawned = prefabPool.spawned;
                    for (var i = 0; i < spawned.Count; i++)
                        _pool.Despawn(spawned[i]);

                    RefreshLive();
                }));

                actions.Add(UnityUtilsUIStyle.CreateButton("Trim Despawned", "btn-secondary", () =>
                {
                    _pool.GetPrefabPool(prefab)?.TrimDespawned(0);
                    RefreshLive();
                }));

                content.Add(actions);
            }

            var instancesContainer = new VisualElement();
            instancesContainer.style.marginTop = 4;
            content.Add(instancesContainer);

            _instanceViews.Add(new PrefabInstancesView
            {
                Prefab = prefab,
                Foldout = foldout,
                Container = instancesContainer
            });

            return container;
        }

        private void SetAllFoldouts(bool expanded)
        {
            for (var i = 0; i < _prefabFoldouts.Count; i++)
            {
                if (_prefabFoldouts[i] != null)
                    _prefabFoldouts[i].value = expanded;
            }

            RefreshInstanceViews(true);
        }

        private void RefreshInstanceViews(bool force = false)
        {
            if (_instanceViews.Count == 0) return;

            for (var i = 0; i < _instanceViews.Count; i++)
            {
                var view = _instanceViews[i];
                if (view?.Container == null) continue;

                if (!Application.isPlaying)
                {
                    if (!force && view.LastSignature == -1) continue;

                    view.Container.Clear();
                    view.Container.Add(MutedLabel("Enter Play Mode to list instances."));
                    view.LastSignature = -1;
                    continue;
                }

                if (view.Foldout != null && !view.Foldout.value && !force)
                    continue;

                var prefabPool = view.Prefab != null ? _pool.GetPrefabPool(view.Prefab) : null;
                if (prefabPool == null)
                {
                    if (!force && view.LastSignature == 0) continue;

                    view.Container.Clear();
                    view.Container.Add(MutedLabel("Not instantiated yet."));
                    view.LastSignature = 0;
                    continue;
                }

                var spawned = prefabPool.spawned;
                var despawned = _showDespawned ? prefabPool.despawned : null;
                var signature = ComputeSignature(spawned, despawned);
                if (!force && signature == view.LastSignature) continue;

                view.LastSignature = signature;
                view.Container.Clear();

                AddInstanceSection(view.Container, "Spawned", spawned, true);
                if (_showDespawned)
                    AddInstanceSection(view.Container, "Despawned", despawned, false);
            }
        }

        private void AddInstanceSection(VisualElement container, string title, List<Transform> instances, bool spawned)
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginTop = 4;

            var label = new Label($"{title} ({instances?.Count ?? 0})");
            label.style.fontSize = 10;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = spawned ? new Color(0.55f, 0.85f, 1f) : Muted;
            header.Add(label);
            container.Add(header);

            if (instances == null || instances.Count == 0)
            {
                container.Add(MutedLabel("  None."));
                return;
            }

            // Keep the inspector responsive even with a huge pool.
            var max = Mathf.Min(instances.Count, 100);
            for (var i = 0; i < max; i++)
            {
                var instance = instances[i];
                if (instance == null) continue;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.paddingLeft = 8;

                var name = new Label(instance.name);
                name.style.fontSize = 10;
                name.style.color = spawned ? Color.white : Muted;
                name.style.flexGrow = 1;
                name.style.whiteSpace = WhiteSpace.Normal;
                row.Add(name);

                var ping = UnityUtilsUIStyle.CreateButton("Ping", "btn-secondary", () => Ping(instance.gameObject));
                ping.tooltip = spawned
                    ? "Ping this active instance in the Hierarchy."
                    : "Ping this despawned instance in the Hierarchy.";
                ping.style.marginLeft = 4;
                ping.style.height = 20;
                row.Add(ping);

                container.Add(row);
            }

            if (instances.Count > max)
                container.Add(MutedLabel($"  ... and {instances.Count - max} more"));
        }

        private static int ComputeSignature(List<Transform> spawned, List<Transform> despawned)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + (spawned?.Count ?? 0);

                if (spawned != null)
                {
                    for (var i = 0; i < spawned.Count; i++)
                    {
                        var instance = spawned[i];
                        hash = hash * 31 + (instance != null ? instance.GetHashCode() : 0);
                        hash = hash * 31 + (instance != null ? instance.name.GetHashCode() : 0);
                    }
                }

                hash = hash * 31 + (despawned?.Count ?? 0);

                if (despawned != null)
                {
                    for (var i = 0; i < despawned.Count; i++)
                    {
                        var instance = despawned[i];
                        hash = hash * 31 + (instance != null ? instance.GetHashCode() : 0);
                        hash = hash * 31 + (instance != null ? instance.name.GetHashCode() : 0);
                    }
                }

                return hash;
            }
        }

        private static void Ping(Object obj)
        {
            if (obj == null) return;
            EditorGUIUtility.PingObject(obj);
        }

        private static Label MutedLabel(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 10;
            label.style.color = Muted;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private void AddPrefabPool()
        {
            serializedObject.Update();

            var list = serializedObject.FindProperty(nameof(SpawnPool._perPrefabPoolOptions));
            list.arraySize++;

            var element = list.GetArrayElementAtIndex(list.arraySize - 1);
            element.FindPropertyRelative(nameof(PrefabPool.prefab)).objectReferenceValue = null;
            element.FindPropertyRelative(nameof(PrefabPool.preloadAmount)).intValue = 1;
            element.FindPropertyRelative(nameof(PrefabPool.preloadTime)).boolValue = false;
            element.FindPropertyRelative(nameof(PrefabPool.preloadFrames)).intValue = 2;
            element.FindPropertyRelative(nameof(PrefabPool.preloadDelay)).floatValue = 0f;
            element.FindPropertyRelative(nameof(PrefabPool.limitInstances)).boolValue = false;
            element.FindPropertyRelative(nameof(PrefabPool.limitAmount)).intValue = 100;
            element.FindPropertyRelative(nameof(PrefabPool.limitFIFO)).boolValue = false;
            element.FindPropertyRelative(nameof(PrefabPool.reuseMode)).enumValueIndex = 0;
            element.FindPropertyRelative(nameof(PrefabPool.cullDespawned)).boolValue = false;
            element.FindPropertyRelative(nameof(PrefabPool.cullAbove)).intValue = 50;
            element.FindPropertyRelative(nameof(PrefabPool.cullDelay)).intValue = 60;
            element.FindPropertyRelative(nameof(PrefabPool.cullMaxPerPass)).intValue = 5;
            element.FindPropertyRelative(nameof(PrefabPool._logMessages)).boolValue = false;

            serializedObject.ApplyModifiedProperties();
            RefreshPrefabList();
        }

        private void RemovePrefabPool(int index)
        {
            serializedObject.Update();

            var list = serializedObject.FindProperty(nameof(SpawnPool._perPrefabPoolOptions));
            if (index < 0 || index >= list.arraySize) return;

            list.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            RefreshPrefabList();
        }

        private static List<string> ValidatePrefabPool(SerializedProperty element)
        {
            var warnings = new List<string>();

            var prefab = element.FindPropertyRelative(nameof(PrefabPool.prefab)).objectReferenceValue as Transform;
            if (prefab == null)
            {
                warnings.Add("No prefab assigned; this entry will be skipped.");
                return warnings;
            }

            var preload = element.FindPropertyRelative(nameof(PrefabPool.preloadAmount)).intValue;
            var limitOn = element.FindPropertyRelative(nameof(PrefabPool.limitInstances)).boolValue;
            var limit = element.FindPropertyRelative(nameof(PrefabPool.limitAmount)).intValue;
            var cullOn = element.FindPropertyRelative(nameof(PrefabPool.cullDespawned)).boolValue;
            var cullAbove = element.FindPropertyRelative(nameof(PrefabPool.cullAbove)).intValue;
            var preloadTime = element.FindPropertyRelative(nameof(PrefabPool.preloadTime)).boolValue;
            var preloadFrames = element.FindPropertyRelative(nameof(PrefabPool.preloadFrames)).intValue;

            if (limitOn && limit <= 0)
                warnings.Add("Limit Amount must be greater than zero.");

            if (limitOn && preload > limit)
                warnings.Add("Preload Amount is greater than Limit Amount; it will be clamped at runtime.");

            if (cullOn && preload >= cullAbove)
                warnings.Add("Cull Above should be greater than Preload Amount, otherwise culling triggers immediately.");

            if (preloadTime && preloadFrames > preload)
                warnings.Add("Preload Frames is greater than Preload Amount; it will be clamped at runtime.");

            return warnings;
        }

        private Dictionary<string, PrefabPoolSnapshot> GetLivePrefabMap()
        {
            var map = new Dictionary<string, PrefabPoolSnapshot>(StringComparer.Ordinal);
            if (!Application.isPlaying || _pool == null) return map;

            var snapshot = _pool.GetSnapshot();
            for (var i = 0; i < snapshot.Prefabs.Count; i++)
                map[snapshot.Prefabs[i].PrefabName] = snapshot.Prefabs[i];

            return map;
        }

        private void RefreshLive()
        {
            if (_pool == null) return;

            if (!Application.isPlaying)
            {
                _spawnedMetric.text = "-";
                _despawnedMetric.text = "-";
                _instantiatedMetric.text = "-";
                _reusedMetric.text = "-";
                _liveContainer?.Clear();
                RefreshInstanceViews();
                return;
            }

            var snapshot = _pool.GetSnapshot();
            _spawnedMetric.text = snapshot.Spawned.ToString();
            _despawnedMetric.text = snapshot.Despawned.ToString();
            _instantiatedMetric.text = snapshot.Instantiated.ToString();
            _reusedMetric.text = snapshot.Reused.ToString();

            if (_liveContainer == null) return;
            _liveContainer.Clear();

            var summary = new Label(
                $"total {snapshot.Total}   peak {snapshot.PeakSpawned}   limit hits {snapshot.LimitHits}   " +
                $"culled {snapshot.Culled}   avg spawn {snapshot.AverageSpawnMilliseconds:0.000} ms");
            summary.style.fontSize = 10;
            summary.style.color = Muted;
            summary.style.whiteSpace = WhiteSpace.Normal;
            _liveContainer.Add(summary);

            var actions = new VisualElement();
            actions.AddToClassList("action-toolbar");
            actions.style.marginTop = 4;

            actions.Add(UnityUtilsUIStyle.CreateButton("Despawn All", "btn-secondary", () =>
            {
                _pool.DespawnAll();
                RefreshLive();
            }));

            actions.Add(UnityUtilsUIStyle.CreateButton("Trim Despawned", "btn-secondary", () =>
            {
                _pool.Trim(0);
                RefreshLive();
            }));

            _liveContainer.Add(actions);
            RefreshInstanceViews();
        }

        private VisualElement Property(string propertyName)
        {
            return Property(serializedObject.FindProperty(propertyName), propertyName);
        }

        private VisualElement PropertyFromParent(SerializedProperty parent, string relativeName)
        {
            return Property(parent?.FindPropertyRelative(relativeName), relativeName);
        }

        private VisualElement Property(SerializedProperty property, string label = null)
        {
            if (property == null)
                return new Label($"Missing property: {label}");

            var field = new PropertyField(property)
            {
                bindingPath = property.propertyPath
            };
            field.Bind(serializedObject);
            return field;
        }
    }
}

