using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// VisualElement rendering the Bootstrap Diagnostic Checker and Scene Kit controls.
    /// </summary>
    public class BootstrapCheckerView : VisualElement
    {
        private DiagnosticReport _report;
        private Label _passMetric;
        private Label _warnMetric;
        private Label _errorMetric;
        private VisualElement _resultsContainer;
        private Label _statusLabel;
        private Button _fixAllBtn;

        public BootstrapCheckerView()
        {
            BuildUI();
            RunCheck(includeDeepScan: false);
        }

        private void BuildUI()
        {
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;

            // 1. Metric Counters
            var metricsRow = new VisualElement();
            metricsRow.AddToClassList("metrics-row");

            metricsRow.Add(UnityUtilsUIStyle.CreateMetricCard("Passed", "0", out _passMetric));
            metricsRow.Add(UnityUtilsUIStyle.CreateMetricCard("Warnings", "0", out _warnMetric));
            metricsRow.Add(UnityUtilsUIStyle.CreateMetricCard("Errors", "0", out _errorMetric));
            scroll.Add(metricsRow);

            // 2. Diagnostics Card
            var diagCard = UnityUtilsUIStyle.CreateCard(
                "Bootstrap Diagnostic Engine",
                "Verifies settings asset, build settings index, scene existence, and persistent singleton leaks.",
                out var diagBody);

            var diagToolbar = new VisualElement();
            diagToolbar.AddToClassList("action-toolbar");
            diagToolbar.style.marginTop = 0;
            diagToolbar.style.borderTopWidth = 0;

            var runBtn = UnityUtilsUIStyle.CreateButton("Run Full Diagnostic Scan", "btn-primary", () => RunCheck(includeDeepScan: true));
            _fixAllBtn = UnityUtilsUIStyle.CreateButton("Fix All Safe Issues", "btn-success", FixAllIssues);
            _fixAllBtn.SetEnabled(false);

            diagToolbar.Add(runBtn);
            diagToolbar.Add(_fixAllBtn);
            diagBody.Add(diagToolbar);

            _resultsContainer = new VisualElement();
            _resultsContainer.style.marginTop = 8;
            diagBody.Add(_resultsContainer);

            scroll.Add(diagCard);

            // 3. Quick Actions Card
            var opsCard = UnityUtilsUIStyle.CreateCard(
                "Bootstrap Scene Operations",
                "One-click shortcuts to build scenes, clean leaks, and configure persistent prefabs.",
                out var opsBody);

            var opsToolbar = new VisualElement();
            opsToolbar.style.flexDirection = FlexDirection.Row;
            opsToolbar.style.flexWrap = Wrap.Wrap;

            var openSettingsBtn = UnityUtilsUIStyle.CreateButton("Locate Settings Asset", "btn-secondary", LocateSettings);
            var rebuildSceneBtn = UnityUtilsUIStyle.CreateButton("Create / Rebuild Scene", "btn-primary", () =>
            {
                BootstrapSceneTools.CreateOrRebuildBootstrapScene();
                RunCheck(includeDeepScan: false);
            });
            var openSceneBtn = UnityUtilsUIStyle.CreateButton("Open Bootstrap Scene", "btn-secondary", OpenBootstrapScene);
            var cleanOtherScenesBtn = UnityUtilsUIStyle.CreateButton("Clean Leaks in Other Scenes", "btn-danger", () =>
            {
                BootstrapSceneTools.RemoveFromOtherScenes();
                RunCheck(includeDeepScan: true);
            });
            var cleanActiveSceneBtn = UnityUtilsUIStyle.CreateButton("Clean Leaks in Active Scene", "btn-secondary", () =>
            {
                BootstrapSceneTools.RemoveFromActiveScene();
                RunCheck(includeDeepScan: false);
            });

            openSettingsBtn.AddToClassList("ops-btn");
            rebuildSceneBtn.AddToClassList("ops-btn");
            openSceneBtn.AddToClassList("ops-btn");
            cleanOtherScenesBtn.AddToClassList("ops-btn");
            cleanActiveSceneBtn.AddToClassList("ops-btn");

            opsToolbar.Add(openSettingsBtn);
            opsToolbar.Add(rebuildSceneBtn);
            opsToolbar.Add(openSceneBtn);
            opsToolbar.Add(cleanOtherScenesBtn);
            opsToolbar.Add(cleanActiveSceneBtn);

            opsBody.Add(opsToolbar);
            scroll.Add(opsCard);

            // 4. Status Box
            _statusLabel = new Label("Ready. Click 'Run Full Diagnostic Scan' to analyze your project.");
            _statusLabel.AddToClassList("status-box");
            scroll.Add(_statusLabel);

            Add(scroll);
        }

        public void RunCheck(bool includeDeepScan = true)
        {
            _resultsContainer.Clear();
            _report = BootstrapChecker.RunDiagnostics(includeDeepSceneScan: includeDeepScan);

            // Update Metrics
            _passMetric.text = _report.PassCount.ToString();
            _warnMetric.text = _report.WarningCount.ToString();
            _errorMetric.text = _report.ErrorCount.ToString();

            // Colorize Metric Values
            _passMetric.style.color = new StyleColor(new Color(0.1f, 0.8f, 0.4f));
            _warnMetric.style.color = _report.WarningCount > 0
                ? new StyleColor(new Color(1f, 0.7f, 0.2f))
                : new StyleColor(Color.white);
            _errorMetric.style.color = _report.ErrorCount > 0
                ? new StyleColor(new Color(1f, 0.35f, 0.35f))
                : new StyleColor(Color.white);

            var fixableCount = _report.Items.Count(i => i.CanFix);
            _fixAllBtn.SetEnabled(fixableCount > 0);

            // Populate Results
            foreach (var item in _report.Items)
            {
                var row = new VisualElement();
                row.AddToClassList("diag-row");

                var infoCol = new VisualElement();
                infoCol.AddToClassList("diag-info");

                var title = new Label(item.Title);
                title.AddToClassList("diag-title");

                var desc = new Label(item.Description);
                desc.AddToClassList("diag-desc");

                infoCol.Add(title);
                infoCol.Add(desc);

                var actionCol = new VisualElement();
                actionCol.AddToClassList("diag-actions");

                var badgeClass = item.Severity switch
                {
                    DiagnosticSeverity.Pass => "badge-pass",
                    DiagnosticSeverity.Warning => "badge-warning",
                    DiagnosticSeverity.Error => "badge-error",
                    _ => "badge-info"
                };

                var badge = UnityUtilsUIStyle.CreateBadge(item.Severity.ToString().ToUpperInvariant(), badgeClass);
                actionCol.Add(badge);

                if (item.CanFix && item.FixAction != null)
                {
                    var fixBtn = UnityUtilsUIStyle.CreateButton("Fix", "btn-secondary", () =>
                    {
                        item.FixAction();
                        RunCheck(includeDeepScan: false);
                    });
                    fixBtn.style.marginLeft = 8;
                    actionCol.Add(fixBtn);
                }

                row.Add(infoCol);
                row.Add(actionCol);
                _resultsContainer.Add(row);
            }

            _statusLabel.text = $"Diagnostic scan completed: {_report.PassCount} passed, {_report.WarningCount} warnings, {_report.ErrorCount} errors.";
        }

        private void FixAllIssues()
        {
            if (_report == null) return;

            var fixable = _report.Items.Where(i => i.CanFix && i.FixAction != null).ToList();
            if (fixable.Count == 0) return;

            foreach (var item in fixable)
            {
                try
                {
                    item.FixAction();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[BootstrapChecker] Failed to fix '{item.Title}': {ex.Message}");
                }
            }

            RunCheck(includeDeepScan: true);
        }

        private static void LocateSettings()
        {
            var settings = BootstrapSceneTools.FindSettings();
            if (settings != null)
            {
                Selection.activeObject = settings;
                EditorGUIUtility.PingObject(settings);
            }
            else
            {
                BootstrapSceneTools.CreateSettingsAsset();
            }
        }

        private static void OpenBootstrapScene()
        {
            var settings = BootstrapSceneTools.FindSettings();
            if (settings == null) return;

            var scenePath = BootstrapSceneTools.ScenePathFor(settings.BootstrapSceneName);
            if (File.Exists(scenePath))
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                }
            }
            else
            {
                EditorUtility.DisplayDialog("Open Bootstrap Scene", $"Scene not found at '{scenePath}'. Create it first.", "OK");
            }
        }
    }
}

