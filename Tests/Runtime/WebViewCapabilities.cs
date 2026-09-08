using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.TestTools;

namespace ZeyWinAds.Tests.Runtime
{
    // Checks the QA checklist's "Поддержка Javascript" and "Поддержка Cookie" items against the
    // REAL, already-open offer WebView — no second WebView is created: reflecting into the live
    // one is a more faithful check than a hand-built WebView (which could pass while the real
    // one, with its real settings/clients, somehow didn't).
    //
    // WebViewLock only exposes IsLocked/CurrentLockedUrl/Instance publicly — the actual native
    // AndroidJavaObject (_webView) is a private field, reached here via reflection (the same
    // approach the SDK itself uses for optional deps) so no QA-only public API is added.
    //
    // Note: WebViewLock._isLocked flips true before the native WebView object is created (inside
    // an async runOnUiThread callback), so these poll the reflected field itself, not IsLocked.
    //
    // ---- Which test covers which row of the ads.zeywin.com/checklist/webview-test page ----
    //
    //   ExecutesJavaScript              JS execution in the LIVE offer WebView
    //                                   (no dedicated web-checklist row; sanity check)
    //   PersistsCookies                 "Cookies (same-site)"  (cookies-same-site) —
    //                                   against the LIVE offer WebView + CookieManager
    //   FollowsRedirectChain › <mode>   "Redirect chain — navigation (5 hops)"  (redirect-navigation) —
    //                                   one row each: http(5x 302) / meta / js / mixed, via the
    //                                   zeywin-ads-api.whiteapps.workers.dev/api/v1/checklist/redirect/5
    //                                   server fixture, landing back on ads.zeywin.com/checklist/webview-test
    //   CleartextHttp                   "Cleartext HTTP (top-level)"  (cleartext-http)
    //   KeepsSessionAcrossNavigation    QA row "Поддержка сессий" — a session cookie set before a
    //                                   navigation is still sent after it (no dedicated web-checklist row)
    //   PassesChecklist                 every `auto`-bucket row (runAuto), + camera
    //                                   when CAMERA is granted to the install
    //   RoutesExternalScheme › <row>   one row each: tg / intent
    //   GrantsProtectedContentWithoutPrompt
    //                                   QA row "Поддержка автоматического разрешения protected content" —
    //                                   ZeyWinAdsWebChromeClient.onPermissionRequest auto-grants a page's
    //                                   Protected Media ID (EME/Widevine DRM) request with no dialog
    //   BackNavigation › <row>          QA row "Возврат назад" — the OS back control:
    //                                   ReturnsToPreviousPage / KeepsSurfaceOpenOnFirstPage
    //   DeepLinks › <row>               QA row "Переход по диплинку" — deep links inside the
    //                                   WebView are intercepted and handed to the OS
    //                                   (shouldOpenExternally / new Intent / popup routing)
    [TestFixture]
    public class WebViewCapabilities : WebViewFixture
    {
        private const float WebViewReadyBudgetSeconds = 20f;  // real offer must actually open
        private const float JsResultBudgetSeconds = 10f;
        private const float CookieRoundTripBudgetSeconds = 5f;
        private const float RedirectChainBudgetSeconds = 20f;
        private const float RedirectNavigationBudgetSeconds = 45f; // 5 fixture hops (2 hosts) + the React landing page's HTML
        private const float ChecklistReadyBudgetSeconds = 90f;  // SPA + 2MB bundle over the device network
        private const float ChecklistRunBudgetSeconds = 180f;   // runAuto() over ~22 checks, several hitting the network
        private const float BackNavNavigateBudgetSeconds = 15f; // loadUrl() -> URL observed
        private const float BackNavSettleBudgetSeconds = 10f;   // system BACK -> goBack() -> URL observed
        // "page 1" / "page 2" for the back-navigation group — the same fixture page, told apart by
        // &zwnav. A query-string change through loadUrl() is a full document load in Android WebView,
        // hence a real back/forward history entry (a #hash change is not). ?autorun=0 keeps it inert.
        private const string BackNavPage1 = "https://ads.zeywin.com/checklist/webview-test?runner=1&autorun=0&zwnav=1";
        private const string BackNavPage2 = "https://ads.zeywin.com/checklist/webview-test?runner=1&autorun=0&zwnav=2";

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _offerWebView;
        private string _backNavStartUrl; // offer's own URL, captured so the UnityTearDown can restore it

        // Polls WebViewLock._webView (reflected) until the real offer WebView's native object exists,
        // leaving it in _offerWebView. The callers exercise the live offer surface, so if it never
        // opens that's a FAIL (a geo/no-SIM/config problem) — same stance as OfferAndLoadingScreen
        // .ForceOfferOpens and the orientation / safe-area tests.
        private IEnumerator WaitForOfferWebView()
        {
            _offerWebView = null;

            FieldInfo webViewField = typeof(global::ZeyWinAds.UI.WebViewLock).GetField(
                "_webView", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(webViewField, "WebViewLock._webView field not found — did the SDK rename it?");

            var budget = new QaBudget(WebViewReadyBudgetSeconds);
            while (_offerWebView == null)
            {
                var instance = global::ZeyWinAds.UI.WebViewLock.Instance;
                if (instance != null)
                    _offerWebView = webViewField.GetValue(instance) as AndroidJavaObject;

                if (_offerWebView == null)
                {
                    if (budget.Expired)
                    {
                        Assert.Fail($"Real offer WebView never opened within {budget.Describe()}. Enable the " +
                            "force offer for this device/app in the admin panel, and check the device isn't " +
                            "geo/no-SIM blocked server-side.");
                    }
                    yield return new WaitForSecondsRealtime(0.5f);
                }
            }
        }

        // android.webkit.WebView methods must run on the thread the WebView was created on (the
        // Android UI thread). This coroutine runs on Unity's scripting thread; calling directly
        // throws "A WebView method was called on thread 'Thread-N'". Marshals `action` across and
        // waits (foreground-budgeted) for it to run.
        private IEnumerator RunOnUiThread(Action action, Action<string> onError)
        {
            bool done = false;
            string error = null;

            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try { action(); }
                    catch (Exception e) { error = e.Message; }
                    finally { done = true; }
                }));
            }

            var budget = new QaBudget(JsResultBudgetSeconds);
            while (!done)
            {
                if (budget.Expired)
                {
                    onError($"UI-thread call did not complete within {budget.Describe()}.");
                    yield break;
                }
                yield return null;
            }

            if (error != null)
                onError(error);
        }

        private AndroidJavaObject _probeWebView;
        private AndroidJavaObject _probeWebViewClient;

        // Builds a detached WebView wired to the SDK's real ZeyWinAdsLockWebViewClient and points
        // it at a redirect-chain URL. Detached (never added to a view tree) so the live offer is
        // untouched; onPageFinished -> OnWebViewNavigationFinished still fires regardless of
        // attachment, which is the signal the test reads.
        private void CreateProbeWebView(string gameObjectName, string url)
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                _probeWebView = new AndroidJavaObject("android.webkit.WebView", activity);
                using (var settings = _probeWebView.Call<AndroidJavaObject>("getSettings"))
                {
                    settings.Call("setJavaScriptEnabled", true);
                    settings.Call("setDomStorageEnabled", true);
                }
                _probeWebViewClient = new AndroidJavaObject(
                    "com.zeywinads.unity.ZeyWinAdsLockWebViewClient", gameObjectName);
                _probeWebView.Call("setWebViewClient", _probeWebViewClient);
                _probeWebView.Call("loadUrl", url);
            }
        }

        // Coroutine, not fire-and-forget: it posts the WebView teardown to the UI thread and then
        // WAITS for it to run. A runOnUiThread(AndroidJavaRunnable) still pending when the app
        // starts quitting invokes a managed delegate on a torn-down scripting runtime -> native
        // SIGSEGV in UnityJavaProxy_invoke. Joining here keeps nothing pending at shutdown.
        private IEnumerator DestroyProbeWebView()
        {
            if (_probeWebView == null)
                yield break;

            var webView = _probeWebView;
            var client = _probeWebViewClient;
            _probeWebView = null;
            _probeWebViewClient = null;

            bool done = false;
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        webView.Call("stopLoading");
                        webView.Call("destroy");
                        webView.Dispose();
                        client?.Dispose();
                    }
                    catch { /* teardown best-effort */ }
                    finally { done = true; }
                }));
            }

            float startedAt = Time.realtimeSinceStartup;
            while (!done && Time.realtimeSinceStartup - startedAt < 5f)
                yield return null;
        }

        // ---- Capability-checklist probe (drives ads.zeywin.com/checklist/webview-test?runner=1) ----

        private AndroidJavaObject _checklistWebView;
        private AndroidJavaObject _checklistWebViewClient;
        private AndroidJavaObject _checklistChromeClient;

        // A WebView configured like the real offer WebView (ShowAndroidWebView in WebViewLock),
        // attached to the activity content view as a 1x1 px view — in the hierarchy so the renderer
        // and JS timers stay alive, but NOT covering the Unity surface (a full-screen cover makes
        // Unity lose focus / pause, which wedges every coroutine incl. the test runner itself).
        private void CreateChecklistProbeWebView(string gameObjectName, string url)
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                _checklistWebView = new AndroidJavaObject("android.webkit.WebView", activity);
                _checklistWebView.Call("setLayerType", 2, (AndroidJavaObject)null); // LAYER_TYPE_HARDWARE

                using (var settings = _checklistWebView.Call<AndroidJavaObject>("getSettings"))
                {
                    settings.Call("setJavaScriptEnabled", true);
                    settings.Call("setDomStorageEnabled", true);
                    settings.Call("setLoadWithOverviewMode", true);
                    settings.Call("setUseWideViewPort", true);
                    settings.Call("setMediaPlaybackRequiresUserGesture", false);
                    settings.Call("setAllowFileAccess", true);
                    settings.Call("setJavaScriptCanOpenWindowsAutomatically", true);
                    settings.Call("setSupportMultipleWindows", true);
                    settings.Call("setMixedContentMode", 0); // MIXED_CONTENT_ALWAYS_ALLOW
                }

                using (var cookieManager = new AndroidJavaClass("android.webkit.CookieManager")
                    .CallStatic<AndroidJavaObject>("getInstance"))
                {
                    cookieManager.Call("setAcceptCookie", true);
                    cookieManager.Call("setAcceptThirdPartyCookies", _checklistWebView, true);
                }

                _checklistChromeClient = new AndroidJavaObject("com.zeywinads.unity.ZeyWinAdsWebChromeClient");
                _checklistWebView.Call("setWebChromeClient", _checklistChromeClient);
                _checklistWebViewClient = new AndroidJavaObject(
                    "com.zeywinads.unity.ZeyWinAdsLockWebViewClient", gameObjectName);
                _checklistWebView.Call("setWebViewClient", _checklistWebViewClient);

                var layoutParams = new AndroidJavaObject("android.widget.FrameLayout$LayoutParams", 1, 1);
                using (var decorView = activity.Call<AndroidJavaObject>("getWindow")
                    .Call<AndroidJavaObject>("getDecorView"))
                using (var androidRId = new AndroidJavaClass("android.R$id"))
                {
                    var contentView = decorView.Call<AndroidJavaObject>(
                        "findViewById", androidRId.GetStatic<int>("content"));
                    contentView.Call("addView", _checklistWebView, layoutParams);
                }

                _checklistWebView.Call("resumeTimers");
                _checklistWebView.Call("loadUrl", url);
            }
        }

        // Coroutine that WAITS for the UI-thread teardown to finish — see DestroyProbeWebView for
        // why a still-pending runOnUiThread runnable crashes the process at shutdown.
        private IEnumerator DestroyChecklistProbeWebView()
        {
            if (_checklistWebView == null)
                yield break;

            var webView = _checklistWebView;
            var webViewClient = _checklistWebViewClient;
            var chromeClient = _checklistChromeClient;
            _checklistWebView = null;
            _checklistWebViewClient = null;
            _checklistChromeClient = null;

            bool done = false;
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        using (var parent = webView.Call<AndroidJavaObject>("getParent"))
                        {
                            if (parent != null)
                                parent.Call("removeView", webView);
                        }
                        webView.Call("stopLoading");
                        webView.Call("destroy");
                        webView.Dispose();
                        webViewClient?.Dispose();
                        chromeClient?.Dispose();
                    }
                    catch { /* teardown best-effort */ }
                    finally { done = true; }
                }));
            }

            float startedAt = Time.realtimeSinceStartup;
            while (!done && Time.realtimeSinceStartup - startedAt < 5f)
                yield return null;
        }

        // Runs `script` in `webView` on the UI thread and returns the result with the WebView's
        // JSON-encoding removed. Returns null on timeout or a UI-thread error. The payloads this
        // test reads back are plain ASCII, so unwrapping the outer quotes + a couple of escapes
        // is enough (no full JSON parse).
        private IEnumerator EvalJs(AndroidJavaObject webView, string script, float budgetSeconds,
            Action<string> onResult)
        {
            string value = null;
            bool got = false;
            var callback = new JsValueCallback(v => { value = v; got = true; });

            string uiError = null;
            yield return RunOnUiThread(
                () => webView.Call("evaluateJavascript", script, callback),
                err => { uiError = err; got = true; });

            // realtime clock, not QaForegroundTimeTracker — a focus/foreground stall must not
            // wedge this loop forever.
            float startedAt = Time.realtimeSinceStartup;
            while (!got)
            {
                if (Time.realtimeSinceStartup - startedAt >= budgetSeconds)
                {
                    onResult(null);
                    yield break;
                }
                yield return null;
            }

            onResult(uiError != null ? null : DecodeJsString(value));
        }

        private static string DecodeJsString(string raw)
        {
            if (string.IsNullOrEmpty(raw) || raw == "null")
                return null;
            if (raw.Length >= 2 && raw[0] == '"' && raw[raw.Length - 1] == '"')
                raw = raw.Substring(1, raw.Length - 2);
            return raw.Replace("\\\"", "\"").Replace("\\/", "/").Replace("\\n", "\n").Replace("\\\\", "\\");
        }

        // Activity.checkSelfPermission(...) == PackageManager.PERMISSION_GRANTED (0). API 23+.
        private static bool AndroidPermissionGranted(string permission)
        {
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    return activity.Call<int>("checkSelfPermission", permission) == 0;
            }
            catch
            {
                return false;
            }
        }
