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
        private static string _detectedPackages;

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
                Debug.LogError($"[ZeyWinAds] Security check failed: {e.Message}");
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

        /// <summary>
        /// Clears cached result to force re-check.
        /// </summary>
        public static void ClearCache()
        {
            _isClean = null;
            _detectedPackages = null;
        }
    }
}
