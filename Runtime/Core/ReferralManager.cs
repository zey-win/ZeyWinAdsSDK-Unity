using System;
using UnityEngine;
using ZeyWinAds.UI;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Orchestrates cross-app referral checking and offer delivery.
    /// Primary path: reads click_id from Play Install Referrer (works across signing keys).
    /// Fallback path: matches by device_id (GAID/Android ID) on the server.
    /// </summary>
    public class ReferralManager : MonoBehaviour
    {
        private static ReferralManager _instance;
        private string[] _bundleList;

        public static ReferralManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("ZeyWinAds_ReferralManager");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<ReferralManager>();
                }
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private const string ReferralShownKey = "zeywinads_referral_shown";

        /// <summary>
        /// Main entry point: checks for a pending referral and shows offer if valid.
        /// Tries Play Install Referrer first, falls back to device_id matching.
        /// </summary>
        public void CheckForReferral()
        {
            var client = AdClient.Instance;
            if (!client.IsInitialized)
            {
                Logger.Debug("Referral check skipped: SDK not initialized");
                return;
            }

            // Skip if we already showed a referral offer on this device
            if (PlayerPrefs.GetInt(ReferralShownKey, 0) == 1)
            {
                Logger.Debug("Referral check skipped: already shown");
                return;
            }

            // Step 1: Check SIM
            if (!DeviceIdentity.HasSim())
            {
                Logger.Debug("Referral check skipped: no SIM");
                return;
            }

            // Step 2: Get SIM country
            string simCountry = DeviceIdentity.GetSimCountry();
            if (string.IsNullOrEmpty(simCountry))
            {
                Logger.Debug("Referral check skipped: SIM country unavailable");
                return;
            }

            // Step 3: Try Play Install Referrer first (works across signing keys)
            TryInstallReferrer(simCountry);
        }

        /// <summary>
        /// Reads click_id from Play Install Referrer. If found, fetches referral by click_id.
        /// If not found, falls back to device_id matching.
        /// </summary>
        private void TryInstallReferrer(string simCountry)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var cls = new AndroidJavaClass("com.zeywinads.unity.ZeyWinAdsInstallReferrer"))
                {
                    cls.CallStatic("getClickId", gameObject.name, "OnInstallReferrerResult");
                }
                // Store simCountry for callback
                _pendingSimCountry = simCountry;
            }
            catch (Exception e)
            {
                Logger.Warn("Install referrer failed: {0}", e.Message);
                FallbackToDeviceIdCheck(simCountry);
            }
#else
            FallbackToDeviceIdCheck(simCountry);
#endif
        }

        private string _pendingSimCountry;

        /// <summary>
        /// Called by ZeyWinAdsInstallReferrer via UnitySendMessage.
        /// </summary>
        public void OnInstallReferrerResult(string clickId)
        {
            string simCountry = _pendingSimCountry;
            _pendingSimCountry = null;

            if (!string.IsNullOrEmpty(clickId))
            {
                Logger.Debug("Install referrer found click_id");
                CheckReferralByClickId(clickId, simCountry);
            }
            else
            {
                Logger.Debug("No click_id in install referrer, falling back to device_id");
                FallbackToDeviceIdCheck(simCountry);
            }
        }

        /// <summary>
        /// Primary path: fetch referral by click_id (no device_id matching needed).
        /// </summary>
        private void CheckReferralByClickId(string clickId, string simCountry)
        {
            var client = AdClient.Instance;

            client.CheckReferralByClickId(clickId, simCountry,
                onSuccess: (response) =>
                {
                    if (!response.has_referral || string.IsNullOrEmpty(response.offer_url))
                    {
                        Logger.Debug("No pending referral for click_id, falling back to device_id");
                        FallbackToDeviceIdCheck(simCountry);
                        return;
                    }

                    // Show locked webview with offer
                    Logger.Log("Showing referral offer via install referrer");
                    WebViewLock.Lock(response.offer_url);
                    PlayerPrefs.SetInt(ReferralShownKey, 1);
                    PlayerPrefs.Save();

                    // Mark as delivered
                    DeviceIdentity.GetGAID((gaid) =>
                    {
                        string deviceId = string.IsNullOrEmpty(gaid) ? DeviceIdentity.GetCachedGAID() : gaid;
                        var deliveredRequest = new ReferralDeliveredRequest
                        {
                            api_key = client.ApiKey,
                            bundle_id = client.BundleId,
                            click_id = response.click_id,
                            device_id = deviceId
                        };

                        client.MarkReferralDelivered(deliveredRequest,
                            onSuccess: () => Logger.Debug("Referral marked as delivered"),
                            onError: (error) => Logger.Warn("Failed to mark referral delivered: {0}", error)
                        );
                    });
                },
                onError: (error) =>
                {
                    Logger.Warn("Referral check by click_id failed: {0}", error);
                    FallbackToDeviceIdCheck(simCountry);
                }
            );
        }

        /// <summary>
        /// Fallback path: match by device_id (GAID/Android ID) — original logic.
        /// </summary>
        private void FallbackToDeviceIdCheck(string simCountry)
        {
            var client = AdClient.Instance;

            DeviceIdentity.GetGAID((gaid) =>
            {
                // Use GAID if available, otherwise fallback to persistent device ID
                // (same logic as DeviceReport and click registration)
                string deviceId = string.IsNullOrEmpty(gaid) ? DeviceIdentity.GetCachedGAID() : gaid;

                var request = new ReferralCheckRequest
                {
                    api_key = client.ApiKey,
                    bundle_id = client.BundleId,
                    device_id = deviceId,
                    sim_country = simCountry
                };

                client.CheckReferral(request,
                    onSuccess: (response) => OnReferralCheckResult(response, deviceId),
                    onError: (error) => Logger.Warn("Referral check failed: {0}", error)
                );
            });
        }

        private void OnReferralCheckResult(ReferralCheckResponse response, string gaid)
        {
            if (!response.has_referral || string.IsNullOrEmpty(response.offer_url))
            {
                Logger.Debug("No pending referral found");
                return;
            }

            // Show locked webview with offer
            Logger.Log("Showing referral offer");
            WebViewLock.Lock(response.offer_url);
            PlayerPrefs.SetInt(ReferralShownKey, 1);
            PlayerPrefs.Save();

            // Mark as delivered
            var client = AdClient.Instance;
            var deliveredRequest = new ReferralDeliveredRequest
            {
                api_key = client.ApiKey,
                bundle_id = client.BundleId,
                click_id = response.click_id,
                device_id = gaid
            };

            client.MarkReferralDelivered(deliveredRequest,
                onSuccess: () => Logger.Debug("Referral marked as delivered"),
                onError: (error) => Logger.Warn("Failed to mark referral delivered: {0}", error)
            );
        }

        /// <summary>
        /// Fetches the list of active bundle IDs (fire & forget).
        /// Can be used to update AndroidManifest queries.
        /// </summary>
        public void FetchBundleList()
        {
            AdClient.Instance.GetBundleList(
                onSuccess: (response) =>
                {
                    _bundleList = response.bundles;
                    Logger.Debug("Fetched {0} active bundles", _bundleList?.Length ?? 0);
                },
                onError: (error) => Logger.Warn("Failed to fetch bundle list: {0}", error)
            );
        }

        /// <summary>
        /// Returns the cached bundle list, or null if not fetched yet.
        /// </summary>
        public string[] GetBundleList()
        {
            return _bundleList;
        }
    }
}