#endif

        // Only acts after BackNavigation's returns-to-previous-page case (which leaves the offer
        // WebView on the fixture page) — restores the real offer URL for the orientation / safe-area
        // fixtures that run next. A no-op for every other test in this fixture.
        [UnityTearDown]
        public IEnumerator RestoreOfferPageAfterBackNav()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (string.IsNullOrEmpty(_backNavStartUrl))
                yield break;
            string target = _backNavStartUrl;
            _backNavStartUrl = null;

            var webView = ReadOfferWebViewField();
            if (webView == null)
                yield break;

            string current = null;
            yield return RunOnUiThread(() => current = webView.Call<string>("getUrl"), _ => { });
            if (current == target)
                yield break;

            Debug.Log("[ZeyWinAds QA] back-nav: teardown restoring offer page -> " + target);
            yield return RunOnUiThread(() => webView.Call("loadUrl", target),
                err => Debug.LogWarning("[ZeyWinAds QA] back-nav: teardown loadUrl failed: " + err));
            yield return new WaitForSecondsRealtime(1f);
#else
            yield break;
#endif
        }

        [UnityTest]
        [Order(3)] // After PushNotifications Order(2).
        public IEnumerator ExecutesJavaScript()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            yield return WaitForOfferWebView();

            string result = null;
            bool received = false;
            string evalError = null;
            var callback = new JsValueCallback(value =>
            {
                result = value;
                received = true;
            });

            var webView = _offerWebView;
            yield return RunOnUiThread(
                () => webView.Call("evaluateJavascript", "String(1 + 1)", callback),
                err => { evalError = err; received = true; });

            var jsBudget = new QaBudget(JsResultBudgetSeconds);
            while (!received)
            {
                if (jsBudget.Expired)
                    Assert.Fail($"evaluateJavascript did not return a result within {jsBudget.Describe()}.");
                yield return null;
            }

            Assert.IsNull(evalError, $"evaluateJavascript failed on the UI thread: {evalError}");
            Debug.Log($"[ZeyWinAds QA] evaluateJavascript(\"String(1 + 1)\") returned: {result}");
            Assert.AreEqual("\"2\"", result,
                "JavaScript did not execute correctly in the real offer WebView.");
#else
            Debug.Log("[ZeyWinAds QA] ExecutesJavaScript: skipped (not an Android device).");
            yield break;
#endif
        }

        [UnityTest]
        [Order(4)]
        public IEnumerator PersistsCookies()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            yield return WaitForOfferWebView();

            using (var cookieManagerClass = new AndroidJavaClass("android.webkit.CookieManager"))
            using (var cookieManager = cookieManagerClass.CallStatic<AndroidJavaObject>("getInstance"))
            {
                Assert.IsTrue(cookieManager.Call<bool>("acceptCookie"),
                    "CookieManager.acceptCookie() is false — the app is not accepting cookies at all.");

                const string cookieUrl = "https://ads.zeywin.com/";
                const string cookie = "zeywin_qa_cookie=1";
                cookieManager.Call("setCookie", cookieUrl, cookie);

                string readBack = null;
                bool found = false;
                var budget = new QaBudget(CookieRoundTripBudgetSeconds);
                while (!found)
                {
                    readBack = cookieManager.Call<string>("getCookie", cookieUrl);
                    found = !string.IsNullOrEmpty(readBack)
                        && readBack.IndexOf("zeywin_qa_cookie=1", StringComparison.Ordinal) >= 0;
                    if (found)
                        break;

                    if (budget.Expired)
                    {
                        Assert.Fail($"Set a cookie for {cookieUrl} but CookieManager.getCookie() never returned it " +
                            $"within {budget.Describe()} (got: '{readBack}').");
                    }
                    yield return new WaitForSecondsRealtime(0.25f);
                }

                Debug.Log($"[ZeyWinAds QA] Cookie round-trip OK for {cookieUrl}: '{readBack}'");
            }
#else
            Debug.Log("[ZeyWinAds QA] PersistsCookies: skipped (not an Android device).");
            yield break;
