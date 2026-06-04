using System;
using UnityEngine;
using ZeyWinAds.Core;
using ZeyWinAds.Mediation;
using Logger = ZeyWinAds.Core.Logger;

namespace ZeyWinAds.UI
{
    /// <summary>
    /// Manages a fullscreen webview that locks the application.
    /// When lock_webview is true, the app is completely covered by a webview
    /// that persists across app restarts.
    /// </summary>
    public class WebViewLock : MonoBehaviour
    {
        private const string LOCK_URL_KEY = "ZeyWinAds_LockWebViewUrl";
        private const string LOCK_ACTIVE_KEY = "ZeyWinAds_LockWebViewActive";
        private const string GAME_OBJECT_NAME = "ZeyWinAds_WebViewLock";
#if UNITY_ANDROID
        private const float AndroidOfferSurfaceElevation = 1000000f;
#endif

        private static WebViewLock _instance;
        public static WebViewLock Instance => _instance;
        public static event Action<string> OnLocked;
        public static event Action OnUnlocked;

        private GameObject _webViewContainer;
        private GameObject _uniWebViewObject;
        private UniWebView _uniWebView;
        private bool _isLocked;
        private bool _zeyWinSurfaceActive;
        private string _lockedUrl;

#if UNITY_ANDROID
        private AndroidJavaObject _webView;
        private AndroidJavaObject _webViewClient;
        private AndroidJavaObject _nativeWebViewContainer;
        private AndroidJavaObject _nativeSafeAreaContainer;
        private AndroidJavaObject _permissionBridge;
#endif

#if UNITY_IOS
        private IntPtr _webViewPtr;
#endif

        /// <summary>
        /// Whether the app is currently locked with a webview
        /// </summary>
        public static bool IsLocked => _instance != null && _instance._isLocked;
        public static string CurrentLockedUrl => _instance != null ? _instance._lockedUrl : null;
        internal static bool HasPersistedLock =>
            PlayerPrefs.GetInt(LOCK_ACTIVE_KEY, 0) == 1
            && !string.IsNullOrEmpty(PlayerPrefs.GetString(LOCK_URL_KEY, ""));

        /// <summary>
        /// Initializes the WebViewLock system. Call this on app startup.
        /// </summary>
        public static void Initialize(bool restoreExistingLock = false)
        {
            if (_instance != null)
                return;

            var go = new GameObject(GAME_OBJECT_NAME);
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<WebViewLock>();
            if (restoreExistingLock)
                _instance.CheckAndRestoreLock();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
            DestroyWebView();
        }

        /// <summary>
        /// Checks if there's a persisted lock and restores it
        /// </summary>
        private void CheckAndRestoreLock()
        {
            if (PlayerPrefs.GetInt(LOCK_ACTIVE_KEY, 0) == 1)
            {
                string url = PlayerPrefs.GetString(LOCK_URL_KEY, "");
                if (!string.IsNullOrEmpty(url))
                {
                    Logger.Log("Restoring locked webview");
                    LockWithUrl(url, false); // Don't re-save, it's already saved
                }
            }
        }

        internal static void RestorePersistedLockIfAllowed()
        {
            if (!global::ZeyWinAds.ZeyWinAds.CanShowOfferWebView)
            {
                Logger.Warn("Blocked persisted WebView restore before eligibility was allowed");
                return;
            }

            if (_instance == null)
                Initialize(restoreExistingLock: false);

            _instance.CheckAndRestoreLock();
        }

        /// <summary>
        /// Locks the application with a fullscreen webview showing the specified URL.
        /// If a Google Ads gclid was captured from the Play Install Referrer at install
        /// time, it is appended to the URL as sub_id_4 so the partner page can later
        /// report offline conversions back to Google Ads.
        /// </summary>
        public static void Lock(string url)
        {
            if (!global::ZeyWinAds.ZeyWinAds.CanShowOfferWebView)
            {
                Logger.Warn("Blocked WebView lock before eligibility was allowed");
                return;
            }

            if (_instance == null)
            {
                Initialize(restoreExistingLock: false);
            }
            _instance.LockWithUrl(url, true);
        }

