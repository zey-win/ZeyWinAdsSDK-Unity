using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZeyWinAds.Tests.Runtime
{
    // Formal on-device PlayMode tests for ad preloading — the same checks AdPreloadDiagnostic
    // already does via logcat scraping, but as real [Test]s with individual pass/fail rows in the
    // Test Runner window.
    //
    // All four ad types preload concurrently in the background from fixture start
    // (AdLoader.Instance.OnSDKInitialize() kicks all of them off together) — so they're checked
    // together too, in ONE shared coroutine in [UnityOneTimeSetUp], rather than as four separate
    // [UnityTest] coroutines. NUnit runs test methods one at a time even when they share a
    // QaBudget deadline: if an earlier test has to poll most of the way to the budget before its
    // ad is ready, a later test's coroutine doesn't even START checking until the earlier one's
    // finishes — so whichever ad type happens to run last can fail purely from running out of a
    // budget it was never actually given a fair chance to use.
    //
    // "Preloaded within budget" is read from ZeyWinAds.WasEverLoaded(AdType) — tracked inside the
    // SDK itself, at the single point every successful load already passes through — not from
    // polling IsXReady() here. Popup specifically has a self-driven lifecycle the other three
    // don't: it preloads, then the SDK auto-shows and consumes it after a server-controlled delay,
    // then re-preloads after its own repeat delay (see AutoShowPopupCoroutine / SchedulePopupRepeat
    // in ZeyWinAds.cs) — so IsPopupReady() is only true in narrow, recurring windows. Even
    // subscribing to OnAdLoaded from inside a fixture's own [UnityOneTimeSetUp] can be too late:
    // this fixture doesn't start until after OfferAndLoadingScreen's tests finish, and Popup's
    // entire preload-to-consumed cycle can complete well within that earlier window.
    // WasEverLoaded is set from inside the SDK itself, before any test fixture exists, so it
    // cannot miss the first load no matter how early it happens.
    //
    // Runs after OfferAndLoadingScreen's loader check (see [Order] below), so this 35s window
    // starts once the loader check is already done, not from true app start — the ads have had a
    // head start preloading in the background the whole time regardless.
    //
    // Uses QaForegroundTimeTracker instead of Time.realtimeSinceStartup so that if the real
    // referral offer WebView sends the device to another app (Play Store, Telegram, etc. — see
    // ZeyWinAdsWebViewNavigation.openExternal), the budget doesn't burn down while genuinely
    // backgrounded and unable to run at all.
    //
    // `global::ZeyWinAds.*` (not just `ZeyWinAds.*`) is required because this file's own namespace
    // (ZeyWinAds.Tests.Runtime) is nested under the ZeyWinAds namespace, which makes a bare
    // `ZeyWinAds` reference ambiguous between the namespace and the class of the same name.
    [TestFixture]
    public class PreloadZeyWinAds : QaFixture
    {
        private const float BudgetSeconds = 35f;
        private static readonly WaitForSecondsRealtime PollInterval = new WaitForSecondsRealtime(0.5f);

        private static bool _interstitialReady;
        private static bool _rewardedReady;
        private static bool _nativeReady;
        private static bool _popupReady;

        [UnityOneTimeSetUp]
        public IEnumerator WaitForAllAdsOrBudget()
        {
            var budget = new QaBudget(BudgetSeconds);
            while (true)
            {
                _interstitialReady = global::ZeyWinAds.ZeyWinAds.WasEverLoaded(global::ZeyWinAds.Core.AdType.Interstitial);
                _rewardedReady = global::ZeyWinAds.ZeyWinAds.WasEverLoaded(global::ZeyWinAds.Core.AdType.Rewarded);
                _nativeReady = global::ZeyWinAds.ZeyWinAds.WasEverLoaded(global::ZeyWinAds.Core.AdType.Native);
                _popupReady = global::ZeyWinAds.ZeyWinAds.WasEverLoaded(global::ZeyWinAds.Core.AdType.Popup);

                if ((_interstitialReady && _rewardedReady && _nativeReady && _popupReady) || budget.Expired)
                {
                    Debug.Log($"[ZeyWinAds QA] Ad preload check settled after {budget.Describe()}: " +
                        $"Interstitial={_interstitialReady}, Rewarded={_rewardedReady}, Native={_nativeReady}, Popup={_popupReady}.");
                    yield break;
                }

                yield return PollInterval;
            }
        }

        [Test]
        [Order(1)] // After OfferAndLoadingScreen's Order(0) loader check — see that file's comment.
        public void PreloadsInterstitialWithinBudget()
        {
            Assert.IsTrue(_interstitialReady, $"Interstitial did not preload within {BudgetSeconds:0}s of app start.");
        }

        [Test]
        [Order(1)]
        public void PreloadsRewardedWithinBudget()
        {
            Assert.IsTrue(_rewardedReady, $"Rewarded did not preload within {BudgetSeconds:0}s of app start.");
        }

        [Test]
        [Order(1)]
        public void PreloadsNativeWithinBudget()
        {
            Assert.IsTrue(_nativeReady, $"Native did not preload within {BudgetSeconds:0}s of app start.");
        }

        [Test]
        [Order(1)]
        public void PreloadsPopupWithinBudget()
        {
            Assert.IsTrue(_popupReady, $"Popup did not preload within {BudgetSeconds:0}s of app start.");
        }
    }
}
