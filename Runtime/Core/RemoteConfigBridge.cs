using System;
using System.Reflection;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Optional Firebase Remote Config bridge. Uses reflection so the SDK keeps
    /// compiling in Unity projects that do not install Firebase packages.
    /// </summary>
    internal static class RemoteConfigBridge
    {
        private static object _defaultInstance;
        private static PropertyInfo _getValueProperty;
        private static MethodInfo _getValueMethod;
        private static bool _resolved;

        public static bool GetBool(string key, bool defaultValue)
        {
            object value = GetConfigValue(key);
            if (value == null)
                return defaultValue;

            object raw = GetPropertyValue(value, "BooleanValue");
            if (raw is bool boolValue)
                return boolValue;

            string text = GetStringFromValue(value);
            return bool.TryParse(text, out bool parsed) ? parsed : defaultValue;
        }

        public static int GetInt(string key, int defaultValue)
        {
            object value = GetConfigValue(key);
            if (value == null)
                return defaultValue;

            object raw = GetPropertyValue(value, "LongValue");
            if (raw is long longValue)
                return ClampInt(longValue, defaultValue);
            if (raw is int intValue)
                return intValue;

            string text = GetStringFromValue(value);
            return int.TryParse(text, out int parsed) ? parsed : defaultValue;
        }

        public static string GetString(string key, string defaultValue)
        {
            object value = GetConfigValue(key);
            if (value == null)
                return defaultValue;

            string text = GetStringFromValue(value);
            return string.IsNullOrEmpty(text) ? defaultValue : text;
        }

        private static object GetConfigValue(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            Resolve();
            if (_defaultInstance == null)
                return null;

            try
            {
                if (_getValueMethod != null)
                    return _getValueMethod.Invoke(_defaultInstance, new object[] { key });

                if (_getValueProperty != null)
                {
                    object indexer = _getValueProperty.GetValue(_defaultInstance, new object[] { key });
                    return indexer;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug("Firebase Remote Config read failed for {0}: {1}", key, ex.Message);
            }

            return null;
        }

        private static void Resolve()
        {
            if (_resolved)
                return;
            _resolved = true;

            Type remoteConfigType = FindType("Firebase.RemoteConfig.FirebaseRemoteConfig");
            if (remoteConfigType == null)
                return;

            try
            {
                PropertyInfo defaultInstanceProperty = remoteConfigType.GetProperty("DefaultInstance", BindingFlags.Public | BindingFlags.Static);
                _defaultInstance = defaultInstanceProperty?.GetValue(null, null);
                if (_defaultInstance == null)
                    return;

                _getValueMethod = remoteConfigType.GetMethod("GetValue", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
                _getValueProperty = remoteConfigType.GetProperty("Item", BindingFlags.Public | BindingFlags.Instance);
            }
            catch (Exception ex)
            {
                Logger.Debug("Firebase Remote Config unavailable: {0}", ex.Message);
            }
        }

        private static Type FindType(string fullName)
        {
            Type type = Type.GetType(fullName);
            if (type != null)
                return type;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType(fullName);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static object GetPropertyValue(object target, string propertyName)
        {
            try
            {
                return target?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target, null);
            }
            catch
            {
                return null;
            }
        }

        private static string GetStringFromValue(object value)
        {
            object raw = GetPropertyValue(value, "StringValue");
            return raw as string;
        }

        private static int ClampInt(long value, int fallback)
        {
            if (value > int.MaxValue || value < int.MinValue)
                return fallback;
            return (int)value;
        }
    }
}