        /// <summary>
        /// Appends Google Ads click identifiers captured at install time:
        ///   sub_id_2 = wbraid (app→web click ID)
        ///   sub_id_3 = gbraid (web→app click ID)
        ///   sub_id_4 = gclid  (classic Google click ID)
        /// Each param is added only if non-empty. Replaces existing values to
        /// keep the URL canonical (re-enriching is idempotent).
        /// </summary>
        private static string EnrichWithGclid(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;

            string gclid = GoogleAdsAttribution.GetGclid();
            string gbraid = GoogleAdsAttribution.GetGbraid();
            string wbraid = GoogleAdsAttribution.GetWbraid();

            if (!string.IsNullOrEmpty(wbraid))
                url = UrlHelper.SetQueryParam(url, "sub_id_2", wbraid);
            if (!string.IsNullOrEmpty(gbraid))
                url = UrlHelper.SetQueryParam(url, "sub_id_3", gbraid);
            if (!string.IsNullOrEmpty(gclid))
                url = UrlHelper.SetQueryParam(url, "sub_id_4", gclid);

            return url;
        }

        private void LockWithUrl(string url, bool persist)
        {
            if (string.IsNullOrEmpty(url))
            {
                Logger.Warn("Cannot lock with empty URL");
                return;
            }

            // Keep the first assigned offer stable for this device, then enrich
            // the chosen URL for the current WebView load.
            url = OfferAssignmentStore.GetOrAssignOfferUrl(url);
            url = EnrichWithGclid(url);

            _lockedUrl = url;
            _isLocked = true;
            LoadingOverlay.Show();

            // Mute all Unity audio while webview is active
            AudioListener.pause = true;
            BeginZeyWinSurface();

            if (persist)
            {
                PlayerPrefs.SetString(LOCK_URL_KEY, url);
                PlayerPrefs.SetInt(LOCK_ACTIVE_KEY, 1);
                PlayerPrefs.Save();
                Logger.Log("Locking app with webview");
            }

            ShowWebView(url);
            OnLocked?.Invoke(url);
        }

        /// <summary>
        /// Unlocks the application (removes the webview lock)
        /// </summary>
        public static void Unlock()
        {
            if (_instance != null)
            {
                _instance.UnlockInternal();
            }

            PlayerPrefs.DeleteKey(LOCK_URL_KEY);
            PlayerPrefs.SetInt(LOCK_ACTIVE_KEY, 0);
            PlayerPrefs.Save();
        }

        private void UnlockInternal()
        {
            _isLocked = false;
            _lockedUrl = null;

            // Restore Unity audio
            AudioListener.pause = false;

            DestroyWebView();
            LoadingOverlay.Hide();
            Logger.Log("WebView lock removed");
            OnUnlocked?.Invoke();
        }

        public void OnWebViewPageLoaded(string _)
        {
            Logger.Debug("Locked WebView page loaded");
            HideNativeLoadingOverlay();
            PromoteAndroidOfferSurface();
            LoadingOverlay.ForceHide();
        }

        public void OnWebViewLoadError(string error)
        {
            Logger.Warn("Locked WebView load error: {0}", error);
            HideNativeLoadingOverlay();
            PromoteAndroidOfferSurface();
            LoadingOverlay.ForceHide();
        }

        private void HideLoadingAfterShowFailure()
        {
            LoadingOverlay.Hide();
        }

        private void HandleUniWebViewPageFinished(string pageUrl)
        {
            RememberResolvedOfferUrl(pageUrl);
            OnWebViewPageLoaded(pageUrl);
        }

        private void RememberResolvedOfferUrl(string pageUrl)
        {
            if (!_isLocked || string.IsNullOrEmpty(pageUrl))
                return;

            string host = GetHost(pageUrl);
            if (!string.IsNullOrEmpty(host) && _uniWebView != null)
                _uniWebView.AddPermissionTrustDomain(host);

            if (!OfferAssignmentStore.PromoteResolvedOfferUrl(pageUrl))
                return;

            Logger.Debug("Locked WebView final navigation observed without replacing sticky offer URL");
        }

        private void ShowWebView(string url)
        {
#if UNITY_EDITOR
            ShowEditorFallback(url);
#elif UNITY_ANDROID
            ShowAndroidWebView(url);
#elif UNITY_IOS
            ShowiOSWebView(url);
#else
            ShowUniWebView(url);
#endif
        }

        private void DestroyWebView()
        {
            DestroyUniWebView();
#if UNITY_ANDROID && !UNITY_EDITOR
            DestroyAndroidWebView();
#elif UNITY_IOS && !UNITY_EDITOR
            DestroyiOSWebView();
#endif
            if (_webViewContainer != null)
            {
                Destroy(_webViewContainer);
                _webViewContainer = null;
            }
        }

