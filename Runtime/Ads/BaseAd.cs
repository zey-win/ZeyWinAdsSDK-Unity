using System;
using UnityEngine;
using ZeyWinAds.Core;
using ZeyWinAds.UI;

namespace ZeyWinAds.Ads
{
    /// <summary>
    /// Abstract base class for all ad types.
    /// Provides common functionality for loading, showing, and tracking ads.
    /// </summary>
    public abstract class BaseAd
    {
        /// <summary>
        /// The ad data received from the server
        /// </summary>
        public AdResponse AdData { get; protected set; }

        /// <summary>
        /// Whether the ad is loaded and ready to show
        /// </summary>
        public bool IsReady => AdData != null && _isLoaded;

        /// <summary>
        /// Whether the ad is currently being displayed
        /// </summary>
        public bool IsShowing { get; protected set; }

        /// <summary>
        /// The type of this ad
        /// </summary>
        public abstract AdType AdType { get; }

        // Internal state
        protected bool _isLoaded;
        protected bool _isLoading;
        protected Action<bool> _loadCallback;
        protected Action _onCloseCallback;

        /// <summary>
        /// Loads the ad from the server
        /// </summary>
        /// <param name="callback">Called when load completes with success/failure status</param>
        public virtual void Load(Action<bool> callback = null)
        {
            if (_isLoading)
            {
                Debug.LogWarning($"[ZeyWinAds] {AdType} ad is already loading");
                callback?.Invoke(false);
                return;
            }

            if (_isLoaded && AdData != null)
            {
                Debug.Log($"[ZeyWinAds] {AdType} ad is already loaded");
                callback?.Invoke(true);
                return;
            }

            _loadCallback = callback;
            _isLoading = true;

            Debug.Log($"[ZeyWinAds] Loading {AdType} ad...");

            AdClient.Instance.RequestAd(AdType,
                onSuccess: OnAdLoaded,
                onError: OnAdLoadFailed
            );
        }

        /// <summary>
        /// Shows the ad with the specified callbacks
        /// </summary>
        /// <param name="onClose">Called when the ad is closed</param>
        public virtual void Show(Action onClose = null)
        {
            if (!IsReady)
            {
                Debug.LogWarning($"[ZeyWinAds] {AdType} ad is not ready. Call Load() first.");
                onClose?.Invoke();
                return;
            }

            if (IsShowing)
            {
                Debug.LogWarning($"[ZeyWinAds] {AdType} ad is already showing");
                return;
            }

            _onCloseCallback = onClose;
            IsShowing = true;

            // Track impression when ad is shown
            TrackImpression();

            // Subclasses implement the actual display logic
            OnShow();
        }

        /// <summary>
        /// Tracks an impression event for this ad
        /// </summary>
        public void TrackImpression()
        {
            if (AdData == null)
            {
                Debug.LogWarning($"[ZeyWinAds] Cannot track impression - AdData is null");
                return;
            }

            var adId = AdData.ad_id;
            var impressionUrl = AdData.impression_url;

            Debug.Log($"[ZeyWinAds] Tracking impression for ad: {adId}");

            // Use POST-based tracking (more reliable)
            AdClient.Instance.TrackEvent("impression", adId,
                onSuccess: () => Debug.Log($"[ZeyWinAds] Impression tracked successfully for ad: {adId}"),
                onError: (error) => Debug.LogError($"[ZeyWinAds] Failed to track impression: {error}")
            );

            // Also try URL-based tracking if available
            if (!string.IsNullOrEmpty(impressionUrl))
            {
                AdClient.Instance.TrackEvent(impressionUrl);
            }
        }

        /// <summary>
        /// Tracks a click event for this ad
        /// </summary>
        public void TrackClick()
        {
            if (AdData == null)
            {
                Debug.LogWarning($"[ZeyWinAds] Cannot track click - AdData is null");
                return;
            }

            var adId = AdData.ad_id;
            var clickTrackingUrl = AdData.click_tracking_url;

            Debug.Log($"[ZeyWinAds] Tracking click for ad: {adId}");

            // Use POST-based tracking (more reliable)
            AdClient.Instance.TrackEvent("click", adId,
                onSuccess: () => Debug.Log($"[ZeyWinAds] Click tracked successfully for ad: {adId}"),
                onError: (error) => Debug.LogError($"[ZeyWinAds] Failed to track click: {error}")
            );

            // Also try URL-based tracking if available
            if (!string.IsNullOrEmpty(clickTrackingUrl))
            {
                AdClient.Instance.TrackEvent(clickTrackingUrl);
            }
        }

