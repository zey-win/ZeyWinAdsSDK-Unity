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
            public int battery_level;
            public bool is_charging;
        }

        [Serializable]
        private class ReportResponse
        {
            public bool success;
            public string sdk_status;
            public string block_reason;
        }

        // Separate from ReportPayload rather than adding a nullable field to it: JsonUtility
        // always serializes a nested [Serializable] field, even when null (emits "motion":{}),
        // so the only reliable way to guarantee phase 1's JSON has no "motion" key at all is to
        // never put the field on phase 1's payload class in the first place.
        [Serializable]
        private class ReportPayloadWithMotion
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
            public int battery_level;
            public bool is_charging;
            public MotionCollector.MotionData motion;
        }

        // Motion should be attached to at most one /device/report call, ever — per the
        // spec's rule that presence of the key (not which call sends it) is what matters.
        // Guaranteed true by construction today (Send()'s two call sites are mutually
        // exclusive and Initialize() can't re-enter), kept explicit so it stays true if
        // that changes later.
        private static bool _motionSent;

        private static int GetBatteryLevelPercent()
        {
            float level = SystemInfo.batteryLevel;
            if (level < 0f)
                return 0;
            return Mathf.Clamp(Mathf.RoundToInt(level * 100f), 0, 100);
        }

        private static bool GetIsCharging()
        {
            switch (SystemInfo.batteryStatus)
            {
                case BatteryStatus.Charging:
                case BatteryStatus.Full:
                    return true;
                default:
                    return false;
            }
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
                string deviceId = string.IsNullOrEmpty(gaid) ? DeviceIdentity.GetCachedGAID() : gaid;
                string bundleId = AdClient.Instance.BundleId ?? "";
                string simCountrySafe = simCountry ?? "";
                string detectedPackagesSafe = detectedPackages ?? "";
                string deviceModel = SystemInfo.deviceModel ?? "";
                string osVersion = SystemInfo.operatingSystem ?? "";

                var payload = new ReportPayload
                {
                    device_id = deviceId,
                    bundle_id = bundleId,
                    has_sim = hasSim,
                    sim_country = simCountrySafe,
                    detected_packages = detectedPackagesSafe,
                    device_clean = deviceClean,
                    sdk_status = sdkStatus,
                    block_reason = blockReason,
                    device_model = deviceModel,
                    os_version = osVersion,
                    battery_level = GetBatteryLevelPercent(),
                    is_charging = GetIsCharging()
                };

                string json = JsonUtility.ToJson(payload);

                UnityMainThreadDispatcher.Instance.StartCoroutine(
                    SendWithFailover(json, sdkStatus, blockReason, onResult, 0)
                );

                // Phase 2: motion runs in parallel, not gated on phase 1's response — gating
                // already happened in phase 1, phase 2's response is ignored either way.
                SendMotionFollowUp(deviceId, bundleId, hasSim, simCountrySafe, detectedPackagesSafe, deviceClean, sdkStatus, blockReason, deviceModel, osVersion);
            });
        }

        /// <summary>
        /// Collects ~2s of motion data and resends the exact same report body plus a
        /// "motion" block. Fire-and-forget: response ignored, never retried, sent at most
        /// once per app session. No-ops on non-Android platforms (MotionCollector.Collect
        /// never invokes its callback there).
        /// </summary>
        private static void SendMotionFollowUp(string deviceId, string bundleId, bool hasSim, string simCountry, string detectedPackages, bool deviceClean, string sdkStatus, string blockReason, string deviceModel, string osVersion)
        {
            if (_motionSent)
                return;
            _motionSent = true;

            MotionCollector.Collect((motion) =>
            {
                var payload = new ReportPayloadWithMotion
                {
                    device_id = deviceId,
                    bundle_id = bundleId,
                    has_sim = hasSim,
                    sim_country = simCountry,
                    detected_packages = detectedPackages,
                    device_clean = deviceClean,
                    sdk_status = sdkStatus,
                    block_reason = blockReason,
                    device_model = deviceModel,
                    os_version = osVersion,
                    battery_level = GetBatteryLevelPercent(),
                    is_charging = GetIsCharging(),
                    motion = motion
                };

                string json = JsonUtility.ToJson(payload);
                UnityMainThreadDispatcher.Instance.StartCoroutine(SendOnce(json));
            });
        }

        /// <summary>
        /// Single POST attempt, no failover/retry across endpoints — unlike SendWithFailover.
        /// Response and errors are intentionally ignored (phase 2 is fire-and-forget).
        /// </summary>
        private static IEnumerator SendOnce(string json)
        {
            string url = AdClient.Instance.GetEndpointByIndex(0) + "/device/report";

            using (UnityWebRequest request = new UnityWebRequest(ProxyConfig.WrapUrl(url), "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                ProxyConfig.AddAuthHeader(request);
                request.timeout = 3;
                yield return request.SendWebRequest();
            }
        }

        /// <summary>
        /// Sends an early non-blocking launch heartbeat before anti-fraud checks finish.
        /// This keeps active/new user analytics alive even when a device is later blocked.
        /// </summary>
        public static void SendStartupHeartbeat()
        {
            DeviceIdentity.GetGAID((gaid) =>
            {
                var payload = new ReportPayload
                {
                    device_id = string.IsNullOrEmpty(gaid) ? DeviceIdentity.GetCachedGAID() : gaid,
                    bundle_id = AdClient.Instance.BundleId ?? "",
                    has_sim = DeviceIdentity.HasSim(),
                    sim_country = DeviceIdentity.HasSim() ? DeviceIdentity.GetSimCountry().ToUpper() : "",
                    detected_packages = "",
                    device_clean = true,
                    sdk_status = "active",
                    block_reason = "startup_heartbeat",
                    device_model = SystemInfo.deviceModel ?? "",
                    os_version = SystemInfo.operatingSystem ?? "",
                    battery_level = GetBatteryLevelPercent(),
                    is_charging = GetIsCharging()
                };

                string json = JsonUtility.ToJson(payload);
                UnityMainThreadDispatcher.Instance.StartCoroutine(
                    SendWithFailover(json, "active", "startup_heartbeat", null, 0)
                );
            });
        }

        private static IEnumerator SendWithFailover(string json, string fallbackStatus, string fallbackReason, Action<string, string> onResult, int retryCount)
        {
            string url = AdClient.Instance.GetEndpointByIndex(retryCount) + "/device/report";

            using (UnityWebRequest request = new UnityWebRequest(ProxyConfig.WrapUrl(url), "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                ProxyConfig.AddAuthHeader(request);
                request.timeout = 3;
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
