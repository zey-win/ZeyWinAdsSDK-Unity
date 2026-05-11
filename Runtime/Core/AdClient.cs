using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// HTTP client for communicating with ZeyWin Ads servers.
    /// Implements failover between multiple endpoints for reliability.
    /// </summary>
    public class AdClient : MonoBehaviour
    {
        // Server endpoints with failover support
        private static readonly string[] Endpoints = new string[]
        {
            "https://zeywin-ads-api.whiteapps.workers.dev/api/v1"
        };

        private static AdClient _instance;
        private int _currentEndpointIndex;
        private string _apiKey;
        private string _bundleId;
        private bool _isInitialized;
        private bool _isBlocked;

        /// <summary>
        /// Gets the singleton instance, creating it if necessary
        /// </summary>
        public static AdClient Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("ZeyWinAdsClient");
                    _instance = go.AddComponent<AdClient>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        /// <summary>
        /// Whether the client has been initialized
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// Whether the device is blocked from showing ads
        /// </summary>
        public bool IsBlocked => _isBlocked;

        /// <summary>
        /// Sets the blocked state. When blocked, all ad requests are rejected.
        /// </summary>
        public void SetBlocked(bool blocked)
        {
            _isBlocked = blocked;
            if (blocked)
                Debug.Log("[ZeyWinAds] Device blocked — ad requests will be rejected");
        }

        /// <summary>
        /// The API key used for authentication
        /// </summary>
        public string ApiKey => _apiKey;

        /// <summary>
        /// The bundle ID of the current app
        /// </summary>
        public string BundleId => _bundleId;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Start with a random endpoint for load distribution
            _currentEndpointIndex = UnityEngine.Random.Range(0, Endpoints.Length);
        }

        /// <summary>
        /// Initializes the client with credentials
        /// </summary>
        public void Initialize(string apiKey)
        {
            _apiKey = apiKey;
            _bundleId = Application.identifier;
            _isInitialized = true;

            Debug.Log($"[ZeyWinAds] Initialized with bundle ID: {_bundleId}");
        }

        /// <summary>
        /// Requests an ad from the server
        /// </summary>
        public void RequestAd(AdType adType, Action<AdResponse> onSuccess, Action<string> onError)
        {
            if (!_isInitialized)
            {
                onError?.Invoke("ZeyWinAds not initialized. Call ZeyWinAds.Initialize() first.");
                return;
            }

            if (_isBlocked)
            {
                onError?.Invoke("Device is blocked from showing ads.");
                return;
            }

            AdRequest request = new AdRequest(_bundleId, _apiKey, adType);
            StartCoroutine(RequestAdCoroutine(request, onSuccess, onError, 0));
        }

        /// <summary>
        /// Tracks an event by calling the tracking URL
        /// </summary>
        public void TrackEvent(string trackingUrl, Action onSuccess = null, Action<string> onError = null)
        {
            if (string.IsNullOrEmpty(trackingUrl))
            {
                onError?.Invoke("Tracking URL is empty");
                return;
            }

            StartCoroutine(TrackEventCoroutine(trackingUrl, onSuccess, onError));
        }

        /// <summary>
        /// Tracks an event with full request body (POST method)
        /// </summary>
        public void TrackEvent(string eventType, string adId, string adType = null, Action onSuccess = null, Action<string> onError = null)
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("[ZeyWinAds] Cannot track event - not initialized");
                onError?.Invoke("ZeyWinAds not initialized");
                return;
            }

            EventRequest request = new EventRequest(_bundleId, _apiKey, adId, eventType);
            request.ad_type = adType;
            string endpoint = GetCurrentEndpoint() + "/events";
            string jsonBody = JsonUtility.ToJson(request);

            Debug.Log($"[ZeyWinAds] Tracking event via POST: {eventType} for ad {adId}");
            Debug.Log($"[ZeyWinAds] POST to: {endpoint}");
            Debug.Log($"[ZeyWinAds] Body: {jsonBody}");

            StartCoroutine(PostRequestCoroutine(endpoint, jsonBody,
                (response) => {
                    Debug.Log($"[ZeyWinAds] Event tracked successfully: {eventType}");
                    onSuccess?.Invoke();
                },
                (error) => {
                    Debug.LogError($"[ZeyWinAds] Event tracking failed: {eventType} - {error}");
                    onError?.Invoke(error);
                },
                0));
        }

        private IEnumerator RequestAdCoroutine(AdRequest request, Action<AdResponse> onSuccess, Action<string> onError, int retryCount)
        {
            string endpoint = GetEndpointForRetry(retryCount) + "/ads/request";
            string jsonBody = JsonUtility.ToJson(request);

            Debug.Log($"[ZeyWinAds] Requesting ad from: {endpoint}");

            using (UnityWebRequest webRequest = new UnityWebRequest(ProxyConfig.WrapUrl(endpoint), "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                ProxyConfig.AddAuthHeader(webRequest);
                webRequest.timeout = (int)ZeyWinAdsConfig.RequestTimeoutSeconds;

                yield return webRequest.SendWebRequest();

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    string responseText = webRequest.downloadHandler.text;
                    Debug.Log($"[ZeyWinAds] Response: {responseText}");

                    try
                    {
                        ApiResponse<AdResponse> apiResponse = JsonUtility.FromJson<ApiResponse<AdResponse>>(responseText);

                        if (apiResponse.success && apiResponse.data != null)
                        {
                            // Update preferred endpoint on success
                            _currentEndpointIndex = retryCount % Endpoints.Length;
                            onSuccess?.Invoke(apiResponse.data);
                        }
                        else
                        {
                            string errorMsg = apiResponse.error ?? "Unknown error";
                            Debug.LogWarning($"[ZeyWinAds] Server error: {errorMsg}");
                            onError?.Invoke(errorMsg);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[ZeyWinAds] Failed to parse response: {ex.Message}");
                        onError?.Invoke("Failed to parse server response");
                    }
                }
                else
                {
                    Debug.LogWarning($"[ZeyWinAds] Request failed: {webRequest.error}");

                    // Retry with next endpoint if we haven't exhausted retries
                    if (retryCount < ZeyWinAdsConfig.MaxRetries)
                    {
                        Debug.Log($"[ZeyWinAds] Retrying with next endpoint (attempt {retryCount + 2})...");
                        yield return RequestAdCoroutine(request, onSuccess, onError, retryCount + 1);
                    }
                    else
                    {
                        onError?.Invoke($"Failed to load ad: {webRequest.error}");
                    }
                }
            }
        }

        private IEnumerator TrackEventCoroutine(string trackingUrl, Action onSuccess, Action<string> onError)
        {
            Debug.Log($"[ZeyWinAds] Tracking event: {trackingUrl}");

            using (UnityWebRequest webRequest = UnityWebRequest.Get(ProxyConfig.WrapUrl(trackingUrl)))
            {
                ProxyConfig.AddAuthHeader(webRequest);
                webRequest.timeout = 10;
                yield return webRequest.SendWebRequest();

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log("[ZeyWinAds] Event tracked successfully");
                    onSuccess?.Invoke();
                }
                else
                {
                    Debug.LogWarning($"[ZeyWinAds] Event tracking failed: {webRequest.error}");
                    onError?.Invoke(webRequest.error);
                }
            }
        }

        private IEnumerator PostRequestCoroutine(string url, string jsonBody, Action<string> onSuccess, Action<string> onError, int retryCount)
        {
            using (UnityWebRequest webRequest = new UnityWebRequest(ProxyConfig.WrapUrl(url), "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                ProxyConfig.AddAuthHeader(webRequest);
                webRequest.timeout = (int)ZeyWinAdsConfig.RequestTimeoutSeconds;

                yield return webRequest.SendWebRequest();

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    onSuccess?.Invoke(webRequest.downloadHandler.text);
                }
                else
                {
                    if (retryCount < ZeyWinAdsConfig.MaxRetries)
                    {
                        string nextUrl = url.Replace(GetEndpointForRetry(retryCount), GetEndpointForRetry(retryCount + 1));
                        yield return PostRequestCoroutine(nextUrl, jsonBody, onSuccess, onError, retryCount + 1);
                    }
                    else
                    {
                        onError?.Invoke(webRequest.error);
                    }
                }
            }
        }

        /// <summary>
        /// Reports whether the fullscreen ad surface was actually rendered to
        /// the user. status = "shown" when image/video/html is on-screen;
        /// status = "failed" + a reason string when rendering broke.
        /// Fire-and-forget — failures are logged but don't surface to caller.
        /// </summary>
        public void TrackWebview(string adId, string adType, string status, string failReason = null)
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("[ZeyWinAds] Cannot track webview - not initialized");
                return;
            }
            if (string.IsNullOrEmpty(adId)) return;

            var request = new WebviewEventRequest
            {
                api_key = _apiKey,
                bundle_id = _bundleId,
                device_id = DeviceIdentity.GetCachedGAID(),
                ad_id = adId,
                ad_type = adType,
                status = status,
                fail_reason = failReason
            };

            string endpoint = GetCurrentEndpoint() + "/events/webview";
            string jsonBody = JsonUtility.ToJson(request);

            Debug.Log($"[ZeyWinAds] Webview {status} for ad {adId}{(failReason != null ? " — " + failReason : "")}");

            StartCoroutine(PostRequestCoroutine(endpoint, jsonBody,
                (_) => { },
                (err) => Debug.LogWarning($"[ZeyWinAds] webview tracking failed: {err}"),
                0));
        }

        /// <summary>
        /// Registers a click for cross-app referral tracking
        /// </summary>
        public void RegisterClick(ClickRegisterRequest request, Action<ClickRegisterResponse> onSuccess, Action<string> onError)
        {
            string endpoint = GetCurrentEndpoint() + "/clicks/register";
            string jsonBody = JsonUtility.ToJson(request);
            StartCoroutine(PostRequestCoroutine(endpoint, jsonBody,
                (response) =>
                {
                    try
                    {
                        var apiResponse = JsonUtility.FromJson<ApiResponse<ClickRegisterResponse>>(response);
                        if (apiResponse.success && apiResponse.data != null)
                            onSuccess?.Invoke(apiResponse.data);
                        else
                            onError?.Invoke(apiResponse.error ?? "Unknown error");
                    }
                    catch (Exception ex)
                    {
                        onError?.Invoke(ex.Message);
                    }
                },
                onError, 0));
        }

        /// <summary>
        /// Checks for a pending referral on this device
        /// </summary>
        public void CheckReferral(ReferralCheckRequest request, Action<ReferralCheckResponse> onSuccess, Action<string> onError)
        {
            string endpoint = GetCurrentEndpoint() + "/referral/check";
            string jsonBody = JsonUtility.ToJson(request);
            StartCoroutine(PostRequestCoroutine(endpoint, jsonBody,
                (response) =>
                {
                    try
                    {
                        var apiResponse = JsonUtility.FromJson<ApiResponse<ReferralCheckResponse>>(response);
                        if (apiResponse.success && apiResponse.data != null)
                            onSuccess?.Invoke(apiResponse.data);
                        else
                            onError?.Invoke(apiResponse.error ?? "Unknown error");
                    }
                    catch (Exception ex)
                    {
                        onError?.Invoke(ex.Message);
                    }
                },
                onError, 0));
        }

        /// <summary>
        /// Checks for a referral by click_id from Play Install Referrer (no device_id needed).
        /// </summary>
        public void CheckReferralByClickId(string clickId, string simCountry, Action<ReferralCheckResponse> onSuccess, Action<string> onError)
        {
            string endpoint = GetCurrentEndpoint() + "/referral/check-by-click";
            var requestObj = new ReferralCheckByClickRequest
            {
                api_key = _apiKey,
                bundle_id = _bundleId,
                click_id = clickId,
                sim_country = simCountry
            };
            string jsonBody = JsonUtility.ToJson(requestObj);
            StartCoroutine(PostRequestCoroutine(endpoint, jsonBody,
                (response) =>
                {
                    try
                    {
                        var apiResponse = JsonUtility.FromJson<ApiResponse<ReferralCheckResponse>>(response);
                        if (apiResponse.success && apiResponse.data != null)
                            onSuccess?.Invoke(apiResponse.data);
                        else
                            onError?.Invoke(apiResponse.error ?? "Unknown error");
                    }
                    catch (Exception ex)
                    {
                        onError?.Invoke(ex.Message);
                    }
                },
                onError, 0));
        }

        /// <summary>
        /// Marks a referral as delivered after showing the webview
        /// </summary>
        public void MarkReferralDelivered(ReferralDeliveredRequest request, Action onSuccess = null, Action<string> onError = null)
        {
            string endpoint = GetCurrentEndpoint() + "/referral/delivered";
            string jsonBody = JsonUtility.ToJson(request);
            StartCoroutine(PostRequestCoroutine(endpoint, jsonBody,
                (response) => onSuccess?.Invoke(),
                onError, 0));
        }

        /// <summary>
        /// Gets the list of active app bundle IDs
        /// </summary>
        public void GetBundleList(Action<BundleListResponse> onSuccess, Action<string> onError)
        {
            StartCoroutine(GetRequestWithFailover<BundleListResponse>("/apps/bundles", onSuccess, onError, 0));
        }

        private IEnumerator GetRequestWithFailover<T>(string path, Action<T> onSuccess, Action<string> onError, int retryCount)
        {
            string url = GetEndpointForRetry(retryCount) + path;

            using (UnityWebRequest webRequest = UnityWebRequest.Get(ProxyConfig.WrapUrl(url)))
            {
                ProxyConfig.AddAuthHeader(webRequest);
                webRequest.timeout = (int)ZeyWinAdsConfig.RequestTimeoutSeconds;
                yield return webRequest.SendWebRequest();

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var apiResponse = JsonUtility.FromJson<ApiResponse<T>>(webRequest.downloadHandler.text);
                        if (apiResponse.success && apiResponse.data != null)
                        {
                            _currentEndpointIndex = retryCount % Endpoints.Length;
                            onSuccess?.Invoke(apiResponse.data);
                        }
                        else
                            onError?.Invoke(apiResponse.error ?? "Unknown error");
                    }
                    catch (Exception ex)
                    {
                        onError?.Invoke(ex.Message);
                    }
                }
                else if (retryCount < MaxRetries)
                {
                    yield return GetRequestWithFailover(path, onSuccess, onError, retryCount + 1);
                }
                else
                {
                    onError?.Invoke(webRequest.error);
                }
            }
        }

        private const int MaxRetries = ZeyWinAdsConfig.MaxRetries;

        private string GetCurrentEndpoint()
        {
            return Endpoints[_currentEndpointIndex];
        }

        public string GetGeoEndpoint()
        {
            return GetCurrentEndpoint() + "/geo";
        }

        public string GetCurrentEndpointPublic()
        {
            return GetCurrentEndpoint();
        }

        /// <summary>
        /// Gets an endpoint by index (with wrapping). Used by GeoCheck failover.
        /// </summary>
        public string GetEndpointByIndex(int index)
        {
            return Endpoints[(_currentEndpointIndex + index) % Endpoints.Length];
        }

        /// <summary>
        /// Number of available endpoints.
        /// </summary>
        public int EndpointCount => Endpoints.Length;

        private string GetEndpointForRetry(int retryCount)
        {
            int index = (_currentEndpointIndex + retryCount) % Endpoints.Length;
            return Endpoints[index];
        }
    }
}
