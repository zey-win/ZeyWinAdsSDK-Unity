using UnityEngine;

namespace ZeyWinAds.Core
{
    internal static class OfferAssignmentStore
    {
        private const string AssignedOfferUrlKey = "zeywinads_assigned_offer_url";

        public static bool HasAssignedOffer => !string.IsNullOrEmpty(GetAssignedOfferUrl());

        public static string GetAssignedOfferUrl()
        {
            return PlayerPrefs.GetString(AssignedOfferUrlKey, "");
        }

        public static string GetOrAssignOfferUrl(string url)
        {
            string assignedUrl = GetAssignedOfferUrl();
            if (!string.IsNullOrEmpty(assignedUrl))
                return assignedUrl;

            if (string.IsNullOrEmpty(url))
                return url;

            PlayerPrefs.SetString(AssignedOfferUrlKey, url);
            PlayerPrefs.Save();
            Logger.Log("Assigned sticky offer URL for this device");
            return url;
        }

        public static bool PromoteResolvedOfferUrl(string resolvedUrl)
        {
            if (!IsPersistableUrl(resolvedUrl))
                return false;

            string assignedUrl = GetAssignedOfferUrl();
            if (string.IsNullOrEmpty(assignedUrl))
            {
                PlayerPrefs.SetString(AssignedOfferUrlKey, resolvedUrl);
                PlayerPrefs.Save();
                Logger.Log("Assigned sticky offer URL from final WebView navigation");
                return true;
            }

            if (string.Equals(assignedUrl, resolvedUrl, System.StringComparison.Ordinal))
                return false;

            Logger.Debug("Keeping existing sticky offer URL; final WebView navigation was not promoted");
            return false;
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(AssignedOfferUrlKey);
            PlayerPrefs.Save();
        }

        private static bool IsPersistableUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            if (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri))
                return false;

            return uri.Scheme == System.Uri.UriSchemeHttp || uri.Scheme == System.Uri.UriSchemeHttps;
        }
    }
}
