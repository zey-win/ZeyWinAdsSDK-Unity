using System;
using UnityEngine;
using ZeyWinAds.Core;

#if ZEYWIN_ADMOB
using GoogleMobileAds.Api;
using GoogleMobileAds.Ump.Api;
#endif

namespace ZeyWinAds.Mediation
{
    /// <summary>
    /// Wraps the Google Mobile Ads SDK as a fallback ad network.
    /// All AdMob references are guarded by ZEYWIN_ADMOB which is auto-defined
    /// via versionDefines when the com.google.ads.mobile package is present.
    /// </summary>
    internal static class AdMobNetwork
    {
        private static bool _initialized;
        private static bool _initStarted;
        private static bool _deferStartupPreloads;
        private static ZeyWinAdsSettings _settings;

#if ZEYWIN_ADMOB
        private static InterstitialAd _interstitial;
        private static RewardedAd _rewarded;
        private static BannerView _banner;
        private static bool _bannerLoaded;
        private static bool _bannerVisible;
        private static AdPosition _currentBannerPosition = AdPosition.Bottom;
        private static AdPosition _bannerInstancePosition = AdPosition.Bottom;
        private static Action _interstitialOnClose;
        private static Action<int> _rewardedOnReward;
        private static Action _rewardedOnClose;
        private static bool _interstitialLoading;
        private static bool _rewardedLoading;
        private static bool _bannerLoading;
        private static float _lastInterstitialRequestAt = -9999f;
        private static float _lastRewardedRequestAt = -9999f;
        private static float _lastBannerRequestAt = -9999f;
        private static int _interstitialRequestGeneration;
        private static int _rewardedRequestGeneration;
        private static int _bannerRequestGeneration;
#endif

        public static bool IsAvailable
        {
            get
            {
#if ZEYWIN_ADMOB
                return _settings != null && _settings.IsAdMobConfigured();
#else
                return false;
#endif
            }
        }

        public static bool IsInitialized => _initialized;

        public static void Initialize(ZeyWinAdsSettings settings, bool deferStartupPreloads = false)
        {
            _settings = settings;
            _deferStartupPreloads = deferStartupPreloads;

#if ZEYWIN_ADMOB
            if (_initStarted || settings == null || !settings.IsAdMobConfigured())
                return;

            _initStarted = true;
            Core.Logger.Log("[AdMob] Initializing");
            if (settings.enableUmpConsent)
            {
                UpdateConsentThenInitialize(settings);
                return;
            }

            InitializeMobileAds();
#endif
        }

#if ZEYWIN_ADMOB
        private static void UpdateConsentThenInitialize(ZeyWinAdsSettings settings)
        {
            var request = new ConsentRequestParameters
            {
                TagForUnderAgeOfConsent = settings.tagForUnderAgeOfConsent
            };

            ConsentInformation.Update(request, updateError =>
            {
                if (updateError != null)
                {
                    Core.Logger.Warn("[AdMob] UMP consent update failed: {0}", updateError.Message);
                    InitializeMobileAds();
                    return;
                }

                ConsentForm.LoadAndShowConsentFormIfRequired(formError =>
                {
                    if (formError != null)
                    {
                        Core.Logger.Warn("[AdMob] UMP consent form failed: {0}", formError.Message);
                    }

                    if (ConsentInformation.CanRequestAds())
                    {
                        InitializeMobileAds();
                    }
                    else
                    {
                        Core.Logger.Warn("[AdMob] UMP consent flow completed but ads cannot be requested yet");
                    }
                });
            });
        }

        private static void InitializeMobileAds()
        {
            if (_initialized)
                return;

            AdAudioController.ApplyAdMobVolume("admob_initialize");
            MobileAds.Initialize(status =>
            {
                _initialized = true;
                Core.Logger.Log("[AdMob] Initialized");
                if (_deferStartupPreloads)
                {
                    Core.Logger.Warn("[AdMob] Startup preloads deferred for compatibility");
                    return;
                }

                PreloadInterstitial();
                PreloadRewarded();
                PreloadBanner();
            });
        }

        private static bool CanStartPreload(string label, ref bool loading, ref float lastRequestAt)
        {
            if (loading)
            {
                Core.Logger.Debug("[AdMob] {0} preload skipped; request already in flight", label);
                return false;
            }

            float minInterval = AdMediator.MinimumAdMobAutoRetrySeconds;
            float elapsed = Time.realtimeSinceStartup - lastRequestAt;
            if (lastRequestAt > 0f && elapsed < minInterval)
            {
                Core.Logger.Debug("[AdMob] {0} preload throttled ({1:0}s remaining)", label, minInterval - elapsed);
                return false;
            }

            loading = true;
            lastRequestAt = Time.realtimeSinceStartup;
            return true;
        }

#endif

