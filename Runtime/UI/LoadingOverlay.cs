using UnityEngine;

namespace ZeyWinAds.UI
{
    /// <summary>
    /// SDK loading is intentionally Java-native only. This bridge keeps the
    /// public C# API stable while preventing any Unity canvas loader from being
    /// created in games.
    /// </summary>
    public static class LoadingOverlay
    {
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

        public static void ForceHide()
        {
            _showCount = 0;
            SetNativeOverlayVisible(false);
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
#endif
        }
    }
}
