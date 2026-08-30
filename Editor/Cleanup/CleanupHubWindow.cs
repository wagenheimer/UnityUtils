// Unity Editor window that aggregates all cleanup tools.
// Path: k:/Games/Open Source/UnityUtils/Editor/Cleanup/CleanupHubWindow.cs

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Wagenheimer.UnityUtils.Editor;

namespace Wagenheimer.UnityUtils.Editor
{
    public class CleanupHubWindow : EditorWindow
    {
        private Vector2 _scrollPos;
        private string _statusMessage = string.Empty;

        [MenuItem("Tools/Wagenheimer/Unity Utils/Project Cleanup Hub...", priority = 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<CleanupHubWindow>(true, "Project Cleanup Hub");
            window.minSize = new Vector2(500, 400);
        }

        private void OnGUI()
        {
            GUILayout.Label("Project Cleanup Hub", EditorStyles.boldLabel);
            GUILayout.Space(8);
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawSection("Audio Listeners", DrawAudioListenerSection);
            DrawSection("Missing Scripts", DrawMissingScriptsSection);
            DrawSection("Legacy Components", DrawLegacyComponentsSection);
            DrawSection("Animator Transitions", DrawAnimatorTransitionsSection);
            DrawSection("TMP CanvasRenderer", DrawTMPCanvasRendererSection);
            DrawSection("TMP TextContainer", DrawTMPTextContainerSection);
            DrawSection("Unused IAP Buttons", DrawUnusedIAPButtonSection);

            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
            }
        }

        private void DrawSection(string title, System.Action drawContent)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            drawContent?.Invoke();
            EditorGUILayout.EndVertical();
            GUILayout.Space(6);
        }

        #region Audio Listeners
        private void DrawAudioListenerSection()
        {
            if (GUILayout.Button("Scan Project"))
            {
                var result = AudioListenerCleaner.ScanProject();
                _statusMessage = $"AudioListeners – Duplicates: {result.DuplicateCount}, Scenes: {result.AffectedScenesCount}, Prefabs: {result.AffectedPrefabsCount}";
                Debug.Log($"[CleanupHub] {_statusMessage}");
            }
            if (GUILayout.Button("Fix All (Scenes && Prefabs)"))
            {
                var sceneRes = AudioListenerCleaner.CleanAllScenes(removeAllNonPersistent: false);
                var prefabRes = AudioListenerCleaner.CleanAllPrefabs(removeAllNonPersistent: false);
                var total = sceneRes.DuplicatesRemoved + prefabRes.DuplicatesRemoved;
                _statusMessage = $"Removed {total} duplicate AudioListener(s) – {sceneRes.ScenesModified} scene(s), {prefabRes.PrefabsModified} prefab(s) updated.";
                Debug.Log($"[CleanupHub] {_statusMessage}");
            }
            if (GUILayout.Button("Remove All Non‑Bootstrap Listeners"))
            {
                AudioListenerCleaner.RemoveAllNonBootstrapListeners();
                _statusMessage = "Removed all non‑bootstrap AudioListeners.";
            }
        }
        #endregion

        #region Missing Scripts
        private void DrawMissingScriptsSection()
        {
            if (GUILayout.Button("Scan Project"))
            {
                var scan = MissingScriptCleaner.ScanProject();
                _statusMessage = $"Missing Scripts – Count: {scan.MissingScriptCount}, Scenes: {scan.AffectedScenesCount}, Prefabs: {scan.AffectedPrefabsCount}";
                Debug.Log($"[CleanupHub] {_statusMessage}");
            }
            if (GUILayout.Button("Fix All (Scenes && Prefabs)"))
            {
                var sceneRes = MissingScriptCleaner.CleanAllScenes();
                var prefabRes = MissingScriptCleaner.CleanAllPrefabs();
                var total = sceneRes.MissingScriptsRemoved + prefabRes.MissingScriptsRemoved;
                _statusMessage = $"Removed {total} missing script(s) – {sceneRes.ScenesModified} scene(s), {prefabRes.PrefabsModified} prefab(s) updated.";
                Debug.Log($"[CleanupHub] {_statusMessage}");
            }
        }
        #endregion

        #region Legacy Components
        private void DrawLegacyComponentsSection()
        {
            if (GUILayout.Button("Resave All Scenes && Prefabs (Full Modernize)"))
            {
                LegacyComponentCleaner.ResaveAllScenesAndPrefabs();
                _statusMessage = "Project modernized – scenes, prefabs resaved and assets reserialized.";
            }
        }
        #endregion

