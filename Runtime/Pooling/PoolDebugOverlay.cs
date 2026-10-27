using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.UnityUtils.Pooling
{
    /// <summary>
    /// In-game UI Toolkit overlay with live pool counters and one-click recovery actions.
    /// Auto-created in the Editor and Development Builds (configurable in
    /// <see cref="SpawnManagerSettings"/>), or add it manually to a scene.
    ///
    /// It is intentionally self-contained: it creates its own PanelSettings and loads the packaged
    /// runtime theme, so it renders in player builds without any project setup.
    /// </summary>
    [AddComponentMenu("Wagenheimer/Unity Utils/Pooling/Pool Debug Overlay")]
    [DisallowMultipleComponent]
    public class PoolDebugOverlay : MonoBehaviour
    {
        #region Settings

        [Tooltip("Hotkey used to open/close the overlay.")]
        public KeyCode toggleKey = KeyCode.F8;

        [Tooltip("Use the key configured in SpawnManagerSettings instead of the field above.")]
        public bool useSettingsToggleKey = true;

        [Tooltip("Draw a small floating 'POOLS' button while the overlay is closed.")]
        public bool showFloatingButton = true;

        [Tooltip("Allow the overlay in non-development builds. Keep off for production.")]
        public bool enableInReleaseBuilds = false;

        [Tooltip("Optional custom PanelSettings. When null the packaged settings/theme are used.")]
        public PanelSettings customPanelSettings;

        [Tooltip("Seconds between automatic refreshes while the window is open.")]
        [Min(0.05f)] public float refreshInterval = 0.25f;

        [Header("Scale (mobile-friendly)")]
        [Range(0.75f, 3f)] public float mobileDefaultScale = 1.75f;
        [Range(0.75f, 3f)] public float desktopDefaultScale = 1f;

        #endregion

        #region State

        private const float ZoomMin = 0.75f;
        private const float ZoomMax = 3f;
        private const float ZoomStep = 0.25f;
        private const string ZoomPrefsKey = "PoolDebugOverlay.Zoom";
        private static readonly Vector2Int BaseReferenceResolution = new Vector2Int(1920, 1080);

        private static readonly Color Accent = new Color(0.35f, 0.80f, 0.95f);
        private static readonly Color TextMuted = new Color(0.65f, 0.68f, 0.75f);
        private static readonly Color Surface = new Color(0.07f, 0.08f, 0.11f, 0.97f);
        private static readonly Color SurfaceHeader = new Color(0.11f, 0.12f, 0.16f);
        private static readonly Color Border = new Color(0.22f, 0.23f, 0.28f);
        private static readonly Color SpawnedColor = new Color(0.35f, 0.80f, 0.95f);
        private static readonly Color DespawnedColor = new Color(0.35f, 0.85f, 0.55f);
        private static readonly Color WarningColor = new Color(0.95f, 0.72f, 0.20f);

        private UIDocument _uiDocument;
        private VisualElement _root;
        private VisualElement _window;
        private VisualElement _pill;
        private VisualElement _list;
        private Label _pillCounts;
        private Label _countsLabel;
        private Label _totalsLabel;
        private Label _zoomLabel;
        private TextField _search;

        private bool _isOpen;
        private float _zoom = 1f;
        private float _lastRefreshTime;

        #endregion

        #region Lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            var settings = SpawnManager.Settings;
            if (!settings.enableRuntimeDebugOverlay) return;
            if (!Debug.isDebugBuild && !Application.isEditor && !settings.debugOverlayInReleaseBuilds) return;
            if (FindExisting() != null) return;

            var go = new GameObject("PoolDebugOverlay");
            go.AddComponent<PoolDebugOverlay>();
        }

        private void Awake()
        {
            var settings = SpawnManager.Settings;

            if (!Debug.isDebugBuild && !Application.isEditor && !enableInReleaseBuilds && !settings.debugOverlayInReleaseBuilds)
            {
                Destroy(gameObject);
                return;
            }

            if (useSettingsToggleKey)
                toggleKey = settings.debugOverlayToggleKey;

            DontDestroyOnLoad(gameObject);
            InitializeUI();
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                SetOpen(!_isOpen);

            if (!_isOpen)
            {
                RefreshPillVisibility();
                return;
            }

            if (Time.unscaledTime - _lastRefreshTime < Mathf.Max(0.05f, refreshInterval))
                return;

            _lastRefreshTime = Time.unscaledTime;
            RebuildList();
        }

        #endregion

        #region UI Toolkit setup

        private void InitializeUI()
        {
            _uiDocument = GetComponent<UIDocument>();
            if (_uiDocument == null) _uiDocument = gameObject.AddComponent<UIDocument>();

            EnsurePanelSettings();
            _uiDocument.panelSettings = Instantiate(_uiDocument.panelSettings); // clone: zoom must not mutate the shared asset
            _zoom = LoadZoom();
            ApplyZoom();

            _root = _uiDocument.rootVisualElement;
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;

            BuildPill();
            BuildWindow();
            RefreshCounts();
            SetOpen(false);
        }

        private void EnsurePanelSettings()
        {
            if (_uiDocument.panelSettings != null) return;

            if (customPanelSettings != null)
            {
                _uiDocument.panelSettings = customPanelSettings;
                return;
            }

            var project = Resources.Load<PanelSettings>("Wagenheimer/DebugPanelSettings");
            if (project != null)
            {
                _uiDocument.panelSettings = project;
                return;
            }

            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "PoolDebugPanelSettings";
            panelSettings.sortingOrder = 9997;
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = BaseReferenceResolution;
            panelSettings.match = 0.5f;
            panelSettings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("Wagenheimer/UnityUtilsDebugTheme");

            if (panelSettings.themeStyleSheet == null)
            {
                var themes = Resources.FindObjectsOfTypeAll<ThemeStyleSheet>();
                if (themes != null && themes.Length > 0)
                    panelSettings.themeStyleSheet = themes[0];
            }

            _uiDocument.panelSettings = panelSettings;
        }

        private float LoadZoom()
        {
            var fallback = Application.isMobilePlatform ? mobileDefaultScale : desktopDefaultScale;
            return Mathf.Clamp(PlayerPrefs.GetFloat(ZoomPrefsKey, fallback), ZoomMin, ZoomMax);
        }

        private void SetZoom(float value)
        {
            _zoom = Mathf.Clamp(Mathf.Round(value / ZoomStep) * ZoomStep, ZoomMin, ZoomMax);
            PlayerPrefs.SetFloat(ZoomPrefsKey, _zoom);
            PlayerPrefs.Save();
            ApplyZoom();
        }

        private void ApplyZoom()
        {
            if (_uiDocument.panelSettings != null)
            {
                _uiDocument.panelSettings.referenceResolution = new Vector2Int(
                    Mathf.RoundToInt(BaseReferenceResolution.x / _zoom),
                    Mathf.RoundToInt(BaseReferenceResolution.y / _zoom));
            }

            if (_zoomLabel != null)
                _zoomLabel.text = $"{_zoom:0.##}x";
        }

        #endregion

        #region UI construction

        private void BuildPill()
        {
            _pill = new VisualElement();
            _pill.pickingMode = PickingMode.Position;
            var st = _pill.style;
            st.position = Position.Absolute;
            st.right = 18;
            st.bottom = 18;
            st.height = 34;
            st.flexDirection = FlexDirection.Row;
            st.alignItems = Align.Center;
            st.paddingLeft = st.paddingRight = 12;
            st.backgroundColor = new Color(0.12f, 0.12f, 0.15f, 0.94f);
            SetBorder(st, Accent, 0.8f);
            st.borderTopLeftRadius = st.borderTopRightRadius = st.borderBottomLeftRadius = st.borderBottomRightRadius = 17;

            var dot = new VisualElement();
            dot.style.width = 8;
            dot.style.height = 8;
            dot.style.marginRight = 6;
            dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius = dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = 4;
            dot.style.backgroundColor = Accent;
            _pill.Add(dot);

            var label = new Label("POOLS");
            label.style.color = Color.white;
            label.style.fontSize = 11.5f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            _pill.Add(label);

            _pillCounts = new Label("");
            _pillCounts.style.color = TextMuted;
            _pillCounts.style.fontSize = 10f;
            _pillCounts.style.marginLeft = 6;
            _pill.Add(_pillCounts);

            _pill.RegisterCallback<ClickEvent>(_ => SetOpen(true));
            _root.Add(_pill);
        }

        private void BuildWindow()
        {
            _window = new VisualElement();
            _window.name = "PoolDebugWindow";
            _window.pickingMode = PickingMode.Position;
            var st = _window.style;
            st.position = Position.Absolute;
            st.left = 24;
            st.bottom = 24;
            st.width = 700;
            st.maxWidth = new StyleLength(new Length(96, LengthUnit.Percent));
            st.maxHeight = new StyleLength(new Length(86, LengthUnit.Percent));
            st.height = new StyleLength(new Length(62, LengthUnit.Percent));
            st.backgroundColor = Surface;
            SetBorder(st, Border, 1f);
            st.borderTopLeftRadius = st.borderTopRightRadius = st.borderBottomLeftRadius = st.borderBottomRightRadius = 10;
            st.overflow = Overflow.Hidden;

            _window.Add(BuildHeader());
            _window.Add(BuildToolbar());

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.paddingLeft = scroll.style.paddingRight = 10;
            scroll.style.paddingTop = 8;
            scroll.style.paddingBottom = 10;

            _list = new VisualElement();
            scroll.Add(_list);
            _window.Add(scroll);

            _root.Add(_window);
        }

        private VisualElement BuildHeader()
        {
            var header = new VisualElement();
            var st = header.style;
            st.flexDirection = FlexDirection.Row;
            st.alignItems = Align.Center;
            st.justifyContent = Justify.SpaceBetween;
            st.height = 38;
            st.paddingLeft = 12;
            st.paddingRight = 6;
            st.backgroundColor = SurfaceHeader;
            st.borderBottomWidth = 1;
            st.borderBottomColor = Border;

            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;

            var title = new Label("Pool Manager");
            title.style.fontSize = 13;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = Accent;
            titleRow.Add(title);

            _countsLabel = new Label("");
            _countsLabel.style.fontSize = 10;
            _countsLabel.style.color = TextMuted;
            _countsLabel.style.marginLeft = 8;
            titleRow.Add(_countsLabel);
            header.Add(titleRow);

            var controls = new VisualElement();
            controls.style.flexDirection = FlexDirection.Row;
            controls.style.alignItems = Align.Center;

            controls.Add(MiniButton("Despawn All", () =>
            {
                SpawnManager.DespawnAll();
                RebuildList();
            }, 84));

            controls.Add(MiniButton("Copy", CopyReport, 44));
            controls.Add(MiniButton("A-", () => SetZoom(_zoom - ZoomStep)));
            _zoomLabel = new Label($"{_zoom:0.##}x");
            _zoomLabel.style.fontSize = 10;
            _zoomLabel.style.color = TextMuted;
            _zoomLabel.style.minWidth = 30;
            _zoomLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            controls.Add(_zoomLabel);
            controls.Add(MiniButton("A+", () => SetZoom(_zoom + ZoomStep)));
            controls.Add(MiniButton("X", () => SetOpen(false)));
            header.Add(controls);

            return header;
        }

        private VisualElement BuildToolbar()
        {
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.paddingLeft = 10;
            bar.style.paddingRight = 10;
            bar.style.paddingTop = bar.style.paddingBottom = 6;
            bar.style.backgroundColor = new Color(0.09f, 0.10f, 0.13f);
            bar.style.borderBottomWidth = 1;
            bar.style.borderBottomColor = Border;

            _search = new TextField();
            _search.style.flexGrow = 1;
            _search.style.height = 24;
            _search.style.fontSize = 11;
            _search.RegisterValueChangedCallback(_ => RebuildList());
            bar.Add(_search);

            _totalsLabel = new Label("");
            _totalsLabel.style.fontSize = 10;
            _totalsLabel.style.color = TextMuted;
            _totalsLabel.style.marginLeft = 10;
            bar.Add(_totalsLabel);

            return bar;
        }

        private Button MiniButton(string text, Action onClick, float width = 0f)
        {
            var button = new Button(onClick) { text = text };
            var st = button.style;
            st.height = 24;
            st.minWidth = 24;
            if (width > 0f) st.width = width;
            st.marginLeft = 3;
            st.fontSize = 10.5f;
            st.unityFontStyleAndWeight = FontStyle.Bold;
            st.backgroundColor = new Color(0.20f, 0.22f, 0.27f);
            st.color = Color.white;
            st.borderTopLeftRadius = st.borderTopRightRadius = st.borderBottomLeftRadius = st.borderBottomRightRadius = 5;
            return button;
        }

        private static void SetBorder(IStyle st, Color color, float width)
        {
            st.borderTopWidth = st.borderBottomWidth = st.borderLeftWidth = st.borderRightWidth = width;
            st.borderTopColor = st.borderBottomColor = st.borderLeftColor = st.borderRightColor = color;
        }

        #endregion

        #region Rendering

        private void RefreshCounts()
        {
            if (_pillCounts == null) return;

            var pools = SpawnManager.Pools.Count;
            var spawned = 0;
            foreach (var pool in SpawnManager.Pools.Values)
            {
                if (pool != null)
                    spawned += pool.SpawnedCount;
            }

            _pillCounts.text = pools > 0 ? $"{pools} pool{(pools == 1 ? "" : "s")}  {spawned}" : "";
        }

        private void RebuildList()
        {
            if (_list == null) return;

            RefreshCounts();
            _list.Clear();

            var report = SpawnManager.GetReport();
            _countsLabel.text = $"{report.PoolCount} pool{(report.PoolCount == 1 ? "" : "s")}  " +
                                $"{report.Spawned} spawned  {report.Despawned} despawned";
            _totalsLabel.text = $"total {report.Total}   inst {report.Instantiated}   reuse {report.Reused}   " +
                                $"limit {report.LimitHits}   culled {report.Culled}   avg {report.AverageSpawnMilliseconds:0.000} ms";

            var filter = _search != null ? _search.value : null;
            if (!string.IsNullOrWhiteSpace(filter))
                filter = filter.Trim();

            var shown = 0;
            for (var i = 0; i < report.Pools.Count; i++)
            {
                var pool = report.Pools[i];
                if (!Matches(filter, pool.PoolName) && !AnyPrefabMatches(filter, pool))
                    continue;

                _list.Add(BuildPoolRow(pool, filter));
                shown++;
            }

            if (shown == 0)
            {
                var empty = new Label(report.PoolCount == 0
                    ? "No pools registered yet. Spawn something or add a SpawnPool component."
                    : "No pool matches the current filter.");
                empty.style.color = TextMuted;
                empty.style.fontSize = 11;
                empty.style.paddingTop = empty.style.paddingLeft = 8;
                _list.Add(empty);
            }
        }

        private static bool Matches(string filter, string value)
        {
            return string.IsNullOrEmpty(filter) ||
                   (!string.IsNullOrEmpty(value) && value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool AnyPrefabMatches(string filter, PoolSnapshot pool)
        {
            if (string.IsNullOrEmpty(filter)) return true;

            for (var i = 0; i < pool.Prefabs.Count; i++)
            {
                if (Matches(filter, pool.Prefabs[i].PrefabName))
                    return true;
            }

            return false;
        }

        private VisualElement BuildPoolRow(PoolSnapshot snapshot, string filter)
        {
            var row = new VisualElement();
            row.style.marginBottom = 8;
            row.style.paddingLeft = 8;
            row.style.paddingRight = 8;
            row.style.paddingTop = 6;
            row.style.paddingBottom = 6;
            row.style.backgroundColor = new Color(0.12f, 0.13f, 0.17f);
            row.style.borderTopLeftRadius = row.style.borderTopRightRadius = row.style.borderBottomLeftRadius = row.style.borderBottomRightRadius = 6;

            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;

            var name = new Label(snapshot.PoolName);
            name.style.fontSize = 12;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.color = Color.white;
            name.style.flexGrow = 1;
            head.Add(name);

            var counts = new Label($"spawned {snapshot.Spawned}   despawned {snapshot.Despawned}   total {snapshot.Total}   peak {snapshot.PeakSpawned}");
            counts.style.fontSize = 10;
            counts.style.color = TextMuted;
            head.Add(counts);

            var despawnButton = MiniButton("Despawn", () =>
            {
                var pool = SpawnManager.GetPool(snapshot.PoolName);
                pool?.DespawnAll();
                RebuildList();
            }, 70);
            head.Add(despawnButton);

            var trimButton = MiniButton("Trim", () =>
            {
                var pool = SpawnManager.GetPool(snapshot.PoolName);
                pool?.Trim(0);
                RebuildList();
            }, 44);
            head.Add(trimButton);

            row.Add(head);

            var bar = new VisualElement();
            bar.style.height = 4;
            bar.style.marginTop = 5;
            bar.style.marginBottom = 6;
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.backgroundColor = new Color(0.08f, 0.09f, 0.11f);

            var total = Mathf.Max(1, snapshot.Total);
            var spawnedBar = new VisualElement();
            spawnedBar.style.width = new StyleLength(new Length(100f * snapshot.Spawned / total, LengthUnit.Percent));
            spawnedBar.style.backgroundColor = SpawnedColor;
            bar.Add(spawnedBar);

            var despawnedBar = new VisualElement();
            despawnedBar.style.width = new StyleLength(new Length(100f * snapshot.Despawned / total, LengthUnit.Percent));
            despawnedBar.style.backgroundColor = DespawnedColor;
            bar.Add(despawnedBar);

            row.Add(bar);

            for (var i = 0; i < snapshot.Prefabs.Count; i++)
            {
                var prefab = snapshot.Prefabs[i];
                if (!Matches(filter, snapshot.PoolName) && !Matches(filter, prefab.PrefabName))
                    continue;

                row.Add(BuildPrefabRow(prefab));
            }

            return row;
        }

        private VisualElement BuildPrefabRow(PrefabPoolSnapshot prefab)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 8;
            row.style.paddingTop = row.style.paddingBottom = 2;

            var name = new Label(prefab.PrefabName);
            name.style.fontSize = 10.5f;
            name.style.color = new Color(0.85f, 0.87f, 0.92f);
            name.style.flexGrow = 1;
            name.style.minWidth = 140;
            row.Add(name);

            var counts = new Label($"spawned {prefab.Spawned}  despawned {prefab.Despawned}  total {prefab.Total}  " +
                                   $"inst {prefab.Instantiated}  reuse {prefab.Reused}");
            counts.style.fontSize = 9.5f;
            counts.style.color = TextMuted;
            counts.style.flexGrow = 2;
            row.Add(counts);

            var warnings = new Label(prefab.LimitHits > 0 ? $"limit {prefab.LimitHits}" : prefab.Culled > 0 ? $"culled {prefab.Culled}" : "");
            warnings.style.fontSize = 9.5f;
            warnings.style.color = WarningColor;
            warnings.style.minWidth = 70;
            warnings.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(warnings);

            return row;
        }

        #endregion

        #region Actions

        /// <summary>Opens or closes the overlay.</summary>
        public void SetOpen(bool open)
        {
            _isOpen = open;
            if (_window != null) _window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;

            if (open)
            {
                if (_pill != null) _pill.style.display = DisplayStyle.None;
                _lastRefreshTime = -1f;
                RebuildList();
            }
            else
            {
                RefreshPillVisibility();
            }
        }

        private void RefreshPillVisibility()
        {
            if (_pill == null) return;

            // Do not leave a "POOLS" button on screen in projects that never create a pool.
            var hasPools = SpawnManager.Pools.Count > 0;
            _pill.style.display = showFloatingButton && hasPools ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void CopyReport()
        {
            try
            {
                GUIUtility.systemCopyBuffer = SpawnManager.GetReport().ToText();
            }
            catch
            {
                // Some platforms have no clipboard access; ignore.
            }
        }

        /// <summary>Programmatically creates the overlay if one does not exist.</summary>
        public static PoolDebugOverlay CreateOverlay()
        {
            var existing = FindExisting();
            if (existing != null) return existing;

            var go = new GameObject("PoolDebugOverlay", typeof(PoolDebugOverlay));
            return go.GetComponent<PoolDebugOverlay>();
        }

        private static PoolDebugOverlay FindExisting()
        {
#if UNITY_2023_1_OR_NEWER
            return FindAnyObjectByType<PoolDebugOverlay>();
#else
            return FindObjectOfType<PoolDebugOverlay>();
#endif
        }

        #endregion
    }
}
