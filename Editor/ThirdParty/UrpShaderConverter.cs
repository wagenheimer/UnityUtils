using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Converts 2DxFX-style Built-in Render Pipeline shaders into URP-compatible
    /// shaders in place, preserving the shader name and asset GUID so existing
    /// materials and components keep working.
    ///
    /// Two conversion paths are supported:
    /// - Classic vertex/fragment shaders using "UnityCG.cginc" are rewritten
    ///   mechanically to the URP ShaderLibrary (high fidelity).
    /// - Legacy surface shaders ("#pragma surface") are transpiled into an
    ///   unlit URP pass that reuses the original "surf" body (best effort, always
    ///   flagged for manual review because lighting/Input semantics can differ).
    ///
    /// The original file is backed up under a "~" folder that Unity ignores, so
    /// the conversion can be reverted from the dashboard at any time.
    /// </summary>
    public static class UrpShaderConverter
    {
        private const string UrpCoreInclude =
            "#include \"Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl\"";

        private const string UrpMarker = "render-pipelines.universal/ShaderLibrary/Core.hlsl";

        private static readonly Regex PragmaRegex = new Regex("#pragma[^\\n]*\\n?", RegexOptions.Compiled);
        private static readonly Regex FragmentOptionRegex = new Regex("#pragma\\s+fragmentoption[^\\n]*\\n?", RegexOptions.Compiled);
        private static readonly Regex Sampler2DRegex = new Regex("\\bsampler2D\\s+([A-Za-z_][A-Za-z0-9_]*)\\s*;", RegexOptions.Compiled);
        private static readonly Regex Tex2DRegex = new Regex("\\btex2D\\s*\\(\\s*([A-Za-z_][A-Za-z0-9_]*)\\s*,", RegexOptions.Compiled);
        private static readonly Regex ObjectToClipPosRegex = new Regex("\\bUnityObjectToClipPos\\s*\\(", RegexOptions.Compiled);
        private static readonly Regex ColorSemanticRegex = new Regex(":\\s*COLOR\\b", RegexOptions.Compiled);
        private static readonly Regex InputStructRegex = new Regex("struct\\s+Input\\s*\\{([^}]*)\\}", RegexOptions.Compiled);
        private static readonly Regex FieldRegex = new Regex("([A-Za-z_][A-Za-z0-9_]*)\\s+([A-Za-z_][A-Za-z0-9_]*)\\s*;", RegexOptions.Compiled);
        private static readonly Regex StateLineRegex = new Regex("^[ \\t]*(Blend|BlendOp|ZWrite|ZTest|Cull|ColorMask|Offset|Lighting|ZClip)\\b[^\\n]*$", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex VertexFuncRegex = new Regex("void\\s+vert\\s*\\([^)]*\\)\\s*\\{", RegexOptions.Compiled);
        private static readonly Regex SurfFuncRegex = new Regex("void\\s+surf\\s*\\([^)]*\\)\\s*\\{", RegexOptions.Compiled);

        private const string VertexFragmentShim =
            "    // --- UnityUtils URP compatibility shim ---\n" +
            "    float4 DxFXURP_ObjectToClipPos(float4 positionOS) { return TransformObjectToHClip(positionOS.xyz); }\n";

        private const string SurfaceShim =
            "    // --- UnityUtils URP compatibility shim (surface shader) ---\n" +
            "    #define fixed half\n    #define fixed2 half2\n    #define fixed3 half3\n    #define fixed4 half4\n" +
            "    struct SurfaceOutput { half3 Albedo; half3 Normal; half3 Emission; half Specular; half Gloss; half Alpha; };\n" +
            "    struct appdata_full { float4 vertex:POSITION; float4 color:COLOR; float4 texcoord:TEXCOORD0; float4 texcoord1:TEXCOORD1; float4 texcoord2:TEXCOORD2; float4 texcoord3:TEXCOORD3; float3 normal:NORMAL; float4 tangent:TANGENT; };\n" +
            "    struct DxFXURP_appdata { float4 vertex:POSITION; float4 color:COLOR; float2 texcoord:TEXCOORD0; };\n" +
            "    #define UNITY_INITIALIZE_OUTPUT(type,name) name = (type)0\n" +
            "    float4 UnityPixelSnap(float4 pos) { return pos; }\n";

        public static bool IsAlreadyUrp(string text)
        {
            return text != null && text.Contains(UrpMarker);
        }

        public static UrpConversionResult ConvertUsedShaders(ThirdPartyAssetProfile profile, ThirdPartyScanResult scan, bool backupOriginal)
        {
            var result = new UrpConversionResult();
            if (profile == null || scan == null || !scan.Succeeded)
            {
                result.Errors.Add(scan != null ? scan.FatalError ?? "Scan did not complete." : "No scan provided.");
                return result;
            }

            for (var i = 0; i < scan.Shaders.Count; i++)
            {
                var shader = scan.Shaders[i];
                if (!shader.IsUsed) continue;

                var entry = new UrpConversionEntry { Path = shader.Path, ShaderName = shader.ShaderName, SurfaceShader = shader.IsSurfaceShader };
                result.Entries.Add(entry);

                string original;
                try
                {
                    original = File.ReadAllText(ThirdPartyPathUtility.ToAbsolutePath(shader.Path));
                }
                catch (Exception e)
                {
                    entry.Message = "Could not read: " + e.Message;
                    result.Errors.Add(entry.Message);
                    result.Skipped++;
                    continue;
                }

                if (IsAlreadyUrp(original))
                {
                    entry.Message = "Already URP-compatible, skipped.";
                    result.Skipped++;
                    continue;
                }

                bool surface;
                bool needsReview;
                string converted;
                try
                {
                    converted = BuildUrpShader(original, out surface, out needsReview);
                }
                catch (Exception e)
                {
                    entry.Message = "Conversion failed: " + e.Message;
                    result.Errors.Add(shader.Path + ": " + e.Message);
                    result.Skipped++;
                    continue;
                }

                if (string.IsNullOrEmpty(converted))
                {
                    entry.Message = "Could not parse shader structure, skipped.";
                    result.Skipped++;
                    continue;
                }

                try
                {
                    if (backupOriginal)
                    {
                        WriteBackup(profile, shader.Path, original);
                        entry.BackedUp = true;
                        result.BackedUp++;
                    }

                    File.WriteAllText(ThirdPartyPathUtility.ToAbsolutePath(shader.Path), converted, new UTF8Encoding(false));
                    AssetDatabase.ImportAsset(shader.Path, ImportAssetOptions.ForceUpdate);

                    entry.Converted = true;
                    entry.SurfaceShader = surface;
                    entry.NeedsManualReview = needsReview;
                    result.Converted++;
                    if (needsReview) result.NeedsReview++;
                    entry.Message = surface
                        ? "Converted from surface shader; verify visual output."
                        : "Converted from built-in vertex/fragment shader.";
                }
                catch (Exception e)
                {
                    entry.Message = "Write failed: " + e.Message;
                    result.Errors.Add(shader.Path + ": " + e.Message);
                    result.Skipped++;
                }
            }

            AssetDatabase.Refresh();
            return result;
        }

        public static bool HasBackups(ThirdPartyAssetProfile profile)
        {
            try
            {
                return profile != null && Directory.Exists(ThirdPartyPathUtility.ToAbsolutePath(profile.BackupFolder));
            }
            catch
            {
                return false;
            }
        }

        public static int RestoreBackups(ThirdPartyAssetProfile profile)
        {
            if (profile == null) return 0;

            var backupRoot = ThirdPartyPathUtility.ToAbsolutePath(profile.BackupFolder);
            if (!Directory.Exists(backupRoot)) return 0;

            var restored = 0;
            try
            {
                foreach (var file in Directory.GetFiles(backupRoot, "*.shader", SearchOption.AllDirectories))
                {
                    var relative = file.Substring(backupRoot.Length).TrimStart('\\', '/').Replace('\\', '/');
                    var assetPath = ThirdPartyAssetProfile.Combine(profile.RootFolder, relative);
                    var absolute = ThirdPartyPathUtility.ToAbsolutePath(assetPath);
                    var directory = Path.GetDirectoryName(absolute);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    File.Copy(file, absolute, true);
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                    restored++;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            AssetDatabase.Refresh();
            return restored;
        }

        public static string BuildUrpShader(string originalText, out bool surfaceShader, out bool needsManualReview)
        {
            surfaceShader = originalText != null && originalText.Contains("#pragma surface");
            needsManualReview = surfaceShader;
            return surfaceShader
                ? BuildSurfaceUrp(originalText)
                : BuildVertexFragmentUrp(originalText);
        }

        private static string BuildVertexFragmentUrp(string text)
        {
            var result = text;

            result = FragmentOptionRegex.Replace(result, string.Empty);
            result = result.Replace("CGPROGRAM", "HLSLPROGRAM");
            result = result.Replace("ENDCG", "ENDHLSL");

            result = Regex.Replace(result, "#include\\s+\"UnityCG\\.cginc\"", UrpCoreInclude);

            if (result.IndexOf(UrpMarker, StringComparison.Ordinal) < 0)
                result = InsertAfterAnchor(result, "HLSLPROGRAM", UrpCoreInclude);

            result = Sampler2DRegex.Replace(result, "TEXTURE2D($1);\n    SAMPLER(sampler_$1);");
            result = Tex2DRegex.Replace(result, "SAMPLE_TEXTURE2D($1, sampler_$1,");
            result = ObjectToClipPosRegex.Replace(result, "DxFXURP_ObjectToClipPos(");
            result = ColorSemanticRegex.Replace(result, ": SV_Target");
            result = EnsureUrpSubShaderTag(result);
            result = InsertAfterAnchor(result, UrpCoreInclude, VertexFragmentShim);

            return result;
        }

        private static string BuildSurfaceUrp(string text)
        {
            var properties = ExtractBalanced(text, FindFirst(text, "{", text.IndexOf("Shader", StringComparison.Ordinal)));
            var subShaderIndex = text.IndexOf("SubShader", StringComparison.Ordinal);
            if (properties == null || subShaderIndex < 0) return null;

            var subShader = ExtractBalanced(text, FindFirst(text, "{", subShaderIndex));
            if (subShader == null) return null;

            var cgStart = subShader.IndexOf("CGPROGRAM", StringComparison.Ordinal);
            var cgEnd = subShader.IndexOf("ENDCG", StringComparison.Ordinal);
            if (cgStart < 0 || cgEnd < 0 || cgEnd <= cgStart) return null;
            var cgBody = subShader.Substring(cgStart + "CGPROGRAM".Length, cgEnd - cgStart - "CGPROGRAM".Length);

            var inputMatch = InputStructRegex.Match(cgBody);
            if (!inputMatch.Success) return null;

            var surfMatch = SurfFuncRegex.Match(cgBody);
            if (!surfMatch.Success) return null;

            var shaderName = GetShaderName(text);
            if (string.IsNullOrEmpty(shaderName)) return null;

            var fields = ParseFields(inputMatch.Groups[1].Value);
            var surfBlock = ExtractBalanced(cgBody, surfMatch.Index + surfMatch.Length - 1);
            var vertMatch = VertexFuncRegex.Match(cgBody);
            var vertBlock = vertMatch.Success ? ExtractBalanced(cgBody, vertMatch.Index + vertMatch.Length - 1) : null;

            var body = cgBody;
            body = PragmaRegex.Replace(body, string.Empty);
            if (!string.IsNullOrEmpty(vertBlock)) body = body.Replace(vertBlock, string.Empty);
            if (!string.IsNullOrEmpty(surfBlock)) body = body.Replace(surfBlock, string.Empty);
            if (!string.IsNullOrEmpty(inputMatch.Value)) body = body.Replace(inputMatch.Value, string.Empty);
            body = Regex.Replace(body, "#include\\s+\"UnityCG\\.cginc\"", string.Empty);
            body = Sampler2DRegex.Replace(body, "TEXTURE2D($1);\n    SAMPLER(sampler_$1);");
            body = Tex2DRegex.Replace(body, "SAMPLE_TEXTURE2D($1, sampler_$1,");
            body = ObjectToClipPosRegex.Replace(body, "DxFXURP_ObjectToClipPos(");
            body = ColorSemanticRegex.Replace(body, ": SV_Target");

            var states = StateLineRegex.Matches(subShader);
            var statesBuilder = new StringBuilder();
            for (var i = 0; i < states.Count; i++)
                statesBuilder.Append("    ").Append(states[i].Value.Trim()).Append('\n');

            var varyings = new StringBuilder();
            varyings.Append("    struct DxFXURP_Varyings {\n");
            varyings.Append("        float4 positionCS : SV_POSITION;\n");
            for (var i = 0; i < fields.Count; i++)
                varyings.Append("        ").Append(fields[i].Type).Append(' ').Append(fields[i].Name)
                        .Append(" : TEXCOORD").Append(i).Append(";\n");
            varyings.Append("    };\n");

            var vertBuilder = new StringBuilder();
            vertBuilder.Append("    DxFXURP_Varyings Vert(DxFXURP_appdata v) {\n");
            vertBuilder.Append("        DxFXURP_Varyings o = (DxFXURP_Varyings)0;\n");
            vertBuilder.Append("        appdata_full vf = (appdata_full)0;\n");
            vertBuilder.Append("        vf.vertex = v.vertex; vf.texcoord = v.texcoord; vf.color = v.color;\n");
            vertBuilder.Append("        Input vi = (Input)0;\n");
            for (var i = 0; i < fields.Count; i++)
                vertBuilder.Append(DefaultInputAssignment("        vi", fields[i]));
            if (!string.IsNullOrEmpty(vertBlock)) vertBuilder.Append("        vert(vf, vi);\n");
            vertBuilder.Append("        o.positionCS = TransformObjectToHClip(vf.vertex.xyz);\n");
            for (var i = 0; i < fields.Count; i++)
                vertBuilder.Append("        o.").Append(fields[i].Name).Append(" = vi.").Append(fields[i].Name).Append(";\n");
            vertBuilder.Append("        return o;\n    }\n");

            var fragBuilder = new StringBuilder();
            fragBuilder.Append("    half4 Frag(DxFXURP_Varyings i) : SV_Target {\n");
            fragBuilder.Append("        Input IN = (Input)0;\n");
            for (var i = 0; i < fields.Count; i++)
                fragBuilder.Append("        IN.").Append(fields[i].Name).Append(" = i.").Append(fields[i].Name).Append(";\n");
            fragBuilder.Append("        SurfaceOutput o = (SurfaceOutput)0;\n");
            fragBuilder.Append("        surf(IN, o);\n");
            fragBuilder.Append("        return half4(o.Emission + o.Albedo, o.Alpha);\n    }\n");

            var output = new StringBuilder();
            output.Append("// Auto-generated URP port by UnityUtils Third-Party Slimmer.\n");
            output.Append("// Original Built-in shader backed up under the package's ~ folder.\n");
            output.Append("Shader \"").Append(shaderName).Append("\"\n");
            output.Append(properties).Append('\n');
            output.Append("SubShader\n{\n");
            output.Append("    Tags { \"RenderType\"=\"Transparent\" \"Queue\"=\"Transparent\" \"IgnoreProjector\"=\"True\" \"PreviewType\"=\"Plane\" \"CanUseSpriteAtlas\"=\"True\" \"RenderPipeline\"=\"UniversalPipeline\" }\n");
            output.Append(statesBuilder);
            output.Append("    Pass\n    {\n        Name \"DxFXURP\"\n        HLSLPROGRAM\n");
            output.Append("        #pragma vertex Vert\n        #pragma fragment Frag\n        #pragma target 3.0\n");
            output.Append("        ").Append(UrpCoreInclude).Append('\n');
            output.Append(SurfaceShim);
            output.Append(body).Append('\n');
            output.Append(inputMatch.Value).Append(";\n");
            if (!string.IsNullOrEmpty(vertBlock)) output.Append(vertBlock).Append('\n');
            if (!string.IsNullOrEmpty(surfBlock)) output.Append(surfBlock).Append('\n');
            output.Append(varyings);
            output.Append(vertBuilder);
            output.Append(fragBuilder);
            output.Append("        ENDHLSL\n    }\n}\nFallback Off\n}\n");
            return output.ToString();
        }

        private static string DefaultInputAssignment(string target, InputField field)
        {
            if (field.Name == "uv_MainTex") return target + ".uv_MainTex = v.texcoord.xy;\n";
            if (field.Name == "color") return target + ".color = v.color;\n";
            return string.Empty;
        }

        private struct InputField
        {
            public string Type;
            public string Name;
        }

        private static List<InputField> ParseFields(string inputBody)
        {
            var fields = new List<InputField>();
            foreach (Match match in FieldRegex.Matches(inputBody))
            {
                var type = match.Groups[1].Value;
                var name = match.Groups[2].Value;
                if (type == "UNITY_INITIALIZE_OUTPUT") continue;
                fields.Add(new InputField { Type = type, Name = name });
            }
            return fields;
        }

        private static void WriteBackup(ThirdPartyAssetProfile profile, string assetPath, string contents)
        {
            var root = profile.RootFolder.Replace('\\', '/').TrimEnd('/') + "/";
            var normalized = assetPath.Replace('\\', '/');
            var relative = normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(root.Length)
                : Path.GetFileName(normalized);

            var backupPath = Path.Combine(ThirdPartyPathUtility.ToAbsolutePath(profile.BackupFolder), relative.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(backupPath, contents, new UTF8Encoding(false));
        }

        private static string GetShaderName(string text)
        {
            var match = Regex.Match(text, "^\\s*Shader\\s+\"([^\"]+)\"", RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string EnsureUrpSubShaderTag(string text)
        {
            if (text.IndexOf("RenderPipeline", StringComparison.Ordinal) >= 0) return text;

            var subShader = text.IndexOf("SubShader", StringComparison.Ordinal);
            if (subShader < 0) return text;

            var tags = text.IndexOf("Tags", subShader, StringComparison.Ordinal);
            if (tags >= 0)
            {
                var open = text.IndexOf('{', tags);
                if (open >= 0)
                    return text.Insert(open + 1, " \"RenderPipeline\"=\"UniversalPipeline\" ");
            }

            var brace = text.IndexOf('{', subShader);
            if (brace >= 0)
                return text.Insert(brace + 1, "\n    Tags { \"RenderPipeline\"=\"UniversalPipeline\" }");
            return text;
        }

        private static string InsertAfterAnchor(string text, string anchor, string insertion)
        {
            var index = text.IndexOf(anchor, StringComparison.Ordinal);
            if (index < 0) return text;

            var lineEnd = text.IndexOf('\n', index);
            if (lineEnd < 0) return text + "\n" + insertion;

            return text.Insert(lineEnd + 1, insertion + "\n");
        }

        private static int FindFirst(string text, string token, int start)
        {
            if (start < 0) return -1;
            return text.IndexOf(token, start, StringComparison.Ordinal);
        }

        private static string ExtractBalanced(string text, int openIndex)
        {
            if (openIndex < 0 || openIndex >= text.Length || text[openIndex] != '{') return null;

            var depth = 0;
            for (var i = openIndex; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0) return text.Substring(openIndex, i - openIndex + 1);
                }
            }
            return null;
        }
    }
}
