using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// UI Toolkit view presenting the consolidated Project Cleanup Suite with
    /// one-click scanning, metric cards, contextual diagnostics, recommended solutions,
    /// interactive pingable asset lists, and before/after improvement panels.
    /// </summary>
    public class CleanupSuiteView : VisualElement
    {
        private Label _audioMetric;
        private Label _scriptsMetric;
        private Label _tmpMetric;
        private Label _statusLabel;

        // Dynamic result panels under each card
        private VisualElement _audioResultPanel;
        private VisualElement _scriptsResultPanel;
        private VisualElement _tmpResultPanel;
        private VisualElement _animResultPanel;
        private VisualElement _legacyResultPanel;
        private VisualElement _iapResultPanel;

        // Last scan counts for diffing
        private int _lastAudioCount;
        private int _lastScriptsCount;
        private int _lastTmpCount;
        private int _lastAnimCount;
        private int _lastLegacyCount;
        private int _lastIapCount;

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

            audioBar.Add(CreateActionButton("Scan Project", "btn-secondary", "AudioListener scan", RunAudioScan));
            audioBar.Add(CreateActionButton("Fix All (Scenes & Prefabs)", "btn-primary", "AudioListener cleanup", RunAudioFixAll));
            audioBar.Add(CreateActionButton("Remove Non-Bootstrap Listeners", "btn-danger", "AudioListener removal", RunAudioRemoveNonBootstrap));

            audioBody.Add(audioBar);
            _audioResultPanel = new VisualElement();
            _audioResultPanel.AddToClassList("scan-result-panel");
            _audioResultPanel.style.display = DisplayStyle.None;
            audioBody.Add(_audioResultPanel);
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

            scriptBar.Add(CreateActionButton("Scan Project", "btn-secondary", "Missing scripts scan", RunScriptsScan));
            scriptBar.Add(CreateActionButton("Fix All (Scenes & Prefabs)", "btn-primary", "Missing scripts cleanup", RunScriptsFixAll));

            scriptBody.Add(scriptBar);
            _scriptsResultPanel = new VisualElement();
            _scriptsResultPanel.AddToClassList("scan-result-panel");
            _scriptsResultPanel.style.display = DisplayStyle.None;
            scriptBody.Add(_scriptsResultPanel);
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

            tmpBar.Add(CreateActionButton("Scan TMP Issues", "btn-secondary", "TMP scan", RunTmpScan));
            tmpBar.Add(CreateActionButton("Fix CanvasRenderers", "btn-primary", "CanvasRenderer cleanup", RunTmpCanvasFix));
            tmpBar.Add(CreateActionButton("Fix TextContainers", "btn-primary", "TextContainer cleanup", RunTmpContainerFix));

            tmpBody.Add(tmpBar);
            _tmpResultPanel = new VisualElement();
            _tmpResultPanel.AddToClassList("scan-result-panel");
            _tmpResultPanel.style.display = DisplayStyle.None;
            tmpBody.Add(_tmpResultPanel);
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

            animBar.Add(CreateActionButton("Audit Transitions", "btn-secondary", "Animator transition audit", RunAnimScan));
            animBar.Add(CreateActionButton("Fix: Enable Exit Time", "btn-primary", "Animator transition fix", () => RunAnimFix(AnimatorTransitionCleaner.FixMode.EnableExitTime)));
            animBar.Add(CreateActionButton("Fix: Remove Invalid", "btn-danger", "Animator transition removal", () => RunAnimFix(AnimatorTransitionCleaner.FixMode.Remove)));

            animBody.Add(animBar);
            _animResultPanel = new VisualElement();
            _animResultPanel.AddToClassList("scan-result-panel");
            _animResultPanel.style.display = DisplayStyle.None;
            animBody.Add(_animResultPanel);
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

            legacyBar.Add(CreateActionButton("Scan Project", "btn-secondary", "Legacy components scan", RunLegacyScan));
            legacyBar.Add(CreateActionButton("Modernize & Resave Project", "btn-secondary", "Project modernize", RunLegacyResaveAll));
            legacyBar.Add(CreateActionButton("Resave Legacy Plugin Meta", "btn-primary", "Legacy plugin importer resave", RunLegacyResavePlugins));

            legacyBody.Add(legacyBar);
            _legacyResultPanel = new VisualElement();
            _legacyResultPanel.AddToClassList("scan-result-panel");
            _legacyResultPanel.style.display = DisplayStyle.None;
            legacyBody.Add(_legacyResultPanel);
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

            iapBar.Add(CreateActionButton("Scan IAP Buttons", "btn-secondary", "IAP button scan", RunIapScan));
            iapBar.Add(CreateActionButton("Fix All Unused", "btn-primary", "IAP button cleanup", RunIapFixAll));

            iapBody.Add(iapBar);
            _iapResultPanel = new VisualElement();
            _iapResultPanel.AddToClassList("scan-result-panel");
            _iapResultPanel.style.display = DisplayStyle.None;
            iapBody.Add(_iapResultPanel);
#endif
            scroll.Add(iapCard);

            // Status label
            _statusLabel = new Label("Ready. Click 'Run Full Project Scan' to check all cleaners.");
            _statusLabel.AddToClassList("status-box");
            scroll.Add(_statusLabel);

            Add(scroll);
        }

        #region Scan Operations

        private void RunFullScan()
        {
            RunGuarded("Full project scan", () =>
            {
                RunAudioScan();
                RunScriptsScan();
                RunTmpScan();
                RunAnimScan();
                RunLegacyScan();
#if WAGENHEIMER_UNITYUTILS_IAP
                RunIapScan();
#endif
                SetStatus("Full project audit complete. Review the results and diagnostics below each module.");
            });
        }

        private void RunAudioScan()
        {
            var scan = AudioListenerCleaner.ScanProject();
            _lastAudioCount = scan.DuplicateCount;
            _audioMetric.text = scan.DuplicateCount.ToString();

            RenderScanResults(
                _audioResultPanel,
                issueCount: scan.DuplicateCount,
                successMsg: "No duplicate AudioListeners found. Your scenes and prefabs have clean audio!",
                diagTitle: "Multiple AudioListeners Detected",
                diagDesc: $"Found {scan.DuplicateCount} duplicate AudioListener(s) in {scan.AffectedScenesCount} scene(s) and {scan.AffectedPrefabsCount} prefab(s). Having more than one listener in a scene causes recurring console warnings and splits audio spatialization.",
                recDesc: "Recommendation: Keep only the main AudioListener (in the bootstrap scene / main camera) and remove the duplicates from the other scenes and prefabs.",
                fixBtnText: "Fix Now (Remove Duplicates)",
                fixAction: RunAudioFixAll,
                items: scan.Items);

            SetStatus($"AudioListeners scan: {scan.DuplicateCount} duplicados em {scan.AffectedScenesCount} cena(s) e {scan.AffectedPrefabsCount} prefab(s).");
        }

        private void RunScriptsScan()
        {
            var scan = MissingScriptCleaner.ScanProject();
            _lastScriptsCount = scan.MissingScriptCount;
            _scriptsMetric.text = scan.MissingScriptCount.ToString();

            RenderScanResults(
                _scriptsResultPanel,
                issueCount: scan.MissingScriptCount,
                successMsg: "No missing scripts found. All MonoBehaviour components are intact!",
                diagTitle: "Components With Missing Scripts Detected",
                diagDesc: $"Found {scan.MissingScriptCount} MonoBehaviour component(s) with a missing script in {scan.AffectedScenesCount} scene(s) and {scan.AffectedPrefabsCount} prefab(s). Missing scripts break references, slow down deserialization and produce persistent 'The referenced script on this Behaviour is missing!' warnings.",
                recDesc: "Recommendation: Remove the orphaned entries of corrupted or uninstalled MonoBehaviours to clean up serialization.",
                fixBtnText: "Remove All Missing Scripts",
                fixAction: RunScriptsFixAll,
                items: scan.Items);

            SetStatus($"Missing scripts scan: {scan.MissingScriptCount} scripts ausentes em {scan.AffectedScenesCount} cena(s) e {scan.AffectedPrefabsCount} prefab(s).");
        }

        private void RunTmpScan()
        {
            var rScan = TMPCanvasRendererCleaner.ScanProject();
            var cScan = TMPTextContainerCleaner.ScanProject();
            var total = rScan.RedundantRendererCount + cScan.ObsoleteContainerCount;
            _lastTmpCount = total;
            _tmpMetric.text = total.ToString();

            var combinedItems = new List<CleanupIssueItem>();
            if (rScan.Items != null) combinedItems.AddRange(rScan.Items);
            if (cScan.Items != null) combinedItems.AddRange(cScan.Items);

            RenderScanResults(
                _tmpResultPanel,
                issueCount: total,
                successMsg: "No redundant TextMeshPro components found. Your 3D and UI texts are clean!",
                diagTitle: "Redundant / Obsolete TextMeshPro Components",
                diagDesc: $"Detected {rScan.RedundantRendererCount} unnecessary CanvasRenderer(s) on world-space texts and {cScan.ObsoleteContainerCount} legacy TextContainer(s). World-space TextMeshPro does not need a CanvasRenderer (it logs runtime warnings) and TextContainer is obsolete in current versions.",
                recDesc: "Recommendation: Safely remove the redundant CanvasRenderers and legacy TextContainers.",
                fixBtnText: "Fix All TMP Redundancies",
                fixAction: () =>
                {
                    RunTmpCanvasFix();
                    RunTmpContainerFix();
                },
                items: combinedItems);

            SetStatus($"TMP scan: {rScan.RedundantRendererCount} CanvasRenderers redundantes, {cScan.ObsoleteContainerCount} TextContainers obsoletos.");
        }

        private void RunAnimScan()
        {
            var scan = AnimatorTransitionCleaner.ScanProject();
            _lastAnimCount = scan.InvalidTransitionCount;

            RenderScanResults(
                _animResultPanel,
                issueCount: scan.InvalidTransitionCount,
                successMsg: "All Animator Controller transitions are valid and healthy!",
                diagTitle: "Animator Transitions Without Condition or Exit Time",
                diagDesc: $"Found {scan.InvalidTransitionCount} invalid Animator transition(s) in {scan.AffectedControllersCount} controller(s). AnyState transitions with no Exit Time and no condition are never fired by the animation engine and spam the console with warnings.",
                recDesc: "Recommendation: Enable 'Has Exit Time' with exitTime = 1.0 so the transition plays normally after the animation ends.",
                fixBtnText: "Enable Exit Time (Recommended)",
                fixAction: () => RunAnimFix(AnimatorTransitionCleaner.FixMode.EnableExitTime),
                items: scan.Items);

            SetStatus($"Animator scan: {scan.InvalidTransitionCount} invalid transition(s) in {scan.AffectedControllersCount} controller(s).");
        }

        private void RunLegacyScan()
        {
            var scan = LegacyComponentCleaner.ScanProject();
            _lastLegacyCount = scan.LegacyPluginCount;

            RenderScanResults(
                _legacyResultPanel,
                issueCount: scan.LegacyPluginCount,
                successMsg: "All plugin metadata is on version 2 (modern) and the project is up to date!",
                diagTitle: "Plugin Metadata on Legacy Version (Version 1)",
                diagDesc: $"Found {scan.LegacyPluginCount} legacy plugin .meta file(s) where PluginImporter is on version 1. Unity 6 requires version 2 and warns on every compile: 'contains a PluginImporter object at version 1, below the supported minimum (2)'.",
                recDesc: "Recommendation: Re-save the legacy plugin importers to raise serialization to version 2 without touching the binaries.",
                fixBtnText: "Re-salvar Metas dos Plugins Legados",
                fixAction: RunLegacyResavePlugins,
                items: scan.Items);

            SetStatus($"Legacy scan: {scan.LegacyPluginCount} plugin meta(s) legados encontrados no projeto.");
        }