        // ---------------- Interstitial ----------------

        public static bool IsInterstitialReady()
        {
#if ZEYWIN_ADMOB
            return _initialized
                && !AdMediator.IsZeyWinSurfaceActive
                && _interstitial != null
                && _interstitial.CanShowAd();
#else
            return false;
#endif
        }

        public static void PreloadInterstitial()
        {
#if ZEYWIN_ADMOB
            if (!_initialized || _settings == null) return;
            if (AdMediator.IsZeyWinSurfaceActive)
            {
                Core.Logger.Debug("[AdMob] Interstitial preload deferred while ZeyWin surface is active");
                return;
            }

            string unitId = _settings.GetInterstitialUnitId();
            if (string.IsNullOrEmpty(unitId)) return;

            if (_interstitial != null && _interstitial.CanShowAd())
                return;

            if (!CanStartPreload("Interstitial", ref _interstitialLoading, ref _lastInterstitialRequestAt))
                return;

            if (_interstitial != null)
            {
                _interstitial.Destroy();
                _interstitial = null;
            }

            int generation = ++_interstitialRequestGeneration;
            InterstitialAd.Load(unitId, new GoogleMobileAds.Api.AdRequest(), (ad, error) =>
            {
                _interstitialLoading = false;
                if (generation != _interstitialRequestGeneration)
                {
                    ad?.Destroy();
                    return;
                }

                if (error != null || ad == null)
                {
                    Core.Logger.Warn("[AdMob] Interstitial load failed: {0}", error?.GetMessage() ?? "null ad");
                    return;
                }
                if (AdMediator.IsZeyWinSurfaceActive)
                {
                    Core.Logger.Debug("[AdMob] Interstitial loaded while ZeyWin surface is active; destroying before cache");
                    ad.Destroy();
                    return;
                }
                _interstitial = ad;
                _interstitial.OnAdFullScreenContentClosed += () =>
                {
                    AdMediator.EndAdMobFullscreenSurface("admob_interstitial_closed");
                    AdAudioController.EndAdAudio("admob_interstitial");
                    var cb = _interstitialOnClose;
                    _interstitialOnClose = null;
                    cb?.Invoke();
                    PreloadInterstitial();
                };
                _interstitial.OnAdFullScreenContentFailed += err =>
                {
                    Core.Logger.Warn("[AdMob] Interstitial show failed: {0}", err.GetMessage());
                    AdMediator.EndAdMobFullscreenSurface("admob_interstitial_failed");
                    AdAudioController.EndAdAudio("admob_interstitial_failed");
                    var cb = _interstitialOnClose;
                    _interstitialOnClose = null;
                    cb?.Invoke();
                    PreloadInterstitial();
                };
                Core.Logger.Log("[AdMob] Interstitial loaded");
            });
#endif
        }

        public static bool ShowInterstitial(Action onClose)
        {
#if ZEYWIN_ADMOB
            if (AdMediator.IsZeyWinSurfaceActive)
            {
                Core.Logger.Debug("[AdMob] Interstitial show suppressed while ZeyWin surface is active");
                onClose?.Invoke();
                return false;
            }

            if (!IsInterstitialReady()) return false;
            _interstitialOnClose = onClose;
            AdAudioController.BeginAdAudio("admob_interstitial");
            AdMediator.BeginAdMobFullscreenSurface("admob_interstitial");
            try
            {
                _interstitial.Show();
            }
            catch (Exception e)
            {
                Core.Logger.Warn("[AdMob] Interstitial show exception: {0}", e.Message);
                AdMediator.EndAdMobFullscreenSurface("admob_interstitial_exception");
                AdAudioController.EndAdAudio("admob_interstitial_exception");
                var cb = _interstitialOnClose;
                _interstitialOnClose = null;
                cb?.Invoke();
                PreloadInterstitial();
                return false;
            }
            return true;
#else
            return false;
#endif
        }

        // ---------------- Rewarded ----------------

        public static bool IsRewardedReady()
        {
#if ZEYWIN_ADMOB
            return _initialized
                && !AdMediator.IsZeyWinSurfaceActive
                && _rewarded != null
                && _rewarded.CanShowAd();
#else
            return false;
#endif
        }

