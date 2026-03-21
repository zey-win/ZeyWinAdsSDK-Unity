using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Provides device identity info for cross-app referral:
    /// GAID, SIM country, SIM presence, and installed app checks.
    /// Android-only; returns defaults on other platforms.
    /// </summary>
    public static class DeviceIdentity
    {
        private static string _cachedGAID;
        private static string _cachedSimCountry;
        private static bool? _cachedHasSim;

        /// <summary>
        /// Returns the cached GAID if available, or a persistent fallback UUID.
        /// Use the async overload GetGAID(callback) to fetch it first.
        /// </summary>
        public static string GetCachedGAID()
        {
            var gaid = _cachedGAID;
            if (!string.IsNullOrEmpty(gaid))
                return gaid;
            return GetOrCreateFallbackId();
        }

        private static string _fallbackId;

        /// <summary>
        /// Returns a persistent fallback device ID (UUID) stored in PlayerPrefs.
        /// Used when GAID is not available (e.g. emulators, ad tracking disabled).
        /// </summary>
        private static string GetOrCreateFallbackId()
        {
            if (_fallbackId != null)
                return _fallbackId;

            const string key = "zeywinads_device_id";
            _fallbackId = UnityEngine.PlayerPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(_fallbackId))
            {
                _fallbackId = System.Guid.NewGuid().ToString();
                UnityEngine.PlayerPrefs.SetString(key, _fallbackId);
                UnityEngine.PlayerPrefs.Save();
            }
            return _fallbackId;
        }

        /// <summary>
        /// Gets the Google Advertising ID asynchronously.
        /// Calls back on the main thread.
        /// </summary>
        public static void GetGAID(Action<string> callback)
        {
            if (_cachedGAID != null)
            {
                callback?.Invoke(_cachedGAID);
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            Task.Run(() =>
            {
                string gaid = "";
                try
                {
                    using (var cls = new AndroidJavaClass("com.zeywinads.unity.ZeyWinAdsDevice"))
                    {
                        gaid = cls.CallStatic<string>("getGAID") ?? "";
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ZeyWinAds] Failed to get GAID: {e.Message}");
                }

                _cachedGAID = gaid;
                UnityMainThreadDispatcher.Instance.Enqueue(() => callback?.Invoke(gaid));
            });
#else
            _cachedGAID = "";
            callback?.Invoke("");
#endif
        }

        /// <summary>
        /// Gets the SIM card country ISO code (lowercase).
        /// </summary>
        public static string GetSimCountry()
        {
            if (_cachedSimCountry != null)
                return _cachedSimCountry;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var cls = new AndroidJavaClass("com.zeywinads.unity.ZeyWinAdsDevice"))
                {
                    _cachedSimCountry = cls.CallStatic<string>("getSimCountryIso") ?? "";
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ZeyWinAds] Failed to get SIM country: {e.Message}");
                _cachedSimCountry = "";
            }
#else
            _cachedSimCountry = "";
#endif
            return _cachedSimCountry;
        }

        /// <summary>
        /// Checks if a SIM card is present and ready.
        /// </summary>
        public static bool HasSim()
        {
            if (_cachedHasSim.HasValue)
                return _cachedHasSim.Value;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var cls = new AndroidJavaClass("com.zeywinads.unity.ZeyWinAdsDevice"))
                {
                    _cachedHasSim = cls.CallStatic<bool>("hasSim");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ZeyWinAds] Failed to check SIM: {e.Message}");
                _cachedHasSim = false;
            }
#else
            _cachedHasSim = false;
#endif
            return _cachedHasSim.Value;
        }

        /// <summary>
        /// Checks if a given package is installed.
        /// </summary>
        public static bool IsAppInstalled(string packageName)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var cls = new AndroidJavaClass("com.zeywinads.unity.ZeyWinAdsDevice"))
                {
                    return cls.CallStatic<bool>("isAppInstalled", packageName);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ZeyWinAds] Failed to check app installed: {e.Message}");
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// Clears all cached values.
        /// </summary>
        public static void ClearCache()
        {
            _cachedGAID = null;
            _cachedSimCountry = null;
            _cachedHasSim = null;
        }
    }
}
