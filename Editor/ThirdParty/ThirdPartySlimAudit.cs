using System.Collections.Generic;
using UnityEditor;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>One thing the slimmer would remove or move out of the build.</summary>
    public struct ThirdPartySlimFinding
    {
        public string Path;
        public string Kind;
        public long SizeBytes;
    }

    /// <summary>
    /// Answers "has the third-party pack (2DxFX) already been slimmed?" for the Project Cleanup scan.
    /// The default policy is the aggressive one: everything the project does not use is removed
    /// (unused scripts, shaders and resources, plus the Examples, Doc and ExtraShaders folders), and
    /// editor-only resources are moved out of the build. A pack that is already slim reports nothing.
    /// </summary>
    public static class ThirdPartySlimAudit
    {
        public sealed class Result
        {
            public bool PackFound;
            public string Error;
            public ThirdPartyAssetProfile Profile;
            public ThirdPartyScanResult Scan;
            public readonly List<ThirdPartySlimFinding> Findings = new List<ThirdPartySlimFinding>();

            public int PendingCount { get { return Findings.Count; } }

            public long PendingBytes
            {
                get
                {
                    long total = 0;
                    for (var i = 0; i < Findings.Count; i++) total += Findings[i].SizeBytes;
                    return total;
                }
            }
        }

        /// <summary>The options used by the automatic fix: remove everything that is not used.</summary>
        public static ThirdPartySlimOptions DefaultOptions()
        {
            return new ThirdPartySlimOptions
            {
                DeleteUnusedScripts = true,
                DeleteUnusedShaders = true,
                DeleteUnusedResources = true,
                MoveEditorResourcesOutOfBuild = true,
                RemoveExamplesFolder = true,
                RemoveDocFolder = true,
                RemoveExtraShadersFolder = true
            };
        }

        public static Result Run()
        {
            var result = new Result();

            var profile = ThirdPartyAssetScanner.DetectDefaultProfile();
            if (!ThirdPartyAssetScanner.LooksLikePack(profile.RootFolder))
                return result; // Not installed: nothing to slim.

            result.PackFound = true;
            result.Profile = profile;
            result.Scan = ThirdPartyAssetScanner.Scan(profile);

            if (!result.Scan.Succeeded)
            {
                result.Error = result.Scan.FatalError;
                return result;
            }

            Collect(result);
            return result;
        }

        private static void Collect(Result result)
        {
            var scan = result.Scan;

            foreach (var script in scan.Scripts)
            {
                if (!script.IsUsed)
                    result.Findings.Add(new ThirdPartySlimFinding { Path = script.Path, Kind = "Unused script", SizeBytes = script.SizeBytes });
            }

            foreach (var shader in scan.Shaders)
            {
                if (!shader.IsUsed)
                    result.Findings.Add(new ThirdPartySlimFinding { Path = shader.Path, Kind = "Unused shader", SizeBytes = shader.SizeBytes });
            }

            foreach (var resource in scan.Resources)
            {
                if (resource.Kind == ThirdPartyResourceKind.Unused)
                {
                    result.Findings.Add(new ThirdPartySlimFinding { Path = resource.Path, Kind = "Unused resource", SizeBytes = resource.SizeBytes });
                }
                else if (resource.Kind == ThirdPartyResourceKind.EditorOnly)
                {
                    result.Findings.Add(new ThirdPartySlimFinding { Path = resource.Path, Kind = "Editor-only resource in the build", SizeBytes = resource.SizeBytes });
                }
            }

            foreach (var folder in scan.RemovableFolderPaths)
            {
                result.Findings.Add(new ThirdPartySlimFinding
                {
                    Path = folder,
                    Kind = "Sample/documentation folder",
                    SizeBytes = ThirdPartyPathUtility.FolderSize(folder)
                });
            }
        }

        /// <summary>Applies the default (remove everything unused) slim to a scanned pack.</summary>
        public static ThirdPartySlimResult Apply(Result audit)
        {
            if (audit == null || !audit.PackFound || audit.Scan == null)
                return new ThirdPartySlimResult();

            var result = ThirdPartyAssetSlimmer.ApplySlim(audit.Profile, audit.Scan, DefaultOptions());
            AssetDatabase.SaveAssets();
            return result;
        }
    }
}