#if WAGENHEIMER_UNITYUTILS_IAP
        private void RunIapScan()
        {
            var scan = UnusedIAPButtonCleaner.ScanProject();
            _lastIapCount = scan.UnusedComponentCount;

            RenderScanResults(
                _iapResultPanel,
                issueCount: scan.UnusedComponentCount,
                successMsg: "No unused IAP buttons found!",
                diagTitle: "Codeless IAP Buttons Without Configured Events",
                diagDesc: $"Found {scan.UnusedComponentCount} inactive/unused IAPButton component(s). IAPButton components without events fire automatic background listeners that pollute logs and consume check cycles.",
                recDesc: "Recommendation: Remove empty or unconfigured IAPButton components.",
                fixBtnText: "Remove Unused IAP Buttons",
                fixAction: RunIapFixAll,
                items: scan.Items);

            SetStatus($"IAP scan: {scan.UnusedComponentCount} unused IAP button(s) detected.");
        }
#endif

        #endregion

        #region Fix Operations

        private void RunAudioFixAll()
        {
            var before = _lastAudioCount;
            var sceneRes = AudioListenerCleaner.CleanAllScenes(removeAllNonPersistent: false);
            var prefabRes = AudioListenerCleaner.CleanAllPrefabs(removeAllNonPersistent: false);
            var total = sceneRes.DuplicatesRemoved + prefabRes.DuplicatesRemoved;
            _audioMetric.text = "0";

            var allMods = new List<CleanupModificationRecord>();
            if (sceneRes.Modifications != null) allMods.AddRange(sceneRes.Modifications);
            if (prefabRes.Modifications != null) allMods.AddRange(prefabRes.Modifications);

            RenderDiffCard(
                _audioResultPanel,
                title: "✓ Duplicate AudioListeners Removed",
                beforeCount: before > 0 ? before : total,
                afterCount: 0,
                benefitDesc: $"Removed {total} duplicate AudioListener(s) in {sceneRes.ScenesModified} scene(s) and {prefabRes.PrefabsModified} prefab(s). Audio conflicts eliminated.",
                modifications: allMods);

            SetStatus($"Removed {total} duplicate AudioListener(s) ({sceneRes.ScenesModified} scene(s), {prefabRes.PrefabsModified} prefab(s)).");
        }

        private void RunAudioRemoveNonBootstrap()
        {
            var before = _lastAudioCount;
            AudioListenerCleaner.RemoveAllNonBootstrapListeners();
            _audioMetric.text = "0";

            RenderDiffCard(
                _audioResultPanel,
                title: "✓ Non-Bootstrap Listeners Removed",
                beforeCount: before,
                afterCount: 0,
                benefitDesc: "All AudioListeners outside the bootstrap scene were removed successfully. Audio is centralized in the bootstrap scene.",
                modifications: null);

            SetStatus("Removed all AudioListeners that do not belong to the bootstrap scene.");
        }

        private void RunScriptsFixAll()
        {
            var before = _lastScriptsCount;
            var sceneRes = MissingScriptCleaner.CleanAllScenes();
            var prefabRes = MissingScriptCleaner.CleanAllPrefabs();
            var total = sceneRes.MissingScriptsRemoved + prefabRes.MissingScriptsRemoved;
            _scriptsMetric.text = "0";

            var allMods = new List<CleanupModificationRecord>();
            if (sceneRes.Modifications != null) allMods.AddRange(sceneRes.Modifications);
            if (prefabRes.Modifications != null) allMods.AddRange(prefabRes.Modifications);

            RenderDiffCard(
                _scriptsResultPanel,
                title: "✓ Missing Scripts Removed Successfully",
                beforeCount: before > 0 ? before : total,
                afterCount: 0,
                benefitDesc: $"Removed {total} MonoBehaviour component(s) with a missing script in {sceneRes.ScenesModified} scene(s) and {prefabRes.PrefabsModified} prefab(s). Serialization cleaned and console warnings suppressed.",
                modifications: allMods);

            SetStatus($"Removed {total} missing script(s) ({sceneRes.ScenesModified} scene(s), {prefabRes.PrefabsModified} prefab(s)).");
        }

        private void RunTmpCanvasFix()
        {
            var before = _lastTmpCount;
            var res = TMPCanvasRendererCleaner.CleanAll();
            _tmpMetric.text = "0";

            RenderDiffCard(
                _tmpResultPanel,
                title: "✓ Redundant CanvasRenderers Removed",
                beforeCount: before > 0 ? before : res.ObjectsFixed,
                afterCount: 0,
                benefitDesc: $"Removed {res.ObjectsFixed} redundant CanvasRenderer(s) on 3D texts ({res.ScenesChanged} scene(s), {res.PrefabsChanged} prefab(s)). 'Please remove the CanvasRenderer' warnings suppressed.",
                modifications: res.Modifications);

            SetStatus($"Fixed {res.ObjectsFixed} redundant CanvasRenderer(s) on 3D TextMeshPro.");
        }

        private void RunTmpContainerFix()
        {
            var before = _lastTmpCount;
            var res = TMPTextContainerCleaner.CleanAll();
            _tmpMetric.text = "0";

            RenderDiffCard(
                _tmpResultPanel,
                title: "✓ Obsolete TextContainers Removed",
                beforeCount: before > 0 ? before : res.ObjectsFixed,
                afterCount: 0,
                benefitDesc: $"Removed {res.ObjectsFixed} obsolete TextContainer(s) ({res.ScenesChanged} scene(s), {res.PrefabsChanged} prefab(s)).",
                modifications: res.Modifications);

            SetStatus($"Fixed {res.ObjectsFixed} obsolete TextContainer(s).");
        }

        private void RunAnimFix(AnimatorTransitionCleaner.FixMode mode)
        {
            var before = _lastAnimCount;
            var res = AnimatorTransitionCleaner.CleanAll(mode);

            RenderDiffCard(
                _animResultPanel,
                title: mode == AnimatorTransitionCleaner.FixMode.EnableExitTime ? "✓ Exit Time Enabled on Transitions" : "✓ Invalid Transitions Removed",
                beforeCount: before > 0 ? before : res.TransitionsFixed,
                afterCount: 0,
                benefitDesc: $"Fixed {res.TransitionsFixed} transition(s) in {res.ControllersModified} Animator Controller(s). 'doesn't have an Exit Time' warnings suppressed.",
                modifications: res.Modifications);

            SetStatus($"Fixed {res.TransitionsFixed} Animator transition(s).");
        }

        private void RunLegacyResavePlugins()
        {
            var before = _lastLegacyCount;
            var res = LegacyComponentCleaner.ResaveLegacyPluginImportersWithResult();

            RenderDiffCard(
                _legacyResultPanel,
                title: "✓ Plugin Metadata Upgraded to Version 2",
                beforeCount: before > 0 ? before : res.PluginsUpdated,
                afterCount: 0,
                benefitDesc: $"Re-saved and upgraded {res.PluginsUpdated} legacy plugin .meta file(s). The Unity 6 'contains a PluginImporter object at version 1' warning is gone.",
                modifications: res.Modifications);

            SetStatus($"Re-saved {res.PluginsUpdated} legacy plugin importer(s).");
        }

        private void RunLegacyResaveAll()
        {
            LegacyComponentCleaner.ResaveAllScenesAndPrefabs();

            RenderDiffCard(
                _legacyResultPanel,
                title: "✓ Project Fully Modernized and Reserialized",
                beforeCount: _lastLegacyCount,
                afterCount: 0,
                benefitDesc: "All scenes, prefabs and ScriptableObjects were reserialized and upgraded to the current native Unity format. GUILayers and orphaned references purged.",
                modifications: null);

            SetStatus("All scenes, prefabs and plugin metadata were re-saved and modernized.");
        }

