using System;
using UnityEngine;
using ZeyWinAds.Core;
using Logger = ZeyWinAds.Core.Logger;

namespace ZeyWinAds.UI
{
    /// <summary>
    /// Displays HTML ad content in a native fullscreen WebView.
    /// Provides a JavaScript bridge (window.ZeyWinAds) for the HTML to communicate:
    ///   - ZeyWinAds.close()    — close the ad
    ///   - ZeyWinAds.complete() — signal ad completion (for rewarded ads)
    ///   - ZeyWinAds.openUrl(url) — open a URL in the system browser
    /// </summary>
    public class HtmlAdView : MonoBehaviour
    {
        private const string GAME_OBJECT_NAME = "ZeyWinAds_HtmlAdView";

        private static HtmlAdView _instance;

        /// <summary>Fired when HTML calls ZeyWinAds.close()</summary>
        public event Action OnClose;

        /// <summary>Fired when HTML calls ZeyWinAds.complete()</summary>
        public event Action OnComplete;

        /// <summary>Fired when the HTML page finishes loading</summary>
        public event Action OnPageLoaded;

        /// <summary>Fired on error</summary>
        public event Action<string> OnError;

        private bool _isShowing;
        private GameObject _uniWebViewObject;
        private UniWebView _uniWebView;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _webView;
        private AndroidJavaObject _nativeContainer;
        private AndroidJavaObject _nativeLoadingOverlay;
#endif

#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void _ZeyWinAds_ShowHtmlAd(string url, string gameObjectName);

        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void _ZeyWinAds_DestroyHtmlAd();
#endif

        /// <summary>
        /// Gets or creates the singleton HtmlAdView instance.
        /// </summary>
        public static HtmlAdView Create()
        {
            if (_instance != null)
                return _instance;

            var go = new GameObject(GAME_OBJECT_NAME);
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<HtmlAdView>();
            return _instance;
        }

        /// <summary>Whether the HTML ad WebView is currently showing</summary>
        public bool IsShowing => _isShowing;

        /// <summary>
        /// Shows the HTML ad in a native fullscreen WebView.
        /// </summary>
        /// <param name="url">URL of the HTML ad (from media_url)</param>
        public void Show(string url)
        {
            if (!global::ZeyWinAds.ZeyWinAds.CanShowOfferWebView)
            {
                Logger.Warn("Blocked HTML WebView before eligibility was allowed");
                OnError?.Invoke("WebView is not allowed for this device");
                return;
            }

            if (_isShowing)
            {
                Logger.Warn("HTML ad view is already showing");
                return;
            }

            if (string.IsNullOrEmpty(url))
            {
                Logger.Error("Cannot show HTML ad - URL is empty");
                OnError?.Invoke("Empty URL");
                return;
            }

            _isShowing = true;
            LoadingOverlay.Show();

#if UNITY_EDITOR
            ShowEditorPlaceholder(url);
#else
            ShowUniWebView(url);
#endif
        }

        /// <summary>
        /// Destroys the native WebView and cleans up.
        /// </summary>
        public void DestroyView()
        {
            if (!_isShowing) return;
            _isShowing = false;
            LoadingOverlay.Hide();

#if UNITY_EDITOR
            DestroyEditorPlaceholder();
#else
            DestroyUniWebView();
#endif
        }

        /// <summary>
        /// Removes all event listeners and destroys the view.
        /// </summary>
        public void Cleanup()
        {
            DestroyView();
            OnClose = null;
            OnComplete = null;
            OnPageLoaded = null;
            OnError = null;
        }

        // ============================================================
        // UnitySendMessage callbacks (called from native code)
        // ============================================================

        public void OnJsBridgeClose(string _)
        {
            Logger.Debug("HTML ad: close() called from JS");
            OnClose?.Invoke();
        }

        public void OnJsBridgeComplete(string _)
        {
            Logger.Debug("HTML ad: complete() called from JS");
            OnComplete?.Invoke();
        }

        public void OnJsBridgeOpenUrl(string url)
        {
            Logger.Debug("HTML ad: openUrl() called from JS");
            if (!string.IsNullOrEmpty(url))
            {
                Application.OpenURL(url);
            }
        }

