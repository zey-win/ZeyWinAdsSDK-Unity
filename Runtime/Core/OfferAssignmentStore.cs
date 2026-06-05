using UnityEngine;

namespace ZeyWinAds.Core
{
    internal static class OfferAssignmentStore
    {
        private const string AssignedOfferUrlKey = "zeywinads_assigned_offer_url";
        private const string AssignedOfferUrlBackupKey = "zeywinads_assigned_offer_url_backup";
        private const string ResolvedOfferUrlKey = "zeywinads_resolved_offer_url";
        private const string ResolvedOfferUrlBackupKey = "zeywinads_resolved_offer_url_backup";

        public static bool HasAssignedOffer => !string.IsNullOrEmpty(GetPreferredOfferUrl());

        public static string GetPreferredOfferUrl()
        {
            string resolvedUrl = GetResolvedOfferUrl();
            if (!string.IsNullOrEmpty(resolvedUrl))
                return resolvedUrl;

            return GetAssignedOfferUrl();
        }

        public static string GetAssignedOfferUrl()
        {
            string assignedUrl = PlayerPrefs.GetString(AssignedOfferUrlKey, "");
            if (IsPersistableUrl(assignedUrl))
                return assignedUrl;

            string backupUrl = PlayerPrefs.GetString(AssignedOfferUrlBackupKey, "");
            if (IsPersistableUrl(backupUrl))
            {
                SaveAssignedOfferUrl(backupUrl);
                LogStickyEvent("Restored sticky offer URL from backup ({0})", DescribeUrl(backupUrl));
                return backupUrl;
            }

            return "";
        }

        public static string GetResolvedOfferUrl()
        {
            string resolvedUrl = PlayerPrefs.GetString(ResolvedOfferUrlKey, "");
            if (IsPersistableUrl(resolvedUrl))
                return resolvedUrl;

            string backupUrl = PlayerPrefs.GetString(ResolvedOfferUrlBackupKey, "");
            if (IsPersistableUrl(backupUrl))
            {
                SaveResolvedOfferUrl(backupUrl);
                LogStickyEvent("Restored resolved sticky offer URL from backup ({0})", DescribeUrl(backupUrl));
                return backupUrl;
            }

            return "";
        }

        public static string GetOrAssignOfferUrl(string url)
        {
            string resolvedUrl = GetResolvedOfferUrl();
            if (!string.IsNullOrEmpty(resolvedUrl))
            {
                LogStickyEvent("Using resolved sticky offer URL ({0})", DescribeUrl(resolvedUrl));
                return resolvedUrl;
            }

            string assignedUrl = GetAssignedOfferUrl();
            if (!string.IsNullOrEmpty(assignedUrl))
            {
                LogStickyEvent("Using sticky assigned offer URL ({0})", DescribeUrl(assignedUrl));
                return assignedUrl;
            }

            if (!IsPersistableUrl(url))
                return url;

            SaveAssignedOfferUrl(url);
            LogStickyEvent("Assigned sticky offer URL for this device ({0})", DescribeUrl(url));
            return url;
        }

        public static void PersistAssignedOfferUrl(string url, string reason)
        {
            if (!IsPersistableUrl(url))
                return;

            string assignedUrl = GetAssignedOfferUrl();
            if (!string.IsNullOrEmpty(assignedUrl))
            {
                LogStickyEvent("Sticky offer URL preserved during {0} ({1})", reason, DescribeUrl(assignedUrl));
                return;
            }

            SaveAssignedOfferUrl(url);
            LogStickyEvent("Sticky offer URL persisted during {0} ({1})", reason, DescribeUrl(url));
        }

        public static bool PromoteResolvedOfferUrl(string resolvedUrl)
        {
            if (!IsPromotableResolvedUrl(resolvedUrl))
                return false;

            string currentResolvedUrl = GetResolvedOfferUrl();
            if (string.Equals(currentResolvedUrl, resolvedUrl, System.StringComparison.Ordinal))
                return false;

            string assignedUrl = GetAssignedOfferUrl();
            if (string.IsNullOrEmpty(assignedUrl))
            {
                SaveAssignedOfferUrl(resolvedUrl);
                LogStickyEvent("Assigned sticky offer URL from resolved WebView navigation ({0})", DescribeUrl(resolvedUrl));
            }

            if (string.Equals(assignedUrl, resolvedUrl, System.StringComparison.Ordinal)
                && string.IsNullOrEmpty(currentResolvedUrl))
                return false;

            SaveResolvedOfferUrl(resolvedUrl);
            LogStickyEvent("Promoted resolved sticky offer URL ({0})", DescribeUrl(resolvedUrl));
            return true;
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(AssignedOfferUrlKey);
            PlayerPrefs.DeleteKey(AssignedOfferUrlBackupKey);
            PlayerPrefs.DeleteKey(ResolvedOfferUrlKey);
            PlayerPrefs.DeleteKey(ResolvedOfferUrlBackupKey);
            PlayerPrefs.Save();
        }

        private static void SaveAssignedOfferUrl(string url)
        {
            PlayerPrefs.SetString(AssignedOfferUrlKey, url);
            PlayerPrefs.SetString(AssignedOfferUrlBackupKey, url);
            PlayerPrefs.Save();
        }

        private static void SaveResolvedOfferUrl(string url)
        {
            PlayerPrefs.SetString(ResolvedOfferUrlKey, url);
            PlayerPrefs.SetString(ResolvedOfferUrlBackupKey, url);
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

        private static bool IsPromotableResolvedUrl(string url)
        {
            if (!IsPersistableUrl(url))
                return false;

            if (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri))
                return false;

            string host = uri.Host.ToLowerInvariant();
            return host != "accounts.google.com"
                && host != "appleid.apple.com"
                && host != "oauth.telegram.org"
                && host != "t.me"
                && host != "telegram.me";
        }

        private static string DescribeUrl(string url)
        {
            if (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri))
                return "invalid";

            string path = uri.AbsolutePath;
            if (string.IsNullOrEmpty(path) || path == "/")
                return uri.Host;

            if (path.Length > 32)
                path = path.Substring(0, 32) + "...";

            return uri.Host + path;
        }

        internal static void LogStickyEvent(string format, params object[] args)
        {
            string message = args == null || args.Length == 0
                ? format
                : string.Format(format, args);

            Logger.Log(message);
            WriteAndroidStickyLog(message);
        }

        private static void WriteAndroidStickyLog(string message)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var log = new AndroidJavaClass("android.util.Log"))
                {
                    log.CallStatic<int>("i", "ZeyWinAdsSticky", message);
                }
            }
            catch
            {
            }
#endif
        }
    }
}
