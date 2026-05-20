using System;
using UnityEngine;
using UnityEngine.UI;
using ZeyWinAds.Core;
using ZeyWinAds.UI;
using Logger = ZeyWinAds.Core.Logger;

namespace ZeyWinAds.Ads
{
    /// <summary>
    /// Native text ad displayed as a compact strip at the top or bottom of the screen.
    /// </summary>
    public class NativeAd : BaseAd
    {
        public override AdType AdType => AdType.Native;

        public const float DefaultHeight = 128f;
        public const float DefaultTabletHeight = 160f;

        private static float? _customHeight = null;
        private static float? _customTabletHeight = null;

        public static float Height => _customHeight ?? DefaultHeight;
        public static float TabletHeight => _customTabletHeight ?? DefaultTabletHeight;

        public static void SetHeight(float? height) => _customHeight = height;
        public static void SetTabletHeight(float? height) => _customTabletHeight = height;

        public static float GetCurrentHeight()
        {
            return DeviceInfo.GetDeviceType() == "tablet" ? TabletHeight : Height;
        }

        public BannerPosition Position { get; private set; } = BannerPosition.Bottom;
        public bool IsVisible { get; private set; }

        private AdCanvas _canvas;
        private GameObject _container;

        public NativeAd()
        {
        }

        public void Show(BannerPosition position, Action onClose = null)
        {
            Position = position;
            base.Show(onClose);
        }

        public override void Show(Action onClose = null)
        {
            Show(BannerPosition.Bottom, onClose);
        }

        protected override void OnShow()
        {
            Logger.Debug("Showing native ad at {0}", Position);

            IsVisible = true;

            _canvas = AdCanvas.Create("NativeAdCanvas");
            _canvas.SetSortingOrder(998);

            CreateLayout();
        }

        private void CreateLayout()
        {
            float height = GetCurrentHeight();
            float padding = 14f;
            float iconSize = 64f;

            // Container
            _container = new GameObject("NativeAdContainer");
            _container.transform.SetParent(_canvas.transform, false);

            var containerRect = _container.AddComponent<RectTransform>();

            Rect safeArea = Screen.safeArea;
            float topInset = Screen.height - (safeArea.y + safeArea.height);
            float bottomInset = safeArea.y;
            float scaleFactor = _canvas.GetScaleFactor();

            if (Position == BannerPosition.Top)
            {
                containerRect.anchorMin = new Vector2(0, 1);
                containerRect.anchorMax = new Vector2(1, 1);
                containerRect.pivot = new Vector2(0.5f, 1);
                containerRect.anchoredPosition = new Vector2(0, -topInset / scaleFactor);
            }
            else
            {
                containerRect.anchorMin = new Vector2(0, 0);
                containerRect.anchorMax = new Vector2(1, 0);
                containerRect.pivot = new Vector2(0.5f, 0);
                containerRect.anchoredPosition = new Vector2(0, bottomInset / scaleFactor);
            }

            containerRect.sizeDelta = new Vector2(0, height);

            // Background
            var bg = _container.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.12f, 0.14f, 1f);

            // Click area
            var clickButton = _container.AddComponent<Button>();
            clickButton.targetGraphic = bg;
            var colors = clickButton.colors;
            colors.highlightedColor = new Color(0.18f, 0.18f, 0.20f, 1f);
            colors.pressedColor = new Color(0.22f, 0.22f, 0.24f, 1f);
            clickButton.colors = colors;
            clickButton.onClick.AddListener(OnClicked);

            // Accent line
            var accentObj = new GameObject("AccentLine");
            accentObj.transform.SetParent(_container.transform, false);
            var accentRect = accentObj.AddComponent<RectTransform>();
            if (Position == BannerPosition.Top)
            {
                accentRect.anchorMin = new Vector2(0, 0);
                accentRect.anchorMax = new Vector2(1, 0);
                accentRect.pivot = new Vector2(0.5f, 0);
            }
            else
            {
                accentRect.anchorMin = new Vector2(0, 1);
                accentRect.anchorMax = new Vector2(1, 1);
                accentRect.pivot = new Vector2(0.5f, 1);
            }
            accentRect.anchoredPosition = Vector2.zero;
            accentRect.sizeDelta = new Vector2(0, 2f);
            var accentImage = accentObj.AddComponent<Image>();
            accentImage.color = new Color(0.30f, 0.56f, 1f, 0.8f);

            // "Ad" badge - top-right corner
            float badgeWidth = 36f;
            float badgeHeight = 22f;