        public void OnJsBridgePageLoaded(string pageUrl)
        {
            Logger.Debug("HTML ad: page loaded");
            RememberResolvedOfferUrl(pageUrl);
            HideNativeLoadingOverlay();
            LoadingOverlay.Hide();
            OnPageLoaded?.Invoke();
        }

        private void RememberResolvedOfferUrl(string pageUrl)
        {
            if (!_isShowing || string.IsNullOrEmpty(pageUrl))
                return;

            string host = GetHost(pageUrl);
            if (!string.IsNullOrEmpty(host) && _uniWebView != null)
                _uniWebView.AddPermissionTrustDomain(host);

            OfferAssignmentStore.PromoteResolvedOfferUrl(pageUrl);
        }

        private static string GetHost(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return uri.Host;

            return null;
        }

        public void OnJsBridgeLoadError(string error)
        {
            Logger.Warn("HTML ad: page load error: {0}", error);
            HideNativeLoadingOverlay();
            FailShow(string.IsNullOrEmpty(error) ? "WebView load error" : error);
        }

        private void FailShow(string error)
        {
            if (_isShowing)
                DestroyView();
            else
                LoadingOverlay.Hide();

            OnError?.Invoke(error);
        }

        private void OnDestroy()
        {
            DestroyView();
            if (_instance == this) _instance = null;
        }

        // ============================================================
        // UniWebView implementation
        // ============================================================

#if !UNITY_EDITOR
        private void ShowUniWebView(string url)
        {
            try
            {
                DestroyUniWebView();

                _uniWebViewObject = new GameObject("ZeyWinAds_UniWebViewHtmlAd");
                DontDestroyOnLoad(_uniWebViewObject);
                _uniWebView = _uniWebViewObject.AddComponent<UniWebView>();

                ConfigureUniWebView(_uniWebView, url);
                _uniWebView.OnPageFinished += (view, statusCode, pageUrl) => OnJsBridgePageLoaded(pageUrl);
                _uniWebView.OnLoadingErrorReceived += (view, errorCode, errorMessage, payload) =>
                {
                    OnJsBridgeLoadError(string.IsNullOrEmpty(errorMessage) ? "WebView load error" : errorMessage);
                };
                _uniWebView.OnMessageReceived += HandleUniWebViewMessage;
                _uniWebView.OnShouldClose += (view) =>
                {
                    OnJsBridgeClose("");
                    return false;
                };

                _uniWebView.AddUrlScheme("zeywinads");
                _uniWebView.AddJavaScript(GetBridgeJavascript());
                _uniWebView.Load(url);
                _uniWebView.Show();

                Logger.Log("UniWebView HTML ad created and loading");
            }
            catch (Exception e)
            {
                Logger.Error("Failed to show UniWebView HTML ad: {0}", e.Message);
                FailShow(e.Message);
            }
        }

        private static void ConfigureUniWebView(UniWebView webView, string url)
        {
            UniWebViewSafety.ApplyOfferDefaults(webView, url, locked: false);
        }

        private static string GetBridgeJavascript()
        {
            return @"(function() {
                if (window.ZeyWinAds) return;
                window.ZeyWinAds = {
                    close: function() { window.location.href = 'zeywinads://close'; },
                    complete: function() { window.location.href = 'zeywinads://complete'; },
                    openUrl: function(url) {
                        window.location.href = 'zeywinads://open?url=' + encodeURIComponent(url || '');
                    }
                };
            })();";
        }

        private void HandleUniWebViewMessage(UniWebView view, UniWebViewMessage message)
        {
            if (!string.Equals(message.Scheme, "zeywinads", StringComparison.OrdinalIgnoreCase))
                return;

            string path = (message.Path ?? "").Trim('/').ToLowerInvariant();
            if (path == "close")
            {
                OnJsBridgeClose("");
                return;
            }

            if (path == "complete")
            {
                OnJsBridgeComplete("");
                return;
            }

            if (path == "open")
            {
                string url = null;
                if (message.Args != null)
                    message.Args.TryGetValue("url", out url);
                OnJsBridgeOpenUrl(url);
            }
        }

        private void DestroyUniWebView()
        {
            if (_uniWebView != null)
            {
                _uniWebView.Hide();
                Destroy(_uniWebView);
                _uniWebView = null;
            }

            if (_uniWebViewObject != null)
            {
                Destroy(_uniWebViewObject);
                _uniWebViewObject = null;
            }
        }
