using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Detects the legacy Path-o-logical PoolManager and migrates it to the UnityUtils pooling
    /// system: scene/prefab components are re-pointed to the new script GUID, scripts get the new
    /// namespace, and the legacy plugin is moved out of the project. Everything is backed up first.
    /// </summary>
    public static class LegacyPoolMigrator
    {
        public const string BackupRoot = "Library/UnityUtilsPoolMigrationBackup";
        private const string PendingSceneKey = "Wagenheimer.Pooling.PendingScene";
        private const string LegacyNamespace = "PathologicalGames";
        private const string NewNamespace = "Wagenheimer.UnityUtils.Pooling";

        private static readonly string[] LegacyFolders =
        {
            "Assets/Plugins/PathologicalGames",
            "Assets/Editor/PathologicalGames"
        };

        #region Data models

        public sealed class Detection
        {
            public bool LegacyPluginPresent;
            public bool Cancelled;
            public string LegacySpawnPoolPath;
            public string LegacySpawnPoolGuid;
            public string LegacyPreRuntimePath;
            public string LegacyPreRuntimeGuid;
            public string NewSpawnPoolPath;
            public string NewSpawnPoolGuid;
            public string NewPreRuntimePath;
            public string NewPreRuntimeGuid;

            public readonly List<string> AssetPaths = new List<string>();
            public readonly List<string> ScriptPaths = new List<string>();
            public readonly List<string> LegacyFolders = new List<string>();

            public int ComponentReferences;
            public int ScriptReferences;

            public bool HasLegacy => LegacyPluginPresent || AssetPaths.Count > 0 || ScriptPaths.Count > 0;

            public bool CanRun =>
                !Cancelled &&
                !string.IsNullOrEmpty(NewSpawnPoolGuid) &&
                !string.IsNullOrEmpty(NewPreRuntimeGuid) &&
                (!LegacyPluginPresent || !string.IsNullOrEmpty(LegacySpawnPoolGuid));

            public string Summary
            {
                get
                {
                    if (!HasLegacy) return "No legacy PoolManager detected.";

                    return $"{ComponentReferences} component reference(s) in {AssetPaths.Count} asset(s), " +
                           $"{ScriptReferences} legacy API reference(s) in {ScriptPaths.Count} script(s)" +
                           (LegacyPluginPresent ? ", legacy plugin present." : ".");
                }
            }
        }

        [Serializable]
        private sealed class BackupManifest
        {
            public string timestamp;
            public string unityVersion;
            public string note;
            public List<string> files = new List<string>();
            public List<string> removedFolders = new List<string>();
        }

        public sealed class BackupInfo
        {
            public string Path;
            public string Timestamp;
            public string Note;
            public int FileCount;
            public int RemovedFolderCount;

            public string DisplayName => $"{Timestamp}  ({FileCount} file(s))";
        }

        public sealed class MigrationResult
        {
            public bool Success;
            public bool Cancelled;
            public int AssetsChanged;
            public int ComponentsConverted;
            public int ScriptsChanged;
            public int FoldersRemoved;
            public string BackupPath;
            public readonly List<string> Errors = new List<string>();

            public string Summary
            {
                get
                {
                    if (Cancelled) return "Migration cancelled.";

                    var text = Success
                        ? $"Migration complete: {AssetsChanged} asset(s), {ComponentsConverted} component(s), " +
                          $"{ScriptsChanged} script(s), {FoldersRemoved} legacy folder(s)."
                        : "Migration failed.";

                    if (!string.IsNullOrEmpty(BackupPath))
                        text += $" Backup: {BackupPath}";

                    if (Errors.Count > 0)
                        text += $" {Errors.Count} warning(s); see the console.";

                    return text;
                }
            }
        }

        #endregion

        #region Detection

        public static Detection Detect(bool includeAssets = true, bool includeScripts = true)
        {
            var detection = new Detection();

            detection.NewSpawnPoolPath = FindScriptPath("SpawnPool", "Wagenheimer.UnityUtils.Pooling.SpawnPool", "Pooling/SpawnPool.cs", null);
            detection.NewSpawnPoolGuid = string.IsNullOrEmpty(detection.NewSpawnPoolPath) ? null : AssetDatabase.AssetPathToGUID(detection.NewSpawnPoolPath);

            detection.NewPreRuntimePath = FindScriptPath("PreRuntimePoolItem", "Wagenheimer.UnityUtils.Pooling.PreRuntimePoolItem", "Pooling/PreRuntimePoolItem.cs", null);
            detection.NewPreRuntimeGuid = string.IsNullOrEmpty(detection.NewPreRuntimePath) ? null : AssetDatabase.AssetPathToGUID(detection.NewPreRuntimePath);

            detection.LegacySpawnPoolPath = FindScriptPath("SpawnPool", "PathologicalGames.SpawnPool", "PathologicalGames/PoolManager/SpawnPool.cs", "Assets");
            detection.LegacySpawnPoolGuid = string.IsNullOrEmpty(detection.LegacySpawnPoolPath) ? null : AssetDatabase.AssetPathToGUID(detection.LegacySpawnPoolPath);

            detection.LegacyPreRuntimePath = FindScriptPath("PreRuntimePoolItem", "PathologicalGames.PreRuntimePoolItem", "PathologicalGames/PoolManager/PreRuntimePoolItem.cs", "Assets");
            detection.LegacyPreRuntimeGuid = string.IsNullOrEmpty(detection.LegacyPreRuntimePath) ? null : AssetDatabase.AssetPathToGUID(detection.LegacyPreRuntimePath);

            for (var i = 0; i < LegacyFolders.Length; i++)
            {
                if (AssetDatabase.IsValidFolder(LegacyFolders[i]))
                    detection.LegacyFolders.Add(LegacyFolders[i]);
            }

            detection.LegacyPluginPresent = detection.LegacyFolders.Count > 0 ||
                                            !string.IsNullOrEmpty(detection.LegacySpawnPoolGuid) ||
                                            !string.IsNullOrEmpty(detection.LegacyPreRuntimeGuid);

            if (includeAssets)
                ScanAssets(detection);

            if (includeScripts)
                ScanScripts(detection);

            return detection;
        }

        private static string FindScriptPath(string fileName, string fullClassName, string pathFragment, string root)
        {
            var roots = string.IsNullOrEmpty(root) ? null : new[] { root };
            var guids = AssetDatabase.FindAssets(fileName + " t:MonoScript", roots);
            string pathFallback = null;

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                var normalized = path.Replace('\\', '/');
                if (!string.IsNullOrEmpty(pathFragment) && normalized.Contains(pathFragment))
                    pathFallback = path;

                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                var type = script != null ? script.GetClass() : null;
                if (type != null && type.FullName == fullClassName)
                    return path;
            }

            // Compile errors make MonoScript.GetClass() return null; the path is a safe fallback.
            return pathFallback;
        }

        private static void ScanAssets(Detection detection)
        {
            var guids = new HashSet<string>(StringComparer.Ordinal);
            AddAssets("t:Scene", guids);
            AddAssets("t:Prefab", guids);
            AddAssets("t:ScriptableObject", guids);

            var paths = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            try
            {
                for (var i = 0; i < paths.Length; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Pool Migration", $"Scanning {paths[i]}", (float)i / Mathf.Max(1, paths.Length)))
                    {
                        detection.Cancelled = true;
                        break;
                    }

                    var count = CountLegacyGuids(paths[i], detection);
                    if (count <= 0) continue;

                    detection.AssetPaths.Add(paths[i]);
                    detection.ComponentReferences += count;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void AddAssets(string filter, HashSet<string> guids)
        {
            var found = AssetDatabase.FindAssets(filter, new[] { "Assets" });
            for (var i = 0; i < found.Length; i++)
                guids.Add(found[i]);
        }

        private static int CountLegacyGuids(string assetPath, Detection detection)
        {
            try
            {
                var text = File.ReadAllText(assetPath);
                var count = 0;

                if (!string.IsNullOrEmpty(detection.LegacySpawnPoolGuid))
                    count += CountOccurrences(text, "guid: " + detection.LegacySpawnPoolGuid);

                if (!string.IsNullOrEmpty(detection.LegacyPreRuntimeGuid))
                    count += CountOccurrences(text, "guid: " + detection.LegacyPreRuntimeGuid);

                return count;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PoolMigration] Could not read '{assetPath}': {ex.Message}");
                return 0;
            }
        }

        private static void ScanScripts(Detection detection)
        {
            // When the package is embedded under Assets/ (development), never rewrite its own
            // migrator/runtime scripts. Installed packages live under Packages/ and are not scanned.
            var packageRoot = GetPackageRoot(detection.NewSpawnPoolPath);

            var guids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });
            var paths = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .Where(p => !IsInsideLegacyFolder(p))
                .Where(p => !IsInsideFolder(p, packageRoot))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            for (var i = 0; i < paths.Length; i++)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                        "Pool Migration", $"Scanning {paths[i]}", (float)i / Mathf.Max(1, paths.Length)))
                {
                    detection.Cancelled = true;
                    break;
                }

                try
                {
                    var text = File.ReadAllText(paths[i]);
                    if (!UsesLegacyApi(text)) continue;

                    detection.ScriptPaths.Add(paths[i]);
                    detection.ScriptReferences += CountLegacyReferences(text);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PoolMigration] Could not read '{paths[i]}': {ex.Message}");
                }
            }

            EditorUtility.ClearProgressBar();
        }

        private static string GetPackageRoot(string newSpawnPoolPath)
        {
            if (string.IsNullOrEmpty(newSpawnPoolPath)) return null;

            var normalized = newSpawnPoolPath.Replace('\\', '/');
            var marker = normalized.IndexOf("/Runtime/Pooling/", StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
                marker = normalized.IndexOf("/Editor/Pooling/", StringComparison.OrdinalIgnoreCase);

            return marker > 0 ? normalized.Substring(0, marker) : null;
        }

        private static bool IsInsideFolder(string path, string folder)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(folder)) return false;

            var normalized = path.Replace('\\', '/');
            return normalized.StartsWith(folder.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInsideLegacyFolder(string path)
        {
            var normalized = path.Replace('\\', '/');
            for (var i = 0; i < LegacyFolders.Length; i++)
            {
                if (normalized.StartsWith(LegacyFolders[i] + "/", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalized, LegacyFolders[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool UsesLegacyApi(string text)
        {
            return Regex.IsMatch(text, @"using\s+" + Regex.Escape(LegacyNamespace) + @"\s*;") ||
                   text.Contains(LegacyNamespace + ".") ||
                   text.Contains("PoolManager.Pools") ||
                   text.Contains("SpawnPoolsDict") ||
                   text.Contains("InstanceHandler.") ||
                   text.Contains("ReadOnlyDictionary") ||
                   text.Contains("PreRuntimePoolItem") ||
                   ((Regex.IsMatch(text, @"\bSpawnPool\b") || Regex.IsMatch(text, @"\bPrefabPool\b")) &&
                    !text.Contains(NewNamespace));
        }

        private static int CountLegacyReferences(string text)
        {
            var count = 0;
            count += CountOccurrences(text, LegacyNamespace);
            count += CountOccurrences(text, "PoolManager.Pools");
            count += CountOccurrences(text, "SpawnPoolsDict");
            count += CountOccurrences(text, "InstanceHandler.");
            count += CountOccurrences(text, "ReadOnlyDictionary");
            count += CountOccurrences(text, "PreRuntimePoolItem");
            return count;
        }

        private static int CountOccurrences(string text, string value)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value)) return 0;

            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        #endregion

        #region Migration

        public static MigrationResult Migrate(Detection detection = null, bool removeLegacyPlugin = true)
        {
            detection ??= Detect();

            var result = new MigrationResult();

            if (detection.Cancelled)
            {
                result.Cancelled = true;
                return result;
            }

            if (!detection.HasLegacy)
            {
                result.Success = true;
                return result;
            }

            if (!detection.CanRun)
            {
                result.Errors.Add("The new pooling scripts were not found. Make sure com.wagenheimer.unityutils is installed " +
                                  "and compiled before migrating.");
                Debug.LogError("[PoolMigration] " + result.Errors[0]);
                return result;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                result.Cancelled = true;
                return result;
            }

            // Back up before touching anything. If a file cannot be backed up, abort instead of
            // rewriting it without a restore point.
            var backupPath = CreateBackup(detection, out var manifest);
            result.BackupPath = backupPath;

            var expectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < detection.AssetPaths.Count; i++) expectedFiles.Add(detection.AssetPaths[i]);
            for (var i = 0; i < detection.ScriptPaths.Count; i++) expectedFiles.Add(detection.ScriptPaths[i]);

            if (manifest.files.Count < expectedFiles.Count)
            {
                result.Errors.Add("The backup is incomplete; migration was aborted so no file is modified without a restore point.");
                Debug.LogError("[PoolMigration] " + result.Errors[0]);
                return result;
            }

            if (removeLegacyPlugin && manifest.removedFolders.Count < detection.LegacyFolders.Count)
            {
                result.Errors.Add("The legacy plugin folder backup is incomplete; migration was aborted.");
                Debug.LogError("[PoolMigration] " + result.Errors[0]);
                return result;
            }

            var activeScenePath = EditorSceneManager.GetActiveScene().path;
            var hasScenes = detection.AssetPaths.Any(p => p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase));

            // A prefab stage keeps an in-memory copy that would overwrite the file we edit on disk.
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                StageUtility.GoToMainStage();

            if (hasScenes)
            {
                SessionState.SetString(PendingSceneKey, activeScenePath ?? "");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            try
            {
                AssetDatabase.StartAssetEditing();

                for (var i = 0; i < detection.AssetPaths.Count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Pool Migration", $"Converting {detection.AssetPaths[i]}", (float)i / Mathf.Max(1, detection.AssetPaths.Count)))
                    {
                        result.Cancelled = true;
                        break;
                    }

                    var converted = ConvertAsset(detection.AssetPaths[i], detection, result);
                    if (converted > 0)
                    {
                        result.AssetsChanged++;
                        result.ComponentsConverted += converted;
                    }
                }

                if (!result.Cancelled)
                {
                    for (var i = 0; i < detection.ScriptPaths.Count; i++)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "Pool Migration", $"Updating {detection.ScriptPaths[i]}", (float)i / Mathf.Max(1, detection.ScriptPaths.Count)))
                        {
                            result.Cancelled = true;
                            break;
                        }

                        if (ConvertScript(detection.ScriptPaths[i]))
                        {
                            result.ScriptsChanged++;
                        }
                        else
                        {
                            // A false return can mean "nothing to change" or a failure; only the
                            // latter must block deleting the legacy plugin.
                            try
                            {
                                if (UsesLegacyApi(File.ReadAllText(detection.ScriptPaths[i])))
                                    result.Errors.Add($"Could not migrate script '{detection.ScriptPaths[i]}'.");
                            }
                            catch
                            {
                                result.Errors.Add($"Could not verify script '{detection.ScriptPaths[i]}'.");
                            }
                        }
                    }
                }

                // Never delete the legacy plugin if any asset/script conversion failed: the
                // project would be left with missing scripts and no way to compile.
                if (!result.Cancelled && result.Errors.Count == 0 && removeLegacyPlugin)
                {
                    for (var i = 0; i < detection.LegacyFolders.Count; i++)
                    {
                        var folder = detection.LegacyFolders[i];
                        if (!AssetDatabase.IsValidFolder(folder)) continue;

                        if (AssetDatabase.DeleteAsset(folder))
                            result.FoldersRemoved++;
                        else
                            result.Errors.Add($"Could not delete legacy folder '{folder}'. Remove it manually.");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(ex.Message);
                Debug.LogException(ex);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
            }

            result.Success = !result.Cancelled && result.Errors.Count == 0;

            // Make Unity import the rewritten assets/scripts and the deleted plugin together.
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            if (hasScenes)
                EditorApplication.delayCall += RestorePendingScene;

            if (result.Errors.Count > 0)
            {
                foreach (var error in result.Errors)
                    Debug.LogWarning("[PoolMigration] " + error);
            }

            Debug.Log("[PoolMigration] " + result.Summary);
            return result;
        }

        private static string CreateBackup(Detection detection, out BackupManifest manifest)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var root = Path.Combine(BackupRoot, timestamp);

            manifest = new BackupManifest
            {
                timestamp = timestamp,
                unityVersion = Application.unityVersion,
                note = "Created by UnityUtils LegacyPoolMigrator"
            };

            var filesRoot = Path.Combine(root, "files");

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < detection.AssetPaths.Count; i++) paths.Add(detection.AssetPaths[i]);
            for (var i = 0; i < detection.ScriptPaths.Count; i++) paths.Add(detection.ScriptPaths[i]);

            foreach (var path in paths)
            {
                try
                {
                    var destination = Path.Combine(filesRoot, path);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? filesRoot);
                    File.Copy(path, destination, true);
                    manifest.files.Add(path);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PoolMigration] Could not back up '{path}': {ex.Message}");
                }
            }

            var removedRoot = Path.Combine(root, "removed");
            for (var i = 0; i < detection.LegacyFolders.Count; i++)
            {
                var folder = detection.LegacyFolders[i];
                try
                {
                    var destination = Path.Combine(removedRoot, folder);
                    CopyDirectory(folder, destination);
                    manifest.removedFolders.Add(folder);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PoolMigration] Could not back up legacy folder '{folder}': {ex.Message}");
                }
            }

            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "manifest.json"), JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));
            return root;
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, directory);
                Directory.CreateDirectory(Path.Combine(destination, relative));
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                var target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destination);
                File.Copy(file, target, true);
            }
        }

        private static int ConvertAsset(string assetPath, Detection detection, MigrationResult result)
        {
            try
            {
                var text = File.ReadAllText(assetPath);
                var converted = 0;

                if (!string.IsNullOrEmpty(detection.LegacySpawnPoolGuid))
                    converted += ReplaceAll(ref text, "guid: " + detection.LegacySpawnPoolGuid, "guid: " + detection.NewSpawnPoolGuid);

                if (!string.IsNullOrEmpty(detection.LegacyPreRuntimeGuid))
                    converted += ReplaceAll(ref text, "guid: " + detection.LegacyPreRuntimeGuid, "guid: " + detection.NewPreRuntimeGuid);

                if (converted > 0)
                    WriteTextPreservingEncoding(assetPath, text);

                return converted;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Could not convert '{assetPath}': {ex.Message}");
                return 0;
            }
        }

        private static bool ConvertScript(string scriptPath)
        {
            try
            {
                var text = File.ReadAllText(scriptPath);
                var original = text;

                if (HasUsing(text, NewNamespace))
                {
                    // Avoid a duplicate using when the file already references the new namespace.
                    text = Regex.Replace(text, @"^[ \t]*using\s+" + Regex.Escape(LegacyNamespace) + @"\s*;[ \t]*\r?\n?",
                        string.Empty, RegexOptions.Multiline);
                }
                else
                {
                    text = Regex.Replace(text, @"using\s+" + Regex.Escape(LegacyNamespace) + @"\s*;",
                        "using " + NewNamespace + ";");
                }

                text = text.Replace(LegacyNamespace + ".", NewNamespace + ".");

                if (UsesLegacyApi(text) && !HasUsing(text, NewNamespace))
                    text = AddUsing(text, NewNamespace);

                if (text == original) return false;

                WriteTextPreservingEncoding(scriptPath, text);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PoolMigration] Could not update '{scriptPath}': {ex.Message}");
                return false;
            }
        }

        private static bool HasUsing(string text, string ns)
        {
            return Regex.IsMatch(text, @"using\s+" + Regex.Escape(ns) + @"\s*;");
        }

        private static string AddUsing(string text, string ns)
        {
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var match = Regex.Match(text, @"^(?<block>(?:[ \t]*using[^\r\n]*;[\r\n]+)+)", RegexOptions.Multiline);

            if (match.Success)
                return text.Insert(match.Index + match.Length, $"using {ns};{newline}");

            return $"using {ns};{newline}{text}";
        }

        private static int ReplaceAll(ref string text, string oldValue, string newValue)
        {
            var count = CountOccurrences(text, oldValue);
            if (count > 0)
                text = text.Replace(oldValue, newValue, StringComparison.Ordinal);

            return count;
        }

        private static void WriteTextPreservingEncoding(string path, string text)
        {
            var bytes = File.ReadAllBytes(path);
            var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            File.WriteAllText(path, text, new UTF8Encoding(hasBom));
        }

        #endregion

        #region Scene restore

        [InitializeOnLoadMethod]
        private static void RestoreSceneAfterDomainReload()
        {
            var path = SessionState.GetString(PendingSceneKey, "");
            if (string.IsNullOrEmpty(path)) return;

            SessionState.EraseString(PendingSceneKey);
            EditorApplication.delayCall += () => OpenSceneIfPossible(path);
        }

        private static void RestorePendingScene()
        {
            var path = SessionState.GetString(PendingSceneKey, "");
            SessionState.EraseString(PendingSceneKey);
            OpenSceneIfPossible(path);
        }

        private static void OpenSceneIfPossible(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            try
            {
                if (EditorSceneManager.GetActiveScene().path != path)
                    EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PoolMigration] Could not reopen '{path}': {ex.Message}");
            }
        }

        #endregion

        #region Backups

        public static List<BackupInfo> GetBackups()
        {
            var backups = new List<BackupInfo>();
            if (!Directory.Exists(BackupRoot)) return backups;

            foreach (var directory in Directory.GetDirectories(BackupRoot))
            {
                var manifestPath = Path.Combine(directory, "manifest.json");
                var info = new BackupInfo
                {
                    Path = directory.Replace('\\', '/'),
                    Timestamp = Path.GetFileName(directory),
                    Note = ""
                };

                if (File.Exists(manifestPath))
                {
                    try
                    {
                        var manifest = JsonUtility.FromJson<BackupManifest>(File.ReadAllText(manifestPath));
                        if (manifest != null)
                        {
                            info.Timestamp = string.IsNullOrEmpty(manifest.timestamp) ? info.Timestamp : manifest.timestamp;
                            info.Note = manifest.note ?? "";
                            info.FileCount = manifest.files != null ? manifest.files.Count : 0;
                            info.RemovedFolderCount = manifest.removedFolders != null ? manifest.removedFolders.Count : 0;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PoolMigration] Could not read backup manifest '{manifestPath}': {ex.Message}");
                    }
                }

                backups.Add(info);
            }

            return backups.OrderByDescending(b => b.Timestamp, StringComparer.Ordinal).ToList();
        }

        public static bool Restore(string backupPath)
        {
            if (string.IsNullOrEmpty(backupPath) || !Directory.Exists(backupPath))
            {
                Debug.LogError($"[PoolMigration] Backup not found: {backupPath}");
                return false;
            }

            var manifestPath = Path.Combine(backupPath, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                Debug.LogError($"[PoolMigration] Backup manifest not found: {manifestPath}");
                return false;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

            try
            {
                var manifest = JsonUtility.FromJson<BackupManifest>(File.ReadAllText(manifestPath));
                var filesRoot = Path.Combine(backupPath, "files");
                var removedRoot = Path.Combine(backupPath, "removed");

                if (manifest?.files != null)
                {
                    foreach (var relative in manifest.files)
                    {
                        var source = Path.Combine(filesRoot, relative);
                        if (!File.Exists(source)) continue;

                        Directory.CreateDirectory(Path.GetDirectoryName(relative) ?? ".");
                        File.Copy(source, relative, true);
                    }
                }

                if (manifest?.removedFolders != null)
                {
                    foreach (var relative in manifest.removedFolders)
                    {
                        var source = Path.Combine(removedRoot, relative);
                        if (!Directory.Exists(source)) continue;

                        CopyDirectory(source, relative);
                    }
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                Debug.Log($"[PoolMigration] Restored backup '{backupPath}'.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
        }

        #endregion
    }
}
