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
    [TestFixture]
    public class WebViewCapabilityRuntimeTests
    {
        private const float WebViewReadyBudgetSeconds = 60f; // real offer must actually open
        private const float JsResultBudgetSeconds = 10f;
        private const float CookieRoundTripBudgetSeconds = 5f;
        private const float RedirectChainBudgetSeconds = 30f;

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

        private void DestroyProbeWebView()
        {
            if (_probeWebView == null)
                return;

            var webView = _probeWebView;
            var client = _probeWebViewClient;
            _probeWebView = null;
            _probeWebViewClient = null;

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
                }));
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
                    DestroyProbeWebView();
                    UnityEngine.Object.Destroy(probeGo);
                    Assert.Inconclusive($"{redirectUrl} neither resolved nor errored within " +
                        $"{RedirectChainBudgetSeconds:F0}s (network unreachable) — nothing to check.");
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }

            string loadError = probe.LoadError;
            string finalUrl = probe.LastNavigationUrl;
            DestroyProbeWebView();
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
                    DestroyProbeWebView();
                    UnityEngine.Object.Destroy(probeGo);
                    Assert.Inconclusive($"{cleartextUrl} neither resolved nor errored within " +
                        $"{RedirectChainBudgetSeconds:F0}s (network unreachable) — nothing to check.");
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }

            string loadError = probe.LoadError;
            string finalUrl = probe.LastNavigationUrl;
            DestroyProbeWebView();
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
