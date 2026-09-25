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
    /// </summary>
    public class ThirdPartySlimmerView : VisualElement
    {
        private static string _rootFolder = "Assets/2DxFX";
        private static ThirdPartyScanResult _scan;

        private TextField _rootField;
        private Label _statusLabel;
        private Label _summaryLabel;
        private Label _usedScriptsMetric;
        private Label _unusedScriptsMetric;
        private Label _usedShadersMetric;
        private Label _reclaimableMetric;

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
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;

            var metricsRow = new VisualElement();
            metricsRow.AddToClassList("metrics-row");
            metricsRow.Add(UnityUtilsUIStyle.CreateMetricCard("Used Scripts", "-", out _usedScriptsMetric));
            metricsRow.Add(UnityUtilsUIStyle.CreateMetricCard("Unused Scripts", "-", out _unusedScriptsMetric));
            metricsRow.Add(UnityUtilsUIStyle.CreateMetricCard("Used Shaders", "-", out _usedShadersMetric));
            metricsRow.Add(UnityUtilsUIStyle.CreateMetricCard("Reclaimable", "-", out _reclaimableMetric));
            scroll.Add(metricsRow);

            var targetCard = UnityUtilsUIStyle.CreateCard(
                "Target Package",
                "Third-party pack to audit. Usage is resolved by GUID, ignoring the pack's own Examples and Doc content.",
                out var targetBody);

            var rootRow = new VisualElement();
            rootRow.style.flexDirection = FlexDirection.Row;
            rootRow.style.alignItems = Align.Center;

            _rootField = new TextField("Package Root") { value = _rootFolder };
            _rootField.style.flexGrow = 1;
            _rootField.RegisterValueChangedCallback(evt => _rootFolder = evt.newValue);
            rootRow.Add(_rootField);

            var detectButton = UnityUtilsUIStyle.CreateButton("Auto-Detect", "btn-secondary", () =>
            {
                var detected = ThirdPartyAssetScanner.DetectDefaultProfile();
                _rootFolder = detected.RootFolder;
                _rootField.value = _rootFolder;
                SetStatus("Detected package root: " + _rootFolder);
            });
            detectButton.style.marginLeft = 6;
            rootRow.Add(detectButton);
            targetBody.Add(rootRow);

            var pipelineRow = new VisualElement();
            pipelineRow.style.flexDirection = FlexDirection.Row;
            pipelineRow.style.alignItems = Align.Center;
            pipelineRow.style.marginTop = 6;

            var isUrp = ThirdPartyAssetScanner.DetectUrp();
            pipelineRow.Add(UnityUtilsUIStyle.CreateBadge(isUrp ? "URP ACTIVE" : "BUILT-IN / OTHER", isUrp ? "badge-info" : "badge-warning"));
            var pipelineHint = new Label(isUrp ? " Built-in surface shaders must be ported before they render correctly." : " URP porting is optional for this project.");
            pipelineHint.style.fontSize = 11;
            pipelineHint.style.color = new Color(0.6f, 0.6f, 0.65f);
            pipelineRow.Add(pipelineHint);
            targetBody.Add(pipelineRow);

            var auditBar = new VisualElement();
            auditBar.AddToClassList("action-toolbar");
            auditBar.Add(UnityUtilsUIStyle.CreateButton("Run Audit", "btn-primary", RunAudit));
            auditBar.Add(UnityUtilsUIStyle.CreateButton("Show Details", "btn-secondary", ShowDetails));
            targetBody.Add(auditBar);
            scroll.Add(targetCard);

            _summaryLabel = new Label("No audit run yet.");
            _summaryLabel.AddToClassList("status-box");
            _summaryLabel.style.whiteSpace = WhiteSpace.Normal;
            scroll.Add(_summaryLabel);

            scroll.Add(BuildSlimCard());
            scroll.Add(BuildExtractCard());
            scroll.Add(BuildUrpCard());

            _statusLabel = new Label("Ready. Set the package root and click 'Run Audit'.");
            _statusLabel.AddToClassList("status-box");
            scroll.Add(_statusLabel);

            Add(scroll);
        }

        private VisualElement BuildSlimCard()
        {
            var card = UnityUtilsUIStyle.CreateCard(
                "Slim In-Place",
                "Deletes everything the project does not reference and moves editor-only resources out of the build.",
                out var body);

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
            bar.Add(UnityUtilsUIStyle.CreateButton("Apply Slim...", "btn-danger", ApplySlim));
            body.Add(bar);
            return card;
        }

        private VisualElement BuildExtractCard()
        {
            var card = UnityUtilsUIStyle.CreateCard(
                "Extract Used Only",
                "Moves the referenced subset into Assets/ThirdParty/<Pack>-Slim and deletes the original pack folder.",
                out var body);

            var note = new Label("Scene and material references are preserved because asset GUIDs are moved, not regenerated.");
            note.AddToClassList("card-subtitle");
            note.style.whiteSpace = WhiteSpace.Normal;
            body.Add(note);

            var bar = new VisualElement();
            bar.AddToClassList("action-toolbar");
            bar.Add(UnityUtilsUIStyle.CreateButton("Extract Used Only...", "btn-danger", ExtractUsedOnly));
            body.Add(bar);
            return card;
        }

        private VisualElement BuildUrpCard()
        {
            var card = UnityUtilsUIStyle.CreateCard(
                "URP Shader Port",
                "Rewrites used Built-in shaders to URP in place, keeping shader names and GUIDs so materials keep working.",
                out var body);

            var note = new Label(
                "Vertex/fragment shaders are converted mechanically. Legacy surface shaders are best-effort and flagged for review. " +
                "Originals are backed up under the pack's '~' folder and can be restored at any time.");
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

        private ThirdPartyAssetProfile GetProfile()
        {
            return new ThirdPartyAssetProfile { RootFolder = string.IsNullOrEmpty(_rootFolder) ? "Assets/2DxFX" : _rootFolder.Replace('\\', '/').TrimEnd('/') };
        }

        private void RunAudit()
        {
            var profile = GetProfile();
            _scan = ThirdPartyAssetScanner.Scan(profile);
            RefreshMetrics();
            SetStatus(_scan.Succeeded ? _scan.SummaryText() : "Audit failed: " + _scan.FatalError);
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

            var reclaimable = _scan.TotalBytes - _scan.UsedBytes;
            _reclaimableMetric.text = ThirdPartySlimResult.FormatBytes(reclaimable);

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

            var result = ThirdPartyAssetSlimmer.ExtractUsedOnly(_scan.Profile, _scan);
            LogResult(result);

            if (result.Errors.Count == 0 && AssetDatabase.IsValidFolder(destination))
                _rootFolder = destination;

            RunAudit();
            SetStatus("Extract complete. " + result.Summary);
        }

        private void ConvertToUrp()
        {
            if (!EnsureScan()) return;

            var surface = _scan.SurfaceShaderCount;
            var message = string.Format(
                "Rewrite all used shaders of '{0}' to URP, in place?\n\n" +
                "Used shaders: {1} (surface shaders needing review: {2})\n\n" +
                "Originals are backed up under '{3}'. Shader names and GUIDs are preserved.",
                _scan.Profile.RootFolder, _scan.UsedShaderCount, surface, _scan.Profile.BackupFolder);

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
