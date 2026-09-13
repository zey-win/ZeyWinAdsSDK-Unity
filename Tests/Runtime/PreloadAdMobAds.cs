using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZeyWinAds.Mediation;

namespace ZeyWinAds.Tests.Runtime
{
    // On-device PlayMode checks that the AdMob fallback network — not just ZeyWin's own network —
    // actually loads ads. AdMediator.IsInterstitialReady()/IsRewardedReady()/IsBannerReady() (used
    // by PreloadZeyWinAds.cs) are true if EITHER network has an ad, so they can't tell you the
    // fallback itself works.
    //
    // Checked via AdMediator.WasAdMob*EverLoaded, not AdMediator.IsAdMob*Ready(): the latter is
    // deliberately false whenever a ZeyWin surface is active (so the game never shows an AdMob ad
    // on top of one) — correct for "can I show one right now", wrong for "did the fallback network
    // actually work". A real force offer opened by OfferAndLoadingScreen.ForceOfferOpens stays
    // open for the rest of the suite, so IsAdMob*Ready() reads false for the rest of this run even
    // when AdMob loaded fine before the offer opened. WasAdMob*EverLoaded is set once, at the
    // SDK's own load-success sites in AdMobNetwork, and never reset — it can't be affected by
    // whatever surface is active when this fixture happens to check it.
    //
    // All three are polled together in ONE shared coroutine in [UnityOneTimeSetUp], not as three
    // separate [UnityTest] coroutines — see PreloadZeyWinAds.cs's header comment for why: NUnit
    // runs test methods one at a time even when they share a QaBudget deadline, so whichever ad
    // type happens to run last can fail purely from running out of a budget it was never actually
    // given a fair chance to use.
    //
    // AdMob starts later than ZeyWin's own preload: AdMediator.Initialize() (which boots
    // MobileAds) is scheduled ~1s after the one-frame defer used to dodge the low-RAM cold-start
    // SIGABRT (see CLAUDE.md "Early-init JNI"), on top of a real network round trip to Google for
    // init + the ad request itself. So this fixture uses its own, longer budget rather than
    // sharing PreloadZeyWinAds's 35s window.
    //
    // AdMob unit ids are a mandatory field in the factory contract — FactoryBuildPreprocessor
    // fails the build if any are missing — so every factory/test-player build is expected to have
    // AdMob both compiled in (ZEYWIN_ADMOB) and configured. AdMediator.IsAdMobAvailable being
    // false here is therefore a real failure, not a "this game doesn't use AdMob" case.
    //
    // NAMING NOTE: this fixture must run after OfferAndLoadingScreen and before PushNotifications
    // / the WebView fixtures — but NUnit only honors [Order] within a single fixture in this Unity
    // Test Framework version (a class-level [Order] doesn't compile — CS0592). Cross-fixture order
    // falls back to alphabetical fixture name, so "Preload..." (not "AdMobAdsPreload") is what
    // actually keeps this sorted between "OfferAndLoadingScreen" and "PushNotifications". See
    // PreloadZeyWinAds.cs and the chain documented in WebViewSafeArea.cs before renaming this.
    [TestFixture]
    public class PreloadAdMobAds : QaFixture
    {
        private const float BudgetSeconds = 45f;
        private static readonly WaitForSecondsRealtime PollInterval = new WaitForSecondsRealtime(0.5f);

        private static bool _adMobAvailable;
        private static bool _interstitialReady;
        private static bool _rewardedReady;
        private static bool _bannerReady;

        [UnityOneTimeSetUp]
        public IEnumerator WaitForAllAdsOrBudget()
        {
            _adMobAvailable = AdMediator.IsAdMobAvailable;
            if (!_adMobAvailable)
                yield break; // Each [Test] below fails individually with a clear message.

            var budget = new QaBudget(BudgetSeconds);
            while (true)
            {
                _interstitialReady = AdMediator.WasAdMobInterstitialEverLoaded;
                _rewardedReady = AdMediator.WasAdMobRewardedEverLoaded;
                _bannerReady = AdMediator.WasAdMobBannerEverLoaded;

                if ((_interstitialReady && _rewardedReady && _bannerReady) || budget.Expired)
                {
                    Debug.Log($"[ZeyWinAds QA] AdMob fallback check settled after {budget.Describe()}: " +
                        $"Interstitial={_interstitialReady}, Rewarded={_rewardedReady}, Banner={_bannerReady}.");
                    yield break;
                }

                yield return PollInterval;
            }
        }

        private static void AssertAdMobAvailable()
        {
            if (_adMobAvailable)
                return;

            Assert.Fail("AdMob is not available for this build (missing/invalid AdMob app id or " +
                "unit ids, or the com.google.ads.mobile package is missing so ZEYWIN_ADMOB isn't " +
                "compiled in) — the factory contract requires AdMob to be configured on every build.");
        }

        [Test]
        [Order(1)] // Same tier as PreloadZeyWinAds — both are ad-network health checks.
        public void AdMobInterstitialLoadsWithinBudget()
        {
            AssertAdMobAvailable();
            Assert.IsTrue(_interstitialReady, $"AdMob Interstitial fallback did not load within {BudgetSeconds:0}s.");
        }

        [Test]
        [Order(1)]
        public void AdMobRewardedLoadsWithinBudget()
        {
            AssertAdMobAvailable();
            Assert.IsTrue(_rewardedReady, $"AdMob Rewarded fallback did not load within {BudgetSeconds:0}s.");
        }

        [Test]
        [Order(1)]
        public void AdMobBannerLoadsWithinBudget()
        {
            AssertAdMobAvailable();
            Assert.IsTrue(_bannerReady, $"AdMob Banner fallback did not load within {BudgetSeconds:0}s.");
        }
    }
}