            var badgeObj = new GameObject("AdBadge");
            badgeObj.transform.SetParent(_container.transform, false);
            var badgeRect = badgeObj.AddComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(1, 1);
            badgeRect.anchorMax = new Vector2(1, 1);
            badgeRect.pivot = new Vector2(1, 1);
            badgeRect.anchoredPosition = new Vector2(-padding, -6f);
            badgeRect.sizeDelta = new Vector2(badgeWidth, badgeHeight);

            var badgeBg = badgeObj.AddComponent<Image>();
            badgeBg.color = new Color(1f, 1f, 1f, 0.15f);

            var badgeTextObj = new GameObject("BadgeText");
            badgeTextObj.transform.SetParent(badgeObj.transform, false);
            var badgeTextRect = badgeTextObj.AddComponent<RectTransform>();
            badgeTextRect.anchorMin = Vector2.zero;
            badgeTextRect.anchorMax = Vector2.one;
            badgeTextRect.sizeDelta = Vector2.zero;

            var badgeText = badgeTextObj.AddComponent<Text>();
            badgeText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            badgeText.text = "Ad";
            badgeText.fontSize = 14;
            badgeText.color = new Color(1f, 1f, 1f, 0.5f);
            badgeText.alignment = TextAnchor.MiddleCenter;

            // === Centered block: [Icon] [Texts] [CTA] ===
            bool hasCta = !string.IsNullOrEmpty(AdData.cta_text);
            bool hasBody = !string.IsNullOrEmpty(AdData.ad_body);
            float ctaWidth = hasCta ? 100f : 24f;
            float textWidth = 250f;
            float gap = 12f;

            // Total content width
            float totalWidth = iconSize + gap + textWidth + gap + ctaWidth;

            // Centered content wrapper — fixed width, anchored to center
            var contentObj = new GameObject("Content");
            contentObj.transform.SetParent(_container.transform, false);
            var contentRect = contentObj.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0.5f, 0);
            contentRect.anchorMax = new Vector2(0.5f, 1);
            contentRect.pivot = new Vector2(0.5f, 0.5f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(totalWidth, 0);

            // Icon — left side, vertically centered
            var iconObj = new GameObject("AdIcon");
            iconObj.transform.SetParent(contentObj.transform, false);
            var iconRect = iconObj.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0, 0.5f);
            iconRect.anchorMax = new Vector2(0, 0.5f);
            iconRect.pivot = new Vector2(0, 0.5f);
            iconRect.anchoredPosition = Vector2.zero;
            iconRect.sizeDelta = new Vector2(iconSize, iconSize);

            var iconImage = iconObj.AddComponent<RawImage>();
            iconImage.color = new Color(0.2f, 0.2f, 0.22f, 1f);

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

            // Text block — middle, vertically centered
            float textX = iconSize + gap;

            var textGroupObj = new GameObject("TextGroup");
            textGroupObj.transform.SetParent(contentObj.transform, false);
            var textGroupRect = textGroupObj.AddComponent<RectTransform>();
            textGroupRect.anchorMin = new Vector2(0, 0);
            textGroupRect.anchorMax = new Vector2(0, 1);
            textGroupRect.pivot = new Vector2(0, 0.5f);
            textGroupRect.anchoredPosition = new Vector2(textX, 0);
            textGroupRect.sizeDelta = new Vector2(textWidth, 0);

            // Headline
            var headlineObj = new GameObject("Headline");
            headlineObj.transform.SetParent(textGroupObj.transform, false);
            var headlineRect = headlineObj.AddComponent<RectTransform>();

            var headlineText = headlineObj.AddComponent<Text>();
            headlineText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            headlineText.text = AdData.ad_text ?? "";
            headlineText.fontSize = 20;
            headlineText.color = new Color(1f, 1f, 1f, 0.95f);
            headlineText.fontStyle = FontStyle.Bold;
            headlineText.horizontalOverflow = HorizontalWrapMode.Overflow;
            headlineText.verticalOverflow = VerticalWrapMode.Truncate;