#endif
        }

        // [UnityTest] can't combine with [TestCase]; parameterized coroutine rows use
        // [TestCaseSource] + TestCaseData(...).Returns(null) (see BackNavigationCases / DeepLinkCases).
        private static IEnumerable RedirectNavigationCases()
        {
            yield return new TestCaseData("http").Returns(null).SetName("Http302");
            yield return new TestCaseData("meta").Returns(null).SetName("MetaRefresh");
            yield return new TestCaseData("js").Returns(null).SetName("JsLocationReplace");
            yield return new TestCaseData("mixed").Returns(null).SetName("Mixed");
        }

        // Checklist row "Redirect chain — navigation (5 hops)" (redirect-navigation), one case per
        // redirect type the checklist SPA's buttons expose:
        //   http  - 5x HTTP 302, then a 302 back to the checklist page
        //   meta  - 5x <meta http-equiv="refresh">, meta return
        //   js    - 5x location.replace(), js return
        //   mixed - meta / js / 302 / meta / js, then a 302 return  (deliberately not 5 identical hops)
        //
        // Drives the SAME server fixture the SPA uses:
        //   https://zeywin-ads-api.whiteapps.workers.dev/api/v1/checklist/redirect/5?mode=<mode>&dest=page&n=5
        // (n=5 pins the hops value echoed back; without it the backend reads it from the /5 path.)
        // The last hop is a genuine top-level navigation to a SECOND host —
        //   https://ads.zeywin.com/checklist/webview-test?redirect=done&hops=5&mode=<mode>
        // — and the SPA then strips redirect/hops/mode from location.href via history.replaceState a
        // beat later, so the probe latches the raw landing URL at onPageFinished, before that runs.
        //
        // We do NOT drive the dashboard's own pass (that needs a sessionStorage ZW_CHECKLIST_RESUME
        // record written by a real button press). This asserts the WebView-level contract the
        // checklist actually grades: the 5-hop chain completed as one top-level navigation back to
        // the result page carrying redirect=done&hops=5&mode=<mode>. Public fixture, no auth, no
        // app-level rate limit; two hosts only (workers.dev fixture + ads.zeywin.com landing).
        [UnityTest]
        [Order(5)]
        [TestCaseSource(nameof(RedirectNavigationCases))]
        public IEnumerator FollowsRedirectChain(string mode)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const int hops = 5;
            string startUrl = "https://zeywin-ads-api.whiteapps.workers.dev/api/v1/checklist/redirect/" +
                hops + "?mode=" + mode + "&dest=page&n=" + hops;
            string expectedLanding = "https://ads.zeywin.com/checklist/webview-test?redirect=done&hops=" +
                hops + "&mode=" + mode;
            string probeObjectName = "ZeyWinAds_RedirectChainProbe_" + mode;

            var probeGo = new GameObject(probeObjectName);
            var probe = probeGo.AddComponent<RedirectChainProbe>();

            string createError = null;
            yield return RunOnUiThread(
                () => CreateProbeWebView(probeObjectName, startUrl),
                err => createError = err);
            Assert.IsNull(createError, $"[{mode}] could not create the probe WebView: {createError}");

            // Wait until the chain lands back on the checklist page (redirect=done latched, or the
            // last committed navigation is already /checklist/webview-test) or the client errors.
            var budget = new QaBudget(RedirectNavigationBudgetSeconds);
            while (probe.LoadError == null
                   && probe.RedirectDoneUrl == null
                   && (probe.LastNavigationUrl == null
                       || !probe.LastNavigationUrl.Contains("ads.zeywin.com/checklist/webview-test")))
            {
                if (budget.Expired)
                {
                    yield return DestroyProbeWebView();
                    UnityEngine.Object.Destroy(probeGo);
                    Assert.Inconclusive($"[{mode}] {startUrl} neither returned to the checklist page nor errored " +
                        $"within {budget.Describe()} (fixture host / ads.zeywin.com unreachable) — nothing to check.");
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }

            // Saw the landing page but not yet its raw query — give onPageFinished a moment to report
            // it before the SPA's replaceState cleanup makes later reads useless.
            if (probe.LoadError == null && probe.RedirectDoneUrl == null)
                yield return new WaitForSecondsRealtime(1.5f);

            string loadError = probe.LoadError;
            string doneUrl = probe.RedirectDoneUrl;
            string lastUrl = probe.LastNavigationUrl;
            yield return DestroyProbeWebView();
            UnityEngine.Object.Destroy(probeGo);

            Assert.IsNull(loadError,
                $"[{mode}] the offer WebViewClient errored while following the {hops}-hop {mode} redirect chain: {loadError}");

            Assert.IsTrue(lastUrl != null && lastUrl.Contains("ads.zeywin.com/checklist/webview-test"),
                $"[{mode}] the {hops}-hop redirect chain never brought the WebView back to " +
                $"ads.zeywin.com/checklist/webview-test (last navigation: '{lastUrl ?? "<none>"}').");

            if (doneUrl == null)
                Assert.Inconclusive($"[{mode}] the WebView returned to the checklist page but no navigation callback " +
                    $"reported the raw landing URL (expected '{expectedLanding}') — the SPA stripped redirect/hops/mode " +
                    "before the probe could latch it. Top-level return happened; the hop-count marker was unobservable.");

            Assert.IsTrue(doneUrl.Contains("redirect=done"),
                $"[{mode}] landing URL is missing redirect=done: '{doneUrl}'.");
            Assert.IsTrue(doneUrl.Contains("hops=" + hops),
                $"[{mode}] landing URL reports the wrong hop count (expected hops={hops}): '{doneUrl}'.");
            Assert.IsTrue(doneUrl.Contains("mode=" + mode),
                $"[{mode}] landing URL reports the wrong redirect mode (expected mode={mode}): '{doneUrl}'.");

            Debug.Log($"[ZeyWinAds QA] redirect-navigation [{mode}]: {hops} hops survived a top-level navigation -> {doneUrl}");
#else
            Debug.Log($"[ZeyWinAds QA] FollowsRedirectChain[{mode}]: skipped (not an Android device).");
            yield break;
#endif
        }

        [UnityTest]
        [Order(6)]
        public IEnumerator CleartextHttp()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const string probeObjectName = "ZeyWinAds_CleartextHttpProbe";
            // Top-level navigation that STARTS on cleartext http:// (same host as the https target),
            // then 301s back to https. Partner trackers routinely drop a plain-http hop into offer
            // redirect chains; an app built without android:usesCleartextTraffic="true" blocks that
            // request with ERR_CLEARTEXT_NOT_PERMITTED before it leaves the device. Must be a
            // top-level load — subresource requests get silently upgraded/blocked, so they can't
            // prove cleartext works.
            const string cleartextUrl =
                "http://httpbin.org/redirect-to?url=https%3A%2F%2Fhttpbin.org%2Fget&status_code=301";

            var probeGo = new GameObject(probeObjectName);
            var probe = probeGo.AddComponent<RedirectChainProbe>();

            string createError = null;
            yield return RunOnUiThread(
                () => CreateProbeWebView(probeObjectName, cleartextUrl),
                err => createError = err);
            Assert.IsNull(createError, $"Could not create the probe WebView: {createError}");

            var budget = new QaBudget(RedirectChainBudgetSeconds);
            while (probe.LastNavigationUrl == null && probe.LoadError == null)
            {
                if (budget.Expired)
                {
                    yield return DestroyProbeWebView();
                    UnityEngine.Object.Destroy(probeGo);
                    Assert.Inconclusive($"{cleartextUrl} neither resolved nor errored within " +
                        $"{budget.Describe()} (network unreachable) — nothing to check.");
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }

            string loadError = probe.LoadError;
            string finalUrl = probe.LastNavigationUrl;
            yield return DestroyProbeWebView();
            UnityEngine.Object.Destroy(probeGo);

            Assert.IsNull(loadError,
                $"The offer WebView rejected a top-level cleartext http:// navigation: {loadError} " +
                "(app likely built without android:usesCleartextTraffic=\"true\").");
            Assert.IsTrue(finalUrl != null && finalUrl.Contains("httpbin.org/get"),
                $"Expected the http:// -> 301 -> https:// hop to resolve to httpbin.org/get, ended at: '{finalUrl}'.");
            Debug.Log($"[ZeyWinAds QA] cleartext http:// top-level navigation resolved to: {finalUrl}");
#else
            Debug.Log("[ZeyWinAds QA] CleartextHttp: skipped (not an Android device).");
            yield break;
#endif
        }

        // QA row "Поддержка сессий" — "Приложение должно поддерживать работу сессий на сайтах."
        // A working web session = a cookie set on login keeps being sent on every later request.
        // httpbin sets a session cookie, then a FRESH top-level navigation goes to a URL that echoes
        // back the cookies the server received — the nonce must be there, and document.cookie must
        // still hold it. Probe WebView (detached, real ZeyWinAdsLockWebViewClient) so the live offer
        // surface is untouched. Network unreachable => Inconclusive.
        [UnityTest]
        [Order(6)]
        public IEnumerator KeepsSessionAcrossNavigation()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const string probeObjectName = "ZeyWinAds_SessionProbe";
            string nonce = "zwsess-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            string setUrl = "https://httpbin.org/cookies/set/zwsession/" + nonce; // Set-Cookie + 302 -> /cookies
            const string echoUrl = "https://httpbin.org/cookies";                 // JSON of the cookies the server got

            var probeGo = new GameObject(probeObjectName);
            var probe = probeGo.AddComponent<RedirectChainProbe>();

            string createError = null;
            yield return RunOnUiThread(
                () => CreateProbeWebView(probeObjectName, setUrl),
                err => createError = err);
            Assert.IsNull(createError, $"Could not create the probe WebView: {createError}");

            // 1. Let the set-cookie load (and its 302 -> /cookies) settle.
            var setBudget = new QaBudget(RedirectChainBudgetSeconds);
            while (probe.LastNavigationUrl == null && probe.LoadError == null)
            {
                if (setBudget.Expired)
                {
                    yield return DestroyProbeWebView();
                    UnityEngine.Object.Destroy(probeGo);
                    Assert.Inconclusive($"{setUrl} neither resolved nor errored within {setBudget.Describe()} " +
                        "(network unreachable) — nothing to check.");
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }
            Assert.IsNull(probe.LoadError, $"Setting the session cookie failed to load: {probe.LoadError}");

            // 2. Fresh top-level navigation — the actual test: does the session cookie ride along?
            probe.LastNavigationUrl = null;
            yield return RunOnUiThread(
                () => _probeWebView.Call("loadUrl", echoUrl),
                err => Assert.Fail("WebView.loadUrl(echo) failed on the UI thread: " + err));

            var navBudget = new QaBudget(RedirectChainBudgetSeconds);
            while (probe.LastNavigationUrl == null && probe.LoadError == null)
            {
                if (navBudget.Expired)
                {
                    yield return DestroyProbeWebView();
                    UnityEngine.Object.Destroy(probeGo);
                    Assert.Inconclusive($"Navigation to {echoUrl} did not settle within {navBudget.Describe()}.");
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }
            Assert.IsNull(probe.LoadError, $"Navigation to {echoUrl} errored: {probe.LoadError}");

            // 3. What the server received back, and what the DOM can still see.
            string serverSaw = null;
            yield return EvalJs(_probeWebView,
                "(function(){try{return (document.body?document.body.innerText:'').slice(0,2000);}catch(e){return '';}})()",
                JsResultBudgetSeconds, r => serverSaw = r);
            string domCookie = null;
            yield return EvalJs(_probeWebView, "document.cookie", JsResultBudgetSeconds, r => domCookie = r);
            Debug.Log($"[ZeyWinAds QA] session: server received -> {serverSaw}");
            Debug.Log($"[ZeyWinAds QA] session: document.cookie -> {domCookie}");

            // 4. Best-effort cleanup so the nonce cookie doesn't linger in the shared cookie jar.
            yield return RunOnUiThread(
                () => _probeWebView.Call("loadUrl", "https://httpbin.org/cookies/delete?zwsession="),
                _ => { });
            yield return new WaitForSecondsRealtime(0.5f);

            yield return DestroyProbeWebView();
            UnityEngine.Object.Destroy(probeGo);

            Assert.IsTrue(serverSaw != null && serverSaw.Contains(nonce),
                "After a fresh top-level navigation the session cookie was NOT sent back to the server — " +
                $"httpbin reported the cookies it received as: {serverSaw}. The offer WebView is not " +
                "persisting / re-sending session cookies across navigations.");
            Assert.IsTrue(domCookie != null && domCookie.Contains("zwsession=" + nonce),
                $"document.cookie no longer holds the session cookie after navigation (got '{domCookie}').");
            Debug.Log("[ZeyWinAds QA] KeepsSessionAcrossNavigation: PASS");
#else
            Debug.Log("[ZeyWinAds QA] KeepsSessionAcrossNavigation: skipped (not an Android device).");
            yield break;
#endif
        }

        [UnityTest]
        [Order(7)]
        [Timeout(300 * 1000)] // 300s — SPA boot (90s) + runAuto over ~22 checks (180s), several hitting the network
        public IEnumerator PassesChecklist()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const string probeObjectName = "ZeyWinAds_CapabilityChecklistProbe";
            // ?autorun=0 so the page sets up ZW_CHECKLIST but waits for us to call runAuto().
            const string url = "https://ads.zeywin.com/checklist/webview-test?runner=1&autorun=0";

            var probeGo = new GameObject(probeObjectName);

            string createError = null;
            yield return RunOnUiThread(
                () => CreateChecklistProbeWebView(probeObjectName, url),
                err => createError = err);
            Assert.IsNull(createError, $"Could not create the checklist probe WebView: {createError}");

            // 1. Wait for the SPA to boot and expose the ZW_CHECKLIST contract. Also surfaces the
            //    raw probe value + document.readyState/URL each poll so a stall is diagnosable.
            const string readyScript =
                "(function(){try{return (typeof window.ZW_CHECKLIST==='object'&&window.ZW_CHECKLIST)" +
                "?'READY':('WAIT rs='+document.readyState+' url='+location.href.slice(0,60));}" +
                "catch(e){return 'WAIT err='+String(e);}})()";
            bool ready = false;
            float readyStartedAt = Time.realtimeSinceStartup;
            while (!ready)
            {
                string probe = null;
                yield return EvalJs(_checklistWebView, readyScript, JsResultBudgetSeconds, r => probe = r);
                Debug.Log($"[ZeyWinAds QA] checklist ready-probe (+{Time.realtimeSinceStartup - readyStartedAt:F0}s): {probe ?? "<null>"}");
                if (probe == "READY")
                {
                    ready = true;
                    break;
                }
                if (Time.realtimeSinceStartup - readyStartedAt >= ChecklistReadyBudgetSeconds)
                {
                    yield return DestroyChecklistProbeWebView();
                    UnityEngine.Object.Destroy(probeGo);
                    Assert.Inconclusive($"window.ZW_CHECKLIST never appeared within {ChecklistReadyBudgetSeconds:F0}s " +
                        $"(page or network unreachable). Last probe: {probe ?? "<null>"}");
                }
                yield return new WaitForSecondsRealtime(2f);
            }

            // 2. Kick runAuto() (the `auto` bucket). camera is also run and graded, but ONLY when
            //    CAMERA is actually held by this install — the plain Editor->device flow reinstalls
            //    the player every run and wipes runtime grants, so there it is reported as
            //    "skipped (OS permission not granted)" and NOT run (no prompt). The CI runner grants
            //    it post-install, so there it runs and is graded.
            //    navigates/external/manual buckets are always reported-only (marked "n/a").
            bool cameraGranted = AndroidPermissionGranted("android.permission.CAMERA");
            Debug.Log($"[ZeyWinAds QA] checklist: CAMERA held by this install = {cameraGranted}");

            string kickScript =
                "(function(){window.__zw={done:false};var gradePerm=" + (cameraGranted ? "true" : "false") + ";" +
                "if(!window.ZW_CHECKLIST){window.__zw={done:true,verdict:'NO_CONTRACT',report:''};return;}" +
                "if((ZW_CHECKLIST.version||0)<3){window.__zw={done:true,verdict:'BAD_VERSION',report:'version='+ZW_CHECKLIST.version};return;}" +
                "var meta=ZW_CHECKLIST.meta||{};var permIds={camera:1};" +
                "Promise.resolve(ZW_CHECKLIST.runAuto()).then(function(){" +
                "if(!gradePerm)return;" +
                "return Promise.all(['camera'].map(function(id){" +
                "return Promise.resolve(ZW_CHECKLIST.run(id)).catch(function(){});}));}).then(function(){" +
                "var r=ZW_CHECKLIST.results();" +
                "var ids=Object.keys(r).sort(function(a,b){" +
                "var ba=(meta[a]&&meta[a].bucket)||'zz',bb=(meta[b]&&meta[b].bucket)||'zz';" +
                "return ba<bb?-1:ba>bb?1:(a<b?-1:1);});" +
                "var lines=[],fails=[],gp=0,gf=0,gs=0,nr=0;" +
                // Human title as shown on the checklist page, with the stable id in braces so a
                // failure line is still greppable. Falls back to the id if meta has no title.
                "function nm(k){var mm=meta[k]||{};var t=mm.title||mm.label||mm.name||mm.text;" +
                "return t?(t+'  {'+k+'}'):k;}" +
                "ids.forEach(function(k){var e=r[k]||{},b=(meta[k]&&meta[k].bucket)||'?',s=e.status||'?';" +
                "var graded=(b==='auto')||(gradePerm&&permIds[k]);" +
                "if(permIds[k]&&!gradePerm){lines.push('⏭️ SKIP  ['+b+']  '+nm(k)+'  - skipped: OS permission not granted to this install (CI grants post-install)');gs++;return;}" +
                "if(!graded){lines.push('⬜ n/a   ['+b+']  '+nm(k));nr++;return;}" +
                // camera fails stay fails. Just annotate which layer broke: NotAllowedError
                // = the SDK's permission wiring; NotReadableError/NotFoundError = permission was fine,
                // the device sensor couldn't open (busy / absent — check the device, not the SDK).
                "if(permIds[k]&&s!=='pass'){var er=String(e.detail||'');" +
                "var layer=/NotAllowedError/.test(er)?' [SDK permission wiring]':" +
                "/Not(Readable|Found)Error|OverconstrainedError|AbortError/.test(er)?' [device sensor could not open — permission grant worked]':'';" +
                "lines.push('❌ FAIL  ['+b+']  '+nm(k)+'  - '+er.slice(0,120)+layer);gf++;fails.push(k+'='+s);return;}" +
                "var d=(e.detail||'').replace(/\\s+/g,' ').slice(0,140);" +
                "var m=s==='pass'?'✅ PASS':s==='skip'?'⏭️ SKIP':s==='pending'?'⬜ n/a  ':'❌ FAIL';" +
                "lines.push(m+'  ['+b+']  '+nm(k)+(d?('  - '+d):''));" +
                "if(s==='pass')gp++;else if(s==='skip')gs++;else{gf++;fails.push(k+'='+s);}});" +
                "var head='graded: '+gp+' pass · '+gf+' fail · '+gs+' skip   |   not run here (other buckets): '+nr;" +
                "window.__zw={done:true,verdict:(fails.length?'FAIL ':'OK ')+'fails=['+fails.join(',')+']'," +
                "report:head+'\\n'+lines.join('\\n')};" +
                "}).catch(function(e){window.__zw={done:true,verdict:'THREW',report:String(e)};});})();";
            yield return EvalJs(_checklistWebView, kickScript, JsResultBudgetSeconds, _ => { });

            // 3. Poll until done. Each tick reports a real breakdown — pass/fail/skip counts, which
            //    check is *running*, which are still pending, plus the page context state — so a
            //    hang (a check that never settles, or the probe page losing ZW_CHECKLIST because
            //    something navigated it / its renderer died) is visible instead of a blank "PENDING".
            const string pollScript =
                "(function(){" +
                "if(window.__zw&&window.__zw.done)" +
                "return 'DONE\\n'+window.__zw.verdict+'\\n===\\n'+(window.__zw.report||'');" +
                "var zw=window.__zw?(window.__zw.done?'done':'running'):'none';" +
                "var rs='?',url='?';try{rs=document.readyState;url=String(location.href).slice(0,80);}catch(e){}" +
                "try{" +
                "if(!window.ZW_CHECKLIST)return 'PENDING no-contract  rs='+rs+'  url='+url+'  zw='+zw;" +
                "var r=ZW_CHECKLIST.results();var ks=Object.keys(r);" +
                "var p=0,f=0,s=0,run=[],pend=[];" +
                "ks.forEach(function(k){var st=(r[k]&&r[k].status)||'?';" +
                "if(st==='pass')p++;else if(st==='fail')f++;else if(st==='skip')s++;" +
                "else if(st==='running')run.push(k);else pend.push(k);});" +
                "return 'PENDING '+(p+f+s)+'/'+ks.length+'  ('+p+'P '+f+'F '+s+'S)'" +
                "+(run.length?'  running='+run.join(','):'')" +
                "+(pend.length?'  pending='+pend.slice(0,8).join(','):'')" +
                "+'  zw='+zw;" +
                "}catch(e){return 'PENDING err='+String(e).slice(0,140)+'  rs='+rs+'  url='+url+'  zw='+zw;}" +
                "})()";
            string payload = null;
            string lastProgress = null;
            bool navigatedToPopupProbe = false;
            float runStartedAt = Time.realtimeSinceStartup;
            while (true)
            {
                string probe = null;
                yield return EvalJs(_checklistWebView, pollScript, JsResultBudgetSeconds, r => probe = r);
                if (!string.IsNullOrEmpty(probe) && probe.StartsWith("DONE\n"))
                {
                    payload = probe;
                    break;
                }
                if (!string.IsNullOrEmpty(probe) && probe.StartsWith("PENDING "))
                    lastProgress = probe;
                Debug.Log($"[ZeyWinAds QA] checklist run-probe (+{Time.realtimeSinceStartup - runStartedAt:F0}s): {probe ?? "<null>"}");

                // The SPA's popup check navigates the probe to /checklist/popup-probe and does not
                // return — it wipes ZW_CHECKLIST, so the run can never complete. Don't wait out the
                // whole budget; bail now with a pointed reason (this check is being reworked
                // server-side into an opener<->popup postMessage handshake).
                if (!string.IsNullOrEmpty(probe) && probe.Contains("/checklist/popup-probe"))
                {
                    navigatedToPopupProbe = true;
                    break;
                }

                if (Time.realtimeSinceStartup - runStartedAt >= ChecklistRunBudgetSeconds)
                    break;
                yield return new WaitForSecondsRealtime(3f);
            }

            yield return DestroyChecklistProbeWebView();
            UnityEngine.Object.Destroy(probeGo);

            if (navigatedToPopupProbe)
                Assert.Inconclusive("Checklist SPA navigated the probe to /checklist/popup-probe and never came back " +
                    "— its popup capability check is mid-rework server-side (opener<->popup postMessage handshake), " +
                    "and while it navigates the top frame it wipes ZW_CHECKLIST so the run can't finish. " +
                    "PassesChecklist will work again once that check lands. Last progress: " + (lastProgress ?? "<none>"));

            if (string.IsNullOrEmpty(payload))
                Assert.Inconclusive($"ZW_CHECKLIST.runAuto() did not finish within {ChecklistRunBudgetSeconds:F0}s. " +
                    $"Last progress: {lastProgress ?? "<none>"}");

            string body = payload.Substring("DONE\n".Length);
            int sep = body.IndexOf("\n===\n", StringComparison.Ordinal);
            string verdict = sep >= 0 ? body.Substring(0, sep) : body;
            string report = sep >= 0 ? body.Substring(sep + "\n===\n".Length) : "";

            Debug.Log($"[ZeyWinAds QA] capability checklist\n{report}\n-> {verdict}");

            Assert.IsFalse(verdict.StartsWith("NO_CONTRACT") || verdict.StartsWith("BAD_VERSION") || verdict.StartsWith("THREW"),
                $"Checklist harness problem: {verdict}\n{report}");
            Assert.IsTrue(verdict.StartsWith("OK "),
                $"One or more auto-bucket WebView capability checks did not pass: {verdict}\n(full per-check report in the log above)");
#else
            Debug.Log("[ZeyWinAds QA] PassesChecklist: skipped (not an Android device).");
            yield break;
#endif
        }

        // One case per external-scheme row of the checklist. Each verifies the level a headless
        // test actually can: the SDK's *routing decision* — ZeyWinAdsWebViewNavigation
        // .shouldOpenExternally() must return true, so the offer WebView hands the URL to the OS
        // instead of trying to load it as a page (hard assert). Whether an Activity resolves it is
        // logged only; actually launching the target app is a UiAutomator job, not this. No
        // dialogs, no navigation, no startActivity.
        [Order(8)]
        [TestCase("deeplink-tg",   "tg://resolve?domain=telegram",                  TestName = "tg")]
        [TestCase("intent-scheme", "intent://example.com#Intent;scheme=https;end",  TestName = "intent")]
        public void RoutesExternalScheme(string checklistId, string url)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var navClass = new AndroidJavaClass("com.zeywinads.unity.ZeyWinAdsWebViewNavigation"))
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var pm = activity.Call<AndroidJavaObject>("getPackageManager"))
            {
                Assert.IsTrue(navClass.CallStatic<bool>("shouldOpenExternally", url),
                    $"[{checklistId}] ZeyWinAdsWebViewNavigation.shouldOpenExternally returned false for {url} — " +
                    "the offer WebView would try to load this scheme as a page instead of handing it to the OS.");

                string resolved = ResolveViewIntentPackage(url, pm);
                Debug.Log($"[ZeyWinAds QA] {checklistId}: shouldOpenExternally=true, resolves to {resolved ?? "<no visible handler>"}");

                if (resolved == null)
                    Assert.Inconclusive($"[{checklistId}] routing verified (shouldOpenExternally=true), but no Activity " +
                        $"resolves {url} — the target app isn't installed, or the scheme isn't in the AndroidManifest " +
                        "<queries>. Real OS hand-off is a UiAutomator check.");
            }
