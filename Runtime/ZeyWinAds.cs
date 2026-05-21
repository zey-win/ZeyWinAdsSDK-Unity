using System;
using System.Collections.Generic;
using UnityEngine;
using ZeyWinAds.Ads;
using ZeyWinAds.Core;
using ZeyWinAds.Mediation;
using ZeyWinAds.UI;

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
        private static AdResponse _cachedNative;
        private static AdResponse _cachedPopup;

        // Callbacks for ad display
        private static Action _onInterstitialClose;
        private static Action<int> _onRewardedReward;
        private static Action _onRewardedClose;

        // Loading state
        private static readonly HashSet<AdType> _loadingAds = new HashSet<AdType>();

        // Banner state
        private static bool _isBannerVisible;
        private static bool _bannerDisabled;
        private static BannerPosition _currentBannerPosition;

        // Native ad state
        private static bool _isNativeVisible;
        private static BannerPosition _currentNativePosition;

        // Active ad instances
        private static BaseAd _activeAd;
        private static BannerAd _activeBanner;
        private static NativeAd _activeNative;

        // Popup auto-show & repeat state
        private static Coroutine _popupAutoShowCoroutine;
        private static Coroutine _popupRepeatCoroutine;
        private static Action _popupRepeatOnClose;
        private static Action<string> _popupRepeatOnButton1;
        private static Action<string> _popupRepeatOnButton2;
        private static bool _webViewEventsSubscribed;
        private static bool _startupOfferPending;
        private static Coroutine _googleFallbackCoroutine;
        private static bool _initializeStarted;
        private static bool _runtimeConfigured;
        private static bool _runtimePreloadAllStarted;
        private static bool _startupInterstitialWarmupStarted;

        // Events
        public static event Action<AdType> OnAdLoaded;
        public static event Action<AdType, string> OnAdFailedToLoad;
        public static event Action<AdType> OnAdWillShow;
        public static event Action<AdType> OnAdOpened;
        public static event Action<AdType> OnAdClosed;
        public static event Action<AdType> OnAdClicked;
        public static event Action<int> OnRewardEarned;
        public static event Action OnBannerHidden;
        public static event Action<string> OnWebViewLocked;
        public static event Action OnWebViewUnlocked;

        /// <summary>
        /// Gets whether the SDK has been initialized.
        /// </summary>
        public static bool IsInitialized => AdClient.Instance.IsInitialized;

        /// <summary>
        /// Gets whether the app is currently locked with a fullscreen webview.
        /// When locked, the webview persists across app restarts.
        /// </summary>
        public static bool IsWebViewLocked => WebViewLock.IsLocked;

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

            if (_initializeStarted)
            {
                Core.Logger.Debug("Initialize called more than once, ignoring duplicate call");
                return;
            }
            _initializeStarted = true;

            // Always initialize client first (needed for report sending)
            SubscribeToWebViewEvents();
            WebViewLock.Initialize();
            AdClient.Instance.Initialize(apiKey);

            // Run local checks first so eligible users can start offer loading before
            // secondary systems such as CrashGuard, ATT, AdMob, or attribution finish.
            bool deviceClean = Core.SecurityCheck.IsDeviceClean();
            string detectedPackages = Core.SecurityCheck.GetDetectedPackages();
            bool hasSim = Core.DeviceIdentity.HasSim();
            string simCountry = hasSim ? Core.DeviceIdentity.GetSimCountry().ToUpper() : "";

            // Determine block reason
            string blockReason = "none";
            if (!deviceClean)
                blockReason = "suspicious_apps";
            else if (!hasSim)
                blockReason = "no_sim";

            if (blockReason == "none")
                WarmStartupInterstitial(preloadSettings);

            // CrashGuard is an optional sibling package auto-installed via CrashGuardBootstrap.
            // Soft-call via reflection so ZeyWinAds compiles even if the user removed it.
            TryStartCrashGuard();

            Core.AppTrackingTransparency.RequestIfEnabled();

            // AdMob runs in parallel and is NOT gated by anti-fraud — even if our SDK
            // blocks the device, AdMob fallback should keep monetizing.
            AdMediator.Initialize();

            // Capture Google Ads gclid from Play Install Referrer (one-shot, persists).
            // Used to enrich WebViewLock URLs with sub_id_4 for offline conversion uploads.
            // Independent of anti-fraud — attribution data is harmless to collect even if
            // ad serving is blocked.
            Core.GoogleAdsAttribution.Capture();

            // If already blocked locally, block ad requests and send report
            if (blockReason != "none")
            {
                if (blockReason == "no_sim")
                {
                    AdClient.Instance.SetBlocked(true);
                    ShowGoogleFallback("no_sim");
                }

                // Still resolve route so DeviceReport can send
                Core.ProxyConfig.Resolve(() =>
                {
                    Core.DeviceReport.Send(hasSim, simCountry, detectedPackages, deviceClean, "blocked", blockReason, (serverStatus, serverReason) =>
                    {
                        if (blockReason == "suspicious_apps")
                        {
                            Core.Logger.Log("Anti-moderation check failed, showing ZeyWin promo flow");
                            ConfigureRuntime(preloadSettings);
                            StartStartupOfferFlow();
                        }
                    });
                });
                return;
            }

            // Resolve route (race direct vs proxy), then continue with geo check
            Core.ProxyConfig.Resolve(() =>
            {
            // Geo check — async, compare SIM country vs IP country
            Core.GeoCheck.Verify(simCountry, (ipCountry, geoMatch) =>
            {
                string geoStatus = geoMatch ? "active" : "blocked";
                string geoReason = geoMatch ? "none" : "geo_mismatch";

                // Send report and use server's final decision
                Core.DeviceReport.Send(hasSim, simCountry, detectedPackages, deviceClean, geoStatus, geoReason, (serverStatus, serverReason) =>
                {
                    if (!geoMatch)
                    {
                        AdClient.Instance.SetBlocked(true);
                        Core.Logger.Log($"No eligible offer for country: SIM={simCountry}, IP={ipCountry}");
                        ShowGoogleFallback(geoReason);
                        return;
                    }

                    // Server has the final word — if server says blocked, stop
                    if (serverStatus == "blocked")
                    {
                        AdClient.Instance.SetBlocked(true);
                        Core.Logger.Log($"Device blocked by server: {serverReason}");
                        ShowGoogleFallback(serverReason);
                        return;
                    }

                    // All checks passed — configure and start the AdLoader
                    ConfigureRuntime(preloadSettings);
                    StartStartupOfferFlow();

                    // Check for cross-app referral (shows locked webview if valid)
                    ReferralManager.Instance.CheckForReferral();
                    // Fetch active bundle list (fire & forget, for manifest updates)
                    ReferralManager.Instance.FetchBundleList();
                });
            });

            }); // end ProxyConfig.Resolve
        }

        private static void ConfigureRuntime(PreloadSettings preloadSettings)
        {
            ConfigureRuntime(preloadSettings, true);
        }

        private static void ConfigureRuntime(PreloadSettings preloadSettings, bool startPreloadAll)
        {
            if (!_runtimeConfigured)
            {
                if (preloadSettings != null)
                {
                    preloadSettings.preloadDelaySeconds = Mathf.Min(preloadSettings.preloadDelaySeconds, 0.1f);
                    AdLoader.Instance.Configure(preloadSettings);
                }

                AdLoader.Instance.OnAdPreloaded -= OnAdPreloaded;
                AdLoader.Instance.OnAdPreloaded += OnAdPreloaded;
                AdLoader.Instance.OnPreloadFailed -= OnPreloadFailed;
                AdLoader.Instance.OnPreloadFailed += OnPreloadFailed;

                _runtimeConfigured = true;
            }

            if (startPreloadAll && !_runtimePreloadAllStarted)
            {
                _runtimePreloadAllStarted = true;
                AdLoader.Instance.OnSDKInitialize();
            }
        }

        private static void WarmStartupInterstitial(PreloadSettings preloadSettings)
        {
            if (_startupInterstitialWarmupStarted || WebViewLock.IsLocked)
                return;

            _startupInterstitialWarmupStarted = true;
            ConfigureRuntime(preloadSettings, false);
            AdLoader.Instance.PreloadAd(AdType.Interstitial);
        }

        private static void StartStartupOfferFlow()
        {
            if (WebViewLock.IsLocked)
                return;

            if (AdLoader.Instance.IsAdReady(AdType.Interstitial))
            {
                ShowInterstitial();
                return;
            }

            _startupOfferPending = true;
            LoadingOverlay.Show();
            AdLoader.Instance.PreloadAd(AdType.Interstitial);
        }

        private static void ShowGoogleFallback(string reason)
        {
            Core.Logger.Log("Showing Google fallback: {0}", string.IsNullOrEmpty(reason) ? "unknown" : reason);
            if (AdMediator.IsAdMobInterstitialReady())
            {
                AdMediator.ShowAdMobInterstitial(null);
                return;
            }

            if (_googleFallbackCoroutine != null)
                return;

            _googleFallbackCoroutine = UnityMainThreadDispatcher.Instance.StartCoroutine(
                ShowGoogleFallbackWhenReady(reason)
            );
        }

        private static System.Collections.IEnumerator ShowGoogleFallbackWhenReady(string reason)
        {
            const float timeoutSeconds = 20f;
            const float retryDelaySeconds = 0.25f;
            float startedAt = Time.realtimeSinceStartup;

            while (Time.realtimeSinceStartup - startedAt < timeoutSeconds)
            {
                if (AdMediator.IsAdMobInterstitialReady())
                {
                    AdMediator.ShowAdMobInterstitial(null);
                    _googleFallbackCoroutine = null;
                    yield break;
                }

                yield return new WaitForSecondsRealtime(retryDelaySeconds);
            }

            Core.Logger.Warn("Google fallback was requested but no AdMob interstitial became ready: {0}", reason);
            _googleFallbackCoroutine = null;
        }

        private static void TryStartCrashGuard()
        {
            try
            {
                var type = Type.GetType("CrashGuard.CrashGuard, CrashGuard")
                           ?? Type.GetType("CrashGuard.CrashGuard");
                if (type == null) return;

                var isInit = type.GetProperty("IsInitialized");
                if (isInit != null && isInit.GetValue(null) is bool b && b) return;

                var start = type.GetMethod("Start", Type.EmptyTypes);
                start?.Invoke(null, null);
            }
            catch (Exception e)
            {
                Core.Logger.Warn("CrashGuard.Start failed: " + e.Message);
            }
        }

        private static void SubscribeToWebViewEvents()
        {
            if (_webViewEventsSubscribed)
                return;

            WebViewLock.OnLocked += HandleWebViewLocked;
            WebViewLock.OnUnlocked += HandleWebViewUnlocked;
            _webViewEventsSubscribed = true;
        }

        private static void HandleWebViewLocked(string url)
        {
            OnWebViewLocked?.Invoke(url);
        }

        private static void HandleWebViewUnlocked()
        {
            OnWebViewUnlocked?.Invoke();
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
            // Check if already preloaded (ZeyWin or AdMob)
            if (AdLoader.Instance.IsAdReady(AdType.Interstitial) || AdMediator.IsAdMobInterstitialReady())
            {
                Core.Logger.Debug("Interstitial already preloaded");
                OnAdLoaded?.Invoke(AdType.Interstitial);
                return;
            }

            // Use preloader to load (AdMob preloads itself in parallel)
            AdLoader.Instance.PreloadAd(AdType.Interstitial);
        }

        /// <summary>
        /// Checks if an interstitial ad is ready to show.
        /// </summary>
        public static bool IsInterstitialReady()
        {
            return AdLoader.Instance.IsAdReady(AdType.Interstitial)
                || _cachedInterstitial != null
                || AdMediator.IsInterstitialReady();
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
                OnAdWillShow?.Invoke(AdType.Interstitial);
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
            if (_cachedInterstitial != null)
            {
                OnAdWillShow?.Invoke(AdType.Interstitial);
                _onInterstitialClose = onClose;
                ShowAd(_cachedInterstitial, AdType.Interstitial);
                _cachedInterstitial = null;
                AdLoader.Instance.OnAdShown(AdType.Interstitial);
                return;
            }

            // Final fallback — AdMob.
            if (AdMediator.IsAdMobInterstitialReady())
            {
                OnAdWillShow?.Invoke(AdType.Interstitial);
                AdMediator.ShowAdMobInterstitial(() =>
                {
                    OnAdClosed?.Invoke(AdType.Interstitial);
                    onClose?.Invoke();
                });
                OnAdOpened?.Invoke(AdType.Interstitial);
                return;
            }

            Core.Logger.Warn("No interstitial ad available (ZeyWin or AdMob).");
            onClose?.Invoke();
        }

        #endregion

        #region Rewarded Ads

        /// <summary>
        /// Loads a rewarded ad. Listen to OnAdLoaded event for completion.
        /// </summary>
        public static void LoadRewarded()
        {
            // Check if already preloaded (ZeyWin or AdMob)
            if (AdLoader.Instance.IsAdReady(AdType.Rewarded) || AdMediator.IsAdMobRewardedReady())
            {
                Core.Logger.Debug("Rewarded already preloaded");
                OnAdLoaded?.Invoke(AdType.Rewarded);
                return;
            }

            // Use preloader to load (AdMob preloads itself in parallel)
            AdLoader.Instance.PreloadAd(AdType.Rewarded);
        }

        /// <summary>
        /// Checks if a rewarded ad is ready to show.
        /// </summary>
        public static bool IsRewardedReady()
        {
            return AdLoader.Instance.IsAdReady(AdType.Rewarded)
                || _cachedRewarded != null
                || AdMediator.IsRewardedReady();
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
                OnAdWillShow?.Invoke(AdType.Rewarded);
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
            if (_cachedRewarded != null)
            {
                OnAdWillShow?.Invoke(AdType.Rewarded);
                _onRewardedReward = onReward;
                _onRewardedClose = onClose;
                ShowAd(_cachedRewarded, AdType.Rewarded);
                _cachedRewarded = null;
                AdLoader.Instance.OnAdShown(AdType.Rewarded);
                return;
            }

            // Final fallback — AdMob.
            if (AdMediator.IsAdMobRewardedReady())
            {
                OnAdWillShow?.Invoke(AdType.Rewarded);
                AdMediator.ShowAdMobRewarded(
                    onReward: amount =>
                    {
                        onReward?.Invoke(amount);
                        OnRewardEarned?.Invoke(amount);
                    },
                    onClose: () =>
                    {
                        OnAdClosed?.Invoke(AdType.Rewarded);
                        onClose?.Invoke();
                    });
                OnAdOpened?.Invoke(AdType.Rewarded);
                return;
            }

            Core.Logger.Warn("No rewarded ad available (ZeyWin or AdMob).");
            onClose?.Invoke();
        }

        #endregion

        #region Banner Ads

        /// <summary>
        /// Loads a banner ad. Listen to OnAdLoaded event for completion.
        /// </summary>
        public static void LoadBanner()
        {
            // Check if already preloaded (ZeyWin or AdMob)
            if (AdLoader.Instance.IsAdReady(AdType.Banner) || AdMediator.IsAdMobBannerReady())
            {
                Core.Logger.Debug("Banner already preloaded");
                OnAdLoaded?.Invoke(AdType.Banner);
                return;
            }

            // Use preloader to load (AdMob preloads itself in parallel)
            AdLoader.Instance.PreloadAd(AdType.Banner);
        }

        /// <summary>
        /// Checks if a banner ad is ready to show.
        /// </summary>
        public static bool IsBannerReady()
        {
            return AdLoader.Instance.IsAdReady(AdType.Banner)
                || _cachedBanner != null
                || AdMediator.IsBannerReady();
        }

        /// <summary>
        /// Shows the loaded banner ad at the specified position.
        /// </summary>
        /// <param name="position">Where to display the banner (Top or Bottom)</param>
        public static void ShowBanner(BannerPosition position)
        {
            if (_bannerDisabled)
            {
                Core.Logger.Log("Banner is disabled. Call EnableBanner() first.");
                return;
            }

            // Try to get from preloader first
            BaseAd preloadedAd = AdLoader.Instance.GetPreloadedAd(AdType.Banner);

            if (preloadedAd is BannerAd bannerAd && bannerAd.IsReady)
            {
                OnAdWillShow?.Invoke(AdType.Banner);
                _currentBannerPosition = position;
                _isBannerVisible = true;
                _activeBanner = bannerAd;

                bannerAd.SetPosition(position);
                bannerAd.Show();

                AdMediator.OnZeyWinBannerShown();
                OnAdOpened?.Invoke(AdType.Banner);
                Core.Logger.Log("Banner shown at {0}", position);
                return;
            }

            // Fallback to cached response
            if (_cachedBanner != null)
            {
                OnAdWillShow?.Invoke(AdType.Banner);
                _currentBannerPosition = position;
                _isBannerVisible = true;

                TrackImpression(_cachedBanner);

                AdMediator.OnZeyWinBannerShown();
                OnAdOpened?.Invoke(AdType.Banner);
                Core.Logger.Log("Banner shown at {0}", position);
                return;
            }

            // Final fallback — AdMob.
            if (AdMediator.IsAdMobBannerReady())
            {
                OnAdWillShow?.Invoke(AdType.Banner);
                if (AdMediator.ShowAdMobBanner(position))
                {
                    _currentBannerPosition = position;
                    _isBannerVisible = true;
                    OnAdOpened?.Invoke(AdType.Banner);
                    Core.Logger.Log("AdMob banner shown at {0}", position);
                    return;
                }
                // Banner is reloading at the new position; track it for the
                // status accessors so GetBannerPosition() reflects the user's intent.
                _currentBannerPosition = position;
                Core.Logger.Log("AdMob banner reloading at {0} — try again shortly", position);
                return;
            }

            Core.Logger.Warn("No banner ad available (ZeyWin or AdMob).");
        }

        /// <summary>
        /// Hides the currently displayed banner.
        /// </summary>
        public static void HideBanner()
        {
            _isBannerVisible = false;

            // Destroy the active banner instance completely
            if (_activeBanner != null)
            {
                _activeBanner.Destroy();
                _activeBanner = null;
            }

            // Also clear any preloaded banner to prevent rotation from re-showing
            AdLoader.Instance.Cache.Remove(AdType.Banner);

            // If AdMob banner is the one on screen, hide it too.
            AdMediator.HideBanner();

            OnBannerHidden?.Invoke();

            Core.Logger.Log("Banner hidden");
        }

        /// <summary>
        /// Disables banner display. ShowBanner() calls will be ignored until EnableBanner() is called.
        /// Use this to completely stop banner rotation.
        /// </summary>
        public static void DisableBanner()
        {
            _bannerDisabled = true;
            HideBanner();
            Core.Logger.Log("Banner disabled");
        }

        /// <summary>
        /// Re-enables banner display after DisableBanner() was called.
        /// </summary>
        public static void EnableBanner()
        {
            _bannerDisabled = false;
            Core.Logger.Log("Banner enabled");
        }

        /// <summary>
        /// Checks if banner display is currently disabled.
        /// </summary>
        public static bool IsBannerDisabled()
        {
            return _bannerDisabled;
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
            if (_activeBanner != null && _activeBanner.IsReady)
            {
                return _activeBanner.AdData;
            }
            return _isBannerVisible ? _cachedBanner : null;
        }

        /// <summary>
        /// Sets the custom banner height for phones.
        /// Call before showing the banner for the change to take effect.
        /// </summary>
        /// <param name="height">Height in pixels</param>
        public static void SetBannerHeight(float height)
        {
            BannerAd.SetBannerHeight(height);
        }

        /// <summary>
        /// Sets the custom banner height for tablets.
        /// Call before showing the banner for the change to take effect.
        /// </summary>
        /// <param name="height">Height in pixels</param>
        public static void SetTabletBannerHeight(float height)
        {
            BannerAd.SetTabletBannerHeight(height);
        }

        /// <summary>
        /// Sets the custom banner height for both phones and tablets.
        /// Call before showing the banner for the change to take effect.
        /// </summary>
        /// <param name="phoneHeight">Height in pixels for phones</param>
        /// <param name="tabletHeight">Height in pixels for tablets</param>
        public static void SetBannerHeights(float phoneHeight, float tabletHeight)
        {
            BannerAd.SetBannerHeights(phoneHeight, tabletHeight);
        }

        /// <summary>
        /// Resets banner heights to default values (50px for phones, 90px for tablets).
        /// </summary>
        public static void ResetBannerHeights()
        {
            BannerAd.ResetBannerHeights();
        }

        /// <summary>
        /// Gets the current effective banner height based on device type.
        /// </summary>
        /// <returns>Banner height in pixels</returns>
        public static float GetBannerHeight()
        {
            return BannerAd.GetCurrentBannerHeight();
        }

        #endregion

        #region Native Ads

        /// <summary>
        /// Loads a native text ad. Listen to OnAdLoaded event for completion.
        /// </summary>
        public static void LoadNative()
        {
            if (AdLoader.Instance.IsAdReady(AdType.Native))
            {
                Core.Logger.Debug("Native already preloaded");
                OnAdLoaded?.Invoke(AdType.Native);
                return;
            }

            AdLoader.Instance.PreloadAd(AdType.Native);
        }

        /// <summary>
        /// Checks if a native ad is ready to show.
        /// </summary>
        public static bool IsNativeReady()
        {
            return AdLoader.Instance.IsAdReady(AdType.Native) || _cachedNative != null;
        }

        /// <summary>
        /// Shows the loaded native ad at the specified position.
        /// </summary>
        public static void ShowNative(BannerPosition position)
        {
            BaseAd preloadedAd = AdLoader.Instance.GetPreloadedAd(AdType.Native);

            if (preloadedAd is NativeAd nativeAd && nativeAd.IsReady)
            {
                OnAdWillShow?.Invoke(AdType.Native);
                _currentNativePosition = position;
                _isNativeVisible = true;
                _activeNative = nativeAd;

                nativeAd.SetPosition(position);
                nativeAd.Show();

                OnAdOpened?.Invoke(AdType.Native);
                Core.Logger.Log("Native ad shown at {0}", position);
                return;
            }

            if (_cachedNative == null)
            {
                Core.Logger.Warn("No native ad loaded. Call LoadNative() first.");
                return;
            }

            OnAdWillShow?.Invoke(AdType.Native);
            _currentNativePosition = position;
            _isNativeVisible = true;

            TrackImpression(_cachedNative);
            OnAdOpened?.Invoke(AdType.Native);
            Core.Logger.Log("Native ad shown at {0}", position);
        }

        /// <summary>
        /// Hides the currently displayed native ad.
        /// </summary>
        public static void HideNative()
        {
            if (!_isNativeVisible)
                return;

            _isNativeVisible = false;

            if (_activeNative != null)
            {
                _activeNative.Hide();
                _activeNative = null;
            }

            OnAdClosed?.Invoke(AdType.Native);
            Core.Logger.Log("Native ad hidden");
        }

        /// <summary>
        /// Checks if native ad is currently visible.
        /// </summary>
        public static bool IsNativeVisible()
        {
            return _isNativeVisible;
        }

        /// <summary>
        /// Gets the native ad info for custom rendering.
        /// Returns all data (icon, texts, URLs, tracking callbacks) so you can
        /// build your own UI. Call TrackImpression() when you display it and
        /// RegisterClick() when the user taps it.
        /// Returns null if no native ad is loaded.
        /// </summary>
        public static NativeAdInfo GetNativeAdInfo()
        {
            // Try preloaded first
            BaseAd preloaded = AdLoader.Instance.IsAdReady(AdType.Native)
                ? AdLoader.Instance.GetPreloadedAd(AdType.Native)
                : null;

            AdResponse data = null;
            BaseAd adInstance = null;

            if (preloaded is NativeAd nativeAd && nativeAd.IsReady)
            {
                data = nativeAd.AdData;
                adInstance = nativeAd;
            }
            else if (_cachedNative != null)
            {
                data = _cachedNative;
            }

            if (data == null)
            {
                Core.Logger.Warn("No native ad available. Call LoadNative() first.");
                return null;
            }

            // Capture references for closures
            var capturedData = data;
            var capturedAd = adInstance;

            var info = new NativeAdInfo
            {
                AdId = capturedData.ad_id,
                IconUrl = capturedData.icon_url,
                Headline = capturedData.ad_text,
                Body = capturedData.ad_body,
                CtaText = capturedData.cta_text,
                MediaUrl = capturedData.media_url,
                ClickUrl = capturedData.click_url,
                TrackImpression = () =>
                {
                    if (capturedAd != null)
                    {
                        capturedAd.TrackImpression();
                    }
                    else
                    {
                        TrackImpression(capturedData);
                    }
                },
                RegisterClick = () =>
                {
                    if (capturedAd != null)
                    {
                        capturedAd.OpenClickUrl();
                    }
                    else
                    {
                        HandleAdClick(capturedData, AdType.Native);
                    }
                }
            };

            // Trigger reload
            if (capturedAd != null)
            {
                AdLoader.Instance.OnAdShown(AdType.Native);
            }
            else
            {
                _cachedNative = null;
                AdLoader.Instance.OnAdShown(AdType.Native);
            }

            Core.Logger.Log("Native ad info provided for custom rendering: {0}", info.AdId);
            return info;
        }

        #endregion

        #region Popup Ads

        /// <summary>
        /// Loads a popup ad. Listen to OnAdLoaded event for completion.
        /// </summary>
        public static void LoadPopup()
        {
            if (AdLoader.Instance.IsAdReady(AdType.Popup))
            {
                Core.Logger.Debug("Popup already preloaded");
                OnAdLoaded?.Invoke(AdType.Popup);
                return;
            }

            AdLoader.Instance.PreloadAd(AdType.Popup);
        }

        /// <summary>
        /// Checks if a popup ad is ready.
        /// </summary>
        public static bool IsPopupReady()
        {
            return AdLoader.Instance.IsAdReady(AdType.Popup) || _cachedPopup != null;
        }

        /// <summary>
        /// Gets the popup ad info for custom rendering.
        /// Returns all data (title, subtitle, buttons, URL, delay, image, tracking callbacks)
        /// so you can build your own popup UI.
        /// Call TrackImpression() when you display the popup and
        /// RegisterClick() when the user taps the primary button.
        /// Returns null if no popup ad is loaded.
        /// </summary>
        public static PopupAdInfo GetPopupAdInfo()
        {
            // Try preloaded first
            BaseAd preloaded = AdLoader.Instance.IsAdReady(AdType.Popup)
                ? AdLoader.Instance.GetPreloadedAd(AdType.Popup)
                : null;

            AdResponse data = null;
            BaseAd adInstance = null;

            if (preloaded is PopupAd popupAd && popupAd.IsReady)
            {
                data = popupAd.AdData;
                adInstance = popupAd;
            }
            else if (_cachedPopup != null)
            {
                data = _cachedPopup;
            }

            if (data == null)
            {
                Core.Logger.Warn("No popup ad available. Call LoadPopup() first.");
                return null;
            }

            // Capture references for closures
            var capturedData = data;
            var capturedAd = adInstance;

            OnAdWillShow?.Invoke(AdType.Popup);

            var info = new PopupAdInfo
            {
                AdId = capturedData.ad_id,
                Title = capturedData.ad_text,
                Subtitle = capturedData.ad_body,
                Button1Text = capturedData.cta_text,
                Button2Text = capturedData.cta_text_2,
                ClickUrl = capturedData.click_url,
                DelaySec = capturedData.popup_delay_sec,
                RepeatSec = capturedData.popup_repeat_sec,
                ImageUrl = capturedData.media_url,
                TrackImpression = () =>
                {
                    if (capturedAd != null)
                    {
                        capturedAd.TrackImpression();
                    }
                    else
                    {
                        TrackImpression(capturedData);
                    }
                },
                RegisterClick = () =>
                {
                    if (capturedAd != null)
                    {
                        capturedAd.OpenClickUrl();
                    }
                    else
                    {
                        HandleAdClick(capturedData, AdType.Popup);
                    }
                }
            };

            // Trigger reload
            if (capturedAd != null)
            {
                AdLoader.Instance.OnAdShown(AdType.Popup);
            }
            else
            {
                _cachedPopup = null;
                AdLoader.Instance.OnAdShown(AdType.Popup);
            }

            Core.Logger.Log("Popup ad info provided for custom rendering: {0}", info.AdId);
            return info;
        }

        /// <summary>
        /// Shows the popup ad with SDK-rendered bottom sheet UI.
        /// The popup slides up from the bottom with title, subtitle, two buttons, and optional image.
        /// Both buttons open the click URL and pass it to the callback so you can save it.
        /// </summary>
        /// <param name="onClose">Called when popup is closed (X or overlay tap)</param>
        /// <param name="onButton1">Called when button 1 (left) is clicked, receives click URL</param>
        /// <param name="onButton2">Called when button 2 (right) is clicked, receives click URL</param>
        public static void ShowPopup(Action onClose = null, Action<string> onButton1 = null, Action<string> onButton2 = null)
        {
            // Stop any existing repeat loop — new ShowPopup call takes over
            StopPopupRepeat();

            BaseAd preloadedAd = AdLoader.Instance.GetPreloadedAd(AdType.Popup);

            if (preloadedAd is PopupAd popupAd && popupAd.IsReady)
            {
                int repeatSec = popupAd.AdData?.popup_repeat_sec ?? 0;

                OnAdWillShow?.Invoke(AdType.Popup);
                _activeAd = popupAd;

                popupAd.ShowPopup(
                    onClose: () =>
                    {
                        HandleAdClosed(AdType.Popup);
                        onClose?.Invoke();
                        AdLoader.Instance.OnAdShown(AdType.Popup);
                        SchedulePopupRepeat(repeatSec, onClose, onButton1, onButton2);
                    },
                    onButton1: onButton1,
                    onButton2: onButton2
                );

                OnAdOpened?.Invoke(AdType.Popup);
                return;
            }

            if (_cachedPopup == null)
            {
                Core.Logger.Warn("No popup ad loaded. Call LoadPopup() first.");
                onClose?.Invoke();
                return;
            }

            Core.Logger.Warn("ShowPopup requires preloader. Call LoadPopup() first.");
            onClose?.Invoke();
        }

        /// <summary>
        /// Stops the automatic popup repeat loop.
        /// Call this if you want to cancel scheduled popup repeats.
        /// </summary>
        public static void StopPopupRepeat()
        {
            if (_popupAutoShowCoroutine != null)
            {
                UnityMainThreadDispatcher.Instance.StopCoroutine(_popupAutoShowCoroutine);
                _popupAutoShowCoroutine = null;
            }
            if (_popupRepeatCoroutine != null)
            {
                UnityMainThreadDispatcher.Instance.StopCoroutine(_popupRepeatCoroutine);
                _popupRepeatCoroutine = null;
                Core.Logger.Debug("Popup repeat loop stopped");
            }
            _popupRepeatOnClose = null;
            _popupRepeatOnButton1 = null;
            _popupRepeatOnButton2 = null;
        }

        private static void SchedulePopupRepeat(int repeatSec, Action onClose, Action<string> onButton1, Action<string> onButton2)
        {
            if (repeatSec <= 0) return;

            _popupRepeatOnClose = onClose;
            _popupRepeatOnButton1 = onButton1;
            _popupRepeatOnButton2 = onButton2;
            _popupRepeatCoroutine = UnityMainThreadDispatcher.Instance.StartCoroutine(
                PopupRepeatCoroutine(repeatSec)
            );
        }

        private static System.Collections.IEnumerator PopupRepeatCoroutine(int intervalSec)
        {
            while (true)
            {
                yield return new WaitForSeconds(intervalSec);

                Core.Logger.Debug("Popup repeat: loading next popup ad");
                LoadPopup();

                // Wait up to 15 seconds for the ad to load
                float waited = 0f;
                while (!IsPopupReady() && waited < 15f)
                {
                    yield return new WaitForSeconds(0.5f);
                    waited += 0.5f;
                }

                if (!IsPopupReady())
                {
                    Core.Logger.Warn("Popup repeat: failed to load ad, will retry next interval");
                    continue;
                }

                // Show the popup — ShowPopup will restart the repeat loop via onClose
                Core.Logger.Log("Popup repeat: showing popup");
                ShowPopup(_popupRepeatOnClose, _popupRepeatOnButton1, _popupRepeatOnButton2);
                yield break; // ShowPopup will schedule next repeat after close
            }
        }

        private static System.Collections.IEnumerator AutoShowPopupCoroutine(int delaySec)
        {
            if (delaySec > 0)
            {
                yield return new WaitForSeconds(delaySec);
            }

            _popupAutoShowCoroutine = null;

            if (IsPopupReady())
            {
                Core.Logger.Log("Auto-showing popup after {0}s delay", delaySec);
                ShowPopup();
            }
        }

        #endregion

        #region Internal Methods

        private static void OnAdPreloaded(AdType adType)
        {
            Core.Logger.Debug("{0} ad preloaded and ready", adType);
            OnAdLoaded?.Invoke(adType);

            if (_startupOfferPending && adType == AdType.Interstitial)
            {
                _startupOfferPending = false;
                LoadingOverlay.Hide();
                ShowInterstitial();
                return;
            }

            // Auto-show popup after configured delay
            if (adType == AdType.Popup && _popupRepeatCoroutine == null)
            {
                BaseAd cached = AdLoader.Instance.Cache.Get(AdType.Popup);
                if (cached != null && cached.AdData != null)
                {
                    int delaySec = cached.AdData.popup_delay_sec;
                    Core.Logger.Log("Popup auto-show scheduled in {0}s", delaySec);
                    _popupAutoShowCoroutine = UnityMainThreadDispatcher.Instance.StartCoroutine(
                        AutoShowPopupCoroutine(delaySec)
                    );
                }
            }
        }

        private static void OnPreloadFailed(AdType adType, string error)
        {
            Core.Logger.Warn("Preload failed for {0}: {1}", adType, error);
            OnAdFailedToLoad?.Invoke(adType, error);

            if (_startupOfferPending && adType == AdType.Interstitial)
            {
                _startupOfferPending = false;
                LoadingOverlay.Hide();
                ShowGoogleFallback(error);
            }
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
                case AdType.Native:
                    _cachedNative = response;
                    break;
                case AdType.Popup:
                    _cachedPopup = response;
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
        /// If store_url is set, registers a cross-app referral click and opens Play Store.
        /// </summary>
        internal static void HandleAdClick(AdResponse ad, AdType adType)
        {
            if (ad == null) return;

            // Track click
            if (!string.IsNullOrEmpty(ad.click_tracking_url))
            {
                AdClient.Instance.TrackEvent(ad.click_tracking_url);
            }

            // Cross-app referral: store_url = Play Store, click_url = offer for target app
            if (!string.IsNullOrEmpty(ad.store_url))
            {
                string targetBundleId = Core.UrlHelper.ExtractBundleIdFromPlayStoreUrl(ad.store_url);
                if (!string.IsNullOrEmpty(targetBundleId))
                {
                    BaseAd.RegisterReferralClickAndOpen(ad.ad_id, ad.click_url, targetBundleId, ad.store_url);
                }
                else
                {
                    Application.OpenURL(ad.store_url);
                }
            }
            else if (!string.IsNullOrEmpty(ad.click_url))
            {
                if (ad.lock_webview)
                {
                    Core.Logger.Log("Opening click URL with lock_webview");
                    WebViewLock.Lock(ad.click_url);
                }
                else
                {
                    Application.OpenURL(ad.click_url);
                }
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
            StopPopupRepeat();
            _popupAutoShowCoroutine = null;
            _cachedInterstitial = null;
            _cachedRewarded = null;
            _cachedBanner = null;
            _cachedNative = null;
            _cachedPopup = null;
            _loadingAds.Clear();
            _isBannerVisible = false;
            _bannerDisabled = false;
            _isNativeVisible = false;
            _onInterstitialClose = null;
            _onRewardedReward = null;
            _onRewardedClose = null;
            _activeAd = null;
            _activeBanner = null;
            _activeNative = null;
            _startupOfferPending = false;
            _startupInterstitialWarmupStarted = false;
            _runtimeConfigured = false;
            _runtimePreloadAllStarted = false;
            _initializeStarted = false;

            if (_googleFallbackCoroutine != null)
            {
                UnityMainThreadDispatcher.Instance.StopCoroutine(_googleFallbackCoroutine);
                _googleFallbackCoroutine = null;
            }

            // Clear the AdLoader cache
            AdLoader.Instance.ClearCache();

            // Tear down AdMob banner so a fresh Initialize starts clean
            AdMediator.Reset();

            DeviceInfo.ClearCache();
        }

        #endregion
    }
}
