using System;
using UnityEngine;
using UnityEngine.UI;
using ZeyWinAds.Core;
using ZeyWinAds.UI;
using Logger = ZeyWinAds.Core.Logger;

namespace ZeyWinAds.Ads
{
    /// <summary>
    /// Banner ad that displays at the top or bottom of the screen.
    /// Stays visible until HideBanner() is called.
    /// </summary>
    public class BannerAd : BaseAd
    {
        public override AdType AdType => AdType.Banner;

        /// <summary>
        /// Default banner height in pixels
        /// </summary>
        public const float DefaultBannerHeight = 50f;

        /// <summary>
        /// Default banner height for tablets in pixels
        /// </summary>
        public const float DefaultTabletBannerHeight = 90f;

        /// <summary>
        /// Custom banner height in pixels (null = use default)
        /// </summary>
        private static float? _customBannerHeight = null;

        /// <summary>
        /// Custom banner height for tablets in pixels (null = use default)
        /// </summary>
        private static float? _customTabletBannerHeight = null;

        /// <summary>
        /// Current banner height in pixels (considering device type and custom settings)
        /// </summary>
        public static float BannerHeight => _customBannerHeight ?? DefaultBannerHeight;

        /// <summary>
        /// Current banner height for tablets in pixels
        /// </summary>
        public static float TabletBannerHeight => _customTabletBannerHeight ?? DefaultTabletBannerHeight;

        /// <summary>
        /// Sets the custom banner height for phones
        /// </summary>
        /// <param name="height">Height in pixels (use null to reset to default)</param>
        public static void SetBannerHeight(float? height)
        {
            _customBannerHeight = height;
        }

        /// <summary>
        /// Sets the custom banner height for tablets
        /// </summary>
        /// <param name="height">Height in pixels (use null to reset to default)</param>
        public static void SetTabletBannerHeight(float? height)
        {
            _customTabletBannerHeight = height;
        }

        /// <summary>
        /// Sets the custom banner height for both phones and tablets
        /// </summary>
        /// <param name="phoneHeight">Height in pixels for phones (use null to reset to default)</param>
        /// <param name="tabletHeight">Height in pixels for tablets (use null to reset to default)</param>
        public static void SetBannerHeights(float? phoneHeight, float? tabletHeight)
        {
            _customBannerHeight = phoneHeight;
            _customTabletBannerHeight = tabletHeight;
        }

        /// <summary>
        /// Resets banner heights to default values
        /// </summary>
        public static void ResetBannerHeights()
        {
            _customBannerHeight = null;
            _customTabletBannerHeight = null;
        }

        /// <summary>
        /// Gets the current effective banner height based on device type
        /// </summary>
        public static float GetCurrentBannerHeight()
        {
            return DeviceInfo.GetDeviceType() == "tablet" ? TabletBannerHeight : BannerHeight;
        }

        /// <summary>
        /// Current banner position
        /// </summary>
        public BannerPosition Position { get; private set; } = BannerPosition.Bottom;

        /// <summary>
        /// Whether the banner is currently visible
        /// </summary>
        public bool IsVisible { get; private set; }

        // UI references
        private AdCanvas _canvas;
        private GameObject _bannerContainer;
        private RawImage _bannerImage;
        private Button _clickArea;

        /// <summary>
        /// Creates a new banner ad instance
        /// </summary>
        public BannerAd()
        {
        }

        /// <summary>
        /// Shows the banner at the specified position
        /// </summary>
        /// <param name="position">Where to display the banner</param>
        /// <param name="onClose">Not typically used for banners, but available for consistency</param>
        public void Show(BannerPosition position, Action onClose = null)
        {
            Position = position;
            base.Show(onClose);
        }

        /// <summary>
        /// Override base Show to use default position (Bottom)
        /// </summary>
        public override void Show(Action onClose = null)
        {
            Show(BannerPosition.Bottom, onClose);
        }

