using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Applies destructive clean-up operations to a third-party pack: in-place
    /// slimming (delete unused assets, move editor-only Resources out of the
    /// build) and "extract used only" (relocate the referenced subset to a new
    /// folder and drop the original). All operations preserve asset GUIDs so
    /// scene, prefab and material references keep resolving.
    ///
    /// Destination folders are created before asset editing starts, because
    /// CreateFolder is not safe to call while the AssetDatabase is batched.
    /// </summary>
    public static class ThirdPartyAssetSlimmer
    {
        public static ThirdPartySlimResult ApplySlim(ThirdPartyAssetProfile profile, ThirdPartyScanResult scan, ThirdPartySlimOptions options)
        {
            var result = new ThirdPartySlimResult();
            if (profile == null || scan == null || !scan.Succeeded)
            {
                result.Errors.Add(scan != null ? scan.FatalError ?? "Scan did not complete." : "No scan provided.");
                return result;
            }

            if (options.MoveEditorResourcesOutOfBuild)
                EnsureFolder(profile.EditorResourcesFolder);

            AssetDatabase.StartAssetEditing();
            try
            {
                if (options.DeleteUnusedScripts)
                {
                    for (var i = 0; i < scan.Scripts.Count; i++)
                    {
                        var script = scan.Scripts[i];
                        if (script.IsUsed) continue;
                        if (Delete(script.Path))
                        {
                            result.ScriptsDeleted++;
                            result.BytesReclaimed += script.SizeBytes;
                        }
                    }
                }

                if (options.DeleteUnusedShaders)
                {
                    for (var i = 0; i < scan.Shaders.Count; i++)
                    {
                        var shader = scan.Shaders[i];
                        if (shader.IsUsed) continue;
                        if (Delete(shader.Path))
                        {
                            result.ShadersDeleted++;
                            result.BytesReclaimed += shader.SizeBytes;
                        }
                    }
                }

                if (options.DeleteUnusedResources)
                {
                    for (var i = 0; i < scan.Resources.Count; i++)
                    {
                        var resource = scan.Resources[i];
                        if (resource.Kind != ThirdPartyResourceKind.Unused) continue;
                        if (Delete(resource.Path))
                        {
                            result.ResourcesDeleted++;
                            result.BytesReclaimed += resource.SizeBytes;
                        }
                    }
                }

                if (options.MoveEditorResourcesOutOfBuild)
                {
                    for (var i = 0; i < scan.Resources.Count; i++)
                    {
                        var resource = scan.Resources[i];
                        if (resource.Kind != ThirdPartyResourceKind.EditorOnly) continue;

                        var target = ThirdPartyAssetProfile.Combine(profile.EditorResourcesFolder, resource.FileName);
                        if (MoveAsset(resource.Path, target, result))
                        {
                            result.ResourcesMoved++;
                            result.BytesReclaimed += resource.SizeBytes;
                        }
                    }
                }

                if (options.RemoveExamplesFolder || options.RemoveDocFolder || options.RemoveExtraShadersFolder)
                {
                    for (var i = 0; i < scan.RemovableFolderPaths.Count; i++)
                    {
                        var folder = scan.RemovableFolderPaths[i];
                        var leaf = Path.GetFileName(folder);
                        var wanted =
                            (options.RemoveExamplesFolder && leaf.Equals("Examples", StringComparison.OrdinalIgnoreCase)) ||
                            (options.RemoveDocFolder && leaf.Equals("Doc", StringComparison.OrdinalIgnoreCase)) ||
                            (options.RemoveExtraShadersFolder && leaf.Equals("ExtraShaders", StringComparison.OrdinalIgnoreCase));
                        if (!wanted) continue;

                        result.BytesReclaimed += ThirdPartyPathUtility.FolderSize(folder);
                        if (Delete(folder))
                        {
                            result.FoldersRemoved++;
                            result.Log.Add("Removed folder: " + folder);
                        }
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            return result;
        }

        public static ThirdPartySlimResult ExtractUsedOnly(ThirdPartyAssetProfile profile, ThirdPartyScanResult scan)
        {
            var result = new ThirdPartySlimResult();
            if (profile == null || scan == null || !scan.Succeeded)
            {
                result.Errors.Add(scan != null ? scan.FatalError ?? "Scan did not complete." : "No scan provided.");
                return result;
            }

            var destination = profile.ExtractDestinationFolder;
            if (AssetDatabase.IsValidFolder(destination))
            {
                result.Errors.Add("Destination already exists: " + destination + ". Remove it first.");
                return result;
            }

            EnsureFolder(destination);

            var moves = new List<KeyValuePair<string, string>>();

            for (var i = 0; i < scan.Scripts.Count; i++)
            {
                var script = scan.Scripts[i];
                if (!script.IsUsed) continue;
                moves.Add(new KeyValuePair<string, string>(script.Path, BuildDestination(destination, Relative(script.Path, profile.RootFolder))));
            }

            for (var i = 0; i < scan.Shaders.Count; i++)
            {
                var shader = scan.Shaders[i];
                if (!shader.IsUsed) continue;

                string relative;
                if (shader.InResources)
                {
                    relative = ThirdPartyAssetProfile.Combine(profile.ResourcesSubFolder, Relative(shader.Path, profile.ResourcesFolder));
                }
                else
                {
                    relative = ThirdPartyAssetProfile.Combine(profile.ResourcesSubFolder, shader.FileName);
                    result.Log.Add("Shader relocated into Resources for runtime inclusion: " + shader.FileName);
                }
                moves.Add(new KeyValuePair<string, string>(shader.Path, BuildDestination(destination, relative)));
            }

            for (var i = 0; i < scan.Resources.Count; i++)
            {
                var resource = scan.Resources[i];
                if (resource.Kind == ThirdPartyResourceKind.Unused) continue;

                string relative;
                if (resource.Kind == ThirdPartyResourceKind.Runtime)
                    relative = ThirdPartyAssetProfile.Combine(profile.ResourcesSubFolder, Relative(resource.Path, profile.ResourcesFolder));
                else
                    relative = ThirdPartyAssetProfile.Combine("Editor/Resources", resource.FileName);

                moves.Add(new KeyValuePair<string, string>(resource.Path, BuildDestination(destination, relative)));
            }

            for (var i = 0; i < moves.Count; i++)
                EnsureFolder(Parent(moves[i].Value));

            AssetDatabase.StartAssetEditing();
            try
            {
                for (var i = 0; i < moves.Count; i++)
                {
                    if (MoveAsset(moves[i].Key, moves[i].Value, result))
                        result.AssetsExtracted++;
                }

                AssetDatabase.Refresh();

                if (AssetDatabase.IsValidFolder(profile.RootFolder))
                {
                    result.BytesReclaimed += ThirdPartyPathUtility.FolderSize(profile.RootFolder);
                    if (Delete(profile.RootFolder))
                        result.Log.Add("Removed original pack folder: " + profile.RootFolder);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            return result;
        }

        private static bool Delete(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            return AssetDatabase.DeleteAsset(assetPath);
        }

        private static bool MoveAsset(string from, string to, ThirdPartySlimResult result)
        {
            from = from.Replace('\\', '/');
            to = to.Replace('\\', '/');

            var destinationAbsolute = ThirdPartyPathUtility.ToAbsolutePath(to);
            if (File.Exists(destinationAbsolute) || Directory.Exists(destinationAbsolute))
            {
                AssetDatabase.DeleteAsset(from);
                result.Log.Add("Dropped duplicate (already present at destination): " + from);
                return true;
            }

            var error = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(error))
            {
                result.Errors.Add("Move failed (" + from + " -> " + to + "): " + error);
                return false;
            }
            return true;
        }

        public static void EnsureFolder(string folder)
        {
            folder = (folder ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;

            var slash = folder.LastIndexOf('/');
            if (slash <= 0) return;

            var parent = folder.Substring(0, slash);
            var leaf = folder.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static string Parent(string assetPath)
        {
            var slash = assetPath.Replace('\\', '/').LastIndexOf('/');
            return slash <= 0 ? "Assets" : assetPath.Replace('\\', '/').Substring(0, slash);
        }

        private static string BuildDestination(string destinationRoot, string relative)
        {
            return ThirdPartyAssetProfile.Combine(destinationRoot, relative);
        }

        private static string Relative(string assetPath, string root)
        {
            var path = assetPath.Replace('\\', '/');
            var prefix = root.Replace('\\', '/').TrimEnd('/') + "/";
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return path.Substring(prefix.Length);
            return Path.GetFileName(path);
        }
    }
}
