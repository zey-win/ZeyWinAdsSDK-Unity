using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Sends device security report to the server.
    /// Fire-and-forget — does not block SDK initialization.
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

        public static void Send(bool hasSim, string simCountry, string detectedPackages, bool deviceClean, string sdkStatus, string blockReason)
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

                string url = AdClient.Instance.GetCurrentEndpointPublic() + "/device/report";
                string json = JsonUtility.ToJson(payload);

                UnityMainThreadDispatcher.Instance.StartCoroutine(SendReportCoroutine(url, json));
            });
        }

        private static IEnumerator SendReportCoroutine(string url, string json)
        {
            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 10;
                yield return request.SendWebRequest();
                // Fire and forget — no error handling needed
            }
        }
    }
}
