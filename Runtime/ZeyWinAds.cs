using System;
using System.Collections.Generic;
using UnityEngine;
using ZeyWinAds.Ads;
using ZeyWinAds.Core;

namespace ZeyWinAds
{
    /// <summary>
    /// Main static API for ZeyWin Ads SDK.
    /// Use this class to initialize the SDK, load ads, and display them.
    /// </summary>
    public static class ZeyWinAds
    {
        // Cached ad responses (fallback for direct caching)
        private static AdResponse _cachedInterstitial;
        private static AdResponse _cachedRewarded;
        private static AdResponse _cachedBanner;

        // Callbacks for ad display
        private static Action _onInterstitialClose;
        private static Action<int> _onRewardedReward;
        private static Action _onRewardedClose;

        // Loading state
        private static readonly HashSet<AdType> _loadingAds = new HashSet<AdType>();

        // Banner state
        private static bool _isBannerVisible;
        private static BannerPosition _currentBannerPosition;

        // Active ad instances
        private static BaseAd _activeAd;

        // Events
        public static event Action<AdType> OnAdLoaded;
        public static event Action<AdType, string> OnAdFailedToLoad;
        public static event Action<AdType> OnAdOpened;
        public static event Action<AdType> OnAdClosed;
        public static event Action<AdType> OnAdClicked;
        public static event Action<int> OnRewardEarned;

        /// <summary>
        /// Gets whether the SDK has been initialized.
        /// </summary>
        public static bool IsInitialized => AdClient.Instance.IsInitialized;

        /// <summary>
        /// Initializes the ZeyWin Ads SDK with the provided API key.
        /// Must be called before any other SDK methods.
        /// </summary>
        /// <param name="apiKey">Your ZeyWin Ads API key from the dashboard</param>
        /// <param name="preloadSettings">Optional preload settings configuration</param>
        public static void Initialize(string apiKey, PreloadSettings preloadSettings = null)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                Core.Logger.Error("API key cannot be null or empty");
                return;
            }

            AdClient.Instance.Initialize(apiKey);
            Core.Logger.Log("SDK initialized successfully");

            // Configure and start the AdLoader
            if (preloadSettings != null)
            {
                AdLoader.Instance.Configure(preloadSettings);
            }

            // Subscribe to AdLoader events
            AdLoader.Instance.OnAdPreloaded -= OnAdPreloaded;
            AdLoader.Instance.OnAdPreloaded += OnAdPreloaded;
            AdLoader.Instance.OnPreloadFailed -= OnPreloadFailed;
            AdLoader.Instance.OnPreloadFailed += OnPreloadFailed;

