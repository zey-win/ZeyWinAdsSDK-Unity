using System;
using UnityEngine;

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

        private static WebViewLock _instance;
        public static WebViewLock Instance => _instance;

        private GameObject _webViewContainer;
        private bool _isLocked;
        private string _lockedUrl;

#if UNITY_ANDROID
        private AndroidJavaObject _webView;
        private AndroidJavaObject _webViewClient;
#endif

#if UNITY_IOS
        private IntPtr _webViewPtr;
#endif

        /// <summary>
        /// Whether the app is currently locked with a webview
        /// </summary>
        public static bool IsLocked => _instance != null && _instance._isLocked;

        /// <summary>
        /// Initializes the WebViewLock system. Call this on app startup.
        /// </summary>
        public static void Initialize()
        {
            if (_instance != null)
                return;

            var go = new GameObject("ZeyWinAds_WebViewLock");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<WebViewLock>();
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
                    Debug.Log($"[ZeyWinAds] Restoring locked webview: {url}");
                    LockWithUrl(url, false); // Don't re-save, it's already saved
                }
            }
        }

        /// <summary>
        /// Locks the application with a fullscreen webview showing the specified URL
        /// </summary>
        public static void Lock(string url)
        {
            if (_instance == null)
            {
                Initialize();
            }
            _instance.LockWithUrl(url, true);
        }

        private void LockWithUrl(string url, bool persist)
        {
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogWarning("[ZeyWinAds] Cannot lock with empty URL");
                return;
            }

            _lockedUrl = url;
            _isLocked = true;

            if (persist)
            {
                PlayerPrefs.SetString(LOCK_URL_KEY, url);
                PlayerPrefs.SetInt(LOCK_ACTIVE_KEY, 1);
                PlayerPrefs.Save();
                Debug.Log($"[ZeyWinAds] Locking app with webview: {url}");
            }

            ShowWebView(url);
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
            DestroyWebView();
            Debug.Log("[ZeyWinAds] WebView lock removed");
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
            ShowEditorFallback(url);
#endif
        }

        private void DestroyWebView()
        {
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

#if UNITY_EDITOR
        private void ShowEditorFallback(string url)
        {
            Debug.Log($"[ZeyWinAds] Editor mode - WebView would show: {url}");

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
            text.text = $"[EDITOR] WebView Lock Active\n\n{url}\n\n(On device, this would be a fullscreen webview)";
        }
#endif

#if UNITY_ANDROID
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
                        // Create WebView
                        _webView = new AndroidJavaObject("android.webkit.WebView", activity);

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

                        // Set WebChromeClient for full rendering support (WebGL, fullscreen, etc.)
                        AndroidJavaObject chromeClient = new AndroidJavaObject("android.webkit.WebChromeClient");
                        _webView.Call("setWebChromeClient", chromeClient);

                        // Set WebViewClient to handle navigation within webview
                        _webViewClient = new AndroidJavaObject("android.webkit.WebViewClient");
                        _webView.Call("setWebViewClient", _webViewClient);

                        // Create FrameLayout.LayoutParams for fullscreen
                        AndroidJavaObject layoutParams = new AndroidJavaObject(
                            "android.widget.FrameLayout$LayoutParams",
                            -1, // MATCH_PARENT
                            -1  // MATCH_PARENT
                        );

                        // Add to activity's content view
                        AndroidJavaObject decorView = activity.Call<AndroidJavaObject>("getWindow")
                            .Call<AndroidJavaObject>("getDecorView");
                        AndroidJavaObject contentView = decorView.Call<AndroidJavaObject>("findViewById",
                            new AndroidJavaClass("android.R$id").GetStatic<int>("content"));

                        contentView.Call("addView", _webView, layoutParams);

                        // Load URL
                        _webView.Call("loadUrl", url);

                        Debug.Log($"[ZeyWinAds] Android WebView created and loading: {url}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[ZeyWinAds] Failed to create Android WebView: {e.Message}");
                    }
                }));
            }
            catch (Exception e)
            {
                Debug.LogError($"[ZeyWinAds] Failed to show Android WebView: {e.Message}");
            }
        }

        private void DestroyAndroidWebView()
        {
            if (_webView == null)
                return;

            try
            {
                AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        AndroidJavaObject decorView = activity.Call<AndroidJavaObject>("getWindow")
                            .Call<AndroidJavaObject>("getDecorView");
                        AndroidJavaObject contentView = decorView.Call<AndroidJavaObject>("findViewById",
                            new AndroidJavaClass("android.R$id").GetStatic<int>("content"));

                        contentView.Call("removeView", _webView);
                        _webView.Call("destroy");
                        _webView.Dispose();
                        _webView = null;

                        if (_webViewClient != null)
                        {
                            _webViewClient.Dispose();
                            _webViewClient = null;
                        }

                        Debug.Log("[ZeyWinAds] Android WebView destroyed");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[ZeyWinAds] Failed to destroy Android WebView: {e.Message}");
                    }
                }));
            }
            catch (Exception e)
            {
                Debug.LogError($"[ZeyWinAds] Failed to destroy Android WebView: {e.Message}");
            }
        }
#endif

#if UNITY_IOS
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern IntPtr _ZeyWinAds_CreateWebView(string url);

        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void _ZeyWinAds_DestroyWebView(IntPtr webView);

        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void _ZeyWinAds_ShowWebView(IntPtr webView);

        private void ShowiOSWebView(string url)
        {
            try
            {
                _webViewPtr = _ZeyWinAds_CreateWebView(url);
                _ZeyWinAds_ShowWebView(_webViewPtr);
                Debug.Log($"[ZeyWinAds] iOS WebView created and showing: {url}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ZeyWinAds] Failed to show iOS WebView: {e.Message}");
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
                    Debug.Log("[ZeyWinAds] iOS WebView destroyed");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ZeyWinAds] Failed to destroy iOS WebView: {e.Message}");
                }
            }
        }
#endif

        private void Update()
        {
            if (!_isLocked)
                return;

#if UNITY_ANDROID && !UNITY_EDITOR
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
                        Debug.LogError($"[ZeyWinAds] Failed to go back in WebView: {e.Message}");
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
                    if (_webView != null)
                    {
                        _webView.Call("bringToFront");
                    }
                }));
#endif
            }
        }
    }
}
