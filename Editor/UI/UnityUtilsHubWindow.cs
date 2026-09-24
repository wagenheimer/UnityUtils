using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Wagenheimer.PackageHub.Editor;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Master UI Toolkit Hub window for UnityUtils: aggregates Bootstrap Diagnostics,
    /// Scene Operations, Project Cleanup Suite, Android tools, and Package updates.
    /// </summary>
    public class UnityUtilsHubWindow : EditorWindow
    {
        public enum Tab
        {
            Bootstrap = 0,
            Cleanup = 1,
            AndroidTools = 2,
            About = 3
        }

        private const string PackageJsonPath = "Packages/com.wagenheimer.unityutils/package.json";
        private const string RepoUrl = "https://github.com/wagenheimer/UnityUtils";
        private const string IssuesUrl = "https://github.com/wagenheimer/UnityUtils/issues";

        private string _version = "1.9.0";
        private Tab _currentTab = Tab.Bootstrap;
        private VisualElement _contentContainer;
        private Button[] _tabButtons;

        [MenuItem("Tools/Wagenheimer/Unity Utils/Dashboard...", priority = 100)]
        public static void OpenDashboard()
        {
            Open(Tab.Bootstrap);
        }

        [MenuItem("Tools/Wagenheimer/Unity Utils/Cleanup/Open Project Cleanup...", priority = 140)]
        public static void OpenCleanup()
        {
            Open(Tab.Cleanup);
        }

        public static UnityUtilsHubWindow Open(Tab tab = Tab.Bootstrap)
        {
            var window = GetWindow<UnityUtilsHubWindow>("Unity Utils");
            window.minSize = new Vector2(620, 520);
            window.SwitchTab(tab);
            window.Show();
            return window;
        }

        private void OnEnable()
        {
            LoadPackageVersion();
        }

        private void CreateGUI()
        {
            rootVisualElement.Clear();

            // Load USS
            var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.wagenheimer.unityutils/Editor/UI/UnityUtilsCommon.uss");
            if (stylesheet == null)
            {
                // Fallback for development inside local assets or worktree
                var guids = AssetDatabase.FindAssets("UnityUtilsCommon t:StyleSheet");
                if (guids.Length > 0)
                {
                    stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }

            if (stylesheet != null)
            {
                rootVisualElement.styleSheets.Add(stylesheet);
            }

            var root = new VisualElement();
            root.AddToClassList("root-container");

            // Header Banner
            var header = UnityUtilsUIStyle.CreateHeader(
                "Unity Utils",
                "Bootstrap Scene Kit, Diagnostics & Optimization Suite",
                _version);
            root.Add(header);

            // Tab Bar
            var tabToolbar = new VisualElement();
            tabToolbar.AddToClassList("tab-toolbar");

            var tabNames = new[] { "Bootstrap & Diagnostics", "Project Cleanup", "Android & Tools", "About & Updates" };
            _tabButtons = new Button[tabNames.Length];

            for (var i = 0; i < tabNames.Length; i++)
            {
                var tabIndex = (Tab)i;
                var btn = new Button(() => SwitchTab(tabIndex))
                {
                    text = tabNames[i]
                };
                btn.AddToClassList("tab-button");
                _tabButtons[i] = btn;
                tabToolbar.Add(btn);
            }
            root.Add(tabToolbar);

            // Content Area
            _contentContainer = new VisualElement();
            _contentContainer.AddToClassList("tab-content");
            root.Add(_contentContainer);

            rootVisualElement.Add(root);

            RenderActiveTab();
        }

        public void SwitchTab(Tab tab)
        {
            _currentTab = tab;
            RenderActiveTab();
        }

        private void RenderActiveTab()
        {
            if (_contentContainer == null)
                return;

            _contentContainer.Clear();

            // Update Tab Button active classes
            if (_tabButtons != null)
            {
                for (var i = 0; i < _tabButtons.Length; i++)
                {
                    if (i == (int)_currentTab)
                        _tabButtons[i].AddToClassList("tab-button-active");
                    else
                        _tabButtons[i].RemoveFromClassList("tab-button-active");
                }
            }

            switch (_currentTab)
            {
                case Tab.Bootstrap:
                    _contentContainer.Add(new BootstrapCheckerView());
                    break;
                case Tab.Cleanup:
                    _contentContainer.Add(new CleanupSuiteView());
                    break;
                case Tab.AndroidTools:
                    _contentContainer.Add(BuildAndroidToolsView());
                    break;
                case Tab.About:
                    _contentContainer.Add(BuildAboutView());
                    break;
            }
        }

        private VisualElement BuildAndroidToolsView()
        {
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;

            // 1. Android AAB Size Warning Card
            var aabCard = UnityUtilsUIStyle.CreateCard(
                "Android App Bundle Size Warning",
                "Controls Unity's built-in 200MB threshold warning for Android AAB builds.",
                out var aabBody);

            var aabStatus = AabSizeWarningTool.IsEnabled() ?? true;
            var aabThreshold = AabSizeWarningTool.GetThreshold() ?? 200;

            var aabStatusRow = new VisualElement();
            aabStatusRow.style.flexDirection = FlexDirection.Row;
            aabStatusRow.style.alignItems = Align.Center;
            aabStatusRow.style.marginBottom = 8;

            var aabStatusLabel = new Label($"Status: {(aabStatus ? "ENABLED" : "DISABLED")} (Threshold: {aabThreshold} MB)");
            aabStatusLabel.style.flexGrow = 1;
            var aabBadge = UnityUtilsUIStyle.CreateBadge(aabStatus ? "ENABLED" : "DISABLED", aabStatus ? "badge-pass" : "badge-warning");
            aabStatusRow.Add(aabStatusLabel);
            aabStatusRow.Add(aabBadge);
            aabBody.Add(aabStatusRow);

            var aabToolbar = new VisualElement();
            aabToolbar.AddToClassList("action-toolbar");
            aabToolbar.style.marginTop = 0;
            aabToolbar.style.borderTopWidth = 0;

            aabToolbar.Add(UnityUtilsUIStyle.CreateButton("Enable Warning", "btn-secondary", () =>
            {
                AabSizeWarningTool.Enable();
                RenderActiveTab();
            }));

            aabToolbar.Add(UnityUtilsUIStyle.CreateButton("Disable Warning", "btn-secondary", () =>
            {
                AabSizeWarningTool.Disable();
                RenderActiveTab();
            }));

            aabToolbar.Add(UnityUtilsUIStyle.CreateButton("Toggle", "btn-primary", () =>
            {
                AabSizeWarningTool.Toggle();
                RenderActiveTab();
            }));

            aabBody.Add(aabToolbar);
            scroll.Add(aabCard);

            // 2. SingleAudioListener Card
            var audioCard = UnityUtilsUIStyle.CreateCard(
                "SingleAudioListener Runtime Component",
                "Runtime guard that automatically enforces a single active AudioListener across all scenes.",
                out var audioBody);

            var audioDesc = new Label(
                "Add 'SingleAudioListener' to your main camera or persistent audio GameObject. " +
                "Whenever new scenes are loaded additively, any competing AudioListeners are safely muted or disabled without console spam.");
            audioDesc.AddToClassList("card-subtitle");
            audioDesc.style.whiteSpace = WhiteSpace.Normal;
            audioBody.Add(audioDesc);

            scroll.Add(audioCard);

            // 3. Fast LZF Compression
            var lzfCard = UnityUtilsUIStyle.CreateCard(
                "Fast Compression Utilities (CLZF2)",
                "Optimized C# port of the LZF compression algorithm for save games and network buffers.",
                out var lzfBody);

            var lzfDesc = new Label(
                "CLZF2 provides ultra-fast byte array compression and decompression with zero external native dependencies. " +
                "Use 'CLZF2.Compress(bytes)' and 'CLZF2.Decompress(bytes)' in runtime scripts.");
            lzfDesc.AddToClassList("card-subtitle");
            lzfDesc.style.whiteSpace = WhiteSpace.Normal;
            lzfBody.Add(lzfDesc);

            scroll.Add(lzfCard);

            return scroll;
        }

        private VisualElement BuildAboutView()
        {
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;

            var infoCard = UnityUtilsUIStyle.CreateCard(
                "Package Information",
                "Open-source Unity Editor productivity tools by Cezar Wagenheimer.",
                out var infoBody);

            AddInfoRow(infoBody, "Package Name", "com.wagenheimer.unityutils");
            AddInfoRow(infoBody, "Installed Version", _version);
            AddInfoRow(infoBody, "Author", "Cezar Wagenheimer");
            AddInfoRow(infoBody, "License", "MIT");

            var infoToolbar = new VisualElement();
            infoToolbar.AddToClassList("action-toolbar");
            infoToolbar.style.marginTop = 12;

            infoToolbar.Add(UnityUtilsUIStyle.CreateButton("Check for Updates", "btn-primary", () =>
            {
                PackageHubWindow.OpenToPackage("com.wagenheimer.unityutils");
            }));

            infoToolbar.Add(UnityUtilsUIStyle.CreateButton("GitHub Repository", "btn-secondary", () =>
            {
                Application.OpenURL(RepoUrl);
            }));

            infoToolbar.Add(UnityUtilsUIStyle.CreateButton("Report Issue", "btn-secondary", () =>
            {
                Application.OpenURL(IssuesUrl);
            }));

            infoBody.Add(infoToolbar);
            scroll.Add(infoCard);

            // Key Modules Overview
            var modulesCard = UnityUtilsUIStyle.CreateCard(
                "Included Modules",
                "Overview of integrated toolkits and cleaners.",
                out var modulesBody);

            AddModuleItem(modulesBody, "Bootstrap Scene Kit", "Manages additive persistent singletons, automatic startup scene ordering, and clean play mode transitions.");
            AddModuleItem(modulesBody, "Bootstrap Diagnostic Checker", "Automated verification of build settings, scene paths, settings asset location, and prefab leak detection.");
            AddModuleItem(modulesBody, "Project Cleanup Suite", "Comprehensive scanners and cleaners for AudioListeners, missing scripts, TMP CanvasRenderers, and obsolete components.");
            AddModuleItem(modulesBody, "Android Tools", "Quick inspection and toggling of the AAB 200MB size threshold warning.");

            scroll.Add(modulesCard);

            return scroll;
        }

        private static void AddInfoRow(VisualElement container, string label, string value)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.paddingTop = 3;
            row.style.paddingBottom = 3;

            var labelElem = new Label(label);
            labelElem.style.color = new StyleColor(new Color(0.65f, 0.65f, 0.7f));
            labelElem.style.fontSize = 11;

            var valElem = new Label(value);
            valElem.style.fontSize = 11;
            valElem.style.unityFontStyleAndWeight = FontStyle.Bold;

            row.Add(labelElem);
            row.Add(valElem);
            container.Add(row);
        }

        private static void AddModuleItem(VisualElement container, string title, string description)
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

        private void LoadPackageVersion()
        {
            try
            {
                if (File.Exists(PackageJsonPath))
                {
                    var json = File.ReadAllText(PackageJsonPath);
                    var match = Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"");
                    if (match.Success)
                    {
                        _version = match.Groups[1].Value;
                        return;
                    }
                }
            }
            catch
            {
                // Ignore, use fallback version
            }
            _version = "1.9.0";
        }
    }
}

