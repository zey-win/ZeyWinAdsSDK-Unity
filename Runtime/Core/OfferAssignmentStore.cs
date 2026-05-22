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

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(AssignedOfferUrlKey);
            PlayerPrefs.Save();
        }
    }
}
