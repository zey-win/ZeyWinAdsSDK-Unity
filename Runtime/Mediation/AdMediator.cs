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
            // Make sure AdMob banner isn't lingering on screen.
            AdMobNetwork.HideBanner();
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
            _initialized = false;
        }
    }
}
