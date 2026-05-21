using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using ZeyWinAds.Core;
using ZeyWinAds.UI;
using Logger = ZeyWinAds.Core.Logger;

namespace ZeyWinAds.Ads
{
    /// <summary>
    /// Native text ad displayed as an automated card at the top or bottom of the screen.
    /// </summary>
    public class NativeAd : BaseAd
    {
        public override AdType AdType => AdType.Native;

        public const float MinHeight = 150f;
        public const float DefaultHeight = 150f;
        public const float DefaultTabletHeight = 180f;

        private const float MaxHeight = 280f;
        private const float CardWidthPercent = 0.8f;
        private const float SlideDuration = 0.25f;

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
        private RectTransform _containerRect;
        private RectTransform _cardRect;
        private Coroutine _slideCoroutine;

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
            float height = CalculateAdaptiveHeight();
            float padding = 14f;
            float iconSize = 54f;
            float ctaWidth = 78f;
            float ctaHeight = 42f;
            bool hasCta = !string.IsNullOrEmpty(AdData.cta_text);
            bool hasBody = !string.IsNullOrEmpty(AdData.ad_body);
            bool hasIcon = !string.IsNullOrEmpty(AdData.icon_url);

            _container = new GameObject("NativeAdContainer");
            _container.transform.SetParent(_canvas.transform, false);
            _containerRect = _container.AddComponent<RectTransform>();
            PositionContainer(height);

            var slotImage = _container.AddComponent<Image>();
            slotImage.color = new Color(0f, 0f, 0f, 0f);
            slotImage.raycastTarget = false;

            var cardObj = new GameObject("NativeAdCard");
            cardObj.transform.SetParent(_container.transform, false);
            _cardRect = cardObj.AddComponent<RectTransform>();
            _cardRect.anchorMin = new Vector2((1f - CardWidthPercent) * 0.5f, 0f);
            _cardRect.anchorMax = new Vector2(1f - ((1f - CardWidthPercent) * 0.5f), 0f);
            _cardRect.pivot = new Vector2(0.5f, 0f);
            _cardRect.anchoredPosition = Vector2.zero;
            _cardRect.sizeDelta = new Vector2(0f, height);

            if (Position == BannerPosition.Top)
            {
                _cardRect.anchorMin = new Vector2((1f - CardWidthPercent) * 0.5f, 1f);
                _cardRect.anchorMax = new Vector2(1f - ((1f - CardWidthPercent) * 0.5f), 1f);
                _cardRect.pivot = new Vector2(0.5f, 1f);
            }

            var cardBg = cardObj.AddComponent<Image>();
            cardBg.color = Color.white;

            var clickButton = cardObj.AddComponent<Button>();
            clickButton.targetGraphic = cardBg;
            var colors = clickButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.96f, 0.97f, 0.98f, 1f);
            colors.pressedColor = new Color(0.90f, 0.92f, 0.95f, 1f);
            clickButton.colors = colors;
            clickButton.onClick.AddListener(OnClicked);

