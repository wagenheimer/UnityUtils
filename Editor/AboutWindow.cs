using System.Text;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// "About Unity Utils" window: package info, installed version, tool list, useful links
    /// and a direct update check — all in one place.
    /// </summary>
    internal class AboutWindow : EditorWindow
    {
        const string PackageJsonPath = "Packages/com.wagenheimer.unityutils/package.json";
        const string RepoUrl = "https://github.com/wagenheimer/UnityUtils";
        const string RewiredHelperUrl = "https://github.com/wagenheimer/RewiredHelper";

        static readonly Color AccentColor = new Color(0.24f, 0.48f, 0.95f);

        string _version = "?";
        Vector2 _scroll;
        Texture2D _headerTex;
        bool _stylesBuilt;
        GUIStyle _titleStyle, _subtitleStyle, _sectionStyle;

        [MenuItem("Tools/Wagenheimer/Unity Utils/About Unity Utils...", priority = 140)]
        static void ShowAbout() => GetWindow<AboutWindow>(true, "About Unity Utils", true);

        private void OnEnable()
        {
            var json = System.IO.File.Exists(PackageJsonPath)
                ? System.IO.File.ReadAllText(PackageJsonPath)
                : null;
            var match = System.Text.RegularExpressions.Regex.Match(json ?? "", "\"version\"\\s*:\\s*\"([^\"]+)\"");
            if (match.Success) _version = match.Groups[1].Value;

            minSize = new Vector2(420, 480);
        }

        private void OnGUI()
        {
            BuildStyles();

            // Header
            var headerRect = GUILayoutUtility.GetRect(position.width, 74, GUILayout.ExpandWidth(false));
            if (_headerTex == null)
            {
                _headerTex = new Texture2D(1, 1);
                _headerTex.SetPixel(0, 0, AccentColor);
                _headerTex.Apply();
            }
            GUI.DrawTexture(headerRect, _headerTex, ScaleMode.StretchToFill);
            var headerLabelStyle = new GUIStyle(EditorStyles.whiteLargeLabel) { alignment = TextAnchor.MiddleLeft, fontSize = 20, fontStyle = FontStyle.Bold };
            var headerSubStyle = new GUIStyle(EditorStyles.whiteMiniLabel) { alignment = TextAnchor.MiddleLeft };
            var headerRectInner = new Rect(headerRect.x + 14, headerRect.y + 10, headerRect.width - 28, 54);
            GUI.Label(new Rect(headerRectInner.x, headerRectInner.y, headerRectInner.width, 30), "Unity Utils", headerLabelStyle);
            GUI.Label(new Rect(headerRectInner.x, headerRectInner.y + 32, headerRectInner.width, 18),
                "Small, independent Unity Editor utilities by Wagenheimer", headerSubStyle);

            _scroll = GUILayout.BeginScrollView(_scroll);

            Section("Package");
            EditorGUILayout.LabelField("Version", _version);
            EditorGUILayout.LabelField("Package Name", "com.wagenheimer.unityutils");
            EditorGUILayout.LabelField("Author", "Cezar Wagenheimer");
            EditorGUILayout.LabelField("License", "MIT");

            Section("Included Tools");
            Bullets(sb =>
            {
                sb.AppendLine("• Bootstrap Scene Kit — persistent singletons scene loaded before anything else; playing from the bootstrap scene auto-loads the first Build Settings scene.");
                sb.AppendLine("• Update Checker — automatic update notification (checks every 24h) + manual check under Tools > Wagenheimer > Unity Utils.");
                sb.AppendLine("• Audio Listener Cleaner — removes duplicate AudioListeners across all scenes.");
                sb.AppendLine("• TMP CanvasRenderer Cleaner — removes stray CanvasRenderers next to world-space TextMeshPro.");
                sb.AppendLine("• TMP TextContainer Cleaner — removes obsolete TMP TextContainer components.");
                sb.AppendLine("• Unused IAPButton Cleaner — removes empty Codeless IAPButtons (requires com.unity.purchasing).");
            });

            Section("Versioning");
            Bullets(sb =>
            {
                sb.AppendLine("• Versions are bumped automatically by CI on every push to master.");
                sb.AppendLine("• feat: → minor | fix: (or anything else) → patch | feat!: / BREAKING CHANGE → major.");
                sb.AppendLine("• CHANGELOG.md is regenerated from conventional commits and every release is tagged vX.Y.Z on GitHub.");
            });

            GUILayout.Space(6);
            if (GUILayout.Button("Check for Updates...", GUILayout.Height(26)))
                UpdateChecker.CheckForUpdate(force: true);

            GUILayout.Space(2);
            if (LinkButton("GitHub Repository")) Application.OpenURL(RepoUrl);
            if (LinkButton("Report an Issue")) Application.OpenURL(RepoUrl + "/issues");
            if (LinkButton("Rewired Helper (companion package)")) Application.OpenURL(RewiredHelperUrl);

            GUILayout.EndScrollView();
        }

        private void Section(string title)
        {
            GUILayout.Space(8);
            EditorGUILayout.LabelField(title, _sectionStyle);
        }

        private void Bullets(System.Action<StringBuilder> fill)
        {
            var sb = new StringBuilder();
            fill(sb);
            EditorGUILayout.HelpBox(sb.ToString().TrimEnd(), MessageType.None);
        }

        private bool LinkButton(string label)
        {
            var style = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter };
            return GUILayout.Button(label, style, GUILayout.Height(22));
        }

        private void BuildStyles()
        {
            if (_stylesBuilt) return;
            _stylesBuilt = true;
            _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            _subtitleStyle = new GUIStyle(EditorStyles.miniLabel);
            _sectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, normal = { textColor = AccentColor } };
        }
    }
}
