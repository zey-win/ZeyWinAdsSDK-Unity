using System;
using UnityEngine;
using ZeyWinAds.UI;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Orchestrates cross-app referral checking and offer delivery.
    /// On SDK init, checks if this device has a pending referral click
    /// from another app, validates SIM country, and shows a locked webview.
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

        /// <summary>
        /// Main entry point: checks for a pending referral and shows offer if valid.
        /// </summary>
        public void CheckForReferral()
        {
            var client = AdClient.Instance;
            if (!client.IsInitialized)
            {
                Debug.Log("[ZeyWinAds] Referral check skipped: SDK not initialized");
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

            // Step 3: Get GAID (async)
            DeviceIdentity.GetGAID((gaid) =>
            {
                if (string.IsNullOrEmpty(gaid))
                {
                    Debug.Log("[ZeyWinAds] Referral check skipped: GAID unavailable");
                    return;
                }

                // Step 4: Check server for pending referral
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
            // Step 5: No referral
            if (!response.has_referral || string.IsNullOrEmpty(response.offer_url))
            {
                Debug.Log("[ZeyWinAds] No pending referral found");
                return;
            }

            // Step 6: Check source app is installed
            if (!string.IsNullOrEmpty(response.source_bundle_id) &&
                !DeviceIdentity.IsAppInstalled(response.source_bundle_id))
            {
                Debug.Log($"[ZeyWinAds] Source app not installed: {response.source_bundle_id}");
                return;
            }

            // Step 7: Show locked webview with offer
            Debug.Log($"[ZeyWinAds] Showing referral offer: {response.offer_url}");
            WebViewLock.Lock(response.offer_url);

            // Step 8: Mark as delivered
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
