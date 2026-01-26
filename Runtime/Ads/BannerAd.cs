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
        /// Standard banner height in pixels
        /// </summary>
        public const float BannerHeight = 50f;

        /// <summary>
        /// Standard banner height for tablets in pixels
        /// </summary>
        public const float TabletBannerHeight = 90f;

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

            // Load and display banner image
            LoadBannerImage();
        }

        private void CreateBannerContainer()
        {
            _bannerContainer = new GameObject("BannerContainer");
            _bannerContainer.transform.SetParent(_canvas.transform, false);

            var rectTransform = _bannerContainer.AddComponent<RectTransform>();

            // Calculate banner height based on device type
            float bannerHeight = DeviceInfo.GetDeviceType() == "tablet" ? TabletBannerHeight : BannerHeight;

            // Get safe area offsets
            Rect safeArea = Screen.safeArea;
            float topInset = Screen.height - (safeArea.y + safeArea.height);
            float bottomInset = safeArea.y;

            // Position based on setting with safe area
            if (Position == BannerPosition.Top)
            {
                rectTransform.anchorMin = new Vector2(0, 1);
                rectTransform.anchorMax = new Vector2(1, 1);
                rectTransform.pivot = new Vector2(0.5f, 1);
                rectTransform.anchoredPosition = new Vector2(0, -topInset);
            }
            else // Bottom
            {
                rectTransform.anchorMin = new Vector2(0, 0);
                rectTransform.anchorMax = new Vector2(1, 0);
                rectTransform.pivot = new Vector2(0.5f, 0);
                rectTransform.anchoredPosition = new Vector2(0, bottomInset);
            }

            rectTransform.sizeDelta = new Vector2(0, bannerHeight);

            Debug.Log($"[ZeyWinAds] Banner safe area - top inset: {topInset}, bottom inset: {bottomInset}");

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

            if (_bannerContainer != null)
            {
                var rectTransform = _bannerContainer.GetComponent<RectTransform>();
                float bannerHeight = DeviceInfo.GetDeviceType() == "tablet" ? TabletBannerHeight : BannerHeight;

                // Get safe area offsets
                Rect safeArea = Screen.safeArea;
                float topInset = Screen.height - (safeArea.y + safeArea.height);
                float bottomInset = safeArea.y;

                if (Position == BannerPosition.Top)
                {
                    rectTransform.anchorMin = new Vector2(0, 1);
                    rectTransform.anchorMax = new Vector2(1, 1);
                    rectTransform.pivot = new Vector2(0.5f, 1);
                    rectTransform.anchoredPosition = new Vector2(0, -topInset);
                }
                else
                {
                    rectTransform.anchorMin = new Vector2(0, 0);
                    rectTransform.anchorMax = new Vector2(1, 0);
                    rectTransform.pivot = new Vector2(0.5f, 0);
                    rectTransform.anchoredPosition = new Vector2(0, bottomInset);
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
