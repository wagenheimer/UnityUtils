using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// UI Toolkit view for auditing and slimming oversized third-party asset packs
    /// (defaults to 2DxFX). Resolves real usage via GUID references, reports build
    /// footprint, applies in-place or extract-only clean-up, and ports used
    /// Built-in Render Pipeline shaders to URP.
    ///
    /// "Slim In-Place" and "Extract Used Only" are alternative strategies, not
    /// sequential steps: Extract relocates the referenced subset and deletes the
    /// original, after which the tool retargets the new "-Slim" folder. The view
    /// persists the working folder so it survives domain reloads.
    /// </summary>
    public class ThirdPartySlimmerView : VisualElement
    {
        private const string RootPrefKey = "Wagenheimer.UnityUtils.ThirdPartySlimmer.RootFolder";

        private static ThirdPartyScanResult _scan;

        private string _rootFolder;
        private TextField _rootField;
        private Label _statusLabel;
        private Label _summaryLabel;
        private Label _usedScriptsMetric;
        private Label _unusedScriptsMetric;
        private Label _usedShadersMetric;
        private Label _reclaimableMetric;
        private Label _packBadge;
        private Label _modeBadge;
        private Label _urpBadge;
        private Button _extractButton;
        private Label _extractNote;
        private Label _slimWarning;

        private Toggle _optScripts;
        private Toggle _optShaders;
        private Toggle _optResources;
        private Toggle _optMoveEditor;
        private Toggle _optExamples;
        private Toggle _optDoc;
        private Toggle _optExtra;

        public ThirdPartySlimmerView()
        {
            BuildUI();
        }

        private void BuildUI()
        {
            if (string.IsNullOrEmpty(_rootFolder))
                _rootFolder = LoadRoot();

            if (_scan != null && (!_scan.Succeeded || _scan.Profile == null ||
                                  !string.Equals(_scan.Profile.RootFolder, _rootFolder, System.StringComparison.OrdinalIgnoreCase)))
                _scan = null;

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;

            var metricsRow = new VisualElement();
            metricsRow.AddToClassList("metrics-row");
            metricsRow.Add(UnityUtilsUIStyle.CreateMetricCard("Used Scripts", "-", out _usedScriptsMetric));
            metricsRow.Add(UnityUtilsUIStyle.CreateMetricCard("Unused Scripts", "-", out _unusedScriptsMetric));
            metricsRow.Add(UnityUtilsUIStyle.CreateMetricCard("Used Shaders", "-", out _usedShadersMetric));
            metricsRow.Add(UnityUtilsUIStyle.CreateMetricCard("Reclaimable", "-", out _reclaimableMetric));
            scroll.Add(metricsRow);

            scroll.Add(BuildWorkflowCard());
            scroll.Add(BuildTargetCard());
            scroll.Add(BuildAuditCard());

            _summaryLabel = new Label("No audit run yet.");
            _summaryLabel.AddToClassList("status-box");
            _summaryLabel.style.whiteSpace = WhiteSpace.Normal;
            scroll.Add(_summaryLabel);

            scroll.Add(BuildSlimCard());
            scroll.Add(BuildExtractCard());
            scroll.Add(BuildUrpCard());

            _statusLabel = new Label("Ready. Pick a folder and click 'Run Audit'.");
            _statusLabel.AddToClassList("status-box");
            scroll.Add(_statusLabel);

            Add(scroll);

            RefreshTargetState();
            if (_scan != null) RefreshMetrics();
        }

        private VisualElement BuildWorkflowCard()
        {
            var card = UnityUtilsUIStyle.CreateCard(
                "Recommended Workflow",
                "Audit first, then pick ONE strategy. You do not need both.",
                out var body);

            body.Add(WorkflowStep("1. Run Audit", "Read-only scan that reports which components, shaders and Resources assets the project references (ignoring the pack's own Examples/Doc)."));

            var extract = WorkflowStep("2a. Extract Used Only  (recommended)",
                "Moves only the referenced assets to 'Assets/ThirdParty/<Pack>-Slim', preserving GUIDs, and deletes the original pack folder. The tool then retargets the slim folder for further audits and the URP port.");
            extract.style.marginBottom = 6;
            body.Add(extract);

            body.Add(WorkflowStep("2b. Slim In-Place  (alternative)",
                "Keeps the pack where it is and deletes unused assets inside it, moving editor-only Resources out of the build. Do NOT run this after Extract - the original folder no longer exists."));

            var urp = WorkflowStep("3. URP Shader Port  (optional)",
                "Rewrites the used shaders to URP in place, on whichever folder is currently targeted (original or '-Slim').");
            urp.style.marginTop = 6;
            body.Add(urp);

            return card;
        }

        private static VisualElement WorkflowStep(string title, string description)
        {
            var item = new VisualElement();

            var t = new Label(title);
            t.style.fontSize = 12;
            t.style.unityFontStyleAndWeight = FontStyle.Bold;

            var d = new Label(description);
            d.style.fontSize = 11;
            d.style.whiteSpace = WhiteSpace.Normal;
            d.style.color = new Color(0.62f, 0.62f, 0.68f);
            d.style.marginLeft = 6;

            item.Add(t);
            item.Add(d);
            return item;
        }

        private VisualElement BuildTargetCard()
        {
            var card = UnityUtilsUIStyle.CreateCard(
                "Target Package",
                "The folder currently being audited and modified.",
                out var body);

            var rootRow = new VisualElement();
            rootRow.style.flexDirection = FlexDirection.Row;
            rootRow.style.alignItems = Align.Center;

            _rootField = new TextField("Package Root") { value = _rootFolder };
            _rootField.style.flexGrow = 1;
            _rootField.RegisterValueChangedCallback(evt =>
            {
                _rootFolder = (evt.newValue ?? string.Empty).Replace('\\', '/').TrimEnd('/');
                EditorPrefs.SetString(RootPrefKey, _rootFolder);
                RefreshTargetState();
            });
            rootRow.Add(_rootField);

            var detectButton = UnityUtilsUIStyle.CreateButton("Auto-Detect", "btn-secondary", AutoDetect);
            detectButton.style.marginLeft = 6;
            rootRow.Add(detectButton);

            var pingButton = UnityUtilsUIStyle.CreateButton("Ping Folder", "btn-secondary", PingFolder);
            pingButton.style.marginLeft = 6;
            rootRow.Add(pingButton);
            body.Add(rootRow);

            var badgeRow = new VisualElement();
            badgeRow.style.flexDirection = FlexDirection.Row;
            badgeRow.style.alignItems = Align.Center;
            badgeRow.style.marginTop = 8;

            _packBadge = UnityUtilsUIStyle.CreateBadge("PACK DETECTED", "badge-info");
            _modeBadge = UnityUtilsUIStyle.CreateBadge("ORIGINAL INSTALL", "badge-warning");
            _urpBadge = UnityUtilsUIStyle.CreateBadge(
                ThirdPartyAssetScanner.DetectUrp() ? "URP ACTIVE" : "BUILT-IN / OTHER",
                ThirdPartyAssetScanner.DetectUrp() ? "badge-info" : "badge-warning");
            _packBadge.style.marginRight = 6;
            _modeBadge.style.marginRight = 6;
            badgeRow.Add(_packBadge);
            badgeRow.Add(_modeBadge);
            badgeRow.Add(_urpBadge);
            body.Add(badgeRow);

            return card;
        }

        private VisualElement BuildAuditCard()
        {
            var card = UnityUtilsUIStyle.CreateCard(
                "Audit",
                "Read-only. Reports used vs. unused assets and the reclaimable build footprint.",
                out var body);

            var bar = new VisualElement();
            bar.AddToClassList("action-toolbar");
            bar.style.marginTop = 0;
            bar.style.borderTopWidth = 0;
            bar.Add(UnityUtilsUIStyle.CreateButton("Run Audit", "btn-primary", RunAudit));
            bar.Add(UnityUtilsUIStyle.CreateButton("Show Details", "btn-secondary", ShowDetails));
            body.Add(bar);
            return card;
        }

        private VisualElement BuildSlimCard()
        {
            var card = UnityUtilsUIStyle.CreateCard(
                "Strategy B - Slim In-Place",
                "Deletes unused assets inside the current folder and moves editor-only resources out of the build.",
                out var body);

            _slimWarning = new Label(
                "This folder looks like a slim extract - it is already minimal, so Slim In-Place is normally unnecessary.");
            _slimWarning.AddToClassList("card-subtitle");
            _slimWarning.style.whiteSpace = WhiteSpace.Normal;
            _slimWarning.style.display = DisplayStyle.None;
            body.Add(_slimWarning);

            _optScripts = new Toggle("Delete unused scripts") { value = true };
            _optShaders = new Toggle("Delete unused shaders") { value = true };
            _optResources = new Toggle("Delete unused Resources assets") { value = true };
            _optMoveEditor = new Toggle("Move editor-only Resources into Editor/Resources") { value = true };
            _optDoc = new Toggle("Delete Doc folder") { value = true };
            _optExamples = new Toggle("Delete Examples folder") { value = false };
            _optExtra = new Toggle("Delete ExtraShaders folder") { value = false };

            body.Add(_optScripts);
            body.Add(_optShaders);
            body.Add(_optResources);
            body.Add(_optMoveEditor);
            body.Add(_optDoc);
            body.Add(_optExamples);
            body.Add(_optExtra);

            var bar = new VisualElement();
            bar.AddToClassList("action-toolbar");
            bar.Add(UnityUtilsUIStyle.CreateButton("Apply Slim In-Place...", "btn-danger", ApplySlim));
            body.Add(bar);
            return card;
        }

        private VisualElement BuildExtractCard()
        {
            var card = UnityUtilsUIStyle.CreateCard(
                "Strategy A - Extract Used Only",
                "Moves the referenced subset to a new '-Slim' folder, preserving GUIDs, and deletes the original pack.",
                out var body);

            var note = new Label(
                "After extracting, the tool automatically targets the new folder. You do NOT need to run Slim In-Place first. " +
                "Scene and material references keep working because asset GUIDs are moved, not regenerated.");
            note.AddToClassList("card-subtitle");
            note.style.whiteSpace = WhiteSpace.Normal;
            body.Add(note);

            _extractNote = new Label("This folder is already a slim extract. Nothing to extract.");
            _extractNote.AddToClassList("card-subtitle");
            _extractNote.style.color = new Color(0.6f, 0.8f, 0.6f);
            _extractNote.style.display = DisplayStyle.None;
            body.Add(_extractNote);

            var bar = new VisualElement();
            bar.AddToClassList("action-toolbar");
            _extractButton = UnityUtilsUIStyle.CreateButton("Extract Used Only...", "btn-danger", ExtractUsedOnly);
            bar.Add(_extractButton);
            body.Add(bar);
            return card;
        }

        private VisualElement BuildUrpCard()
        {
            var card = UnityUtilsUIStyle.CreateCard(
                "URP Shader Port",
                "Rewrites the used shaders to URP in place, keeping shader names and GUIDs so materials keep working.",
                out var body);

            var note = new Label(
                "Runs on the folder currently targeted above. Vertex/fragment shaders are converted mechanically; " +
                "legacy surface shaders are best-effort and flagged for review. Originals are backed up under the pack's '~' folder and can be restored.");
            note.AddToClassList("card-subtitle");
            note.style.whiteSpace = WhiteSpace.Normal;
            body.Add(note);

            var bar = new VisualElement();
            bar.AddToClassList("action-toolbar");
            bar.Add(UnityUtilsUIStyle.CreateButton("Convert Used Shaders to URP...", "btn-primary", ConvertToUrp));
            bar.Add(UnityUtilsUIStyle.CreateButton("Restore Backup", "btn-secondary", RestoreBackup));
            body.Add(bar);
            return card;
        }

        private static string LoadRoot()
        {
            var saved = EditorPrefs.GetString(RootPrefKey, string.Empty);
            if (!string.IsNullOrEmpty(saved) && AssetDatabase.IsValidFolder(saved))
                return saved.Replace('\\', '/').TrimEnd('/');

            return ThirdPartyAssetScanner.DetectDefaultProfile().RootFolder;
        }

        private ThirdPartyAssetProfile GetProfile()
        {
            var root = string.IsNullOrEmpty(_rootFolder) ? "Assets/2DxFX" : _rootFolder;
            return new ThirdPartyAssetProfile { RootFolder = root };
        }

        private void RefreshTargetState()
        {
            var profile = GetProfile();
            var valid = ThirdPartyAssetScanner.LooksLikePack(_rootFolder);
            var isSlim = ThirdPartyAssetScanner.LooksLikeSlimExtract(profile, _rootFolder);

            if (_packBadge != null)
            {
                _packBadge.text = valid ? "PACK DETECTED" : "NOT A PACK";
                SetBadgeClass(_packBadge, valid ? "badge-pass" : "badge-error");
            }

            if (_modeBadge != null)
            {
                _modeBadge.text = isSlim ? "SLIM EXTRACT" : "ORIGINAL INSTALL";
                SetBadgeClass(_modeBadge, isSlim ? "badge-info" : "badge-warning");
            }

            if (_extractButton != null) _extractButton.SetEnabled(valid && !isSlim);
            if (_extractNote != null) _extractNote.style.display = isSlim ? DisplayStyle.Flex : DisplayStyle.None;
            if (_slimWarning != null) _slimWarning.style.display = isSlim ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void SetBadgeClass(Label badge, string typeClass)
        {
            badge.RemoveFromClassList("badge-info");
            badge.RemoveFromClassList("badge-pass");
            badge.RemoveFromClassList("badge-warning");
            badge.RemoveFromClassList("badge-error");
            badge.AddToClassList(typeClass);
        }

        private void AutoDetect()
        {
            var detected = ThirdPartyAssetScanner.DetectDefaultProfile(_rootFolder);
            _rootFolder = detected.RootFolder;
            _rootField.value = _rootFolder;
            EditorPrefs.SetString(RootPrefKey, _rootFolder);
            RefreshTargetState();
            SetStatus("Detected package root: " + _rootFolder);
        }

        private void PingFolder()
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(_rootFolder);
            if (asset == null)
            {
                SetStatus("Folder not found: " + _rootFolder);
                return;
            }
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private void RunAudit()
        {
            _scan = ThirdPartyAssetScanner.Scan(GetProfile());
            RefreshMetrics();

            if (!_scan.Succeeded)
            {
                SetStatus("Audit failed: " + _scan.FatalError);
                return;
            }

            RefreshTargetState();
            SetStatus(_scan.SummaryText());
        }

        private void ShowDetails()
        {
            if (_scan == null || !_scan.Succeeded)
            {
                SetStatus("Run an audit first.");
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine("Used components:");
            for (var i = 0; i < _scan.Scripts.Count; i++)
            {
                var script = _scan.Scripts[i];
                if (script.ReferenceCount <= 0) continue;
                builder.Append("  • ").Append(script.ClassName).Append(" (").Append(script.ReferenceCount).Append(" ref, shader: ").Append(script.ShaderName ?? "n/a").AppendLine(")");
            }

            builder.AppendLine("Used shaders:");
            for (var i = 0; i < _scan.Shaders.Count; i++)
            {
                var shader = _scan.Shaders[i];
                if (!shader.IsUsed) continue;
                builder.Append("  • ").Append(shader.ShaderName)
                    .Append(shader.UsedByMaterial ? " [material]" : string.Empty)
                    .Append(shader.IsSurfaceShader ? " [surface]" : string.Empty)
                    .AppendLine();
            }

            builder.AppendLine("External materials referencing the pack:");
            for (var i = 0; i < _scan.ExternalMaterialReferences.Count; i++)
                builder.Append("  • ").AppendLine(_scan.ExternalMaterialReferences[i]);

            if (_scan.Notes.Count > 0)
            {
                builder.AppendLine("Notes:");
                for (var i = 0; i < _scan.Notes.Count; i++)
                    builder.Append("  • ").AppendLine(_scan.Notes[i]);
            }

            Debug.Log("[ThirdPartySlimmer]\n" + builder);
            SetStatus("Details written to the Console.");
        }

        private void RefreshMetrics()
        {
            if (_scan == null || !_scan.Succeeded) return;

            _usedScriptsMetric.text = _scan.UsedScriptCount + "/" + _scan.Scripts.Count;
            _unusedScriptsMetric.text = _scan.UnusedScriptCount.ToString();
            _usedShadersMetric.text = _scan.UsedShaderCount + "/" + _scan.Shaders.Count;
            _reclaimableMetric.text = ThirdPartySlimResult.FormatBytes(_scan.TotalBytes - _scan.UsedBytes);
            _summaryLabel.text = _scan.SummaryText();
        }

        private void ApplySlim()
        {
            if (!EnsureScan()) return;

            var options = new ThirdPartySlimOptions
            {
                DeleteUnusedScripts = _optScripts.value,
                DeleteUnusedShaders = _optShaders.value,
                DeleteUnusedResources = _optResources.value,
                MoveEditorResourcesOutOfBuild = _optMoveEditor.value,
                RemoveExamplesFolder = _optExamples.value,
                RemoveDocFolder = _optDoc.value,
                RemoveExtraShadersFolder = _optExtra.value
            };

            var message = string.Format(
                "This will permanently delete unused assets from '{0}' and move editor-only resources.\n\n" +
                "Unused scripts: {1}\nUnused shaders: {2}\nUnused resources: {3}\nEditor-only resources: {4}\n" +
                "Delete Examples: {5} | Doc: {6} | ExtraShaders: {7}\n\nMake sure the project is backed up or committed.",
                _scan.Profile.RootFolder, _scan.UnusedScriptCount, _scan.UnusedShaderCount,
                _scan.UnusedResourceCount, _scan.EditorOnlyResourceCount,
                _optExamples.value, _optDoc.value, _optExtra.value);

            if (!EditorUtility.DisplayDialog("Slim Third-Party Package", message, "Apply", "Cancel"))
                return;

            var result = ThirdPartyAssetSlimmer.ApplySlim(_scan.Profile, _scan, options);
            LogResult(result);
            RunAudit();
            SetStatus("Slim complete. " + result.Summary);
        }

        private void ExtractUsedOnly()
        {
            if (!EnsureScan()) return;

            var destination = _scan.Profile.ExtractDestinationFolder;
            var message = string.Format(
                "This moves the referenced subset of '{0}' into '{1}' and permanently deletes the original folder.\n\n" +
                "Used scripts: {2}\nUsed shaders: {3}\nRuntime resources: {4}\nEditor resources: {5}\n\n" +
                "Asset GUIDs are preserved. Make sure the project is backed up or committed.",
                _scan.Profile.RootFolder, destination, _scan.UsedScriptCount, _scan.UsedShaderCount,
                _scan.RuntimeResourceCount, _scan.EditorOnlyResourceCount);

            if (!EditorUtility.DisplayDialog("Extract Used Only", message, "Extract", "Cancel"))
                return;

            // Persist the destination up front: moving MonoScripts can trigger a
            // domain reload that destroys this window before the call returns.
            EditorPrefs.SetString(RootPrefKey, destination);

            var result = ThirdPartyAssetSlimmer.ExtractUsedOnly(_scan.Profile, _scan);
            LogResult(result);

            if (AssetDatabase.IsValidFolder(destination))
            {
                _rootFolder = destination;
                _rootField.value = destination;
            }

            _scan = null;
            RefreshTargetState();
            RunAudit();

            SetStatus(result.Errors.Count == 0
                ? "Extract complete. Now targeting " + _rootFolder + ". " + result.Summary
                : "Extract finished with errors. Now targeting " + _rootFolder + ". " + result.Summary);
        }

        private void ConvertToUrp()
        {
            if (!EnsureScan()) return;

            var message = string.Format(
                "Rewrite all used shaders of '{0}' to URP, in place?\n\n" +
                "Used shaders: {1} (surface shaders needing review: {2})\n\n" +
                "Originals are backed up under '{3}'. Shader names and GUIDs are preserved.",
                _scan.Profile.RootFolder, _scan.UsedShaderCount, _scan.SurfaceShaderCount, _scan.Profile.BackupFolder);

            if (!EditorUtility.DisplayDialog("Convert Shaders to URP", message, "Convert", "Cancel"))
                return;

            var result = UrpShaderConverter.ConvertUsedShaders(_scan.Profile, _scan, true);
            for (var i = 0; i < result.Errors.Count; i++) Debug.LogWarning("[ThirdPartySlimmer] " + result.Errors[i]);
            SetStatus("URP conversion: " + result.Summary +
                      (result.NeedsReview > 0 ? " Review the flagged surface shaders visually in a scene." : string.Empty));
        }

        private void RestoreBackup()
        {
            var profile = GetProfile();
            if (!UrpShaderConverter.HasBackups(profile))
            {
                SetStatus("No URP backup found for " + profile.RootFolder + ".");
                return;
            }

            if (!EditorUtility.DisplayDialog("Restore URP Backup",
                    "Restore the original Built-in shaders for '" + profile.RootFolder + "'?", "Restore", "Cancel"))
                return;

            var restored = UrpShaderConverter.RestoreBackups(profile);
            SetStatus("Restored " + restored + " shader(s) from backup.");
        }

        private bool EnsureScan()
        {
            if (_scan == null || !_scan.Succeeded)
            {
                SetStatus("Run an audit first.");
                return false;
            }
            return true;
        }

        private static void LogResult(ThirdPartySlimResult result)
        {
            for (var i = 0; i < result.Errors.Count; i++) Debug.LogWarning("[ThirdPartySlimmer] " + result.Errors[i]);
            for (var i = 0; i < result.Log.Count; i++) Debug.Log("[ThirdPartySlimmer] " + result.Log[i]);
        }

        private void SetStatus(string message)
        {
            if (_statusLabel != null) _statusLabel.text = message;
            Debug.Log("[ThirdPartySlimmer] " + message);
        }
    }

    internal static class ThirdPartyScanResultExtensions
    {
        public static string SummaryText(this ThirdPartyScanResult scan)
        {
            if (scan == null || !scan.Succeeded) return "No scan available.";

            return string.Format(
                "{0}: {1}/{2} scripts used, {3}/{4} shaders used, {5} runtime / {6} editor-only / {7} unused resources. " +
                "Reclaimable approx. {8}. Pipeline: {9}.",
                scan.Profile.RootFolder, scan.UsedScriptCount, scan.Scripts.Count,
                scan.UsedShaderCount, scan.Shaders.Count,
                scan.RuntimeResourceCount, scan.EditorOnlyResourceCount, scan.UnusedResourceCount,
                ThirdPartySlimResult.FormatBytes(scan.TotalBytes - scan.UsedBytes),
                scan.IsUrp ? "URP" : "Built-in / other");
        }
    }
}
