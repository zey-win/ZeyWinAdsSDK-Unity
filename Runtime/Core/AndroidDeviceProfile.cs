using UnityEngine;

namespace ZeyWinAds.Core
{
    internal static class AndroidDeviceProfile
    {
        private const int DefaultSystemMemoryThresholdMb = 4096;
        private const int DefaultGraphicsMemoryThresholdMb = 1024;
        private const int DefaultMemoryClassThresholdMb = 192;

        private static bool _resolved;
        private static bool _startupAdPreloadConstrained;
        private static string _startupAdPreloadConstraintReason = "not_android";

        public static bool IsStartupAdPreloadConstrained
        {
            get
            {
                if (!RemoteConfigBridge.GetBool("zeywin_low_memory_startup_guard_enabled", true))
                    return false;

                Resolve();
                return _startupAdPreloadConstrained;
            }
        }

        public static string StartupAdPreloadConstraintReason
        {
            get
            {
                Resolve();
                return string.IsNullOrEmpty(_startupAdPreloadConstraintReason)
                    ? "unknown"
                    : _startupAdPreloadConstraintReason;
            }
        }

        private static void Resolve()
        {
            if (_resolved)
                return;

            _resolved = true;
            _startupAdPreloadConstrained = false;
            _startupAdPreloadConstraintReason = "not_android";

#if UNITY_ANDROID && !UNITY_EDITOR
            int systemMemoryMb = SystemInfo.systemMemorySize;
            int graphicsMemoryMb = SystemInfo.graphicsMemorySize;
            int memoryClassMb = 0;
            int largeMemoryClassMb = 0;
            bool androidLowRam = false;

            TryReadActivityManager(out androidLowRam, out memoryClassMb, out largeMemoryClassMb);

            int systemThreshold = Mathf.Clamp(
                RemoteConfigBridge.GetInt("zeywin_low_memory_system_mb_threshold", DefaultSystemMemoryThresholdMb),
                512,
                16384);
            int graphicsThreshold = Mathf.Clamp(
                RemoteConfigBridge.GetInt("zeywin_low_memory_graphics_mb_threshold", DefaultGraphicsMemoryThresholdMb),
                128,
                4096);
            int memoryClassThreshold = Mathf.Clamp(
                RemoteConfigBridge.GetInt("zeywin_low_memory_class_mb_threshold", DefaultMemoryClassThresholdMb),
                64,
                1024);

            if (androidLowRam)
            {
                _startupAdPreloadConstrained = true;
                _startupAdPreloadConstraintReason = "android_low_ram";
            }
            else if (memoryClassMb > 0 && memoryClassMb <= memoryClassThreshold)
            {
                _startupAdPreloadConstrained = true;
                _startupAdPreloadConstraintReason = "memory_class_" + memoryClassMb + "mb";
            }
            else if (systemMemoryMb > 0 && systemMemoryMb <= systemThreshold)
            {
                _startupAdPreloadConstrained = true;
                _startupAdPreloadConstraintReason = "system_memory_" + systemMemoryMb + "mb";
            }
            else if (graphicsMemoryMb > 0 && graphicsMemoryMb <= graphicsThreshold)
            {
                _startupAdPreloadConstrained = true;
                _startupAdPreloadConstraintReason = "graphics_memory_" + graphicsMemoryMb + "mb";
            }
            else
            {
                _startupAdPreloadConstraintReason =
                    "ok_system_" + systemMemoryMb + "mb_graphics_" + graphicsMemoryMb + "mb_class_" + memoryClassMb + "mb_large_" + largeMemoryClassMb + "mb";
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static void TryReadActivityManager(out bool lowRam, out int memoryClassMb, out int largeMemoryClassMb)
        {
            lowRam = false;
            memoryClassMb = 0;
            largeMemoryClassMb = 0;

            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var context = activity.Call<AndroidJavaObject>("getApplicationContext"))
                using (var serviceName = new AndroidJavaClass("android.content.Context"))
                using (var activityManager = context.Call<AndroidJavaObject>(
                    "getSystemService",
                    serviceName.GetStatic<string>("ACTIVITY_SERVICE")))
                {
                    if (activityManager == null)
                        return;

                    lowRam = activityManager.Call<bool>("isLowRamDevice");
                    memoryClassMb = activityManager.Call<int>("getMemoryClass");
                    largeMemoryClassMb = activityManager.Call<int>("getLargeMemoryClass");
                }
            }
            catch (System.Exception ex)
            {
                Logger.Debug("Android device profile unavailable: {0}", ex.Message);
            }
        }
#endif
    }
}
