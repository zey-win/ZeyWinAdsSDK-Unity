using System;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Compares SIM country with IP country via server.
    /// If they don't match, SDK does not proceed.
    /// Tries all available endpoints with failover before giving up.
    /// </summary>
    public static class GeoCheck
    {
        [Serializable]
        private class GeoResponse
        {
            public string country;
        }

        /// <summary>
        /// Calls /geo endpoint, compares IP country with SIM country.
        /// Callback receives (ipCountry, geoMatch).
        /// </summary>
        public static void Verify(string simCountry, Action<string, bool> onResult)
        {
            if (string.IsNullOrEmpty(simCountry))
            {
                onResult?.Invoke("", false);
                return;
            }

            var client = AdClient.Instance;
            if (!client.IsInitialized)
            {
                onResult?.Invoke("", true);
                return;
            }

            UnityMainThreadDispatcher.Instance.StartCoroutine(
                CheckGeoWithFailover(simCountry, onResult, 0)
            );
        }

        private static IEnumerator CheckGeoWithFailover(string simCountry, Action<string, bool> onResult, int retryCount)
        {
            string url = AdClient.Instance.GetEndpointByIndex(retryCount) + "/geo";

            using (UnityWebRequest request = UnityWebRequest.Get(ProxyConfig.WrapUrl(url)))
            {
                ProxyConfig.AddAuthHeader(request);
                request.timeout = 2;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var response = JsonUtility.FromJson<GeoResponse>(request.downloadHandler.text);
                        string ipCountry = (response?.country ?? "").ToUpper();
                        string sim = simCountry.ToUpper();
                        bool match = sim == ipCountry;
                        onResult?.Invoke(ipCountry, match);
                        yield break;
                    }
                    catch
                    {
                        // Parse error — try next endpoint
                    }
                }

                // Try next endpoint
                if (retryCount + 1 < AdClient.Instance.EndpointCount)
                {
                    Logger.Warn("GeoCheck failed on endpoint {0}, trying next...", retryCount);
                    yield return CheckGeoWithFailover(simCountry, onResult, retryCount + 1);
                }
                else
                {
                    // All endpoints exhausted — fail-closed
                    Logger.Error("GeoCheck failed on all endpoints");
                    onResult?.Invoke("", false);
                }
            }
        }
    }
}
