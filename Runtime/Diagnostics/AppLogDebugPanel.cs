using UnityEngine;

namespace Wagenheimer.UnityUtils
{
    /// <summary>
    /// Small "DEBUG" button (top-left) that opens a panel to send the log to the AppDeployHub Forge and to switch the
    /// automatic upload on errors on or off. Created by <see cref="AppFileLog"/> only when the upload is configured and
    /// allowed (Development Build / Editor, see <see cref="AppLogSettings"/>). Drawn with IMGUI so it needs no prefab,
    /// canvas or input module and works over any scene.
    /// </summary>
    public sealed class AppLogDebugPanel : MonoBehaviour
    {
        private const float ReferenceHeight = 720f;

        private bool _open;
        private GUIStyle _button;
        private GUIStyle _label;
        private GUIStyle _box;

        private void OnGUI()
        {
            float scale = Mathf.Max(0.75f, Screen.height / ReferenceHeight);
            var previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            EnsureStyles();

            // Respect the notch / rounded corners.
            Rect safe = Screen.safeArea;
            float x = safe.x / scale + 8f;
            float y = (Screen.height - safe.yMax) / scale + 8f;

            if (!_open)
            {
                GUI.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
                if (GUI.Button(new Rect(x, y, 86f, 34f), "DEBUG", _button))
                    _open = true;
            }
            else
            {
                DrawPanel(x, y);
            }

            GUI.matrix = previous;
        }

        private void DrawPanel(float x, float y)
        {
            var area = new Rect(x, y, 340f, 214f);
            GUI.Box(area, GUIContent.none, _box);

            GUILayout.BeginArea(new Rect(area.x + 12f, area.y + 10f, area.width - 24f, area.height - 20f));

            GUILayout.Label("Debug log", _label);
            GUILayout.Label(Application.productName + " " + Application.version, _label);

            GUI.enabled = !AppFileLog.IsUploading;
            if (GUILayout.Button(AppFileLog.IsUploading ? "Sending..." : "Send log to Forge", _button, GUILayout.Height(40f)))
                AppFileLog.Upload("manual");
            GUI.enabled = true;

            bool auto = GUILayout.Toggle(AppFileLog.AutoUploadOnError, " Auto-send when an error happens", _label);
            if (auto != AppFileLog.AutoUploadOnError)
                AppFileLog.AutoUploadOnError = auto;

            if (!string.IsNullOrEmpty(AppFileLog.LastUploadStatus))
                GUILayout.Label(AppFileLog.LastUploadStatus, _label);

            if (GUILayout.Button("Close", _button, GUILayout.Height(30f)))
                _open = false;

            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            if (_button != null) return;

            _button = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            _label.normal.textColor = Color.white;

            _box = new GUIStyle(GUI.skin.box);
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.88f));
            tex.Apply();
            _box.normal.background = tex;
        }
    }
}