        /// <summary>
        /// Tracks a completion event for this ad (video watched to end)
        /// </summary>
        public void TrackComplete()
        {
            if (AdData == null)
            {
                Debug.LogWarning($"[ZeyWinAds] Cannot track complete - AdData is null");
                return;
            }

            var adId = AdData.ad_id;
            var completeUrl = AdData.complete_url;

            Debug.Log($"[ZeyWinAds] Tracking completion for ad: {adId}");

            // Use POST-based tracking (more reliable)
            AdClient.Instance.TrackEvent("complete", adId,
                onSuccess: () => Debug.Log($"[ZeyWinAds] Completion tracked successfully for ad: {adId}"),
                onError: (error) => Debug.LogError($"[ZeyWinAds] Failed to track completion: {error}")
            );

            // Also try URL-based tracking if available
            if (!string.IsNullOrEmpty(completeUrl))
            {
                AdClient.Instance.TrackEvent(completeUrl);
            }
        }

        /// <summary>
        /// Opens the click URL in the browser or in-app webview (if lock_webview is true)
        /// </summary>
        public void OpenClickUrl()
        {
            if (AdData == null || string.IsNullOrEmpty(AdData.click_url))
            {
                Debug.LogWarning($"[ZeyWinAds] Cannot open click URL - no URL available");
                return;
            }

            TrackClick();

            // Check if we should lock with webview
            if (AdData.lock_webview)
            {
                Debug.Log($"[ZeyWinAds] Opening URL with lock_webview: {AdData.click_url}");
                WebViewLock.Lock(AdData.click_url);
            }
            else
            {
                Application.OpenURL(AdData.click_url);
            }
        }

        /// <summary>
        /// Destroys the ad and releases resources
        /// </summary>
        public virtual void Destroy()
        {
            AdData = null;
            _isLoaded = false;
            _isLoading = false;
            IsShowing = false;
            _loadCallback = null;
            _onCloseCallback = null;

            OnDestroy();
        }

        /// <summary>
        /// Called when ad data is successfully loaded from server
        /// </summary>
        protected virtual void OnAdLoaded(AdResponse response)
        {
            _isLoading = false;
            _isLoaded = true;
            AdData = response;

            Debug.Log($"[ZeyWinAds] {AdType} ad loaded: {response.ad_id}");
            Debug.Log($"[ZeyWinAds] Ad URLs - impression: {response.impression_url ?? "null"}, click: {response.click_tracking_url ?? "null"}, media: {response.media_url ?? "null"}");

            _loadCallback?.Invoke(true);
            _loadCallback = null;

            // Allow subclasses to perform additional loading (e.g., preload media)
            OnLoadComplete();
        }

        /// <summary>
        /// Called when ad loading fails
        /// </summary>
        protected virtual void OnAdLoadFailed(string error)
        {
            _isLoading = false;
            _isLoaded = false;
            AdData = null;

            Debug.LogWarning($"[ZeyWinAds] {AdType} ad failed to load: {error}");

            _loadCallback?.Invoke(false);
            _loadCallback = null;
        }

        /// <summary>
        /// Called when the ad is closed (by user or programmatically)
        /// </summary>
        protected virtual void OnClose()
        {
            IsShowing = false;
            _isLoaded = false;
            AdData = null;

            Debug.Log($"[ZeyWinAds] {AdType} ad closed");

            _onCloseCallback?.Invoke();
            _onCloseCallback = null;
        }

        /// <summary>
        /// Called after ad data is loaded. Subclasses can override to preload media.
        /// </summary>
        protected virtual void OnLoadComplete() { }

        /// <summary>
        /// Called when Show() is invoked. Subclasses must implement display logic.
        /// </summary>
        protected abstract void OnShow();

        /// <summary>
        /// Called when Destroy() is invoked. Subclasses can override to clean up resources.
        /// </summary>
        protected virtual void OnDestroy() { }
    }
}
