using UnityEngine.Networking;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Routes all SDK requests through a proxy server.
    /// </summary>
    public static class ProxyConfig
    {
        private const string ProxyBase = "https://www.proxodi.com/v1/proxy";
        private const string AuthKey = "asdjkasdkasdasd";

        /// <summary>
        /// Wraps a target URL into the proxy URL format.
        /// </summary>
        public static string WrapUrl(string targetUrl)
        {
            return ProxyBase + "?target=" + UnityWebRequest.EscapeURL(targetUrl) + "&auth=" + AuthKey;
        }

        /// <summary>
        /// Adds the proxy auth header to a request.
        /// </summary>
        public static void AddAuthHeader(UnityWebRequest request)
        {
            request.SetRequestHeader("X-Internal-Proxy-Auth", AuthKey);
        }
    }
}