        protected override void OnShow()
        {
            Logger.Debug("Showing banner ad at {0}", Position);

            IsVisible = true;

            // Create ad canvas
            _canvas = AdCanvas.Create("BannerAdCanvas");
            _canvas.SetSortingOrder(999); // Below fullscreen ads

            // Create banner container (hidden until image loads)
            CreateBannerContainer();
            _bannerContainer.SetActive(false);

            // Load image, then show banner
            LoadBannerImage();
        }

        private void CreateBannerContainer()
        {
            _bannerContainer = new GameObject("BannerContainer");
            _bannerContainer.transform.SetParent(_canvas.transform, false);

            var rectTransform = _bannerContainer.AddComponent<RectTransform>();

            // Calculate banner height based on device type
            float bannerHeight = DeviceInfo.GetDeviceType() == "tablet" ? TabletBannerHeight : BannerHeight;

            // Get safe area offsets and scale factor
            Rect safeArea = Screen.safeArea;
            float topInset = Screen.height - (safeArea.y + safeArea.height);
            float bottomInset = safeArea.y;
            float scaleFactor = _canvas.GetScaleFactor();

            // Position based on setting with safe area (convert pixels to canvas units)
            if (Position == BannerPosition.Top)
            {
                rectTransform.anchorMin = new Vector2(0, 1);
                rectTransform.anchorMax = new Vector2(1, 1);
                rectTransform.pivot = new Vector2(0.5f, 1);
                rectTransform.anchoredPosition = new Vector2(0, -topInset / scaleFactor);
            }
            else // Bottom
            {
                rectTransform.anchorMin = new Vector2(0, 0);
                rectTransform.anchorMax = new Vector2(1, 0);
                rectTransform.pivot = new Vector2(0.5f, 0);
                rectTransform.anchoredPosition = new Vector2(0, bottomInset / scaleFactor);
            }

            rectTransform.sizeDelta = new Vector2(0, bannerHeight);

            Logger.Debug("Banner safe area - top: {0}, bottom: {1}, scale: {2}", topInset, bottomInset, scaleFactor);

            // Background
            var background = _bannerContainer.AddComponent<Image>();
            background.color = AdThemeController.Current.SurfacePressed;

            // Click area
            _clickArea = _bannerContainer.AddComponent<Button>();
            _clickArea.targetGraphic = background;
            _clickArea.onClick.AddListener(OnBannerClicked);
        }

        private void LoadBannerImage()
        {
            if (string.IsNullOrEmpty(AdData.media_url))
            {
                Logger.Warn("Banner has no media URL");
                return;
            }

            // Create mask container for clipping
            var maskObj = new GameObject("BannerMask");
            maskObj.transform.SetParent(_bannerContainer.transform, false);

            var maskRect = maskObj.AddComponent<RectTransform>();
            maskRect.anchorMin = Vector2.zero;
            maskRect.anchorMax = Vector2.one;
            maskRect.sizeDelta = Vector2.zero;
            maskRect.anchoredPosition = Vector2.zero;

            maskObj.AddComponent<RectMask2D>();

            // Create image inside mask
            var imageObj = new GameObject("BannerImage");
            imageObj.transform.SetParent(maskObj.transform, false);

            var imageRect = imageObj.AddComponent<RectTransform>();
            imageRect.anchorMin = new Vector2(0.5f, 0.5f);
            imageRect.anchorMax = new Vector2(0.5f, 0.5f);
            imageRect.pivot = new Vector2(0.5f, 0.5f);
            imageRect.anchoredPosition = Vector2.zero;

            _bannerImage = imageObj.AddComponent<RawImage>();
            _bannerImage.color = Color.white;

            // Load image asynchronously, show banner only after loaded
            _canvas.LoadImage(AdData.media_url, (texture) =>
            {
                if (texture != null && _bannerImage != null && maskRect != null)
                {
                    _bannerImage.texture = texture;
                    ApplyBannerAspectFill(imageRect, maskRect, texture.width, texture.height);

                    // Show banner only if still supposed to be visible (not hidden while loading)
                    if (_bannerContainer != null && IsVisible)
                        _bannerContainer.SetActive(true);

                    Logger.Debug("Banner image loaded (visible={0})", IsVisible);
                }
            });
        }

