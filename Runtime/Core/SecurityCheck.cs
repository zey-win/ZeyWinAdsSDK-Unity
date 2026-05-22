using UnityEngine;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Checks for debugger, inspector, and hooking tools on the device.
    /// If suspicious apps are found, ad display is blocked.
    /// </summary>
    public static class SecurityCheck
    {
        private static bool? _isClean;
        private static bool? _isRooted;
        private static string _detectedPackages;
        private static string _rootIndicators;

        /// <summary>
        /// Returns true if the device is clean (no suspicious apps detected).
        /// Result is cached after first call.
        /// </summary>
        public static bool IsDeviceClean()
        {
            if (_isClean.HasValue)
                return _isClean.Value;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var cls = new AndroidJavaClass("com.zeywinads.unity.ZeyWinAdsSecurityCheck"))
                {
                    _detectedPackages = cls.CallStatic<string>("getDetectedPackages") ?? "";
                    _isClean = string.IsNullOrEmpty(_detectedPackages);
                }
            }
            catch (System.Exception e)
            {
                Logger.Error("Security check failed: {0}", e.Message);
                _isClean = true; // Don't block on error
                _detectedPackages = "";
            }
#else
            _isClean = true;
            _detectedPackages = "";
#endif

            // No logging — silent check

            return _isClean.Value;
        }

        /// <summary>
        /// Returns comma-separated list of detected suspicious package names.
        /// Empty string if device is clean.
        /// </summary>
        public static string GetDetectedPackages()
        {
            if (_detectedPackages == null)
                IsDeviceClean();
            return _detectedPackages;
        }

        public static bool IsRooted()
        {
            if (_isRooted.HasValue)
                return _isRooted.Value;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var cls = new AndroidJavaClass("com.zeywinads.unity.ZeyWinAdsSecurityCheck"))
                {
                    _rootIndicators = cls.CallStatic<string>("getRootIndicators") ?? "";
                    _isRooted = !string.IsNullOrEmpty(_rootIndicators);
                }
            }
            catch (System.Exception e)
            {
                Logger.Error("Root check failed: {0}", e.Message);
                _isRooted = false;
                _rootIndicators = "";
            }
#else
            _isRooted = false;
            _rootIndicators = "";
#endif

            return _isRooted.Value;
        }

        public static string GetRootIndicators()
        {
            if (_rootIndicators == null)
                IsRooted();
            return _rootIndicators;
        }

        /// <summary>
        /// Clears cached result to force re-check.
        /// </summary>
        public static void ClearCache()
        {
            _isClean = null;
            _isRooted = null;
            _detectedPackages = null;
            _rootIndicators = null;
        }
    }
}
