using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZeyWinAds.Tests.Runtime
{
    // QA checklist row "Размер вебвью" — the offer WebView must render inside the Safe Area: display
    // cutouts / notches, the status bar and the navigation bar must never overlap WebView content,
    // and this must hold in EVERY screen orientation.
    //
    // Code under test (unchanged by this test):
    //   Runtime/Plugins/Android/ZeyWinAdsSafeAreaFrameLayout.java — clipToPadding=false + an
    //   OnApplyWindowInsetsListener that per edge takes max(WindowInsets.getSystemWindowInset*(),
    //   DisplayCutout.getSafeInset*()) (cutout part SDK_INT>=28) and setPadding()s itself.
    //   Runtime/UI/WebViewLock.cs ShowAndroidWebView — chain
    //     android.R.id.content -> _nativeWebViewContainer (FrameLayout, MATCH_PARENT)
    //       -> _nativeSafeAreaContainer (ZeyWinAdsSafeAreaFrameLayout, MATCH_PARENT)
    //         -> _webView (MATCH_PARENT, direct child)
    //   so the WebView's usable rect = safe-area container rect minus its padding = content frame
    //   minus the window insets.
    //
    // For portrait + both landscapes this rotates via Screen.orientation (an explicit override —
    // NOT Activity.setRequestedOrientation, which the Unity player re-asserts away), waits for the
    // safe-area padding to settle to the live window insets, then checks natively that the WebView
    // rectangle sits inside `content` minus those insets.
    //
    // Requires a REAL offer WebView to be up (same as OfferAndLoaderRuntimeTests.ShowsForceOffer):
    // no live offer => FAIL, not Inconclusive.
    //
    // Class name starts "WebViewS..." so it sorts after "WebViewOrientation..." / "WebViewCapability..."
    // — NUnit here runs fixtures in class-name order, so this runs last (the [Order] is intent only).
    [TestFixture]
    public class WebViewSafeAreaRuntimeTests
    {
        private const float OfferOpenBudgetSeconds = 60f;       // wait for the SDK's force offer to open
        private const float RotationSettleBudgetSeconds = 12f;  // Screen.orientation setter -> native re-layout
        private const float InsetsSettleBudgetSeconds = 8f;     // safe-area padding catching up to the new insets
        private const int Slop = 4;                             // px — layout rounding + measurement timing

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
        [Order(10)] // After WebViewOrientationRuntimeTests (Order 9); both mutate + restore orientation.
        [Timeout(120000)] // hard cap — a stalled rotation must not hang the whole run
        public IEnumerator OfferWebView_RendersInsideSafeAreaInAllOrientations()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Debug.Log("[ZeyWinAds QA] safe-area: START");
            var lockType = typeof(global::ZeyWinAds.UI.WebViewLock);
            var instance = global::ZeyWinAds.UI.WebViewLock.Instance;
            Assert.IsNotNull(instance, "WebViewLock.Instance is null — the SDK did not initialise.");

            const BindingFlags priv = BindingFlags.NonPublic | BindingFlags.Instance;
            var webViewField = lockType.GetField("_webView", priv);
            var safeAreaField = lockType.GetField("_nativeSafeAreaContainer", priv);
            var containerField = lockType.GetField("_nativeWebViewContainer", priv);
            Assert.IsNotNull(webViewField, "WebViewLock._webView not found — did the SDK rename it?");
            Assert.IsNotNull(safeAreaField, "WebViewLock._nativeSafeAreaContainer not found — did the SDK rename it?");
            Assert.IsNotNull(containerField, "WebViewLock._nativeWebViewContainer not found — did the SDK rename it?");

            // Wait for the SDK's force offer surface to be FULLY up (this test never opens one
            // itself). ShowAndroidWebView assigns _webView, then _nativeWebViewContainer, then
            // _nativeSafeAreaContainer within one runOnUiThread block, and DestroyAndroidWebView
            // nulls all three on a rebuild — so require all three present AND the WebView laid out
            // (non-zero) for two consecutive reads, not just _nativeWebViewContainer. No offer => FAIL.
            Debug.Log("[ZeyWinAds QA] safe-area: waiting for the SDK's offer surface (all 3 native views)...");
            AndroidJavaObject container = null, webView = null, safeArea = null;
            float waitStart = Time.realtimeSinceStartup;
            float lastLog = waitStart;
            int stableReads = 0;
            while (true)
            {
                container = containerField.GetValue(instance) as AndroidJavaObject;
                webView = webViewField.GetValue(instance) as AndroidJavaObject;
                safeArea = safeAreaField.GetValue(instance) as AndroidJavaObject;

                bool laidOut = container != null && webView != null && safeArea != null
                    && webView.Call<int>("getWidth") > 0 && webView.Call<int>("getHeight") > 0;
                stableReads = laidOut ? stableReads + 1 : 0;
                if (stableReads >= 2)
                    break;

                float elapsed = Time.realtimeSinceStartup - waitStart;
                if (elapsed >= OfferOpenBudgetSeconds)
                    Assert.Fail($"The offer WebView surface was not fully up within {OfferOpenBudgetSeconds:F0}s " +
                        $"(container={container != null} webView={webView != null} safeArea={safeArea != null}) " +
                        "— cannot verify the safe area. Enable the force offer for this device/app in the admin " +
                        "panel, and check the device isn't geo/no-SIM blocked server-side.");
                if (Time.realtimeSinceStartup - lastLog >= 5f)
                {
                    Debug.Log($"[ZeyWinAds QA] safe-area: waiting for offer surface — container={container != null} " +
                        $"webView={webView != null} safeArea={safeArea != null} after {elapsed:F0}s...");
                    lastLog = Time.realtimeSinceStartup;
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Debug.Log("[ZeyWinAds QA] safe-area: offer surface found (container + webView + safeArea, laid out)");

            // Baseline — restored in TearDown.
            _baselineOrientation = Screen.orientation;
            _baselineAutoPortrait = Screen.autorotateToPortrait;
            _baselineAutoPortraitUpsideDown = Screen.autorotateToPortraitUpsideDown;
            _baselineAutoLandscapeLeft = Screen.autorotateToLandscapeLeft;
            _baselineAutoLandscapeRight = Screen.autorotateToLandscapeRight;
            _baselineCaptured = true;
            Debug.Log($"[ZeyWinAds QA] safe-area: baseline orientation={_baselineOrientation}");

            var steps = new (ScreenOrientation orient, string label, bool wantLandscape)[]
            {
                (ScreenOrientation.Portrait, "portrait", false),
                (ScreenOrientation.LandscapeLeft, "landscape-left", true),
                (ScreenOrientation.LandscapeRight, "landscape-right", true),
            };

            foreach (var step in steps)
            {
                Debug.Log($"[ZeyWinAds QA] safe-area: rotating to {step.label}");
                yield return RotateAndSettle(step.orient, step.wantLandscape, safeArea);
                AssertWebViewInsideSafeArea(webView, safeArea, step.label);
            }

            Debug.Log("[ZeyWinAds QA] OfferWebView_RendersInsideSafeAreaInAllOrientations: PASS " +
                "(WebView stayed inside the safe area in portrait and both landscapes).");
#else
            Debug.Log("[ZeyWinAds QA] safe-area test skipped (not an Android device).");
            yield break;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // Rotate, wait for the content frame to flip, then wait for the safe-area container's padding
        // to agree with the live window insets for two consecutive reads (the insets dispatch can lag
        // the frame flip by a layout pass).
        private IEnumerator RotateAndSettle(ScreenOrientation target, bool wantLandscape, AndroidJavaObject safeArea)
        {
            Screen.orientation = target;
            yield return null;
            yield return null;
            yield return WaitForContentFrame(wantLandscape);

            float start = Time.realtimeSinceStartup;
            float lastLog = start;
            bool matchedLast = false;
            while (true)
            {
                ReadReferenceSafeInsets(safeArea, out int rl, out int rt, out int rr, out int rb);
                int pl = safeArea.Call<int>("getPaddingLeft");
                int pt = safeArea.Call<int>("getPaddingTop");
                int pr = safeArea.Call<int>("getPaddingRight");
                int pb = safeArea.Call<int>("getPaddingBottom");

                bool match = Mathf.Abs(pl - rl) <= Slop && Mathf.Abs(pt - rt) <= Slop
                    && Mathf.Abs(pr - rr) <= Slop && Mathf.Abs(pb - rb) <= Slop;

                if (match && matchedLast)
                {
                    Debug.Log($"[ZeyWinAds QA] safe-area: padding settled to insets L{pl} T{pt} R{pr} B{pb}");
                    yield break;
                }
                matchedLast = match;

                float elapsed = Time.realtimeSinceStartup - start;
                if (elapsed >= InsetsSettleBudgetSeconds)
                    Assert.Fail($"Safe-area container padding (L{pl} T{pt} R{pr} B{pb}) did not settle to the " +
                        $"window insets (L{rl} T{rt} R{rr} B{rb}) within {InsetsSettleBudgetSeconds:F0}s after " +
                        $"rotating — is ZeyWinAdsSafeAreaFrameLayout.applyZeyWinInsets applying setPadding()?");
                if (Time.realtimeSinceStartup - lastLog >= 3f)
                {
                    Debug.Log($"[ZeyWinAds QA] safe-area: waiting for padding to match insets — " +
                        $"pad L{pl} T{pt} R{pr} B{pb} vs insets L{rl} T{rt} R{rr} B{rb} ({elapsed:F0}s)");
                    lastLog = Time.realtimeSinceStartup;
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        private void AssertWebViewInsideSafeArea(AndroidJavaObject webView, AndroidJavaObject safeArea, string label)
        {
            ReadContentFrame(out int cw, out int ch);
            ReadViewScreenRect(safeArea, out int sl, out int st, out int sr, out int sb, label + " safe-area container");
            ReadViewScreenRect(webView, out int wl, out int wt, out int wr, out int wb, label + " webView");
            ReadReferenceSafeInsets(safeArea, out int rl, out int rt, out int rr, out int rb);

            int spl = safeArea.Call<int>("getPaddingLeft");
            int spt = safeArea.Call<int>("getPaddingTop");
            int spr = safeArea.Call<int>("getPaddingRight");
            int spb = safeArea.Call<int>("getPaddingBottom");

            // Content frame is anchored at the window origin, same space as getGlobalVisibleRect here.
            int cl = sl, ct = st;              // safe-area container fills the content frame
            int cr = sl + cw, cb = st + ch;

            Debug.Log($"[ZeyWinAds QA] safe-area {label}: content={cw}x{ch}  " +
                $"safeContainer=({sl},{st},{sr},{sb})  webView=({wl},{wt},{wr},{wb})  " +
                $"insets=L{rl}/T{rt}/R{rr}/B{rb}  pad=L{spl}/T{spt}/R{spr}/B{spb}");

            // Chain intact: the safe-area container fills the content frame.
            Assert.LessOrEqual(Mathf.Abs((sr - sl) - cw), Slop,
                $"[{label}] safe-area container width {sr - sl} != content frame width {cw}.");
            Assert.LessOrEqual(Mathf.Abs((sb - st) - ch), Slop,
                $"[{label}] safe-area container height {sb - st} != content frame height {ch}.");

            // 1) No overlap with the unsafe zones (the requirement).
            Assert.GreaterOrEqual(wt - ct, rt - Slop,
                $"[{label}] WebView top edge is {ct + rt - wt}px into the status-bar / top-cutout zone " +
                $"(webView.top={wt}, content.top={ct}, top inset={rt}).");
            Assert.GreaterOrEqual(cb - wb, rb - Slop,
                $"[{label}] WebView bottom edge is {rb - (cb - wb)}px into the navigation-bar / bottom-cutout zone " +
                $"(content.bottom={cb}, webView.bottom={wb}, bottom inset={rb}).");
            Assert.GreaterOrEqual(wl - cl, rl - Slop,
                $"[{label}] WebView left edge is {cl + rl - wl}px into the left-cutout / bar zone " +
                $"(webView.left={wl}, content.left={cl}, left inset={rl}).");
            Assert.GreaterOrEqual(cr - wr, rr - Slop,
                $"[{label}] WebView right edge is {rr - (cr - wr)}px into the right-cutout / bar zone " +
                $"(content.right={cr}, webView.right={wr}, right inset={rr}).");

            // 2) The SDK's padding equals the window insets (pinpoints applyZeyWinInsets).
            Assert.LessOrEqual(Mathf.Abs(spl - rl), Slop, $"[{label}] safe-area paddingLeft {spl} != left inset {rl}.");
            Assert.LessOrEqual(Mathf.Abs(spt - rt), Slop, $"[{label}] safe-area paddingTop {spt} != top inset {rt}.");
            Assert.LessOrEqual(Mathf.Abs(spr - rr), Slop, $"[{label}] safe-area paddingRight {spr} != right inset {rr}.");
            Assert.LessOrEqual(Mathf.Abs(spb - rb), Slop, $"[{label}] safe-area paddingBottom {spb} != bottom inset {rb}.");

            // 3) The WebView occupies exactly the padded area (pinpoints clipToPadding + MATCH_PARENT child).
            Assert.LessOrEqual(Mathf.Abs(wl - (sl + spl)), Slop, $"[{label}] webView.left {wl} != container.left+padL {sl + spl}.");
            Assert.LessOrEqual(Mathf.Abs(wt - (st + spt)), Slop, $"[{label}] webView.top {wt} != container.top+padT {st + spt}.");
            Assert.LessOrEqual(Mathf.Abs(wr - (sr - spr)), Slop, $"[{label}] webView.right {wr} != container.right-padR {sr - spr}.");
            Assert.LessOrEqual(Mathf.Abs(wb - (sb - spb)), Slop, $"[{label}] webView.bottom {wb} != container.bottom-padB {sb - spb}.");

            // 4) Not degenerate — the WebView is ~the whole safe area, not collapsed or over-inset.
            int safeW = cw - rl - rr;
            int safeH = ch - rt - rb;
            Assert.GreaterOrEqual(wr - wl, Mathf.RoundToInt(safeW * 0.9f),
                $"[{label}] WebView width {wr - wl} is far below the safe-area width {safeW}.");
            Assert.GreaterOrEqual(wb - wt, Mathf.RoundToInt(safeH * 0.9f),
                $"[{label}] WebView height {wb - wt} is far below the safe-area height {safeH}.");
        }

        // Fills l/t/r/b with the view's on-screen rect via getGlobalVisibleRect (deterministic JNI
        // field reads on a mutated Java Rect — avoids the getLocationOnScreen(int[]) copy-back quirk).
        private static void ReadViewScreenRect(AndroidJavaObject view, out int l, out int t, out int r, out int b, string what)
        {
            using (var rect = new AndroidJavaObject("android.graphics.Rect"))
            {
                bool visible = view.Call<bool>("getGlobalVisibleRect", rect);
                l = rect.Get<int>("left");
                t = rect.Get<int>("top");
                r = rect.Get<int>("right");
                b = rect.Get<int>("bottom");
                Assert.IsTrue(visible && r > l && b > t,
                    $"getGlobalVisibleRect for '{what}' returned no visible area ({l},{t},{r},{b}).");
            }
        }

        // Mirrors ZeyWinAdsSafeAreaFrameLayout.applyZeyWinInsets exactly: deprecated
        // getSystemWindowInset* maxed per edge with DisplayCutout.getSafeInset* (SDK_INT >= 28).
        private static void ReadReferenceSafeInsets(AndroidJavaObject view, out int l, out int t, out int r, out int b)
        {
            using (var insets = view.Call<AndroidJavaObject>("getRootWindowInsets"))
            {
                Assert.IsNotNull(insets,
                    "View.getRootWindowInsets() returned null — cannot compute the reference safe area.");

                l = insets.Call<int>("getSystemWindowInsetLeft");
                t = insets.Call<int>("getSystemWindowInsetTop");
                r = insets.Call<int>("getSystemWindowInsetRight");
                b = insets.Call<int>("getSystemWindowInsetBottom");

                if (GetSdkInt() >= 28)
                {
                    using (var cutout = insets.Call<AndroidJavaObject>("getDisplayCutout"))
                    {
                        if (cutout != null)
                        {
                            l = Mathf.Max(l, cutout.Call<int>("getSafeInsetLeft"));
                            t = Mathf.Max(t, cutout.Call<int>("getSafeInsetTop"));
                            r = Mathf.Max(r, cutout.Call<int>("getSafeInsetRight"));
                            b = Mathf.Max(b, cutout.Call<int>("getSafeInsetBottom"));
                        }
                    }
                }
            }
        }

        private static int GetSdkInt()
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                return version.GetStatic<int>("SDK_INT");
        }

        // Polls the native Activity content frame until it is the requested shape.
        private IEnumerator WaitForContentFrame(bool wantLandscape)
        {
            float start = Time.realtimeSinceStartup;
            float lastLog = start;
            while (true)
            {
                ReadContentFrame(out int w, out int h);
                if (w > 0 && h > 0 && (wantLandscape ? w > h : h > w))
                {
                    Debug.Log($"[ZeyWinAds QA] safe-area: content frame settled {w}x{h} " +
                        $"({(wantLandscape ? "landscape" : "portrait")})");
                    yield break;
                }
                float elapsed = Time.realtimeSinceStartup - start;
                if (elapsed >= RotationSettleBudgetSeconds)
                    Assert.Fail($"Screen did not settle to {(wantLandscape ? "landscape" : "portrait")} " +
                        $"within {RotationSettleBudgetSeconds:F0}s (content frame {w}x{h}).");
                if (Time.realtimeSinceStartup - lastLog >= 3f)
                {
                    Debug.Log($"[ZeyWinAds QA] safe-area: waiting for " +
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
#endif
    }
}
