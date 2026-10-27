using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Wagenheimer.UnityUtils.Pooling;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Dashboard tab for the pooling system: live runtime counters, the legacy PoolManager
    /// detection/migration workflow, backups, and project settings.
    /// </summary>
    public class PoolingHubView : VisualElement
    {
        private static readonly Color Muted = new Color(0.65f, 0.68f, 0.75f);
        private static readonly Color Warning = new Color(0.95f, 0.72f, 0.20f);
        private static readonly Color Error = new Color(0.95f, 0.35f, 0.35f);
        private static readonly Color Success = new Color(0.35f, 0.85f, 0.55f);

        private Label _poolMetric;
        private Label _prefabMetric;
        private Label _spawnedMetric;
        private Label _despawnedMetric;
        private Label _status;
        private Label _detectionSummary;
        private VisualElement _detectionBox;
        private VisualElement _runtimeList;
        private VisualElement _settingsBody;
        private VisualElement _backupRow;
        private PopupField<string> _backupPopup;
        private Button _restoreButton;

        private LegacyPoolMigrator.Detection _detection;
        private List<LegacyPoolMigrator.BackupInfo> _backups = new List<LegacyPoolMigrator.BackupInfo>();
        private bool _busy;

        public PoolingHubView()
        {
            BuildUI();
            schedule.Execute(RefreshRuntime).Every(500);
            RefreshRuntime();
            RefreshBackups();
        }

        #region UI

        private void BuildUI()
        {
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;

            var metrics = new VisualElement();
            metrics.AddToClassList("metrics-row");
            metrics.Add(UnityUtilsUIStyle.CreateMetricCard("Pools", "-", out _poolMetric));
            metrics.Add(UnityUtilsUIStyle.CreateMetricCard("Prefabs", "-", out _prefabMetric));
            metrics.Add(UnityUtilsUIStyle.CreateMetricCard("Spawned", "-", out _spawnedMetric));
            metrics.Add(UnityUtilsUIStyle.CreateMetricCard("Despawned", "-", out _despawnedMetric));
            scroll.Add(metrics);

            scroll.Add(BuildRuntimeCard());
            scroll.Add(BuildMigrationCard());
            scroll.Add(BuildSettingsCard());
            scroll.Add(BuildUsageCard());

            _status = new Label("Ready. Runtime counters update every 500 ms while playing.");
            _status.AddToClassList("status-box");
            scroll.Add(_status);

            Add(scroll);
        }

        private VisualElement BuildRuntimeCard()
        {
            var card = UnityUtilsUIStyle.CreateCard(
                "Runtime Pools",
                "Live view of SpawnManager.Pools. Despawn/Trim actions are safe to use while testing.",
                out var body);

            var toolbar = new VisualElement();
            toolbar.AddToClassList("action-toolbar");
            toolbar.style.marginTop = 0;
            toolbar.style.borderTopWidth = 0;

            toolbar.Add(UnityUtilsUIStyle.CreateButton("Refresh", "btn-secondary", RefreshRuntime));
            toolbar.Add(UnityUtilsUIStyle.CreateButton("Despawn All Pools", "btn-secondary", () =>
            {
                SpawnManager.DespawnAll();
                RefreshRuntime();
            }));
            toolbar.Add(UnityUtilsUIStyle.CreateButton("Trim All Despawned", "btn-secondary", () =>
            {
                foreach (var pool in SpawnManager.Pools.Values)
                    pool?.Trim(0);

                RefreshRuntime();
            }));
            body.Add(toolbar);

            _runtimeList = new VisualElement();
            _runtimeList.style.marginTop = 6;
            body.Add(_runtimeList);

            return card;
        }

        private VisualElement BuildMigrationCard()
        {
            var card = UnityUtilsUIStyle.CreateCard(
                "Legacy PoolManager Migration",
                "Detects the Path-o-logical PoolManager, converts SpawnPool components and rewrites the old API to the UnityUtils namespace.",
                out var body);

            var summaryRow = new VisualElement();
            summaryRow.style.flexDirection = FlexDirection.Row;
            summaryRow.style.alignItems = Align.Center;
            summaryRow.style.marginBottom = 6;

            _detectionSummary = new Label("Not scanned yet.");
            _detectionSummary.style.fontSize = 11;
            _detectionSummary.style.flexGrow = 1;
            summaryRow.Add(_detectionSummary);
            body.Add(summaryRow);

            var toolbar = new VisualElement();
            toolbar.AddToClassList("action-toolbar");
            toolbar.style.marginTop = 0;
            toolbar.style.borderTopWidth = 0;

            toolbar.Add(UnityUtilsUIStyle.CreateButton("Scan Project", "btn-primary", () => RunScan(true)));
            toolbar.Add(UnityUtilsUIStyle.CreateButton("Preview Migration", "btn-secondary", () => RunScan(true)));
            toolbar.Add(UnityUtilsUIStyle.CreateButton("Migrate Now", "btn-success", RunMigration));
            body.Add(toolbar);

            _detectionBox = new VisualElement();
            _detectionBox.style.marginTop = 6;
            body.Add(_detectionBox);

            var toggles = new VisualElement();
            toggles.style.flexDirection = FlexDirection.Row;
            toggles.style.flexWrap = Wrap.Wrap;
            toggles.style.marginTop = 8;

            var autoDetect = new Toggle("Auto-detect on project load")
            {
                value = PoolingAutoDetector.AutoDetectEnabled
            };
            autoDetect.style.marginRight = 16;
            autoDetect.RegisterValueChangedCallback(e => PoolingAutoDetector.AutoDetectEnabled = e.newValue);
            toggles.Add(autoDetect);

            var autoMigrate = new Toggle("Auto-migrate when detected (no prompt)")
            {
                value = PoolingAutoDetector.AutoMigrateEnabled
            };
            autoMigrate.style.marginRight = 16;
            autoMigrate.RegisterValueChangedCallback(e => PoolingAutoDetector.AutoMigrateEnabled = e.newValue);
            toggles.Add(autoMigrate);

            toggles.Add(UnityUtilsUIStyle.CreateButton("Reset prompt", "btn-secondary", () =>
            {
                PoolingAutoDetector.ResetPrompt();
                SetStatus("The migration prompt will appear again on the next project load.", Muted);
            }));

            body.Add(toggles);

            _backupRow = new VisualElement();
            _backupRow.style.flexDirection = FlexDirection.Row;
            _backupRow.style.alignItems = Align.Center;
            _backupRow.style.marginTop = 10;
            _backupRow.style.paddingTop = 8;
            _backupRow.style.borderTopWidth = 1;
            _backupRow.style.borderTopColor = new Color(0.24f, 0.25f, 0.30f);
            body.Add(_backupRow);

            return card;
        }

        private VisualElement BuildSettingsCard()
        {
            var card = UnityUtilsUIStyle.CreateCard(
                "Pooling Settings",
                "Project-wide defaults used by the runtime and the debug overlay.",
                out var body);

            _settingsBody = body;
            RefreshSettings();
            return card;
        }

        private VisualElement BuildUsageCard()
        {
            var card = UnityUtilsUIStyle.CreateCard(
                "Usage",
                "The API is source-compatible with the old PoolManager.",
                out var body);

            AddCode(body, "// Migrated code (same calls as before)\n" +
                          "SpawnManager.Pools[\"level\"].Spawn(prefab, position, rotation);\n" +
                          "SpawnManager.Pools[\"level\"].Despawn(instance, 2f);\n\n" +
                          "// Optional: no reflection, no BroadcastMessage\n" +
                          "public class Bullet : MonoBehaviour, IPoolable\n" +
                          "{\n" +
                          "    public void OnSpawned(SpawnPool pool) { }\n" +
                          "    public void OnDespawned(SpawnPool pool) { }\n" +
                          "}\n\n" +
                          "// Diagnostics\n" +
                          "PoolReport report = SpawnManager.GetReport();\n" +
                          "Debug.Log(report.ToText());");

            return card;
        }

        private static void AddCode(VisualElement body, string code)
        {
            var label = new Label(code);
            label.style.fontSize = 10;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
            label.style.paddingLeft = label.style.paddingRight = 8;
            label.style.paddingTop = label.style.paddingBottom = 6;
            label.style.borderTopLeftRadius = label.style.borderTopRightRadius = label.style.borderBottomLeftRadius = label.style.borderBottomRightRadius = 4;
            body.Add(label);
        }

        #endregion

        #region Runtime

        private void RefreshRuntime()
        {
            if (panel == null) return;

            var report = SpawnManager.GetReport();
            _poolMetric.text = report.PoolCount.ToString();
            _prefabMetric.text = report.PrefabCount.ToString();
            _spawnedMetric.text = report.Spawned.ToString();
            _despawnedMetric.text = report.Despawned.ToString();

            _runtimeList.Clear();

            if (!Application.isPlaying)
            {
                _runtimeList.Add(MutedLabel("Enter Play Mode to inspect the live pools."));
                return;
            }

            if (report.Pools.Count == 0)
            {
                _runtimeList.Add(MutedLabel("No pools registered yet. Spawn something or add a SpawnPool component."));
                return;
            }

            for (var i = 0; i < report.Pools.Count; i++)
                _runtimeList.Add(BuildRuntimeRow(report.Pools[i]));
        }

        private VisualElement BuildRuntimeRow(PoolSnapshot snapshot)
        {
            var row = new VisualElement();
            row.style.marginBottom = 6;
            row.style.paddingLeft = 8;
            row.style.paddingRight = 8;
            row.style.paddingTop = 6;
            row.style.paddingBottom = 6;
            row.style.backgroundColor = new Color(0.15f, 0.16f, 0.20f);
            row.style.borderTopLeftRadius = row.style.borderTopRightRadius = row.style.borderBottomLeftRadius = row.style.borderBottomRightRadius = 5;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;

            var name = new Label(snapshot.PoolName);
            name.style.fontSize = 12;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.flexGrow = 1;
            header.Add(name);

            var counts = new Label($"spawned {snapshot.Spawned}  despawned {snapshot.Despawned}  total {snapshot.Total}  " +
                                   $"inst {snapshot.Instantiated}  reuse {snapshot.Reused}");
            counts.style.fontSize = 10;
            counts.style.color = Muted;
            header.Add(counts);

            header.Add(UnityUtilsUIStyle.CreateButton("Despawn", "btn-secondary", () =>
            {
                SpawnManager.GetPool(snapshot.PoolName)?.DespawnAll();
                RefreshRuntime();
            }));

            header.Add(UnityUtilsUIStyle.CreateButton("Trim", "btn-secondary", () =>
            {
                SpawnManager.GetPool(snapshot.PoolName)?.Trim(0);
                RefreshRuntime();
            }));

            row.Add(header);

            for (var i = 0; i < snapshot.Prefabs.Count; i++)
            {
                var prefab = snapshot.Prefabs[i];
                var detail = new Label(
                    $"{prefab.PrefabName}: spawned {prefab.Spawned}, despawned {prefab.Despawned}, total {prefab.Total}, " +
                    $"instantiated {prefab.Instantiated}, reused {prefab.Reused}" +
                    (prefab.LimitHits > 0 ? $", limit hits {prefab.LimitHits}" : "") +
                    (prefab.Culled > 0 ? $", culled {prefab.Culled}" : ""));
                detail.style.fontSize = 9.5f;
                detail.style.color = Muted;
                detail.style.paddingLeft = 10;
                row.Add(detail);
            }

            return row;
        }

        #endregion

        #region Migration

        private void RunScan(bool includeScripts)
        {
            if (_busy) return;

            _busy = true;
            try
            {
                SetStatus("Scanning the project...", Muted);
                _detection = LegacyPoolMigrator.Detect(includeAssets: true, includeScripts: includeScripts);
                RenderDetection();
            }
            catch (Exception ex)
            {
                SetStatus($"Scan failed: {ex.Message}", Error);
                Debug.LogException(ex);
            }
            finally
            {
                _busy = false;
            }
        }

        private void RenderDetection()
        {
            _detectionBox.Clear();

            if (_detection == null)
            {
                _detectionSummary.text = "Not scanned yet.";
                return;
            }

            if (!_detection.HasLegacy)
            {
                _detectionSummary.text = "No legacy PoolManager detected.";
                _detectionBox.Add(StatusBadge("CLEAN", "badge-pass"));
                SetStatus("No legacy PoolManager found. Nothing to migrate.", Success);
                return;
            }

            _detectionSummary.text = _detection.Summary;
            _detectionBox.Add(StatusBadge(_detection.CanRun ? "MIGRATION READY" : "REVIEW REQUIRED",
                _detection.CanRun ? "badge-warning" : "badge-error"));

            if (_detection.Cancelled)
                _detectionBox.Add(MutedLabel("Scan cancelled; the list below is partial."));

            if (!string.IsNullOrEmpty(_detection.LegacySpawnPoolPath))
                AddDetail("Legacy SpawnPool", _detection.LegacySpawnPoolPath);
            if (!string.IsNullOrEmpty(_detection.NewSpawnPoolPath))
                AddDetail("New SpawnPool", _detection.NewSpawnPoolPath);

            AddPathList("Assets with legacy components", _detection.AssetPaths, 25);
            AddPathList("Scripts using the legacy API", _detection.ScriptPaths, 25);

            if (_detection.LegacyFolders.Count > 0)
                AddPathList("Legacy folders (moved out of Assets)", _detection.LegacyFolders, 10);

            if (!_detection.CanRun)
                SetStatus("The new pooling scripts were not found or the legacy script GUID is missing. " +
                          "Make sure the package is installed and compiled, then scan again.", Error);
            else
                SetStatus("Ready to migrate. A backup is created in Library/UnityUtilsPoolMigrationBackup.", Success);
        }

        private void RunMigration()
        {
            if (_busy) return;

            if (_detection == null || !_detection.HasLegacy)
                RunScan(true);

            if (_detection == null || !_detection.HasLegacy) return;

            if (!_detection.CanRun)
            {
                SetStatus("Cannot migrate yet: the new pooling scripts or the legacy script GUID were not found.", Error);
                return;
            }

            var message =
                "Migrate the legacy PoolManager to UnityUtils?\n\n" +
                _detection.Summary + "\n\n" +
                "The migration will:\n" +
                "- Re-point SpawnPool and PreRuntimePoolItem components to the new scripts (serialized data is preserved).\n" +
                "- Replace 'using PathologicalGames;' with 'using Wagenheimer.UnityUtils.Pooling;' in project scripts.\n" +
                "- Move the legacy plugin folders out of Assets.\n" +
                "- Back up every changed file to Library/UnityUtilsPoolMigrationBackup.\n\n" +
                "Save your work first. Continue?";

            if (!EditorUtility.DisplayDialog("Migrate PoolManager", message, "Migrate", "Cancel"))
                return;

            _busy = true;
            var shouldRescan = false;
            try
            {
                SetStatus("Migrating... Unity may recompile the scripts.", Muted);
                var result = LegacyPoolMigrator.Migrate(_detection, removeLegacyPlugin: true);

                if (result.Cancelled)
                {
                    SetStatus("Migration cancelled.", Warning);
                }
                else
                {
                    SetStatus(result.Summary, result.Success ? Success : Error);
                    RefreshBackups();
                    _detection = null;
                    shouldRescan = true;
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Migration failed: {ex.Message}", Error);
                Debug.LogException(ex);
            }
            finally
            {
                _busy = false;
            }

            if (shouldRescan)
                RunScan(true);
        }

        private void AddDetail(string title, string value)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 2;

            var label = new Label(title);
            label.style.fontSize = 10;
            label.style.color = Muted;
            label.style.minWidth = 150;
            row.Add(label);

            var path = new Label(value);
            path.style.fontSize = 10;
            path.style.whiteSpace = WhiteSpace.Normal;
            row.Add(path);

            _detectionBox.Add(row);
        }

        private void AddPathList(string title, List<string> paths, int max)
        {
            if (paths == null || paths.Count == 0) return;

            var titleLabel = new Label($"{title} ({paths.Count})");
            titleLabel.style.fontSize = 10;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.marginTop = 6;
            _detectionBox.Add(titleLabel);

            var shown = Mathf.Min(max, paths.Count);
            for (var i = 0; i < shown; i++)
            {
                var path = new Label("  " + paths[i]);
                path.style.fontSize = 9.5f;
                path.style.color = Muted;
                path.style.whiteSpace = WhiteSpace.Normal;
                _detectionBox.Add(path);
            }

            if (paths.Count > shown)
                _detectionBox.Add(MutedLabel($"  ... and {paths.Count - shown} more"));
        }

        #endregion

        #region Backups

        private void RefreshBackups()
        {
            if (_backupRow == null) return;

            _backups = LegacyPoolMigrator.GetBackups();
            _backupRow.Clear();

            var label = new Label("Backups");
            label.style.fontSize = 10;
            label.style.color = Muted;
            label.style.minWidth = 60;
            _backupRow.Add(label);

            var choices = new List<string>();
            for (var i = 0; i < _backups.Count; i++)
                choices.Add(_backups[i].DisplayName);

            if (choices.Count == 0)
                choices.Add("(no backups yet)");

            _backupPopup = new PopupField<string>(choices, 0);
            _backupPopup.style.flexGrow = 1;
            _backupPopup.style.maxWidth = 360;
            _backupPopup.SetEnabled(_backups.Count > 0);
            _backupRow.Add(_backupPopup);

            _restoreButton = UnityUtilsUIStyle.CreateButton("Restore Selected", "btn-danger", RestoreSelectedBackup);
            _restoreButton.SetEnabled(_backups.Count > 0);
            _restoreButton.style.marginLeft = 6;
            _backupRow.Add(_restoreButton);

            _backupRow.Add(UnityUtilsUIStyle.CreateButton("Open Folder", "btn-secondary", () =>
            {
                if (Directory.Exists(LegacyPoolMigrator.BackupRoot))
                    EditorUtility.RevealInFinder(LegacyPoolMigrator.BackupRoot);
                else
                    SetStatus("No backup folder exists yet.", Muted);
            }));
        }

        private void RestoreSelectedBackup()
        {
            if (_backups.Count == 0) return;

            var index = Mathf.Clamp(_backupPopup.index, 0, _backups.Count - 1);
            var backup = _backups[index];

            if (!EditorUtility.DisplayDialog("Restore PoolManager backup",
                    $"Restore the backup from {backup.Timestamp}?\n\n" +
                    "This copies the backed-up assets/scripts back and restores the legacy plugin folders. " +
                    "Current versions of those files will be overwritten.",
                    "Restore", "Cancel"))
                return;

            if (LegacyPoolMigrator.Restore(backup.Path))
            {
                SetStatus($"Backup restored: {backup.Timestamp}", Success);
                _detection = null;
                RunScan(true);
            }
            else
            {
                SetStatus("Restore failed. Check the console for details.", Error);
            }
        }

        #endregion

        #region Settings

        private void RefreshSettings()
        {
            if (_settingsBody == null) return;
            _settingsBody.Clear();

            var settings = Resources.Load<SpawnManagerSettings>("Wagenheimer/SpawnManagerSettings");
            if (settings == null)
            {
                _settingsBody.Add(MutedLabel("No settings asset found. The runtime uses built-in defaults."));
                _settingsBody.Add(UnityUtilsUIStyle.CreateButton("Create Settings Asset", "btn-primary", CreateSettingsAsset));
                return;
            }

            var path = AssetDatabase.GetAssetPath(settings);
            AddDetailTo(_settingsBody, "Asset", path);

            var logLevel = new EnumField("Log level", settings.logLevel);
            logLevel.RegisterValueChangedCallback(e =>
            {
                settings.logLevel = (PoolLogLevel)e.newValue;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            });
            _settingsBody.Add(logLevel);

            var legacy = new Toggle("Send legacy OnSpawned/OnDespawned messages") { value = settings.sendLegacyMessages };
            legacy.RegisterValueChangedCallback(e =>
            {
                settings.sendLegacyMessages = e.newValue;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            });
            _settingsBody.Add(legacy);

            var detect = new Toggle("Detect legacy messages per prefab (faster)") { value = settings.detectLegacyMessages };
            detect.RegisterValueChangedCallback(e =>
            {
                settings.detectLegacyMessages = e.newValue;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            });
            _settingsBody.Add(detect);

            var overlay = new Toggle("Enable runtime pool overlay") { value = settings.enableRuntimeDebugOverlay };
            overlay.RegisterValueChangedCallback(e =>
            {
                settings.enableRuntimeDebugOverlay = e.newValue;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            });
            _settingsBody.Add(overlay);

            var preload = new IntegerField("Default preload for auto-created pools") { value = settings.defaultPreloadAmount };
            preload.RegisterValueChangedCallback(e =>
            {
                settings.defaultPreloadAmount = Mathf.Max(0, e.newValue);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            });
            _settingsBody.Add(preload);

            var toolbar = new VisualElement();
            toolbar.AddToClassList("action-toolbar");
            toolbar.style.marginTop = 4;
            toolbar.Add(UnityUtilsUIStyle.CreateButton("Select Asset", "btn-secondary", () => Selection.activeObject = settings));
            toolbar.Add(UnityUtilsUIStyle.CreateButton("Open Folder", "btn-secondary", () =>
                EditorUtility.RevealInFinder(Path.GetDirectoryName(path) ?? "Assets")));
            _settingsBody.Add(toolbar);
        }

        private void CreateSettingsAsset()
        {
            const string folder = "Assets/Resources/Wagenheimer";
            const string path = folder + "/SpawnManagerSettings.asset";

            if (!AssetDatabase.IsValidFolder(folder))
            {
                var parent = "Assets";
                var parts = folder.Split('/');
                for (var i = 1; i < parts.Length; i++)
                {
                    var current = parent + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(current))
                        AssetDatabase.CreateFolder(parent, parts[i]);
                    parent = current;
                }
            }

            var existing = AssetDatabase.LoadAssetAtPath<SpawnManagerSettings>(path);
            if (existing == null)
            {
                var settings = ScriptableObject.CreateInstance<SpawnManagerSettings>();
                AssetDatabase.CreateAsset(settings, path);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                existing = settings;
            }

            Selection.activeObject = existing;
            SetStatus($"Settings asset ready at '{path}'.", Success);
            RefreshSettings();
        }

        private static void AddDetailTo(VisualElement container, string title, string value)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 3;

            var label = new Label(title);
            label.style.fontSize = 10;
            label.style.color = Muted;
            label.style.minWidth = 60;
            row.Add(label);

            var path = new Label(value);
            path.style.fontSize = 10;
            path.style.whiteSpace = WhiteSpace.Normal;
            row.Add(path);

            container.Add(row);
        }

        #endregion

        #region Helpers

        private void SetStatus(string message, Color color)
        {
            if (_status == null) return;

            _status.text = message;
            _status.style.color = color;
        }

        private static Label MutedLabel(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 10;
            label.style.color = Muted;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static Label StatusBadge(string text, string styleClass)
        {
            var badge = UnityUtilsUIStyle.CreateBadge(text, styleClass);
            badge.style.alignSelf = Align.FlexStart;
            badge.style.marginTop = 2;
            badge.style.marginBottom = 4;
            return badge;
        }

        #endregion
    }
}