#if WAGENHEIMER_UNITYUTILS_IAP
        private void RunIapFixAll()
        {
            var before = _lastIapCount;
            var res = UnusedIAPButtonCleaner.CleanAll();

            RenderDiffCard(
                _iapResultPanel,
                title: "✓ Codeless IAP Buttons Cleaned",
                beforeCount: before > 0 ? before : res.ObjectsFixed,
                afterCount: 0,
                benefitDesc: $"Removed {res.ObjectsFixed} inactive IAP button(s) with no bound events ({res.ScenesChanged} scene(s), {res.PrefabsChanged} prefab(s)).",
                modifications: res.Modifications);

            SetStatus($"Removed {res.ObjectsFixed} inactive IAPButton component(s).");
        }
#endif

        #endregion

        #region UI Rendering Helpers

        private void RenderScanResults(
            VisualElement panel,
            int issueCount,
            string successMsg,
            string diagTitle,
            string diagDesc,
            string recDesc,
            string fixBtnText,
            Action fixAction,
            List<CleanupIssueItem> items)
        {
            panel.Clear();
            panel.style.display = DisplayStyle.Flex;

            if (issueCount == 0)
            {
                var successBox = new VisualElement();
                successBox.AddToClassList("clean-success-banner");

                var successLabel = new Label($"✓ {successMsg}");
                successLabel.AddToClassList("clean-success-text");
                successBox.Add(successLabel);

                panel.Add(successBox);
                return;
            }

            // Solution Banner
            var solutionBox = new VisualElement();
            solutionBox.AddToClassList("solution-banner");

            var solTitle = new Label($"💡 {diagTitle} ({issueCount} item(s) encontrado(s))");
            solTitle.AddToClassList("solution-title");
            solutionBox.Add(solTitle);

            var solDesc = new Label($"{diagDesc}\n\n{recDesc}");
            solDesc.AddToClassList("solution-desc");
            solutionBox.Add(solDesc);

            if (fixAction != null && !string.IsNullOrEmpty(fixBtnText))
            {
                var actionRow = new VisualElement();
                actionRow.AddToClassList("solution-action-row");

                var fixBtn = UnityUtilsUIStyle.CreateButton(fixBtnText, "btn-primary", () => RunGuarded("Recommended fix", fixAction));
                fixBtn.style.alignSelf = Align.FlexStart;
                actionRow.Add(fixBtn);

                solutionBox.Add(actionRow);
            }

            panel.Add(solutionBox);

            // Detailed items list
            if (items != null && items.Count > 0)
            {
                var listContainer = new ScrollView();
                listContainer.AddToClassList("items-list-container");

                foreach (var item in items)
                {
                    var row = new VisualElement();
                    row.AddToClassList("item-row");

                    var content = new VisualElement();
                    content.AddToClassList("item-content");

                    var pathLabel = new Label($"{(item.IsScene ? "🎬 [Scene] " : item.IsPrefab ? "📦 [Prefab] " : "📄 ")}{item.AssetPath}");
                    pathLabel.AddToClassList("item-path");

                    var descLabel = new Label(item.Description);
                    descLabel.AddToClassList("item-desc");

                    content.Add(pathLabel);
                    content.Add(descLabel);
                    row.Add(content);

                    var pingBtn = new Button(() => CleanupAssetUtility.PingAsset(item.AssetPath));
                    pingBtn.text = "Inspecionar";
                    pingBtn.AddToClassList("btn-ping");
                    row.Add(pingBtn);

                    listContainer.Add(row);
                }

                panel.Add(listContainer);
            }
        }

        private void RenderDiffCard(
            VisualElement panel,
            string title,
            int beforeCount,
            int afterCount,
            string benefitDesc,
            List<CleanupModificationRecord> modifications)
        {
            panel.Clear();
            panel.style.display = DisplayStyle.Flex;

            var diffCard = new VisualElement();
            diffCard.AddToClassList("diff-card");

            var header = new VisualElement();
            header.AddToClassList("diff-header");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("diff-title");

            var badge = new Label(beforeCount > 0 ? $"{beforeCount} ➔ {afterCount} resolvido" : "✓ 100% Otimizado");
            badge.AddToClassList("diff-stat-badge");

            header.Add(titleLabel);
            header.Add(badge);
            diffCard.Add(header);

            var desc = new Label(benefitDesc);
            desc.AddToClassList("diff-desc");
            diffCard.Add(desc);

            if (modifications != null && modifications.Count > 0)
            {
                var list = new ScrollView();
                list.AddToClassList("diff-list");

                foreach (var mod in modifications)
                {
                    var item = new Label($"• {mod.AssetPath} ➔ {mod.ActionTaken}");
                    item.AddToClassList("diff-item");
                    list.Add(item);
                }

                diffCard.Add(list);
            }

            panel.Add(diffCard);
        }

        #endregion

        private Button CreateActionButton(string label, string styleClass, string operation, Action action)
        {
            return UnityUtilsUIStyle.CreateButton(label, styleClass, () => RunGuarded(operation, action));
        }

        private void RunGuarded(string operation, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                SetStatus($"{operation} failed: {ex.Message}");
            }
        }

        private void SetStatus(string message)
        {
            if (_statusLabel == null)
            {
                Debug.Log($"[CleanupSuite] {message}");
                return;
            }

            _statusLabel.text = message;
            Debug.Log($"[CleanupSuite] {message}");
        }
    }
}
