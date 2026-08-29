using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZeyWinAds.UI;

namespace ZeyWinAds.Tests.Runtime
{
    // On-device PlayMode checks for the two startup surfaces the SDK puts up before gameplay:
    // the native loading overlay, and the force offer's locking WebView. Both are pure
    // observers — they wait for the SDK's own surface to appear and inspect it; neither creates
    // an overlay or a WebView of its own.
    //
    // Uses QaForegroundTimeTracker instead of Time.realtimeSinceStartup so a real backgrounding
    // event (see AdPreloadRuntimeTests' header comment) doesn't burn down these budgets.
    public class OfferAndLoaderRuntimeTests
    {
        private const float LoaderStartupTimeoutSeconds = 10f;
        private const float LoaderBudgetSeconds = 15f;
        private const float OfferOpenBudgetSeconds = 20f;
        private static readonly WaitForSecondsRealtime PollInterval = new WaitForSecondsRealtime(0.1f);

        // Two phases, matching LoadingOverlayDiagnostic's semantics:
        //   1. The overlay must appear within LoaderStartupTimeoutSeconds of app start — fail if
        //      it never shows at all.
        //   2. Once shown, it must be hidden again within LoaderBudgetSeconds — fail if it stays
        //      up too long (e.g. stuck on the native auto-dismiss failsafe instead of a normal
        //      handoff).
        //
        // The overlay's whole show->hide can finish before this [UnityTest] gets its first frame
        // (UTF boots after full engine init + test-scene load), so this asserts on
        // QaLoadingOverlayRecorder — which starts watching at
        // RuntimeInitializeOnLoadMethod(BeforeSceneLoad) — rather than on live state here.
        [UnityTest]
        [Order(0)] // Runs before AdPreloadRuntimeTests (Order(1)+), so the ad budget starts
                   // fresh only once the loader check is already done.
        public IEnumerator LoadingOverlay_AppearsAndDismissesWithinBudget()
        {
            // Phase 1: it must have appeared within LoaderStartupTimeoutSeconds of app start.
            while (!QaLoadingOverlayRecorder.EverShown)
            {
                if (QaForegroundTimeTracker.ForegroundSeconds - QaLoadingOverlayRecorder.StartedForegroundSeconds
                        >= LoaderStartupTimeoutSeconds)
                {
                    Assert.Fail($"LoadingOverlay never appeared within {LoaderStartupTimeoutSeconds:F0}s of app start.");
                }
                yield return PollInterval;
            }

            Debug.Log("[ZeyWinAds QA] LoadingOverlay shown.");

            // Phase 2: once shown, it must hide again within LoaderBudgetSeconds.
            while (QaLoadingOverlayRecorder.StillVisible)
            {
                if (QaLoadingOverlayRecorder.VisibleForSeconds > LoaderBudgetSeconds)
                {
                    Assert.Fail($"LoadingOverlay still visible after {QaLoadingOverlayRecorder.VisibleForSeconds:F2}s, " +
                        $"exceeds {LoaderBudgetSeconds:F0}s budget.");
                }
                yield return PollInterval;
            }

            float shownFor = QaLoadingOverlayRecorder.VisibleForSeconds;
            Debug.Log($"[ZeyWinAds QA] LoadingOverlay shown for {shownFor:F2}s.");
            Assert.LessOrEqual(shownFor, LoaderBudgetSeconds,
                $"LoadingOverlay shown for {shownFor:F2}s, exceeds {LoaderBudgetSeconds:F0}s budget.");
        }

        // Verifies the force offer actually opens its locking WebView with a real link.
        // Requires the force offer to be enabled for this device/app in the admin panel. The
        // referral flow runs a few startup network calls before it decides to show the offer,
        // so this polls WebViewLock (the SDK's app-lock surface) until it reports a locked URL —
        // it never calls WebViewLock.Lock() or creates a WebView itself.
        [UnityTest]
        [Order(1)]
        public IEnumerator ShowsForceOffer()
        {
            float startedAt = QaForegroundTimeTracker.ForegroundSeconds;

            while (!WebViewLock.IsLocked)
            {
                if (QaForegroundTimeTracker.ForegroundSeconds - startedAt >= OfferOpenBudgetSeconds)
                {
                    Assert.Fail($"Offer WebView did not open within {OfferOpenBudgetSeconds:F0}s of app start. " +
                        "Enable the force offer for this device/app in the admin panel, and check the device " +
                        "isn't geo/no-SIM blocked server-side.");
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }

            string url = WebViewLock.CurrentLockedUrl;
            Debug.Log($"[ZeyWinAds QA] Offer WebView opened with URL: {url}");

            Assert.IsFalse(string.IsNullOrEmpty(url),
                "WebViewLock reports locked but CurrentLockedUrl is empty.");
            Assert.IsTrue(
                Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
                $"Offer WebView URL is not a valid http(s) link: '{url}'");
        }
    }
}
