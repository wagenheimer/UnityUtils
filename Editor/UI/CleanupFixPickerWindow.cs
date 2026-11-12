using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Modal picker used by "Fix All Recommended": every available fix is listed with a checkbox so the user
    /// applies only the ones they want. Returns the indexes of the chosen options.
    /// </summary>
    internal sealed class CleanupFixPickerWindow : EditorWindow
    {
        private string[] _labels;
        private bool[] _checked;
        private Action<List<int>> _onApply;
        private Vector2 _scroll;

        public static void Open(string title, IList<string> labels, Action<List<int>> onApply)
        {
            var window = CreateInstance<CleanupFixPickerWindow>();
            window.titleContent = new GUIContent(title);
            window._labels = new string[labels.Count];
            window._checked = new bool[labels.Count];
            for (var i = 0; i < labels.Count; i++)
            {
                window._labels[i] = labels[i].TrimStart('-', ' ');
                window._checked[i] = true;
            }

            window._onApply = onApply;
            window.minSize = new Vector2(520, 260);
            window.position = new Rect(Screen.currentResolution.width / 2f - 280, Screen.currentResolution.height / 2f - 200, 560, 400);
            window.ShowModalUtility();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Choose the fixes to apply", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Commit your work first. You can cancel while it runs.", EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select all", GUILayout.Width(90))) SetAll(true);
                if (GUILayout.Button("Select none", GUILayout.Width(90))) SetAll(false);
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var wrap = new GUIStyle(EditorStyles.label) { wordWrap = true };
            for (var i = 0; i < _labels.Length; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _checked[i] = EditorGUILayout.Toggle(_checked[i], GUILayout.Width(18));
                    GUILayout.Label(_labels[i], wrap);
                }
            }

            EditorGUILayout.EndScrollView();

            var count = 0;
            foreach (var c in _checked) if (c) count++;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(90)))
                    Close();

                using (new EditorGUI.DisabledScope(count == 0))
                {
                    if (GUILayout.Button($"Apply {count} fix(es)", GUILayout.Width(130)))
                    {
                        var chosen = new List<int>();
                        for (var i = 0; i < _checked.Length; i++)
                            if (_checked[i]) chosen.Add(i);

                        var apply = _onApply;
                        Close();
                        if (apply != null) apply(chosen);
                    }
                }
            }
        }

        private void SetAll(bool value)
        {
            for (var i = 0; i < _checked.Length; i++) _checked[i] = value;
        }
    }
}
