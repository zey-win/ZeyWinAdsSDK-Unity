using System;
using System.Reflection;
using UnityEngine;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Owns the crash-reporting lifecycle for the SDK. Resolves a soft reference to
    /// <c>Firebase.Crashlytics.Crashlytics</c> via reflection only - the same approach as
    /// <see cref="FirebaseMessagingService"/> - so the SDK never vendors or hard-references
    /// the Crashlytics package; the consuming game installs it (or not) on its own.
    ///
    /// Everything this writes shows up in the Firebase console on each crash event:
    ///  - custom keys, all prefixed <c>zw_</c>, filterable in the Issues list
    ///  - breadcrumb <c>Log()</c> lines on the event timeline
    ///
    /// e.g. a fatal carrying <c>zw_init_stage = security_check</c> is the SecurityCheck JNI
    /// abort during <see cref="ZeyWinAds.Initialize"/> - identifiable with no symbolication.
    /// Every method is a no-op when Crashlytics is absent and never throws.
    /// </summary>
    internal static class CrashReportingService
    {
        private const string CrashlyticsTypeName = "Firebase.Crashlytics.Crashlytics";

        private static bool _bridgeResolved;
        private static bool _contextStamped;
        private static MethodInfo _setCustomKey;   // static void SetCustomKey(string, string)
        private static MethodInfo _log;            // static void Log(string)
        private static MethodInfo _setUserId;      // static void SetUserId(string)
        private static MethodInfo _logException;   // static void LogException(Exception)

        /// <summary>
        /// Stamps the process-wide context keys that apply to every crash - SDK version, a
        /// stable pseudonymous install id, and platform. Call once, early in
        /// <see cref="ZeyWinAds.Initialize"/>. Idempotent; safe when Crashlytics is absent.
        ///
        /// Uses a custom key <c>zw_device_id</c> rather than <c>Crashlytics.SetUserId</c>:
        /// the user-id slot is a single global the consuming game may want for its own
        /// player id, whereas a custom key is non-destructive and still lets you group and
        /// filter crashes by device in the console (answering "same user crash-looping" vs
        /// "one event per user").
        /// </summary>
        public static void Initialize()
        {
            if (_contextStamped)
                return;
            _contextStamped = true;

            EnsureBridge();
            SetKey("zw_sdk_version", ZeyWinAdsConfig.SdkVersion);
            SetKey("zw_device_id", DeviceIdentity.GetStableInstallId());
            SetKey("zw_platform", Application.platform.ToString());
        }

        /// <summary>
        /// Records how far <see cref="ZeyWinAds.Initialize"/> progressed. On a crash, the
        /// value of <c>zw_init_stage</c> is the last step that started; the matching
        /// breadcrumb marks it on the event timeline too.
        /// </summary>
        public static void SetInitStage(string stage)
        {
            SetKey("zw_init_stage", stage);
            Log("ZeyWinAds.Initialize: " + stage);
        }

        public static void SetKey(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
                return;
            EnsureBridge();
            try { _setCustomKey?.Invoke(null, new object[] { key, value ?? "" }); }
            catch { /* never let telemetry break the caller */ }
        }

        public static void SetKey(string key, bool value) => SetKey(key, value ? "true" : "false");

        public static void SetKey(string key, int value) =>
            SetKey(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public static void Log(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;
            EnsureBridge();
            try { _log?.Invoke(null, new object[] { message }); }
            catch { }
        }

        public static void SetUserId(string identifier)
        {
            EnsureBridge();
            try { _setUserId?.Invoke(null, new object[] { identifier ?? "" }); }
            catch { }
        }

        public static void LogException(Exception exception)
        {
            if (exception == null)
                return;
            EnsureBridge();
            try { _logException?.Invoke(null, new object[] { exception }); }
            catch { }
        }

        private static void EnsureBridge()
        {
            if (_bridgeResolved)
                return;
            _bridgeResolved = true;

            try
            {
                Type t = Type.GetType(CrashlyticsTypeName);
                if (t == null)
                {
                    Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    for (int i = 0; i < assemblies.Length && t == null; i++)
                        t = assemblies[i].GetType(CrashlyticsTypeName);
                }
                if (t == null)
                    return;

                _setCustomKey = t.GetMethod("SetCustomKey", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string), typeof(string) }, null);
                _log = t.GetMethod("Log", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string) }, null);
                _setUserId = t.GetMethod("SetUserId", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string) }, null);
                _logException = t.GetMethod("LogException", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(Exception) }, null);
            }
            catch
            {
                // Crashlytics not usable - stay a no-op.
            }
        }

        internal static void ResetForTests()
        {
            _bridgeResolved = false;
            _contextStamped = false;
            _setCustomKey = _log = _setUserId = _logException = null;
        }
    }
}
