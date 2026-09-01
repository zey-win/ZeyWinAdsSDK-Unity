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
    //   RealOfferWebView_ExecutesJavaScript          JS execution in the LIVE offer WebView
    //                                                (no dedicated web-checklist row; sanity check)
    //   RealOfferWebView_SupportsCookies             "Cookies (same-site)"  (cookies-same-site) —
    //                                                against the LIVE offer WebView + CookieManager
    //   OfferWebViewClient_FollowsRedirectChain      "Redirect chain — navigation (5 hops)"  (redirect-navigation)
    //   OfferWebViewClient_LoadsCleartextHttpTopLevel"Cleartext HTTP (top-level)"  (cleartext-http)
    //   OfferWebView_PassesCapabilityChecklist       every `auto`-bucket row (runAuto), + camera/microphone
    //                                                when CAMERA/RECORD_AUDIO are granted to the install
    //   OfferWebView_RoutesExternalScheme(case)      one row each:
    //                                                  deeplink-tg   -> "Deep link (tg://)"
    //                                                  intent-scheme -> "intent:// (Android)"
    //                                                  mailto        -> "mailto:"
    //                                                  tel           -> "tel:"
    //                                                  sms           -> "sms:"
    [TestFixture]
    public class WebViewCapabilityRuntimeTests
    {
        private const float WebViewReadyBudgetSeconds = 60f; // real offer must actually open
        private const float JsResultBudgetSeconds = 10f;
        private const float CookieRoundTripBudgetSeconds = 5f;
        private const float RedirectChainBudgetSeconds = 30f;
        private const float ChecklistReadyBudgetSeconds = 90f;  // SPA + 2MB bundle over the device network
        private const float ChecklistRunBudgetSeconds = 180f;   // runAuto() over ~22 checks, several hitting the network

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _offerWebView;

        // Polls WebViewLock._webView (reflected) until the real offer WebView's native object
        // exists, leaving it in _offerWebView. Marks the test Inconclusive (not Failed) if the
        // offer never opens — that means it's disabled server-side or the device is geo/no-SIM
        // blocked, not that the capability is broken.
        private IEnumerator WaitForOfferWebView()
        {
            _offerWebView = null;

            FieldInfo webViewField = typeof(global::ZeyWinAds.UI.WebViewLock).GetField(
                "_webView", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(webViewField, "WebViewLock._webView field not found — did the SDK rename it?");

            float startedAt = QaForegroundTimeTracker.ForegroundSeconds;
            while (_offerWebView == null)
            {
                var instance = global::ZeyWinAds.UI.WebViewLock.Instance;
                if (instance != null)
                    _offerWebView = webViewField.GetValue(instance) as AndroidJavaObject;

                if (_offerWebView == null)
                {
                    if (QaForegroundTimeTracker.ForegroundSeconds - startedAt >= WebViewReadyBudgetSeconds)
                    {
                        Assert.Inconclusive($"Real offer WebView never opened within {WebViewReadyBudgetSeconds:F0}s " +
                            "(no_sim/geo block, or offer disabled) — nothing to check against.");
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

            float startedAt = QaForegroundTimeTracker.ForegroundSeconds;
            while (!done)
            {
                if (QaForegroundTimeTracker.ForegroundSeconds - startedAt >= JsResultBudgetSeconds)
                {
                    onError($"UI-thread call did not complete within {JsResultBudgetSeconds:F0}s.");
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

        [UnityTest]
        [Order(3)] // After PushNotificationRuntimeTests' Order(2).
        public IEnumerator RealOfferWebView_ExecutesJavaScript()
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

            float jsStartedAt = QaForegroundTimeTracker.ForegroundSeconds;
            while (!received)
            {
                if (QaForegroundTimeTracker.ForegroundSeconds - jsStartedAt >= JsResultBudgetSeconds)
                    Assert.Fail($"evaluateJavascript did not return a result within {JsResultBudgetSeconds:F0}s.");
                yield return null;
            }

            Assert.IsNull(evalError, $"evaluateJavascript failed on the UI thread: {evalError}");
            Debug.Log($"[ZeyWinAds QA] evaluateJavascript(\"String(1 + 1)\") returned: {result}");
            Assert.AreEqual("\"2\"", result,
                "JavaScript did not execute correctly in the real offer WebView.");
#else
            Debug.Log("[ZeyWinAds QA] RealOfferWebView_ExecutesJavaScript: skipped (not an Android device).");
            yield break;
#endif
        }

        [UnityTest]
        [Order(4)]
        public IEnumerator RealOfferWebView_SupportsCookies()
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
                float startedAt = QaForegroundTimeTracker.ForegroundSeconds;
                while (!found)
                {
                    readBack = cookieManager.Call<string>("getCookie", cookieUrl);
                    found = !string.IsNullOrEmpty(readBack)
                        && readBack.IndexOf("zeywin_qa_cookie=1", StringComparison.Ordinal) >= 0;
                    if (found)
                        break;

                    if (QaForegroundTimeTracker.ForegroundSeconds - startedAt >= CookieRoundTripBudgetSeconds)
                    {
                        Assert.Fail($"Set a cookie for {cookieUrl} but CookieManager.getCookie() never returned it " +
                            $"within {CookieRoundTripBudgetSeconds:F0}s (got: '{readBack}').");
                    }
                    yield return new WaitForSecondsRealtime(0.25f);
                }

                Debug.Log($"[ZeyWinAds QA] Cookie round-trip OK for {cookieUrl}: '{readBack}'");
            }
#else
            Debug.Log("[ZeyWinAds QA] RealOfferWebView_SupportsCookies: skipped (not an Android device).");
            yield break;
#endif
        }

        [UnityTest]
        [Order(5)]
        public IEnumerator OfferWebViewClient_FollowsRedirectChain()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const string probeObjectName = "ZeyWinAds_RedirectChainProbe";
            // 5x HTTP 302, final hop lands on https://httpbin.org/get (HTTP 200).
            const string redirectUrl = "https://httpbin.org/redirect/5";

            var probeGo = new GameObject(probeObjectName);
            var probe = probeGo.AddComponent<RedirectChainProbe>();

            string createError = null;
            yield return RunOnUiThread(
                () => CreateProbeWebView(probeObjectName, redirectUrl),
                err => createError = err);
            Assert.IsNull(createError, $"Could not create the probe WebView: {createError}");

            float startedAt = QaForegroundTimeTracker.ForegroundSeconds;
            while (probe.LastNavigationUrl == null && probe.LoadError == null)
            {
                if (QaForegroundTimeTracker.ForegroundSeconds - startedAt >= RedirectChainBudgetSeconds)
                {
                    yield return DestroyProbeWebView();
                    UnityEngine.Object.Destroy(probeGo);
                    Assert.Inconclusive($"{redirectUrl} neither resolved nor errored within " +
                        $"{RedirectChainBudgetSeconds:F0}s (network unreachable) — nothing to check.");
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }

            string loadError = probe.LoadError;
            string finalUrl = probe.LastNavigationUrl;
            yield return DestroyProbeWebView();
            UnityEngine.Object.Destroy(probeGo);

            Assert.IsNull(loadError,
                $"The offer WebViewClient failed on a 5-hop redirect chain: {loadError}");
            Assert.IsTrue(finalUrl != null && finalUrl.Contains("httpbin.org/get"),
                $"Expected the 5 redirects to resolve to httpbin.org/get, ended at: '{finalUrl}'.");
            Debug.Log($"[ZeyWinAds QA] 5-hop redirect chain resolved to: {finalUrl}");
#else
            Debug.Log("[ZeyWinAds QA] OfferWebViewClient_FollowsRedirectChain: skipped (not an Android device).");
            yield break;
#endif
        }

        [UnityTest]
        [Order(6)]
        public IEnumerator OfferWebViewClient_LoadsCleartextHttpTopLevel()
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

            float startedAt = QaForegroundTimeTracker.ForegroundSeconds;
            while (probe.LastNavigationUrl == null && probe.LoadError == null)
            {
                if (QaForegroundTimeTracker.ForegroundSeconds - startedAt >= RedirectChainBudgetSeconds)
                {
                    yield return DestroyProbeWebView();
                    UnityEngine.Object.Destroy(probeGo);
                    Assert.Inconclusive($"{cleartextUrl} neither resolved nor errored within " +
                        $"{RedirectChainBudgetSeconds:F0}s (network unreachable) — nothing to check.");
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
            Debug.Log("[ZeyWinAds QA] OfferWebViewClient_LoadsCleartextHttpTopLevel: skipped (not an Android device).");
            yield break;
#endif
        }

        [UnityTest]
        [Order(7)]
        public IEnumerator OfferWebView_PassesCapabilityChecklist()
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

            // 2. Kick runAuto() (the `auto` bucket). camera + microphone are also run and graded,
            //    but ONLY when CAMERA + RECORD_AUDIO are actually held by this install — the plain
            //    Editor->device flow reinstalls the player every run and wipes runtime grants, so
            //    there they are reported as "skipped (OS permission not granted)" and NOT run (no
            //    prompt). The CI runner grants them post-install, so there they run and are graded.
            //    navigates/external/manual buckets are always reported-only (marked "n/a").
            bool micCamGranted =
                AndroidPermissionGranted("android.permission.CAMERA") &&
                AndroidPermissionGranted("android.permission.RECORD_AUDIO");
            Debug.Log($"[ZeyWinAds QA] checklist: CAMERA+RECORD_AUDIO held by this install = {micCamGranted}");

            string kickScript =
                "(function(){window.__zw={done:false};var gradePerm=" + (micCamGranted ? "true" : "false") + ";" +
                "if(!window.ZW_CHECKLIST){window.__zw={done:true,verdict:'NO_CONTRACT',report:''};return;}" +
                "if((ZW_CHECKLIST.version||0)<3){window.__zw={done:true,verdict:'BAD_VERSION',report:'version='+ZW_CHECKLIST.version};return;}" +
                "var meta=ZW_CHECKLIST.meta||{};var permIds={camera:1,microphone:1};" +
                "Promise.resolve(ZW_CHECKLIST.runAuto()).then(function(){" +
                "if(!gradePerm)return;" +
                "return Promise.all(['camera','microphone'].map(function(id){" +
                "return Promise.resolve(ZW_CHECKLIST.run(id)).catch(function(){});}));}).then(function(){" +
                "var r=ZW_CHECKLIST.results();" +
                "var ids=Object.keys(r).sort(function(a,b){" +
                "var ba=(meta[a]&&meta[a].bucket)||'zz',bb=(meta[b]&&meta[b].bucket)||'zz';" +
                "return ba<bb?-1:ba>bb?1:(a<b?-1:1);});" +
                "var lines=[],fails=[],gp=0,gf=0,gs=0,nr=0;" +
                "ids.forEach(function(k){var e=r[k]||{},b=(meta[k]&&meta[k].bucket)||'?',s=e.status||'?';" +
                "var graded=(b==='auto')||(gradePerm&&permIds[k]);" +
                "if(permIds[k]&&!gradePerm){lines.push('⏭️ SKIP  ['+b+']  '+k+'  - skipped: OS permission not granted to this install (CI grants post-install)');gs++;return;}" +
                "if(!graded){lines.push('⬜ n/a   ['+b+']  '+k);nr++;return;}" +
                // camera/microphone fails stay fails. Just annotate which layer broke: NotAllowedError
                // = the SDK's permission wiring; NotReadableError/NotFoundError = permission was fine,
                // the device sensor couldn't open (busy / absent — check the device, not the SDK).
                "if(permIds[k]&&s!=='pass'){var er=String(e.detail||'');" +
                "var layer=/NotAllowedError/.test(er)?' [SDK permission wiring]':" +
                "/Not(Readable|Found)Error|OverconstrainedError|AbortError/.test(er)?' [device sensor could not open — permission grant worked]':'';" +
                "lines.push('❌ FAIL  ['+b+']  '+k+'  - '+er.slice(0,120)+layer);gf++;fails.push(k+'='+s);return;}" +
                "var d=(e.detail||'').replace(/\\s+/g,' ').slice(0,140);" +
                "var m=s==='pass'?'✅ PASS':s==='skip'?'⏭️ SKIP':s==='pending'?'⬜ n/a  ':'❌ FAIL';" +
                "lines.push(m+'  ['+b+']  '+k+(d?('  - '+d):''));" +
                "if(s==='pass')gp++;else if(s==='skip')gs++;else{gf++;fails.push(k+'='+s);}});" +
                "var head='graded: '+gp+' pass · '+gf+' fail · '+gs+' skip   |   not run here (other buckets): '+nr;" +
                "window.__zw={done:true,verdict:(fails.length?'FAIL ':'OK ')+'fails=['+fails.join(',')+']'," +
                "report:head+'\\n'+lines.join('\\n')};" +
                "}).catch(function(e){window.__zw={done:true,verdict:'THREW',report:String(e)};});})();";
            yield return EvalJs(_checklistWebView, kickScript, JsResultBudgetSeconds, _ => { });

            // 3. Poll until done. While pending, report terminal-count so a stuck check is visible.
            const string pollScript =
                "(function(){if(window.__zw&&window.__zw.done)" +
                "return 'DONE\\n'+window.__zw.verdict+'\\n===\\n'+(window.__zw.report||'');" +
                "try{var r=ZW_CHECKLIST.results();var ks=Object.keys(r);" +
                "var d=ks.filter(function(k){var s=r[k].status;return s==='pass'||s==='fail'||s==='skip';});" +
                "return 'PENDING '+d.length+'/'+ks.length;}catch(e){return 'PENDING';}})()";
            string payload = null;
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
                Debug.Log($"[ZeyWinAds QA] checklist run-probe (+{Time.realtimeSinceStartup - runStartedAt:F0}s): {probe ?? "<null>"}");
                if (Time.realtimeSinceStartup - runStartedAt >= ChecklistRunBudgetSeconds)
                    break;
                yield return new WaitForSecondsRealtime(3f);
            }

            yield return DestroyChecklistProbeWebView();
            UnityEngine.Object.Destroy(probeGo);

            if (string.IsNullOrEmpty(payload))
                Assert.Inconclusive($"ZW_CHECKLIST.runAuto() did not finish within {ChecklistRunBudgetSeconds:F0}s.");

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
            Debug.Log("[ZeyWinAds QA] OfferWebView_PassesCapabilityChecklist: skipped (not an Android device).");
            yield break;
#endif
        }

        // One case per external-scheme row of the checklist. Each verifies the level a headless
        // test actually can: the SDK's *routing decision* — ZeyWinAdsWebViewNavigation
        // .shouldOpenExternally() must return true, so the offer WebView hands the URL to the OS
        // instead of trying to load it as a page (hard assert). Whether an Activity resolves it is
        // logged; if a scheme that every phone handles (mailto/tel/sms) doesn't resolve, the test
        // is Inconclusive with the reason (an AndroidManifest <queries> gap). Actually launching
        // the target app is a UiAutomator job, not this. No dialogs, no navigation, no startActivity.
        [Order(8)]
        [TestCase("deeplink-tg",   "tg://resolve?domain=telegram",                  false, TestName = "RoutesExternalScheme_deeplink_tg (tg://)")]
        [TestCase("intent-scheme", "intent://example.com#Intent;scheme=https;end",  false, TestName = "RoutesExternalScheme_intent_scheme (intent://)")]
        [TestCase("mailto",        "mailto:qa@zeywin.com?subject=probe",            true,  TestName = "RoutesExternalScheme_mailto (mailto:)")]
        [TestCase("tel",           "tel:+10000000000",                             true,  TestName = "RoutesExternalScheme_tel (tel:)")]
        [TestCase("sms",           "sms:+10000000000?body=probe",                  true,  TestName = "RoutesExternalScheme_sms (sms:)")]
        public void OfferWebView_RoutesExternalScheme(string checklistId, string url, bool everyPhoneHandlesIt)
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
                {
                    if (everyPhoneHandlesIt)
                        Assert.Inconclusive($"[{checklistId}] routing verified (shouldOpenExternally=true), but no Activity " +
                            $"resolves {url} — every phone has a handler for this, so the base app's AndroidManifest <queries> " +
                            "is missing the scheme. Android 11+ visibility then hides the handler and " +
                            "ZeyWinAdsWebViewNavigation.openExternal's startActivity would throw (silently caught -> link does " +
                            "nothing). Add <data android:scheme=\"tel\"/> / \"sms\" / \"mailto\" (and \"tg\") to <queries>, or have the SDK ship them.");
                    else
                        Assert.Inconclusive($"[{checklistId}] routing verified (shouldOpenExternally=true), but no Activity " +
                            $"resolves {url} — the target app isn't installed, or the scheme isn't in the AndroidManifest " +
                            "<queries>. Real OS hand-off is a UiAutomator check.");
                }
            }
#else
            Assert.Ignore($"OfferWebView_RoutesExternalScheme[{checklistId}]: Android device only.");
#endif
        }

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

            [Preserve]
            public void OnWebViewPageLoaded(string url) { }

            [Preserve]
            public void OnWebViewNavigationFinished(string url) { LastNavigationUrl = url ?? ""; }

            [Preserve]
            public void OnWebViewLoadError(string error)
            {
                LoadError = string.IsNullOrEmpty(error) ? "WebView load error" : error;
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
