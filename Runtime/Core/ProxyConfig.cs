using System;
using UnityEngine.Networking;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Routes SDK requests. Product builds use direct API URLs only; proxy
    /// credentials must never be shipped in a client SDK.
    /// </summary>
    public static class ProxyConfig
    {
        private static bool _resolved = true;

        /// <summary>
        /// Whether route has been resolved.
        /// </summary>
        public static bool IsResolved => _resolved;

        /// <summary>
        /// Resolves the route. Kept asynchronous-compatible for older callers.
        /// </summary>
        public static void Resolve(Action onReady)
        {
            _resolved = true;
            Logger.Debug("Using direct route");
            onReady?.Invoke();
        }

        /// <summary>
        /// Prepares a URL for request.
        /// </summary>
        public static string WrapUrl(string targetUrl)
        {
            return targetUrl;
        }

        /// <summary>
        /// Adds route-specific headers. No-op for direct product routing.
        /// </summary>
        public static void AddAuthHeader(UnityWebRequest request)
        {
        }
    }
}
