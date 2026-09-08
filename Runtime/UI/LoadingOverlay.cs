using System;
using System.Collections;
using UnityEngine;
using ZeyWinAds.Core;

namespace ZeyWinAds.UI
{
    /// <summary>
    /// SDK loading is intentionally native-only (Java on Android, UIKit on iOS).
    /// This bridge keeps the public C# API stable while preventing any Unity
    /// canvas loader from being created in games.
    /// </summary>
    public static class LoadingOverlay
    {
#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void _ZeyWinAdsStartupOverlay_SetVisible(bool visible);

        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern bool _ZeyWinAdsStartupOverlay_IsVisible();
#endif

        private static int _showCount;

        // Fires whenever a call here actually flips the native overlay's visibility (not on
        // every ref-counted Show/Hide call). Used by QA tooling to time how long the loader
        // was actually up; not part of the SDK's public integration surface.
        public static event Action<bool> VisibilityChanged;

        public static void Show()
        {
            var wasHidden = _showCount == 0;
            _showCount++;
            SetNativeOverlayVisible(true);
            if (wasHidden)
                VisibilityChanged?.Invoke(true);
        }

        public static void Hide()
        {
            _showCount = Mathf.Max(0, _showCount - 1);
            if (_showCount == 0)
            {
                SetNativeOverlayVisible(false);
                VisibilityChanged?.Invoke(false);
            }
        }

        public static void HideAfterDelay(float delaySeconds)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            UnityMainThreadDispatcher.Instance.StartCoroutine(HideAfterDelayCoroutine(delaySeconds));
#endif
        }

        public static void ForceHide()
        {
            var wasShowing = _showCount != 0;
            _showCount = 0;
            SetNativeOverlayVisible(false);
            if (wasShowing)
                VisibilityChanged?.Invoke(false);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static IEnumerator HideAfterDelayCoroutine(float delaySeconds)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, delaySeconds));
            Hide();
        }
#endif

        // Queries the native overlay's true current visibility. Used by QA tooling that needs
        // to know the real on-screen state rather than which C# call sites fired — dismissal
        // paths like the native auto-dismiss failsafe never call back into C# at all.
        public static bool IsNativeOverlayVisible()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var startupOverlay = new AndroidJavaClass("com.zeywinads.unity.ZeyWinAdsStartupOverlay"))
                {
                    return startupOverlay.CallStatic<bool>("isVisible");
                }
            }
            catch
            {
                return false;
            }
#elif UNITY_IOS && !UNITY_EDITOR
            try
            {
                return _ZeyWinAdsStartupOverlay_IsVisible();
            }
            catch
            {
                return false;
            }
#else
            return false;
#endif
        }

        private static void SetNativeOverlayVisible(bool visible)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var startupOverlay = new AndroidJavaClass("com.zeywinads.unity.ZeyWinAdsStartupOverlay"))
                {
                    startupOverlay.CallStatic("setLoadingOverlayVisible", visible);
                }
            }
            catch
            {
                // The native overlay is installed by the Android project setup.
            }
#elif UNITY_IOS && !UNITY_EDITOR
            try
            {
                _ZeyWinAdsStartupOverlay_SetVisible(visible);
            }
            catch
            {
                // The native overlay is installed by the iOS project setup.
            }
#endif
        }
    }
}
