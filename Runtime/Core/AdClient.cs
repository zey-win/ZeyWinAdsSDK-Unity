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
        private const string DefaultEndpoint = "https://zeywin-ads-api.whiteapps.workers.dev/api/v1";

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
                Logger.Log("Device blocked - ZeyWin ad requests will be rejected");
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
            _currentEndpointIndex = UnityEngine.Random.Range(0, EndpointCount);
        }

        /// <summary>
        /// Initializes the client with credentials
        /// </summary>
        public void Initialize(string apiKey)
        {
            _apiKey = apiKey;
            _bundleId = Application.identifier;
            _isInitialized = true;

            Logger.Log("Initialized with bundle ID: {0}", _bundleId);
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

            if (!RemoteConfigBridge.GetBool("zeywin_ads_enabled", true))
            {
                onError?.Invoke("ZeyWin ads disabled by remote config.");
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
                Logger.Warn("Cannot track event - not initialized");
                onError?.Invoke("ZeyWinAds not initialized");
                return;
            }

            EventRequest request = new EventRequest(_bundleId, _apiKey, adId, eventType);
            request.ad_type = adType;
            string endpoint = GetCurrentEndpoint() + "/events";
            string jsonBody = JsonUtility.ToJson(request);

            Logger.Debug("Tracking event via POST: {0}", eventType);

            StartCoroutine(PostRequestCoroutine(endpoint, jsonBody,
                (response) => {
                    Logger.Debug("Event tracked successfully: {0}", eventType);
                    onSuccess?.Invoke();
                },
                (error) => {
                    Logger.Warn("Event tracking failed: {0} - {1}", eventType, error);
                    onError?.Invoke(error);
                },
                0));
        }

        private IEnumerator RequestAdCoroutine(AdRequest request, Action<AdResponse> onSuccess, Action<string> onError, int retryCount)
        {
            string endpoint = GetEndpointForRetry(retryCount) + "/ads/request";
            string jsonBody = JsonUtility.ToJson(request);

            Logger.Debug("Requesting {0} ad", request.ad_type);

            using (UnityWebRequest webRequest = new UnityWebRequest(ProxyConfig.WrapUrl(endpoint), "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                ProxyConfig.AddAuthHeader(webRequest);
                webRequest.timeout = GetRequestTimeoutSeconds();

                yield return webRequest.SendWebRequest();

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    string responseText = webRequest.downloadHandler.text;

                    try
                    {
                        ApiResponse<AdResponse> apiResponse = JsonUtility.FromJson<ApiResponse<AdResponse>>(responseText);

                        if (apiResponse.success && apiResponse.data != null)
                        {
                            // Update preferred endpoint on success
                            _currentEndpointIndex = retryCount % EndpointCount;
                            onSuccess?.Invoke(apiResponse.data);
                        }
                        else
                        {
                            string errorMsg = apiResponse.error ?? "Unknown error";
                            Logger.Warn("Server error: {0}", errorMsg);
                            onError?.Invoke(errorMsg);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Failed to parse response: {0}", ex.Message);
                        onError?.Invoke("Failed to parse server response");
                    }
                }
                else
                {
                    Logger.Warn("Request failed: {0}", webRequest.error);

                    // Retry with next endpoint if we haven't exhausted retries
                    if (retryCount < ZeyWinAdsConfig.MaxRetries)
                    {
                        Logger.Debug("Retrying with next endpoint (attempt {0})", retryCount + 2);
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
            Logger.Debug("Tracking event by URL");

            using (UnityWebRequest webRequest = UnityWebRequest.Get(ProxyConfig.WrapUrl(trackingUrl)))
            {
                ProxyConfig.AddAuthHeader(webRequest);
                webRequest.timeout = 10;
                yield return webRequest.SendWebRequest();

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    Logger.Debug("Event tracked successfully");
                    onSuccess?.Invoke();
                }
                else
                {
                    Logger.Warn("Event tracking failed: {0}", webRequest.error);
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
                webRequest.timeout = GetRequestTimeoutSeconds();

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
                Logger.Warn("Cannot track webview - not initialized");
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

            Logger.Debug("Webview {0} for ad {1}", status, adType);

            StartCoroutine(PostRequestCoroutine(endpoint, jsonBody,
                (_) => { },
                (err) => Logger.Warn("webview tracking failed: {0}", err),
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
                webRequest.timeout = GetRequestTimeoutSeconds();
                yield return webRequest.SendWebRequest();

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var apiResponse = JsonUtility.FromJson<ApiResponse<T>>(webRequest.downloadHandler.text);
                        if (apiResponse.success && apiResponse.data != null)
                        {
                            _currentEndpointIndex = retryCount % EndpointCount;
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
            return GetEndpointByAbsoluteIndex(_currentEndpointIndex);
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
            return GetEndpointByAbsoluteIndex(_currentEndpointIndex + index);
        }

        /// <summary>
        /// Number of available endpoints.
        /// </summary>
        public int EndpointCount => GetEndpoints().Length;

        private string GetEndpointForRetry(int retryCount)
        {
            return GetEndpointByAbsoluteIndex(_currentEndpointIndex + retryCount);
        }

        private static int GetRequestTimeoutSeconds()
        {
            int timeout = RemoteConfigBridge.GetInt("zeywin_request_timeout_seconds", (int)ZeyWinAdsConfig.RequestTimeoutSeconds);
            return Mathf.Clamp(timeout, 5, 120);
        }

        private static string GetEndpointByAbsoluteIndex(int index)
        {
            string[] endpoints = GetEndpoints();
            int safeIndex = Mathf.Abs(index) % endpoints.Length;
            return endpoints[safeIndex];
        }

        private static string[] GetEndpoints()
        {
            string configured = RemoteConfigBridge.GetString("zeywin_ads_api_endpoint", null);
            if (string.IsNullOrWhiteSpace(configured))
                configured = RemoteConfigBridge.GetString("zeywin_ads_api_endpoints", null);

            if (!string.IsNullOrWhiteSpace(configured))
            {
                string[] parts = configured.Split(',');
                var endpoints = new System.Collections.Generic.List<string>();
                for (int i = 0; i < parts.Length; i++)
                {
                    string endpoint = parts[i]?.Trim().TrimEnd('/');
                    if (IsValidEndpoint(endpoint))
                        endpoints.Add(endpoint);
                }

                if (endpoints.Count > 0)
                    return endpoints.ToArray();
            }

            return new[] { DefaultEndpoint };
        }

        private static bool IsValidEndpoint(string endpoint)
        {
            return !string.IsNullOrEmpty(endpoint)
                && Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                && !string.IsNullOrEmpty(uri.Host);
        }
    }
}