            if (hasBody)
            {
                headlineRect.anchorMin = new Vector2(0, 0.5f);
                headlineRect.anchorMax = new Vector2(1, 1f);
                headlineRect.offsetMin = Vector2.zero;
                headlineRect.offsetMax = Vector2.zero;
                headlineText.alignment = TextAnchor.LowerLeft;

                var bodyObj = new GameObject("BodyText");
                bodyObj.transform.SetParent(textGroupObj.transform, false);
                var bodyRect = bodyObj.AddComponent<RectTransform>();
                bodyRect.anchorMin = new Vector2(0, 0);
                bodyRect.anchorMax = new Vector2(1, 0.5f);
                bodyRect.offsetMin = Vector2.zero;
                bodyRect.offsetMax = Vector2.zero;

                var bodyText = bodyObj.AddComponent<Text>();
                bodyText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                bodyText.text = AdData.ad_body;
                bodyText.fontSize = 15;
                bodyText.color = new Color(1f, 1f, 1f, 0.5f);
                bodyText.alignment = TextAnchor.UpperLeft;
                bodyText.horizontalOverflow = HorizontalWrapMode.Overflow;
                bodyText.verticalOverflow = VerticalWrapMode.Truncate;
            }
            else
            {
                headlineRect.anchorMin = Vector2.zero;
                headlineRect.anchorMax = Vector2.one;
                headlineRect.offsetMin = Vector2.zero;
                headlineRect.offsetMax = Vector2.zero;
                headlineText.alignment = TextAnchor.MiddleLeft;
            }

            // CTA / Arrow — right side, vertically centered
            float ctaX = textX + textWidth + gap;

            if (hasCta)
            {
                var ctaObj = new GameObject("CTAButton");
                ctaObj.transform.SetParent(contentObj.transform, false);
                var ctaRect = ctaObj.AddComponent<RectTransform>();
                ctaRect.anchorMin = new Vector2(0, 0.5f);
                ctaRect.anchorMax = new Vector2(0, 0.5f);
                ctaRect.pivot = new Vector2(0, 0.5f);
                ctaRect.anchoredPosition = new Vector2(ctaX, 0);
                ctaRect.sizeDelta = new Vector2(ctaWidth, 40f);

                var ctaBg = ctaObj.AddComponent<Image>();
                ctaBg.color = new Color(0.30f, 0.56f, 1f, 1f);

                var ctaButton = ctaObj.AddComponent<Button>();
                ctaButton.targetGraphic = ctaBg;
                ctaButton.onClick.AddListener(OnClicked);

                var ctaTextObj = new GameObject("CTAText");
                ctaTextObj.transform.SetParent(ctaObj.transform, false);
                var ctaTextRect = ctaTextObj.AddComponent<RectTransform>();
                ctaTextRect.anchorMin = Vector2.zero;
                ctaTextRect.anchorMax = Vector2.one;
                ctaTextRect.sizeDelta = Vector2.zero;

                var ctaText = ctaTextObj.AddComponent<Text>();
                ctaText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                ctaText.text = AdData.cta_text;
                ctaText.fontSize = 16;
                ctaText.color = Color.white;
                ctaText.fontStyle = FontStyle.Bold;
                ctaText.alignment = TextAnchor.MiddleCenter;
            }
            else
            {
                var arrowObj = new GameObject("Arrow");
                arrowObj.transform.SetParent(contentObj.transform, false);
                var arrowRect = arrowObj.AddComponent<RectTransform>();
                arrowRect.anchorMin = new Vector2(0, 0.5f);
                arrowRect.anchorMax = new Vector2(0, 0.5f);
                arrowRect.pivot = new Vector2(0, 0.5f);
                arrowRect.anchoredPosition = new Vector2(ctaX, 0);
                arrowRect.sizeDelta = new Vector2(24f, 24f);

                var arrowText = arrowObj.AddComponent<Text>();
                arrowText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                arrowText.text = "\u203A";
                arrowText.fontSize = 28;
                arrowText.color = new Color(1f, 1f, 1f, 0.3f);
                arrowText.alignment = TextAnchor.MiddleCenter;
            }

            Logger.Debug("Native ad layout created");
        }

        private void OnClicked()
        {
            Logger.Debug("Native ad clicked");
            OpenClickUrl();
        }

        public void Hide()
        {
            if (!IsVisible)
                return;

            Logger.Debug("Hiding native ad");
            IsVisible = false;

            if (_container != null)
                _container.SetActive(false);
        }

        public void ShowAgain()
        {
            if (!IsShowing || IsVisible)
                return;

            if (_container != null)
            {
                _container.SetActive(true);
                IsVisible = true;
                TrackImpression();
            }
        }

        public void SetPosition(BannerPosition newPosition)
        {
            if (Position == newPosition)
                return;

            Position = newPosition;

            if (_container != null && _canvas != null)
            {
                var rectTransform = _container.GetComponent<RectTransform>();
                float height = GetCurrentHeight();

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

                rectTransform.sizeDelta = new Vector2(0, height);
            }
        }

        public override void Destroy()
        {
            if (_canvas != null)
            {
                _canvas.Destroy();
                _canvas = null;
            }

            _container = null;
            IsVisible = false;

            base.Destroy();
        }

        protected override void OnDestroy()
        {
        }

        protected override void OnClose()
        {
            IsVisible = false;
            base.OnClose();
        }
    }
}
