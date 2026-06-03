using System;
using UnityEngine;
using Logger = ZeyWinAds.Core.Logger;

namespace ZeyWinAds.UI
{
    public static class UniWebViewSafety
    {
        private static readonly string[] ExternalSchemes =
        {
            "intent:",
            "market:",
            "mailto:",
            "tel:",
            "sms:",
            "whatsapp:",
            "tg:",
            "viber:"
        };

        private static readonly string[] DownloadExtensions =
        {
            ".apk",
            ".aab",
            ".zip",
            ".rar",
            ".7z",
            ".pdf",
            ".doc",
            ".docx",
            ".xls",
            ".xlsx",
            ".csv",
            ".mp4",
            ".mov",
            ".mp3",
            ".wav",
            ".bin",
            ".exe",
            ".dmg"
        };

        public static void ApplyOfferDefaults(UniWebView webView, string initialUrl, bool locked)
        {
            if (webView == null)
                return;

            UniWebView.SetAllowAutoPlay(true);
            UniWebView.SetAllowJavaScriptOpenWindow(true);
            UniWebView.SetAllowUniversalAccessFromFileURLs(true);

            ApplySafeAreaFrame(webView);
            webView.BackgroundColor = Color.black;
            webView.SetShowToolbar(false);
            webView.SetBackButtonEnabled(!locked);
            webView.SetOpenLinksInExternalBrowser(false);
            webView.SetSupportMultipleWindows(true, true);
            webView.SetAllowFileAccess(true);
            webView.SetAllowFileAccessFromFileURLs(true);
            webView.SetAcceptThirdPartyCookies(true);
            webView.SetAllowHTTPAuthPopUpWindow(true);
            webView.SetShowSpinnerWhileLoading(true);
            webView.SetSpinnerText("Loading");
            webView.SetAllowUserDismissSpinner(false);
            webView.SetDownloadEventForContextMenuEnabled(false);
            webView.RegisterShouldHandleRequest(ShouldHandleOfferRequest);

            string host = GetHost(initialUrl);
            if (!string.IsNullOrEmpty(host))
                webView.AddPermissionTrustDomain(host);
        }

        public static void ApplySafeAreaFrame(UniWebView webView)
        {
            if (webView == null)
                return;

            Rect safeArea = Screen.safeArea;
            if (safeArea.width <= 0 || safeArea.height <= 0)
                safeArea = new Rect(0, 0, Screen.width, Screen.height);

            float yFromTop = Screen.height - (safeArea.y + safeArea.height);
            webView.Frame = new Rect(safeArea.x, yFromTop, safeArea.width, safeArea.height);
        }

        public static bool ShouldHandleOfferRequest(UniWebViewChannelMethodHandleRequest request)
        {
            string url = request?.Url;
            if (string.IsNullOrEmpty(url))
                return true;

            if (ShouldOpenExternally(url) || LooksLikeDownload(url))
            {
                Logger.Debug("Opening WebView URL externally: {0}", url);
                Application.OpenURL(url);
                return false;
            }

            return true;
        }

        public static bool LooksLikeDownload(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            string path = url;
            int queryIndex = path.IndexOfAny(new[] { '?', '#' });
            if (queryIndex >= 0)
                path = path.Substring(0, queryIndex);

            foreach (string extension in DownloadExtensions)
            {
                if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static bool ShouldOpenExternally(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            foreach (string scheme in ExternalSchemes)
            {
                if (url.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string GetHost(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return uri.Host;

            return null;
        }
    }
}
