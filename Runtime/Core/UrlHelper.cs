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
    }
}