            // Start preloading
            AdLoader.Instance.OnSDKInitialize();
        }

        /// <summary>
        /// Configures the preload settings. Can be called after initialization.
        /// </summary>
        /// <param name="settings">The preload settings to apply</param>
        public static void ConfigurePreloading(PreloadSettings settings)
        {
            AdLoader.Instance.Configure(settings);
        }

        /// <summary>
        /// Sets the logging level for the SDK.
        /// </summary>
        /// <param name="level">The log level to set</param>
        public static void SetLogLevel(LogLevel level)
        {
            Core.Logger.SetLogLevel(level);
        }

        #region Interstitial Ads

        /// <summary>
        /// Loads an interstitial ad. Listen to OnAdLoaded event for completion.
        /// </summary>
        public static void LoadInterstitial()
        {
            // Check if already preloaded
            if (AdLoader.Instance.IsAdReady(AdType.Interstitial))
            {
                Core.Logger.Debug("Interstitial already preloaded");
                OnAdLoaded?.Invoke(AdType.Interstitial);
                return;
            }

            // Use preloader to load
            AdLoader.Instance.PreloadAd(AdType.Interstitial);
        }

        /// <summary>
        /// Checks if an interstitial ad is ready to show.
        /// </summary>
        public static bool IsInterstitialReady()
        {
            return AdLoader.Instance.IsAdReady(AdType.Interstitial) || _cachedInterstitial != null;
        }

        /// <summary>
        /// Shows the loaded interstitial ad.
        /// </summary>
        /// <param name="onClose">Callback invoked when the ad is closed</param>
        public static void ShowInterstitial(Action onClose = null)
        {
            // Try to get from preloader first
            BaseAd preloadedAd = AdLoader.Instance.GetPreloadedAd(AdType.Interstitial);

            if (preloadedAd != null && preloadedAd.IsReady)
            {
                _onInterstitialClose = onClose;
                _activeAd = preloadedAd;

                preloadedAd.Show(() =>
                {
                    HandleAdClosed(AdType.Interstitial);
                    // Trigger preload of next ad
                    AdLoader.Instance.OnAdShown(AdType.Interstitial);
                });

                OnAdOpened?.Invoke(AdType.Interstitial);
                return;
            }

            // Fallback to cached response
            if (_cachedInterstitial == null)
            {
                Core.Logger.Warn("No interstitial ad loaded. Call LoadInterstitial() first.");
                onClose?.Invoke();
                return;
            }

            _onInterstitialClose = onClose;
            ShowAd(_cachedInterstitial, AdType.Interstitial);
            _cachedInterstitial = null;

            // Trigger preload of next ad
            AdLoader.Instance.OnAdShown(AdType.Interstitial);
        }

        #endregion

        #region Rewarded Ads

        /// <summary>
        /// Loads a rewarded ad. Listen to OnAdLoaded event for completion.
        /// </summary>
        public static void LoadRewarded()
        {
            // Check if already preloaded
            if (AdLoader.Instance.IsAdReady(AdType.Rewarded))
            {
                Core.Logger.Debug("Rewarded already preloaded");
                OnAdLoaded?.Invoke(AdType.Rewarded);
                return;
            }

            // Use preloader to load
            AdLoader.Instance.PreloadAd(AdType.Rewarded);
        }

        /// <summary>
        /// Checks if a rewarded ad is ready to show.
        /// </summary>
        public static bool IsRewardedReady()
        {
            return AdLoader.Instance.IsAdReady(AdType.Rewarded) || _cachedRewarded != null;
        }

        /// <summary>
        /// Shows the loaded rewarded ad.
        /// </summary>
        /// <param name="onReward">Callback invoked when reward is earned, with reward amount</param>
        /// <param name="onClose">Callback invoked when the ad is closed</param>
        public static void ShowRewarded(Action<int> onReward, Action onClose = null)
        {
            // Try to get from preloader first
            BaseAd preloadedAd = AdLoader.Instance.GetPreloadedAd(AdType.Rewarded);

            if (preloadedAd is RewardedAd rewardedAd && rewardedAd.IsReady)
            {
                _onRewardedReward = onReward;
                _onRewardedClose = onClose;
                _activeAd = rewardedAd;

                rewardedAd.Show(
                    onReward: (amount) =>
                    {
                        _onRewardedReward?.Invoke(amount);
                        OnRewardEarned?.Invoke(amount);
                    },
                    onClose: () =>
                    {
                        HandleAdClosed(AdType.Rewarded);
                        // Trigger preload of next ad
                        AdLoader.Instance.OnAdShown(AdType.Rewarded);
                    }
                );

                OnAdOpened?.Invoke(AdType.Rewarded);
                return;
            }

            // Fallback to cached response
            if (_cachedRewarded == null)
            {
                Core.Logger.Warn("No rewarded ad loaded. Call LoadRewarded() first.");
                onClose?.Invoke();
                return;
            }

            _onRewardedReward = onReward;
            _onRewardedClose = onClose;
            ShowAd(_cachedRewarded, AdType.Rewarded);
            _cachedRewarded = null;

            // Trigger preload of next ad
            AdLoader.Instance.OnAdShown(AdType.Rewarded);
        }

        #endregion

        #region Banner Ads

        /// <summary>
        /// Loads a banner ad. Listen to OnAdLoaded event for completion.
        /// </summary>
        public static void LoadBanner()
        {
            // Check if already preloaded
            if (AdLoader.Instance.IsAdReady(AdType.Banner))
            {
                Core.Logger.Debug("Banner already preloaded");
                OnAdLoaded?.Invoke(AdType.Banner);
                return;
            }

            // Use preloader to load
            AdLoader.Instance.PreloadAd(AdType.Banner);
        }

        /// <summary>
        /// Checks if a banner ad is ready to show.
        /// </summary>
        public static bool IsBannerReady()
        {
            return AdLoader.Instance.IsAdReady(AdType.Banner) || _cachedBanner != null;
        }

        /// <summary>
        /// Shows the loaded banner ad at the specified position.
        /// </summary>
        /// <param name="position">Where to display the banner (Top or Bottom)</param>
        public static void ShowBanner(BannerPosition position)
        {
            // Try to get from preloader first
            BaseAd preloadedAd = AdLoader.Instance.GetPreloadedAd(AdType.Banner);

            if (preloadedAd is BannerAd bannerAd && bannerAd.IsReady)
            {
                _currentBannerPosition = position;
                _isBannerVisible = true;
                _activeAd = bannerAd;

                bannerAd.SetPosition(position);
                bannerAd.Show();

                OnAdOpened?.Invoke(AdType.Banner);
                Core.Logger.Log("Banner shown at {0}", position);
                return;
            }

            // Fallback to cached response
            if (_cachedBanner == null)
            {
                Core.Logger.Warn("No banner ad loaded. Call LoadBanner() first.");
                return;
            }

            _currentBannerPosition = position;
            _isBannerVisible = true;

            // Track impression
            TrackImpression(_cachedBanner);

            OnAdOpened?.Invoke(AdType.Banner);

            Core.Logger.Log("Banner shown at {0}", position);
        }

        /// <summary>
        /// Hides the currently displayed banner.
        /// </summary>
        public static void HideBanner()
        {
            if (!_isBannerVisible)
                return;

            _isBannerVisible = false;

            // Hide the banner ad instance if active
            if (_activeAd is BannerAd bannerAd)
            {
                bannerAd.Hide();
                _activeAd = null;
            }

            OnAdClosed?.Invoke(AdType.Banner);

            Core.Logger.Log("Banner hidden");
        }

        /// <summary>
        /// Gets the current banner position.
        /// </summary>
        public static BannerPosition GetBannerPosition()
        {
            return _currentBannerPosition;
        }

        /// <summary>
        /// Checks if banner is currently visible.
        /// </summary>
        public static bool IsBannerVisible()
        {
            return _isBannerVisible;
        }

        /// <summary>
        /// Gets the current banner ad data (for UI rendering).
        /// </summary>
        public static AdResponse GetCurrentBanner()
        {
            if (_activeAd is BannerAd bannerAd && bannerAd.IsReady)
            {
                return bannerAd.AdData;
            }
            return _isBannerVisible ? _cachedBanner : null;
        }

        #endregion

        #region Internal Methods

        private static void OnAdPreloaded(AdType adType)
        {
            Core.Logger.Debug("{0} ad preloaded and ready", adType);
            OnAdLoaded?.Invoke(adType);
        }

        private static void OnPreloadFailed(AdType adType, string error)
        {
            Core.Logger.Warn("Preload failed for {0}: {1}", adType, error);
            OnAdFailedToLoad?.Invoke(adType, error);
        }

        private static void LoadAd(AdType adType)
        {
            if (!AdClient.Instance.IsInitialized)
            {
                Core.Logger.Error("SDK not initialized. Call ZeyWinAds.Initialize() first.");
                OnAdFailedToLoad?.Invoke(adType, "SDK not initialized");
                return;
            }

            if (_loadingAds.Contains(adType))
            {
                Core.Logger.Warn("{0} ad is already loading", adType);
                return;
            }

            _loadingAds.Add(adType);
            Core.Logger.Log("Loading {0} ad...", adType);

            AdClient.Instance.RequestAd(adType,
                onSuccess: (response) =>
                {
                    _loadingAds.Remove(adType);
                    CacheAd(adType, response);
                    Core.Logger.Log("{0} ad loaded successfully", adType);
                    OnAdLoaded?.Invoke(adType);
                },
                onError: (error) =>
                {
                    _loadingAds.Remove(adType);
                    Core.Logger.Warn("Failed to load {0} ad: {1}", adType, error);
                    OnAdFailedToLoad?.Invoke(adType, error);
                }
            );
        }

        private static void CacheAd(AdType adType, AdResponse response)
        {
            switch (adType)
            {
                case AdType.Interstitial:
                    _cachedInterstitial = response;
                    break;
                case AdType.Rewarded:
                    _cachedRewarded = response;
                    break;
                case AdType.Banner:
                    _cachedBanner = response;
                    break;
            }
        }

        private static void ShowAd(AdResponse ad, AdType adType)
        {
            // Track impression
            TrackImpression(ad);

            OnAdOpened?.Invoke(adType);

            Core.Logger.Log("Showing {0} ad: {1}", adType, ad.ad_id);
        }

        private static void TrackImpression(AdResponse ad)
        {
            if (!string.IsNullOrEmpty(ad.impression_url))
            {
                AdClient.Instance.TrackEvent(ad.impression_url);
            }
        }

        /// <summary>
        /// Called by UI when ad is clicked. Tracks the click and opens the URL.
        /// </summary>
        internal static void HandleAdClick(AdResponse ad, AdType adType)
        {
            if (ad == null) return;

            // Track click
            if (!string.IsNullOrEmpty(ad.click_tracking_url))
            {
                AdClient.Instance.TrackEvent(ad.click_tracking_url);
            }

            // Open click URL
            if (!string.IsNullOrEmpty(ad.click_url))
            {
                Application.OpenURL(ad.click_url);
            }

            OnAdClicked?.Invoke(adType);
        }

        /// <summary>
        /// Called by UI when ad viewing is completed.
        /// </summary>
        internal static void HandleAdComplete(AdResponse ad, AdType adType)
        {
            if (ad == null) return;

            // Track completion
            if (!string.IsNullOrEmpty(ad.complete_url))
            {
                AdClient.Instance.TrackEvent(ad.complete_url);
            }

            if (adType == AdType.Rewarded && !string.IsNullOrEmpty(ad.reward_url))
            {
                // Track reward
                AdClient.Instance.TrackEvent(ad.reward_url,
                    onSuccess: () =>
                    {
                        int rewardAmount = ZeyWinAdsConfig.DefaultRewardAmount;
                        _onRewardedReward?.Invoke(rewardAmount);
                        OnRewardEarned?.Invoke(rewardAmount);
                    }
                );
            }
        }

        /// <summary>
        /// Called by UI when ad is closed.
        /// </summary>
        internal static void HandleAdClosed(AdType adType)
        {
            _activeAd = null;
            OnAdClosed?.Invoke(adType);

            switch (adType)
            {
                case AdType.Interstitial:
                    _onInterstitialClose?.Invoke();
                    _onInterstitialClose = null;
                    break;
                case AdType.Rewarded:
                    _onRewardedClose?.Invoke();
                    _onRewardedClose = null;
                    _onRewardedReward = null;
                    break;
            }
        }

        /// <summary>
        /// Resets SDK state (useful for testing).
        /// </summary>
        public static void Reset()
        {
            _cachedInterstitial = null;
            _cachedRewarded = null;
            _cachedBanner = null;
            _loadingAds.Clear();
            _isBannerVisible = false;
            _onInterstitialClose = null;
            _onRewardedReward = null;
            _onRewardedClose = null;
            _activeAd = null;

            // Clear the AdLoader cache
            AdLoader.Instance.ClearCache();

            DeviceInfo.ClearCache();
        }

        #endregion
    }
}
