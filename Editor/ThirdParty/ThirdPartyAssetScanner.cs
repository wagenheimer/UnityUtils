using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Audits a third-party asset pack and resolves what is actually used by the
    /// project. Scenes, prefabs and materials only reference scripts and shaders
    /// through GUIDs, never class names, so every lookup is GUID-based.
    /// The pack's own Examples/Doc folders are excluded from reference counting,
    /// otherwise the bundled demo content makes every component look "in use".
    /// </summary>
    public static class ThirdPartyAssetScanner
    {
        private static readonly Regex ShaderNameRegex =
            new Regex("^\\s*Shader\\s+\"([^\"]+)\"", RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex DeclaredShaderRegex =
            new Regex("string\\s+shader\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled);

        private static readonly Regex GuidRegex =
            new Regex("guid:\\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);

        private static readonly Regex MaterialShaderRegex =
            new Regex("m_Shader:\\s*\\{[^}]*?guid:\\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);

        private static readonly Regex ResourcesLoadRegex =
            new Regex("Resources\\.Load\\s*\\(\\s*\"([^\"]+)\"", RegexOptions.Compiled);

        /// <summary>Locates the default 2DxFX installation, preferring the shallowest folder named "2DxFX".</summary>
        public static ThirdPartyAssetProfile DetectDefaultProfile()
        {
            var profile = new ThirdPartyAssetProfile();

            var matches = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("2DxFX t:Folder"))
            {
                var path = Normalize(AssetDatabase.GUIDToAssetPath(guid));
                if (string.Equals(Path.GetFileName(path), "2DxFX", StringComparison.OrdinalIgnoreCase))
                    matches.Add(path);
            }

            if (matches.Count > 0)
            {
                matches.Sort((a, b) => a.Length.CompareTo(b.Length));
                profile.RootFolder = matches[0];
                return profile;
            }

            if (AssetDatabase.IsValidFolder("Assets/2DxFX"))
                profile.RootFolder = "Assets/2DxFX";

            return profile;
        }

        public static bool DetectUrp()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null) pipeline = GraphicsSettings.defaultRenderPipeline;
            if (pipeline == null) pipeline = QualitySettings.renderPipeline;
            if (pipeline == null) return false;

            var typeName = pipeline.GetType().FullName ?? string.Empty;
            return typeName.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("URP", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static ThirdPartyScanResult Scan(ThirdPartyAssetProfile profile)
        {
            var result = new ThirdPartyScanResult { Profile = profile };

            if (profile == null || !AssetDatabase.IsValidFolder(profile.RootFolder))
            {
                result.FatalError = "Package folder not found: " + (profile != null ? profile.RootFolder : "<null>");
                return result;
            }

            result.IsUrp = DetectUrp();

            try
            {
                ScanShaders(profile, result);
                ScanScripts(profile, result);
                ScanAssetReferences(profile, result);
                if (result.Cancelled)
                {
                    result.FatalError = "Scan cancelled before completion; results are incomplete.";
                    return result;
                }
                ScanMaterials(profile, result);
                if (result.Cancelled)
                {
                    result.FatalError = "Scan cancelled before completion; results are incomplete.";
                    return result;
                }
                MarkComponentsAndShaders(result);
                ClassifyResources(profile, result);
                CollectRemovableFolders(profile, result);
            }
            catch (Exception e)
            {
                result.FatalError = e.Message;
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return result;
        }

        private static void ScanShaders(ThirdPartyAssetProfile profile, ThirdPartyScanResult result)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Shader", new[] { profile.RootFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;

                var text = ReadText(path);
                result.Shaders.Add(new ThirdPartyShaderInfo
                {
                    Path = Normalize(path),
                    FileName = Path.GetFileName(path),
                    ShaderName = text != null ? GetShaderName(text) : null,
                    IsSurfaceShader = text != null && text.Contains("#pragma surface"),
                    InResources = ThirdPartyPathUtility.IsUnderFolder(path, profile.ResourcesFolder),
                    SizeBytes = ThirdPartyPathUtility.SafeFileSize(path)
                });
            }
        }

        private static void ScanScripts(ThirdPartyAssetProfile profile, ThirdPartyScanResult result)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", new[] { profile.RootFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                var text = ReadText(path);
                var isEditorScript = Normalize(path).Contains("/Editor/");

                var info = new ThirdPartyScriptInfo
                {
                    Path = Normalize(path),
                    FileName = Path.GetFileName(path),
                    ClassName = Path.GetFileNameWithoutExtension(path),
                    IsEditorScript = isEditorScript,
                    IsHelper = isEditorScript || text == null || !text.Contains("[AddComponentMenu"),
                    ShaderName = text != null ? GetDeclaredShader(text) : null,
                    SizeBytes = ThirdPartyPathUtility.SafeFileSize(path)
                };

                HashSet<string> runtime;
                HashSet<string> editorOnly;
                CollectResourcesLoads(text, out runtime, out editorOnly);
                info.RuntimeResourceNames.AddRange(runtime);
                info.EditorResourceNames.AddRange(editorOnly);

                result.Scripts.Add(info);
            }
        }

        private static void ScanAssetReferences(ThirdPartyAssetProfile profile, ThirdPartyScanResult result)
        {
            var scriptByGuid = new Dictionary<string, ThirdPartyScriptInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", new[] { profile.RootFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                for (var i = 0; i < result.Scripts.Count; i++)
                {
                    if (string.Equals(result.Scripts[i].Path, Normalize(path), StringComparison.OrdinalIgnoreCase))
                    {
                        scriptByGuid[guid] = result.Scripts[i];
                        break;
                    }
                }
            }

            var shaderByGuid = new Dictionary<string, ThirdPartyShaderInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var guid in AssetDatabase.FindAssets("t:Shader", new[] { profile.RootFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                for (var i = 0; i < result.Shaders.Count; i++)
                {
                    if (string.Equals(result.Shaders[i].Path, Normalize(path), StringComparison.OrdinalIgnoreCase))
                    {
                        shaderByGuid[guid] = result.Shaders[i];
                        break;
                    }
                }
            }

            var candidates = new HashSet<string>(scriptByGuid.Keys, StringComparer.OrdinalIgnoreCase);
            candidates.UnionWith(shaderByGuid.Keys);

            var assetPaths = new List<string>();
            assetPaths.AddRange(AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }));
            assetPaths.AddRange(AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }));

            for (var i = 0; i < assetPaths.Count; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(assetPaths[i]);
                if (string.IsNullOrEmpty(path)) continue;
                if (ThirdPartyPathUtility.IsUnderFolder(path, profile.RootFolder)) continue;

                if (EditorUtility.DisplayCancelableProgressBar("Third-Party Slimmer", "Scanning references: " + path, (float)i / assetPaths.Count))
                {
                    result.Cancelled = true;
                    return;
                }

                var text = ReadText(path);
                if (text == null) continue;

                foreach (Match match in GuidRegex.Matches(text))
                {
                    var guid = match.Groups[1].Value;
                    ThirdPartyScriptInfo script;
                    if (scriptByGuid.TryGetValue(guid, out script)) script.ReferenceCount++;
                    ThirdPartyShaderInfo shader;
                    if (shaderByGuid.TryGetValue(guid, out shader)) shader.ReferenceCount++;
                }
            }
        }

        private static void ScanMaterials(ThirdPartyAssetProfile profile, ThirdPartyScanResult result)
        {
            var shaderByGuid = new Dictionary<string, ThirdPartyShaderInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var guid in AssetDatabase.FindAssets("t:Shader", new[] { profile.RootFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                for (var i = 0; i < result.Shaders.Count; i++)
                {
                    if (string.Equals(result.Shaders[i].Path, Normalize(path), StringComparison.OrdinalIgnoreCase))
                    {
                        shaderByGuid[guid] = result.Shaders[i];
                        break;
                    }
                }
            }

            var materialGuids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < materialGuids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(materialGuids[i]);
                if (string.IsNullOrEmpty(path)) continue;
                if (ThirdPartyPathUtility.IsUnderFolder(path, profile.RootFolder)) continue;

                if (EditorUtility.DisplayCancelableProgressBar("Third-Party Slimmer", "Scanning materials: " + path, (float)i / Mathf.Max(1, materialGuids.Length)))
                {
                    result.Cancelled = true;
                    return;
                }

                var text = ReadText(path);
                if (text == null) continue;

                var match = MaterialShaderRegex.Match(text);
                if (!match.Success) continue;

                ThirdPartyShaderInfo shader;
                if (!shaderByGuid.TryGetValue(match.Groups[1].Value, out shader)) continue;

                shader.UsedByMaterial = true;
                var normalized = Normalize(path);
                if (seen.Add(normalized))
                    result.ExternalMaterialReferences.Add(normalized);
            }
        }

        private static void MarkComponentsAndShaders(ThirdPartyScanResult result)
        {
            var usedShaderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < result.Scripts.Count; i++)
            {
                var script = result.Scripts[i];
                if (script.IsUsed && !string.IsNullOrEmpty(script.ShaderName))
                    usedShaderNames.Add(script.ShaderName);
            }

            var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < result.Shaders.Count; i++)
            {
                var shader = result.Shaders[i];
                if (!string.IsNullOrEmpty(shader.ShaderName) && usedShaderNames.Contains(shader.ShaderName))
                {
                    shader.UsedByComponent = true;
                    matched.Add(shader.ShaderName);
                }
            }

            foreach (var name in usedShaderNames)
            {
                if (!matched.Contains(name))
                    result.Notes.Add("Referenced shader not found in package: " + name);
            }

            var hasReferences = false;
            for (var i = 0; i < result.Scripts.Count; i++)
            {
                if (result.Scripts[i].ReferenceCount > 0) { hasReferences = true; break; }
            }
            if (!hasReferences && result.Cancelled == false)
                result.Notes.Add("No pack component or shader is referenced outside the package's own Examples/Doc content.");
        }

        private static void ClassifyResources(ThirdPartyAssetProfile profile, ThirdPartyScanResult result)
        {
            if (!AssetDatabase.IsValidFolder(profile.ResourcesFolder)) return;

            var runtimeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var editorNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < result.Scripts.Count; i++)
            {
                var script = result.Scripts[i];
                if (!script.IsUsed) continue;
                for (var j = 0; j < script.RuntimeResourceNames.Count; j++) runtimeNames.Add(script.RuntimeResourceNames[j]);
                for (var j = 0; j < script.EditorResourceNames.Count; j++) editorNames.Add(script.EditorResourceNames[j]);
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Object", new[] { profile.ResourcesFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) continue;
                if (path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)) continue;
                if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;

                var resourceName = Path.GetFileNameWithoutExtension(path);
                ThirdPartyResourceKind kind;
                if (runtimeNames.Contains(resourceName))
                    kind = ThirdPartyResourceKind.Runtime;
                else if (editorNames.Contains(resourceName) || MatchesEditorPrefix(resourceName, profile))
                    kind = ThirdPartyResourceKind.EditorOnly;
                else
                    kind = ThirdPartyResourceKind.Unused;

                result.Resources.Add(new ThirdPartyResourceInfo
                {
                    Path = Normalize(path),
                    FileName = Path.GetFileName(path),
                    ResourceName = resourceName,
                    Kind = kind,
                    SizeBytes = ThirdPartyPathUtility.SafeFileSize(path)
                });
            }
        }

        private static void CollectRemovableFolders(ThirdPartyAssetProfile profile, ThirdPartyScanResult result)
        {
            for (var i = 0; i < profile.RemovableSubFolders.Length; i++)
            {
                var folder = profile.GetSubFolder(profile.RemovableSubFolders[i]);
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                if (ContainsUsedAsset(result, folder)) continue;
                result.RemovableFolderPaths.Add(folder);
            }
        }

        private static bool ContainsUsedAsset(ThirdPartyScanResult result, string folder)
        {
            for (var i = 0; i < result.Scripts.Count; i++)
                if (result.Scripts[i].IsUsed && ThirdPartyPathUtility.IsUnderFolder(result.Scripts[i].Path, folder)) return true;
            for (var i = 0; i < result.Shaders.Count; i++)
                if (result.Shaders[i].IsUsed && ThirdPartyPathUtility.IsUnderFolder(result.Shaders[i].Path, folder)) return true;
            for (var i = 0; i < result.Resources.Count; i++)
                if (result.Resources[i].Kind != ThirdPartyResourceKind.Unused &&
                    ThirdPartyPathUtility.IsUnderFolder(result.Resources[i].Path, folder)) return true;
            return false;
        }

        private static bool MatchesEditorPrefix(string resourceName, ThirdPartyAssetProfile profile)
        {
            for (var i = 0; i < profile.EditorResourceNamePrefixes.Length; i++)
            {
                if (resourceName.StartsWith(profile.EditorResourceNamePrefixes[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string GetShaderName(string text)
        {
            var match = ShaderNameRegex.Match(text);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string GetDeclaredShader(string text)
        {
            var match = DeclaredShaderRegex.Match(text);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static void CollectResourcesLoads(string text, out HashSet<string> runtime, out HashSet<string> editorOnly)
        {
            runtime = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            editorOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text)) return;

            var depth = 0;
            var editorDepth = -1;
            var lines = text.Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var raw = lines[i];
                var line = raw.TrimStart();

                if (line.StartsWith("#if", StringComparison.Ordinal))
                {
                    depth++;
                    if (editorDepth < 0 && line.Contains("UNITY_EDITOR")) editorDepth = depth;
                    continue;
                }
                if (line.StartsWith("#endif", StringComparison.Ordinal))
                {
                    if (editorDepth == depth) editorDepth = -1;
                    depth = Mathf.Max(0, depth - 1);
                    continue;
                }
                if (line.StartsWith("#else", StringComparison.Ordinal))
                {
                    if (editorDepth == depth) editorDepth = -1;
                    continue;
                }
                if (line.StartsWith("#elif", StringComparison.Ordinal))
                    continue;

                var match = ResourcesLoadRegex.Match(raw);
                if (!match.Success) continue;

                var name = match.Groups[1].Value;
                if (editorDepth >= 0) editorOnly.Add(name);
                else runtime.Add(name);
            }
        }

        private static string ReadText(string assetPath)
        {
            try
            {
                var absolute = ThirdPartyPathUtility.ToAbsolutePath(assetPath);
                return File.Exists(absolute) ? File.ReadAllText(absolute) : null;
            }
            catch
            {
                return null;
            }
        }

        private static string Normalize(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
        }
    }
}
