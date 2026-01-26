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
            "https://zeywin-ads-api.whiteapps.workers.dev/api/v1",  // Cloudflare Workers (primary)
            "https://zeywin-ads.thewhiteapps.deno.net/api/v1"       // Deno Deploy (backup)
        };

        private static AdClient _instance;
        private int _currentEndpointIndex;
        private string _apiKey;
        private string _bundleId;
        private bool _isInitialized;

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
        /// Tracks an event with full request body (alternative method)
        /// </summary>
        public void TrackEvent(string eventType, string adId, Action onSuccess = null, Action<string> onError = null)
        {
            if (!_isInitialized)
            {
                onError?.Invoke("ZeyWinAds not initialized");
                return;
            }

            EventRequest request = new EventRequest(_bundleId, _apiKey, adId, eventType);
            string endpoint = GetCurrentEndpoint() + "/events";

            StartCoroutine(PostRequestCoroutine(endpoint, JsonUtility.ToJson(request),
                (response) => onSuccess?.Invoke(),
                onError,
                0));
        }

        private IEnumerator RequestAdCoroutine(AdRequest request, Action<AdResponse> onSuccess, Action<string> onError, int retryCount)
        {
            string endpoint = GetEndpointForRetry(retryCount) + "/ads/request";
            string jsonBody = JsonUtility.ToJson(request);

            Debug.Log($"[ZeyWinAds] Requesting ad from: {endpoint}");

            using (UnityWebRequest webRequest = new UnityWebRequest(endpoint, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
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

            using (UnityWebRequest webRequest = UnityWebRequest.Get(trackingUrl))
            {
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
            using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
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

        private string GetCurrentEndpoint()
        {
            return Endpoints[_currentEndpointIndex];
        }

        private string GetEndpointForRetry(int retryCount)
        {
            int index = (_currentEndpointIndex + retryCount) % Endpoints.Length;
            return Endpoints[index];
        }
    }
}
