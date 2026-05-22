using System;
using UnityEngine;
using ZeyWinAds.Core;
using ZeyWinAds.UI;
using Logger = ZeyWinAds.Core.Logger;

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
                Logger.Warn("{0} ad is already loading", AdType);
                callback?.Invoke(false);
                return;
            }

            if (_isLoaded && AdData != null)
            {
                Logger.Debug("{0} ad is already loaded", AdType);
                callback?.Invoke(true);
                return;
            }

            _loadCallback = callback;
            _isLoading = true;

            Logger.Log("Loading {0} ad...", AdType);

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
                Logger.Warn("{0} ad is not ready. Call Load() first.", AdType);
                onClose?.Invoke();
                return;
            }

            if (IsShowing)
            {
                Logger.Warn("{0} ad is already showing", AdType);
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
                Logger.Warn("Cannot track impression - AdData is null");
                return;
            }

            var adId = AdData.ad_id;
            var impressionUrl = AdData.impression_url;

            Logger.Debug("Tracking impression");

            // Use POST-based tracking (more reliable)
            AdClient.Instance.TrackEvent("impression", adId, AdData.ad_type,
                onSuccess: () => Logger.Debug("Impression tracked successfully"),
                onError: (error) => Logger.Error("Failed to track impression: {0}", error)
            );

            // Also try URL-based tracking if available
            if (!string.IsNullOrEmpty(impressionUrl))
            {
                AdClient.Instance.TrackEvent(impressionUrl);
            }
        }

        /// <summary>
        /// Reports that the fullscreen ad surface successfully rendered to the
        /// user (image loaded / video prepared / HTML page finished). Stronger
        /// signal than TrackImpression — fired only when pixels are on-screen.
        /// </summary>
        protected void TrackWebviewShown()
        {
            if (AdData == null) return;
            global::ZeyWinAds.ZeyWinAds.HandleFullscreenSurfaceShown(AdType);
            AdClient.Instance.TrackWebview(AdData.ad_id, AdData.ad_type, "shown");
        }

        /// <summary>
        /// Reports that rendering the fullscreen ad failed before the user
        /// could see it. Reason codes: image_load_error, video_load_error,
        /// html_load_error, media_missing, render_exception.
        /// </summary>
        protected void TrackWebviewFailed(string reason)
        {
            if (AdData == null) return;
            AdClient.Instance.TrackWebview(AdData.ad_id, AdData.ad_type, "failed", reason);
        }

        /// <summary>
        /// Tracks a click event for this ad
        /// </summary>
        public void TrackClick()
        {
            if (AdData == null)
            {
                Logger.Warn("Cannot track click - AdData is null");
                return;
            }

            var adId = AdData.ad_id;
            var clickTrackingUrl = AdData.click_tracking_url;

            Logger.Debug("Tracking click");

            // Use POST-based tracking (more reliable)
            AdClient.Instance.TrackEvent("click", adId, AdData.ad_type,
                onSuccess: () => Logger.Debug("Click tracked successfully"),
                onError: (error) => Logger.Error("Failed to track click: {0}", error)
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
                Logger.Warn("Cannot track complete - AdData is null");
                return;
            }

            var adId = AdData.ad_id;
            var completeUrl = AdData.complete_url;

            Logger.Debug("Tracking completion");

            // Use POST-based tracking (more reliable)
            AdClient.Instance.TrackEvent("complete", adId, AdData.ad_type,
                onSuccess: () => Logger.Debug("Completion tracked successfully"),
                onError: (error) => Logger.Error("Failed to track completion: {0}", error)
            );

            // Also try URL-based tracking if available
            if (!string.IsNullOrEmpty(completeUrl))
            {
                AdClient.Instance.TrackEvent(completeUrl);
            }
        }

        /// <summary>
        /// Opens the click URL in the browser or in-app webview (if lock_webview is true).
        /// For Play Store URLs, registers the click for cross-app referral before opening.
        /// </summary>
        public void OpenClickUrl()
        {
            if (AdData == null)
            {
                Logger.Warn("Cannot open click URL - AdData is null");
                return;
            }

            TrackClick();

            // Cross-app referral: store_url = Play Store link, click_url = offer for target app
            if (!string.IsNullOrEmpty(AdData.store_url))
            {
                string targetBundleId = UrlHelper.ExtractBundleIdFromPlayStoreUrl(AdData.store_url);
                if (!string.IsNullOrEmpty(targetBundleId))
                {
                    // Register click, get click_id, append as referrer, then open Play Store
                    RegisterReferralClickAndOpen(AdData.ad_id, AdData.click_url, targetBundleId, AdData.store_url);
                }
                else
                {
                    Application.OpenURL(AdData.store_url);
                }
            }
            else if (!string.IsNullOrEmpty(AdData.click_url))
            {
                if (AdData.lock_webview)
                {
                    Logger.Log("Opening click URL with lock_webview");
                    WebViewLock.Lock(AdData.click_url);
                }
                else
                {
                    Application.OpenURL(AdData.click_url);
                }
            }
            else
            {
                Logger.Warn("No URL available to open");
            }
        }

        internal static void RegisterReferralClickAndOpen(string adId, string offerUrl, string targetBundleId, string storeUrl)
        {
            var client = AdClient.Instance;
            if (!client.IsInitialized)
            {
                Application.OpenURL(storeUrl);
                return;
            }

            DeviceIdentity.GetGAID((gaid) =>
            {
                // Use GAID if available, otherwise fallback to persistent device ID
                // (same logic as DeviceReport to keep device_id consistent)
                string deviceId = string.IsNullOrEmpty(gaid) ? DeviceIdentity.GetCachedGAID() : gaid;

                var request = new ClickRegisterRequest
                {
                    api_key = client.ApiKey,
                    bundle_id = client.BundleId,
                    ad_id = adId,
                    device_id = deviceId,
                    click_url = offerUrl ?? "",
                    target_bundle_id = targetBundleId
                };

                client.RegisterClick(request,
                    onSuccess: (resp) =>
                    {
                        Logger.Debug("Click registered");
                        // Append click_id as referrer to Play Store URL
                        string urlWithReferrer = UrlHelper.AppendReferrer(storeUrl, resp.click_id);
                        Application.OpenURL(urlWithReferrer);
                    },
                    onError: (err) =>
                    {
                        Logger.Warn("Click registration failed: {0}", err);
                        Application.OpenURL(storeUrl);
                    }
                );
            });
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

            Logger.Log("{0} ad loaded", AdType);
            Logger.Debug("Ad response contains impression={0}, click={1}, media={2}",
                !string.IsNullOrEmpty(response.impression_url),
                !string.IsNullOrEmpty(response.click_tracking_url),
                !string.IsNullOrEmpty(response.media_url));

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

            Logger.Warn("{0} ad failed to load: {1}", AdType, error);

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

            Logger.Log("{0} ad closed", AdType);

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