#else
            Assert.Ignore($"RoutesExternalScheme[{checklistId}]: Android device only.");
#endif
        }

        // QA checklist row "Поддержка автоматического разрешения protected content": the offer WebView must
        // auto-approve a page's Protected Media ID (EME / Widevine DRM playback) permission request with no
        // dialog — the equivalent of UniWebView's RegisterOnRequestMediaCapturePermission(_ => Grant).
        //
        // SUT: ZeyWinAdsWebChromeClient.onPermissionRequest -> handlePermissionRequest. It grants any
        // resource set whose members need no *dangerous Android runtime* permission, immediately and with
        // no prompt (allAndroidPermissionsGranted == true -> grantRequest -> request.grant(resources)).
        // What classifies each resource is toAndroidPermission(String): CAMERA for RESOURCE_VIDEO_CAPTURE,
        // RECORD_AUDIO for RESOURCE_AUDIO_CAPTURE, and null ("nothing to ask the OS for") for everything
        // else — including RESOURCE_PROTECTED_MEDIA_ID. So this asserts that classification via JNI: if a
        // change ever mapped PROTECTED_MEDIA_ID to a permission, the grant would start waiting on / prompting
        // for it and this checklist row would regress.
        //
        // Pure classification check — no WebView, no live offer surface, no PermissionRequest instance
        // (it's abstract; a real end-to-end grant needs the Espresso harness with a Widevine test page).
        [Test]
        [Order(8)]
        public void GrantsProtectedContentWithoutPrompt()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            string protectedMediaId, videoCapture, audioCapture;
            using (var permissionRequest = new AndroidJavaClass("android.webkit.PermissionRequest"))
            {
                protectedMediaId = permissionRequest.GetStatic<string>("RESOURCE_PROTECTED_MEDIA_ID");
                videoCapture = permissionRequest.GetStatic<string>("RESOURCE_VIDEO_CAPTURE");
                audioCapture = permissionRequest.GetStatic<string>("RESOURCE_AUDIO_CAPTURE");
            }

            // Low-level JNI: toAndroidPermission is private static, so AndroidJavaClass.CallStatic (public-
            // member reflection) can't reach it — GetStaticMethodID resolves private members fine.
            IntPtr chromeClientClass = AndroidJNI.FindClass("com/zeywinads/unity/ZeyWinAdsWebChromeClient");
            if (AndroidJNI.ExceptionOccurred() != IntPtr.Zero) AndroidJNI.ExceptionClear();
            Assert.AreNotEqual(IntPtr.Zero, chromeClientClass,
                "com.zeywinads.unity.ZeyWinAdsWebChromeClient not found via JNI.");

            IntPtr toAndroidPermissionId = AndroidJNI.GetStaticMethodID(
                chromeClientClass, "toAndroidPermission", "(Ljava/lang/String;)Ljava/lang/String;");
            if (AndroidJNI.ExceptionOccurred() != IntPtr.Zero) AndroidJNI.ExceptionClear();
            Assert.AreNotEqual(IntPtr.Zero, toAndroidPermissionId,
                "ZeyWinAdsWebChromeClient.toAndroidPermission(String) not found — did the SDK rename it? " +
                "The Protected-Media auto-grant routing can no longer be verified.");

            Func<string, string> mapResource = resource =>
            {
                IntPtr jResource = AndroidJNI.NewStringUTF(resource);
                try
                {
                    var args = new jvalue[1];
                    args[0].l = jResource;
                    return AndroidJNI.CallStaticStringMethod(chromeClientClass, toAndroidPermissionId, args);
                }
                finally { AndroidJNI.DeleteLocalRef(jResource); }
            };

            string mappedForProtectedMedia = mapResource(protectedMediaId);
            Assert.IsNull(mappedForProtectedMedia,
                $"ZeyWinAdsWebChromeClient.toAndroidPermission mapped Protected Media ID ('{protectedMediaId}') " +
                $"to '{mappedForProtectedMedia}'. handlePermissionRequest would then gate the EME/DRM grant " +
                "behind a runtime permission request/prompt instead of calling request.grant() immediately — " +
                "the 'auto-approve protected content' checklist row regresses.");

            // Sanity that the JNI call actually hit the real method (not a same-named no-op).
            Assert.AreEqual("android.permission.CAMERA", mapResource(videoCapture),
                "RESOURCE_VIDEO_CAPTURE should still map to CAMERA.");
            Assert.AreEqual("android.permission.RECORD_AUDIO", mapResource(audioCapture),
                "RESOURCE_AUDIO_CAPTURE should still map to RECORD_AUDIO.");

            AndroidJNI.DeleteLocalRef(chromeClientClass);
            Debug.Log("[ZeyWinAds QA] GrantsProtectedContentWithoutPrompt: PASS — Protected Media ID needs no OS " +
                "permission gate, so ZeyWinAdsWebChromeClient.onPermissionRequest grants it with no dialog.");