        private void ShowUniWebView(string url)
        {
            try
            {
                DestroyUniWebView();

                _uniWebViewObject = new GameObject("ZeyWinAds_UniWebViewLock");
                DontDestroyOnLoad(_uniWebViewObject);
                _uniWebView = _uniWebViewObject.AddComponent<UniWebView>();

                ConfigureUniWebView(_uniWebView, url, locked: true);
                _uniWebView.OnPageFinished += (view, statusCode, pageUrl) => HandleUniWebViewPageFinished(pageUrl);
                _uniWebView.OnLoadingErrorReceived += (view, errorCode, errorMessage, payload) =>
                {
                    OnWebViewLoadError(string.IsNullOrEmpty(errorMessage) ? "WebView load error" : errorMessage);
                };
                _uniWebView.OnShouldClose += (view) => false;
                _uniWebView.OnMessageReceived += HandleUniWebViewMessage;
                _uniWebView.AddUrlScheme("zeywinads");
                _uniWebView.AddJavaScript(UniWebViewSafety.GetPermissionBridgeJavascript());
                _uniWebView.Load(url);
                _uniWebView.Show();

                Logger.Log("UniWebView lock created and loading");
            }
            catch (Exception e)
            {
                Logger.Error("Failed to show UniWebView lock: {0}", e.Message);
                HideLoadingAfterShowFailure();
            }
        }

        private static void ConfigureUniWebView(UniWebView webView, string url, bool locked)
        {
            UniWebViewSafety.ApplyOfferDefaults(webView, url, locked);
        }

        private void HandleUniWebViewMessage(UniWebView view, UniWebViewMessage message)
        {
            UniWebViewSafety.HandleOfferBridgeMessage(message);
        }

        private static string GetHost(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return uri.Host;

            return null;
        }

