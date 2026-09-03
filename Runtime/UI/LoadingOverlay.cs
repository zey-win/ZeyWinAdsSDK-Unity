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
#endif

        private static int _showCount;

        public static void Show()
        {
            _showCount++;
            SetNativeOverlayVisible(true);
        }

        public static void Hide()
        {
            _showCount = Mathf.Max(0, _showCount - 1);
            if (_showCount == 0)
                SetNativeOverlayVisible(false);
        }

        public static void HideAfterDelay(float delaySeconds)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            UnityMainThreadDispatcher.Instance.StartCoroutine(HideAfterDelayCoroutine(delaySeconds));
#endif
        }

        public static void ForceHide()
        {
            _showCount = 0;
            SetNativeOverlayVisible(false);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static IEnumerator HideAfterDelayCoroutine(float delaySeconds)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, delaySeconds));
            Hide();
        }
#endif

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