        #region Animator Transitions
        private void DrawAnimatorTransitionsSection()
        {
            if (GUILayout.Button("Audit Invalid Transitions"))
            {
                AnimatorTransitionCleaner.AuditInvalidTransitions();
                _statusMessage = "Audit completed – see Console for details.";
            }
            if (GUILayout.Button("Fix – Enable Exit Time"))
            {
                AnimatorTransitionCleaner.FixInvalidTransitionsEnableExitTime();
                _statusMessage = "Enabled Exit Time on invalid transitions.";
            }
            if (GUILayout.Button("Fix – Remove Invalid Transitions"))
            {
                AnimatorTransitionCleaner.FixInvalidTransitionsRemove();
                _statusMessage = "Removed invalid transitions.";
            }
        }
        #endregion

        #region TMP CanvasRenderer
        private void DrawTMPCanvasRendererSection()
        {
            if (GUILayout.Button("Scan Project"))
            {
                var scan = TMPCanvasRendererCleaner.ScanProject();
                _statusMessage = $"TMP CanvasRenderer – Redundant: {scan.RedundantRendererCount}, Scenes: {scan.AffectedScenesCount}, Prefabs: {scan.AffectedPrefabsCount}";
                Debug.Log($"[CleanupHub] {_statusMessage}");
            }
            if (GUILayout.Button("Fix All (Scenes && Prefabs)"))
            {
                var result = TMPCanvasRendererCleaner.CleanAll();
                _statusMessage = $"Removed {result.ObjectsFixed} redundant CanvasRenderer(s) – {result.ScenesChanged} scene(s), {result.PrefabsChanged} prefab(s).";
                Debug.Log($"[CleanupHub] {_statusMessage}");
            }
        }
        #endregion

        #region TMP TextContainer
        private void DrawTMPTextContainerSection()
        {
            // Assuming TMPTextContainerCleaner exists with similar API.
            var type = typeof(TMPTextContainerCleaner);
            var scanMethod = type.GetMethod("ScanProject");
            var cleanMethod = type.GetMethod("CleanAll");
            if (GUILayout.Button("Scan Project"))
            {
                var scan = scanMethod.Invoke(null, null);
                var field = scan.GetType().GetField("RedundantRendererCount");
                _statusMessage = $"TMP TextContainer – Redundant: {field.GetValue(scan)}";
                Debug.Log($"[CleanupHub] {_statusMessage}");
            }
            if (GUILayout.Button("Fix All (Scenes && Prefabs)"))
            {
                var result = cleanMethod.Invoke(null, null);
                var objs = result.GetType().GetField("ObjectsFixed").GetValue(result);
                var scenes = result.GetType().GetField("ScenesChanged").GetValue(result);
                var prefabs = result.GetType().GetField("PrefabsChanged").GetValue(result);
                _statusMessage = $"Removed {objs} redundant TextContainer(s) – {scenes} scene(s), {prefabs} prefab(s).";
                Debug.Log($"[CleanupHub] {_statusMessage}");
            }
        }
        #endregion

        #region Unused IAP Buttons
        private void DrawUnusedIAPButtonSection()
        {
            // Assuming UnusedIAPButtonCleaner exists with ScanProject and CleanAll.
            var type = typeof(UnusedIAPButtonCleaner);
            var scanMethod = type.GetMethod("ScanProject");
            var cleanMethod = type.GetMethod("CleanAll");
            if (GUILayout.Button("Scan Project"))
            {
                var scan = scanMethod?.Invoke(null, null);
                var countField = scan?.GetType().GetField("UnusedButtonCount");
                var count = countField?.GetValue(scan) ?? 0;
                _statusMessage = $"Unused IAP Buttons – {count} found.";
                Debug.Log($"[CleanupHub] {_statusMessage}");
            }
            if (GUILayout.Button("Fix All (Scenes && Prefabs)"))
            {
                var result = cleanMethod?.Invoke(null, null);
                var removedField = result?.GetType().GetField("ButtonsRemoved");
                var removed = removedField?.GetValue(result) ?? 0;
                _statusMessage = $"Removed {removed} unused IAP Button(s).";
                Debug.Log($"[CleanupHub] {_statusMessage}");
            }
        }
        #endregion
    }
}

