using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using ZeyWinAds.Core;
using ZeyWinAds.Mediation;
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

        public const float MinHeight = 170f;
        public const float DefaultHeight = 176f;
        public const float DefaultTabletHeight = 210f;

        private const float MaxHeight = 360f;
        private const float SlideDuration = 0.34f;
        private const float AttentionIntervalSeconds = 6.5f;
        private const float VariantRotationIntervalSeconds = 15f;
        private const float BottomSafeAreaRelax = 8f;

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
        private Coroutine _attentionCoroutine;
        private Coroutine _shineCoroutine;
        private Coroutine _variantRotationCoroutine;
        private RectTransform _iconRect;
        private RectTransform _ctaRect;
        private RectTransform _shineRect;
        private Vector2 _cardRestPosition = Vector2.zero;
        private BannerVariant _bannerVariant = BannerVariant.IconLeading;
        private bool _canRotateVariant;
        private static Font _preferredFont;

        private enum BannerVariant
        {
            IconLeading,
            CtaLeading
        }

        private struct LayoutMetrics
        {
            public float Height;
            public float Padding;
            public float IconSize;
            public float CtaWidth;
            public float CtaHeight;
            public float CardWidthPercent;
            public float HeadlineSize;
            public float BodySize;
            public float CtaFontSize;
            public float BadgeFontSize;
            public float BadgeWidth;
            public float BadgeHeight;
            public float Gap;
            public float TextGap;
            public float HeadlineHeight;
            public float BodyHeight;
            public float TextWidth;
            public float BottomRelax;
            public int HeadlineLines;
            public int BodyLines;
        }

        private static Font PreferredFont
        {
            get
            {
                if (_preferredFont != null)
                    return _preferredFont;

                try
                {
                    _preferredFont = Font.CreateDynamicFontFromOSFont(
                        new[] { "Roboto", "Noto Sans", "NotoSans", "Helvetica", "Arial" },
                        32);
                }
                catch
                {
                    _preferredFont = null;
                }

                if (_preferredFont == null)
                    _preferredFont = ZeyWinFont.GetPreferred();

                return _preferredFont;
            }
        }

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
            _bannerVariant = BannerVariant.IconLeading;
            AdMediator.OnZeyWinBannerShown();

            _canvas = AdCanvas.Create("NativeAdCanvas");
            _canvas.SetSortingOrder(998);

            CreateLayout();
        }

        private void CreateLayout(bool restartVariantRotation = true, bool animateIn = true)
        {
            LayoutMetrics layout = CalculateLayoutMetrics();
            float height = layout.Height;
            float padding = layout.Padding;
            float iconSize = layout.IconSize;
            float ctaWidth = layout.CtaWidth;
            float ctaHeight = layout.CtaHeight;
            bool hasCta = !string.IsNullOrEmpty(AdData.cta_text);
            bool hasBody = !string.IsNullOrEmpty(AdData.ad_body);
            bool hasIcon = !string.IsNullOrEmpty(AdData.icon_url);
            bool swapIconAndCta = _bannerVariant == BannerVariant.CtaLeading && hasIcon && hasCta;
            var theme = AdThemeController.Current;
            _iconRect = null;
            _ctaRect = null;
            _shineRect = null;

            _container = new GameObject("NativeAdContainer");
            _container.transform.SetParent(_canvas.transform, false);
            _containerRect = _container.AddComponent<RectTransform>();
            PositionContainer(layout);

            var slotImage = _container.AddComponent<Image>();
            slotImage.color = new Color(0f, 0f, 0f, 0f);
            slotImage.raycastTarget = false;

            var cardObj = new GameObject("NativeAdCard");
            cardObj.transform.SetParent(_container.transform, false);
            _cardRect = cardObj.AddComponent<RectTransform>();
            _cardRect.anchorMin = new Vector2((1f - layout.CardWidthPercent) * 0.5f, 0f);
            _cardRect.anchorMax = new Vector2(1f - ((1f - layout.CardWidthPercent) * 0.5f), 0f);
            _cardRect.pivot = new Vector2(0.5f, 0f);
            _cardRect.anchoredPosition = Vector2.zero;
            _cardRect.sizeDelta = new Vector2(0f, height);
            _cardRestPosition = Vector2.zero;

            if (Position == BannerPosition.Top)
            {
                _cardRect.anchorMin = new Vector2((1f - layout.CardWidthPercent) * 0.5f, 1f);
                _cardRect.anchorMax = new Vector2(1f - ((1f - layout.CardWidthPercent) * 0.5f), 1f);
                _cardRect.pivot = new Vector2(0.5f, 1f);
            }

            var mask = cardObj.AddComponent<RectMask2D>();

            var cardBg = cardObj.AddComponent<Image>();
            cardBg.color = theme.Surface;

            var clickButton = cardObj.AddComponent<Button>();
            clickButton.targetGraphic = cardBg;
            var colors = clickButton.colors;
            colors.normalColor = theme.Surface;
            colors.highlightedColor = theme.SurfaceHighlight;
            colors.pressedColor = theme.SurfacePressed;
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
            accentImage.color = theme.Accent;
            accentImage.raycastTarget = false;

            CreateShine(cardObj.transform, height, layout);

            float contentLeft = padding;
            float contentRight = padding;

            if (hasIcon)
            {
                var iconObj = new GameObject("AdIcon");
                iconObj.transform.SetParent(cardObj.transform, false);
                var iconRect = iconObj.AddComponent<RectTransform>();
                _iconRect = iconRect;
                iconRect.anchorMin = new Vector2(swapIconAndCta ? 1f : 0f, 0.5f);
                iconRect.anchorMax = new Vector2(swapIconAndCta ? 1f : 0f, 0.5f);
                iconRect.pivot = new Vector2(swapIconAndCta ? 1f : 0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(swapIconAndCta ? -padding : padding, 0f);
                iconRect.sizeDelta = new Vector2(iconSize, iconSize);

                var iconImage = iconObj.AddComponent<RawImage>();
                iconImage.color = theme.ImagePlaceholder;
                iconImage.raycastTarget = false;

                _canvas.LoadImage(AdData.icon_url, (texture) =>
                {
                    if (texture != null && iconImage != null)
                    {
                        iconImage.texture = texture;
                        iconImage.color = Color.white;
                    }
                });

                if (swapIconAndCta)
                    contentRight += iconSize + 16f;
                else
                    contentLeft += iconSize + 16f;
            }

            float badgeWidth = layout.BadgeWidth;
            float badgeHeight = layout.BadgeHeight;

            var badgeObj = new GameObject("AdBadge");
            badgeObj.transform.SetParent(cardObj.transform, false);
            var badgeRect = badgeObj.AddComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(1, 1);
            badgeRect.anchorMax = new Vector2(1, 1);
            badgeRect.pivot = new Vector2(1, 1);
            badgeRect.anchoredPosition = new Vector2(-padding, -8f);
            badgeRect.sizeDelta = new Vector2(badgeWidth, badgeHeight);

            var badgeBg = badgeObj.AddComponent<Image>();
            badgeBg.color = theme.BadgeBackground;
            badgeBg.raycastTarget = false;

            var badgeTextObj = new GameObject("BadgeText");
            badgeTextObj.transform.SetParent(badgeObj.transform, false);
            var badgeTextRect = badgeTextObj.AddComponent<RectTransform>();
            badgeTextRect.anchorMin = Vector2.zero;
            badgeTextRect.anchorMax = Vector2.one;
            badgeTextRect.sizeDelta = Vector2.zero;

            var badgeText = badgeTextObj.AddComponent<Text>();
            ApplyTypography(
                badgeText,
                "Ad",
                layout.BadgeFontSize,
                Mathf.Max(10f, layout.BadgeFontSize - 4f),
                FontStyle.Bold,
                theme.BadgeText,
                TextAnchor.MiddleCenter,
                false);
            badgeText.raycastTarget = false;

            var textGroupObj = new GameObject("TextGroup");
            textGroupObj.transform.SetParent(cardObj.transform, false);
            var textGroupRect = textGroupObj.AddComponent<RectTransform>();
            textGroupRect.anchorMin = new Vector2(0, 0);
            textGroupRect.anchorMax = new Vector2(1, 1);
            textGroupRect.offsetMin = new Vector2(contentLeft, padding);
            if (hasCta)
            {
                if (swapIconAndCta)
                    contentLeft += ctaWidth + layout.Gap;
                else
                    contentRight += ctaWidth + layout.Gap;
            }
            else
            {
                contentRight += 24f + layout.Gap;
            }
            textGroupRect.offsetMin = new Vector2(contentLeft, padding);
            textGroupRect.offsetMax = new Vector2(-contentRight, -padding);

            float stackHeight = layout.HeadlineHeight + (hasBody ? layout.TextGap + layout.BodyHeight : 0f);
            float textBoxHeight = Mathf.Max(1f, height - (padding * 2f));
            float topOffset = Mathf.Max(0f, (textBoxHeight - stackHeight) * 0.5f);

            var headlineObj = new GameObject("Headline");
            headlineObj.transform.SetParent(textGroupObj.transform, false);
            var headlineRect = headlineObj.AddComponent<RectTransform>();
            headlineRect.anchorMin = new Vector2(0, 1);
            headlineRect.anchorMax = new Vector2(1, 1);
            headlineRect.pivot = new Vector2(0, 1);
            headlineRect.anchoredPosition = new Vector2(0f, -topOffset);
            headlineRect.sizeDelta = new Vector2(0f, layout.HeadlineHeight);

            var headlineText = headlineObj.AddComponent<Text>();
            ApplyTypography(
                headlineText,
                AdData.ad_text ?? "",
                layout.HeadlineSize,
                Mathf.Max(30f, layout.HeadlineSize - 8f),
                FontStyle.Bold,
                theme.TextPrimary,
                TextAnchor.UpperLeft,
                true,
                false);
            headlineText.raycastTarget = false;

            if (hasBody)
            {
                var bodyObj = new GameObject("BodyText");
                bodyObj.transform.SetParent(textGroupObj.transform, false);
                var bodyRect = bodyObj.AddComponent<RectTransform>();
                bodyRect.anchorMin = new Vector2(0, 1);
                bodyRect.anchorMax = new Vector2(1, 1);
                bodyRect.pivot = new Vector2(0, 1);
                bodyRect.anchoredPosition = new Vector2(0f, -(topOffset + layout.HeadlineHeight + layout.TextGap));
                bodyRect.sizeDelta = new Vector2(0f, layout.BodyHeight);

                var bodyText = bodyObj.AddComponent<Text>();
                ApplyTypography(
                    bodyText,
                    AdData.ad_body,
                    layout.BodySize,
                    Mathf.Max(24f, layout.BodySize - 7f),
                    FontStyle.Normal,
                    theme.TextSecondary,
                    TextAnchor.UpperLeft,
                    true,
                    false);
                bodyText.raycastTarget = false;
            }

            if (hasCta)
            {
                var ctaObj = new GameObject("CTAButton");
                ctaObj.transform.SetParent(cardObj.transform, false);
                var ctaRect = ctaObj.AddComponent<RectTransform>();
                _ctaRect = ctaRect;
                ctaRect.anchorMin = new Vector2(swapIconAndCta ? 0f : 1f, 0.5f);
                ctaRect.anchorMax = new Vector2(swapIconAndCta ? 0f : 1f, 0.5f);
                ctaRect.pivot = new Vector2(swapIconAndCta ? 0f : 1f, 0.5f);
                ctaRect.anchoredPosition = new Vector2(swapIconAndCta ? padding : -padding, 0f);
                ctaRect.sizeDelta = new Vector2(ctaWidth, ctaHeight);

                var ctaBg = ctaObj.AddComponent<Image>();
                ctaBg.color = theme.PrimaryButton;

                var ctaButton = ctaObj.AddComponent<Button>();
                ctaButton.targetGraphic = ctaBg;
                ctaButton.onClick.AddListener(OnClicked);

                var ctaTextObj = new GameObject("CTAText");
                ctaTextObj.transform.SetParent(ctaObj.transform, false);
                var ctaTextRect = ctaTextObj.AddComponent<RectTransform>();
                ctaTextRect.anchorMin = Vector2.zero;
                ctaTextRect.anchorMax = Vector2.one;
                ctaTextRect.sizeDelta = Vector2.zero;
                ctaTextRect.offsetMin = new Vector2(10f, 6f);
                ctaTextRect.offsetMax = new Vector2(-10f, -6f);

                var ctaText = ctaTextObj.AddComponent<Text>();
                float ctaMax = layout.CtaFontSize;
                ApplyTypography(
                    ctaText,
                    AdData.cta_text,
                    ctaMax,
                    Mathf.Max(24f, ctaMax - 6f),
                    FontStyle.Bold,
                    theme.PrimaryButtonText,
                    TextAnchor.MiddleCenter,
                    true,
                    false);
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
                arrowRect.anchoredPosition = new Vector2(-padding, 0f);
                arrowRect.sizeDelta = new Vector2(24f, 24f);

                var arrowText = arrowObj.AddComponent<Text>();
                ApplyTypography(
                    arrowText,
                    "\u203A",
                    28f,
                    22f,
                    FontStyle.Bold,
                    theme.TextMuted,
                    TextAnchor.MiddleCenter,
                    false);
                arrowText.raycastTarget = false;
            }

            _canRotateVariant = hasIcon && hasCta;

            if (animateIn && AdAnimationConfig.NativeBannerSlideEnabled)
                StartSlideIn();
            else
            {
                _cardRestPosition = Vector2.zero;
                if (_cardRect != null)
                    _cardRect.anchoredPosition = _cardRestPosition;
                StartAttentionLoops();
            }

            if (restartVariantRotation)
                StartVariantRotation(_canRotateVariant);

            Logger.Log(
                "Native-баннер: крупный шрифт height={0}, textWidth={1}, titleSize={2}, bodySize={3}, ctaSize={4}, titleLines={5}, bodyLines={6}, bottomRelax={7}, variant={8}",
                Mathf.RoundToInt(layout.Height),
                Mathf.RoundToInt(layout.TextWidth),
                Mathf.RoundToInt(layout.HeadlineSize),
                Mathf.RoundToInt(layout.BodySize),
                Mathf.RoundToInt(layout.CtaFontSize),
                layout.HeadlineLines,
                layout.BodyLines,
                Mathf.RoundToInt(layout.BottomRelax),
                _bannerVariant);
        }

        private LayoutMetrics CalculateLayoutMetrics()
        {
            float scaleFactor = _canvas != null ? Mathf.Max(0.01f, _canvas.GetScaleFactor()) : 1f;
            float canvasWidth = Screen.width / scaleFactor;
            float widthFactor = Mathf.Clamp(canvasWidth / 1080f, 0.78f, 1.18f);
            bool tablet = DeviceInfo.GetDeviceType() == "tablet";

            string title = NormalizeWrappedText(AdData.ad_text, true);
            string body = NormalizeWrappedText(AdData.ad_body, true);
            string cta = NormalizeWrappedText(AdData.cta_text, true);
            int titleLength = CountVisualLength(title);
            int bodyLength = CountVisualLength(body);
            int ctaLength = CountVisualLength(cta);
            int textLength = titleLength + bodyLength;
            bool hasBody = !string.IsNullOrEmpty(body);
            bool hasCta = !string.IsNullOrEmpty(cta);

            LayoutMetrics m = new LayoutMetrics();
            m.CardWidthPercent = canvasWidth < 720f ? 1.00f : 0.98f;
            m.Padding = Mathf.Round(Mathf.Clamp(18f * widthFactor, 12f, 24f));
            m.Gap = Mathf.Round(Mathf.Clamp(18f * widthFactor, 12f, 24f));
            m.TextGap = Mathf.Round(Mathf.Clamp(6f * widthFactor, 3f, 8f));
            m.IconSize = Mathf.Round(Mathf.Clamp((tablet ? 122f : 108f) * widthFactor, 82f, 138f));
            m.HeadlineSize = Mathf.Round(Mathf.Clamp((titleLength > 118 ? 34f : titleLength > 88 ? 37f : titleLength > 52 ? 41f : 48f) * widthFactor, 32f, 52f));
            m.BodySize = Mathf.Round(Mathf.Clamp((bodyLength > 96 ? 29f : bodyLength > 76 ? 31f : bodyLength > 44 ? 34f : 38f) * widthFactor, 26f, 42f));
            m.CtaFontSize = Mathf.Round(Mathf.Clamp((ctaLength > 14 ? 31f : ctaLength > 9 ? 35f : 39f) * widthFactor, 28f, 42f));
            m.BadgeFontSize = Mathf.Round(Mathf.Clamp(14f * widthFactor, 11f, 16f));
            m.BadgeWidth = Mathf.Round(Mathf.Clamp(38f * widthFactor, 30f, 44f));
            m.BadgeHeight = Mathf.Round(Mathf.Clamp(22f * widthFactor, 18f, 26f));

            float baseHeight = Mathf.Max(MinHeight, GetCurrentHeight());
            float cardWidth = canvasWidth * m.CardWidthPercent;
            float usableWidth = Mathf.Max(1f, cardWidth - (m.Padding * 2f));
            float iconSpace = string.IsNullOrEmpty(AdData.icon_url) ? 0f : m.IconSize + 16f;
            float preferredCtaWidth = Mathf.Clamp((ctaLength > 14 ? 224f : ctaLength > 9 ? 194f : 176f) * widthFactor, 138f, 250f);
            float maxCtaWidth = Mathf.Clamp(usableWidth * (textLength > 92 ? 0.22f : 0.25f), 130f, 250f);
            m.CtaWidth = hasCta ? Mathf.Round(Mathf.Min(preferredCtaWidth, maxCtaWidth)) : 0f;
            m.CtaHeight = m.IconSize;

            float ctaSpace = hasCta ? m.CtaWidth + m.Gap : 24f + m.Gap;
            float textWidth = Mathf.Max(120f, usableWidth - iconSpace - ctaSpace);
            m.TextWidth = textWidth;

            int maxTitleLines = titleLength > 118 ? 4 : 3;
            int maxBodyLines = bodyLength > 96 ? 3 : 2;
            int titleLines = EstimateWrappedLineCount(title, textWidth, m.HeadlineSize, maxTitleLines);
            int bodyLines = hasBody ? EstimateWrappedLineCount(body, textWidth, m.BodySize, maxBodyLines) : 0;

            if (bodyLength > 48 && bodyLines < 2)
                bodyLines = 2;

            m.HeadlineLines = Mathf.Max(1, titleLines);
            m.BodyLines = Mathf.Max(0, bodyLines);
            m.HeadlineHeight = Mathf.Ceil((m.HeadlineSize * 1.16f) * m.HeadlineLines);
            m.BodyHeight = hasBody ? Mathf.Ceil((m.BodySize * 1.16f) * Mathf.Max(1, bodyLines)) : 0f;
            m.BottomRelax = ResolveBottomSafeAreaRelax(m);

            float textExtra = 0f;
            if (textLength > 52) textExtra += 18f;
            if (textLength > 88) textExtra += 22f;
            if (textLength > 132) textExtra += 26f;
            if (hasCta && ctaLength > 12) textExtra += 12f;

            float textStackHeight = m.HeadlineHeight + (hasBody ? m.TextGap + m.BodyHeight : 0f);
            float requiredHeight = (m.Padding * 2f) + Mathf.Max(m.IconSize, m.CtaHeight, textStackHeight);
            m.Height = Mathf.Clamp(Mathf.Max(baseHeight + textExtra, requiredHeight), MinHeight, MaxHeight);

            return m;
        }

        private static float ResolveBottomSafeAreaRelax(LayoutMetrics metrics)
        {
            bool noWrappedCopy = metrics.HeadlineLines <= 1 && metrics.BodyLines <= 1;
            bool tallCopy = metrics.HeadlineLines >= 2 && metrics.BodyLines >= 2;
            int visibleLines = Mathf.Max(1, metrics.HeadlineLines) + Mathf.Max(0, metrics.BodyLines);
            float lineUnit = Mathf.Max(24f, ((metrics.HeadlineSize + Mathf.Max(metrics.BodySize, 1f)) * 0.5f) * 1.16f);
            if (noWrappedCopy)
                return BottomSafeAreaRelax + (lineUnit * 2.25f);

            if (tallCopy)
                return BottomSafeAreaRelax + (lineUnit * 0.9f);

            if (visibleLines <= 3)
                return BottomSafeAreaRelax + (lineUnit * 1.85f);

            return BottomSafeAreaRelax + (lineUnit * 1.1f);
        }

        private static int EstimateWrappedLineCount(string value, float width, float fontSize, int maxLines)
        {
            if (string.IsNullOrEmpty(value))
                return 1;

            float averageGlyphWidth = Mathf.Max(8f, fontSize * 0.53f);
            int visualCapacity = Mathf.Max(8, Mathf.FloorToInt(width / averageGlyphWidth));
            int visualLength = CountVisualLength(value);
            int lines = Mathf.CeilToInt(visualLength / (float)visualCapacity);
            return Mathf.Clamp(lines, 1, Mathf.Max(1, maxLines));
        }

        private static int CountVisualLength(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            int length = 0;
            for (int i = 0; i < value.Length; i++)
                length += value[i] > 127 ? 2 : 1;
            return length;
        }

        private static void ApplyTypography(Text text, string value, float maxSize, float minSize, FontStyle style, Color color, TextAnchor alignment, bool wrap, bool bestFit = true)
        {
            text.font = PreferredFont;
            text.text = NormalizeWrappedText(value, wrap);
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = bestFit;
            text.resizeTextMaxSize = Mathf.RoundToInt(maxSize);
            text.resizeTextMinSize = Mathf.RoundToInt(minSize);
            text.fontSize = Mathf.RoundToInt(maxSize);
            text.lineSpacing = wrap ? 0.98f : 1f;
            text.supportRichText = false;
        }

        private static string NormalizeWrappedText(string value, bool wrap)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            if (!wrap)
                return value;

            string normalized = value
                .Replace('\u00A0', ' ')
                .Replace('\t', ' ')
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();

            return InsertSoftBreaks(CollapseWhitespace(normalized));
        }

        private static string CollapseWhitespace(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var builder = new StringBuilder(value.Length);
            bool previousWasSpace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isSpace = char.IsWhiteSpace(c);
                if (isSpace)
                {
                    if (!previousWasSpace)
                        builder.Append(' ');
                    previousWasSpace = true;
                }
                else
                {
                    builder.Append(c);
                    previousWasSpace = false;
                }
            }

            return builder.ToString().Trim();
        }

        private static string InsertSoftBreaks(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var builder = new StringBuilder(value.Length + 8);
            int runLength = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                builder.Append(c);

                if (char.IsWhiteSpace(c) || c == '-' || c == '/' || c == '_' || c == '.')
                {
                    runLength = 0;
                    continue;
                }

                runLength += c > 127 ? 2 : 1;
                if (runLength >= 18)
                {
                    builder.Append('\u200B');
                    runLength = 0;
                }
            }

            return builder.ToString();
        }

        private void PositionContainer(LayoutMetrics layout)
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
                float bottomOffset = Mathf.Max(0f, (bottomInset / scaleFactor) - layout.BottomRelax);
                _containerRect.anchoredPosition = new Vector2(0, bottomOffset);
            }

            _containerRect.sizeDelta = new Vector2(0, layout.Height);
        }

        private void CreateShine(Transform parent, float height, LayoutMetrics layout)
        {
            var shineObj = new GameObject("NativeAdShine");
            shineObj.transform.SetParent(parent, false);
            _shineRect = shineObj.AddComponent<RectTransform>();
            _shineRect.anchorMin = new Vector2(0f, 0f);
            _shineRect.anchorMax = new Vector2(0f, 1f);
            _shineRect.pivot = new Vector2(0.5f, 0.5f);
            _shineRect.sizeDelta = new Vector2(Mathf.Max(70f, height * 0.55f), 0f);
            _shineRect.anchoredPosition = new Vector2(-260f, 0f);
            _shineRect.localRotation = Quaternion.Euler(0f, 0f, -18f);

            var shine = shineObj.AddComponent<Image>();
            shine.color = new Color(1f, 1f, 1f, 0.0f);
            shine.raycastTarget = false;
        }

        private void StartSlideIn()
        {
            if (_cardRect == null)
                return;

            if (!AdAnimationConfig.NativeBannerSlideEnabled)
            {
                _cardRestPosition = Vector2.zero;
                _cardRect.anchoredPosition = _cardRestPosition;
                StartAttentionLoops();
                return;
            }

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
            _cardRestPosition = target;
            _slideCoroutine = _canvas.StartCoroutine(SlideCard(start, target));
        }

        private IEnumerator SlideCard(Vector2 start, Vector2 target)
        {
            float elapsed = 0f;

            while (elapsed < SlideDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / SlideDuration);
                float eased = EaseOutBack(t);
                _cardRect.anchoredPosition = Vector2.LerpUnclamped(start, target, eased);
                yield return null;
            }

            _cardRect.anchoredPosition = target;
            _slideCoroutine = null;
            StartAttentionLoops();
        }

        private void StartAttentionLoops()
        {
            if (_canvas == null)
                return;

            if (_attentionCoroutine != null)
                _canvas.StopCoroutine(_attentionCoroutine);
            if (_shineCoroutine != null)
                _canvas.StopCoroutine(_shineCoroutine);

            if (AdAnimationConfig.NativeBannerAttentionEnabled)
                _attentionCoroutine = _canvas.StartCoroutine(AttentionCoroutine());
            if (AdAnimationConfig.NativeBannerShineEnabled)
                _shineCoroutine = _canvas.StartCoroutine(ShineCoroutine(0.7f));
        }

        private void StartVariantRotation(bool canRotate)
        {
            if (_canvas == null)
                return;

            if (_variantRotationCoroutine != null)
            {
                _canvas.StopCoroutine(_variantRotationCoroutine);
                _variantRotationCoroutine = null;
            }

            if (!canRotate || !AdAnimationConfig.NativeBannerVariantRotationEnabled)
                return;

            _variantRotationCoroutine = _canvas.StartCoroutine(VariantRotationCoroutine());
        }

        private IEnumerator VariantRotationCoroutine()
        {
            yield return new WaitForSecondsRealtime(VariantRotationIntervalSeconds);

            while (IsVisible && _container != null)
            {
                ToggleBannerVariant();
                yield return new WaitForSecondsRealtime(VariantRotationIntervalSeconds);
            }

            _variantRotationCoroutine = null;
        }

        private void ToggleBannerVariant()
        {
            if (!_canRotateVariant || _canvas == null || _container == null)
                return;

            _bannerVariant = _bannerVariant == BannerVariant.IconLeading
                ? BannerVariant.CtaLeading
                : BannerVariant.IconLeading;

            StopAnimations(false);

            _container.SetActive(false);
            UnityEngine.Object.Destroy(_container);
            _container = null;
            _containerRect = null;
            _cardRect = null;
            _iconRect = null;
            _ctaRect = null;
            _shineRect = null;

            CreateLayout(false, false);
            Logger.Log("Native-баннер: вариант обновлен, rotationSec={0}, variant={1}", Mathf.RoundToInt(VariantRotationIntervalSeconds), _bannerVariant);
        }

        private IEnumerator AttentionCoroutine()
        {
            yield return new WaitForSecondsRealtime(1.2f);

            while (IsVisible && _cardRect != null)
            {
                yield return PulseRect(_iconRect, 1.05f, 0.32f);
                yield return WiggleRect(_ctaRect, 0.44f, 4.5f, 6f);
                yield return ShakeCard(0.30f, 5f);
                if (AdAnimationConfig.NativeBannerShineEnabled)
                    yield return ShineCoroutine(0.62f);
                yield return new WaitForSecondsRealtime(AttentionIntervalSeconds);
            }

            _attentionCoroutine = null;
        }

        private IEnumerator ShineCoroutine(float duration)
        {
            if (_shineRect == null)
                yield break;

            var image = _shineRect.GetComponent<Image>();
            float elapsed = 0f;
            float startX = -260f;
            float endX = 1280f;

            while (elapsed < duration && _shineRect != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float ease = 1f - Mathf.Pow(1f - t, 2.8f);
                _shineRect.anchoredPosition = new Vector2(Mathf.Lerp(startX, endX, ease), 0f);
                if (image != null)
                {
                    float alpha = Mathf.Sin(Mathf.PI * t) * 0.22f;
                    image.color = new Color(1f, 0.95f, 0.66f, alpha);
                }
                yield return null;
            }

            if (image != null)
                image.color = new Color(1f, 1f, 1f, 0f);
            if (_shineRect != null)
                _shineRect.anchoredPosition = new Vector2(startX, 0f);
            _shineCoroutine = null;
        }

        private IEnumerator PulseRect(RectTransform rect, float maxScale, float duration)
        {
            if (rect == null)
                yield break;

            float elapsed = 0f;
            while (elapsed < duration && rect != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float pulse = Mathf.Sin(Mathf.PI * t);
                float scale = Mathf.Lerp(1f, maxScale, pulse);
                rect.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }

            if (rect != null)
                rect.localScale = Vector3.one;
        }

        private IEnumerator WiggleRect(RectTransform rect, float duration, float maxAngle, float xAmplitude)
        {
            if (rect == null)
                yield break;

            Vector2 basePosition = rect.anchoredPosition;
            Quaternion baseRotation = rect.localRotation;
            float elapsed = 0f;

            while (elapsed < duration && rect != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float wave = Mathf.Sin(t * Mathf.PI * 6f);
                float damp = 1f - t;
                float scalePulse = Mathf.Sin(Mathf.PI * t);
                rect.anchoredPosition = basePosition + new Vector2(wave * xAmplitude * damp, 0f);
                rect.localRotation = Quaternion.Euler(0f, 0f, wave * maxAngle * damp);
                float scale = Mathf.Lerp(1f, 1.08f, scalePulse);
                rect.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }

            if (rect != null)
            {
                rect.anchoredPosition = basePosition;
                rect.localRotation = baseRotation;
                rect.localScale = Vector3.one;
            }
        }

        private IEnumerator ShakeCard(float duration, float amplitude)
        {
            if (_cardRect == null)
                yield break;

            float elapsed = 0f;
            while (elapsed < duration && _cardRect != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float damp = 1f - t;
                float x = Mathf.Sin(t * Mathf.PI * 8f) * amplitude * damp;
                _cardRect.anchoredPosition = _cardRestPosition + new Vector2(x, 0f);
                yield return null;
            }

            if (_cardRect != null)
                _cardRect.anchoredPosition = _cardRestPosition;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
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
            StopAnimations();
            AdMediator.HideBanner();

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
                AdMediator.OnZeyWinBannerShown();
                TrackImpression();
                StartSlideIn();
                StartVariantRotation(_canRotateVariant);
            }
        }

        public void SetPosition(BannerPosition newPosition)
        {
            if (Position == newPosition)
                return;

            Position = newPosition;

            if (_container != null && _canvas != null)
            {
                StopAnimations();

                UnityEngine.Object.Destroy(_container);
                _container = null;
                _containerRect = null;
                _cardRect = null;
                _iconRect = null;
                _ctaRect = null;
                _shineRect = null;
                CreateLayout();
            }
        }

        public override void Destroy()
        {
            StopAnimations();

            if (_canvas != null)
            {
                _canvas.Destroy();
                _canvas = null;
            }

            _container = null;
            _containerRect = null;
            _cardRect = null;
            _iconRect = null;
            _ctaRect = null;
            _shineRect = null;
            IsVisible = false;
            AdMediator.HideBanner();

            base.Destroy();
        }

        private void StopAnimations(bool stopVariantRotation = true)
        {
            if (_canvas == null)
                return;

            if (_slideCoroutine != null)
            {
                _canvas.StopCoroutine(_slideCoroutine);
                _slideCoroutine = null;
            }
            if (_attentionCoroutine != null)
            {
                _canvas.StopCoroutine(_attentionCoroutine);
                _attentionCoroutine = null;
            }
            if (_shineCoroutine != null)
            {
                _canvas.StopCoroutine(_shineCoroutine);
                _shineCoroutine = null;
            }
            if (stopVariantRotation && _variantRotationCoroutine != null)
            {
                _canvas.StopCoroutine(_variantRotationCoroutine);
                _variantRotationCoroutine = null;
            }
            if (_cardRect != null)
                _cardRect.anchoredPosition = _cardRestPosition;
            if (_iconRect != null)
                _iconRect.localScale = Vector3.one;
            if (_ctaRect != null)
                _ctaRect.localScale = Vector3.one;
        }

        protected override void OnDestroy()
        {
        }

        protected override void OnClose()
        {
            IsVisible = false;
            AdMediator.HideBanner();
            base.OnClose();
        }
    }
}
