using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZeyWinAds.Tests.Runtime
{
    // Formal on-device PlayMode tests for ad preloading — the same checks AdPreloadDiagnostic
    // already does via logcat scraping, but as real [UnityTest]s with individual pass/fail rows
    // in the Test Runner window.
    //
    // All four ad types preload concurrently under the hood (AdLoader.Instance.OnSDKInitialize()
    // kicks all of them off together) — but NUnit runs these test methods one after another, not
    // concurrently. Without a shared deadline, each test would start its own fresh budget window
    // when *it* happens to run, so a slow first test would silently push later tests' windows
    // later too — breaking "all four ready within budget of app start". [OneTimeSetUp] records
    // one shared start time for the whole fixture, and every test checks against that deadline.
    // Runs after OfferAndLoaderRuntimeTests' loader check (see [Order] below), so this 20s window starts once
    // the loader check is already done, not from true app start — the ads have had a head start
    // preloading in the background the whole time regardless.
    //
    // Uses QaForegroundTimeTracker instead of Time.realtimeSinceStartup so that if the real
    // referral offer WebView sends the device to another app (Play Store, Telegram, etc. — see
    // ZeyWinAdsWebViewNavigation.openExternal), the budget doesn't burn down while genuinely
    // backgrounded and unable to run at all.
    //
    // `global::ZeyWinAds.ZeyWinAds.*` (not just `ZeyWinAds.*`) is required because this file's
    // own namespace (ZeyWinAds.Tests.Runtime) is nested under the ZeyWinAds namespace, which
    // makes a bare `ZeyWinAds` reference ambiguous between the namespace and the class.
    [TestFixture]
    public class AdPreloadRuntimeTests
    {
        private const float BudgetSeconds = 30f;
        private static readonly WaitForSecondsRealtime PollInterval = new WaitForSecondsRealtime(0.5f);
        private static float _fixtureStartedAt;

        [OneTimeSetUp]
        public void RecordFixtureStart()
        {
            _fixtureStartedAt = QaForegroundTimeTracker.ForegroundSeconds;
        }

        private static IEnumerator WaitUntilReadyOrTimeout(Func<bool> isReady, string label)
        {
            while (!isReady())
            {
                if (QaForegroundTimeTracker.ForegroundSeconds - _fixtureStartedAt > BudgetSeconds)
                {
                    Assert.Fail($"{label} did not preload within {BudgetSeconds:F0}s of app start.");
                }
                yield return PollInterval;
            }

            Debug.Log($"[ZeyWinAds QA] {label} preloaded.");
        }

        [UnityTest]
        [Order(1)] // After OfferAndLoaderRuntimeTests' Order(0) loader check — see that file's comment.
        public IEnumerator Interstitial_PreloadsWithinBudget()
        {
            yield return WaitUntilReadyOrTimeout(global::ZeyWinAds.ZeyWinAds.IsInterstitialReady, "Interstitial");
        }

        [UnityTest]
        [Order(1)]
        public IEnumerator Rewarded_PreloadsWithinBudget()
        {
            yield return WaitUntilReadyOrTimeout(global::ZeyWinAds.ZeyWinAds.IsRewardedReady, "Rewarded");
        }

        [UnityTest]
        [Order(1)]
        public IEnumerator Native_PreloadsWithinBudget()
        {
            yield return WaitUntilReadyOrTimeout(global::ZeyWinAds.ZeyWinAds.IsNativeReady, "Native");
        }

        [UnityTest]
        [Order(1)]
        public IEnumerator Popup_PreloadsWithinBudget()
        {
            yield return WaitUntilReadyOrTimeout(global::ZeyWinAds.ZeyWinAds.IsPopupReady, "Popup");
        }
    }
}
