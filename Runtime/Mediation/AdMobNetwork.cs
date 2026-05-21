using System;
using System.Collections.Generic;
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
        private static ZeyWinAdsSettings _settings;

#if ZEYWIN_ADMOB
        private static InterstitialAd _interstitial;
        private static RewardedAd _rewarded;
        private static BannerView _banner;
        private static bool _bannerLoaded;
        private static bool _bannerVisible;
        private static AdPosition _currentBannerPosition = AdPosition.Bottom;
        private static Action _interstitialOnClose;
        private static Action<int> _rewardedOnReward;
        private static Action _rewardedOnClose;
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

        public static void Initialize(ZeyWinAdsSettings settings)
        {
            _settings = settings;

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

            ConfigureTestDevices();
            MobileAds.Initialize(status =>
            {
                _initialized = true;
                Core.Logger.Log("[AdMob] Initialized");
                PreloadInterstitial();
                PreloadRewarded();
                PreloadBanner();
            });
        }

        private static void ConfigureTestDevices()
        {
            var testDeviceIds = new List<string> { "E2D426604A92FFADE86351336AAC473E" };
            MobileAds.SetRequestConfiguration(new RequestConfiguration
            {
                TestDeviceIds = testDeviceIds
            });
            Core.Logger.Log("[AdMob] Test device ids configured: {0}", string.Join(",", testDeviceIds));
        }
#endif

        // ---------------- Interstitial ----------------

        public static bool IsInterstitialReady()
        {
#if ZEYWIN_ADMOB
            return _initialized && _interstitial != null && _interstitial.CanShowAd();
#else
            return false;
#endif
        }

        public static void PreloadInterstitial()
        {
#if ZEYWIN_ADMOB
            if (!_initialized || _settings == null) return;
            string unitId = _settings.GetInterstitialUnitId();
            if (string.IsNullOrEmpty(unitId)) return;

            if (_interstitial != null)
            {
                _interstitial.Destroy();
                _interstitial = null;
            }

            InterstitialAd.Load(unitId, new GoogleMobileAds.Api.AdRequest(), (ad, error) =>
            {
                if (error != null || ad == null)
                {
                    Core.Logger.Warn("[AdMob] Interstitial load failed: {0}", error?.GetMessage() ?? "null ad");
                    return;
                }
                _interstitial = ad;
                _interstitial.OnAdFullScreenContentClosed += () =>
                {
                    var cb = _interstitialOnClose;
                    _interstitialOnClose = null;
                    cb?.Invoke();
                    PreloadInterstitial();
                };
                _interstitial.OnAdFullScreenContentFailed += err =>
                {
                    Core.Logger.Warn("[AdMob] Interstitial show failed: {0}", err.GetMessage());
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
            if (!IsInterstitialReady()) return false;
            _interstitialOnClose = onClose;
            _interstitial.Show();
            return true;
#else
            return false;
#endif
        }

        // ---------------- Rewarded ----------------

        public static bool IsRewardedReady()
        {
#if ZEYWIN_ADMOB
            return _initialized && _rewarded != null && _rewarded.CanShowAd();
#else
            return false;
#endif
        }

        public static void PreloadRewarded()
        {
#if ZEYWIN_ADMOB
            if (!_initialized || _settings == null) return;
            string unitId = _settings.GetRewardedUnitId();
            if (string.IsNullOrEmpty(unitId)) return;

            if (_rewarded != null)
            {
                _rewarded.Destroy();
                _rewarded = null;
            }

            RewardedAd.Load(unitId, new GoogleMobileAds.Api.AdRequest(), (ad, error) =>
            {
                if (error != null || ad == null)
                {
                    Core.Logger.Warn("[AdMob] Rewarded load failed: {0}", error?.GetMessage() ?? "null ad");
                    return;
                }
                _rewarded = ad;
                _rewarded.OnAdFullScreenContentClosed += () =>
                {
                    var cb = _rewardedOnClose;
                    _rewardedOnClose = null;
                    _rewardedOnReward = null;
                    cb?.Invoke();
                    PreloadRewarded();
                };
                _rewarded.OnAdFullScreenContentFailed += err =>
                {
                    Core.Logger.Warn("[AdMob] Rewarded show failed: {0}", err.GetMessage());
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
            if (!IsRewardedReady()) return false;
            _rewardedOnReward = onReward;
            _rewardedOnClose = onClose;
            _rewarded.Show(reward =>
            {
                int amount = reward != null ? Mathf.RoundToInt((float)reward.Amount) : ZeyWinAdsConfig.DefaultRewardAmount;
                _rewardedOnReward?.Invoke(amount);
            });
            return true;
#else
            return false;
#endif
        }

        // ---------------- Banner ----------------

        public static bool IsBannerReady()
        {
#if ZEYWIN_ADMOB
            return _initialized && _banner != null && _bannerLoaded;
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
            string unitId = _settings.GetBannerUnitId();
            if (string.IsNullOrEmpty(unitId)) return;

            if (_banner != null)
            {
                _banner.Destroy();
                _banner = null;
            }
            _bannerLoaded = false;

            _banner = new BannerView(unitId, AdSize.Banner, _currentBannerPosition);
            _banner.OnBannerAdLoaded += () =>
            {
                _bannerLoaded = true;
                Core.Logger.Log("[AdMob] Banner loaded");
                if (!_bannerVisible)
                    _banner.Hide();
            };
            _banner.OnBannerAdLoadFailed += err =>
            {
                _bannerLoaded = false;
                Core.Logger.Warn("[AdMob] Banner load failed: {0}", err.GetMessage());
            };
            _banner.LoadAd(new GoogleMobileAds.Api.AdRequest());
#endif
        }

        public static bool ShowBanner(BannerPosition position)
        {
#if ZEYWIN_ADMOB
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
            _bannerLoaded = false;
            _bannerVisible = false;
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
            if (_rewarded != null)
            {
                _rewarded.Destroy();
                _rewarded = null;
            }
            _interstitialOnClose = null;
            _rewardedOnReward = null;
            _rewardedOnClose = null;
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
