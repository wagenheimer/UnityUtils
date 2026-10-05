using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.UnityUtils
{
    /// <summary>
    /// In-game UI Toolkit debug console that captures everything logged through Unity's debug logging
    /// (<see cref="Debug.Log"/>/<see cref="Debug.LogWarning"/>/<see cref="Debug.LogError"/>/exceptions) and
    /// shows it in a filterable, scrollable, collapsible panel. It is generic and reusable by any project
    /// that references UnityUtils: it auto-initializes in the Editor and Development Builds, ships its own
    /// PanelSettings/theme (so it renders in player builds), and can also be added manually to a scene.
    /// Toggle with the hotkey, the floating pill, or <see cref="SetOpen"/>.
    /// </summary>
    [AddComponentMenu("Wagenheimer/Unity Utils/Debug Console Overlay")]
    [DisallowMultipleComponent]
    public class DebugConsoleOverlay : MonoBehaviour
    {
        #region Settings

        [Tooltip("Hot key to toggle the console in game.")]
        public KeyCode toggleKey = KeyCode.F9;

        [Tooltip("Whether to draw a small floating 'CONSOLE' button on screen.")]
        public bool showFloatingButton = true;

        [Tooltip("Allow the console to run in non-development / release builds. Strongly recommended FALSE for production.")]
        public bool enableInReleaseBuilds = false;

        [Tooltip("Optional custom PanelSettings. If null, a project-wide override or the packaged settings/theme are used.")]
        public PanelSettings customPanelSettings;

        [Tooltip("Maximum number of log entries kept in memory (oldest are dropped).")]
        public int maxEntries = 500;

        [Header("Scale (mobile-friendly)")]
        [Range(1f, 3f)] public float mobileDefaultScale = 1.75f;
        [Range(0.75f, 3f)] public float desktopDefaultScale = 1f;

        [Tooltip("When on, new entries scroll the view to the bottom automatically.")]
        public bool tail = true;

        #endregion

        #region State

        private sealed class LogEntry
        {
            public LogType Level;
            public string Message;
            public string Stack;
            public DateTime Time;
        }

        private const float ZoomMin = 0.75f;
        private const float ZoomMax = 3f;
        private const float ZoomStep = 0.25f;
        private const float RefreshInterval = 0.2f;
        private const string ZoomPrefsKey = "DebugConsoleOverlay.Zoom";
        private static readonly Vector2Int BaseReferenceResolution = new Vector2Int(1920, 1080);

        private readonly List<LogEntry> _entries = new List<LogEntry>();

        private UIDocument _uiDocument;
        private VisualElement _root;
        private VisualElement _window;
        private VisualElement _pill;
        private Label _pillCounts;
        private ScrollView _scroll;
        private VisualElement _list;
        private Label _countsLabel;
        private Label _zoomLabel;

        private bool _isOpen;
        private bool _dirty;
        private float _lastRefreshTime;
        private float _zoom = 1f;
        private bool _showLog = true, _showWarn = true, _showError = true;
        private bool _isDragging;
        private bool _isMaximized;
        private Vector2 _dragStartPointer;
        private Vector2 _dragStartWindowPos;
        private StyleLength _restoreLeft, _restoreTop, _restoreWidth, _restoreHeight, _restoreMaxHeight;

        private static readonly Color Accent = new Color(0.35f, 0.80f, 0.95f);
        private static readonly Color TextMuted = new Color(0.65f, 0.68f, 0.75f);
        private static readonly Color Surface = new Color(0.07f, 0.08f, 0.11f, 0.97f);
        private static readonly Color SurfaceHeader = new Color(0.11f, 0.12f, 0.16f);
        private static readonly Color Border = new Color(0.22f, 0.23f, 0.28f);
        private static readonly Color LogColor = new Color(0.80f, 0.83f, 0.88f);
        private static readonly Color WarnColor = new Color(0.95f, 0.72f, 0.20f);
        private static readonly Color ErrorColor = new Color(0.95f, 0.32f, 0.30f);

        #endregion

        #region Lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (!Debug.isDebugBuild && !Application.isEditor) return;
            if (FindObjectOfType<DebugConsoleOverlay>() == null)
            {
                var go = new GameObject("DebugConsoleOverlay");
                go.AddComponent<DebugConsoleOverlay>();
            }
        }

        private void Awake()
        {
            if (!Debug.isDebugBuild && !Application.isEditor && !enableInReleaseBuilds)
            {
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);
            Application.logMessageReceived += HandleLog;
            InitializeUI();
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= HandleLog;
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey)) SetOpen(!_isOpen);

            if (_isOpen && _dirty && Time.unscaledTime - _lastRefreshTime >= RefreshInterval)
            {
                _lastRefreshTime = Time.unscaledTime;
                RebuildList();
            }
        }

        #endregion

        #region Log Capture

        private void HandleLog(string condition, string stackTrace, LogType type)
        {
            if (_entries.Count >= Mathf.Max(1, maxEntries)) _entries.RemoveAt(0);
            _entries.Add(new LogEntry { Level = type, Message = condition, Stack = stackTrace, Time = DateTime.Now });
            _dirty = true;
            RefreshCounts();
        }

        private bool PassesFilter(LogType level) => level switch
        {
            LogType.Log => _showLog,
            LogType.Warning => _showWarn,
            _ => _showError // Error, Assert, Exception
        };

        private static Color ColorFor(LogType level) => level switch
        {
            LogType.Log => LogColor,
            LogType.Warning => WarnColor,
            _ => ErrorColor
        };

        #endregion

        #region UI Toolkit

        private void InitializeUI()
        {
            _uiDocument = gameObject.GetComponent<UIDocument>();
            if (_uiDocument == null) _uiDocument = gameObject.AddComponent<UIDocument>();

            EnsurePanelSettings();
            _uiDocument.panelSettings = Instantiate(_uiDocument.panelSettings); // clone: zoom never mutates the shared asset
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

            // Optional project-wide override (any project can drop its own under Resources/Wagenheimer/).
            var project = Resources.Load<PanelSettings>("Wagenheimer/DebugPanelSettings");
            if (project != null)
            {
                _uiDocument.panelSettings = project;
                return;
            }

            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.name = "DebugConsolePanelSettings";
            ps.sortingOrder = 9996;
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = BaseReferenceResolution;
            ps.match = 0.5f;
            // Player builds have no ThemeStyleSheet loaded, so a runtime-created panel renders nothing
            // without one; ship a theme under Resources and load it here.
            ps.themeStyleSheet = Resources.Load<ThemeStyleSheet>("Wagenheimer/UnityUtilsDebugTheme");
            if (ps.themeStyleSheet == null)
            {
                var themes = Resources.FindObjectsOfTypeAll<ThemeStyleSheet>();
                if (themes != null && themes.Length > 0) ps.themeStyleSheet = themes[0];
            }
            _uiDocument.panelSettings = ps;
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
            if (_zoomLabel != null) _zoomLabel.text = $"{_zoom:0.##}x";
        }

        private void BuildPill()
        {
            _pill = new VisualElement();
            _pill.pickingMode = PickingMode.Position;
            var st = _pill.style;
            st.position = Position.Absolute;
            st.left = 18;
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

            var label = new Label("CONSOLE");
            label.style.color = Color.white;
            label.style.fontSize = 11.5f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            _pill.Add(label);

            _pillCounts = new Label("");
            _pillCounts.style.color = WarnColor;
            _pillCounts.style.fontSize = 10f;
            _pillCounts.style.marginLeft = 6;
            _pill.Add(_pillCounts);

            _pill.RegisterCallback<PointerUpEvent>(_ => SetOpen(true));
            _root.Add(_pill);
        }

        private void BuildWindow()
        {
            _window = new VisualElement();
            _window.name = "DebugConsoleWindow";
            _window.pickingMode = PickingMode.Position;
            var st = _window.style;
            st.position = Position.Absolute;
            st.left = 24;
            st.top = 30;
            st.width = 620;
            st.maxWidth = new StyleLength(new Length(96, LengthUnit.Percent));
            st.maxHeight = new StyleLength(new Length(86, LengthUnit.Percent));
            st.height = new StyleLength(new Length(60, LengthUnit.Percent));
            st.backgroundColor = Surface;
            SetBorder(st, Border, 1f);
            st.borderTopLeftRadius = st.borderTopRightRadius = st.borderBottomLeftRadius = st.borderBottomRightRadius = 10;
            st.overflow = Overflow.Hidden;

            _window.Add(BuildHeader());
            _window.Add(BuildFilterBar());

            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.style.flexGrow = 1;
            _scroll.style.paddingLeft = _scroll.style.paddingRight = 8;
            _scroll.style.paddingTop = 6;
            _scroll.style.paddingBottom = 8;
            _list = new VisualElement();
            _scroll.Add(_list);
            _window.Add(_scroll);

            _root.Add(_window);
        }

        private VisualElement BuildHeader()
        {
            var header = new VisualElement();
            var hst = header.style;
            hst.flexDirection = FlexDirection.Row;
            hst.alignItems = Align.Center;
            hst.justifyContent = Justify.SpaceBetween;
            hst.height = 36;
            hst.paddingLeft = 12;
            hst.paddingRight = 6;
            hst.backgroundColor = SurfaceHeader;
            hst.borderBottomWidth = 1;
            hst.borderBottomColor = Border;

            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;
            var title = new Label("Debug Console");
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

            var ctrlRow = new VisualElement();
            ctrlRow.style.flexDirection = FlexDirection.Row;
            ctrlRow.style.alignItems = Align.Center;

            var clearBtn = MiniButton("Clear", Clear);
            clearBtn.style.width = 46;
            ctrlRow.Add(clearBtn);

            var copyBtn = MiniButton("Copy", CopyAll);
            copyBtn.style.width = 44;
            ctrlRow.Add(copyBtn);

            ctrlRow.Add(MiniButton("A-", () => SetZoom(_zoom - ZoomStep)));
            _zoomLabel = new Label($"{_zoom:0.##}x");
            _zoomLabel.style.fontSize = 10;
            _zoomLabel.style.color = TextMuted;
            _zoomLabel.style.minWidth = 30;
            _zoomLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            ctrlRow.Add(_zoomLabel);
            ctrlRow.Add(MiniButton("A+", () => SetZoom(_zoom + ZoomStep)));

            var maxBtn = MiniButton("[ ]", ToggleMaximize);
            maxBtn.tooltip = "Maximizar/restaurar.";
            ctrlRow.Add(maxBtn);

            var closeBtn = MiniButton("X", () => SetOpen(false));
            closeBtn.tooltip = "Fechar.";
            ctrlRow.Add(closeBtn);

            header.Add(ctrlRow);

            header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || _isMaximized) return;
                _isDragging = true;
                _dragStartPointer = (Vector2)evt.position;
                _dragStartWindowPos = new Vector2(_window.resolvedStyle.left, _window.resolvedStyle.top);
                header.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            header.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!_isDragging || _isMaximized) return;
                var delta = (Vector2)evt.position - _dragStartPointer;
                _window.style.left = Mathf.Max(0, _dragStartWindowPos.x + delta.x);
                _window.style.top = Mathf.Max(0, _dragStartWindowPos.y + delta.y);
                evt.StopPropagation();
            });
            header.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!_isDragging) return;
                _isDragging = false;
                header.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            });

            return header;
        }

        private VisualElement BuildFilterBar()
        {
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.paddingLeft = 10;
            bar.style.paddingTop = bar.style.paddingBottom = 5;
            bar.style.backgroundColor = new Color(0.09f, 0.10f, 0.13f);
            bar.style.borderBottomWidth = 1;
            bar.style.borderBottomColor = Border;

            bar.Add(FilterChip("Log", LogColor, _showLog, () => { _showLog = !_showLog; RebuildList(); }));
            bar.Add(FilterChip("Warning", WarnColor, _showWarn, () => { _showWarn = !_showWarn; RebuildList(); }));
            bar.Add(FilterChip("Error", ErrorColor, _showError, () => { _showError = !_showError; RebuildList(); }));

            bar.Add(new VisualElement { style = { flexGrow = 1 } });

            var tailToggle = FilterChip("Tail", Accent, tail, () => { tail = !tail; });
            tailToggle.tooltip = "Rolar automaticamente para o fim a cada novo log.";
            bar.Add(tailToggle);

            return bar;
        }

        private static VisualElement FilterChip(string text, Color color, bool active, Action onClick)
        {
            var chip = new Button(onClick);
            chip.text = text;
            chip.style.fontSize = 10.5f;
            chip.style.marginRight = 4;
            chip.style.paddingLeft = chip.style.paddingRight = 8;
            chip.style.paddingTop = chip.style.paddingBottom = 3;
            chip.style.borderTopLeftRadius = chip.style.borderTopRightRadius = chip.style.borderBottomLeftRadius = chip.style.borderBottomRightRadius = 4;
            chip.style.color = active ? color : TextMuted;
            chip.style.backgroundColor = active ? new Color(color.r, color.g, color.b, 0.18f) : new Color(0.16f, 0.17f, 0.21f);
            chip.style.unityFontStyleAndWeight = FontStyle.Bold;
            return chip;
        }

        private Button MiniButton(string text, Action onClick)
        {
            var btn = new Button(onClick);
            btn.text = text;
            var st = btn.style;
            st.height = 24;
            st.minWidth = 24;
            st.marginLeft = 3;
            st.fontSize = 11;
            st.unityFontStyleAndWeight = FontStyle.Bold;
            st.backgroundColor = new Color(0.20f, 0.22f, 0.27f);
            st.color = Color.white;
            st.borderTopLeftRadius = st.borderTopRightRadius = st.borderBottomLeftRadius = st.borderBottomRightRadius = 5;
            return btn;
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
            int log = 0, warn = 0, err = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                switch (_entries[i].Level)
                {
                    case LogType.Log: log++; break;
                    case LogType.Warning: warn++; break;
                    default: err++; break;
                }
            }

            if (_countsLabel != null) _countsLabel.text = $"{log} log  {warn} warn  {err} err";
            if (_pillCounts != null)
            {
                _pillCounts.text = err > 0 ? $"{err}!" : warn > 0 ? $"{warn}" : "";
                _pillCounts.style.color = err > 0 ? ErrorColor : WarnColor;
            }
        }

        private void RebuildList()
        {
            _dirty = false;
            if (_list == null) return;

            _list.Clear();
            VisualElement lastRow = null;
            int shown = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (!PassesFilter(entry.Level)) continue;
                lastRow = BuildRow(entry);
                _list.Add(lastRow);
                shown++;
            }

            if (shown == 0)
            {
                var empty = new Label("(sem entradas para o filtro atual)");
                empty.style.color = TextMuted;
                empty.style.fontSize = 10.5f;
                empty.style.paddingTop = empty.style.paddingLeft = 6;
                _list.Add(empty);
                return;
            }

            if (tail && _scroll != null)
            {
                var row = lastRow;
                _scroll.schedule.Execute(() => { if (row != null) _scroll.ScrollTo(row); });
            }
        }

        private VisualElement BuildRow(LogEntry entry)
        {
            var row = new VisualElement();
            row.pickingMode = PickingMode.Position;
            row.style.flexDirection = FlexDirection.Column;
            row.style.marginBottom = 2;
            row.style.paddingLeft = 6;
            row.style.paddingRight = 4;
            row.style.paddingTop = row.style.paddingBottom = 1;
            row.style.borderLeftWidth = 3;
            row.style.borderLeftColor = ColorFor(entry.Level);

            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.FlexStart;

            var time = new Label(entry.Time.ToString("HH:mm:ss.fff"));
            time.style.fontSize = 9;
            time.style.color = TextMuted;
            time.style.minWidth = 72;
            head.Add(time);

            var msg = new Label(entry.Message);
            msg.style.fontSize = 10.5f;
            msg.style.whiteSpace = WhiteSpace.Normal;
            msg.style.color = ColorFor(entry.Level);
            msg.style.flexGrow = 1;
            head.Add(msg);
            row.Add(head);

            if (!string.IsNullOrEmpty(entry.Stack))
            {
                var stack = new Label(entry.Stack.Trim());
                stack.style.fontSize = 9;
                stack.style.color = TextMuted;
                stack.style.whiteSpace = WhiteSpace.Normal;
                stack.style.marginTop = 2;
                stack.style.display = DisplayStyle.None;
                row.Add(stack);
                row.tooltip = "Clique para ver/ocultar o stack trace.";
                row.RegisterCallback<ClickEvent>(_ =>
                    stack.style.display = stack.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None);
            }

            return row;
        }

        #endregion

        #region Actions

        private void Clear()
        {
            _entries.Clear();
            _dirty = true;
            RefreshCounts();
            RebuildList();
        }

        private void CopyAll()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (!PassesFilter(e.Level)) continue;
                sb.Append('[').Append(e.Time.ToString("HH:mm:ss.fff")).Append("] ").Append(e.Level).Append(": ").AppendLine(e.Message);
            }
            try { GUIUtility.systemCopyBuffer = sb.ToString(); } catch { /* platform without clipboard */ }
        }

        public void SetOpen(bool open)
        {
            _isOpen = open;
            if (_window != null) _window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (_pill != null) _pill.style.display = (showFloatingButton && !open) ? DisplayStyle.Flex : DisplayStyle.None;
            if (open)
            {
                _lastRefreshTime = -1f;
                RebuildList();
            }
        }

        private void ToggleMaximize()
        {
            _isMaximized = !_isMaximized;
            var st = _window.style;
            if (_isMaximized)
            {
                _restoreLeft = st.left; _restoreTop = st.top;
                _restoreWidth = st.width; _restoreHeight = st.height; _restoreMaxHeight = st.maxHeight;
                st.left = 0; st.top = 0; st.right = 0;
                st.width = new StyleLength(new Length(100, LengthUnit.Percent));
                st.height = new StyleLength(new Length(100, LengthUnit.Percent));
                st.maxHeight = new StyleLength(new Length(100, LengthUnit.Percent));
                return;
            }
            st.left = _restoreLeft; st.top = _restoreTop; st.right = StyleKeyword.Auto;
            st.width = _restoreWidth; st.height = _restoreHeight; st.maxHeight = _restoreMaxHeight;
        }

        /// <summary>Programmatically instantiates the console if one does not exist.</summary>
        public static DebugConsoleOverlay CreateOverlay()
        {
            var existing = FindObjectOfType<DebugConsoleOverlay>();
            if (existing != null) return existing;
            var go = new GameObject("DebugConsoleOverlay", typeof(DebugConsoleOverlay));
            return go.GetComponent<DebugConsoleOverlay>();
        }

        #endregion
    }
}
