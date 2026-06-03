using System;
using ZeyWinAds.Core;

namespace ZeyWinAds.Mediation
{
    /// <summary>
    /// Routes ad show requests: ZeyWin first, AdMob as fallback.
    /// Both networks preload in parallel from Initialize() so a fallback
    /// is ready the moment ZeyWin doesn't have an ad.
    /// </summary>
    public static class AdMediator
    {
        private static bool _initialized;

        /// <summary>
        /// Tracks which network is currently rendering the banner so HideBanner
        /// hits the correct one. Banner ads stay visible until hidden.
        /// </summary>
        public enum BannerSource { None, ZeyWin, AdMob }
        public static BannerSource ActiveBannerSource { get; private set; } = BannerSource.None;
        private static int _zeyWinSurfaceDepth;

        /// <summary>
        /// True while any ZeyWin-owned surface that must sit above banners is visible.
        /// This covers WebView offers, locked WebViews, SDK popups, and ZeyWin banners.
        /// </summary>
        public static bool IsZeyWinSurfaceActive => _zeyWinSurfaceDepth > 0 || ActiveBannerSource == BannerSource.ZeyWin;

        /// <summary>
        /// Called from ZeyWinAds.Initialize after the ad client is ready.
        /// Boots AdMob in parallel — failure is silently logged, ZeyWin keeps working.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            var settings = ZeyWinAdsSettings.Load();
            if (settings == null)
            {
                Logger.Debug("[Mediator] No ZeyWinAdsSettings asset — AdMob fallback disabled");
                return;
            }

            // First call boots MobileAds + preloads in its init callback.
            // Re-init after Reset() does not re-init MobileAds (one-shot), so
            // only then do we explicitly re-trigger preloads.
            bool wasAdMobInitialized = AdMobNetwork.IsInitialized;
            AdMobNetwork.Initialize(settings);
            if (wasAdMobInitialized)
                AdMobNetwork.RepreloadAll();
        }

        // ---------------- Interstitial ----------------

        // ---------------- Interstitial ----------------

        public static bool IsInterstitialReady()
        {
            return AdLoader.Instance.IsAdReady(AdType.Interstitial)
                || AdMobNetwork.IsInterstitialReady();
        }

        public static bool IsAdMobInterstitialReady() => AdMobNetwork.IsInterstitialReady();

        public static void ShowAdMobInterstitial(Action onClose) => AdMobNetwork.ShowInterstitial(onClose);

        // ---------------- Rewarded ----------------

        public static bool IsRewardedReady()
        {
            return AdLoader.Instance.IsAdReady(AdType.Rewarded)
                || AdMobNetwork.IsRewardedReady();
        }

        public static bool IsAdMobRewardedReady() => AdMobNetwork.IsRewardedReady();

        public static void ShowAdMobRewarded(Action<int> onReward, Action onClose)
            => AdMobNetwork.ShowRewarded(onReward, onClose);

        // ---------------- Banner ----------------

        public static bool IsBannerReady()
        {
            return AdLoader.Instance.IsAdReady(AdType.Banner)
                || AdMobNetwork.IsBannerReady();
        }

        public static bool IsAdMobBannerReady() => AdMobNetwork.IsBannerReady();

        /// <summary>
        /// Shows the AdMob banner. Returns false if the loaded banner is at a different
        /// position than requested (a reload is triggered; caller should retry on next tick).
        /// </summary>
        public static bool ShowAdMobBanner(BannerPosition position)
        {
            if (IsZeyWinSurfaceActive)
            {
                AdMobNetwork.DestroyBanner();
                Logger.Debug("[Mediator] AdMob banner suppressed while ZeyWin surface is active");
                return false;
            }

            if (AdMobNetwork.ShowBanner(position))
            {
                ActiveBannerSource = BannerSource.AdMob;
                return true;
            }
            return false;
        }

        public static void OnZeyWinBannerShown()
        {
            ActiveBannerSource = BannerSource.ZeyWin;
            // Make sure AdMob banner is not just hidden but fully detached, so
            // Google cannot record an impression under a ZeyWin surface.
            AdMobNetwork.DestroyBanner();
        }

        public static void BeginZeyWinSurface(string reason)
        {
            _zeyWinSurfaceDepth++;
            AdMobNetwork.DestroyBanner();
            Logger.Debug("[Mediator] ZeyWin surface began: {0}", string.IsNullOrEmpty(reason) ? "unknown" : reason);
        }

        public static void EndZeyWinSurface(string reason)
        {
            if (_zeyWinSurfaceDepth > 0)
                _zeyWinSurfaceDepth--;

            Logger.Debug("[Mediator] ZeyWin surface ended: {0}", string.IsNullOrEmpty(reason) ? "unknown" : reason);
            if (!IsZeyWinSurfaceActive && _initialized)
                AdMobNetwork.PreloadBanner();
        }

        public static void HideBanner()
        {
            if (ActiveBannerSource == BannerSource.AdMob)
            {
                AdMobNetwork.HideBanner();
            }
            ActiveBannerSource = BannerSource.None;
        }

        /// <summary>
        /// Full teardown — destroys all AdMob ad instances. MobileAds stays
        /// initialized (it's a one-shot global). The next Initialize() call
        /// will re-trigger preloads via RepreloadAll().
        /// </summary>
        public static void Reset()
        {
            AdMobNetwork.DestroyAll();
            ActiveBannerSource = BannerSource.None;
            _zeyWinSurfaceDepth = 0;
            _initialized = false;
        }
    }
}
