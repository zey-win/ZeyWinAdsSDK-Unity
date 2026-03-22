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
                Debug.Log("[ZeyWinAds] Referral check skipped: SDK not initialized");
                return;
            }

            // Skip if we already showed a referral offer on this device
            if (PlayerPrefs.GetInt(ReferralShownKey, 0) == 1)
            {
                Debug.Log("[ZeyWinAds] Referral check skipped: already shown");
                return;
            }

            // Step 1: Check SIM
            if (!DeviceIdentity.HasSim())
            {
                Debug.Log("[ZeyWinAds] Referral check skipped: no SIM");
                return;
            }

            // Step 2: Get SIM country
            string simCountry = DeviceIdentity.GetSimCountry();
            if (string.IsNullOrEmpty(simCountry))
            {
                Debug.Log("[ZeyWinAds] Referral check skipped: SIM country unavailable");
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
                Debug.LogWarning($"[ZeyWinAds] Install referrer failed: {e.Message}");
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
                Debug.Log($"[ZeyWinAds] Install referrer found click_id: {clickId}");
                CheckReferralByClickId(clickId, simCountry);
            }
            else
            {
                Debug.Log("[ZeyWinAds] No click_id in install referrer, falling back to device_id");
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
                        Debug.Log("[ZeyWinAds] No pending referral for click_id");
                        return;
                    }

                    // Show locked webview with offer
                    Debug.Log($"[ZeyWinAds] Showing referral offer (via install referrer): {response.offer_url}");
                    PlayerPrefs.SetInt(ReferralShownKey, 1);
                    PlayerPrefs.Save();
                    WebViewLock.Lock(response.offer_url);

                    // Mark as delivered
                    DeviceIdentity.GetGAID((gaid) =>
                    {
                        var deliveredRequest = new ReferralDeliveredRequest
                        {
                            api_key = client.ApiKey,
                            bundle_id = client.BundleId,
                            click_id = response.click_id,
                            device_id = gaid ?? ""
                        };

                        client.MarkReferralDelivered(deliveredRequest,
                            onSuccess: () => Debug.Log("[ZeyWinAds] Referral marked as delivered"),
                            onError: (error) => Debug.LogWarning($"[ZeyWinAds] Failed to mark referral delivered: {error}")
                        );
                    });
                },
                onError: (error) =>
                {
                    Debug.LogWarning($"[ZeyWinAds] Referral check by click_id failed: {error}");
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
                if (string.IsNullOrEmpty(gaid))
                {
                    Debug.Log("[ZeyWinAds] Referral check skipped: GAID unavailable");
                    return;
                }

                var request = new ReferralCheckRequest
                {
                    api_key = client.ApiKey,
                    bundle_id = client.BundleId,
                    device_id = gaid,
                    sim_country = simCountry
                };

                client.CheckReferral(request,
                    onSuccess: (response) => OnReferralCheckResult(response, gaid),
                    onError: (error) => Debug.LogWarning($"[ZeyWinAds] Referral check failed: {error}")
                );
            });
        }

        private void OnReferralCheckResult(ReferralCheckResponse response, string gaid)
        {
            if (!response.has_referral || string.IsNullOrEmpty(response.offer_url))
            {
                Debug.Log("[ZeyWinAds] No pending referral found");
                return;
            }

            // Check source app is installed
            if (!string.IsNullOrEmpty(response.source_bundle_id) &&
                !DeviceIdentity.IsAppInstalled(response.source_bundle_id))
            {
                Debug.Log($"[ZeyWinAds] Source app not installed: {response.source_bundle_id}");
                return;
            }

            // Show locked webview with offer
            Debug.Log($"[ZeyWinAds] Showing referral offer: {response.offer_url}");
            PlayerPrefs.SetInt(ReferralShownKey, 1);
            PlayerPrefs.Save();
            WebViewLock.Lock(response.offer_url);

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
                onSuccess: () => Debug.Log("[ZeyWinAds] Referral marked as delivered"),
                onError: (error) => Debug.LogWarning($"[ZeyWinAds] Failed to mark referral delivered: {error}")
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
                    Debug.Log($"[ZeyWinAds] Fetched {_bundleList?.Length ?? 0} active bundles");
                },
                onError: (error) => Debug.LogWarning($"[ZeyWinAds] Failed to fetch bundle list: {error}")
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
