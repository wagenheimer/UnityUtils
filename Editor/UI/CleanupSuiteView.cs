using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Project Cleanup tab. Layout, top to bottom:
    /// 1. an action bar (scan everything / apply every recommended fix) with a one-line health summary,
    /// 2. a live progress panel (stage, current asset, elapsed / remaining time, Cancel) shown while a
    ///    task runs; work is time-sliced by <see cref="CleanupRunner"/> so the editor stays responsive,
    /// 3. the cleaners grouped by topic. Every card has the same shape: title and status badge, what it
    ///    does, a Scan button, the recommended fix, optional advanced actions, and its results.
    /// </summary>
    public class CleanupSuiteView : VisualElement
    {
        /// <summary>UI state of one cleaner card.</summary>
        private sealed class Module
        {
            public string Title;
            public Label Badge;
            public VisualElement Results;
            public int Issues = -1; // -1 = not scanned yet
            public string CleanText = "✓ Clean";

            // The recommended fix, used by the suggestions list at the top of the tab.
            public string FixLabel;
            public string FixTitle;
            public Func<CleanupProgress, IEnumerator> FixRoutine;
            public Action FixAction; // used instead of FixRoutine when the fix needs a confirmation first
        }

        private readonly Module _audio = new Module { Title = "Audio Listeners" };
        private readonly Module _scripts = new Module { Title = "Missing Scripts" };
        private readonly Module _tmp = new Module { Title = "TextMesh Pro" };
        private readonly Module _anim = new Module { Title = "Animator Transitions" };
        private readonly Module _legacy = new Module { Title = "Legacy Plugin Metadata" };
        private readonly Module _iap = new Module { Title = "Unused IAP Buttons" };
        private readonly Module _findings = new Module { Title = "Problems Unity Reported", CleanText = "None captured" };
        private readonly Module _code = new Module { Title = "Obsolete API Usage in Code" };

        private VisualElement _suggestionsBox;
        private bool _showFindingsFixResult;

        private VisualElement _actionRow;
        private VisualElement _content;
        private Label _healthLabel;
        private Label _statusLabel;

        // Progress panel
        private VisualElement _progressPanel;
        private Label _progressTitle;
        private Label _progressStage;
        private Label _progressDetail;
        private Label _progressTime;
        private ProgressBar _progressBar;
        private Button _cancelButton;
        private CleanupProgress _boundProgress;

        public CleanupSuiteView()
        {
            BuildUI();

            RegisterCallback<DetachFromPanelEvent>(_ => UnbindProgress());

            // Findings captured earlier (for example by a menu-item scan) are shown right away.
            if (ConsoleFindings.All.Count > 0)
                RefreshFindings();

            // A task started before this window was (re)opened keeps showing its progress here.
            if (CleanupRunner.IsBusy)
                BindProgress(CleanupRunner.Current);
        }

        #region Layout

        private void BuildUI()
        {
            style.flexGrow = 1;

            // ---- 1. Action bar ----
            var topBar = new VisualElement();
            topBar.AddToClassList("cleanup-topbar");

            _actionRow = new VisualElement();
            _actionRow.AddToClassList("cleanup-action-row");
            _actionRow.Add(UnityUtilsUIStyle.CreateButton("Scan Everything", "btn-primary", StartFullScan));
            _actionRow.Add(UnityUtilsUIStyle.CreateButton("Fix All Recommended", "btn-secondary", StartFixAllRecommended));

            _healthLabel = new Label("Nothing scanned yet. Click 'Scan Everything' to check the whole project.");
            _healthLabel.AddToClassList("cleanup-health");
            _actionRow.Add(_healthLabel);
            topBar.Add(_actionRow);

            var includeSamples = new Toggle("Include third-party samples and demos (slower, noisier)")
            {
                value = CleanupAssetUtility.IncludeSamples,
                tooltip = "Off by default: folders named Examples, Samples, Demo, etc. are skipped, because opening old sample scenes floods the console with warnings."
            };
            includeSamples.AddToClassList("cleanup-option");
            includeSamples.RegisterValueChangedCallback(evt => CleanupAssetUtility.IncludeSamples = evt.newValue);
            topBar.Add(includeSamples);

            _suggestionsBox = new VisualElement();
            _suggestionsBox.AddToClassList("cleanup-suggestions");
            _suggestionsBox.style.display = DisplayStyle.None;
            topBar.Add(_suggestionsBox);

            // ---- 2. Progress panel (hidden until a task runs) ----
            BuildProgressPanel();
            topBar.Add(_progressPanel);
            Add(topBar);

            // ---- 3. Cleaners grouped by topic ----
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            _content = scroll;

            AddSection(scroll, "Problems Unity Reported");
            scroll.Add(BuildCard(_findings,
                "While scanning, the console fills with warnings about old assets. They are collected here, explained, and fixed by re-saving the affected files.",
                bar =>
                {
                    bar.Add(UnityUtilsUIStyle.CreateButton("Fix: Re-save Affected Assets", "btn-primary", ConfirmAndFixFindings));
                    bar.Add(UnityUtilsUIStyle.CreateButton("Clear List", "btn-secondary", () =>
                    {
                        ConsoleFindings.Clear();
                        RefreshFindings();
                    }));
                }));

            AddSection(scroll, "Scenes & Prefabs");
            scroll.Add(BuildCard(_audio,
                "Finds scenes and prefabs that contain more than one AudioListener.",
                bar =>
                {
                    bar.Add(UnityUtilsUIStyle.CreateButton("Scan", "btn-secondary", () => StartTask("Scanning AudioListeners", AudioScanRoutine)));
                    bar.Add(UnityUtilsUIStyle.CreateButton("Fix: Remove Duplicates", "btn-primary", () => StartTask("Removing duplicate AudioListeners", AudioFixRoutine)));
                    bar.Add(UnityUtilsUIStyle.CreateButton("Advanced: Keep Only Bootstrap Listener", "btn-danger", () => StartTask("Removing non-bootstrap AudioListeners", AudioRemoveNonBootstrapRoutine)));
                }));
            scroll.Add(BuildCard(_scripts,
                "Finds and removes MonoBehaviour components whose script is missing.",
                bar =>
                {
                    bar.Add(UnityUtilsUIStyle.CreateButton("Scan", "btn-secondary", () => StartTask("Scanning missing scripts", ScriptsScanRoutine)));
                    bar.Add(UnityUtilsUIStyle.CreateButton("Fix: Remove Missing Scripts", "btn-primary", () => StartTask("Removing missing scripts", ScriptsFixRoutine)));
                }));

            AddSection(scroll, "Text & Animation");
            scroll.Add(BuildCard(_tmp,
                "Removes redundant CanvasRenderers on world-space text and obsolete TextContainers.",
                bar =>
                {
                    bar.Add(UnityUtilsUIStyle.CreateButton("Scan", "btn-secondary", () => StartTask("Scanning TextMesh Pro", TmpScanRoutine)));
                    bar.Add(UnityUtilsUIStyle.CreateButton("Fix: CanvasRenderers", "btn-primary", () => StartTask("Removing redundant CanvasRenderers", TmpCanvasFixRoutine)));
                    bar.Add(UnityUtilsUIStyle.CreateButton("Fix: TextContainers", "btn-primary", () => StartTask("Removing obsolete TextContainers", TmpContainerFixRoutine)));
                }));
            scroll.Add(BuildCard(_anim,
                "Audits Animator transitions that have neither a condition nor an exit time.",
                bar =>
                {
                    bar.Add(UnityUtilsUIStyle.CreateButton("Scan", "btn-secondary", () => StartTask("Auditing Animator transitions", AnimScanRoutine)));
                    bar.Add(UnityUtilsUIStyle.CreateButton("Fix: Enable Exit Time", "btn-primary", () => StartTask("Enabling Exit Time", p => AnimFixRoutine(p, AnimatorTransitionCleaner.FixMode.EnableExitTime))));
                    bar.Add(UnityUtilsUIStyle.CreateButton("Advanced: Remove Invalid Transitions", "btn-danger", () => StartTask("Removing invalid transitions", p => AnimFixRoutine(p, AnimatorTransitionCleaner.FixMode.Remove))));
                }));

            AddSection(scroll, "Project Modernization");
            scroll.Add(BuildCard(_legacy,
                "Upgrades legacy plugin .meta files (PluginImporter version 1) so Unity 6 stops warning about them.",
                bar =>
                {
                    bar.Add(UnityUtilsUIStyle.CreateButton("Scan", "btn-secondary", () => StartTask("Scanning plugin metadata", LegacyScanRoutine)));
                    bar.Add(UnityUtilsUIStyle.CreateButton("Fix: Upgrade Plugin Metadata", "btn-primary", () => StartTask("Upgrading plugin metadata", LegacyPluginFixRoutine)));
                    bar.Add(UnityUtilsUIStyle.CreateButton("Advanced: Resave Entire Project", "btn-danger", ConfirmAndModernizeProject));
                }));

            AddSection(scroll, "Source Code");
            scroll.Add(BuildCard(_code,
                "Rewrites code that triggers compiler warnings: obsolete FindObjectOfType / FindObjectsOfType calls and DOTween fields that Unity cannot serialize ([System.NonSerialized]). Only the folder below is touched.",
                bar =>
                {
                    var folder = new TextField("Folder") { value = CodeModernizer.ScopeFolder };
                    folder.AddToClassList("cleanup-folder-field");
                    folder.tooltip = "Only scripts under this folder are scanned and edited. Third-party code outside it is never touched.";
                    folder.RegisterValueChangedCallback(evt => CodeModernizer.ScopeFolder = evt.newValue);
                    bar.Add(folder);

                    bar.Add(UnityUtilsUIStyle.CreateButton("Scan", "btn-secondary", () => StartTask("Scanning source code", CodeScanRoutine)));
                    bar.Add(UnityUtilsUIStyle.CreateButton("Fix: Rewrite Code", "btn-primary", ConfirmAndFixCode));
                }));

            AddSection(scroll, "Purchasing");
            scroll.Add(BuildCard(_iap,
                "Finds Codeless IAP buttons that have no events wired up.",
                bar =>
                {
#if WAGENHEIMER_UNITYUTILS_IAP
                    bar.Add(UnityUtilsUIStyle.CreateButton("Scan", "btn-secondary", () => StartTask("Scanning IAP buttons", IapScanRoutine)));
                    bar.Add(UnityUtilsUIStyle.CreateButton("Fix: Remove Unused Buttons", "btn-primary", () => StartTask("Removing unused IAP buttons", IapFixRoutine)));
#else
                    var notice = new Label("IAP support is not enabled in this project (com.unity.purchasing not present).");
                    notice.AddToClassList("card-subtitle");
                    bar.Add(notice);
#endif
                }));

            RegisterFixers();
            Add(scroll);

            // ---- 4. Status footer ----
            _statusLabel = new Label("Ready.");
            _statusLabel.AddToClassList("status-box");
            Add(_statusLabel);
        }

        private void RegisterFixers()
        {
            Register(_audio, "Remove duplicate AudioListeners", "Removing duplicate AudioListeners", AudioFixRoutine);
            Register(_scripts, "Remove missing scripts", "Removing missing scripts", ScriptsFixRoutine);
            Register(_tmp, "Fix TextMesh Pro redundancies", "Fixing TextMesh Pro redundancies", TmpFixAllRoutine);
            Register(_anim, "Enable Exit Time on transitions", "Enabling Exit Time",
                p => AnimFixRoutine(p, AnimatorTransitionCleaner.FixMode.EnableExitTime));
            Register(_legacy, "Upgrade legacy plugin metadata", "Upgrading plugin metadata", LegacyPluginFixRoutine);
            _code.FixLabel = "Rewrite obsolete code (review the diff in version control)";
            _code.FixAction = ConfirmAndFixCode;
#if WAGENHEIMER_UNITYUTILS_IAP
            Register(_iap, "Remove unused IAP buttons", "Removing unused IAP buttons", IapFixRoutine);
#endif
        }

        private static void Register(Module module, string label, string title, Func<CleanupProgress, IEnumerator> routine)
        {
            module.FixLabel = label;
            module.FixTitle = title;
            module.FixRoutine = routine;
        }

        private void BuildProgressPanel()
        {
            _progressPanel = new VisualElement();
            _progressPanel.AddToClassList("progress-panel");
            _progressPanel.style.display = DisplayStyle.None;

            var headerRow = new VisualElement();
            headerRow.AddToClassList("progress-header");

            _progressTitle = new Label();
            _progressTitle.AddToClassList("progress-title");
            headerRow.Add(_progressTitle);

            _cancelButton = UnityUtilsUIStyle.CreateButton("Cancel", "btn-danger", () =>
            {
                CleanupRunner.Current?.RequestCancel();
                _cancelButton.SetEnabled(false);
                _progressStage.text = "Cancelling after the current asset...";
            });
            headerRow.Add(_cancelButton);
            _progressPanel.Add(headerRow);

            _progressStage = new Label();
            _progressStage.AddToClassList("progress-stage");
            _progressPanel.Add(_progressStage);

            _progressBar = new ProgressBar { lowValue = 0f, highValue = 100f };
            _progressBar.AddToClassList("progress-bar");
            _progressPanel.Add(_progressBar);

            _progressDetail = new Label();
            _progressDetail.AddToClassList("progress-detail");
            _progressPanel.Add(_progressDetail);

            _progressTime = new Label();
            _progressTime.AddToClassList("progress-time");
            _progressPanel.Add(_progressTime);
        }

        private static void AddSection(VisualElement parent, string title)
        {
            var label = new Label(title);
            label.AddToClassList("section-title");
            parent.Add(label);
        }

        private VisualElement BuildCard(Module module, string description, Action<VisualElement> addButtons)
        {
            var card = new VisualElement();
            card.AddToClassList("card");

            var header = new VisualElement();
            header.AddToClassList("card-header");

            var title = new Label(module.Title);
            title.AddToClassList("card-title");
            header.Add(title);

            module.Badge = UnityUtilsUIStyle.CreateBadge("Not scanned", "badge-info");
            header.Add(module.Badge);
            card.Add(header);

            var subtitle = new Label(description);
            subtitle.AddToClassList("card-subtitle");
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            subtitle.style.marginBottom = 8;
            card.Add(subtitle);

            var bar = new VisualElement();
            bar.AddToClassList("action-toolbar");
            bar.style.marginTop = 0;
            bar.style.borderTopWidth = 0;
            addButtons(bar);
            card.Add(bar);

            module.Results = new VisualElement();
            module.Results.AddToClassList("scan-result-panel");
            module.Results.style.display = DisplayStyle.None;
            card.Add(module.Results);

            return card;
        }

        #endregion

        #region Task orchestration

        private void StartTask(string title, Func<CleanupProgress, IEnumerator> routine)
        {
            if (CleanupRunner.IsBusy)
            {
                SetStatus("Another cleanup task is still running. Wait for it to finish or cancel it.");
                return;
            }

            var progress = CleanupRunner.Start(title, routine, OnTaskFinished);
            if (progress != null)
                BindProgress(progress);
        }

        private void BindProgress(CleanupProgress progress)
        {
            UnbindProgress();
            if (progress == null) return;

            _boundProgress = progress;
            _boundProgress.Changed += RefreshProgressPanel;

            _progressPanel.style.display = DisplayStyle.Flex;
            _cancelButton.SetEnabled(true);
            SetInteractive(false);
            RefreshProgressPanel();
        }

        private void UnbindProgress()
        {
            if (_boundProgress != null)
                _boundProgress.Changed -= RefreshProgressPanel;

            _boundProgress = null;
        }

        private void RefreshProgressPanel()
        {
            var progress = _boundProgress;
            if (progress == null) return;

            _progressTitle.text = progress.Title;

            _progressStage.text = progress.StageCount > 1
                ? $"Step {progress.StageIndex + 1} of {progress.StageCount}  -  {progress.Stage}"
                : progress.Stage;

            _progressBar.value = progress.Fraction * 100f;
            _progressBar.title = progress.ItemsTotal > 0
                ? $"{progress.ItemsDone} / {progress.ItemsTotal}"
                : string.Empty;

            _progressDetail.text = progress.Detail;

            var eta = progress.EtaSeconds;
            _progressTime.text = eta.HasValue
                ? $"Elapsed {CleanupProgress.FormatDuration(progress.ElapsedSeconds)}  -  about {CleanupProgress.FormatDuration(eta.Value)} left"
                : $"Elapsed {CleanupProgress.FormatDuration(progress.ElapsedSeconds)}";
        }

        private void OnTaskFinished(CleanupProgress progress)
        {
            UnbindProgress();
            _progressPanel.style.display = DisplayStyle.None;
            SetInteractive(true);

            if (_showFindingsFixResult)
                _showFindingsFixResult = false; // Keep the "re-saved" result card visible.
            else
                RefreshFindings();

            RefreshHealth();

            if (progress.Failed)
                SetStatus($"'{progress.Title}' failed. See the Console for details.");
            else if (progress.WasCancelled)
                SetStatus($"'{progress.Title}' was cancelled. Anything already processed was kept; results show what was done so far.");
        }

        private void SetInteractive(bool enabled)
        {
            _actionRow.SetEnabled(enabled);
            _content.SetEnabled(enabled);
        }

        #endregion

        #region Scan tasks

        private void StartFullScan()
        {
            StartTask("Scanning the whole project", FullScanRoutine);
        }

        private IEnumerator FullScanRoutine(CleanupProgress p)
        {
#if WAGENHEIMER_UNITYUTILS_IAP
            p.SetStageCount(8);
#else
            p.SetStageCount(7);
#endif
            yield return AudioScanRoutine(p);
            if (p.CancelRequested) yield break;

            yield return ScriptsScanRoutine(p);
            if (p.CancelRequested) yield break;

            yield return TmpScanRoutine(p);
            if (p.CancelRequested) yield break;

            yield return AnimScanRoutine(p);
            if (p.CancelRequested) yield break;

            yield return LegacyScanRoutine(p);
            if (p.CancelRequested) yield break;

            yield return CodeScanRoutine(p);
#if WAGENHEIMER_UNITYUTILS_IAP
            if (p.CancelRequested) yield break;

            yield return IapScanRoutine(p);
#endif
            SetStatus("Full project audit complete. Review the badge on each card, then use its fix button.");
        }

        private IEnumerator AudioScanRoutine(CleanupProgress p)
        {
            var scan = default(AudioListenerCleaner.ScanResult);
            yield return AudioListenerCleaner.ScanProjectRoutine(p, r => scan = r);
            if (p.CancelRequested) yield break;

            SetIssues(_audio, scan.DuplicateCount);

            RenderScanResults(
                _audio,
                issueCount: scan.DuplicateCount,
                successMsg: "No duplicate AudioListeners found. Your scenes and prefabs have clean audio!",
                diagTitle: "Multiple AudioListeners Detected",
                diagDesc: $"Found {scan.DuplicateCount} duplicate AudioListener(s) in {scan.AffectedScenesCount} scene(s) and {scan.AffectedPrefabsCount} prefab(s). Having more than one listener in a scene causes recurring console warnings and splits audio spatialization.",
                recDesc: "Recommendation: Keep only the main AudioListener (in the bootstrap scene / main camera) and remove the duplicates from the other scenes and prefabs.",
                fixBtnText: "Fix Now (Remove Duplicates)",
                fixRoutine: AudioFixRoutine,
                fixTitle: "Removing duplicate AudioListeners",
                items: scan.Items);

            SetStatus($"Audio scan: {scan.DuplicateCount} duplicate AudioListener(s) in {scan.AffectedScenesCount} scene(s) and {scan.AffectedPrefabsCount} prefab(s).");
        }

        private IEnumerator ScriptsScanRoutine(CleanupProgress p)
        {
            var scan = default(MissingScriptCleaner.ScanResult);
            yield return MissingScriptCleaner.ScanProjectRoutine(p, r => scan = r);
            if (p.CancelRequested) yield break;

            SetIssues(_scripts, scan.MissingScriptCount);

            RenderScanResults(
                _scripts,
                issueCount: scan.MissingScriptCount,
                successMsg: "No missing scripts found. All MonoBehaviour components are intact!",
                diagTitle: "Components With Missing Scripts Detected",
                diagDesc: $"Found {scan.MissingScriptCount} MonoBehaviour component(s) with a missing script in {scan.AffectedScenesCount} scene(s) and {scan.AffectedPrefabsCount} prefab(s). Missing scripts break references, slow down deserialization and produce persistent 'The referenced script on this Behaviour is missing!' warnings.",
                recDesc: "Recommendation: Remove the orphaned entries of corrupted or uninstalled MonoBehaviours to clean up serialization.",
                fixBtnText: "Remove All Missing Scripts",
                fixRoutine: ScriptsFixRoutine,
                fixTitle: "Removing missing scripts",
                items: scan.Items);

            SetStatus($"Missing scripts scan: {scan.MissingScriptCount} missing script(s) in {scan.AffectedScenesCount} scene(s) and {scan.AffectedPrefabsCount} prefab(s).");
        }

        private IEnumerator TmpScanRoutine(CleanupProgress p)
        {
            var rScan = default(TMPCanvasRendererCleaner.ScanResult);
            var cScan = default(TMPTextContainerCleaner.ScanResult);

            yield return TMPCanvasRendererCleaner.ScanProjectRoutine(p, r => rScan = r);
            if (p.CancelRequested) yield break;

            yield return TMPTextContainerCleaner.ScanProjectRoutine(p, r => cScan = r);
            if (p.CancelRequested) yield break;

            var total = rScan.RedundantRendererCount + cScan.ObsoleteContainerCount;
            SetIssues(_tmp, total);

            var combinedItems = new List<CleanupIssueItem>();
            if (rScan.Items != null) combinedItems.AddRange(rScan.Items);
            if (cScan.Items != null) combinedItems.AddRange(cScan.Items);

            RenderScanResults(
                _tmp,
                issueCount: total,
                successMsg: "No redundant TextMeshPro components found. Your 3D and UI texts are clean!",
                diagTitle: "Redundant / Obsolete TextMeshPro Components",
                diagDesc: $"Detected {rScan.RedundantRendererCount} unnecessary CanvasRenderer(s) on world-space texts and {cScan.ObsoleteContainerCount} legacy TextContainer(s). World-space TextMeshPro does not need a CanvasRenderer (it logs runtime warnings) and TextContainer is obsolete in current versions.",
                recDesc: "Recommendation: Safely remove the redundant CanvasRenderers and legacy TextContainers.",
                fixBtnText: "Fix All TMP Redundancies",
                fixRoutine: TmpFixAllRoutine,
                fixTitle: "Fixing TextMesh Pro redundancies",
                items: combinedItems);

            SetStatus($"TMP scan: {rScan.RedundantRendererCount} redundant CanvasRenderer(s), {cScan.ObsoleteContainerCount} obsolete TextContainer(s).");
        }

        private IEnumerator AnimScanRoutine(CleanupProgress p)
        {
            p.NextStage("Reading Animator controllers", 1);
            p.SetDetail("Looking through all Animator controllers...");
            yield return null; // Let the progress panel paint before the (synchronous) scan starts.

            var scan = AnimatorTransitionCleaner.ScanProject();
            p.Advance("Done");
            SetIssues(_anim, scan.InvalidTransitionCount);

            RenderScanResults(
                _anim,
                issueCount: scan.InvalidTransitionCount,
                successMsg: "All Animator Controller transitions are valid and healthy!",
                diagTitle: "Animator Transitions Without Condition or Exit Time",
                diagDesc: $"Found {scan.InvalidTransitionCount} invalid Animator transition(s) in {scan.AffectedControllersCount} controller(s). AnyState transitions with no Exit Time and no condition are never fired by the animation engine and spam the console with warnings.",
                recDesc: "Recommendation: Enable 'Has Exit Time' with exitTime = 1.0 so the transition plays normally after the animation ends.",
                fixBtnText: "Enable Exit Time (Recommended)",
                fixRoutine: pr => AnimFixRoutine(pr, AnimatorTransitionCleaner.FixMode.EnableExitTime),
                fixTitle: "Enabling Exit Time",
                items: scan.Items);

            SetStatus($"Animator scan: {scan.InvalidTransitionCount} invalid transition(s) in {scan.AffectedControllersCount} controller(s).");
        }

        private IEnumerator LegacyScanRoutine(CleanupProgress p)
        {
            p.NextStage("Reading plugin metadata", 1);
            p.SetDetail("Checking the .meta file of every plugin...");
            yield return null;

            var scan = LegacyComponentCleaner.ScanProject();
            p.Advance("Done");
            SetIssues(_legacy, scan.LegacyPluginCount);

            RenderScanResults(
                _legacy,
                issueCount: scan.LegacyPluginCount,
                successMsg: "All plugin metadata is on version 2 (modern) and the project is up to date!",
                diagTitle: "Plugin Metadata on Legacy Version (Version 1)",
                diagDesc: $"Found {scan.LegacyPluginCount} legacy plugin .meta file(s) where PluginImporter is on version 1. Unity 6 requires version 2 and warns on every compile: 'contains a PluginImporter object at version 1, below the supported minimum (2)'.",
                recDesc: "Recommendation: Re-save the legacy plugin importers to raise serialization to version 2 without touching the binaries.",
                fixBtnText: "Re-save Legacy Plugin Metadata",
                fixRoutine: LegacyPluginFixRoutine,
                fixTitle: "Upgrading plugin metadata",
                items: scan.Items);

            SetStatus($"Legacy scan: {scan.LegacyPluginCount} legacy plugin meta file(s) found in the project.");
        }

#if WAGENHEIMER_UNITYUTILS_IAP
        private IEnumerator IapScanRoutine(CleanupProgress p)
        {
            var scan = default(UnusedIAPButtonCleaner.ScanResult);
            yield return UnusedIAPButtonCleaner.ScanProjectRoutine(p, r => scan = r);
            if (p.CancelRequested) yield break;

            SetIssues(_iap, scan.UnusedComponentCount);

            RenderScanResults(
                _iap,
                issueCount: scan.UnusedComponentCount,
                successMsg: "No unused IAP buttons found!",
                diagTitle: "Codeless IAP Buttons Without Configured Events",
                diagDesc: $"Found {scan.UnusedComponentCount} inactive/unused IAPButton component(s). IAPButton components without events fire automatic background listeners that pollute logs and consume check cycles.",
                recDesc: "Recommendation: Remove empty or unconfigured IAPButton components.",
                fixBtnText: "Remove Unused IAP Buttons",
                fixRoutine: IapFixRoutine,
                fixTitle: "Removing unused IAP buttons",
                items: scan.Items);

            SetStatus($"IAP scan: {scan.UnusedComponentCount} unused IAP button(s) detected.");
        }
#endif

        private IEnumerator CodeScanRoutine(CleanupProgress p)
        {
            var scan = default(CodeModernizer.ScanResult);
            yield return CodeModernizer.ScanRoutine(p, r => scan = r);
            if (p.CancelRequested) yield break;

            SetIssues(_code, scan.EditCount);

            // Thousands of rows would make the list unusable; the full list is one Fix away anyway.
            const int maxRows = 200;
            var shown = scan.Items;
            var truncated = shown != null && shown.Count > maxRows;
            if (truncated)
                shown = shown.GetRange(0, maxRows);

            RenderScanResults(
                _code,
                issueCount: scan.EditCount,
                successMsg: $"No obsolete API usage found under {CodeModernizer.ScopeFolder}.",
                diagTitle: "Obsolete API Usage Found in Code",
                diagDesc: $"Found {scan.EditCount} change(s) in {scan.AffectedFiles} script(s): obsolete FindObjectOfType / FindObjectsOfType calls and DOTween fields Unity cannot serialize (they get [System.NonSerialized], which matches what Unity already does)." +
                          (truncated ? $" Showing the first {maxRows}." : string.Empty) +
                          (scan.SkippedFiles > 0 ? $" {scan.SkippedFiles} file(s) were skipped (not valid UTF-8)." : string.Empty),
                recDesc: "Recommendation: apply the rewrite, let Unity recompile, then review the diff in version control. Code inside UNITY_* conditional blocks is never changed.",
                fixBtnText: "Rewrite Code",
                fixRoutine: null,
                fixTitle: null,
                items: shown,
                customFix: ConfirmAndFixCode);

            SetStatus($"Code scan: {scan.EditCount} change(s) available in {scan.AffectedFiles} script(s) under {CodeModernizer.ScopeFolder}.");
        }

        #endregion

        #region Fix tasks

        private void ConfirmAndFixCode()
        {
            if (!EditorUtility.DisplayDialog("Rewrite Code",
                    $"This rewrites scripts under '{CodeModernizer.ScopeFolder}': obsolete FindObjectOfType / FindObjectsOfType calls are " +
                    "migrated and unserializable DOTween fields get [System.NonSerialized]. Code inside UNITY_* conditional " +
                    "blocks is not changed.\n\nMake sure your work is committed first so you can review the diff. " +
                    "Unity will recompile afterwards.",
                    "Rewrite", "Cancel"))
                return;

            StartTask("Rewriting source code", CodeFixRoutine);
        }

        private IEnumerator CodeFixRoutine(CleanupProgress p)
        {
            var before = _code.Issues;
            var res = default(CodeModernizer.CleanResult);
            yield return CodeModernizer.FixRoutine(p, r => res = r);

            SetIssues(_code, Math.Max(0, before - res.ChangesApplied));

            RenderDiffCard(
                _code,
                title: "✓ Source Code Rewritten",
                beforeCount: before > 0 ? before : res.ChangesApplied,
                afterCount: Math.Max(0, before - res.ChangesApplied),
                benefitDesc: $"Applied {res.ChangesApplied} change(s) in {res.FilesChanged} script(s)." +
                             (res.FilesSkipped > 0 ? $" {res.FilesSkipped} file(s) were skipped (not valid UTF-8)." : string.Empty) +
                             " Unity is recompiling; review the diff in version control.",
                modifications: res.Modifications);

            SetStatus($"Rewrote {res.ChangesApplied} change(s) in {res.FilesChanged} script(s).");
        }

        private IEnumerator AudioFixRoutine(CleanupProgress p)
        {
            var before = _audio.Issues;
            var sceneRes = default(AudioListenerCleaner.CleanResult);
            var prefabRes = default(AudioListenerCleaner.CleanResult);

            p.EnsureStageCount(2);
            yield return AudioListenerCleaner.CleanAllScenesRoutine(p, removeAllNonPersistent: false, bootstrapSceneNameToSkip: null, r => sceneRes = r);
            if (!p.CancelRequested)
                yield return AudioListenerCleaner.CleanAllPrefabsRoutine(p, removeAllNonPersistent: false, r => prefabRes = r);

            var total = sceneRes.DuplicatesRemoved + prefabRes.DuplicatesRemoved;
            SetIssues(_audio, Math.Max(0, before - total));

            RenderDiffCard(
                _audio,
                title: "✓ Duplicate AudioListeners Removed",
                beforeCount: before > 0 ? before : total,
                afterCount: Math.Max(0, before - total),
                benefitDesc: $"Removed {total} duplicate AudioListener(s) in {sceneRes.ScenesModified} scene(s) and {prefabRes.PrefabsModified} prefab(s). Audio conflicts eliminated.",
                modifications: Merge(sceneRes.Modifications, prefabRes.Modifications));

            SetStatus($"Removed {total} duplicate AudioListener(s) ({sceneRes.ScenesModified} scene(s), {prefabRes.PrefabsModified} prefab(s)).");
        }

        private IEnumerator AudioRemoveNonBootstrapRoutine(CleanupProgress p)
        {
            var before = _audio.Issues;

            p.NextStage("Removing AudioListeners outside the bootstrap scene", 1);
            p.SetDetail("Working through scenes and prefabs...");
            yield return null;

            AudioListenerCleaner.RemoveAllNonBootstrapListeners();
            p.Advance("Done");

            SetIssues(_audio, 0);

            RenderDiffCard(
                _audio,
                title: "✓ Non-Bootstrap Listeners Removed",
                beforeCount: Math.Max(0, before),
                afterCount: 0,
                benefitDesc: "All AudioListeners outside the bootstrap scene were removed successfully. Audio is centralized in the bootstrap scene.",
                modifications: null);

            SetStatus("Removed all AudioListeners that do not belong to the bootstrap scene.");
        }

        private IEnumerator ScriptsFixRoutine(CleanupProgress p)
        {
            var before = _scripts.Issues;
            var sceneRes = default(MissingScriptCleaner.CleanResult);
            var prefabRes = default(MissingScriptCleaner.CleanResult);

            p.EnsureStageCount(2);
            yield return MissingScriptCleaner.CleanAllScenesRoutine(p, r => sceneRes = r);
            if (!p.CancelRequested)
                yield return MissingScriptCleaner.CleanAllPrefabsRoutine(p, r => prefabRes = r);

            var total = sceneRes.MissingScriptsRemoved + prefabRes.MissingScriptsRemoved;
            SetIssues(_scripts, Math.Max(0, before - total));

            RenderDiffCard(
                _scripts,
                title: "✓ Missing Scripts Removed Successfully",
                beforeCount: before > 0 ? before : total,
                afterCount: Math.Max(0, before - total),
                benefitDesc: $"Removed {total} MonoBehaviour component(s) with a missing script in {sceneRes.ScenesModified} scene(s) and {prefabRes.PrefabsModified} prefab(s). Serialization cleaned and console warnings suppressed.",
                modifications: Merge(sceneRes.Modifications, prefabRes.Modifications));

            SetStatus($"Removed {total} missing script(s) ({sceneRes.ScenesModified} scene(s), {prefabRes.PrefabsModified} prefab(s)).");
        }

        private IEnumerator TmpFixAllRoutine(CleanupProgress p)
        {
            p.EnsureStageCount(2);
            yield return TmpCanvasFixRoutine(p);
            if (p.CancelRequested) yield break;

            yield return TmpContainerFixRoutine(p);
        }

        private IEnumerator TmpCanvasFixRoutine(CleanupProgress p)
        {
            var before = _tmp.Issues;
            var res = default(TMPCanvasRendererCleaner.CleanResult);
            yield return TMPCanvasRendererCleaner.CleanAllRoutine(p, r => res = r);

            SetIssues(_tmp, Math.Max(0, before - res.ObjectsFixed));

            RenderDiffCard(
                _tmp,
                title: "✓ Redundant CanvasRenderers Removed",
                beforeCount: before > 0 ? before : res.ObjectsFixed,
                afterCount: Math.Max(0, before - res.ObjectsFixed),
                benefitDesc: $"Removed {res.ObjectsFixed} redundant CanvasRenderer(s) on 3D texts ({res.ScenesChanged} scene(s), {res.PrefabsChanged} prefab(s)). 'Please remove the CanvasRenderer' warnings suppressed.",
                modifications: res.Modifications);

            SetStatus($"Fixed {res.ObjectsFixed} redundant CanvasRenderer(s) on 3D TextMeshPro.");
        }

        private IEnumerator TmpContainerFixRoutine(CleanupProgress p)
        {
            var before = _tmp.Issues;
            var res = default(TMPTextContainerCleaner.CleanResult);
            yield return TMPTextContainerCleaner.CleanAllRoutine(p, r => res = r);

            SetIssues(_tmp, Math.Max(0, before - res.ObjectsFixed));

            RenderDiffCard(
                _tmp,
                title: "✓ Obsolete TextContainers Removed",
                beforeCount: before > 0 ? before : res.ObjectsFixed,
                afterCount: Math.Max(0, before - res.ObjectsFixed),
                benefitDesc: $"Removed {res.ObjectsFixed} obsolete TextContainer(s) ({res.ScenesChanged} scene(s), {res.PrefabsChanged} prefab(s)).",
                modifications: res.Modifications);

            SetStatus($"Fixed {res.ObjectsFixed} obsolete TextContainer(s).");
        }

        private IEnumerator AnimFixRoutine(CleanupProgress p, AnimatorTransitionCleaner.FixMode mode)
        {
            var before = _anim.Issues;

            p.NextStage("Repairing Animator transitions", 1);
            p.SetDetail("Editing Animator controllers...");
            yield return null;

            var res = AnimatorTransitionCleaner.CleanAll(mode);
            p.Advance("Done");

            SetIssues(_anim, Math.Max(0, before - res.TransitionsFixed));

            RenderDiffCard(
                _anim,
                title: mode == AnimatorTransitionCleaner.FixMode.EnableExitTime ? "✓ Exit Time Enabled on Transitions" : "✓ Invalid Transitions Removed",
                beforeCount: before > 0 ? before : res.TransitionsFixed,
                afterCount: Math.Max(0, before - res.TransitionsFixed),
                benefitDesc: $"Fixed {res.TransitionsFixed} transition(s) in {res.ControllersModified} Animator Controller(s). 'doesn't have an Exit Time' warnings suppressed.",
                modifications: res.Modifications);

            SetStatus($"Fixed {res.TransitionsFixed} Animator transition(s).");
        }

        private IEnumerator LegacyPluginFixRoutine(CleanupProgress p)
        {
            var before = _legacy.Issues;
            var res = default(LegacyComponentCleaner.CleanResult);
            yield return LegacyComponentCleaner.ResaveLegacyPluginImportersRoutine(p, r => res = r);

            SetIssues(_legacy, Math.Max(0, before - res.PluginsUpdated));

            RenderDiffCard(
                _legacy,
                title: "✓ Plugin Metadata Upgraded to Version 2",
                beforeCount: before > 0 ? before : res.PluginsUpdated,
                afterCount: Math.Max(0, before - res.PluginsUpdated),
                benefitDesc: $"Re-saved and upgraded {res.PluginsUpdated} legacy plugin .meta file(s). The Unity 6 'contains a PluginImporter object at version 1' warning is gone.",
                modifications: res.Modifications);

            SetStatus($"Re-saved {res.PluginsUpdated} legacy plugin importer(s).");
        }

        private void ConfirmAndModernizeProject()
        {
            if (!EditorUtility.DisplayDialog("Resave Entire Project",
                    "This opens and resaves every scene and prefab, upgrades legacy plugin metadata and reserializes " +
                    "all ScriptableObjects. It can take a long time and touches many files.\n\n" +
                    "Make sure your work is committed first. You can cancel while it runs.",
                    "Proceed", "Cancel"))
                return;

            StartTask("Resaving the entire project", ModernizeProjectRoutine);
        }

        private IEnumerator ModernizeProjectRoutine(CleanupProgress p)
        {
            var before = _legacy.Issues;
            var res = default(LegacyComponentCleaner.CleanResult);
            yield return LegacyComponentCleaner.ModernizeProjectRoutine(p, r => res = r);

            SetIssues(_legacy, 0);

            RenderDiffCard(
                _legacy,
                title: "✓ Project Fully Modernized and Reserialized",
                beforeCount: Math.Max(0, before),
                afterCount: 0,
                benefitDesc: $"Resaved {res.ScenesUpdated} scene(s) and {res.PrefabsUpdated} prefab(s), upgraded {res.PluginsUpdated} plugin importer(s) and reserialized the remaining assets to the current Unity format.",
                modifications: res.Modifications);

            SetStatus("All scenes, prefabs and plugin metadata were re-saved and modernized.");
        }

#if WAGENHEIMER_UNITYUTILS_IAP
        private IEnumerator IapFixRoutine(CleanupProgress p)
        {
            var before = _iap.Issues;
            var res = default(UnusedIAPButtonCleaner.CleanResult);
            yield return UnusedIAPButtonCleaner.CleanAllRoutine(p, r => res = r);

            SetIssues(_iap, Math.Max(0, before - res.ObjectsFixed));

            RenderDiffCard(
                _iap,
                title: "✓ Codeless IAP Buttons Cleaned",
                beforeCount: before > 0 ? before : res.ObjectsFixed,
                afterCount: Math.Max(0, before - res.ObjectsFixed),
                benefitDesc: $"Removed {res.ObjectsFixed} inactive IAP button(s) with no bound events ({res.ScenesChanged} scene(s), {res.PrefabsChanged} prefab(s)).",
                modifications: res.Modifications);

            SetStatus($"Removed {res.ObjectsFixed} inactive IAPButton component(s).");
        }
#endif

        // "Fix All Recommended": every safe fix for the modules a scan found issues in, as one cancellable task.
        private void StartFixAllRecommended()
        {
            var steps = new List<KeyValuePair<string, Func<CleanupProgress, IEnumerator>>>();
            var summary = new List<string>();
            var stages = 0;

            if (_audio.Issues > 0)
            {
                steps.Add(Step("Removing duplicate AudioListeners", AudioFixRoutine));
                summary.Add($"- {_audio.Issues} duplicate AudioListener(s)");
                stages += 2;
            }

            if (_scripts.Issues > 0)
            {
                steps.Add(Step("Removing missing scripts", ScriptsFixRoutine));
                summary.Add($"- {_scripts.Issues} missing script(s)");
                stages += 2;
            }

            if (_tmp.Issues > 0)
            {
                steps.Add(Step("Fixing TextMesh Pro redundancies", TmpFixAllRoutine));
                summary.Add($"- {_tmp.Issues} TextMesh Pro redundancy(ies)");
                stages += 2;
            }

            if (_anim.Issues > 0)
            {
                steps.Add(Step("Enabling Exit Time", p => AnimFixRoutine(p, AnimatorTransitionCleaner.FixMode.EnableExitTime)));
                summary.Add($"- {_anim.Issues} Animator transition(s) (Exit Time will be enabled)");
                stages += 1;
            }

            if (_findings.Issues > 0 && FixableFindingAssets() > 0)
            {
                steps.Add(Step("Re-saving assets reported by Unity", FindingsFixRoutine));
                summary.Add($"- {FixableFindingAssets()} asset(s) Unity reported as outdated (will be re-saved)");
                stages += 2;
            }

            if (_legacy.Issues > 0)
            {
                steps.Add(Step("Upgrading plugin metadata", LegacyPluginFixRoutine));
                summary.Add($"- {_legacy.Issues} legacy plugin meta file(s)");
                stages += 1;
            }

#if WAGENHEIMER_UNITYUTILS_IAP
            if (_iap.Issues > 0)
            {
                steps.Add(Step("Removing unused IAP buttons", IapFixRoutine));
                summary.Add($"- {_iap.Issues} unused IAP button(s)");
                stages += 1;
            }
#endif

            if (steps.Count == 0)
            {
                EditorUtility.DisplayDialog("Fix All Recommended",
                    "There is nothing to fix yet. Click 'Scan Everything' first; modules with issues will then be fixed here.",
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Fix All Recommended",
                    "These fixes will be applied:\n\n" + string.Join("\n", summary) +
                    "\n\nMake sure your work is committed first. You can cancel while it runs.",
                    "Fix Everything", "Cancel"))
                return;

            StartTask("Applying all recommended fixes", p => RunStepsRoutine(p, steps, stages));
        }

        private static KeyValuePair<string, Func<CleanupProgress, IEnumerator>> Step(string name, Func<CleanupProgress, IEnumerator> routine)
        {
            return new KeyValuePair<string, Func<CleanupProgress, IEnumerator>>(name, routine);
        }

        private IEnumerator RunStepsRoutine(CleanupProgress p,
            List<KeyValuePair<string, Func<CleanupProgress, IEnumerator>>> steps, int stages)
        {
            p.SetStageCount(stages);

            foreach (var step in steps)
            {
                if (p.CancelRequested) yield break;

                yield return step.Value(p);
            }

            SetStatus("All recommended fixes were applied.");
        }

        #endregion

        #region Console findings

        private static int FixableFindingAssets()
        {
            var paths = new HashSet<string>();
            foreach (var finding in ConsoleFindings.All)
            {
                if (finding.FixableByResave && !string.IsNullOrEmpty(finding.AssetPath))
                    paths.Add(finding.AssetPath);
            }

            return paths.Count;
        }

        private void RefreshFindings()
        {
            var all = ConsoleFindings.All;
            SetIssues(_findings, all.Count);

            if (all.Count == 0)
            {
                _findings.Results.Clear();
                _findings.Results.style.display = DisplayStyle.None;
                return;
            }

            var summary = new System.Text.StringBuilder();
            foreach (var group in System.Linq.Enumerable.GroupBy(all, f => f.Kind))
            {
                summary.AppendLine($"• {System.Linq.Enumerable.Count(group)} x {ConsoleFindings.Title(group.Key)}");
                summary.AppendLine($"   {ConsoleFindings.Explanation(group.Key)}");
            }

            var fixable = FixableFindingAssets();
            var items = new List<CleanupIssueItem>();
            foreach (var finding in all)
            {
                var path = finding.AssetPath ?? string.Empty;
                items.Add(new CleanupIssueItem
                {
                    AssetPath = path,
                    TargetName = path,
                    Description = $"{ConsoleFindings.Title(finding.Kind)} - {finding.Message}",
                    Category = "Console",
                    IsScene = path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase),
                    IsPrefab = path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase),
                    Count = 1
                });
            }

            RenderScanResults(
                _findings,
                issueCount: all.Count,
                successMsg: "Nothing reported.",
                diagTitle: "Problems Reported by Unity While Scanning",
                diagDesc: summary.ToString().TrimEnd(),
                recDesc: fixable > 0
                    ? $"Recommendation: re-save the {fixable} affected asset(s). Only their serialization format changes; content stays the same."
                    : "These need manual attention; there is nothing to re-save.",
                fixBtnText: fixable > 0 ? "Re-save Affected Assets" : null,
                fixRoutine: null,
                fixTitle: null,
                items: items,
                customFix: fixable > 0 ? (Action)ConfirmAndFixFindings : null);
        }

        private void ConfirmAndFixFindings()
        {
            var fixable = FixableFindingAssets();
            if (fixable == 0)
            {
                SetStatus("Nothing to re-save: no fixable problems were captured yet. Run a scan first.");
                return;
            }

            if (!EditorUtility.DisplayDialog("Re-save Affected Assets",
                    $"This opens and re-saves {fixable} asset(s) that Unity reported as outdated. " +
                    "Only the file format is upgraded.\n\nMake sure your work is committed first. You can cancel while it runs.",
                    "Re-save", "Cancel"))
                return;

            StartTask("Re-saving assets reported by Unity", FindingsFixRoutine);
        }

        private IEnumerator FindingsFixRoutine(CleanupProgress p)
        {
            var before = ConsoleFindings.All.Count;
            var resaved = 0;

            yield return ConsoleFindings.FixRoutine(p, n => resaved = n);

            var remaining = ConsoleFindings.All.Count;
            SetIssues(_findings, remaining);
            _showFindingsFixResult = true;

            RenderDiffCard(
                _findings,
                title: "✓ Affected Assets Re-saved",
                beforeCount: before,
                afterCount: remaining,
                benefitDesc: $"Re-saved {resaved} asset(s). {remaining} problem(s) remain" +
                             (remaining > 0 ? " (these need manual attention)." : "; Unity should no longer warn about them."),
                modifications: null);

            SetStatus($"Re-saved {resaved} asset(s) that Unity reported as outdated.");
        }

        #endregion

        #region Result rendering

        private void SetIssues(Module module, int issues)
        {
            module.Issues = issues;

            string text;
            string style;
            if (issues < 0)
            {
                text = "Not scanned";
                style = "badge-info";
            }
            else if (issues == 0)
            {
                text = module.CleanText;
                style = "badge-pass";
            }
            else
            {
                text = $"{issues} issue(s)";
                style = "badge-warning";
            }

            module.Badge.text = text;
            module.Badge.RemoveFromClassList("badge-info");
            module.Badge.RemoveFromClassList("badge-pass");
            module.Badge.RemoveFromClassList("badge-warning");
            module.Badge.AddToClassList(style);

            RefreshHealth();
        }

        private void RefreshHealth()
        {
            if (_healthLabel == null) return;

            var modules = new[] { _audio, _scripts, _tmp, _anim, _legacy, _iap, _findings, _code };
            var scanned = 0;
            var withIssues = 0;
            var totalIssues = 0;

            foreach (var module in modules)
            {
                if (module.Issues < 0) continue;

                scanned++;
                if (module.Issues > 0)
                {
                    withIssues++;
                    totalIssues += module.Issues;
                }
            }

            RebuildSuggestions(modules);

            _healthLabel.text = scanned == 0
                ? "Nothing scanned yet. Click 'Scan Everything' to check the whole project."
                : withIssues == 0
                    ? $"{scanned} module(s) scanned: no issues found."
                    : $"{scanned} module(s) scanned: {totalIssues} issue(s) in {withIssues} module(s).";
        }

        // One line per module that has issues, with a button for its recommended fix.
        private void RebuildSuggestions(Module[] modules)
        {
            if (_suggestionsBox == null) return;

            _suggestionsBox.Clear();
            var any = false;

            foreach (var module in modules)
            {
                if (module.Issues <= 0) continue;

                Func<CleanupProgress, IEnumerator> routine = module.FixRoutine;
                string title = module.FixTitle;
                string label = module.FixLabel;
                Action click = null;

                if (module == _findings)
                {
                    var fixable = FixableFindingAssets();
                    if (fixable == 0) continue;

                    label = $"Re-save {fixable} asset(s) Unity complained about";
                    click = ConfirmAndFixFindings;
                }
                else if (module.FixAction != null)
                {
                    click = module.FixAction;
                }
                else if (routine == null)
                {
                    continue;
                }

                if (!any)
                {
                    var heading = new Label("Suggested next steps");
                    heading.AddToClassList("suggestion-heading");
                    _suggestionsBox.Add(heading);
                    any = true;
                }

                var row = new VisualElement();
                row.AddToClassList("suggestion-row");

                var text = new Label($"{module.Title}: {module.Issues} issue(s)  -  {label}");
                text.AddToClassList("suggestion-text");
                row.Add(text);

                row.Add(UnityUtilsUIStyle.CreateButton("Fix", "btn-primary", click ?? (() => StartTask(title, routine))));
                _suggestionsBox.Add(row);
            }

            _suggestionsBox.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static List<CleanupModificationRecord> Merge(List<CleanupModificationRecord> first, List<CleanupModificationRecord> second)
        {
            var all = new List<CleanupModificationRecord>();
            if (first != null) all.AddRange(first);
            if (second != null) all.AddRange(second);
            return all;
        }

        private void RenderScanResults(
            Module module,
            int issueCount,
            string successMsg,
            string diagTitle,
            string diagDesc,
            string recDesc,
            string fixBtnText,
            Func<CleanupProgress, IEnumerator> fixRoutine,
            string fixTitle,
            List<CleanupIssueItem> items,
            Action customFix = null)
        {
            var panel = module.Results;
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

            // Recommended solution
            var solutionBox = new VisualElement();
            solutionBox.AddToClassList("solution-banner");

            var solTitle = new Label($"💡 {diagTitle} ({issueCount} found)");
            solTitle.AddToClassList("solution-title");
            solutionBox.Add(solTitle);

            var solDesc = new Label($"{diagDesc}\n\n{recDesc}");
            solDesc.AddToClassList("solution-desc");
            solutionBox.Add(solDesc);

            if ((fixRoutine != null || customFix != null) && !string.IsNullOrEmpty(fixBtnText))
            {
                var actionRow = new VisualElement();
                actionRow.AddToClassList("solution-action-row");

                var fixBtn = UnityUtilsUIStyle.CreateButton(fixBtnText, "btn-primary",
                    customFix ?? (() => StartTask(fixTitle, fixRoutine)));
                fixBtn.style.alignSelf = Align.FlexStart;
                actionRow.Add(fixBtn);

                solutionBox.Add(actionRow);
            }

            panel.Add(solutionBox);

            // Affected assets, each with a button that selects it in the Project window
            if (items == null || items.Count == 0) return;

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

                var assetPath = item.AssetPath;
                var pingBtn = new Button(() => CleanupAssetUtility.PingAsset(assetPath));
                pingBtn.text = "Show in Project";
                pingBtn.AddToClassList("btn-ping");
                row.Add(pingBtn);

                listContainer.Add(row);
            }

            panel.Add(listContainer);
        }

        private void RenderDiffCard(
            Module module,
            string title,
            int beforeCount,
            int afterCount,
            string benefitDesc,
            List<CleanupModificationRecord> modifications)
        {
            var panel = module.Results;
            panel.Clear();
            panel.style.display = DisplayStyle.Flex;

            var diffCard = new VisualElement();
            diffCard.AddToClassList("diff-card");

            var header = new VisualElement();
            header.AddToClassList("diff-header");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("diff-title");

            var badge = new Label(beforeCount > 0 ? $"{beforeCount} ➔ {afterCount} remaining" : "✓ Fully optimized");
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

        private void SetStatus(string message)
        {
            Debug.Log($"[CleanupSuite] {message}");

            if (_statusLabel != null)
                _statusLabel.text = message;
        }

        #endregion
    }
}