#endif

        // ============================================================
        // Android implementation
        // ============================================================

#if UNITY_ANDROID && !UNITY_EDITOR
        private static int AndroidColor(uint argb)
        {
            return unchecked((int)argb);
        }

        private void ShowAndroid(string url)
        {
            try
            {
                AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        // Create WebView
                        _webView = new AndroidJavaObject("android.webkit.WebView", activity);
                        _webView.Call("setBackgroundColor", unchecked((int)0xFF000000));

                        // Enable hardware acceleration (critical for WebGL content)
                        // LAYER_TYPE_HARDWARE = 2
                        _webView.Call("setLayerType", 2, (AndroidJavaObject)null);

                        // Configure settings
                        AndroidJavaObject settings = _webView.Call<AndroidJavaObject>("getSettings");
                        settings.Call("setJavaScriptEnabled", true);
                        settings.Call("setDomStorageEnabled", true);
                        settings.Call("setLoadWithOverviewMode", true);
                        settings.Call("setUseWideViewPort", true);
                        settings.Call("setMediaPlaybackRequiresUserGesture", false);
                        settings.Call("setAllowFileAccess", true);

                        // Set WebChromeClient for full rendering support (WebGL, fullscreen, etc.)
                        AndroidJavaObject chromeClient = new AndroidJavaObject("android.webkit.WebChromeClient");
                        _webView.Call("setWebChromeClient", chromeClient);

                        // Add JavaScript interface (window.ZeyWinAds)
                        AndroidJavaObject bridge = new AndroidJavaObject(
                            "com.zeywinads.unity.ZeyWinAdsHtmlBridge",
                            GAME_OBJECT_NAME
                        );
                        _webView.Call("addJavascriptInterface", bridge, "ZeyWinAds");

                        // Set custom WebViewClient (intercepts external links)
                        AndroidJavaObject webViewClient = new AndroidJavaObject(
                            "com.zeywinads.unity.ZeyWinAdsHtmlWebViewClient",
                            url,
                            GAME_OBJECT_NAME
                        );
                        _webView.Call("setWebViewClient", webViewClient);

                        // Fullscreen layout params
                        AndroidJavaObject layoutParams = new AndroidJavaObject(
                            "android.widget.FrameLayout$LayoutParams",
                            -1, // MATCH_PARENT
                            -1  // MATCH_PARENT
                        );

                        _nativeContainer = new AndroidJavaObject("android.widget.FrameLayout", activity);
                        _nativeContainer.Call("setBackgroundColor", AndroidColor(0xFF000000));
                        _nativeContainer.Call("addView", _webView, layoutParams);
                        _nativeLoadingOverlay = CreateAndroidLoadingOverlay(activity);
                        _nativeContainer.Call("addView", _nativeLoadingOverlay, layoutParams);

                        // Add to activity's content view. The native loading overlay is
                        // inside the same hierarchy as the WebView, so it stays above it.
                        AndroidJavaObject decorView = activity.Call<AndroidJavaObject>("getWindow")
                            .Call<AndroidJavaObject>("getDecorView");
                        AndroidJavaObject contentView = decorView.Call<AndroidJavaObject>("findViewById",
                            new AndroidJavaClass("android.R$id").GetStatic<int>("content"));

                        contentView.Call("addView", _nativeContainer, layoutParams);
                        PromoteAndroidLoadingOverlay();

                        // Load URL
                        _webView.Call("loadUrl", url);

                        Logger.Log("Android HTML WebView created");
                    }
                    catch (Exception e)
                    {
                        string message = e.Message;
                        Logger.Error("Failed to create Android HTML WebView: {0}", message);
                        UnityMainThreadDispatcher.Instance.Enqueue(() => FailShow(message));
                    }
                }));
            }
            catch (Exception e)
            {
                Logger.Error("Failed to show Android HTML ad: {0}", e.Message);
                FailShow(e.Message);
            }
        }

        private void DestroyAndroid()
        {
            if (_webView == null && _nativeContainer == null) return;

            try
            {
                AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

                var containerRef = _nativeContainer;
                var webViewRef = _webView;
                var loadingRef = _nativeLoadingOverlay;
                _nativeContainer = null;
                _webView = null;
                _nativeLoadingOverlay = null;

                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        AndroidJavaObject decorView = activity.Call<AndroidJavaObject>("getWindow")
                            .Call<AndroidJavaObject>("getDecorView");
                        AndroidJavaObject contentView = decorView.Call<AndroidJavaObject>("findViewById",
                            new AndroidJavaClass("android.R$id").GetStatic<int>("content"));

                        if (containerRef != null)
                        {
                            contentView.Call("removeView", containerRef);
                        }
                        if (webViewRef != null)
                        {
                            webViewRef.Call("destroy");
                            webViewRef.Dispose();
                        }
                        loadingRef?.Dispose();
                        containerRef?.Dispose();

                        Logger.Debug("Android HTML WebView destroyed");
                    }
                    catch (Exception e)
                    {
                        Logger.Error("Failed to destroy Android HTML WebView: {0}", e.Message);
                    }
                }));
            }
            catch (Exception e)
            {
                Logger.Error("Failed to destroy Android HTML ad: {0}", e.Message);
            }
        }

        private AndroidJavaObject CreateAndroidLoadingOverlay(AndroidJavaObject activity)
        {
            var overlay = new AndroidJavaObject("android.widget.FrameLayout", activity);
            overlay.Call("setBackgroundColor", AndroidColor(0xFF000000));
            overlay.Call("setClickable", true);

            var box = new AndroidJavaObject("android.widget.LinearLayout", activity);
            box.Call("setOrientation", 1);
            box.Call("setGravity", 17);

            var progress = new AndroidJavaObject("android.widget.ProgressBar", activity);
            progress.Call("setIndeterminate", true);
            var text = new AndroidJavaObject("android.widget.TextView", activity);
            text.Call("setText", "Loading");
            text.Call("setTextColor", AndroidColor(0xFFFFFFFF));
            text.Call("setTextSize", 18f);
            text.Call("setGravity", 17);

            var wrapParams = new AndroidJavaObject("android.widget.LinearLayout$LayoutParams", -2, -2);
            box.Call("addView", progress, wrapParams);
            box.Call("addView", text, wrapParams);

            var boxParams = new AndroidJavaObject("android.widget.FrameLayout$LayoutParams", -2, -2);
            boxParams.Set<int>("gravity", 17);
            overlay.Call("addView", box, boxParams);

            return overlay;
        }

        private void PromoteAndroidLoadingOverlay()
        {
            if (_nativeLoadingOverlay == null)
                return;

            try
            {
                _nativeLoadingOverlay.Call("setVisibility", 0);
                _nativeLoadingOverlay.Call("bringToFront");
                _nativeLoadingOverlay.Call("setElevation", 100000f);
                _nativeLoadingOverlay.Call("setTranslationZ", 100000f);
                _nativeLoadingOverlay.Call("invalidate");
                _nativeContainer?.Call("invalidate");
            }
            catch (Exception e)
            {
                Logger.Warn("Failed to promote Android HTML loading overlay: {0}", e.Message);
            }
        }

        private void HideNativeLoadingOverlay()
        {
            if (_nativeLoadingOverlay == null)
                return;

            try
            {
                AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                var overlay = _nativeLoadingOverlay;
                _nativeLoadingOverlay = null;
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        overlay.Call("setVisibility", 8);
                        overlay.Dispose();
                    }
                    catch (Exception e)
                    {
                        Logger.Warn("Failed to hide Android HTML loading overlay: {0}", e.Message);
                    }
                }));
            }
            catch (Exception e)
            {
                Logger.Warn("Failed to schedule Android HTML loading overlay hide: {0}", e.Message);
            }
        }