            var accentObj = new GameObject("AccentLine");
            accentObj.transform.SetParent(cardObj.transform, false);
            var accentRect = accentObj.AddComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, Position == BannerPosition.Top ? 0f : 1f);
            accentRect.anchorMax = new Vector2(1f, Position == BannerPosition.Top ? 0f : 1f);
            accentRect.pivot = new Vector2(0.5f, Position == BannerPosition.Top ? 0f : 1f);
            accentRect.anchoredPosition = Vector2.zero;
            accentRect.sizeDelta = new Vector2(0f, 3f);
            var accentImage = accentObj.AddComponent<Image>();
            accentImage.color = new Color(0.13f, 0.62f, 0.30f, 1f);
            accentImage.raycastTarget = false;

            float contentLeft = padding;

            if (hasIcon)
            {
                var iconObj = new GameObject("AdIcon");
                iconObj.transform.SetParent(cardObj.transform, false);
                var iconRect = iconObj.AddComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(padding, 0f);
                iconRect.sizeDelta = new Vector2(iconSize, iconSize);

                var iconImage = iconObj.AddComponent<RawImage>();
                iconImage.color = new Color(0.92f, 0.94f, 0.96f, 1f);
                iconImage.raycastTarget = false;

                _canvas.LoadImage(AdData.icon_url, (texture) =>
                {
                    if (texture != null && iconImage != null)
                    {
                        iconImage.texture = texture;
                        iconImage.color = Color.white;
                    }
                });

                contentLeft += iconSize + 12f;
            }

            float badgeWidth = 36f;
            float badgeHeight = 20f;

            var badgeObj = new GameObject("AdBadge");
            badgeObj.transform.SetParent(cardObj.transform, false);
            var badgeRect = badgeObj.AddComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(1, 1);
            badgeRect.anchorMax = new Vector2(1, 1);
            badgeRect.pivot = new Vector2(1, 1);
            badgeRect.anchoredPosition = new Vector2(-padding, -8f);
            badgeRect.sizeDelta = new Vector2(badgeWidth, badgeHeight);

            var badgeBg = badgeObj.AddComponent<Image>();
            badgeBg.color = new Color(0.90f, 0.92f, 0.95f, 1f);
            badgeBg.raycastTarget = false;

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
            badgeText.color = new Color(0.33f, 0.37f, 0.42f, 1f);
            badgeText.alignment = TextAnchor.MiddleCenter;
            badgeText.raycastTarget = false;

            var textGroupObj = new GameObject("TextGroup");
            textGroupObj.transform.SetParent(cardObj.transform, false);
            var textGroupRect = textGroupObj.AddComponent<RectTransform>();
            textGroupRect.anchorMin = new Vector2(0, 0);
            textGroupRect.anchorMax = new Vector2(1, 1);
            textGroupRect.offsetMin = new Vector2(contentLeft, padding);
            textGroupRect.offsetMax = new Vector2(-(padding + ctaWidth + 12f), -padding);

            var headlineObj = new GameObject("Headline");
            headlineObj.transform.SetParent(textGroupObj.transform, false);
            var headlineRect = headlineObj.AddComponent<RectTransform>();
            headlineRect.anchorMin = new Vector2(0, hasBody ? 0.52f : 0f);
            headlineRect.anchorMax = Vector2.one;
            headlineRect.offsetMin = Vector2.zero;
            headlineRect.offsetMax = Vector2.zero;

            var headlineText = headlineObj.AddComponent<Text>();
            headlineText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            headlineText.text = AdData.ad_text ?? "";
            headlineText.fontSize = 19;
            headlineText.color = new Color(0.08f, 0.10f, 0.12f, 1f);
            headlineText.fontStyle = FontStyle.Bold;
            headlineText.horizontalOverflow = HorizontalWrapMode.Wrap;
            headlineText.verticalOverflow = VerticalWrapMode.Truncate;
            headlineText.alignment = hasBody ? TextAnchor.LowerLeft : TextAnchor.MiddleLeft;
            headlineText.raycastTarget = false;

            if (hasBody)
            {
                var bodyObj = new GameObject("BodyText");
                bodyObj.transform.SetParent(textGroupObj.transform, false);
                var bodyRect = bodyObj.AddComponent<RectTransform>();
                bodyRect.anchorMin = new Vector2(0, 0);
                bodyRect.anchorMax = new Vector2(1, 0.50f);
                bodyRect.offsetMin = Vector2.zero;
                bodyRect.offsetMax = Vector2.zero;

                var bodyText = bodyObj.AddComponent<Text>();
                bodyText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                bodyText.text = AdData.ad_body;
                bodyText.fontSize = 15;
                bodyText.color = new Color(0.23f, 0.27f, 0.32f, 1f);
                bodyText.alignment = TextAnchor.UpperLeft;
                bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
                bodyText.verticalOverflow = VerticalWrapMode.Truncate;
                bodyText.raycastTarget = false;
            }

            if (hasCta)
            {
                var ctaObj = new GameObject("CTAButton");
                ctaObj.transform.SetParent(cardObj.transform, false);
                var ctaRect = ctaObj.AddComponent<RectTransform>();
                ctaRect.anchorMin = new Vector2(1, 0.5f);
                ctaRect.anchorMax = new Vector2(1, 0.5f);
                ctaRect.pivot = new Vector2(1, 0.5f);
                ctaRect.anchoredPosition = new Vector2(-padding, -4f);
                ctaRect.sizeDelta = new Vector2(ctaWidth, ctaHeight);

                var ctaBg = ctaObj.AddComponent<Image>();
                ctaBg.color = new Color(0.12f, 0.70f, 0.28f, 1f);

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
                ctaText.fontSize = AdData.cta_text.Length > 10 ? 13 : 15;
                ctaText.color = Color.white;
                ctaText.fontStyle = FontStyle.Bold;
                ctaText.alignment = TextAnchor.MiddleCenter;
                ctaText.horizontalOverflow = HorizontalWrapMode.Wrap;
                ctaText.verticalOverflow = VerticalWrapMode.Truncate;
                ctaText.raycastTarget = false;
            }
            else
            {
                var arrowObj = new GameObject("Arrow");
                arrowObj.transform.SetParent(cardObj.transform, false);
                var arrowRect = arrowObj.AddComponent<RectTransform>();
                arrowRect.anchorMin = new Vector2(1, 0.5f);
                arrowRect.anchorMax = new Vector2(1, 0.5f);
                arrowRect.pivot = new Vector2(1, 0.5f);
                arrowRect.anchoredPosition = new Vector2(-padding, -4f);
                arrowRect.sizeDelta = new Vector2(24f, 24f);

                var arrowText = arrowObj.AddComponent<Text>();
                arrowText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                arrowText.text = "\u203A";
                arrowText.fontSize = 28;
                arrowText.color = new Color(0.20f, 0.24f, 0.28f, 0.65f);
                arrowText.alignment = TextAnchor.MiddleCenter;
                arrowText.raycastTarget = false;
            }

            StartSlideIn();
            Logger.Debug("Native ad layout created");
        }

        private float CalculateAdaptiveHeight()
        {
            float height = Mathf.Max(MinHeight, GetCurrentHeight());
            int textLength = (AdData.ad_text?.Length ?? 0) + (AdData.ad_body?.Length ?? 0);

            if (textLength > 85)
                height += 32f;

            if (textLength > 150)
                height += 34f;

            if (!string.IsNullOrEmpty(AdData.cta_text) && AdData.cta_text.Length > 12)
                height += 14f;

            return Mathf.Clamp(height, MinHeight, MaxHeight);
        }

        private void PositionContainer(float height)
        {
            Rect safeArea = Screen.safeArea;
            float topInset = Screen.height - (safeArea.y + safeArea.height);
            float bottomInset = safeArea.y;
            float scaleFactor = _canvas.GetScaleFactor();

            if (Position == BannerPosition.Top)
            {
                _containerRect.anchorMin = new Vector2(0, 1);
                _containerRect.anchorMax = new Vector2(1, 1);
                _containerRect.pivot = new Vector2(0.5f, 1);
                _containerRect.anchoredPosition = new Vector2(0, -topInset / scaleFactor);
            }
            else
            {
                _containerRect.anchorMin = new Vector2(0, 0);
                _containerRect.anchorMax = new Vector2(1, 0);
                _containerRect.pivot = new Vector2(0.5f, 0);
                _containerRect.anchoredPosition = new Vector2(0, bottomInset / scaleFactor);
            }

            _containerRect.sizeDelta = new Vector2(0, height);
        }

        private void StartSlideIn()
        {
            if (_cardRect == null)
                return;

            if (_slideCoroutine != null && _canvas != null)
            {
                _canvas.StopCoroutine(_slideCoroutine);
                _slideCoroutine = null;
            }

            Vector2 target = Vector2.zero;
            float offset = _cardRect.rect.height + 24f;
            Vector2 start = Position == BannerPosition.Top
                ? new Vector2(0, offset)
                : new Vector2(0, -offset);

            _cardRect.anchoredPosition = start;
            _slideCoroutine = _canvas.StartCoroutine(SlideCard(start, target));
        }

        private IEnumerator SlideCard(Vector2 start, Vector2 target)
        {
            float elapsed = 0f;

            while (elapsed < SlideDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / SlideDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                _cardRect.anchoredPosition = Vector2.LerpUnclamped(start, target, eased);
                yield return null;
            }

            _cardRect.anchoredPosition = target;
            _slideCoroutine = null;
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

            if (_slideCoroutine != null && _canvas != null)
            {
                _canvas.StopCoroutine(_slideCoroutine);
                _slideCoroutine = null;
            }

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
                StartSlideIn();
            }
        }

        public void SetPosition(BannerPosition newPosition)
        {
            if (Position == newPosition)
                return;

            Position = newPosition;

            if (_container != null && _canvas != null)
            {
                if (_slideCoroutine != null)
                {
                    _canvas.StopCoroutine(_slideCoroutine);
                    _slideCoroutine = null;
                }

                UnityEngine.Object.Destroy(_container);
                _container = null;
                _containerRect = null;
                _cardRect = null;
                CreateLayout();
            }
        }

        public override void Destroy()
        {
            if (_slideCoroutine != null && _canvas != null)
            {
                _canvas.StopCoroutine(_slideCoroutine);
                _slideCoroutine = null;
            }

            if (_canvas != null)
            {
                _canvas.Destroy();
                _canvas = null;
            }

            _container = null;
            _containerRect = null;
            _cardRect = null;
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
