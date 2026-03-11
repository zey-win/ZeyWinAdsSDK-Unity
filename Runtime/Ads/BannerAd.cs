using System;
using UnityEngine;
using UnityEngine.UI;
using ZeyWinAds.Core;
using ZeyWinAds.UI;

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
            Debug.Log($"[ZeyWinAds] Showing banner ad at {Position}: {AdData.ad_id}");

            IsVisible = true;

            // Create ad canvas
            _canvas = AdCanvas.Create("BannerAdCanvas");
            _canvas.SetSortingOrder(999); // Below fullscreen ads

            // Create banner container
            CreateBannerContainer();

            // Display based on media type
            if (AdData.GetMediaType() == MediaType.Native)
            {
                CreateNativeBannerLayout();
            }
            else
            {
                // Load and display banner image
                LoadBannerImage();
            }
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

            Debug.Log($"[ZeyWinAds] Banner safe area - top: {topInset}, bottom: {bottomInset}, scale: {scaleFactor}");

            // Background
            var background = _bannerContainer.AddComponent<Image>();
            background.color = new Color(0.1f, 0.1f, 0.1f, 1f);

            // Click area
            _clickArea = _bannerContainer.AddComponent<Button>();
            _clickArea.targetGraphic = background;
            _clickArea.onClick.AddListener(OnBannerClicked);
        }

        private void LoadBannerImage()
        {
            if (string.IsNullOrEmpty(AdData.media_url))
            {
                Debug.LogWarning("[ZeyWinAds] Banner has no media URL");
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

            // Load image asynchronously with aspect fill
            _canvas.LoadImage(AdData.media_url, (texture) =>
            {
                if (texture != null && _bannerImage != null && maskRect != null)
                {
                    _bannerImage.texture = texture;
                    ApplyBannerAspectFill(imageRect, maskRect, texture.width, texture.height);
                    Debug.Log("[ZeyWinAds] Banner image loaded");
                }
            });
        }

        /// <summary>
        /// Creates a native ad banner layout (Google AdMob small template style):
        /// [Ad badge] [Icon] [Headline text] [> arrow]
        /// </summary>
        private void CreateNativeBannerLayout()
        {
            float bannerHeight = DeviceInfo.GetDeviceType() == "tablet" ? TabletBannerHeight : BannerHeight;
            float padding = 6f;
            float iconSize = bannerHeight - padding * 2;
            float badgeWidth = 24f;
            float badgeHeight = 16f;
            float arrowSize = 20f;

            // "Ad" badge - green label (Google AdMob style: #3A6728)
            var badgeObj = new GameObject("AdBadge");
            badgeObj.transform.SetParent(_bannerContainer.transform, false);

            var badgeRect = badgeObj.AddComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(0, 0.5f);
            badgeRect.anchorMax = new Vector2(0, 0.5f);
            badgeRect.pivot = new Vector2(0, 0.5f);
            badgeRect.anchoredPosition = new Vector2(padding, 0);
            badgeRect.sizeDelta = new Vector2(badgeWidth, badgeHeight);

            var badgeBg = badgeObj.AddComponent<Image>();
            badgeBg.color = new Color(0.227f, 0.404f, 0.157f, 1f); // #3A6728

            var badgeTextObj = new GameObject("BadgeText");
            badgeTextObj.transform.SetParent(badgeObj.transform, false);
            var badgeTextRect = badgeTextObj.AddComponent<RectTransform>();
            badgeTextRect.anchorMin = Vector2.zero;
            badgeTextRect.anchorMax = Vector2.one;
            badgeTextRect.sizeDelta = Vector2.zero;

            var badgeText = badgeTextObj.AddComponent<Text>();
            badgeText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            badgeText.text = "Ad";
            badgeText.fontSize = 10;
            badgeText.color = Color.white;
            badgeText.fontStyle = FontStyle.Bold;
            badgeText.alignment = TextAnchor.MiddleCenter;

            // Icon (square, loaded from icon_url)
            float iconX = padding + badgeWidth + padding;

            var iconObj = new GameObject("AdIcon");
            iconObj.transform.SetParent(_bannerContainer.transform, false);

            var iconRect = iconObj.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0, 0.5f);
            iconRect.anchorMax = new Vector2(0, 0.5f);
            iconRect.pivot = new Vector2(0, 0.5f);
            iconRect.anchoredPosition = new Vector2(iconX, 0);
            iconRect.sizeDelta = new Vector2(iconSize, iconSize);

            var iconImage = iconObj.AddComponent<RawImage>();
            iconImage.color = new Color(0.3f, 0.3f, 0.3f, 1f); // placeholder color

            if (!string.IsNullOrEmpty(AdData.icon_url))
            {
                _canvas.LoadImage(AdData.icon_url, (texture) =>
                {
                    if (texture != null && iconImage != null)
                    {
                        iconImage.texture = texture;
                        iconImage.color = Color.white;
                    }
                });
            }

            // Arrow ">" on the right
            var arrowObj = new GameObject("Arrow");
            arrowObj.transform.SetParent(_bannerContainer.transform, false);

            var arrowRect = arrowObj.AddComponent<RectTransform>();
            arrowRect.anchorMin = new Vector2(1, 0.5f);
            arrowRect.anchorMax = new Vector2(1, 0.5f);
            arrowRect.pivot = new Vector2(1, 0.5f);
            arrowRect.anchoredPosition = new Vector2(-padding, 0);
            arrowRect.sizeDelta = new Vector2(arrowSize, arrowSize);

            var arrowText = arrowObj.AddComponent<Text>();
            arrowText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            arrowText.text = ">";
            arrowText.fontSize = 16;
            arrowText.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            arrowText.alignment = TextAnchor.MiddleCenter;

            // Headline text (fills remaining space)
            float textX = iconX + iconSize + padding;
            float textRight = arrowSize + padding * 2;

            var textObj = new GameObject("HeadlineText");
            textObj.transform.SetParent(_bannerContainer.transform, false);

            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0, 0);
            textRect.anchorMax = new Vector2(1, 1);
            textRect.offsetMin = new Vector2(textX, 0);
            textRect.offsetMax = new Vector2(-textRight, 0);

            var headlineText = textObj.AddComponent<Text>();
            headlineText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            headlineText.text = AdData.ad_text ?? "";
            headlineText.fontSize = 13;
            headlineText.color = Color.white;
            headlineText.fontStyle = FontStyle.Bold;
            headlineText.alignment = TextAnchor.MiddleLeft;
            headlineText.horizontalOverflow = HorizontalWrapMode.Overflow;
            headlineText.verticalOverflow = VerticalWrapMode.Truncate;

            Debug.Log("[ZeyWinAds] Native banner layout created");
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
            Debug.Log("[ZeyWinAds] Banner clicked");
            OpenClickUrl();
        }

        /// <summary>
        /// Hides the banner ad
        /// </summary>
        public void Hide()
        {
            if (!IsVisible)
                return;

            Debug.Log("[ZeyWinAds] Hiding banner");

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