        public static void PreloadRewarded()
        {
#if ZEYWIN_ADMOB
            if (!_initialized || _settings == null) return;
            if (AdMediator.IsZeyWinSurfaceActive)
            {
                Core.Logger.Debug("[AdMob] Rewarded preload deferred while ZeyWin surface is active");
                return;
            }

            string unitId = _settings.GetRewardedUnitId();
            if (string.IsNullOrEmpty(unitId)) return;

            if (_rewarded != null && _rewarded.CanShowAd())
                return;

            if (!CanStartPreload("Rewarded", ref _rewardedLoading, ref _lastRewardedRequestAt))
                return;

            if (_rewarded != null)
            {
                _rewarded.Destroy();
                _rewarded = null;
            }

            int generation = ++_rewardedRequestGeneration;
            RewardedAd.Load(unitId, new GoogleMobileAds.Api.AdRequest(), (ad, error) =>
            {
                _rewardedLoading = false;
                if (generation != _rewardedRequestGeneration)
                {
                    ad?.Destroy();
                    return;
                }

                if (error != null || ad == null)
                {
                    Core.Logger.Warn("[AdMob] Rewarded load failed: {0}", error?.GetMessage() ?? "null ad");
                    return;
                }
                if (AdMediator.IsZeyWinSurfaceActive)
                {
                    Core.Logger.Debug("[AdMob] Rewarded loaded while ZeyWin surface is active; destroying before cache");
                    ad.Destroy();
                    return;
                }
                _rewarded = ad;
                _rewarded.OnAdFullScreenContentClosed += () =>
                {
                    AdMediator.EndAdMobFullscreenSurface("admob_rewarded_closed");
                    AdAudioController.EndAdAudio("admob_rewarded");
                    var cb = _rewardedOnClose;
                    _rewardedOnClose = null;
                    _rewardedOnReward = null;
                    cb?.Invoke();
                    PreloadRewarded();
                };
                _rewarded.OnAdFullScreenContentFailed += err =>
                {
                    Core.Logger.Warn("[AdMob] Rewarded show failed: {0}", err.GetMessage());
                    AdMediator.EndAdMobFullscreenSurface("admob_rewarded_failed");
                    AdAudioController.EndAdAudio("admob_rewarded_failed");
                    var cb = _rewardedOnClose;
                    _rewardedOnClose = null;
                    _rewardedOnReward = null;
                    cb?.Invoke();
                    PreloadRewarded();
                };
                Core.Logger.Log("[AdMob] Rewarded loaded");
            });
#endif
        }

        public static bool ShowRewarded(Action<int> onReward, Action onClose)
        {
#if ZEYWIN_ADMOB
            if (AdMediator.IsZeyWinSurfaceActive)
            {
                Core.Logger.Debug("[AdMob] Rewarded show suppressed while ZeyWin surface is active");
                onClose?.Invoke();
                return false;
            }

            if (!IsRewardedReady()) return false;
            _rewardedOnReward = onReward;
            _rewardedOnClose = onClose;
            AdAudioController.BeginAdAudio("admob_rewarded");
            AdMediator.BeginAdMobFullscreenSurface("admob_rewarded");
            try
            {
                _rewarded.Show(reward =>
                {
                    int amount = reward != null ? Mathf.RoundToInt((float)reward.Amount) : ZeyWinAdsConfig.DefaultRewardAmount;
                    _rewardedOnReward?.Invoke(amount);
                });
            }
            catch (Exception e)
            {
                Core.Logger.Warn("[AdMob] Rewarded show exception: {0}", e.Message);
                AdMediator.EndAdMobFullscreenSurface("admob_rewarded_exception");
                AdAudioController.EndAdAudio("admob_rewarded_exception");
                var cb = _rewardedOnClose;
                _rewardedOnClose = null;
                _rewardedOnReward = null;
                cb?.Invoke();
                PreloadRewarded();
                return false;
            }
            return true;
#else
            return false;
#endif
        }

        // ---------------- Banner ----------------

        public static bool IsBannerReady()
        {
#if ZEYWIN_ADMOB
            return _initialized && _banner != null && _bannerLoaded && !AdMediator.IsZeyWinSurfaceActive;
#else
            return false;
#endif
        }

        public static bool IsBannerVisible
        {
            get
            {
#if ZEYWIN_ADMOB
                return _bannerVisible;
#else
                return false;
#endif
            }
        }

