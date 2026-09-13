using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZeyWinAds.Mediation;

#if ZEYWIN_ADMOB
using GoogleMobileAds.Api;
#endif

namespace ZeyWinAds.Tests.Runtime
{
    // Companion to PreloadAdMobAds.cs. That fixture checks whether AdMob preloads through the
    // app's own automatic startup flow — and correctly treats "a ZeyWin surface is up" as a
    // reason to ignore rather than fail, because AdMobNetwork.Preload*() deliberately bails out
    // while AdMediator.IsZeyWinSurfaceActive is true.
    //
    // This fixture answers a different question: is AdMob/GMA itself actually capable of loading
    // an ad on this build + device + these unit ids, at all? It loads its own InterstitialAd /
    // RewardedAd / BannerView instances directly via the GMA API, using the same unit ids as
    // production (ZeyWinAdsSettings) but entirely separate ad instances from AdMobNetwork's — it
    // never calls AdMobNetwork.Preload*() or touches AdMediator's gating, so a real force offer
    // holding a ZeyWin surface open for the rest of the suite (see PreloadAdMobAds.cs's header)
    // cannot cause this one to be skipped or to report a false negative.
    //
    // This is a network + configuration check, not a check of our own mediation logic. It proves
    // "GMA can load with these unit ids right now" independent of whatever the app's own timing or
    // surface state happens to be. The ad instances created here are never shown, and each is
    // destroyed the instant its own load callback fires (success or failure) — not held alive
    // until OneTimeTearDown. A loaded interstitial/rewarded ad can carry real video + audio
    // creative, which spins up genuine VP9/codec + AAudio native resources; holding 3 of them
    // alive for the rest of this fixture's ~45s budget was observed (via logcat: CCodec video
    // decoder + AAudioStreamBuilder activity overlapping a WebView redirect-chain test's window)
    // to plausibly starve/delay unrelated native WebView callbacks running elsewhere in the same
    // process at the same time — see the NAMING NOTE below for the other half of that fix.
    //
    // ASMDEF NOTE: this is the only file in this assembly that calls the raw GMA API directly, so
    // it's the only one that needed "QA Runtime Tests.asmdef" to declare the same versionDefines
    // entry as ZeyWinAds.Runtime.asmdef (com.google.ads.mobile -> ZEYWIN_ADMOB) — that entry was
    // missing until this fixture was added. Version defines are per-assembly, not inherited
    // through an assembly reference: without it, #if ZEYWIN_ADMOB below is false while compiling
    // THIS assembly even though it's true for ZeyWinAds.Runtime, so AdMediator.IsAdMobAvailable /
    // AdMobNetwork.IsInitialized (real cross-assembly calls into Runtime) still correctly read
    // true, but the GMA-calling code right below silently compiled out to nothing — the fixture
    // passed its own availability gate instantly, then hit the #else yield break with every ad
    // type still false. First-hand cause of every "fails near-instantly, zero internal log output"
    // run before this was fixed. PreloadAdMobAds.cs never needed this — it only calls AdMediator's
    // public wrapper properties, never raw GoogleMobileAds.Api types itself.
    //
    // NAMING NOTE: same cross-fixture alphabetical-ordering caveat as PreloadZeyWinAds /
    // PreloadAdMobAds (NUnit only honors [Order] within a single fixture in this Unity Test
    // Framework version). This fixture used to be "PreloadAdMobAdsDirect", sorted right after
    // "PreloadAdMobAds" — i.e. BEFORE PushNotifications and all the WebView fixtures. That
    // position is exactly what caused a real, observed failure: this fixture's real GMA ad loads
    // (interstitial/rewarded video creative, real codec + audio activity) were overlapping in
    // wall-clock time with WebViewCapabilities' redirect-chain tests, and a native WebView
    // callback (onPageFinished / OnWebViewNavigationFinished) got starved/delayed under that
    // contention, intermittently failing FollowsRedirectChain("http") with no code changes on
    // that side at all. The WebView tests should be self-contained — unaffected by whatever
    // AdMob is doing — so instead of trying to interleave the two safely, this fixture now runs
    // dead LAST, named to sort after every other fixture: DeviceIdentity, OfferAndLoadingScreen,
    // PreloadAdMobAds, PreloadZeyWinAds, PushNotifications, WebViewCapabilities,
    // WebViewOrientation, WebViewSafeArea, ZAdMobDirectPreload. Combined with destroying each ad
    // the instant it loads (see above), its real ad-loading no longer overlaps the WebView tests
    // at all. It does not depend on this exact position for its OWN correctness — it never checks
    // IsZeyWinSurfaceActive — only the other fixtures depend on it running last.
    [TestFixture]
    public class ZAdMobDirectPreload : QaFixture
    {
        private const float BudgetSeconds = 45f;

        private static bool _adMobAvailable;
        private static bool _interstitialLoaded;
        private static bool _rewardedLoaded;
        private static bool _bannerLoaded;

