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
        private Action<bool> _pendingReferralCompletion;
        private bool _referralCompletionInvoked;
        private bool _deviceIdCheckStarted;
        private int _deviceIdRequestsInFlight;

        public void CheckForReferral()
        {
            CheckForReferral(null);
        }

        public void CheckForReferral(Action<bool> onCompleted)
        {
            _pendingReferralCompletion = onCompleted;
            _referralCompletionInvoked = false;
            _deviceIdCheckStarted = false;
            _deviceIdRequestsInFlight = 0;

            var client = AdClient.Instance;
            if (!client.IsInitialized)
            {
                Logger.Log("[ReferralDiag] skipped: SDK not initialized");
                CompleteReferralCheck(false);
                return;
            }

            // Skip if we already showed a referral offer on this device
            if (PlayerPrefs.GetInt(ReferralShownKey, 0) == 1)
            {
                Logger.Log("[ReferralDiag] skipped: already shown (ReferralShownKey=1)");
                CompleteReferralCheck(false);
                return;
            }


            string simCountry = DeviceIdentity.GetSimCountry();
            Logger.Log("[ReferralDiag] start: bundle='{0}' simCountry='{1}'", client.BundleId, simCountry ?? "");

#if UNITY_ANDROID && !UNITY_EDITOR
            // Step 1: Check SIM
            if (!DeviceIdentity.HasSim())
            {
                Logger.Debug("Referral check skipped: no SIM");
                CompleteReferralCheck(false);
                return;
            }

            // Step 2: Get SIM country
            
            if (string.IsNullOrEmpty(simCountry))
            {
                Logger.Debug("Referral check skipped: SIM country unavailable");
                CompleteReferralCheck(false);
                return;
            } 
#endif
            // Step 3: Try Play Install Referrer first (works across signing keys)
            TryInstallReferrer(simCountry);
        }

        private void CompleteReferralCheck(bool lockedWebView)
        {
            if (_referralCompletionInvoked)
                return;

            _referralCompletionInvoked = true;
            var callback = _pendingReferralCompletion;
            _pendingReferralCompletion = null;
            callback?.Invoke(lockedWebView);
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
                // Device-id referral matching is a valid fallback and is faster on
                // some devices than waiting for Play Install Referrer to finish.
                FallbackToDeviceIdCheck(simCountry);
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
            if (_referralCompletionInvoked)
                return;

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
                    if (_referralCompletionInvoked)
                        return;

                    if (!response.has_referral || string.IsNullOrEmpty(response.offer_url))
                    {
                        Logger.Debug("No pending referral for click_id, falling back to device_id");
                        FallbackToDeviceIdCheck(simCountry);
                        return;
                    }

                    // Show locked webview with offer
                    Logger.Log("Showing referral offer via install referrer");
                    WebViewLock.Lock(response.offer_url);
                    CompleteReferralCheck(true);
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
            if (_deviceIdCheckStarted)
                return;

            _deviceIdCheckStarted = true;
            string fastDeviceId = DeviceIdentity.GetFastDeviceId();
            Logger.Log("[ReferralDiag] fallback device-id check: fastDeviceId {0}", DiagId(fastDeviceId));
            if (!string.IsNullOrEmpty(fastDeviceId))
                CheckReferralWithDeviceId(fastDeviceId, simCountry);

            DeviceIdentity.GetGAID((gaid) =>
            {
                if (_referralCompletionInvoked)
                    return;

                string gaidDeviceId = string.IsNullOrEmpty(gaid) ? DeviceIdentity.GetCachedGAID() : gaid;
                Logger.Log("[ReferralDiag] gaid path: gaid {0} cached/fallback {1}", DiagId(gaid), DiagId(gaidDeviceId));
                if (!string.IsNullOrEmpty(gaidDeviceId) && gaidDeviceId != fastDeviceId)
                    CheckReferralWithDeviceId(gaidDeviceId, simCountry);
                else if (_deviceIdRequestsInFlight == 0)
                {
                    Logger.Log("[ReferralDiag] no distinct gaid device id — completing with no offer");
                    CompleteReferralCheck(false);
                }
            });
        }

        private void CheckReferralWithDeviceId(string deviceId, string simCountry)
        {
            if (_referralCompletionInvoked || string.IsNullOrEmpty(deviceId))
                return;

            var client = AdClient.Instance;
            var request = new ReferralCheckRequest
            {
                api_key = client.ApiKey,
                bundle_id = client.BundleId,
                device_id = deviceId,
                sim_country = simCountry
            };

            _deviceIdRequestsInFlight++;
            Logger.Log("[ReferralDiag] -> CheckReferral request: bundle='{0}' device_id {1} sim_country='{2}'",
                request.bundle_id, DiagId(deviceId), request.sim_country ?? "");
            client.CheckReferral(request,
                onSuccess: (response) =>
                {
                    _deviceIdRequestsInFlight = Math.Max(0, _deviceIdRequestsInFlight - 1);
                    Logger.Log("[ReferralDiag] <- CheckReferral response: has_referral={0} offer_url_present={1} click_id='{2}' source='{3}'",
                        response != null && response.has_referral,
                        response != null && !string.IsNullOrEmpty(response.offer_url),
                        response != null ? (response.click_id ?? "") : "",
                        response != null ? (response.source_bundle_id ?? "") : "");
                    OnReferralCheckResult(response, deviceId);
                    if (!_referralCompletionInvoked && _deviceIdRequestsInFlight == 0)
                        CompleteReferralCheck(false);
                },
                onError: (error) =>
                {
                    _deviceIdRequestsInFlight = Math.Max(0, _deviceIdRequestsInFlight - 1);
                    Logger.Warn("[ReferralDiag] CheckReferral failed: {0}", error);
                    if (!_referralCompletionInvoked && _deviceIdRequestsInFlight == 0)
                        CompleteReferralCheck(false);
                }
            );
        }

        private void OnReferralCheckResult(ReferralCheckResponse response, string gaid)
        {
            if (_referralCompletionInvoked)
                return;

            if (response == null || !response.has_referral || string.IsNullOrEmpty(response.offer_url))
            {
                Logger.Log("[ReferralDiag] no pending referral for this device — no offer will be shown");
                return;
            }

            // Show locked webview with offer
            Logger.Log("Showing referral offer");
            WebViewLock.Lock(response.offer_url);
            CompleteReferralCheck(true);
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

        // Temporary [ReferralDiag] helper: shows enough of an id to tell values
        // apart across runs / platforms without logging the raw identifier.
        private static string DiagId(string id)
        {
            if (string.IsNullOrEmpty(id))
                return "(empty)";
            return string.Format("(len={0} '{1}...')", id.Length, id.Substring(0, Math.Min(8, id.Length)));
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