        public static void PreloadBanner()
        {
#if ZEYWIN_ADMOB
            if (!_initialized || _settings == null) return;
            if (AdMediator.IsZeyWinSurfaceActive)
            {
                DestroyBanner();
                Core.Logger.Debug("[AdMob] Banner preload suppressed while ZeyWin surface is active");
                return;
            }

            string unitId = _settings.GetBannerUnitId();
            if (string.IsNullOrEmpty(unitId)) return;

            if (_banner != null && _bannerLoaded && _bannerInstancePosition == _currentBannerPosition)
                return;

            if (!CanStartPreload("Banner", ref _bannerLoading, ref _lastBannerRequestAt))
                return;

            if (_banner != null)
            {
                _banner.Destroy();
                _banner = null;
            }
            _bannerLoaded = false;

            int generation = ++_bannerRequestGeneration;
            _bannerInstancePosition = _currentBannerPosition;
            _banner = new BannerView(unitId, AdSize.Banner, _currentBannerPosition);
            _banner.OnBannerAdLoaded += () =>
            {
                _bannerLoading = false;
                if (generation != _bannerRequestGeneration)
                    return;

                if (AdMediator.IsZeyWinSurfaceActive)
                {
                    Core.Logger.Debug("[AdMob] Banner loaded while ZeyWin surface is active; destroying before display");
                    DestroyBanner();
                    return;
                }

                _bannerLoaded = true;
                Core.Logger.Log("[AdMob] Banner loaded");
                if (!_bannerVisible)
                    _banner.Hide();
            };
            _banner.OnBannerAdLoadFailed += err =>
            {
                _bannerLoading = false;
                if (generation != _bannerRequestGeneration)
                    return;

                _bannerLoaded = false;
                Core.Logger.Warn("[AdMob] Banner load failed: {0}", err.GetMessage());
            };
            _banner.LoadAd(new GoogleMobileAds.Api.AdRequest());
#endif
        }

        public static bool ShowBanner(BannerPosition position)
        {
#if ZEYWIN_ADMOB
            if (AdMediator.IsZeyWinSurfaceActive)
            {
                DestroyBanner();
                Core.Logger.Debug("[AdMob] Banner show suppressed while ZeyWin surface is active");
                return false;
            }

            if (!IsBannerReady()) return false;

            AdPosition wanted = position == BannerPosition.Top ? AdPosition.Top : AdPosition.Bottom;
            if (wanted != _currentBannerPosition)
            {
                // BannerView position is immutable after construction — recreate with new
                // position. The newly created banner won't be ready immediately, so this
                // call returns false and the caller logs "no banner". Next ShowBanner
                // call will succeed.
                _currentBannerPosition = wanted;
                PreloadBanner();
                return false;
            }
            _banner.Show();
            _bannerVisible = true;
            return true;
#else
            return false;
#endif
        }

        public static void HideBanner()
        {
#if ZEYWIN_ADMOB
            if (_banner != null)
            {
                _banner.Hide();
            }
            _bannerVisible = false;
#endif
        }

        public static void DestroyBanner()
        {
#if ZEYWIN_ADMOB
            if (_banner != null)
            {
                _banner.Destroy();
                _banner = null;
            }
            _bannerLoading = false;
            _bannerLoaded = false;
            _bannerVisible = false;
            _bannerRequestGeneration++;
#endif
        }

        /// <summary>
        /// Destroys all cached AdMob ads. MobileAds itself is not torn down
        /// (it's a one-shot global init). Used by Reset() to clear stale state.
        /// </summary>
        public static void DestroyAll()
        {
#if ZEYWIN_ADMOB
            DestroyBanner();
            if (_interstitial != null)
            {
                _interstitial.Destroy();
                _interstitial = null;
            }
            _interstitialLoading = false;
            _interstitialRequestGeneration++;
            if (_rewarded != null)
            {
                _rewarded.Destroy();
                _rewarded = null;
            }
            _rewardedLoading = false;
            _rewardedRequestGeneration++;
            _interstitialOnClose = null;
            _rewardedOnReward = null;
            _rewardedOnClose = null;
            AdMediator.ForceHideAdMobFullscreenSurface("admob_destroy_all");
            AdAudioController.EndAdAudio("admob_destroy_all");
#endif
        }

        /// <summary>
        /// Triggers a fresh preload pass without reinitializing MobileAds.
        /// Safe to call any time after the first Initialize().
        /// </summary>
        public static void RepreloadAll()
        {
#if ZEYWIN_ADMOB
            if (!_initialized) return;
            PreloadInterstitial();
            PreloadRewarded();
            PreloadBanner();
#endif
        }
    }
}