#else
        private void HideNativeLoadingOverlay() {}
#endif

        // ============================================================
        // iOS implementation
        // ============================================================

#if UNITY_IOS && !UNITY_EDITOR
        private void ShowiOS(string url)
        {
            try
            {
                _ZeyWinAds_ShowHtmlAd(url, GAME_OBJECT_NAME);
                Logger.Log("iOS HTML WebView shown");
            }
            catch (Exception e)
            {
                Logger.Error("Failed to show iOS HTML ad: {0}", e.Message);
                FailShow(e.Message);
            }
        }

        private void DestroyiOS()
        {
            try
            {
                _ZeyWinAds_DestroyHtmlAd();
                Logger.Debug("iOS HTML WebView destroyed");
            }
            catch (Exception e)
            {
                Logger.Error("Failed to destroy iOS HTML ad: {0}", e.Message);
            }
        }
#endif

        // ============================================================
        // Editor placeholder (for testing in Unity Editor)
        // ============================================================

#if UNITY_EDITOR
        private GameObject _editorContainer;

        private void ShowEditorPlaceholder(string url)
        {
            Logger.Debug("Editor: HTML ad would show");
            LoadingOverlay.Hide();

            _editorContainer = new GameObject("HtmlAdEditorPlaceholder");
            _editorContainer.transform.SetParent(transform);

            var canvas = _editorContainer.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31000;

            var scaler = _editorContainer.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);

            _editorContainer.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            // Dark background
            var bg = new GameObject("Background");
            bg.transform.SetParent(_editorContainer.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            var bgImage = bg.AddComponent<UnityEngine.UI.Image>();
            bgImage.color = new Color(0.05f, 0.05f, 0.1f, 1f);

            // Info text
            var textObj = new GameObject("InfoText");
            textObj.transform.SetParent(_editorContainer.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.1f, 0.5f);
            textRect.anchorMax = new Vector2(0.9f, 0.75f);
            textRect.sizeDelta = Vector2.zero;
            var text = textObj.AddComponent<UnityEngine.UI.Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 22;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = $"[EDITOR] HTML Ad\n\n{url}\n\nOn device this is a fullscreen WebView.\n" +
                        "Use buttons below to simulate JS callbacks.";

            // Close button
            CreateEditorButton("CloseButton", "Simulate close()", new Color(0.8f, 0.2f, 0.2f),
                new Vector2(0.3f, 0.28f), new Vector2(0.7f, 0.35f), () => OnJsBridgeClose(""));

            // Complete button
            CreateEditorButton("CompleteButton", "Simulate complete()", new Color(0.2f, 0.7f, 0.2f),
                new Vector2(0.3f, 0.18f), new Vector2(0.7f, 0.25f), () => OnJsBridgeComplete(""));
        }

        private void CreateEditorButton(string name, string label, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Action onClick)
        {
            var btnObj = new GameObject(name);
            btnObj.transform.SetParent(_editorContainer.transform, false);
            var btnRect = btnObj.AddComponent<RectTransform>();
            btnRect.anchorMin = anchorMin;
            btnRect.anchorMax = anchorMax;
            btnRect.sizeDelta = Vector2.zero;
            var btnImage = btnObj.AddComponent<UnityEngine.UI.Image>();
            btnImage.color = color;
            var btn = btnObj.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = btnImage;
            btn.onClick.AddListener(() => onClick());

            var txtObj = new GameObject("Text");
            txtObj.transform.SetParent(btnObj.transform, false);
            var txtRect = txtObj.AddComponent<RectTransform>();
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.sizeDelta = Vector2.zero;
            var txt = txtObj.AddComponent<UnityEngine.UI.Text>();
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 20;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.text = label;
        }

        private void DestroyEditorPlaceholder()
        {
            if (_editorContainer != null)
            {
                Destroy(_editorContainer);
                _editorContainer = null;
            }
        }
#endif
    }
}
