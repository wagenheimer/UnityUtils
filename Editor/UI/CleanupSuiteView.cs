using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// UI Toolkit view presenting the consolidated Project Cleanup Suite with
    /// one-click scanning, metric cards, and individual cleaner tools.
    /// </summary>
    public class CleanupSuiteView : VisualElement
    {
        private Label _audioMetric;
        private Label _scriptsMetric;
        private Label _tmpMetric;
        private Label _statusLabel;

        public CleanupSuiteView()
        {
            BuildUI();
        }

        private void BuildUI()
        {
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;

            // 1. Metrics Overview
            var metricsRow = new VisualElement();
            metricsRow.AddToClassList("metrics-row");

            metricsRow.Add(UnityUtilsUIStyle.CreateMetricCard("Audio Duplicates", "-", out _audioMetric));
            metricsRow.Add(UnityUtilsUIStyle.CreateMetricCard("Missing Scripts", "-", out _scriptsMetric));
            metricsRow.Add(UnityUtilsUIStyle.CreateMetricCard("TMP Redundancies", "-", out _tmpMetric));
            scroll.Add(metricsRow);

            // 2. Scan All Banner
            var scanCard = UnityUtilsUIStyle.CreateCard(
                "Full Project Audit",
                "Scan all scenes and prefabs across all diagnostic cleaner modules.",
                out var scanBody);

            var scanBtn = UnityUtilsUIStyle.CreateButton("Run Full Project Scan", "btn-primary", RunFullScan);
            scanBtn.style.alignSelf = Align.FlexStart;
            scanBody.Add(scanBtn);
            scroll.Add(scanCard);

            // 3. Audio Listeners Card
            var audioCard = UnityUtilsUIStyle.CreateCard(
                "Audio Listeners",
                "Detects and cleans duplicate AudioListeners across scenes and prefabs.",
                out var audioBody);

            var audioBar = new VisualElement();
            audioBar.AddToClassList("action-toolbar");
            audioBar.style.marginTop = 0;
            audioBar.style.borderTopWidth = 0;

            audioBar.Add(UnityUtilsUIStyle.CreateButton("Scan Project", "btn-secondary", () =>
            {
                var scan = AudioListenerCleaner.ScanProject();
                _audioMetric.text = scan.DuplicateCount.ToString();
                SetStatus($"AudioListeners scan: {scan.DuplicateCount} duplicates in {scan.AffectedScenesCount} scenes and {scan.AffectedPrefabsCount} prefabs.");
            }));

            audioBar.Add(UnityUtilsUIStyle.CreateButton("Fix All (Scenes & Prefabs)", "btn-primary", () =>
            {
                var sceneRes = AudioListenerCleaner.CleanAllScenes(removeAllNonPersistent: false);
                var prefabRes = AudioListenerCleaner.CleanAllPrefabs(removeAllNonPersistent: false);
                var total = sceneRes.DuplicatesRemoved + prefabRes.DuplicatesRemoved;
                _audioMetric.text = "0";
                SetStatus($"Removed {total} duplicate AudioListener(s) ({sceneRes.ScenesModified} scene(s), {prefabRes.PrefabsModified} prefab(s)).");
            }));

            audioBar.Add(UnityUtilsUIStyle.CreateButton("Remove Non-Bootstrap Listeners", "btn-danger", () =>
            {
                AudioListenerCleaner.RemoveAllNonBootstrapListeners();
                SetStatus("Removed all non-bootstrap AudioListeners.");
            }));

            audioBody.Add(audioBar);
            scroll.Add(audioCard);

            // 4. Missing Scripts Card
            var scriptCard = UnityUtilsUIStyle.CreateCard(
                "Missing Scripts",
                "Finds and removes missing MonoBehaviour script components from GameObjects.",
                out var scriptBody);

            var scriptBar = new VisualElement();
            scriptBar.AddToClassList("action-toolbar");
            scriptBar.style.marginTop = 0;
            scriptBar.style.borderTopWidth = 0;

            scriptBar.Add(UnityUtilsUIStyle.CreateButton("Scan Project", "btn-secondary", () =>
            {
                var scan = MissingScriptCleaner.ScanProject();
                _scriptsMetric.text = scan.MissingScriptCount.ToString();
                SetStatus($"Missing scripts scan: {scan.MissingScriptCount} missing scripts in {scan.AffectedScenesCount} scenes and {scan.AffectedPrefabsCount} prefabs.");
            }));

            scriptBar.Add(UnityUtilsUIStyle.CreateButton("Fix All (Scenes & Prefabs)", "btn-primary", () =>
            {
                var sceneRes = MissingScriptCleaner.CleanAllScenes();
                var prefabRes = MissingScriptCleaner.CleanAllPrefabs();
                var total = sceneRes.MissingScriptsRemoved + prefabRes.MissingScriptsRemoved;
                _scriptsMetric.text = "0";
                SetStatus($"Removed {total} missing script(s) ({sceneRes.ScenesModified} scene(s), {prefabRes.PrefabsModified} prefab(s)).");
            }));

            scriptBody.Add(scriptBar);
            scroll.Add(scriptCard);

            // 5. TextMeshPro Cleanup Card
            var tmpCard = UnityUtilsUIStyle.CreateCard(
                "TextMesh Pro Cleaners",
                "Cleans redundant CanvasRenderers on world-space text and removes obsolete TextContainers.",
                out var tmpBody);

            var tmpBar = new VisualElement();
            tmpBar.AddToClassList("action-toolbar");
            tmpBar.style.marginTop = 0;
            tmpBar.style.borderTopWidth = 0;

            tmpBar.Add(UnityUtilsUIStyle.CreateButton("Scan TMP Issues", "btn-secondary", () =>
            {
                var rScan = TMPCanvasRendererCleaner.ScanProject();
                var cScan = TMPTextContainerCleaner.ScanProject();
                var total = rScan.RedundantRendererCount + cScan.ObsoleteContainerCount;
                _tmpMetric.text = total.ToString();
                SetStatus($"TMP scan: {rScan.RedundantRendererCount} redundant CanvasRenderers, {cScan.ObsoleteContainerCount} obsolete TextContainers.");
            }));

            tmpBar.Add(UnityUtilsUIStyle.CreateButton("Fix CanvasRenderers", "btn-primary", () =>
            {
                var res = TMPCanvasRendererCleaner.CleanAll();
                SetStatus($"Fixed {res.ObjectsFixed} redundant CanvasRenderer(s).");
            }));

            tmpBar.Add(UnityUtilsUIStyle.CreateButton("Fix TextContainers", "btn-primary", () =>
            {
                var res = TMPTextContainerCleaner.CleanAll();
                SetStatus($"Fixed {res.ObjectsFixed} obsolete TextContainer(s).");
            }));

            tmpBody.Add(tmpBar);
            scroll.Add(tmpCard);

            // 6. Animator Transitions Card
            var animCard = UnityUtilsUIStyle.CreateCard(
                "Animator Transitions",
                "Audits and repairs invalid animator state transitions without conditions.",
                out var animBody);

            var animBar = new VisualElement();
            animBar.AddToClassList("action-toolbar");
            animBar.style.marginTop = 0;
            animBar.style.borderTopWidth = 0;

            animBar.Add(UnityUtilsUIStyle.CreateButton("Audit Transitions", "btn-secondary", () =>
            {
                AnimatorTransitionCleaner.AuditInvalidTransitions();
                SetStatus("Audited animator transitions. Check Console for details.");
            }));

            animBar.Add(UnityUtilsUIStyle.CreateButton("Fix: Enable Exit Time", "btn-primary", () =>
            {
                AnimatorTransitionCleaner.FixInvalidTransitionsEnableExitTime();
                SetStatus("Enabled Exit Time on invalid transitions.");
            }));

            animBar.Add(UnityUtilsUIStyle.CreateButton("Fix: Remove Invalid", "btn-danger", () =>
            {
                AnimatorTransitionCleaner.FixInvalidTransitionsRemove();
                SetStatus("Removed invalid transitions.");
            }));

            animBody.Add(animBar);
            scroll.Add(animCard);

            // 7. Legacy Components & Modernize Card
            var legacyCard = UnityUtilsUIStyle.CreateCard(
                "Legacy Component Modernizer",
                "Re-saves all scenes and prefabs to reserialize assets with the current Unity version format.",
                out var legacyBody);

            var legacyBar = new VisualElement();
            legacyBar.AddToClassList("action-toolbar");
            legacyBar.style.marginTop = 0;
            legacyBar.style.borderTopWidth = 0;

            legacyBar.Add(UnityUtilsUIStyle.CreateButton("Modernize & Resave Project", "btn-secondary", () =>
            {
                LegacyComponentCleaner.ResaveAllScenesAndPrefabs();
                SetStatus("All scenes and prefabs resaved and modernized.");
            }));

            legacyBody.Add(legacyBar);
            scroll.Add(legacyCard);

            // 8. Unused IAP Buttons Card
            var iapCard = UnityUtilsUIStyle.CreateCard(
                "Unused IAP Buttons",
                "Detects empty and unused Codeless IAP buttons.",
                out var iapBody);

#if !WAGENHEIMER_UNITYUTILS_IAP
            var iapNotice = new Label("IAP support is not enabled in this project (com.unity.purchasing not present).");
            iapNotice.AddToClassList("card-subtitle");
            iapBody.Add(iapNotice);
#else
            var iapBar = new VisualElement();
            iapBar.AddToClassList("action-toolbar");
            iapBar.style.marginTop = 0;
            iapBar.style.borderTopWidth = 0;

            iapBar.Add(UnityUtilsUIStyle.CreateButton("Scan IAP Buttons", "btn-secondary", () =>
            {
                var scan = UnusedIAPButtonCleaner.ScanProject();
                SetStatus($"IAP Buttons scan: {scan.UnusedComponentCount} unused components found.");
            }));

            iapBar.Add(UnityUtilsUIStyle.CreateButton("Fix All Unused", "btn-primary", () =>
            {
                var res = UnusedIAPButtonCleaner.CleanAll();
                SetStatus($"Removed {res.ObjectsFixed} unused IAP button components.");
            }));

            iapBody.Add(iapBar);
#endif
            scroll.Add(iapCard);

            // Status label
            _statusLabel = new Label("Ready. Click 'Run Full Project Scan' to check all cleaners.");
            _statusLabel.AddToClassList("status-box");
            scroll.Add(_statusLabel);

            Add(scroll);
        }

        private void RunFullScan()
        {
            var audioScan = AudioListenerCleaner.ScanProject();
            var scriptsScan = MissingScriptCleaner.ScanProject();
            var tmpRenderScan = TMPCanvasRendererCleaner.ScanProject();
            var tmpContainerScan = TMPTextContainerCleaner.ScanProject();

            _audioMetric.text = audioScan.DuplicateCount.ToString();
            _scriptsMetric.text = scriptsScan.MissingScriptCount.ToString();
            _tmpMetric.text = (tmpRenderScan.RedundantRendererCount + tmpContainerScan.ObsoleteContainerCount).ToString();

            SetStatus($"Full project scan completed: {audioScan.DuplicateCount} audio duplicates, {scriptsScan.MissingScriptCount} missing scripts, {tmpRenderScan.RedundantRendererCount + tmpContainerScan.ObsoleteContainerCount} TMP redundancies.");
        }

        private void SetStatus(string message)
        {
            _statusLabel.text = message;
            Debug.Log($"[CleanupSuite] {message}");
        }
    }
}

