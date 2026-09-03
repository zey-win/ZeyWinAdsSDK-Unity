using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZeyWinAds.Tests.Runtime
{
    // QA checklist row "Поворот экрана" — while an offer WebView is on screen the user must be able
    // to rotate the device on every side even if the host game is pinned to one orientation, and the
    // native offer surface must re-lay-out to fill the screen in the new orientation.
    //
    // WebViewLock.AllowFreeRotationForOfferSurface() opens all four autorotate axes + AutoRotation
    // when the offer surface begins, and RestoreOrientationAfterOfferSurface() puts the host's
    // orientation back when it ends. Both are private and reached here by reflection (same approach
    // the rest of this suite uses for WebViewLock internals).
    //
    // The Screen.orientation *getter* never returns AutoRotation, so this asserts the effect: it
    // simulates a portrait-locked host, invokes the SDK's free-rotation call, then drives real
    // rotations through Unity's own Screen.orientation setter and checks the live offer container
    // (_nativeWebViewContainer, MATCH_PARENT) still fills the Activity content frame both ways.
    // Rotation is driven via Screen.orientation, NOT Activity.setRequestedOrientation, which the
    // Unity player re-asserts away (see WebViewLock.AllowFreeRotationForOfferSurface comment).
    //
    // Requires a REAL offer WebView to be up (same as OfferAndLoadingScreen.ForceOfferOpens): if the SDK's
    // force offer never opens for this device/app, this FAILS — it does not open an offer of its own
    // and does not go Inconclusive.
    //
    // Class name "WebViewOrientation..." sorts after "WebViewCapabilities..." — NUnit runs fixtures
    // in name order, so this is kept after the capability suite (the [Order] is intent only).
    [TestFixture]
    public class WebViewOrientation : WebViewFixture
    {
        private const float OfferOpenBudgetSeconds = 20f;       // wait for the SDK's force offer to open
        private const float RotationSettleBudgetSeconds = 12f;  // Screen.orientation setter -> native re-layout

#if UNITY_ANDROID && !UNITY_EDITOR
        private bool _baselineCaptured;
        private ScreenOrientation _baselineOrientation;
        private bool _baselineAutoPortrait;
        private bool _baselineAutoPortraitUpsideDown;
        private bool _baselineAutoLandscapeLeft;
        private bool _baselineAutoLandscapeRight;
#endif

        // Safety net: a mid-coroutine Assert can't be wrapped in try/finally, so restore the host's
        // orientation here no matter how the test ended.
        [TearDown]
        public void RestoreHostOrientation()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!_baselineCaptured)
                return;

            Screen.autorotateToPortrait = _baselineAutoPortrait;
            Screen.autorotateToPortraitUpsideDown = _baselineAutoPortraitUpsideDown;
            Screen.autorotateToLandscapeLeft = _baselineAutoLandscapeLeft;
            Screen.autorotateToLandscapeRight = _baselineAutoLandscapeRight;

            bool baselineFreelyRotating = _baselineAutoPortrait
                && _baselineAutoLandscapeLeft
                && _baselineAutoLandscapeRight;
            Screen.orientation = baselineFreelyRotating
                ? ScreenOrientation.AutoRotation
                : _baselineOrientation;
#endif
        }

        [UnityTest]
        [Order(9)] // After WebViewCapabilities' Order(3)-(8); reuses the offer surface they leave open.
        [Timeout(120 * 1000)] // 120s — hard cap — a stalled rotation must not hang the whole run
        public IEnumerator AllowsFreeRotationWhileOfferShowing()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Debug.Log("[ZeyWinAds QA] orientation: START");
            var lockType = typeof(global::ZeyWinAds.UI.WebViewLock);
            var instance = global::ZeyWinAds.UI.WebViewLock.Instance;
            Assert.IsNotNull(instance, "WebViewLock.Instance is null — the SDK did not initialise.");

            const BindingFlags priv = BindingFlags.NonPublic | BindingFlags.Instance;
            var containerField = lockType.GetField("_nativeWebViewContainer", priv);
            var overrideField = lockType.GetField("_orientationOverrideActive", priv);
            var allowMethod = lockType.GetMethod("AllowFreeRotationForOfferSurface", priv);
            var restoreMethod = lockType.GetMethod("RestoreOrientationAfterOfferSurface", priv);
            Assert.IsNotNull(containerField, "WebViewLock._nativeWebViewContainer not found — did the SDK rename it?");
            Assert.IsNotNull(overrideField, "WebViewLock._orientationOverrideActive not found — did the SDK rename it?");
            Assert.IsNotNull(allowMethod, "WebViewLock.AllowFreeRotationForOfferSurface not found.");
            Assert.IsNotNull(restoreMethod, "WebViewLock.RestoreOrientationAfterOfferSurface not found.");

            // Wait for the SDK's force offer to open its native WebView surface (this test never
            // opens one itself). No offer => FAIL.
            Debug.Log("[ZeyWinAds QA] orientation: waiting for the SDK's offer surface (_nativeWebViewContainer)...");
            AndroidJavaObject container = null;
            float waitStart = Time.realtimeSinceStartup;
            float lastLog = waitStart;
            while (container == null)
            {
                container = containerField.GetValue(instance) as AndroidJavaObject;
                if (container != null)
                    break;
                float elapsed = Time.realtimeSinceStartup - waitStart;
                if (elapsed >= OfferOpenBudgetSeconds)
                    Assert.Fail($"Offer WebView did not open within {OfferOpenBudgetSeconds:F0}s " +
                        "(_nativeWebViewContainer == null) — cannot verify rotation. Enable the force " +
                        "offer for this device/app in the admin panel, and check the device isn't " +
                        "geo/no-SIM blocked server-side.");
                if (Time.realtimeSinceStartup - lastLog >= 5f)
                {
                    Debug.Log($"[ZeyWinAds QA] orientation: still no offer surface after {elapsed:F0}s...");
                    lastLog = Time.realtimeSinceStartup;
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Debug.Log("[ZeyWinAds QA] orientation: offer surface found");

            // 0. Wiring check — the offer opened via BeginZeyWinSurface, which must have called
            //    AllowFreeRotationForOfferSurface(). So the override is active RIGHT NOW, before this
            //    test touches anything. Fails if that call was removed from BeginZeyWinSurface().
            Assert.IsTrue((bool)overrideField.GetValue(instance),
                "A live offer surface is up but _orientationOverrideActive is false — the SDK did not " +
                "free rotation when the offer opened (is AllowFreeRotationForOfferSurface() still " +
                "called from BeginZeyWinSurface()?).");
            Debug.Log("[ZeyWinAds QA] orientation: wiring OK — offer opened with _orientationOverrideActive=true");

            // 1. Baseline — restored in TearDown.
            _baselineOrientation = Screen.orientation;
            _baselineAutoPortrait = Screen.autorotateToPortrait;
            _baselineAutoPortraitUpsideDown = Screen.autorotateToPortraitUpsideDown;
            _baselineAutoLandscapeLeft = Screen.autorotateToLandscapeLeft;
            _baselineAutoLandscapeRight = Screen.autorotateToLandscapeRight;
            _baselineCaptured = true;
            Debug.Log($"[ZeyWinAds QA] orientation: baseline captured (orientation={_baselineOrientation}, " +
                $"autoP={_baselineAutoPortrait} autoLL={_baselineAutoLandscapeLeft} autoLR={_baselineAutoLandscapeRight})");

            // 2. Hostile precondition — simulate a host game pinned to portrait.
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.orientation = ScreenOrientation.Portrait;
            // Clear the override the real offer-open already set, so the next call snapshots THIS state.
            overrideField.SetValue(instance, false);
            yield return null;
            yield return null;
            Debug.Log("[ZeyWinAds QA] orientation: phase 2 — forced portrait-locked precondition, waiting for portrait frame");
            yield return WaitForContentFrame(wantLandscape: false);

            // 3. The SDK frees rotation for the offer.
            Debug.Log("[ZeyWinAds QA] orientation: phase 3 — invoking AllowFreeRotationForOfferSurface()");
            allowMethod.Invoke(instance, null);
            yield return null;

            // 4. Premise — every rotate axis is now open despite the portrait-locked host.
            Assert.IsTrue(Screen.autorotateToPortrait,
                "AllowFreeRotationForOfferSurface did not enable autorotateToPortrait.");
            Assert.IsTrue(Screen.autorotateToLandscapeLeft,
                "AllowFreeRotationForOfferSurface did not enable autorotateToLandscapeLeft.");
            Assert.IsTrue(Screen.autorotateToLandscapeRight,
                "AllowFreeRotationForOfferSurface did not enable autorotateToLandscapeRight.");
            Assert.IsTrue((bool)overrideField.GetValue(instance),
                "AllowFreeRotationForOfferSurface did not set _orientationOverrideActive.");
            LogRequestedOrientation("after AllowFreeRotationForOfferSurface");

            // 5. Effect — rotate to landscape; the offer container must follow and fill the screen.
            Debug.Log("[ZeyWinAds QA] orientation: phase 5 — Screen.orientation = LandscapeLeft, waiting for landscape frame");
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            yield return null;
            yield return null;
            yield return WaitForContentFrame(wantLandscape: true);
            yield return new WaitForSecondsRealtime(0.3f);
            AssertContainerFillsFrame(container, expectLandscape: true);

            // 6. Effect — back to portrait.
            Debug.Log("[ZeyWinAds QA] orientation: phase 6 — Screen.orientation = Portrait, waiting for portrait frame");
            Screen.orientation = ScreenOrientation.Portrait;
            yield return null;
            yield return null;
            yield return WaitForContentFrame(wantLandscape: false);
            yield return new WaitForSecondsRealtime(0.3f);
            AssertContainerFillsFrame(container, expectLandscape: false);

            // 7. The SDK restores the host baseline on offer close.
            Debug.Log("[ZeyWinAds QA] orientation: phase 7 — invoking RestoreOrientationAfterOfferSurface()");
            restoreMethod.Invoke(instance, null);
            yield return null;
            Assert.IsFalse(Screen.autorotateToPortrait,
                "RestoreOrientationAfterOfferSurface did not restore autorotateToPortrait=false.");
            Assert.IsFalse(Screen.autorotateToLandscapeLeft,
                "RestoreOrientationAfterOfferSurface did not restore autorotateToLandscapeLeft=false.");
            Assert.IsFalse((bool)overrideField.GetValue(instance),
                "RestoreOrientationAfterOfferSurface did not clear _orientationOverrideActive.");

            Debug.Log("[ZeyWinAds QA] AllowsFreeRotationWhileOfferShowing: PASS " +
                "(offer surface followed the rotation and refilled the screen both ways).");
#else
            Debug.Log("[ZeyWinAds QA] AllowsFreeRotationWhileOfferShowing: skipped (not an Android device).");
            yield break;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // Polls the native Activity content frame until it is the requested shape. Screen.width/height
        // are avoided on purpose — a stalled Unity frame loop can't corrupt a native View read.
        private IEnumerator WaitForContentFrame(bool wantLandscape)
        {
            float start = Time.realtimeSinceStartup;
            float lastLog = start;
            while (true)
            {
                ReadContentFrame(out int w, out int h);
                if (w > 0 && h > 0 && (wantLandscape ? w > h : h > w))
                {
                    Debug.Log($"[ZeyWinAds QA] orientation: content frame settled {w}x{h} " +
                        $"({(wantLandscape ? "landscape" : "portrait")})");
                    yield break;
                }
                float elapsed = Time.realtimeSinceStartup - start;
                if (elapsed >= RotationSettleBudgetSeconds)
                    Assert.Fail($"Screen did not settle to {(wantLandscape ? "landscape" : "portrait")} " +
                        $"within {RotationSettleBudgetSeconds:F0}s (content frame {w}x{h}).");
                if (Time.realtimeSinceStartup - lastLog >= 3f)
                {
                    Debug.Log($"[ZeyWinAds QA] orientation: waiting for " +
                        $"{(wantLandscape ? "landscape" : "portrait")} — frame still {w}x{h} after {elapsed:F0}s");
                    lastLog = Time.realtimeSinceStartup;
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        private static void ReadContentFrame(out int width, out int height)
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var decorView = activity.Call<AndroidJavaObject>("getWindow")
                .Call<AndroidJavaObject>("getDecorView"))
            using (var androidRId = new AndroidJavaClass("android.R$id"))
            using (var content = decorView.Call<AndroidJavaObject>(
                "findViewById", androidRId.GetStatic<int>("content")))
            {
                width = content.Call<int>("getWidth");
                height = content.Call<int>("getHeight");
            }
        }

        private static void AssertContainerFillsFrame(AndroidJavaObject container, bool expectLandscape)
        {
            int cw = container.Call<int>("getWidth");
            int ch = container.Call<int>("getHeight");
            ReadContentFrame(out int fw, out int fh);
            Debug.Log($"[ZeyWinAds QA] orientation: offer container {cw}x{ch}, content frame {fw}x{fh}, " +
                $"expect {(expectLandscape ? "landscape" : "portrait")}.");

            Assert.LessOrEqual(Mathf.Abs(cw - fw), 2,
                $"Offer container width {cw} does not fill the content frame width {fw}.");
            Assert.LessOrEqual(Mathf.Abs(ch - fh), 2,
                $"Offer container height {ch} does not fill the content frame height {fh}.");
            if (expectLandscape)
                Assert.Greater(cw, ch, "Offer container did not adapt to landscape (width <= height).");
            else
                Assert.Greater(ch, cw, "Offer container did not adapt to portrait (height <= width).");
        }

        private static void LogRequestedOrientation(string when)
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                int req = activity.Call<int>("getRequestedOrientation");
                Debug.Log($"[ZeyWinAds QA] orientation: Activity.getRequestedOrientation() {when} = {req} " +
                    "(-1/2/4/10/13 = free rotation; 0/1/5/6/7/8/9/11/12/14 = locked).");
            }
        }
#endif
    }
}