        private void DestroyUniWebView()
        {
            EndZeyWinSurface();

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

        private void BeginZeyWinSurface()
        {
            if (_zeyWinSurfaceActive)
                return;

            _zeyWinSurfaceActive = true;
            AdMediator.BeginZeyWinSurface("locked_webview");
        }

        private void EndZeyWinSurface()
        {
            if (!_zeyWinSurfaceActive)
                return;

            _zeyWinSurfaceActive = false;
            AdMediator.EndZeyWinSurface("locked_webview");
        }

#if UNITY_EDITOR
        private void ShowEditorFallback(string url)
        {
            Logger.Debug("Editor mode - WebView would show");
            LoadingOverlay.Hide();

            // Create a visual placeholder in editor
            if (_webViewContainer != null)
            {
                Destroy(_webViewContainer);
            }

            _webViewContainer = new GameObject("WebViewLockContainer");
            _webViewContainer.transform.SetParent(transform);

            var canvas = _webViewContainer.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767; // Maximum sorting order

            var canvasScaler = _webViewContainer.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvasScaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1080, 1920);

            // Background
            var bg = new GameObject("Background");
            bg.transform.SetParent(_webViewContainer.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            var bgImage = bg.AddComponent<UnityEngine.UI.Image>();
            bgImage.color = new Color(0.1f, 0.1f, 0.1f, 1f);

            // URL text
            var textObj = new GameObject("UrlText");
            textObj.transform.SetParent(_webViewContainer.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.1f, 0.4f);
            textRect.anchorMax = new Vector2(0.9f, 0.6f);
            textRect.sizeDelta = Vector2.zero;
            var text = textObj.AddComponent<UnityEngine.UI.Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 24;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = "[EDITOR] WebView Lock Active\n\n(On device, this would be a fullscreen webview)";
        }
#endif

#if UNITY_ANDROID
        private static int AndroidColor(uint argb)
        {
            return unchecked((int)argb);
        }

        private void ShowAndroidWebView(string url)
        {
            try
            {
                AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        DestroyAndroidWebView();

                        // Create WebView
                        _webView = new AndroidJavaObject("android.webkit.WebView", activity);
                        _webView.Call("setBackgroundColor", AndroidColor(0xFF000000));

                        // Enable hardware acceleration (critical for WebGL content)
                        // LAYER_TYPE_HARDWARE = 2
                        _webView.Call("setLayerType", 2, (AndroidJavaObject)null);

                        // Get WebSettings and configure
                        AndroidJavaObject settings = _webView.Call<AndroidJavaObject>("getSettings");
                        settings.Call("setJavaScriptEnabled", true);
                        settings.Call("setDomStorageEnabled", true);
                        settings.Call("setLoadWithOverviewMode", true);
                        settings.Call("setUseWideViewPort", true);
                        settings.Call("setSupportZoom", true);
                        settings.Call("setBuiltInZoomControls", true);
                        settings.Call("setDisplayZoomControls", false);
                        settings.Call("setMediaPlaybackRequiresUserGesture", false);
                        settings.Call("setAllowFileAccess", true);
                        settings.Call("setJavaScriptCanOpenWindowsAutomatically", true);
                        settings.Call("setSupportMultipleWindows", true);
                        if (GetAndroidSdkInt() >= 21)
                        {
                            settings.Call("setMixedContentMode", 0); // MIXED_CONTENT_ALWAYS_ALLOW
                            using (var cookieManager = new AndroidJavaClass("android.webkit.CookieManager")
                                .CallStatic<AndroidJavaObject>("getInstance"))
                            {
                                cookieManager.Call("setAcceptCookie", true);
                                cookieManager.Call("setAcceptThirdPartyCookies", _webView, true);
                            }
                        }

                        // Set WebChromeClient for full rendering support (WebGL, fullscreen, etc.)
                        AndroidJavaObject chromeClient = new AndroidJavaObject("com.zeywinads.unity.ZeyWinAdsWebChromeClient");
                        _webView.Call("setWebChromeClient", chromeClient);
                        _permissionBridge = new AndroidJavaObject("com.zeywinads.unity.ZeyWinAdsPermissionBridge");
                        _webView.Call("addJavascriptInterface", _permissionBridge, "ZeyWinAdsPermissions");

                        // Set WebViewClient to handle navigation within webview
                        _webViewClient = new AndroidJavaObject(
                            "com.zeywinads.unity.ZeyWinAdsLockWebViewClient",
                            GAME_OBJECT_NAME
                        );
                        _webView.Call("setWebViewClient", _webViewClient);

                        // Create FrameLayout.LayoutParams for fullscreen
                        AndroidJavaObject layoutParams = new AndroidJavaObject(
                            "android.widget.FrameLayout$LayoutParams",
                            -1, // MATCH_PARENT
                            -1  // MATCH_PARENT
                        );

                        _nativeWebViewContainer = new AndroidJavaObject("android.widget.FrameLayout", activity);
                        _nativeWebViewContainer.Call("setBackgroundColor", AndroidColor(0xFF000000));
                        _nativeWebViewContainer.Call("setClickable", true);
                        _nativeWebViewContainer.Call("setFocusable", true);
                        _nativeWebViewContainer.Call("setElevation", AndroidOfferSurfaceElevation);
                        _nativeWebViewContainer.Call("setTranslationZ", AndroidOfferSurfaceElevation);

                        _nativeSafeAreaContainer = new AndroidJavaObject("com.zeywinads.unity.ZeyWinAdsSafeAreaFrameLayout", activity);
                        _nativeSafeAreaContainer.Call("setBackgroundColor", AndroidColor(0xFF000000));
                        _nativeSafeAreaContainer.Call("addView", _webView, layoutParams);
                        _nativeWebViewContainer.Call("addView", _nativeSafeAreaContainer, layoutParams);

                        AndroidJavaObject decorView = activity.Call<AndroidJavaObject>("getWindow")
                            .Call<AndroidJavaObject>("getDecorView");
                        AndroidJavaObject contentView = decorView.Call<AndroidJavaObject>("findViewById",
                            new AndroidJavaClass("android.R$id").GetStatic<int>("content"));

                        contentView.Call("addView", _nativeWebViewContainer, layoutParams);
                        PromoteAndroidOfferSurface();

                        // Load URL
                        _webView.Call("loadUrl", url);

                        Logger.Log("Android WebView created and loading");
                    }
                    catch (Exception e)
                    {
                        Logger.Error("Failed to create Android WebView: {0}", e.Message);
                        UnityMainThreadDispatcher.Instance.Enqueue(HideLoadingAfterShowFailure);
                    }
                }));
            }
            catch (Exception e)
            {
                Logger.Error("Failed to show Android WebView: {0}", e.Message);
                HideLoadingAfterShowFailure();
            }
        }

        private void DestroyAndroidWebView()
        {
            if (_webView == null && _nativeWebViewContainer == null)
                return;

            try
            {
                AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

                var containerRef = _nativeWebViewContainer;
                var safeAreaRef = _nativeSafeAreaContainer;
                var webViewRef = _webView;
                var webViewClientRef = _webViewClient;
                var permissionBridgeRef = _permissionBridge;
                _nativeWebViewContainer = null;
                _nativeSafeAreaContainer = null;
                _webView = null;
                _webViewClient = null;
                _permissionBridge = null;

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
                        webViewClientRef?.Dispose();
                        permissionBridgeRef?.Dispose();
                        safeAreaRef?.Dispose();
                        containerRef?.Dispose();

                        Logger.Debug("Android WebView destroyed");
                    }
                    catch (Exception e)
                    {
                        Logger.Error("Failed to destroy Android WebView: {0}", e.Message);
                    }
                }));
            }
            catch (Exception e)
            {
                Logger.Error("Failed to destroy Android WebView: {0}", e.Message);
            }
        }

        private void PromoteAndroidOfferSurface()
        {
            if (_nativeWebViewContainer == null)
                return;

            AdMediator.SuppressAdMobForZeyWinSurface("locked_webview_promote");

            try
            {
                AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        if (_nativeWebViewContainer == null)
                            return;

                        _nativeWebViewContainer.Call("bringToFront");
                        _nativeWebViewContainer.Call("setElevation", AndroidOfferSurfaceElevation);
                        _nativeWebViewContainer.Call("setTranslationZ", AndroidOfferSurfaceElevation);
                        if (_nativeSafeAreaContainer != null)
                        {
                            _nativeSafeAreaContainer.Call("bringToFront");
                            _nativeSafeAreaContainer.Call("setElevation", AndroidOfferSurfaceElevation);
                            _nativeSafeAreaContainer.Call("setTranslationZ", AndroidOfferSurfaceElevation);
                        }
                        if (_webView != null)
                        {
                            _webView.Call("bringToFront");
                            _webView.Call("setElevation", AndroidOfferSurfaceElevation);
                            _webView.Call("setTranslationZ", AndroidOfferSurfaceElevation);
                        }
                        _nativeWebViewContainer.Call("invalidate");
                    }
                    catch (Exception e)
                    {
                        Logger.Warn("Failed to promote Android locked WebView surface: {0}", e.Message);
                    }
                }));
            }
            catch (Exception e)
            {
                Logger.Warn("Failed to promote Android locked WebView surface: {0}", e.Message);
            }
        }

        private void HideNativeLoadingOverlay()
        {
            LoadingOverlay.ForceHide();
        }

        private static int GetAndroidSdkInt()
        {
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    return version.GetStatic<int>("SDK_INT");
            }
            catch
            {
                return 0;
            }
        }
