using UnityEngine;
using UnityEngine.UI;
using ZeyWinAds.Core;
using Logger = ZeyWinAds.Core.Logger;

namespace ZeyWinAds.UI
{
    /// <summary>
    /// Covers the Unity game with an opaque themed surface while Google Mobile Ads
    /// renders a fullscreen interstitial or rewarded ad above Unity.
    /// </summary>
    internal static class AdMobFullscreenBackdrop
    {
        private const int SortingOrder = 32000;

        private static GameObject _root;
        private static Image _image;
        private static int _depth;

        // GMA close/fail events arrive on a Java thread; SetActive there throws and leaves the
        // opaque backdrop covering the game. Marshal so callers needn't care.
        public static void Show(string reason) => UnityMainThreadDispatcher.RunOnMainThread(() => ShowOnMain(reason));

        public static void Hide(string reason) => UnityMainThreadDispatcher.RunOnMainThread(() => HideOnMain(reason));

        public static void ForceHide(string reason) => UnityMainThreadDispatcher.RunOnMainThread(() => ForceHideOnMain(reason));

        private static void ShowOnMain(string reason)
        {
            _depth++;
            Ensure();
            ApplyTheme();
            if (_root != null && !_root.activeSelf)
                _root.SetActive(true);
            Logger.Debug("[AdMob] Fullscreen themed backdrop shown: {0}", SafeReason(reason));
        }

        private static void HideOnMain(string reason)
        {
            if (_depth > 0)
                _depth--;

            if (_depth == 0 && _root != null)
                _root.SetActive(false);
            Logger.Debug("[AdMob] Fullscreen themed backdrop hidden: {0}", SafeReason(reason));
        }

        private static void ForceHideOnMain(string reason)
        {
            _depth = 0;
            if (_root != null)
                _root.SetActive(false);
            Logger.Debug("[AdMob] Fullscreen themed backdrop force hidden: {0}", SafeReason(reason));
        }

        private static void Ensure()
        {
            if (_root != null)
                return;

            _root = new GameObject("ZeyWinAdsAdMobFullscreenBackdrop");
            Object.DontDestroyOnLoad(_root);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _root.AddComponent<GraphicRaycaster>();

            var blocker = new GameObject("Black");
            blocker.transform.SetParent(_root.transform, false);
            var rect = blocker.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = blocker.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = true;
            _image = image;
        }

        private static void ApplyTheme()
        {
            if (_image == null)
                return;

            _image.color = global::ZeyWinAds.Core.AdThemeController.Current.AdMobFullscreenBackground;
        }

        private static string SafeReason(string reason)
        {
            return string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }
    }
}
