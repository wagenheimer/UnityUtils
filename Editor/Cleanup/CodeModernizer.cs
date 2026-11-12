using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Finds and rewrites source code that triggers common compiler/analyzer warnings:
    /// <list type="bullet">
    /// <item><description>UAC1001: a public DOTween <c>Tweener</c>/<c>Tween</c>/<c>Sequence</c> field in a MonoBehaviour is
    /// skipped by serialization. It gets <c>[System.NonSerialized]</c>, which states what Unity already does.</description></item>
    /// <item><description>Obsolete <c>FindObjectOfType</c>/<c>FindObjectsOfType</c> and the obsolete sort-mode overloads of
    /// <c>FindObjectsByType</c> are migrated to the replacement the installed Unity version offers.</description></item>
    /// <item><description>The deprecated <c>com.unity.analytics</c> API: <c>Analytics.CustomEvent(...)</c> statements are
    /// migrated to <c>Wagenheimer.Tracking.GameAnalytics.Track(...)</c> (Unity Analytics, package
    /// <c>com.wagenheimer.tracking</c>) when that package is installed; otherwise the scan reports what to install.</description></item>
    /// </list>
    /// Only files under a chosen folder are touched (default: the project's own code), anything inside a
    /// <c>UNITY_*</c> conditional compilation block is left alone, and the rewrite is a plain text edit
    /// that is easy to review in version control.
    /// </summary>
    public static class CodeModernizer
    {
        private sealed class Edit
        {
            public int Index;
            public int Length;
            public string Replacement;
            public int Line;
            public string Before;
            public string After;
        }

        public struct ScanResult
        {
            public int EditCount;
            public int AffectedFiles;
            public int SkippedFiles;
            /// <summary>Calls to the deprecated Analytics.CustomEvent found in scripts (migratable or not).</summary>
            public int LegacyAnalyticsCalls;
            /// <summary>Findings that need a manual step (install a package, remove a package, hand edits).</summary>
            public int AdvisoryCount;
            public List<string> Details;
            public List<CleanupIssueItem> Items;
        }

        public struct CleanResult
        {
            public int ChangesApplied;
            public int FilesChanged;
            public int FilesSkipped;
            public List<CleanupModificationRecord> Modifications;
        }

        // Analytics.CustomEvent(...) / UnityEngine.Analytics.Analytics.CustomEvent(...), never a member of another type.
        private static readonly Regex LegacyCustomEvent = new Regex(
            @"(?<![\w.])(?:UnityEngine\.Analytics\.)?Analytics\.CustomEvent\s*\(", RegexOptions.CultureInvariant);

        private const string GameAnalyticsCall = "Wagenheimer.Tracking.GameAnalytics.Track(";

        private static readonly bool HasGameAnalytics = AppDomain.CurrentDomain.GetAssemblies()
            .Any(a => a.GetType("Wagenheimer.Tracking.GameAnalytics", false) != null);

        private const string ScopePreference = "WagenheimerUnityUtils.CodeModernizer.Scope";
        private const string NonSerializedAttribute = "[System.NonSerialized] ";

        // public [attrs] Tweener name;   (single declaration, optional initializer, one line)
        private static readonly Regex TweenField = new Regex(
            @"^(?<indent>[ \t]*)(?<attrs>(?:\[[^\]\r\n]*\][ \t]*)*)(?<mod>public)[ \t]+(?<type>(?:DG\.Tweening\.)?(?:Tweener|Tween|Sequence))[ \t]+(?<name>\w+)[ \t]*(?:=[^;\r\n]*)?;",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        private static readonly Regex FindObjectGeneric = new Regex(
            @"\bFindObjectOfType\s*<(?<t>[^<>()]+)>\s*\(\s*(?<arg>true|false)?\s*\)", RegexOptions.CultureInvariant);

        private static readonly Regex FindObjectByType = new Regex(
            @"\bFindObjectOfType\s*\(\s*(?<expr>typeof\s*\([^()]*\))\s*(?:,\s*(?<arg>true|false)\s*)?\)", RegexOptions.CultureInvariant);

        private static readonly Regex FindObjectsGeneric = new Regex(
            @"\bFindObjectsOfType\s*<(?<t>[^<>()]+)>\s*\(\s*(?<arg>true|false)?\s*\)", RegexOptions.CultureInvariant);

        private static readonly Regex FindObjectsByTypeof = new Regex(
            @"\bFindObjectsOfType\s*\(\s*(?<expr>typeof\s*\([^()]*\))\s*(?:,\s*(?<arg>true|false)\s*)?\)", RegexOptions.CultureInvariant);

        private static readonly Regex SortModeWithInactive = new Regex(
            @"\bFindObjectsByType\s*<(?<t>[^<>()]+)>\s*\(\s*(?<inactive>FindObjectsInactive\.\w+)\s*,\s*FindObjectsSortMode\.\w+\s*\)",
            RegexOptions.CultureInvariant);

        private static readonly Regex SortModeOnly = new Regex(
            @"\bFindObjectsByType\s*<(?<t>[^<>()]+)>\s*\(\s*FindObjectsSortMode\.\w+\s*\)", RegexOptions.CultureInvariant);

        // What the installed Unity offers, detected by reflection so the same tool works across versions.
        private static readonly MethodInfo[] UnityObjectMethods =
            typeof(UnityEngine.Object).GetMethods(BindingFlags.Public | BindingFlags.Static);

        private static readonly bool HasFindFirst = UnityObjectMethods.Any(m => m.Name == "FindFirstObjectByType");

        private static readonly bool HasUnsortedFindObjectsByType = UnityObjectMethods.Any(m =>
            m.Name == "FindObjectsByType" && m.IsGenericMethodDefinition &&
            m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(FindObjectsInactive));

        /// <summary>The folder whose scripts are scanned and edited. Defaults to Assets/_Game when it exists.</summary>
        public static string ScopeFolder
        {
            get
            {
                var saved = EditorPrefs.GetString(ScopePreference, string.Empty);
                if (!string.IsNullOrEmpty(saved)) return saved;
                return Directory.Exists("Assets/_Game") ? "Assets/_Game" : "Assets";
            }
            set => EditorPrefs.SetString(ScopePreference, (value ?? string.Empty).Replace('\\', '/').TrimEnd('/'));
        }

        public static IEnumerator ScanRoutine(CleanupProgress progress, Action<ScanResult> done)
        {
            var scan = new ScanResult { Details = new List<string>(), Items = new List<CleanupIssueItem>() };
            var files = EnumerateScripts();

            progress.NextStage("Scanning source code", files.Count);

            foreach (var path in files)
            {
                if (progress.CancelRequested) break;

                progress.Advance(path);

                if (!TryRead(path, out var text, out _))
                {
                    scan.SkippedFiles++;
                    yield return null;
                    continue;
                }

                scan.LegacyAnalyticsCalls += LegacyCustomEvent.Matches(text).Count;

                var edits = FindEdits(text);
                if (edits.Count > 0)
                {
                    scan.AffectedFiles++;
                    scan.EditCount += edits.Count;
                    scan.Details.Add($"{path}: {edits.Count} change(s)");

                    foreach (var edit in edits)
                    {
                        scan.Items.Add(new CleanupIssueItem
                        {
                            AssetPath = path,
                            TargetName = Path.GetFileName(path),
                            Description = $"line {edit.Line}:  {edit.Before}   ->   {edit.After}",
                            Category = "Source Code",
                            IsScene = false,
                            IsPrefab = false,
                            Count = 1
                        });
                    }
                }

                yield return null;
            }

            AddLegacyAnalyticsAdvisory(scan);
            done(scan);
        }

        // Explains the analytics migration even when no edit can be applied yet (new package missing, or
        // calls whose return value is used and so need a hand edit).
        private static void AddLegacyAnalyticsAdvisory(ScanResult scan)
        {
            var packageInManifest = File.Exists("Packages/manifest.json") &&
                                    File.ReadAllText("Packages/manifest.json").Contains("\"com.unity.analytics\"");

            string message = null;
            if (scan.LegacyAnalyticsCalls > 0 && !HasGameAnalytics)
                message = $"{scan.LegacyAnalyticsCalls} call(s) to the deprecated Analytics.CustomEvent (com.unity.analytics). " +
                          "Install com.wagenheimer.tracking (GameAnalytics on Unity Analytics) and run this fix to migrate them.";
            else if (scan.LegacyAnalyticsCalls > 0 && HasGameAnalytics)
                message = $"{scan.LegacyAnalyticsCalls} call(s) to the deprecated Analytics.CustomEvent in total; those used as an " +
                          "expression (return value read) must be migrated by hand to GameAnalytics.Track.";
            else if (packageInManifest && HasGameAnalytics)
                message = "No Analytics.CustomEvent calls are left: remove the deprecated com.unity.analytics package from Packages/manifest.json.";

            if (message == null) return;

            scan.AdvisoryCount++;
            scan.Details.Add(message);
            scan.Items.Add(new CleanupIssueItem
            {
                AssetPath = "Packages/manifest.json",
                TargetName = "com.unity.analytics",
                Description = message,
                Category = "Analytics",
                IsScene = false,
                IsPrefab = false,
                Count = Math.Max(1, scan.LegacyAnalyticsCalls)
            });
        }

        public static IEnumerator FixRoutine(CleanupProgress progress, Action<CleanResult> done)
        {
            var result = new CleanResult { Modifications = new List<CleanupModificationRecord>() };
            var files = EnumerateScripts();

            progress.NextStage("Rewriting source code", files.Count);

            foreach (var path in files)
            {
                if (progress.CancelRequested) break;

                progress.Advance(path);

                if (!TryRead(path, out var text, out var hadBom))
                {
                    result.FilesSkipped++;
                    yield return null;
                    continue;
                }

                var edits = FindEdits(text);
                if (edits.Count > 0 && TryWrite(path, Apply(text, edits), hadBom))
                {
                    result.FilesChanged++;
                    result.ChangesApplied += edits.Count;
                    result.Modifications.Add(new CleanupModificationRecord
                    {
                        AssetPath = path,
                        ActionTaken = $"Applied {edits.Count} change(s)",
                        ItemsFixed = edits.Count,
                        IsScene = false,
                        IsPrefab = false
                    });
                }

                yield return null;
            }

            if (result.FilesChanged > 0)
                AssetDatabase.Refresh(); // Starts the recompile so the warnings can be confirmed gone.

            done(result);
        }

        // ------------------------------------------------------------------ discovery

        private static List<string> EnumerateScripts()
        {
            var scope = ScopeFolder;
            if (string.IsNullOrEmpty(scope) || !scope.StartsWith("Assets", StringComparison.Ordinal) || !Directory.Exists(scope))
                return new List<string>();

            var includeSamples = CleanupAssetUtility.IncludeSamples;

            return Directory.GetFiles(scope, "*.cs", SearchOption.AllDirectories)
                .Select(p => p.Replace('\\', '/'))
                .Where(p => includeSamples || !CleanupAssetUtility.IsSampleOrDemoPath(p))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ------------------------------------------------------------------ rules

        private static List<Edit> FindEdits(string text)
        {
            var edits = new List<Edit>();
            var lineStarts = ComputeLineStarts(text);
            var skipLine = ComputeConditionalLines(text, lineStarts.Count);

            bool ShouldSkip(int index) => skipLine[LineOf(lineStarts, index)];

            AddTweenFieldEdits(text, edits, lineStarts, ShouldSkip);

            if (HasFindFirst)
                AddFindApiEdits(text, edits, lineStarts, ShouldSkip);

            if (HasGameAnalytics)
                AddLegacyAnalyticsEdits(text, edits, lineStarts, ShouldSkip);

            // Keep edits ordered and non-overlapping (two rules never target the same span in practice).
            edits.Sort((a, b) => a.Index.CompareTo(b.Index));
            for (var i = edits.Count - 1; i > 0; i--)
            {
                if (edits[i].Index < edits[i - 1].Index + edits[i - 1].Length)
                    edits.RemoveAt(i);
            }

            return edits;
        }

        private static void AddTweenFieldEdits(string text, List<Edit> edits, List<int> lineStarts, Func<int, bool> shouldSkip)
        {
            // Only types Unity serializes: a field on a plain class is never reported by the analyzer.
            if (!text.Contains("MonoBehaviour") && !text.Contains("ScriptableObject") && !text.Contains("Serializable"))
                return;

            foreach (Match match in TweenField.Matches(text))
            {
                var mod = match.Groups["mod"];
                if (shouldSkip(mod.Index)) continue;
                if (match.Groups["attrs"].Value.Contains("NonSerialized")) continue;
                if (PreviousLineIsNonSerialized(text, match.Index)) continue;

                var before = match.Value.Trim();
                var offset = mod.Index - match.Index - (match.Value.Length - match.Value.TrimStart().Length);
                var after = before.Insert(Math.Max(0, offset), NonSerializedAttribute);

                edits.Add(new Edit
                {
                    Index = mod.Index,
                    Length = 0,
                    Replacement = NonSerializedAttribute,
                    Line = LineOf(lineStarts, mod.Index) + 1,
                    Before = before,
                    After = after
                });
            }
        }

        private static void AddLegacyAnalyticsEdits(string text, List<Edit> edits, List<int> lineStarts, Func<int, bool> shouldSkip)
        {
            foreach (Match m in LegacyCustomEvent.Matches(text))
            {
                // Only statements: GameAnalytics.Track returns void, so a call whose result is used stays manual.
                if (!IsStatementStart(text, m.Index)) continue;

                AddReplace(edits, lineStarts, shouldSkip, m, GameAnalyticsCall);
            }
        }

        private static bool IsStatementStart(string text, int index)
        {
            for (var i = index - 1; i >= 0; i--)
            {
                var c = text[i];
                if (char.IsWhiteSpace(c)) continue;
                return c == ';' || c == '{' || c == '}' || c == ')';
            }

            return true;
        }

        private static void AddFindApiEdits(string text, List<Edit> edits, List<int> lineStarts, Func<int, bool> shouldSkip)
        {
            foreach (Match m in FindObjectGeneric.Matches(text))
                AddReplace(edits, lineStarts, shouldSkip, m, $"FindFirstObjectByType<{m.Groups["t"].Value.Trim()}>({InactiveArgument(m)})");

            foreach (Match m in FindObjectByType.Matches(text))
                AddReplace(edits, lineStarts, shouldSkip, m, $"FindFirstObjectByType({m.Groups["expr"].Value}{InactiveSuffix(m)})");

            foreach (Match m in FindObjectsGeneric.Matches(text))
                AddReplace(edits, lineStarts, shouldSkip, m, $"FindObjectsByType<{m.Groups["t"].Value.Trim()}>({FindObjectsArguments(m)})");

            foreach (Match m in FindObjectsByTypeof.Matches(text))
            {
                var inactive = InactiveArgument(m);
                var args = HasUnsortedFindObjectsByType
                    ? m.Groups["expr"].Value + (inactive.Length > 0 ? ", " + inactive : string.Empty)
                    : m.Groups["expr"].Value + ", " + (inactive.Length > 0 ? inactive + ", " : string.Empty) + "FindObjectsSortMode.None";
                AddReplace(edits, lineStarts, shouldSkip, m, $"FindObjectsByType({args})");
            }

            // The sort-mode overloads are obsolete in the newest Unity versions.
            if (HasUnsortedFindObjectsByType)
            {
                foreach (Match m in SortModeWithInactive.Matches(text))
                    AddReplace(edits, lineStarts, shouldSkip, m, $"FindObjectsByType<{m.Groups["t"].Value.Trim()}>({m.Groups["inactive"].Value})");

                foreach (Match m in SortModeOnly.Matches(text))
                    AddReplace(edits, lineStarts, shouldSkip, m, $"FindObjectsByType<{m.Groups["t"].Value.Trim()}>()");
            }
        }

        private static string InactiveArgument(Match m)
        {
            var arg = m.Groups["arg"].Value;
            return arg == "true" ? "FindObjectsInactive.Include" : arg == "false" ? "FindObjectsInactive.Exclude" : string.Empty;
        }

        private static string InactiveSuffix(Match m)
        {
            var inactive = InactiveArgument(m);
            return inactive.Length > 0 ? ", " + inactive : string.Empty;
        }

        // FindObjectsOfType<T>(includeInactive) -> the arguments of the matching FindObjectsByType overload.
        private static string FindObjectsArguments(Match m)
        {
            var inactive = InactiveArgument(m);

            if (HasUnsortedFindObjectsByType)
                return inactive;

            return inactive.Length > 0 ? inactive + ", FindObjectsSortMode.None" : "FindObjectsSortMode.None";
        }

        private static void AddReplace(List<Edit> edits, List<int> lineStarts, Func<int, bool> shouldSkip, Match match, string replacement)
        {
            if (shouldSkip(match.Index)) return;

            edits.Add(new Edit
            {
                Index = match.Index,
                Length = match.Length,
                Replacement = replacement,
                Line = LineOf(lineStarts, match.Index) + 1,
                Before = match.Value,
                After = replacement
            });
        }

        // ------------------------------------------------------------------ text helpers

        private static string Apply(string text, List<Edit> edits)
        {
            var builder = new StringBuilder(text);
            for (var i = edits.Count - 1; i >= 0; i--)
            {
                var edit = edits[i];
                builder.Remove(edit.Index, edit.Length);
                builder.Insert(edit.Index, edit.Replacement);
            }

            return builder.ToString();
        }

        private static bool PreviousLineIsNonSerialized(string text, int matchIndex)
        {
            var end = text.LastIndexOf('\n', Math.Max(0, matchIndex - 1));
            if (end <= 0) return false;

            var start = text.LastIndexOf('\n', end - 1);
            var previous = text.Substring(start + 1, end - start - 1).Trim();
            return previous.StartsWith("[NonSerialized", StringComparison.Ordinal) ||
                   previous.StartsWith("[System.NonSerialized", StringComparison.Ordinal);
        }

        private static List<int> ComputeLineStarts(string text)
        {
            var starts = new List<int> { 0 };
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                    starts.Add(i + 1);
            }

            return starts;
        }

        private static int LineOf(List<int> lineStarts, int index)
        {
            var line = lineStarts.BinarySearch(index);
            return line >= 0 ? line : ~line - 1;
        }

        // Lines inside any #if/#elif/#else block that mentions a UNITY_ symbol are left alone: those blocks
        // usually exist precisely because the code targets specific Unity versions.
        private static bool[] ComputeConditionalLines(string text, int lineCount)
        {
            var skip = new bool[lineCount];
            var stack = new Stack<bool>();
            var depth = 0;
            var lines = text.Split('\n');

            for (var i = 0; i < lines.Length && i < lineCount; i++)
            {
                var trimmed = lines[i].TrimStart();

                if (trimmed.StartsWith("#if", StringComparison.Ordinal))
                {
                    var isUnityConditional = trimmed.Contains("UNITY_");
                    stack.Push(isUnityConditional);
                    if (isUnityConditional) depth++;
                }

                skip[i] = depth > 0;

                if (trimmed.StartsWith("#endif", StringComparison.Ordinal) && stack.Count > 0 && stack.Pop())
                    depth--;
            }

            return skip;
        }

        private static bool TryRead(string path, out string text, out bool hadBom)
        {
            text = null;
            hadBom = false;

            try
            {
                var bytes = File.ReadAllBytes(path);
                hadBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

                // Strict decoding: a file that is not valid UTF-8 is skipped rather than silently corrupted.
                var encoding = new UTF8Encoding(false, true);
                text = encoding.GetString(bytes, hadBom ? 3 : 0, bytes.Length - (hadBom ? 3 : 0));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryWrite(string path, string text, bool bom)
        {
            try
            {
                var body = new UTF8Encoding(false).GetBytes(text);
                var bytes = bom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray() : body;
                File.WriteAllBytes(path, bytes);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CodeModernizer] Could not write '{path}': {ex.Message}");
                return false;
            }
        }
    }
}