        // Two different readinesses matter here, and this fixture needs BOTH before it's safe to
        // call the raw GMA Load APIs itself:
        //  - AdMediator.IsAdMobAvailable: our own settings are configured (app id + unit ids).
        //    True essentially the instant AdMediator.Initialize() runs — set synchronously, before
        //    MobileAds.Initialize's native callback ever fires.
        //  - AdMobNetwork.IsInitialized (internal, reachable via InternalsVisibleTo): GMA's own
        //    MobileAds.Initialize(...) native callback has actually completed. AdMobNetwork's own
        //    Preload*() methods gate on this too (EnsureInitializedForDemand) before ever calling
        //    InterstitialAd.Load/RewardedAd.Load/new BannerView — this fixture bypasses
        //    AdMobNetwork entirely, so it has to reproduce that same gate itself. Calling those GMA
        //    APIs before MobileAds.Initialize completes is what was making every run here fail
        //    near-instantly ("one or more child tests had errors", ~0.05s) — first-hand evidence
        //    this fixture was checking the wrong flag; IsAdMobAvailable alone was never enough.
        // AdMediator.Initialize() itself is deliberately delayed ~1s after boot
        // (AdMobInitializeDelaySeconds — see CLAUDE.md "Early-init JNI", a cold-start-crash
        // mitigation), so this wait is real and needed regardless of where this fixture sits in
        // the run order.
        private const float ReadyWaitSeconds = 15f;

        [UnityOneTimeSetUp]
        public IEnumerator LoadAdsDirectly()
        {
            float waitStart = Time.realtimeSinceStartup;
            while (!(AdMediator.IsAdMobAvailable && global::ZeyWinAds.Mediation.AdMobNetwork.IsInitialized)
                   && Time.realtimeSinceStartup - waitStart < ReadyWaitSeconds)
                yield return new WaitForSecondsRealtime(0.25f);

            _adMobAvailable = AdMediator.IsAdMobAvailable && global::ZeyWinAds.Mediation.AdMobNetwork.IsInitialized;
            if (!_adMobAvailable)
                yield break; // Each [Test] below fails individually with a clear message.

#if ZEYWIN_ADMOB
            var settings = global::ZeyWinAds.ZeyWinAdsSettings.Load();

            InterstitialAd.Load(settings.GetInterstitialUnitId(), new AdRequest(), (ad, error) =>
            {
                if (error != null || ad == null)
                {
                    Debug.LogWarning($"[ZeyWinAds QA] Standalone AdMob interstitial load failed: " +
                        $"{error?.GetMessage() ?? "null ad"}");
                    return;
                }
                _interstitialLoaded = true;
                ad.Destroy(); // We only need proof it loaded — holding it alive keeps its video
                              // creative's codec/audio resources live for no reason (see header).
            });

            RewardedAd.Load(settings.GetRewardedUnitId(), new AdRequest(), (ad, error) =>
            {
                if (error != null || ad == null)
                {
                    Debug.LogWarning($"[ZeyWinAds QA] Standalone AdMob rewarded load failed: " +
                        $"{error?.GetMessage() ?? "null ad"}");
                    return;
                }
                _rewardedLoaded = true;
                ad.Destroy();
            });

            var banner = new BannerView(settings.GetBannerUnitId(), AdSize.Banner, AdPosition.Bottom);
            banner.OnBannerAdLoaded += () =>
            {
                _bannerLoaded = true;
                banner.Destroy();
            };
            banner.OnBannerAdLoadFailed += err =>
                Debug.LogWarning($"[ZeyWinAds QA] Standalone AdMob banner load failed: {err.GetMessage()}");
            banner.LoadAd(new AdRequest());

            var budget = new QaBudget(BudgetSeconds);
            while (!((_interstitialLoaded && _rewardedLoaded && _bannerLoaded) || budget.Expired))
                yield return new WaitForSecondsRealtime(0.5f);

            Debug.Log($"[ZeyWinAds QA] Standalone AdMob load check settled after {budget.Describe()}: " +
                $"Interstitial={_interstitialLoaded}, Rewarded={_rewardedLoaded}, Banner={_bannerLoaded}.");
#else
            yield break;
#endif
        }

        private static void AssertAdMobAvailable()
        {
            if (_adMobAvailable)
                return;

            Assert.Fail($"AdMob was still not available/initialized after waiting {ReadyWaitSeconds:0}s " +
                "(missing/invalid AdMob app id or unit ids, the com.google.ads.mobile package missing " +
                "so ZEYWIN_ADMOB isn't compiled in, or MobileAds.Initialize never completed) — the " +
                "factory contract requires AdMob to be configured on every build.");
        }

        [Test]
        public void InterstitialLoadsDirectly()
        {
            AssertAdMobAvailable();
            Assert.IsTrue(_interstitialLoaded,
                $"GMA InterstitialAd.Load did not succeed within {BudgetSeconds:0}s using the " +
                "configured unit id — a genuine AdMob/config/network problem, unrelated to any " +
                "ZeyWin surface (this fixture never checks for one).");
        }

        [Test]
        public void RewardedLoadsDirectly()
        {
            AssertAdMobAvailable();
            Assert.IsTrue(_rewardedLoaded,
                $"GMA RewardedAd.Load did not succeed within {BudgetSeconds:0}s using the " +
                "configured unit id — a genuine AdMob/config/network problem, unrelated to any " +
                "ZeyWin surface (this fixture never checks for one).");
        }

        [Test]
        public void BannerLoadsDirectly()
        {
            AssertAdMobAvailable();
            Assert.IsTrue(_bannerLoaded,
                $"GMA BannerView.LoadAd did not succeed within {BudgetSeconds:0}s using the " +
                "configured unit id — a genuine AdMob/config/network problem, unrelated to any " +
                "ZeyWin surface (this fixture never checks for one).");
        }
    }
}
