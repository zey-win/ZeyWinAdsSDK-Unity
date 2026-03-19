using System;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Compares SIM country with IP country via server.
    /// If they don't match, SDK does not proceed.
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
                CheckGeoCoroutine(simCountry, onResult)
            );
        }

        private static IEnumerator CheckGeoCoroutine(string simCountry, Action<string, bool> onResult)
        {
            string url = AdClient.Instance.GetGeoEndpoint();

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 5;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    // Network error — allow SDK to work
                    onResult?.Invoke("", true);
                    yield break;
                }

                try
                {
                    var response = JsonUtility.FromJson<GeoResponse>(request.downloadHandler.text);
                    string ipCountry = (response?.country ?? "").ToUpper();
                    string sim = simCountry.ToUpper();
                    bool match = sim == ipCountry;
                    onResult?.Invoke(ipCountry, match);
                }
                catch
                {
                    // Parse error — allow SDK to work
                    onResult?.Invoke("", true);
                }
            }
        }
    }
}
