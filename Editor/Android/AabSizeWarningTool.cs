using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Wagenheimer.UnityUtils.Editor
{
    /// <summary>
    /// Controls Unity's built-in Android App Bundle size check ("Player Settings > Other Settings
    /// > Build > Warn about App Bundle size", serialized as <c>AndroidValidateAppBundleSize</c>).
    ///
    /// Unity warns when the built .aab download size exceeds the configured threshold (200 MB by
    /// default), which is only useful for Play Store release builds — games that always exceed it
    /// get a warning on every AAB build. The state lives in the project's Player Settings, so it
    /// travels with the project like any other setting.
    /// </summary>
    public static class AabSizeWarningTool
    {
        const string MenuRoot = "Tools/Wagenheimer/Unity Utils/Android/App Bundle Size Warning/";
        const string ProjectSettingsPath = "ProjectSettings/ProjectSettings.asset";
        const string SerializedEnabledKey = "AndroidValidateAppBundleSize";
        const string SerializedThresholdKey = "AndroidAppBundleSizeToValidate";

        #region Menu Items

        [MenuItem(MenuRoot + "Disable", priority = 10)]
        public static void Disable()
        {
            SetEnabled(false, notify: true);
        }

        [MenuItem(MenuRoot + "Enable", priority = 11)]
        public static void Enable()
        {
            SetEnabled(true, notify: true);
        }

        [MenuItem(MenuRoot + "Toggle", priority = 12)]
        public static void Toggle()
        {
            SetEnabled(!IsEnabled(), notify: true);
        }

        [MenuItem(MenuRoot + "Toggle", validate = true, priority = 12)]
        public static bool ToggleValidate()
        {
            Menu.SetChecked(MenuRoot + "Toggle", IsEnabled() ?? true);
            return true;
        }

        [MenuItem(MenuRoot + "Log Status", priority = 13)]
        public static void LogStatus()
        {
            var threshold = GetThreshold();
            Debug.Log($"[AabSizeWarningTool] Warn about App Bundle size: {(IsEnabled() ? "ENABLED" : "DISABLED")}"
                      + (threshold.HasValue ? $" (threshold: {threshold.Value} MB)" : ""));
        }

        #endregion

        /// <summary>
        /// Applies the setting. Silent no-op when already in the requested state, so it is safe to
        /// call automatically on every Android build (e.g. from a build script).
        /// </summary>
        public static void SetEnabled(bool enabled, bool notify = false)
        {
            var current = IsEnabled();
            if (current.HasValue && current.Value == enabled)
            {
                if (notify) Debug.Log($"[AabSizeWarningTool] Warn about App Bundle size is already {(enabled ? "ENABLED" : "DISABLED")}.");
                return;
            }

            if (TrySetViaReflection(enabled))
            {
                if (notify) Debug.Log($"[AabSizeWarningTool] Warn about App Bundle size {(enabled ? "enabled" : "disabled")} (PlayerSettings API).");
                else Debug.Log($"[AabSizeWarningTool] Warn about App Bundle size {(enabled ? "enabled" : "disabled")}.");
                return;
            }

            if (TrySetSerializedValue(SerializedEnabledKey, enabled ? 1 : 0))
            {
                Debug.Log($"[AabSizeWarningTool] Warn about App Bundle size {(enabled ? "enabled" : "disabled")} (ProjectSettings.asset).");
                return;
            }

            Debug.LogWarning("[AabSizeWarningTool] Could not change the App Bundle size warning setting.");
        }

        public static bool? IsEnabled()
        {
            var prop = FindStaticProperty("validateAppBundleSize");
            if (prop != null)
            {
                try { return (bool)prop.GetValue(null); } catch { }
            }

            return ReadSerializedValue(SerializedEnabledKey) == 1 ? true
                 : ReadSerializedValue(SerializedEnabledKey) == 0 ? false : (bool?)null;
        }

        public static int? GetThreshold()
        {
            var prop = FindStaticProperty("appBundleSizeToValidate");
            if (prop != null)
            {
                try { return (int)prop.GetValue(null); } catch { }
            }

            return ReadSerializedValue(SerializedThresholdKey);
        }

        public static void SetThreshold(int megabytes)
        {
            var prop = FindStaticProperty("appBundleSizeToValidate");
            if (prop != null)
            {
                try
                {
                    prop.SetValue(null, megabytes);
                    Debug.Log($"[AabSizeWarningTool] App Bundle size threshold set to {megabytes} MB.");
                    return;
                }
                catch { }
            }

            if (TrySetSerializedValue(SerializedThresholdKey, megabytes))
                Debug.Log($"[AabSizeWarningTool] App Bundle size threshold set to {megabytes} MB (ProjectSettings.asset).");
            else
                Debug.LogWarning("[AabSizeWarningTool] Could not change the App Bundle size threshold.");
        }

        #region Reflection

        // These Player Settings are not part of the public scripting documentation, but
        // validateAppBundleSize / appBundleSizeToValidate are exposed as properties on the
        // editor assemblies (verified on Unity 6.x).

        static IEnumerable<Type> CandidateTypes()
        {
            var android = typeof(PlayerSettings).GetNestedType("Android", BindingFlags.Public);
            if (android != null) yield return android;
            yield return typeof(PlayerSettings);
            yield return typeof(EditorUserBuildSettings);
        }

        static PropertyInfo FindStaticProperty(string name)
        {
            foreach (var type in CandidateTypes())
            {
                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (prop != null) return prop;
            }

            return null;
        }

        static bool TrySetViaReflection(bool value)
        {
            var prop = FindStaticProperty("validateAppBundleSize");
            if (prop == null) return false;

            try
            {
                prop.SetValue(null, value);
                return true;
            }
            catch { return false; }
        }

        #endregion

        #region ProjectSettings.asset fallback

        static Object[] LoadSettingsAssets()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(ProjectSettingsPath);
            return assets != null && assets.Length > 0 ? assets : null;
        }

        static int? ReadSerializedValue(string key)
        {
            var assets = LoadSettingsAssets();
            if (assets == null) return null;

            var prop = new SerializedObject(assets[0]).FindProperty(key);
            return prop != null ? prop.intValue : (int?)null;
        }

        static bool TrySetSerializedValue(string key, int value)
        {
            var assets = LoadSettingsAssets();
            if (assets == null) return false;

            var so = new SerializedObject(assets[0]);
            var prop = so.FindProperty(key);
            if (prop == null) return false;

            prop.intValue = value;
            so.ApplyModifiedProperties();
            return true;
        }

        #endregion
    }
}
