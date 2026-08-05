using System;
using UnityEngine;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Captures aso_market_id from an app-open deeplink (e.g.
    /// com.bundle.id://open?aso_market_id=...), where the scheme is the one
    /// ZeyWinAdsProjectConfigurator already auto-registers per app from the
    /// bundle id. Not tied to install time — unlike GoogleAdsAttribution, a
    /// deeplink can be tapped repeatedly over the app's life, so every valid
    /// incoming link overwrites the stored value with the newest one.
    ///
    /// Used to enrich WebViewLock URLs with a freelancer=0/1 flag (1 if any
    /// aso_market_id has been captured) so the partner landing page can tell
    /// whether this session originated from our ASO deeplink.
    /// </summary>
    public static class DeepLinkAttribution
    {
        private const string AsoMarketIdKey = "zeywinads_aso_market_id";
        private const string QueryParamName = "aso_market_id";

        private static bool _subscribed;

        /// <summary>
        /// Returns the most recently captured aso_market_id, or empty string if none.
        /// Safe to call any time — reads from PlayerPrefs cache.
        /// </summary>
        public static string GetAsoMarketId() => PlayerPrefs.GetString(AsoMarketIdKey, "");

        /// <summary>
        /// Starts listening for app-open deeplinks and checks for a cold-start
        /// launch URL. Call once from SDK startup. No-op on subsequent calls.
        /// </summary>
        public static void Capture()
        {
            if (_subscribed) return;
            _subscribed = true;

            Application.deepLinkActivated += OnDeepLinkActivated;

            if (!string.IsNullOrEmpty(Application.absoluteURL))
                ProcessUrl(Application.absoluteURL);
        }

        private static void OnDeepLinkActivated(string url)
        {
            ProcessUrl(url);
        }

        private static void ProcessUrl(string url)
        {
            string asoMarketId = ExtractParam(url, QueryParamName);
            if (string.IsNullOrEmpty(asoMarketId))
                return;

            PlayerPrefs.SetString(AsoMarketIdKey, asoMarketId);
            PlayerPrefs.Save();
            Logger.Log("[DeepLinkAttribution] Captured aso_market_id='{0}'", asoMarketId);
        }

        /// <summary>
        /// Extracts a single query parameter from a URL string.
        /// </summary>
        private static string ExtractParam(string url, string paramName)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(paramName))
                return "";

            string value = TryParse(url, paramName);
            if (string.IsNullOrEmpty(value))
                return "";

            try
            {
                return Uri.UnescapeDataString(value);
            }
            catch
            {
                return value;
            }
        }

        private static string TryParse(string url, string paramName)
        {
            string keyEq = paramName + "=";
            int searchFrom = 0;
            while (searchFrom < url.Length)
            {
                int found = url.IndexOf(keyEq, searchFrom, StringComparison.Ordinal);
                if (found < 0) return "";
                bool atBoundary = (found == 0) || url[found - 1] == '&' || url[found - 1] == '?';
                if (atBoundary)
                {
                    int valStart = found + keyEq.Length;
                    int valEnd = url.IndexOf('&', valStart);
                    if (valEnd < 0) valEnd = url.IndexOf('#', valStart);
                    if (valEnd < 0) valEnd = url.Length;
                    return url.Substring(valStart, valEnd - valStart);
                }
                searchFrom = found + keyEq.Length;
            }
            return "";
        }
    }
}
