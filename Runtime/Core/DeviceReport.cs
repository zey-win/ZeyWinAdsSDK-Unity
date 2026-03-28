using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Sends device security report to the server and returns the server's blocking decision.
    /// Tries all available endpoints with failover before falling back to client-side decision.
    /// </summary>
    public static class DeviceReport
    {
        [Serializable]
        private class ReportPayload
        {
            public string device_id;
            public string bundle_id;
            public bool has_sim;
            public string sim_country;
            public string detected_packages;
            public bool device_clean;
            public string sdk_status;
            public string block_reason;
            public string device_model;
            public string os_version;
        }

        [Serializable]
        private class ReportResponse
        {
            public bool success;
            public string sdk_status;
            public string block_reason;
        }

        /// <summary>
        /// Sends device report and invokes callback with server's blocking decision.
        /// Callback receives (serverSdkStatus, serverBlockReason).
        /// If all endpoints fail, returns the client-side values as fallback.
        /// </summary>
        public static void Send(bool hasSim, string simCountry, string detectedPackages, bool deviceClean, string sdkStatus, string blockReason, Action<string, string> onResult = null)
        {
            DeviceIdentity.GetGAID((gaid) =>
            {
                var payload = new ReportPayload
                {
                    device_id = string.IsNullOrEmpty(gaid) ? DeviceIdentity.GetCachedGAID() : gaid,
                    bundle_id = AdClient.Instance.BundleId ?? "",
                    has_sim = hasSim,
                    sim_country = simCountry ?? "",
                    detected_packages = detectedPackages ?? "",
                    device_clean = deviceClean,
                    sdk_status = sdkStatus,
                    block_reason = blockReason,
                    device_model = SystemInfo.deviceModel ?? "",
                    os_version = SystemInfo.operatingSystem ?? ""
                };

                string json = JsonUtility.ToJson(payload);

                UnityMainThreadDispatcher.Instance.StartCoroutine(
                    SendWithFailover(json, sdkStatus, blockReason, onResult, 0)
                );
            });
        }

        private static IEnumerator SendWithFailover(string json, string fallbackStatus, string fallbackReason, Action<string, string> onResult, int retryCount)
        {
            string url = AdClient.Instance.GetEndpointByIndex(retryCount) + "/device/report";

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 10;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    if (onResult == null)
                        yield break;

                    try
                    {
                        var response = JsonUtility.FromJson<ReportResponse>(request.downloadHandler.text);
                        if (response != null && !string.IsNullOrEmpty(response.sdk_status))
                        {
                            onResult.Invoke(response.sdk_status, response.block_reason ?? "none");
                            yield break;
                        }
                    }
                    catch
                    {
                        // Parse error — try next endpoint
                    }
                }

                // Try next endpoint
                if (retryCount + 1 < AdClient.Instance.EndpointCount)
                {
                    Logger.Warn("DeviceReport failed on endpoint {0}, trying next...", retryCount);
                    yield return SendWithFailover(json, fallbackStatus, fallbackReason, onResult, retryCount + 1);
                }
                else
                {
                    // All endpoints exhausted — use client-side decision as fallback
                    if (onResult != null)
                    {
                        Logger.Warn("DeviceReport failed on all endpoints, using fallback: {0}", fallbackStatus);
                        onResult.Invoke(fallbackStatus, fallbackReason);
                    }
                }
            }
        }
    }
}
