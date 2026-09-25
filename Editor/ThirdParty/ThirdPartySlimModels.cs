using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Describes a third-party asset pack that can be audited and slimmed down.
    /// Built around the layout used by VETASOFT's 2DxFX, but generic enough for
    /// other packs that follow the Scripts/Resources/Examples convention.
    /// </summary>
    public sealed class ThirdPartyAssetProfile
    {
        public string Id = "2dxfx";
        public string DisplayName = "2DxFX";
        public string RootFolder = "Assets/2DxFX";
        public string ScriptsSubFolder = "Scripts";
        public string ResourcesSubFolder = "Resources";
        public string EditorResourcesSubFolder = "Editor/Resources";
        public string BackupSubFolder = "URP-Backup";

        public string[] RemovableSubFolders = { "Examples", "Doc", "ExtraShaders" };

        public string[] EditorResourceNamePrefixes = { "2dxfx-icon", "2dxfxinspector", "2dxfx-p-" };

        public string ScriptsFolder { get { return Combine(RootFolder, ScriptsSubFolder); } }

        public string ResourcesFolder { get { return Combine(RootFolder, ResourcesSubFolder); } }

        public string EditorResourcesFolder { get { return Combine(RootFolder, EditorResourcesSubFolder); } }

        public string BackupFolder { get { return RootFolder + "~/" + BackupSubFolder; } }

        public string ExtractDestinationFolder { get { return "Assets/ThirdParty/" + DisplayName.Replace(" ", "") + "-Slim"; } }

        public static string Combine(string a, string b)
        {
            return a.TrimEnd('/') + "/" + b.TrimStart('/');
        }

        public string GetSubFolder(string name)
        {
            return Combine(RootFolder, name);
        }
    }

    /// <summary>Classification of an asset living inside the pack's Resources folder.</summary>
    public enum ThirdPartyResourceKind
    {
        Runtime = 0,
        EditorOnly = 1,
        Unused = 2
    }

    /// <summary>Per-script usage and dependency information gathered during a scan.</summary>
    public sealed class ThirdPartyScriptInfo
    {
        public string Path;
        public string FileName;
        public string ClassName;
        public bool IsHelper;
        public bool IsEditorScript;
        public int ReferenceCount;
        public string ShaderName;
        public long SizeBytes;

        public readonly List<string> RuntimeResourceNames = new List<string>();
        public readonly List<string> EditorResourceNames = new List<string>();

        public bool IsUsed { get { return IsHelper || ReferenceCount > 0; } }
    }

    /// <summary>Per-shader usage and compatibility information gathered during a scan.</summary>
    public sealed class ThirdPartyShaderInfo
    {
        public string Path;
        public string FileName;
        public string ShaderName;
        public bool InResources;
        public bool IsSurfaceShader;
        public bool UsedByComponent;
        public bool UsedByMaterial;
        public int ReferenceCount;
        public long SizeBytes;

        public bool IsUsed { get { return UsedByComponent || UsedByMaterial || ReferenceCount > 0; } }
    }

    /// <summary>Per-resource asset classification gathered during a scan.</summary>
    public sealed class ThirdPartyResourceInfo
    {
        public string Path;
        public string FileName;
        public string ResourceName;
        public ThirdPartyResourceKind Kind;
        public long SizeBytes;
    }

    /// <summary>Result of auditing a third-party pack.</summary>
    public sealed class ThirdPartyScanResult
    {
        public ThirdPartyAssetProfile Profile;
        public bool IsUrp;
        public bool Cancelled;
        public string FatalError;

        public readonly List<ThirdPartyScriptInfo> Scripts = new List<ThirdPartyScriptInfo>();
        public readonly List<ThirdPartyShaderInfo> Shaders = new List<ThirdPartyShaderInfo>();
        public readonly List<ThirdPartyResourceInfo> Resources = new List<ThirdPartyResourceInfo>();
        public readonly List<string> ExternalMaterialReferences = new List<string>();
        public readonly List<string> RemovableFolderPaths = new List<string>();
        public readonly List<string> Notes = new List<string>();

        public bool Succeeded { get { return string.IsNullOrEmpty(FatalError); } }

        public int UsedScriptCount
        {
            get
            {
                var n = 0;
                for (var i = 0; i < Scripts.Count; i++) if (Scripts[i].IsUsed) n++;
                return n;
            }
        }

        public int UnusedScriptCount { get { return Scripts.Count - UsedScriptCount; } }

        public int UsedShaderCount
        {
            get
            {
                var n = 0;
                for (var i = 0; i < Shaders.Count; i++) if (Shaders[i].IsUsed) n++;
                return n;
            }
        }

        public int UnusedShaderCount { get { return Shaders.Count - UsedShaderCount; } }

        public int UnusedResourceCount
        {
            get
            {
                var n = 0;
                for (var i = 0; i < Resources.Count; i++) if (Resources[i].Kind == ThirdPartyResourceKind.Unused) n++;
                return n;
            }
        }

        public int EditorOnlyResourceCount
        {
            get
            {
                var n = 0;
                for (var i = 0; i < Resources.Count; i++) if (Resources[i].Kind == ThirdPartyResourceKind.EditorOnly) n++;
                return n;
            }
        }

        public int RuntimeResourceCount
        {
            get
            {
                var n = 0;
                for (var i = 0; i < Resources.Count; i++) if (Resources[i].Kind == ThirdPartyResourceKind.Runtime) n++;
                return n;
            }
        }

        public int SurfaceShaderCount
        {
            get
            {
                var n = 0;
                for (var i = 0; i < Shaders.Count; i++) if (Shaders[i].IsUsed && Shaders[i].IsSurfaceShader) n++;
                return n;
            }
        }

        public long TotalBytes
        {
            get
            {
                long total = 0;
                for (var i = 0; i < Scripts.Count; i++) total += Scripts[i].SizeBytes;
                for (var i = 0; i < Shaders.Count; i++) total += Shaders[i].SizeBytes;
                for (var i = 0; i < Resources.Count; i++) total += Resources[i].SizeBytes;
                return total;
            }
        }

        public long UsedBytes
        {
            get
            {
                long total = 0;
                for (var i = 0; i < Scripts.Count; i++) if (Scripts[i].IsUsed) total += Scripts[i].SizeBytes;
                for (var i = 0; i < Shaders.Count; i++) if (Shaders[i].IsUsed) total += Shaders[i].SizeBytes;
                for (var i = 0; i < Resources.Count; i++) if (Resources[i].Kind != ThirdPartyResourceKind.Unused) total += Resources[i].SizeBytes;
                return total;
            }
        }
    }

    /// <summary>User-selected operations applied by the slimmer.</summary>
    public sealed class ThirdPartySlimOptions
    {
        public bool DeleteUnusedScripts = true;
        public bool DeleteUnusedShaders = true;
        public bool DeleteUnusedResources = true;
        public bool MoveEditorResourcesOutOfBuild = true;
        public bool RemoveExamplesFolder;
        public bool RemoveDocFolder = true;
        public bool RemoveExtraShadersFolder;
    }

    /// <summary>Outcome of a slim or extract operation.</summary>
    public sealed class ThirdPartySlimResult
    {
        public int ScriptsDeleted;
        public int ShadersDeleted;
        public int ResourcesDeleted;
        public int ResourcesMoved;
        public int FoldersRemoved;
        public int AssetsExtracted;
        public long BytesReclaimed;

        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Log = new List<string>();

        public string Summary
        {
            get
            {
                return string.Format(
                    "{0} script(s), {1} shader(s), {2} resource(s) removed; {3} resource(s) moved; " +
                    "{4} folder(s) removed; {5} asset(s) extracted; ~{6} reclaimed.",
                    ScriptsDeleted, ShadersDeleted, ResourcesDeleted, ResourcesMoved,
                    FoldersRemoved, AssetsExtracted, FormatBytes(BytesReclaimed));
            }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024f).ToString("0.0") + " KB";
            return (bytes / (1024f * 1024f)).ToString("0.00") + " MB";
        }
    }

    /// <summary>Per-shader URP conversion outcome.</summary>
    public sealed class UrpConversionEntry
    {
        public string Path;
        public string ShaderName;
        public bool Converted;
        public bool SurfaceShader;
        public bool BackedUp;
        public bool NeedsManualReview;
        public string Message;
    }

    /// <summary>Outcome of a URP conversion pass.</summary>
    public sealed class UrpConversionResult
    {
        public int Converted;
        public int Skipped;
        public int BackedUp;
        public int NeedsReview;

        public readonly List<UrpConversionEntry> Entries = new List<UrpConversionEntry>();
        public readonly List<string> Errors = new List<string>();

        public string Summary
        {
            get { return string.Format("{0} converted, {1} backed up, {2} need review, {3} skipped.", Converted, BackedUp, NeedsReview, Skipped); }
        }
    }

    internal static class ThirdPartyPathUtility
    {
        public static string ToAbsolutePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return assetPath;
            if (assetPath.StartsWith("Assets/") || assetPath == "Assets")
                return Path.GetFullPath(assetPath);
            return assetPath;
        }

        public static bool IsUnderFolder(string assetPath, string folder)
        {
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(folder))
                return false;
            var a = assetPath.Replace('\\', '/').TrimEnd('/');
            var f = folder.Replace('\\', '/').TrimEnd('/');
            return a.Equals(f, System.StringComparison.OrdinalIgnoreCase) ||
                   a.StartsWith(f + "/", System.StringComparison.OrdinalIgnoreCase);
        }

        public static long SafeFileSize(string assetPath)
        {
            try
            {
                var abs = ToAbsolutePath(assetPath);
                if (File.Exists(abs)) return new FileInfo(abs).Length;
            }
            catch
            {
                // Ignore unreadable files, size is best-effort.
            }
            return 0;
        }

        public static long FolderSize(string assetFolder)
        {
            try
            {
                var abs = ToAbsolutePath(assetFolder);
                if (!Directory.Exists(abs)) return 0;
                long total = 0;
                foreach (var file in Directory.GetFiles(abs, "*", SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase)) continue;
                    total += new FileInfo(file).Length;
                }
                return total;
            }
            catch
            {
                return 0;
            }
        }
    }
}
