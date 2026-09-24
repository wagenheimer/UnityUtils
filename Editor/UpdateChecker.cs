using UnityEditor;
using Wagenheimer.PackageHub.Editor;

namespace Wagenheimer.UnityUtils.Editor
{
    public static class UpdateChecker
    {
        [MenuItem("Tools/Wagenheimer/Unity Utils/Check for Updates...", priority = 200)]
        public static void CheckForUpdateMenu() => CheckForUpdate(true);

        public static void CheckForUpdate(bool force = false)
        {
            PackageHubWindow.OpenToPackage("com.wagenheimer.unityutils");
        }
    }
}
