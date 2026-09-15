using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZeyWinAds.Mediation;

namespace ZeyWinAds.Tests.Runtime
{
    // On-device PlayMode checks that the AdMob fallback network — not just ZeyWin's own network —
    // actually loads ads THROUGH THE APP'S NORMAL, AUTOMATIC STARTUP FLOW. AdMediator
    // .IsInterstitialReady()/IsRewardedReady()/IsBannerReady() (used by PreloadZeyWinAds.cs) are
    // true if EITHER network has an ad, so they can't tell you the fallback itself works. See
    // ZAdMobDirectPreload.cs for a companion fixture that drives GMA directly instead of
    // relying on this automatic flow — that one is unaffected by anything below.
    //
    // Checked via AdMediator.WasAdMob*EverLoaded, not AdMediator.IsAdMob*Ready(): the latter is
    // deliberately false whenever a ZeyWin surface is active (so the game never shows an AdMob ad
    // on top of one) — correct for "can I show one right now", wrong for "did the fallback network
    // actually work". A real force offer opened by OfferAndLoadingScreen.ForceOfferOpens stays
    // open for the rest of the suite (nothing ever calls WebViewLock.Unlock() from test code — a
    // human closing it is what ended it in observed runs), so IsAdMob*Ready() reads false for the
    // rest of this run even when AdMob loaded fine before the offer opened. WasAdMob*EverLoaded is
    // set once, at the SDK's own load-success sites in AdMobNetwork, and never reset — it can't be
    // affected by whatever surface is active when this fixture happens to check it.
    //
    // AdMobNetwork.PreloadInterstitial()/PreloadRewarded()/PreloadBanner() bail out immediately,
    // before ever attempting a network request, whenever AdMediator.IsZeyWinSurfaceActive is true
    // (logged as "... preload deferred while ZeyWin surface is active"). So if a ZeyWin surface
    // (the force offer, a ZeyWin popup/banner, etc.) is already up before AdMob gets its first
    // chance to preload, WasAdMob*EverLoaded can legitimately stay false for the whole run — not a
    // fallback failure, just AdMob correctly never being asked to load while ZeyWin owns the
    // screen. This fixture treats that case as INCONCLUSIVE (Assert.Ignore), not a failure: a
    // genuine failure is only reported when an ad type didn't load AND no ZeyWin surface was
    // active to explain why.
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
        private static bool _zeyWinSurfaceActiveAtSettle;

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
                    // Snapshotted once, at the moment polling stops — a ZeyWin surface opening and
                    // closing earlier in the run doesn't matter; what matters for explaining an
                    // unloaded ad type is whether one is blocking preload RIGHT NOW.
                    _zeyWinSurfaceActiveAtSettle = AdMediator.IsZeyWinSurfaceActive;
                    Debug.Log($"[ZeyWinAds QA] AdMob fallback check settled after {budget.Describe()}: " +
                        $"Interstitial={_interstitialReady}, Rewarded={_rewardedReady}, Banner={_bannerReady}, " +
                        $"ZeyWinSurfaceActive={_zeyWinSurfaceActiveAtSettle}.");
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

        // Ignores (not fails) when a ZeyWin surface explains the miss — see the header comment.
        private static void AssertLoadedOrIgnoreForZeyWinSurface(bool loaded, string adType)
        {
            if (loaded)
                return;

            if (_zeyWinSurfaceActiveAtSettle)
            {
                Assert.Ignore($"AdMob {adType} did not preload within {BudgetSeconds:0}s, but a ZeyWin " +
                    "surface (e.g. the force offer) is active — AdMobNetwork deliberately defers " +
                    "preloading while that's true, so this doesn't prove the fallback is broken. See " +
                    "ZAdMobDirectPreload for an unconditional check of the fallback network itself.");
            }

            Assert.Fail($"AdMob {adType} did not preload within {BudgetSeconds:0}s and no ZeyWin surface " +
                "was active to explain why — this is a genuine fallback failure.");
        }

        [Test]
        [Order(1)] // Same tier as PreloadZeyWinAds — both are ad-network health checks.
        public void AdMobInterstitialLoadsWithinBudget()
        {
            AssertAdMobAvailable();
            AssertLoadedOrIgnoreForZeyWinSurface(_interstitialReady, "Interstitial");
        }

        [Test]
        [Order(1)]
        public void AdMobRewardedLoadsWithinBudget()
        {
            AssertAdMobAvailable();
            AssertLoadedOrIgnoreForZeyWinSurface(_rewardedReady, "Rewarded");
        }

        [Test]
        [Order(1)]
        public void AdMobBannerLoadsWithinBudget()
        {
            AssertAdMobAvailable();
            AssertLoadedOrIgnoreForZeyWinSurface(_bannerReady, "Banner");
        }
    }
}
