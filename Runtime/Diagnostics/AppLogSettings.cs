using UnityEngine;

namespace Wagenheimer.UnityUtils
{
    /// <summary>
    /// Where a debug build sends its log (the AppDeployHub Forge). Optional: without this asset in
    /// <c>Resources/Wagenheimer/AppLogSettings</c> (or without an upload key) the app still writes its log file, but
    /// the DEBUG button and the automatic upload stay off. Create it from the menu
    /// <b>Wagenheimer &gt; Debug Log &gt; Create Settings</b>.
    /// <para>
    /// The key only lets a client SEND text logs to the Forge (it reads nothing), but it ships inside the build, so the
    /// feature is limited to Development Builds and the Editor unless <see cref="allowInReleaseBuilds"/> is set.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "AppLogSettings", menuName = "Wagenheimer/App Log Settings")]
    public sealed class AppLogSettings : ScriptableObject
    {
        public const string ResourcePath = "Wagenheimer/AppLogSettings";

        [Tooltip("Base address of the Forge, e.g. https://forge.appdeployhub.com")]
        public string serverUrl = "https://forge.appdeployhub.com";

        [Tooltip("Value of DeviceLogs__UploadKey configured on the Forge. Empty disables the upload.")]
        public string uploadKey = "";

        [Header("Behaviour")]
        [Tooltip("Show the small DEBUG button (opens the log panel).")]
        public bool showDebugButton = true;

        [Tooltip("Default for the panel's 'auto-send on error' switch (the player can flip it on the device).")]
        public bool autoUploadOnError = true;

        [Tooltip("Also send the log whenever the app goes to the background.")]
        public bool autoUploadOnPause = false;

        [Tooltip("Keep the button and the upload in release builds too. Leave off for store builds.")]
        public bool allowInReleaseBuilds = false;

        /// <summary>True when the upload can run in this build.</summary>
        public bool IsUsable =>
            !string.IsNullOrWhiteSpace(serverUrl) &&
            !string.IsNullOrWhiteSpace(uploadKey) &&
            (allowInReleaseBuilds || Debug.isDebugBuild || Application.isEditor);

        public static AppLogSettings Load() => Resources.Load<AppLogSettings>(ResourcePath);
    }
}