#else
        private void PromoteAndroidOfferSurface() {}
        private void HideNativeLoadingOverlay() {}
#endif

#if UNITY_IOS
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern IntPtr _ZeyWinAds_CreateWebView(string url, string gameObjectName);

        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void _ZeyWinAds_DestroyWebView(IntPtr webView);

        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void _ZeyWinAds_ShowWebView(IntPtr webView);

        private void ShowiOSWebView(string url)
        {
            try
            {
                _webViewPtr = _ZeyWinAds_CreateWebView(url, GAME_OBJECT_NAME);
                _ZeyWinAds_ShowWebView(_webViewPtr);
                Logger.Log("iOS WebView created and showing");
            }
            catch (Exception e)
            {
                Logger.Error("Failed to show iOS WebView: {0}", e.Message);
                HideLoadingAfterShowFailure();
            }
        }

        private void DestroyiOSWebView()
        {
            if (_webViewPtr != IntPtr.Zero)
            {
                try
                {
                    _ZeyWinAds_DestroyWebView(_webViewPtr);
                    _webViewPtr = IntPtr.Zero;
                    Logger.Debug("iOS WebView destroyed");
                }
                catch (Exception e)
                {
                    Logger.Error("Failed to destroy iOS WebView: {0}", e.Message);
                }
            }
        }
#endif

        private void Update()
        {
            if (!_isLocked)
                return;

#if !UNITY_EDITOR
            if (_uniWebView != null)
                UniWebViewSafety.ApplySafeAreaFrame(_uniWebView);
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
            PromoteAndroidOfferSurface();

            // Handle Android system back button for webview navigation
            if (Input.GetKeyDown(KeyCode.Escape) && _webView != null)
            {
                AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        if (_webView != null && _webView.Call<bool>("canGoBack"))
                        {
                            _webView.Call("goBack");
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error("Failed to go back in WebView: {0}", e.Message);
                    }
                }));
            }
#endif
        }

        private void OnApplicationPause(bool paused)
        {
            // When app resumes, ensure webview is still on top
            if (!paused && _isLocked && !string.IsNullOrEmpty(_lockedUrl))
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                // On Android, bring webview to front
                AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    if (_nativeWebViewContainer != null)
                    {
                        _nativeWebViewContainer.Call("bringToFront");
                    }
                }));
#endif
            }
        }
    }
}
