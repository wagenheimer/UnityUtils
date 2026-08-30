// Unity Editor window that aggregates all cleanup tools.
// Path: k:/Games/Open Source/UnityUtils/Editor/Cleanup/CleanupHubWindow.cs

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

        // Foldout state per section.
        private bool _foldoutAudioListeners = true;
        private bool _foldoutMissingScripts = true;
        private bool _foldoutLegacyComponents = true;
        private bool _foldoutAnimatorTransitions = true;
        private bool _foldoutTMPCanvasRenderer = true;
        private bool _foldoutTMPTextContainer = true;
        private bool _foldoutUnusedIAPButtons = true;

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

            DrawSection("Audio Listeners", ref _foldoutAudioListeners, DrawAudioListenerSection);
            DrawSection("Missing Scripts", ref _foldoutMissingScripts, DrawMissingScriptsSection);
            DrawSection("Legacy Components", ref _foldoutLegacyComponents, DrawLegacyComponentsSection);
            DrawSection("Animator Transitions", ref _foldoutAnimatorTransitions, DrawAnimatorTransitionsSection);
            DrawSection("TMP CanvasRenderer", ref _foldoutTMPCanvasRenderer, DrawTMPCanvasRendererSection);
            DrawSection("TMP TextContainer", ref _foldoutTMPTextContainer, DrawTMPTextContainerSection);
            DrawSection("Unused IAP Buttons", ref _foldoutUnusedIAPButtons, DrawUnusedIAPButtonSection);

            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
            }
        }

        private void DrawSection(string title, ref bool foldout, System.Action drawContent)
        {
            EditorGUILayout.BeginVertical("box");
            foldout = EditorGUILayout.Foldout(foldout, title, true, EditorStyles.boldLabel);
            if (foldout)
            {
                drawContent?.Invoke();
            }
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
            if (GUILayout.Button("Scan Project"))
            {
                var scan = TMPTextContainerCleaner.ScanProject();
                _statusMessage = $"TMP TextContainer – Obsolete: {scan.ObsoleteContainerCount}, Scenes: {scan.AffectedScenesCount}, Prefabs: {scan.AffectedPrefabsCount}";
                Debug.Log($"[CleanupHub] {_statusMessage}");
            }
            if (GUILayout.Button("Fix All (Scenes && Prefabs)"))
            {
                var result = TMPTextContainerCleaner.CleanAll();
                _statusMessage = $"Removed {result.ObjectsFixed} obsolete TextContainer(s) – {result.ScenesChanged} scene(s), {result.PrefabsChanged} prefab(s).";
                Debug.Log($"[CleanupHub] {_statusMessage}");
            }
        }
        #endregion

        #region Unused IAP Buttons
        private void DrawUnusedIAPButtonSection()
        {
#if !WAGENHEIMER_UNITYUTILS_IAP
            EditorGUILayout.HelpBox(
                "IAP support is not enabled in this project (define WAGENHEIMER_UNITYUTILS_IAP). " +
                "Scan and fix actions are disabled.", MessageType.Warning);
#else
            if (GUILayout.Button("Scan Project"))
            {
                var scan = UnusedIAPButtonCleaner.ScanProject();
                _statusMessage = $"Unused IAP Buttons – Unused components: {scan.UnusedComponentCount}, Scenes: {scan.AffectedScenesCount}, Prefabs: {scan.AffectedPrefabsCount}";
                Debug.Log($"[CleanupHub] {_statusMessage}");
            }
            if (GUILayout.Button("Fix All (Scenes && Prefabs)"))
            {
                var result = UnusedIAPButtonCleaner.CleanAll();
                _statusMessage = $"Removed {result.ObjectsFixed} unused IAP component(s) – {result.ScenesChanged} scene(s), {result.PrefabsChanged} prefab(s).";
                Debug.Log($"[CleanupHub] {_statusMessage}");
            }
#endif
        }
        #endregion
    }
}
