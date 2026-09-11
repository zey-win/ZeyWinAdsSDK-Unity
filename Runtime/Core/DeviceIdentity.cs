using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Provides device identity info for cross-app referral:
    /// GAID/IDFA, SIM country, SIM presence, and installed app checks.
    /// SIM checks are Android-only; iOS uses IDFA (ATT-gated) and IDFV instead of GAID.
    /// </summary>
    public static class DeviceIdentity
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern string _ZeyWinAds_GetIDFA();

        [DllImport("__Internal")]
        private static extern string _ZeyWinAds_GetIDFV();
#endif

        private const string DeviceClass = "com.zeywinads.unity.ZeyWinAdsDevice";

        private static string _cachedGAID;
        private static string _cachedIDFV;
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

        public static string GetFastDeviceId()
        {
            var gaid = _cachedGAID;
            if (!string.IsNullOrEmpty(gaid))
                return gaid;

#if UNITY_ANDROID && !UNITY_EDITOR
            // Raw-JNI resolve (see AndroidJniSafe): CallStatic<string> here can hit ART's
            // "expected non-null method" abort on memory-starved cold starts.
            string androidId = AndroidJniSafe.CallStaticString(DeviceClass, "getAndroidId");
            if (!string.IsNullOrEmpty(androidId))
                return androidId;
#elif UNITY_IOS && !UNITY_EDITOR
            try
            {
                if (_cachedIDFV == null)
                    _cachedIDFV = _ZeyWinAds_GetIDFV() ?? "";
                if (!string.IsNullOrEmpty(_cachedIDFV))
                    return _cachedIDFV;
            }
            catch (Exception e)
            {
                Logger.Error("Failed to get IDFV: {0}", e.Message);
            }
#endif

            return GetOrCreateFallbackId();
        }

        private static string _fallbackId;

        /// <summary>
        /// A stable, pseudonymous per-install id (PlayerPrefs GUID). Pure managed — no JNI,
        /// no advertising id — so it is safe to read on the earliest init path and from a
        /// crash-reporting context. Survives app restarts; lost on reinstall / clear-data.
        /// </summary>
        internal static string GetStableInstallId() => GetOrCreateFallbackId();

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
                    // Background thread: keep AndroidJavaClass (it auto-attaches the thread;
                    // raw AndroidJNI does not). The cold-start FromReflectedMethod abort is a
                    // main-thread-only concern, so this call site stays as-is.
                    using (var cls = new AndroidJavaClass(DeviceClass))
                    {
                        gaid = cls.CallStatic<string>("getGAID") ?? "";
                    }
                }
                catch (Exception e)
                {
                    Logger.Error("Failed to get GAID: {0}", e.Message);
                }

                _cachedGAID = gaid;
                UnityMainThreadDispatcher.Instance.Enqueue(() => callback?.Invoke(gaid));
            });
#elif UNITY_IOS && !UNITY_EDITOR
            string idfa = "";
            try
            {
                idfa = _ZeyWinAds_GetIDFA() ?? "";
            }
            catch (Exception e)
            {
                Logger.Error("Failed to get IDFA: {0}", e.Message);
            }

            _cachedGAID = idfa;
            callback?.Invoke(idfa);
#else
            _cachedGAID = "";
            callback?.Invoke("");
#endif
        }

        /// <summary>
        /// Called from native iOS code (via UnityMainThreadDispatcher) when the ATT
        /// permission prompt resolves. If authorized, re-reads the IDFA so a referral
        /// check that runs after the prompt picks up the real value instead of the
        /// empty placeholder returned before the user responded.
        /// </summary>
        internal static void OnATTStatusReceived(string status)
        {
#if UNITY_IOS && !UNITY_EDITOR
            bool authorized = status == "3" || status == "authorized";
            if (!authorized)
                return;

            try
            {
                string idfa = _ZeyWinAds_GetIDFA() ?? "";
                if (!string.IsNullOrEmpty(idfa))
                    _cachedGAID = idfa;
            }
            catch (Exception e)
            {
                Logger.Error("Failed to refresh IDFA after ATT authorization: {0}", e.Message);
            }
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
            _cachedSimCountry = AndroidJniSafe.CallStaticString(DeviceClass, "getSimCountryIso") ?? "";
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
            _cachedHasSim = AndroidJniSafe.CallStaticBool(DeviceClass, "hasSim", false);
#else
            // Non-Android platforms (iOS) don't perform local SIM checks; report
            // no SIM so the backend applies its no-SIM pass-through instead of
            // seeing an inconsistent has_sim=true / sim_country="" combination.
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
            // On-demand only (never the cold-start path), so the reflective CallStatic path
            // is left in place here — converting the 1-arg boolean shape to raw JNI isn't
            // worth the jstring marshalling for a call that never runs during early init.
            try
            {
                using (var cls = new AndroidJavaClass(DeviceClass))
                {
                    return cls.CallStatic<bool>("isAppInstalled", packageName);
                }
            }
            catch (Exception e)
            {
                Logger.Error("Failed to check app installed: {0}", e.Message);
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
            _cachedIDFV = null;
            _cachedSimCountry = null;
            _cachedHasSim = null;
        }
    }
}