        private void ApplyBannerAspectFill(RectTransform imageRect, RectTransform containerRect, float imageWidth, float imageHeight)
        {
            float containerWidth = containerRect.rect.width > 0 ? containerRect.rect.width : Screen.width;
            float containerHeight = containerRect.rect.height > 0 ? containerRect.rect.height : BannerHeight;

            float imageAspect = imageWidth / imageHeight;
            float containerAspect = containerWidth / containerHeight;

            float width, height;

            if (imageAspect > containerAspect)
            {
                // Image is wider - match height, overflow width
                height = containerHeight;
                width = height * imageAspect;
            }
            else
            {
                // Image is taller - match width, overflow height
                width = containerWidth;
                height = width / imageAspect;
            }

            imageRect.sizeDelta = new Vector2(width, height);
        }

        private void OnBannerClicked()
        {
            Logger.Debug("Banner clicked");
            OpenClickUrl();
        }

        /// <summary>
        /// Hides the banner ad
        /// </summary>
        public void Hide()
        {
            Logger.Debug("Hiding banner");

            IsVisible = false;

            if (_bannerContainer != null)
            {
                _bannerContainer.SetActive(false);
            }
        }

        /// <summary>
        /// Shows a previously hidden banner (without reloading)
        /// </summary>
        public void ShowAgain()
        {
            if (!IsShowing || IsVisible)
                return;

            if (_bannerContainer != null)
            {
                _bannerContainer.SetActive(true);
                IsVisible = true;
                TrackImpression(); // Track new impression on show
            }
        }

        /// <summary>
        /// Changes the banner position
        /// </summary>
        /// <param name="newPosition">New position for the banner</param>
        public void SetPosition(BannerPosition newPosition)
        {
            if (Position == newPosition)
                return;

            Position = newPosition;

            if (_bannerContainer != null && _canvas != null)
            {
                var rectTransform = _bannerContainer.GetComponent<RectTransform>();
                float bannerHeight = DeviceInfo.GetDeviceType() == "tablet" ? TabletBannerHeight : BannerHeight;

                // Get safe area offsets and scale factor
                Rect safeArea = Screen.safeArea;
                float topInset = Screen.height - (safeArea.y + safeArea.height);
                float bottomInset = safeArea.y;
                float scaleFactor = _canvas.GetScaleFactor();

                if (Position == BannerPosition.Top)
                {
                    rectTransform.anchorMin = new Vector2(0, 1);
                    rectTransform.anchorMax = new Vector2(1, 1);
                    rectTransform.pivot = new Vector2(0.5f, 1);
                    rectTransform.anchoredPosition = new Vector2(0, -topInset / scaleFactor);
                }
                else
                {
                    rectTransform.anchorMin = new Vector2(0, 0);
                    rectTransform.anchorMax = new Vector2(1, 0);
                    rectTransform.pivot = new Vector2(0.5f, 0);
                    rectTransform.anchoredPosition = new Vector2(0, bottomInset / scaleFactor);
                }

                rectTransform.sizeDelta = new Vector2(0, bannerHeight);
            }
        }

        /// <summary>
        /// Destroys the banner ad
        /// </summary>
        public override void Destroy()
        {
            if (_canvas != null)
            {
                _canvas.Destroy();
                _canvas = null;
            }

            _bannerContainer = null;
            _bannerImage = null;
            _clickArea = null;
            IsVisible = false;

            base.Destroy();
        }

        protected override void OnDestroy()
        {
            // Banner cleanup is handled in Destroy()
        }

        protected override void OnClose()
        {
            IsVisible = false;
            base.OnClose();
        }
    }
}
