using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Dashboard tab for <see cref="MobileAssetOptimizer"/>: scan options, per-item fixes, "Fix All Safe"
    /// (warnings and errors only — Info items change device reach and stay manual) and the best-practice guide.
    /// </summary>
    public class MobileOptimizerView : VisualElement
    {
        private static readonly int[] MaxSizes = { 512, 1024, 2048 };
        private const string MaxSizePref = "Wagenheimer.MobileOptimizer.MaxSize";

        private readonly MobileAssetOptimizer.Options _options = new();
        private DiagnosticReport _report;
        private Label _errorMetric;
        private Label _warnMetric;
        private Label _infoMetric;
        private VisualElement _results;
        private Label _status;
        private Button _fixAllBtn;

        public MobileOptimizerView()
        {
            _options.MaxTextureSize = EditorPrefs.GetInt(MaxSizePref, 1024);
            BuildUI();
        }

        private void BuildUI()
        {
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;

            var metrics = new VisualElement();
            metrics.AddToClassList("metrics-row");
            metrics.Add(UnityUtilsUIStyle.CreateMetricCard("Errors", "-", out _errorMetric));
            metrics.Add(UnityUtilsUIStyle.CreateMetricCard("Warnings", "-", out _warnMetric));
            metrics.Add(UnityUtilsUIStyle.CreateMetricCard("Info", "-", out _infoMetric));
            scroll.Add(metrics);

            var card = UnityUtilsUIStyle.CreateCard("Mobile Asset Optimizer",
                "Checks the textures, audio and Android settings that actually ship in the build (Build Settings scenes + Resources).",
                out var body);
            body.Add(BuildOptionsRow());

            var toolbar = new VisualElement();
            toolbar.AddToClassList("action-toolbar");
            toolbar.style.marginTop = 8;
            toolbar.Add(UnityUtilsUIStyle.CreateButton("Scan Build Assets", "btn-primary", RunScan));
            _fixAllBtn = UnityUtilsUIStyle.CreateButton("Fix All Safe Issues", "btn-success", FixAllSafe);
            _fixAllBtn.SetEnabled(false);
            toolbar.Add(_fixAllBtn);
            body.Add(toolbar);

            _results = new VisualElement();
            _results.style.marginTop = 8;
            body.Add(_results);
            scroll.Add(card);

            scroll.Add(BuildBestPracticesCard());

            _status = new Label("Click 'Scan Build Assets'. Scanning resolves the build dependency graph and can take a few seconds.");
            _status.AddToClassList("status-box");
            scroll.Add(_status);
            Add(scroll);
        }

        private VisualElement BuildOptionsRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Center;

            var android = new Toggle("Android") { value = _options.Android };
            android.RegisterValueChangedCallback(e => _options.Android = e.newValue);
            var ios = new Toggle("iOS") { value = _options.IOS };
            ios.RegisterValueChangedCallback(e => _options.IOS = e.newValue);

            var choices = MaxSizes.Select(s => s.ToString()).ToList();
            var maxSize = new PopupField<string>("Max texture size", choices, _options.MaxTextureSize.ToString());
            maxSize.RegisterValueChangedCallback(e =>
            {
                _options.MaxTextureSize = int.Parse(e.newValue);
                EditorPrefs.SetInt(MaxSizePref, _options.MaxTextureSize);
            });

            foreach (var element in new VisualElement[] { android, ios, maxSize })
            {
                element.style.marginRight = 16;
                row.Add(element);
            }
            return row;
        }

        private void RunScan()
        {
            if (!_options.Android && !_options.IOS)
            {
                _status.text = "Select at least one platform.";
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("Mobile Optimizer", "Resolving build dependencies…", 0.3f);
                _report = MobileAssetOptimizer.Scan(_options);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            Render();
        }

        private void Render()
        {
            _results.Clear();
            _errorMetric.text = _report.ErrorCount.ToString();
            _warnMetric.text = _report.WarningCount.ToString();
            _infoMetric.text = _report.InfoCount.ToString();
            _errorMetric.style.color = _report.ErrorCount > 0 ? new StyleColor(new Color(1f, 0.35f, 0.35f)) : new StyleColor(Color.white);
            _warnMetric.style.color = _report.WarningCount > 0 ? new StyleColor(new Color(1f, 0.7f, 0.2f)) : new StyleColor(Color.white);

            foreach (var item in _report.Items.OrderByDescending(i => i.Severity))
                _results.Add(BuildRow(item));

            _fixAllBtn.SetEnabled(_report.Items.Any(IsSafeFix));
            _status.text = $"Scan completed: {_report.ErrorCount} errors, {_report.WarningCount} warnings, {_report.InfoCount} info.";
        }

        private VisualElement BuildRow(DiagnosticItem item)
        {
            var row = new VisualElement();
            row.AddToClassList("diag-row");

            var info = new VisualElement();
            info.AddToClassList("diag-info");
            var title = new Label($"[{item.Category}] {item.Title}");
            title.AddToClassList("diag-title");
            var desc = new Label(item.Description);
            desc.AddToClassList("diag-desc");
            desc.style.whiteSpace = WhiteSpace.Normal;
            info.Add(title);
            info.Add(desc);

            var actions = new VisualElement();
            actions.AddToClassList("diag-actions");
            actions.Add(UnityUtilsUIStyle.CreateBadge(item.Severity.ToString().ToUpperInvariant(), item.Severity switch
            {
                DiagnosticSeverity.Pass => "badge-pass",
                DiagnosticSeverity.Warning => "badge-warning",
                DiagnosticSeverity.Error => "badge-error",
                _ => "badge-info"
            }));

            if (item.CanFix && item.FixAction != null)
            {
                var fix = UnityUtilsUIStyle.CreateButton("Fix", "btn-secondary", () => RunFixes(new[] { item }));
                fix.style.marginLeft = 8;
                actions.Add(fix);
            }

            row.Add(info);
            row.Add(actions);
            return row;
        }

        private static bool IsSafeFix(DiagnosticItem item) =>
            item.CanFix && item.FixAction != null && item.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error;

        private void FixAllSafe()
        {
            if (_report == null) return;
            var items = _report.Items.Where(IsSafeFix).ToArray();
            if (items.Length == 0) return;
            if (!EditorUtility.DisplayDialog("Fix All Safe Issues",
                    $"Apply {items.Length} fix(es)? Textures and audio are reimported — this can take several minutes on a large project.",
                    "Apply", "Cancel"))
                return;
            RunFixes(items);
        }

        private void RunFixes(DiagnosticItem[] items)
        {
            foreach (var item in items)
            {
                try
                {
                    item.FixAction();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[MobileOptimizer] Failed to fix '{item.Title}': {ex.Message}");
                }
            }
            RunScan();
        }

        private static VisualElement BuildBestPracticesCard()
        {
            var card = UnityUtilsUIStyle.CreateCard("Mobile Best Practices",
                "What the optimizer enforces, and why.", out var body);

            AddPractice(body, "No crunch on mobile",
                "Crunch shrinks the download but is decompressed on the CPU when a scene loads. ASTC without crunch loads much faster.");
            AddPractice(body, "ASTC with a per-platform override",
                "ASTC 6x6 for colour/mask maps, 5x5 for normal maps, 4x4 for UI sprites. Without an override the project default applies (often legacy ETC).");
            AddPractice(body, "Cap texture size (1024 for 3D)",
                "4096 textures from asset-store packs are built for PC. On a phone, 1024 is usually indistinguishable and 16x smaller than 4096.");
            AddPractice(body, "No mipmaps on sprites/UI",
                "Sprites are drawn at screen size; mipmaps only add ~33% memory. Keep mipmaps on 3D textures.");
            AddPractice(body, "Pack UI sprites in Sprite Atlases",
                "Atlases reduce draw calls. Do not atlas single large images (backgrounds) — they waste page area for no batching gain.");
            AddPractice(body, "Audio: Vorbis, stream long clips",
                "Music, ambience and voice (>= 20 s) stream with Load In Background; SFX stay Compressed In Memory at 22 kHz; avoid Decompress On Load above ~1 MB.");
            AddPractice(body, "Android: ASTC + ETC2, not ETC1",
                "With min SDK 24+ every device supports ETC2. ETC1 adds a full extra texture import per build and looks worse.");
            AddPractice(body, "Android: consider arm64 only",
                "ARMv7 doubles IL2CPP compile time and package size for a shrinking share of devices — decide from your Play Console audience.");
            AddPractice(body, "Measure on a device",
                "Editor numbers are indicative. Compare scene load times and memory (Memory Profiler) on a real phone before and after.");
            return card;
        }

        private static void AddPractice(VisualElement container, string title, string description)
        {
            var item = new VisualElement();
            item.style.marginBottom = 8;

            var t = new Label($"• {title}");
            t.style.fontSize = 12;
            t.style.unityFontStyleAndWeight = FontStyle.Bold;

            var d = new Label(description);
            d.style.fontSize = 11;
            d.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.65f));
            d.style.whiteSpace = WhiteSpace.Normal;
            d.style.marginLeft = 10;

            item.Add(t);
            item.Add(d);
            container.Add(item);
        }
    }
}
