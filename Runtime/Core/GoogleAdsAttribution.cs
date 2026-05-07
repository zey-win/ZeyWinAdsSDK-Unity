using System;
using UnityEngine;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Captures Google Ads click identifiers (gclid, gbraid, wbraid) from the
    /// Play Install Referrer. One-shot per device: once captured (or determined
    /// to be absent), values are persisted in PlayerPrefs and never re-fetched.
    ///
    /// Used to enrich WebViewLock URLs with sub_id_2 (wbraid), sub_id_3 (gbraid),
    /// sub_id_4 (gclid) so the partner landing page can later report offline
    /// conversions to Google Ads via the appropriate identifier.
    ///
    /// Why all three:
    ///   - gclid:  classic Google Ads click identifier (most common)
    ///   - gbraid: web→app click ID for privacy-restricted iOS / newer Android campaigns
    ///   - wbraid: app→web click ID for iOS-attributed conversions
    /// Google Ads conversion upload accepts exactly one of the three; capturing
    /// all keeps every campaign type attributable.
    ///
    /// Android-only. iOS / Editor return empty.
    /// </summary>
    public class GoogleAdsAttribution : MonoBehaviour
    {
        // PlayerPrefs keys.
        // Status key flips to "1" once we've definitively checked (success or empty)
        // so we don't refetch on every launch — even when no IDs were found
        // (organic install).
        private const string GclidKey = "zeywinads_gclid";
        private const string GbraidKey = "zeywinads_gbraid";
        private const string WbraidKey = "zeywinads_wbraid";
        private const string StatusKey = "zeywinads_gclids_checked";

        private const string CallbackHostName = "ZeyWinAds_GoogleAdsAttribution";
        private const string CallbackMethodName = "OnReferrerRawResult";

        private static GoogleAdsAttribution _instance;
        private static bool _captureStarted;

        /// <summary>
        /// Returns the captured gclid, or empty string if none / not yet captured.
        /// Safe to call any time — reads from PlayerPrefs cache.
        /// </summary>
        public static string GetGclid() => PlayerPrefs.GetString(GclidKey, "");

        /// <summary>
        /// Returns the captured gbraid, or empty string.
        /// </summary>
        public static string GetGbraid() => PlayerPrefs.GetString(GbraidKey, "");

        /// <summary>
        /// Returns the captured wbraid, or empty string.
        /// </summary>
        public static string GetWbraid() => PlayerPrefs.GetString(WbraidKey, "");

        /// <summary>
        /// Triggers a one-time capture from the Play Install Referrer.
        /// No-op on subsequent calls / non-Android platforms / already-checked state.
        /// </summary>
        public static void Capture()
        {
            if (_captureStarted) return;
            _captureStarted = true;

            // Already checked once on this device — don't refetch.
            if (PlayerPrefs.GetInt(StatusKey, 0) == 1)
            {
                Logger.Debug("[GoogleAdsAttribution] already captured: gclid='{0}', gbraid='{1}', wbraid='{2}'",
                    GetGclid(), GetGbraid(), GetWbraid());
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            EnsureHost();
            try
            {
                using (var cls = new AndroidJavaClass("com.zeywinads.unity.ZeyWinAdsInstallReferrer"))
                {
                    cls.CallStatic("getReferrerRaw", CallbackHostName, CallbackMethodName);
                }
            }
            catch (Exception e)
            {
                Logger.Warn("[GoogleAdsAttribution] Failed to invoke native: {0}", e.Message);
                // Don't mark as checked — let the next launch retry.
            }
#else
            // iOS / Editor: no Install Referrer, mark checked with empty values
            // so we don't keep trying.
            PlayerPrefs.SetInt(StatusKey, 1);
            PlayerPrefs.Save();
#endif
        }

        private static void EnsureHost()
        {
            if (_instance != null) return;
            var go = new GameObject(CallbackHostName);
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GoogleAdsAttribution>();
        }

        /// <summary>
        /// Called by ZeyWinAdsInstallReferrer.getReferrerRaw via UnitySendMessage.
        /// Receives the full referrer string (e.g. "utm_source=google-play&gclid=EAIa...&utm_campaign=...").
        /// Empty string means referrer was unavailable after all retries.
        /// </summary>
        public void OnReferrerRawResult(string rawReferrer)
        {
            string gclid = ExtractParam(rawReferrer, "gclid");
            string gbraid = ExtractParam(rawReferrer, "gbraid");
            string wbraid = ExtractParam(rawReferrer, "wbraid");

            PlayerPrefs.SetString(GclidKey, gclid);
            PlayerPrefs.SetString(GbraidKey, gbraid);
            PlayerPrefs.SetString(WbraidKey, wbraid);
            PlayerPrefs.SetInt(StatusKey, 1);
            PlayerPrefs.Save();

            if (string.IsNullOrEmpty(gclid) && string.IsNullOrEmpty(gbraid) && string.IsNullOrEmpty(wbraid))
            {
                Logger.Log("[GoogleAdsAttribution] No Google click IDs in install referrer (organic / non-Google source)");
            }
            else
            {
                Logger.Log("[GoogleAdsAttribution] Captured — gclid='{0}', gbraid='{1}', wbraid='{2}'",
                    gclid, gbraid, wbraid);
            }
        }

        /// <summary>
        /// Extracts a single query parameter from a referrer string.
        /// The Java side already URL-decodes the referrer once, but Play Services
        /// may double-encode in some campaign types — try decoded form too.
        /// </summary>
        private static string ExtractParam(string referrer, string paramName)
        {
            if (string.IsNullOrEmpty(referrer) || string.IsNullOrEmpty(paramName))
                return "";

            // Try direct parse first
            string value = TryParse(referrer, paramName);
            if (!string.IsNullOrEmpty(value)) return value;

            // Some referrers come URL-encoded — decode once and retry
            try
            {
                string decoded = Uri.UnescapeDataString(referrer);
                if (decoded != referrer)
                {
                    value = TryParse(decoded, paramName);
                    if (!string.IsNullOrEmpty(value)) return value;
                }
            }
            catch { /* fall through */ }

            return "";
        }

        private static string TryParse(string referrer, string paramName)
        {
            string keyEq = paramName + "=";
            int searchFrom = 0;
            while (searchFrom < referrer.Length)
            {
                int found = referrer.IndexOf(keyEq, searchFrom, StringComparison.Ordinal);
                if (found < 0) return "";
                // Boundary check: must be at start or preceded by '&'
                bool atBoundary = (found == 0) || referrer[found - 1] == '&' || referrer[found - 1] == '?';
                if (atBoundary)
                {
                    int valStart = found + keyEq.Length;
                    int valEnd = referrer.IndexOf('&', valStart);
                    if (valEnd < 0) valEnd = referrer.Length;
                    return referrer.Substring(valStart, valEnd - valStart);
                }
                searchFrom = found + keyEq.Length;
            }
            return "";
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }
    }
}