#else
            Assert.Ignore("GrantsProtectedContentWithoutPrompt: Android device only.");
#endif
        }

        // QA checklist row "Возврат назад" — while an offer WebView is on screen the OS's standard back
        // control (on-screen Back button OR edge-swipe gesture — both normalise to KEYCODE_BACK) must
        // (returns-to-previous-page) take the user back to the previous page, and (first-page-no-close)
        // NOT close the WebView when used on the first page of the site.
        //
        // SUT: WebViewLock.Update() (Android device only) polls Input.GetKeyDown(KeyCode.Escape) —
        // Unity's mapping of the Android BACK key delivered to its Activity — and runs
        //   if (_webView.canGoBack()) _webView.goBack();  else NOTHING.
        // No native back handling exists in the SDK and the WebView never holds key focus, so
        // requirement #2 holds purely by the absence of any Unlock/finish/destroy on back-at-root.
        //
        // "page 1" / "page 2" are staged on the checklist fixture URL (told apart by &zwnav). The back
        // press is injected as KEYCODE_BACK via Activity.dispatchKeyEvent (the Unity player's input
        // entry point) — the realistic ceiling for a PlayMode test; a true edge-swipe gesture needs a
        // manual companion check. No live offer => Inconclusive (via WaitForOfferWebView).
        // [UnityTest] can't be combined with [TestCase]; the supported way to give a coroutine test
        // parameterized rows is [TestCaseSource] with TestCaseData(...).Returns(null) — the
        // .Returns(null) satisfies NUnit's "expected result" check so it doesn't reject the
        // IEnumerator return, then the [UnityTest] runner drives the coroutine.
        private static IEnumerable BackNavigationCases()
        {
            yield return new TestCaseData("returns-to-previous-page")
                .Returns(null)
                .SetName("ReturnsToPreviousPage");
            yield return new TestCaseData("first-page-no-close")
                .Returns(null)
                .SetName("KeepsSurfaceOpenOnFirstPage");
        }

        [UnityTest]
        [Order(9)] // After RoutesExternalScheme (Order 8); reuses the already-open offer surface.
        [Timeout(90 * 1000)] // 90s
        [TestCaseSource(nameof(BackNavigationCases))]
        public IEnumerator BackNavigation(string scenario)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            yield return WaitForOfferWebView(); // leaves _offerWebView; Inconclusive if no live offer

            var legacyBackField = typeof(global::ZeyWinAds.UI.WebViewLock).GetField(
                "_legacyBackInputUnavailable", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(legacyBackField,
                "WebViewLock._legacyBackInputUnavailable not found — did the SDK rename it?");
            Assert.IsFalse((bool)legacyBackField.GetValue(global::ZeyWinAds.UI.WebViewLock.Instance),
                "WebViewLock._legacyBackInputUnavailable is true — the project's Active Input Handling must " +
                "include the old Input Manager ('Both' or 'Input Manager'), or the SDK's system-back handler " +
                "is permanently disabled for the session and the offer WebView cannot honour back.");
            Assert.IsTrue(global::ZeyWinAds.UI.WebViewLock.IsLocked,
                "Offer surface is not locked though its WebView is up.");

            switch (scenario)
            {
                case "returns-to-previous-page": yield return ReturnsToPreviousPage(); break;
                case "first-page-no-close":      yield return KeepsSurfaceOpenOnFirstPage(); break;
                default: Assert.Fail($"Unknown BackNavigation scenario '{scenario}'."); break;
            }
#else
            Debug.Log($"[ZeyWinAds QA] BackNavigation[{scenario}]: skipped (not an Android device).");
            yield break;
#endif
        }

        // QA checklist row "Переход по диплинку" — a deep link encountered inside the offer WebView must
        // be caught and handed to the OS (new Intent), not loaded as a page. SUT:
        // ZeyWinAdsWebViewNavigation.shouldOpenExternally (deep-link scheme allow-list) + openExternal(Activity,
        // url) (builds ACTION_VIEW / Intent.parseUri + startActivity), reached from
        // ZeyWinAdsLockWebViewClient.shouldOverrideUrlLoading and, for window.open/_blank, from
        // ZeyWinAdsWebChromeClient.onCreateWindow -> routePopupUrl. Rows 1-4 are pure JNI (no WebView,
        // no startActivity); row 5 drives the real ZeyWinAdsWebChromeClient on the live offer WebView
        // with an intent:// naming an absent package, so nothing actually launches.
        private static IEnumerable DeepLinkCases()
        {
            yield return new TestCaseData("all-schemes-route-external")
                .Returns(null).SetName("RouteWhitelistedSchemes");
            yield return new TestCaseData("non-deeplink-fall-through")
                .Returns(null).SetName("IgnoreUnknownSchemes");
            yield return new TestCaseData("builds-view-intent")
                .Returns(null).SetName("BuildViewIntent");
            yield return new TestCaseData("intent-uri-parses-fallback")
                .Returns(null).SetName("ParseIntentUriFallback");
            yield return new TestCaseData("popup-routed-out")
                .Returns(null).SetName("PopupKeepsOfferOnPage");
        }

        [UnityTest]
        [Order(9)] // After RoutesExternalScheme (Order 8); shares 9 with BackNavigation (order between them irrelevant).
        [Timeout(90 * 1000)] // 90s
        [TestCaseSource(nameof(DeepLinkCases))]
        public IEnumerator DeepLinks(string scenario)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            switch (scenario)
            {
                case "all-schemes-route-external": RouteWhitelistedSchemes(); break;
                case "non-deeplink-fall-through":  IgnoreUnknownSchemes(); break;
                case "builds-view-intent":         BuildViewIntent(); break;
                case "intent-uri-parses-fallback": ParseIntentUriFallback(); break;
                case "popup-routed-out":           yield return PopupKeepsOfferOnPage(); break;
                default: Assert.Fail($"Unknown DeepLinks scenario '{scenario}'."); break;
            }
            yield break;
