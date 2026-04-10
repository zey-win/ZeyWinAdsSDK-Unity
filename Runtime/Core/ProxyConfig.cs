using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Routes SDK requests via direct or proxy path.
    /// On first launch races both, caches the winner in PlayerPrefs.
    /// Re-checks once per day.
    /// </summary>
    public static class ProxyConfig
    {
        private const string ProxyBase = "https://www.proxodi.com/v1/proxy";
        private const string AuthKey = "asdjkasdkasdasd";
        private const string PrefRoute = "zeywin_route";       // "direct" or "proxy"
        private const string PrefRouteTs = "zeywin_route_ts";   // unix timestamp of last check
        private const int TtlSeconds = 86400; // 24h

        private static bool _resolved;
        private static bool _useDirect; // true = direct to CF, false = through proxy

        /// <summary>
        /// Whether route has been resolved (race finished or loaded from cache).
        /// </summary>
        public static bool IsResolved => _resolved;

        /// <summary>
        /// Resolves the fastest route. Call once at SDK init.
        /// Fires callback when ready.
        /// </summary>
        public static void Resolve(Action onReady)
        {
            // Check cache
            string cached = PlayerPrefs.GetString(PrefRoute, "");
            long ts = long.Parse(PlayerPrefs.GetString(PrefRouteTs, "0"));
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (!string.IsNullOrEmpty(cached) && (now - ts) < TtlSeconds)
            {
                _useDirect = cached == "direct";
                _resolved = true;
                Logger.Log($"[ProxyConfig] Using cached route: {cached}");
                onReady?.Invoke();
                return;
            }

            // Race
            UnityMainThreadDispatcher.Instance.StartCoroutine(RaceCoroutine(onReady));
        }

        private static IEnumerator RaceCoroutine(Action onReady)
        {
            string pingUrl = "https://zeywin-ads-api.whiteapps.workers.dev/api/v1/geo";
            string directUrl = pingUrl;
            string proxyUrl = ProxyBase + "?target=" + UnityWebRequest.EscapeURL(pingUrl) + "&auth=" + AuthKey;

            var directReq = UnityWebRequest.Head(directUrl);
            directReq.timeout = 3;
            var proxyReq = UnityWebRequest.Head(proxyUrl);
            proxyReq.SetRequestHeader("X-Internal-Proxy-Auth", AuthKey);
            proxyReq.timeout = 5;

            // Fire both
            var directOp = directReq.SendWebRequest();
            var proxyOp = proxyReq.SendWebRequest();

            string winner = null;

            // Poll until one wins
            while (winner == null)
            {
                if (directOp.isDone && directReq.result == UnityWebRequest.Result.Success)
                    winner = "direct";
                else if (proxyOp.isDone && proxyReq.result == UnityWebRequest.Result.Success)
                    winner = "proxy";
                else if (directOp.isDone && proxyOp.isDone)
                {
                    // Both failed — default to proxy (more likely to work in blocked geos)
                    winner = "proxy";
                }

                if (winner == null)
                    yield return null;
            }

            directReq.Dispose();
            proxyReq.Dispose();

            _useDirect = winner == "direct";
            _resolved = true;

            // Save
            PlayerPrefs.SetString(PrefRoute, winner);
            PlayerPrefs.SetString(PrefRouteTs, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            PlayerPrefs.Save();

            Logger.Log($"[ProxyConfig] Race winner: {winner}");
            onReady?.Invoke();
        }

        /// <summary>
        /// Prepares a URL for request — direct or wrapped through proxy.
        /// </summary>
        public static string WrapUrl(string targetUrl)
        {
            if (_useDirect)
                return targetUrl;

            return ProxyBase + "?target=" + UnityWebRequest.EscapeURL(targetUrl) + "&auth=" + AuthKey;
        }

        /// <summary>
        /// Adds proxy auth header if routing through proxy. No-op for direct.
        /// </summary>
        public static void AddAuthHeader(UnityWebRequest request)
        {
            if (!_useDirect)
                request.SetRequestHeader("X-Internal-Proxy-Auth", AuthKey);
        }
    }
}
