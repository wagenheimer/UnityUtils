using System.IO;
using UnityEditor;
using UnityEngine;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>Menu to create <c>Assets/Resources/Wagenheimer/AppLogSettings.asset</c> (see <see cref="AppLogSettings"/>).</summary>
    public static class AppLogSettingsMenu
    {
        private const string AssetPath = "Assets/Resources/" + AppLogSettings.ResourcePath + ".asset";

        [MenuItem("Wagenheimer/Debug Log/Create Settings")]
        public static void CreateSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AppLogSettings>(AssetPath);
            if (existing == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
                existing = ScriptableObject.CreateInstance<AppLogSettings>();
                AssetDatabase.CreateAsset(existing, AssetPath);
                AssetDatabase.SaveAssets();
            }

            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
        }
    }
}