#else
            Debug.Log($"[ZeyWinAds QA] DeepLinks[{scenario}]: skipped (not an Android device).");
            yield break;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static void RouteWhitelistedSchemes()
        {
            // mailto:/tel:/sms: are on the SDK allow-list too, but out of QA scope — not asserted here.
            string[] urls =
            {
                "intent://scan/#Intent;scheme=zxing;end",
                "market://details?id=com.example.app",
                "tg://resolve?domain=telegram",
                "telegram://resolve?domain=telegram",
                "whatsapp://send?text=hi",
                "viber://forward?text=hi",
            };
            using (var nav = new AndroidJavaClass("com.zeywinads.unity.ZeyWinAdsWebViewNavigation"))
            {
                foreach (var url in urls)
                    Assert.IsTrue(nav.CallStatic<bool>("shouldOpenExternally", url),
                        $"ZeyWinAdsWebViewNavigation.shouldOpenExternally returned false for '{url}' — the offer " +
                        "WebView would load this deep-link scheme as a page instead of handing it to the OS.");
            }
        }

        private static void IgnoreUnknownSchemes()
        {
            // Schemes NOT on the allow-list are handed back to the WebView (-> ERR_UNKNOWN_URL_SCHEME).
            // Pins the boundary so a future allow-list change is noticed. http/https/about are isWebUrl
            // -> also never routed out.
            string[] notRouted =
            {
                "myapp://open?x=1",
                "bitcoin:1A2b3C",
                "fb://profile/1",
                "spotify:track:xyz",
                "https://example.com/",
                "about:blank",
            };
            using (var nav = new AndroidJavaClass("com.zeywinads.unity.ZeyWinAdsWebViewNavigation"))
            {
                foreach (var url in notRouted)
                    Assert.IsFalse(nav.CallStatic<bool>("shouldOpenExternally", url),
                        $"ZeyWinAdsWebViewNavigation.shouldOpenExternally returned true for '{url}' — only " +
                        "whitelisted deep-link schemes should route out.");
            }
        }

        private static void BuildViewIntent()
        {
            // Mirror openExternal's non-intent:// branch: new Intent(ACTION_VIEW, Uri.parse(url)).
            (string url, string scheme)[] cases =
            {
                ("tg://resolve?domain=telegram", "tg"),
                ("market://details?id=com.zeywin.example", "market"),
            };
            foreach (var (url, scheme) in cases)
            {
                AndroidJavaObject uri = null, intent = null, data = null;
                try
                {
                    using (var uriClass = new AndroidJavaClass("android.net.Uri"))
                        uri = uriClass.CallStatic<AndroidJavaObject>("parse", url);
                    intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.VIEW", uri);

                    Assert.AreEqual("android.intent.action.VIEW", intent.Call<string>("getAction"),
                        $"[{scheme}] openExternal would not build an ACTION_VIEW intent for the deep link.");
                    data = intent.Call<AndroidJavaObject>("getData");
                    Assert.AreEqual(scheme, data.Call<string>("getScheme"),
                        $"[{scheme}] the ACTION_VIEW intent's data scheme is wrong.");
                    string dataString = intent.Call<string>("getDataString");
                    Assert.IsTrue((dataString ?? "").StartsWith(scheme + "://"),
                        $"[{scheme}] the ACTION_VIEW intent's data URI '{dataString}' is not a {scheme}:// URI.");
                }
                finally
                {
                    data?.Dispose();
                    intent?.Dispose();
                    uri?.Dispose();
                }
            }
        }

        private static void ParseIntentUriFallback()
        {
            const string url =
                "intent://x/#Intent;scheme=https;package=com.zeywinads.qa.nolauncher;" +
                "S.browser_fallback_url=https%3A%2F%2Fexample.com%2Ffb;end";

            AndroidJavaObject intent = null, resolved = null;
            try
            {
                using (var intentClass = new AndroidJavaClass("android.content.Intent"))
                    intent = intentClass.CallStatic<AndroidJavaObject>("parseUri", url, 1); // URI_INTENT_SCHEME

                Assert.AreEqual("https://example.com/fb",
                    intent.Call<string>("getStringExtra", "browser_fallback_url"),
                    "Intent.parseUri did not surface the browser_fallback_url extra that openExternal reads.");

                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var pm = activity.Call<AndroidJavaObject>("getPackageManager"))
                {
                    resolved = intent.Call<AndroidJavaObject>("resolveActivity", pm);
                    Assert.IsNull(resolved,
                        "The intent:// names a package that isn't installed, so resolveActivity() must be null — " +
                        "the condition under which openExternal falls back to browser_fallback_url.");
                }
            }
            finally
            {
                resolved?.Dispose();
                intent?.Dispose();
            }
        }

        private IEnumerator PopupKeepsOfferOnPage()
        {
            yield return WaitForOfferWebView(); // Inconclusive if no live offer
            Assert.IsTrue(global::ZeyWinAds.UI.WebViewLock.IsLocked,
                "Offer surface is not locked though its WebView is up.");

            var webView = _offerWebView;

            int baseIdx = int.MinValue;
            yield return ReadHistoryIndex(webView, v => baseIdx = v);
            string baseUrl = null;
            yield return ReadWebViewUrl(webView, v => baseUrl = v);
            Debug.Log($"[ZeyWinAds QA] deeplink: offer at index {baseIdx}, url {baseUrl}");

            // window.open(...) on the real offer WebView -> ZeyWinAdsWebChromeClient.onCreateWindow ->
            // routePopupUrl: shouldOpenExternally true -> openExternal(activity, url) -> destroyChild ->
            // return true. The absent package guarantees nothing launches (openExternal's startActivity
            // throws ActivityNotFoundException, swallowed); the parent WebView must stay put.
            yield return EvalJs(webView,
                "window.open('intent://x/#Intent;scheme=https;package=com.zeywinads.qa.nolauncher;end','_blank');'ok'",
                JsResultBudgetSeconds, _ => { });

            yield return new WaitForSecondsRealtime(2f);

            string afterUrl = null;
            yield return ReadWebViewUrl(webView, v => afterUrl = v);
            int afterIdx = int.MinValue;
            yield return ReadHistoryIndex(webView, v => afterIdx = v);
            Debug.Log($"[ZeyWinAds QA] deeplink: after popup — index {afterIdx}, url {afterUrl}");

            Assert.IsFalse((afterUrl ?? "").StartsWith("intent://"),
                $"the offer WebView navigated to the deep link itself (url now '{afterUrl}') — " +
                "window.open('intent://…') was loaded as content instead of being routed to the OS.");
            Assert.IsTrue((afterUrl ?? "").StartsWith("http"),
                $"the offer WebView is no longer on an http(s) page after a popup deep link (url now '{afterUrl}').");
            if (baseIdx != afterIdx)
                Debug.LogWarning($"[ZeyWinAds QA] deeplink: offer history index changed {baseIdx} -> {afterIdx} " +
                    "during the settle window (offer page self-navigated?); relying on the URL assertions.");
            Assert.IsTrue(global::ZeyWinAds.UI.WebViewLock.IsLocked,
                "the offer surface unlocked itself after a popup deep link.");
            Assert.IsNotNull(ReadOfferWebViewField(),
                "the offer WebView was destroyed after a popup deep link.");

            Debug.Log("[ZeyWinAds QA] PopupKeepsOfferOnPage: PASS");
        }

        private IEnumerator ReturnsToPreviousPage()
        {
            var webView = _offerWebView;

            // The offer's own URL, for the teardown restore (first value getUrl() returns twice running).
            string prev = null;
            float capStart = Time.realtimeSinceStartup;
            while (true)
            {
                string cur = null;
                yield return ReadWebViewUrl(webView, v => cur = v);
                if (!string.IsNullOrEmpty(cur) && cur == prev) { _backNavStartUrl = cur; break; }
                prev = cur;
                if (Time.realtimeSinceStartup - capStart >= BackNavNavigateBudgetSeconds)
                {
                    _backNavStartUrl = cur ?? prev;
                    break;
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Debug.Log("[ZeyWinAds QA] back-nav: offer start URL = " + _backNavStartUrl);

            // Gate each navigation on the back/forward CURRENT-INDEX actually advancing — i.e.
            // loadUrl() committed a real history entry. getUrl() reflects the in-flight URL the
            // instant loadUrl() is called, so polling it would let a fast second loadUrl() pre-empt
            // the first before it ever became an entry (that is exactly what failed here).
            int baseIdx = int.MinValue;
            yield return ReadHistoryIndex(webView, v => baseIdx = v);
            Debug.Log("[ZeyWinAds QA] back-nav: baseline history index = " + baseIdx);

            yield return LoadWebViewUrl(webView, BackNavPage1);
            yield return WaitForHistoryCommitPast(webView, baseIdx, BackNavNavigateBudgetSeconds,
                "offer WebView did not commit a history entry for fixture page 1 — device offline or " +
                "ads.zeywin.com unreachable.");
            int page1Idx = int.MinValue;
            yield return ReadHistoryIndex(webView, v => page1Idx = v);
            string page1Url = null;
            yield return ReadWebViewUrl(webView, v => page1Url = v);
            Assert.IsTrue((page1Url ?? "").Contains("zwnav=1"),
                $"fixture page 1 committed a history entry but its URL is not zwnav=1 (got '{page1Url}') — " +
                "the page rewrote the query string.");

            yield return LoadWebViewUrl(webView, BackNavPage2);
            yield return WaitForHistoryCommitPast(webView, page1Idx, BackNavNavigateBudgetSeconds,
                "offer WebView did not commit a history entry for fixture page 2.");
            int page2Idx = int.MinValue;
            yield return ReadHistoryIndex(webView, v => page2Idx = v);

            bool canGoBack = false;
            yield return ReadWebViewCanGoBack(webView, v => canGoBack = v);
            Assert.IsTrue(canGoBack,
                "WebView.canGoBack() is false at fixture page 2 — no history recorded.");

            Debug.Log($"[ZeyWinAds QA] back-nav: history base={baseIdx} page1={page1Idx} page2={page2Idx}; " +
                "pressing system BACK");
            yield return PressSystemBack();

            // goBack() must move exactly one entry: page2Idx -> page1Idx, back onto fixture page 1.
            yield return WaitForHistoryIndexEquals(webView, page1Idx, BackNavSettleBudgetSeconds,
                "system BACK did not move the offer WebView back exactly one history entry. Either the SDK " +
                "back handler did not run (WebViewLock.Update -> Input.GetKeyDown(KeyCode.Escape) -> " +
                "canGoBack/goBack), the KEYCODE_BACK event did not reach Unity's input, or it moved more " +
                "than one entry.");

            // The index is back at page1Idx; wait for the back navigation to actually commit before
            // reading getUrl() (it lags the index by a frame or two — see WaitForWebViewUrlContains).
            yield return WaitForWebViewUrlContains(webView, "zwnav=1", BackNavSettleBudgetSeconds,
                "system BACK moved history to the fixture-page-1 entry but the WebView never committed " +
                "back onto its URL — goBack() re-pointed the list without loading the entry.");

            string afterBack = null;
            yield return ReadWebViewUrl(webView, v => afterBack = v);
            Assert.IsFalse((afterBack ?? "").Contains("zwnav=2"),
                "after system BACK the WebView is still on fixture page 2 — goBack() did not move history.");
            Assert.IsTrue(global::ZeyWinAds.UI.WebViewLock.IsLocked,
                "Offer surface unlocked itself after an in-history BACK.");
            Assert.IsNotNull(ReadOfferWebViewField(),
                "Offer WebView was destroyed after an in-history BACK.");

            Debug.Log("[ZeyWinAds QA] BackNavigation returns-to-previous-page: PASS");
        }

        private IEnumerator KeepsSurfaceOpenOnFirstPage()
        {
            var webView = _offerWebView;

            // Drain any in-page history so we are genuinely on the first page (canGoBack() == false).
            for (int i = 0; i < 6; i++)
            {
                bool canGoBack = false;
                yield return ReadWebViewCanGoBack(webView, v => canGoBack = v);
                if (!canGoBack)
                    break;
                Debug.Log($"[ZeyWinAds QA] back-nav: draining in-page history, press {i + 1}");
                yield return PressSystemBack();
                yield return new WaitForSecondsRealtime(1f);
            }

            bool stillCanGoBack = false;
            yield return ReadWebViewCanGoBack(webView, v => stillCanGoBack = v);
            if (stillCanGoBack)
                Debug.LogWarning("[ZeyWinAds QA] back-nav: WebView still reports canGoBack() after draining — " +
                    "asserting the surface survives BACK anyway (that holds at any history depth).");

            string rootUrl = null;
            yield return ReadWebViewUrl(webView, v => rootUrl = v);
            Debug.Log("[ZeyWinAds QA] back-nav: at history root, URL = " + rootUrl);

            Debug.Log("[ZeyWinAds QA] back-nav: pressing system BACK at history root");
            yield return PressSystemBack();
            yield return new WaitForSecondsRealtime(1.5f);

            Assert.IsNotNull(ReadOfferWebViewField(),
                "System BACK at the WebView history root destroyed the offer WebView — WebViewLock must ignore " +
                "back with no history (no Unlock / finish / destroy).");
            Assert.IsTrue(global::ZeyWinAds.UI.WebViewLock.IsLocked,
                "System BACK at the WebView history root unlocked the offer surface — WebViewLock must ignore " +
                "back with no history.");
            Assert.IsTrue(Application.isPlaying,
                "System BACK at the WebView history root ended the app / dropped it to background instead of " +
                "being swallowed by the offer surface.");

            string afterBack = null;
            yield return ReadWebViewUrl(webView, v => afterBack = v);
            if (!string.IsNullOrEmpty(rootUrl) && !string.IsNullOrEmpty(afterBack))
                Assert.AreEqual(rootUrl, afterBack,
                    "System BACK at the WebView history root navigated the WebView (it should have done nothing).");

            Debug.Log("[ZeyWinAds QA] BackNavigation first-page-no-close: PASS");
        }

        private static AndroidJavaObject ReadOfferWebViewField()
        {
            var f = typeof(global::ZeyWinAds.UI.WebViewLock).GetField(
                "_webView", BindingFlags.NonPublic | BindingFlags.Instance);
            return f?.GetValue(global::ZeyWinAds.UI.WebViewLock.Instance) as AndroidJavaObject;
        }

        private IEnumerator ReadWebViewUrl(AndroidJavaObject webView, Action<string> result)
        {
            string url = null;
            yield return RunOnUiThread(() => url = webView.Call<string>("getUrl"),
                err => Assert.Fail("WebView.getUrl() failed on the UI thread: " + err));
            result(url);
        }

        private IEnumerator ReadWebViewCanGoBack(AndroidJavaObject webView, Action<bool> result)
        {
            bool can = false;
            yield return RunOnUiThread(() => can = webView.Call<bool>("canGoBack"),
                err => Assert.Fail("WebView.canGoBack() failed on the UI thread: " + err));
            result(can);
        }

        private IEnumerator LoadWebViewUrl(AndroidJavaObject webView, string url)
        {
            yield return RunOnUiThread(() => webView.Call("loadUrl", url),
                err => Assert.Fail("WebView.loadUrl(" + url + ") failed on the UI thread: " + err));
        }

        // WebView.copyBackForwardList().getCurrentIndex() — the 0-based position in the back/forward
        // list. Only advances when a navigation actually COMMITS, so it's the reliable "the entry
        // exists now" signal (unlike getUrl(), which returns the in-flight URL immediately).
        private IEnumerator ReadHistoryIndex(AndroidJavaObject webView, Action<int> result)
        {
            int idx = int.MinValue;
            yield return RunOnUiThread(() =>
            {
                using (var list = webView.Call<AndroidJavaObject>("copyBackForwardList"))
                    idx = list.Call<int>("getCurrentIndex");
            }, err => Assert.Fail("WebView.copyBackForwardList().getCurrentIndex() failed on the UI thread: " + err));
            result(idx);
        }

        private IEnumerator WaitForHistoryCommitPast(AndroidJavaObject webView, int fromIndex,
            float budgetSeconds, string failMessage)
        {
            float startedAt = Time.realtimeSinceStartup;
            while (true)
            {
                int idx = int.MinValue;
                yield return ReadHistoryIndex(webView, v => idx = v);
                if (idx > fromIndex)
                {
                    Debug.Log($"[ZeyWinAds QA] back-nav: history advanced {fromIndex} -> {idx}");
                    yield break;
                }
                if (Time.realtimeSinceStartup - startedAt >= budgetSeconds)
                    Assert.Fail(failMessage + $" (history index still {idx} after {budgetSeconds:F0}s)");
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        private IEnumerator WaitForHistoryIndexEquals(AndroidJavaObject webView, int target,
            float budgetSeconds, string failMessage)
        {
            float startedAt = Time.realtimeSinceStartup;
            while (true)
            {
                int idx = int.MinValue;
                yield return ReadHistoryIndex(webView, v => idx = v);
                if (idx == target)
                {
                    Debug.Log($"[ZeyWinAds QA] back-nav: history index is {idx}");
                    yield break;
                }
                if (Time.realtimeSinceStartup - startedAt >= budgetSeconds)
                    Assert.Fail(failMessage + $" (history index {idx}, wanted {target}, after {budgetSeconds:F0}s)");
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        // getCurrentIndex() flips to the target entry as soon as goBack() re-points the back/forward
        // list, but getUrl() keeps returning the OLD entry's URL until the back navigation actually
        // commits a frame or two later. Poll getUrl() for the expected marker instead of reading it
        // once right after the index check (that single read races the commit — the flake this fixes).
        private IEnumerator WaitForWebViewUrlContains(AndroidJavaObject webView, string needle,
            float budgetSeconds, string failMessage)
        {
            float startedAt = Time.realtimeSinceStartup;
            string last = null;
            while (true)
            {
                yield return ReadWebViewUrl(webView, v => last = v);
                if ((last ?? "").Contains(needle))
                {
                    Debug.Log($"[ZeyWinAds QA] back-nav: URL now contains '{needle}' ({last})");
                    yield break;
                }
                if (Time.realtimeSinceStartup - startedAt >= budgetSeconds)
                    Assert.Fail(failMessage + $" (URL still '{last}' after {budgetSeconds:F0}s)");
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        // A real KEYCODE_BACK (DOWN then UP) to the Unity Activity — the exact event the on-screen
        // Back button and the gesture-nav swipe both produce, and what Unity turns into KeyCode.Escape
        // for WebViewLock.Update() to poll. A few frames then pass for the SDK to post its goBack().
        private IEnumerator PressSystemBack()
        {
            yield return DispatchBackKey(0); // KeyEvent.ACTION_DOWN
            yield return null;
            yield return DispatchBackKey(1); // KeyEvent.ACTION_UP
            yield return null;
            yield return null;
            yield return null;
        }

        private IEnumerator DispatchBackKey(int action)
        {
            yield return RunOnUiThread(() =>
            {
                using (var keyEventClass = new AndroidJavaClass("android.view.KeyEvent"))
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    int keycodeBack = keyEventClass.GetStatic<int>("KEYCODE_BACK");
                    using (var evt = new AndroidJavaObject("android.view.KeyEvent", action, keycodeBack))
                        activity.Call<bool>("dispatchKeyEvent", evt);
                }
            }, err => Assert.Fail("dispatchKeyEvent(KEYCODE_BACK) failed: " + err));
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        // Builds the same ACTION_VIEW intent ZeyWinAdsWebViewNavigation.openExternal would, and
        // returns the resolving package name (or null). Does NOT startActivity.
        private static string ResolveViewIntentPackage(string url, AndroidJavaObject packageManager)
        {
            AndroidJavaObject intent = null;
            AndroidJavaObject component = null;
            try
            {
                if (url.StartsWith("intent://", StringComparison.OrdinalIgnoreCase))
                {
                    using (var intentClass = new AndroidJavaClass("android.content.Intent"))
                        intent = intentClass.CallStatic<AndroidJavaObject>("parseUri", url, 1); // URI_INTENT_SCHEME
                }
                else
                {
                    using (var uriClass = new AndroidJavaClass("android.net.Uri"))
                    using (var uri = uriClass.CallStatic<AndroidJavaObject>("parse", url))
                        intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.VIEW", uri);
                }

                component = intent.Call<AndroidJavaObject>("resolveActivity", packageManager);
                return component != null ? component.Call<string>("getPackageName") : null;
            }
            catch
            {
                return null;
            }
            finally
            {
                component?.Dispose();
                intent?.Dispose();
            }
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        // Receives the SDK's existing UnitySendMessage callbacks (same names ZeyWinAdsLockWebViewClient
        // sends to the real lock GameObject) so the test can observe the client without new SDK API.
        private class RedirectChainProbe : MonoBehaviour
        {
            public string LoadError;
            public string LastNavigationUrl;
            // Latched: the first page-load / navigation URL seen carrying "redirect=done". The
            // ads.zeywin.com checklist SPA calls history.replaceState() shortly after landing to
            // strip redirect/hops/mode, so this captures the raw landing URL while it's still there.
            public string RedirectDoneUrl;

            [Preserve]
            public void OnWebViewPageLoaded(string url) { Note(url); }

            [Preserve]
            public void OnWebViewNavigationFinished(string url) { LastNavigationUrl = url ?? ""; Note(url); }

            [Preserve]
            public void OnWebViewLoadError(string error)
            {
                LoadError = string.IsNullOrEmpty(error) ? "WebView load error" : error;
            }

            private void Note(string url)
            {
                if (RedirectDoneUrl == null && !string.IsNullOrEmpty(url) && url.Contains("redirect=done"))
                    RedirectDoneUrl = url;
            }
        }

        // WebView.evaluateJavascript's second parameter is android.webkit.ValueCallback<String> —
        // a genuine Java interface (unlike WebViewClient/WebChromeClient, which are concrete
        // classes), so AndroidJavaProxy can implement it directly with no new .java file needed.
        private class JsValueCallback : AndroidJavaProxy
        {
            private readonly Action<string> _onResult;

            public JsValueCallback(Action<string> onResult) : base("android.webkit.ValueCallback")
            {
                _onResult = onResult;
            }

            [Preserve]
            public void onReceiveValue(string value)
            {
                _onResult?.Invoke(value);
            }
        }
#endif
    }
}
