using System;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Shared URL utility methods for Play Store detection and parsing.
    /// </summary>
    public static class UrlHelper
    {
        /// <summary>
        /// Checks if a URL points to the Google Play Store.
        /// </summary>
        public static bool IsPlayStoreUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return url.Contains("play.google.com/store/apps") || url.StartsWith("market://details");
        }

        /// <summary>
        /// Extracts the package name (bundle ID) from a Play Store URL.
        /// Supports both https://play.google.com/store/apps/details?id=... and market://details?id=...
        /// </summary>
        public static string ExtractBundleIdFromPlayStoreUrl(string url)
        {
            try
            {
                int idIndex = url.IndexOf("id=", StringComparison.Ordinal);
                if (idIndex < 0) return null;
                string afterId = url.Substring(idIndex + 3);
                int ampIndex = afterId.IndexOf('&');
                return ampIndex >= 0 ? afterId.Substring(0, ampIndex) : afterId;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Appends click_id as referrer parameter to a Play Store URL.
        /// Example: https://play.google.com/store/apps/details?id=com.example
        ///       → https://play.google.com/store/apps/details?id=com.example&referrer=utm_source%3Dzeywinads%26click_id%3Dxxx
        /// </summary>
        public static string AppendReferrer(string storeUrl, string clickId)
        {
            if (string.IsNullOrEmpty(storeUrl) || string.IsNullOrEmpty(clickId))
                return storeUrl;

            string referrerValue = Uri.EscapeDataString($"utm_source=zeywinads&click_id={clickId}");
            string separator = storeUrl.Contains("?") ? "&" : "?";
            return $"{storeUrl}{separator}referrer={referrerValue}";
        }

        /// <summary>
        /// Appends or replaces a single query parameter on the URL.
        /// If the URL already contains the parameter, the existing value is replaced.
        /// Empty value or empty url is a no-op (returns input unchanged).
        /// </summary>
        public static string SetQueryParam(string url, string key, string value)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                return url;

            string encoded = Uri.EscapeDataString(value);
            string keyEq = key + "=";

            int qIdx = url.IndexOf('?');
            int hashIdx = url.IndexOf('#');
            string baseAndQuery = hashIdx >= 0 ? url.Substring(0, hashIdx) : url;
            string fragment = hashIdx >= 0 ? url.Substring(hashIdx) : "";

            if (qIdx < 0)
            {
                return baseAndQuery + "?" + keyEq + encoded + fragment;
            }

            // Look for existing param. Boundary check: must be preceded by '?' or '&'.
            string query = baseAndQuery.Substring(qIdx);
            int searchFrom = 0;
            while (searchFrom < query.Length)
            {
                int found = query.IndexOf(keyEq, searchFrom, StringComparison.Ordinal);
                if (found < 0) break;
                char prev = query[found - 1];
                if (prev == '?' || prev == '&')
                {
                    int valEnd = query.IndexOf('&', found);
                    if (valEnd < 0) valEnd = query.Length;
                    string before = baseAndQuery.Substring(0, qIdx) + query.Substring(0, found) + keyEq + encoded;
                    string after = query.Substring(valEnd);
                    return before + after + fragment;
                }
                searchFrom = found + keyEq.Length;
            }

            // Not present — append.
            char sep = query.Length > 1 ? '&' : '\0'; // "?" alone vs "?x=y..."
            return baseAndQuery + (sep == '\0' ? "" : "&") + keyEq + encoded + fragment;
        }
    }
}
