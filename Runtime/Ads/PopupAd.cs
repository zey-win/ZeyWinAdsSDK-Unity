using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using ZeyWinAds.Core;
using ZeyWinAds.Mediation;
using ZeyWinAds.UI;
using Logger = ZeyWinAds.Core.Logger;

namespace ZeyWinAds.Ads
{
    /// <summary>
    /// Popup ad — bottom sheet style card.
    /// Can be used data-only via GetPopupAdInfo() or rendered via ShowPopup().
    /// </summary>
    public class PopupAd : BaseAd
    {
        public override AdType AdType => AdType.Popup;

        private AdCanvas _canvas;
        private GameObject _overlay;
        private GameObject _card;
        private GameObject _goldFlash;
        private bool _isVisible;
        private bool _zeyWinSurfaceActive;
        private Coroutine _animCoroutine;

        // Colors matching the screenshot
        private static readonly Color CardBg = new Color(0.93f, 0.95f, 0.96f, 1f);       // light gray-blue
        private static readonly Color TitleColor = new Color(0.06f, 0.16f, 0.33f, 1f);    // dark navy
        private static readonly Color SubtitleColor = new Color(0.30f, 0.35f, 0.42f, 1f); // gray
        private static readonly Color Btn1Bg = new Color(0.78f, 0.84f, 0.89f, 1f);        // light blue-gray
        private static readonly Color Btn1Text = new Color(0.25f, 0.35f, 0.45f, 1f);      // dark blue-gray
        private static readonly Color Btn2Bg = new Color(0.47f, 0.72f, 0.10f, 1f);        // green
        private static readonly Color CloseColor = new Color(0.50f, 0.55f, 0.60f, 1f);    // gray X

        // Rounded-rect sprite cache
        private static Sprite _roundedCardSprite;
        private static Sprite _roundedBtnSprite;
        private static Font _preferredFont;

        private static Font PreferredFont
        {
            get
            {
                if (_preferredFont != null)
                    return _preferredFont;

                try
                {
                    _preferredFont = Font.CreateDynamicFontFromOSFont(
                        new[] { "Roboto", "Noto Sans", "NotoSans", "Helvetica", "Arial", "Droid Sans", "sans-serif" },
                        34);
                }
                catch
                {
                    _preferredFont = null;
                }

                if (_preferredFont == null)
                    _preferredFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                return _preferredFont;
            }
        }

        private static Sprite GetRoundedSprite(int width, int height, int radius)
        {
            var tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            var pixels = new Color32[width * height];
            var clear = new Color32(0, 0, 0, 0);
            var white = new Color32(255, 255, 255, 255);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // determine closest corner center
                    int cx = x < radius ? radius : (x >= width - radius ? width - radius - 1 : x);
                    int cy = y < radius ? radius : (y >= height - radius ? height - radius - 1 : y);

                    if (cx != x || cy != y)
                    {
                        float dist = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        pixels[y * width + x] = dist <= radius + 0.5f ? white : clear;
                    }
                    else
                    {
                        pixels[y * width + x] = white;
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            int border = radius;
            return Sprite.Create(tex,
                new Rect(0, 0, width, height),
                new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
        }

        private static Sprite CardSprite
        {
            get
            {
                if (_roundedCardSprite == null)
                    _roundedCardSprite = GetRoundedSprite(64, 64, 20);
                return _roundedCardSprite;
            }
        }

        private static Sprite BtnSprite
        {
            get
            {
                if (_roundedBtnSprite == null)
                    _roundedBtnSprite = GetRoundedSprite(64, 64, 14);
                return _roundedBtnSprite;
            }
        }

        public void ShowPopup(Action onClose = null, Action<string> onButton1 = null, Action<string> onButton2 = null)
        {
            _onCloseCallback = onClose;
            _onButton1Callback = onButton1;
            _onButton2Callback = onButton2;
            base.Show(onClose);
        }

        private Action<string> _onButton1Callback;
        private Action<string> _onButton2Callback;

        protected override void OnShow()
        {
            Logger.Debug("Showing popup ad");
            _isVisible = true;
            BeginZeyWinSurface();

            _canvas = AdCanvas.Create("PopupAdCanvas");
            _canvas.SetSortingOrder(32760);

            CreateOverlay();
            CreateCard();
            CreateGoldFlash();
            AnimateIn();
        }

        private void CreateOverlay()
        {
            _overlay = new GameObject("Overlay");
            _overlay.transform.SetParent(_canvas.transform, false);

            var rect = _overlay.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;

            var img = _overlay.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.4f);

            var btn = _overlay.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(0, 0, 0, 0.4f);
            colors.pressedColor = new Color(0, 0, 0, 0.4f);
            btn.colors = colors;
            btn.onClick.AddListener(Close);
        }

        private void CreateCard()
        {
            float cardMarginH = 36f;   // horizontal margin from screen edges
            float cardMarginB = 30f;   // bottom margin — makes the card "float"
            float cardWidth = 1080f - cardMarginH * 2; // full width minus margins
            float padding = 40f;
            float btnHeight = 100f;
            float btnGap = 20f;

            bool hasImage = !string.IsNullOrEmpty(AdData.media_url);
            bool hasSubtitle = !string.IsNullOrEmpty(AdData.ad_body);
            bool hasBtn2 = !string.IsNullOrEmpty(AdData.cta_text_2);

            // Calculate card height
            float imageHeight = hasImage ? 400f : 0f;
            float titleHeight = 60f;
            float subtitleHeight = hasSubtitle ? 50f : 0f;
            float gapAfterText = 30f;
            float totalHeight = padding + imageHeight + (hasImage ? 20f : 0f)
                + titleHeight + (hasSubtitle ? 10f : 0f) + subtitleHeight
                + gapAfterText + btnHeight + padding;

            // Card container — anchored to bottom center
            _card = new GameObject("PopupCard");
            _card.transform.SetParent(_canvas.transform, false);

            var cardRect = _card.AddComponent<RectTransform>();
            // Stretch horizontally, anchored to bottom
            cardRect.anchorMin = new Vector2(0, 0);
            cardRect.anchorMax = new Vector2(1, 0);
            cardRect.pivot = new Vector2(0.5f, 0);

            // Account for safe area + bottom margin
            float bottomInset = Screen.safeArea.y / _canvas.GetScaleFactor();
            cardRect.anchoredPosition = new Vector2(0, bottomInset + cardMarginB);
            // offsetMin.x = left margin, offsetMax.x = -right margin
            cardRect.offsetMin = new Vector2(cardMarginH, cardRect.offsetMin.y);
            cardRect.offsetMax = new Vector2(-cardMarginH, cardRect.offsetMax.y);
            cardRect.sizeDelta = new Vector2(cardRect.sizeDelta.x, totalHeight);

            // Card background with rounded corners
            var cardBg = _card.AddComponent<Image>();
            cardBg.color = CardBg;
            cardBg.sprite = CardSprite;
            cardBg.type = Image.Type.Sliced;
            cardBg.pixelsPerUnitMultiplier = 1f;

            float yOffset = -padding;

            // === Optional image ===
            if (hasImage)
            {
                var imgContainer = new GameObject("PopupImage");
                imgContainer.transform.SetParent(_card.transform, false);

                var imgRect = imgContainer.AddComponent<RectTransform>();
                imgRect.anchorMin = new Vector2(0, 1);
                imgRect.anchorMax = new Vector2(1, 1);
                imgRect.pivot = new Vector2(0.5f, 1);
                imgRect.anchoredPosition = new Vector2(0, yOffset);
                imgRect.sizeDelta = new Vector2(-padding * 2, imageHeight);
                imgRect.offsetMin = new Vector2(padding, imgRect.offsetMin.y);
                imgRect.offsetMax = new Vector2(-padding, imgRect.offsetMax.y);

                var mask = imgContainer.AddComponent<RectMask2D>();

                var imgObj = new GameObject("Image");
                imgObj.transform.SetParent(imgContainer.transform, false);
                var rawImgRect = imgObj.AddComponent<RectTransform>();
                rawImgRect.anchorMin = Vector2.zero;
                rawImgRect.anchorMax = Vector2.one;
                rawImgRect.sizeDelta = Vector2.zero;

                var rawImg = imgObj.AddComponent<RawImage>();
                rawImg.color = new Color(0.85f, 0.87f, 0.90f, 1f);

                _canvas.LoadImage(AdData.media_url, (tex) =>
                {
                    if (tex != null && rawImg != null)
                    {
                        rawImg.texture = tex;
                        rawImg.color = Color.white;
                    }
                });

                yOffset -= imageHeight + 20f;
            }

            // === Close button (X) — top right ===
            var closeObj = new GameObject("CloseBtn");
            closeObj.transform.SetParent(_card.transform, false);

            var closeRect = closeObj.AddComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1, 1);
            closeRect.anchorMax = new Vector2(1, 1);
            closeRect.pivot = new Vector2(1, 1);
            closeRect.anchoredPosition = new Vector2(-padding * 0.5f, -padding * 0.5f);
            closeRect.sizeDelta = new Vector2(70f, 70f);

            var closeBtnImg = closeObj.AddComponent<Image>();
            closeBtnImg.color = Color.clear;

            var closeBtn = closeObj.AddComponent<Button>();
            closeBtn.targetGraphic = closeBtnImg;
            closeBtn.onClick.AddListener(Close);

            var closeTextObj = new GameObject("X");
            closeTextObj.transform.SetParent(closeObj.transform, false);
            var closeTextRect = closeTextObj.AddComponent<RectTransform>();
            closeTextRect.anchorMin = Vector2.zero;
            closeTextRect.anchorMax = Vector2.one;
            closeTextRect.sizeDelta = Vector2.zero;

            var closeText = closeTextObj.AddComponent<Text>();
            ApplyTypography(closeText, "\u00D7", 44, 34, FontStyle.Bold, CloseColor, TextAnchor.MiddleCenter, false);

            // === Title ===
            var titleObj = new GameObject("Title");
            titleObj.transform.SetParent(_card.transform, false);

            var titleRect = titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.pivot = new Vector2(0, 1);
            titleRect.anchoredPosition = new Vector2(padding, yOffset);
            titleRect.sizeDelta = new Vector2(-(padding * 2 + 70f), titleHeight);
            titleRect.offsetMin = new Vector2(padding, titleRect.offsetMin.y);
            titleRect.offsetMax = new Vector2(-padding - 70f, titleRect.offsetMax.y);

            var titleText = titleObj.AddComponent<Text>();
            ApplyTypography(titleText, AdData.ad_text ?? "", 42, 28, FontStyle.Bold, TitleColor, TextAnchor.MiddleLeft, true);

            yOffset -= titleHeight;

            // === Subtitle ===
            if (hasSubtitle)
            {
                yOffset -= 10f;

                var subObj = new GameObject("Subtitle");
                subObj.transform.SetParent(_card.transform, false);

                var subRect = subObj.AddComponent<RectTransform>();
                subRect.anchorMin = new Vector2(0, 1);
                subRect.anchorMax = new Vector2(1, 1);
                subRect.pivot = new Vector2(0, 1);
                subRect.anchoredPosition = new Vector2(padding, yOffset);
                subRect.sizeDelta = new Vector2(0, subtitleHeight);
                subRect.offsetMin = new Vector2(padding, subRect.offsetMin.y);
                subRect.offsetMax = new Vector2(-padding, subRect.offsetMax.y);

                var subText = subObj.AddComponent<Text>();
                ApplyTypography(subText, AdData.ad_body, 30, 20, FontStyle.Normal, SubtitleColor, TextAnchor.UpperLeft, true);

                yOffset -= subtitleHeight;
            }

            yOffset -= gapAfterText;

            // === Buttons row ===
            if (hasBtn2)
            {
                // Button 1 (left — light gray-blue): left half
                CreateButtonStretch(_card.transform,
                    padding, btnGap / 2f, yOffset, btnHeight,
                    0f, 0.5f,
                    AdData.cta_text ?? "Button 1", 30,
                    Btn1Bg, Btn1Text,
                    OnButton1Clicked);

                // Button 2 (right — green): right half
                CreateButtonStretch(_card.transform,
                    btnGap / 2f, padding, yOffset, btnHeight,
                    0.5f, 1f,
                    AdData.cta_text_2, 30,
                    Btn2Bg, Color.white,
                    OnButton2Clicked);
            }
            else
            {
                // Single button full width
                CreateButtonStretch(_card.transform,
                    padding, padding, yOffset, btnHeight,
                    0f, 1f,
                    AdData.cta_text ?? "OK", 30,
                    Btn2Bg, Color.white,
                    OnButton1Clicked);
            }

            Logger.Debug("Popup card layout created");
        }

        private void CreateGoldFlash()
        {
            if (_card == null)
                return;

            _goldFlash = new GameObject("GoldFlash");
            _goldFlash.transform.SetParent(_card.transform, false);
            _goldFlash.transform.SetSiblingIndex(0);

            var flashRect = _goldFlash.AddComponent<RectTransform>();
            flashRect.anchorMin = Vector2.zero;
            flashRect.anchorMax = Vector2.one;
            flashRect.offsetMin = new Vector2(-48f, -34f);
            flashRect.offsetMax = new Vector2(48f, 34f);
            flashRect.localScale = new Vector3(0.82f, 0.82f, 1f);

            var flashImage = _goldFlash.AddComponent<Image>();
            flashImage.sprite = CardSprite;
            flashImage.type = Image.Type.Sliced;
            flashImage.pixelsPerUnitMultiplier = 1f;
            flashImage.color = new Color(1f, 0.74f, 0.12f, 0f);
            flashImage.raycastTarget = false;

            var glowObj = new GameObject("GoldCore");
            glowObj.transform.SetParent(_goldFlash.transform, false);
            var glowRect = glowObj.AddComponent<RectTransform>();
            glowRect.anchorMin = new Vector2(0.08f, 0.10f);
            glowRect.anchorMax = new Vector2(0.92f, 0.90f);
            glowRect.offsetMin = Vector2.zero;
            glowRect.offsetMax = Vector2.zero;

            var glowImage = glowObj.AddComponent<Image>();
            glowImage.sprite = CardSprite;
            glowImage.type = Image.Type.Sliced;
            glowImage.pixelsPerUnitMultiplier = 1f;
            glowImage.color = new Color(1f, 0.95f, 0.48f, 0f);
            glowImage.raycastTarget = false;
        }

        private void CreateButtonStretch(Transform parent,
            float leftInset, float rightInset, float y, float h,
            float anchorXMin, float anchorXMax,
            string label, int fontSize, Color bgColor, Color textColor, Action onClick)
        {
            var btnObj = new GameObject("Btn_" + label);
            btnObj.transform.SetParent(parent, false);

            var btnRect = btnObj.AddComponent<RectTransform>();
            // Stretch horizontally between anchorXMin..anchorXMax, pinned to top of parent
            btnRect.anchorMin = new Vector2(anchorXMin, 1);
            btnRect.anchorMax = new Vector2(anchorXMax, 1);
            // offsetMin = lower-left relative to lower-left anchor
            // offsetMax = upper-right relative to upper-right anchor
            // y is negative (offset from parent top), h is positive
            btnRect.offsetMin = new Vector2(leftInset, y - h);
            btnRect.offsetMax = new Vector2(-rightInset, y);

            var btnImg = btnObj.AddComponent<Image>();
            btnImg.color = bgColor;
            btnImg.sprite = BtnSprite;
            btnImg.type = Image.Type.Sliced;
            btnImg.pixelsPerUnitMultiplier = 1f;

            var button = btnObj.AddComponent<Button>();
            button.targetGraphic = btnImg;
            var colors = button.colors;
            colors.highlightedColor = bgColor * 0.9f;
            colors.pressedColor = bgColor * 0.8f;
            colors.highlightedColor = new Color(colors.highlightedColor.r, colors.highlightedColor.g, colors.highlightedColor.b, 1f);
            colors.pressedColor = new Color(colors.pressedColor.r, colors.pressedColor.g, colors.pressedColor.b, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick?.Invoke());

            var textObj = new GameObject("Label");
            textObj.transform.SetParent(btnObj.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            var text = textObj.AddComponent<Text>();
            ApplyTypography(text, label, fontSize, Mathf.Max(18, fontSize - 10), FontStyle.Bold, textColor, TextAnchor.MiddleCenter, true);
        }

        private static void ApplyTypography(Text text, string value, int maxSize, int minSize, FontStyle style, Color color, TextAnchor alignment, bool wrap)
        {
            text.font = PreferredFont;
            text.text = value ?? "";
            text.fontSize = maxSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMaxSize = maxSize;
            text.resizeTextMinSize = minSize;
            text.lineSpacing = wrap ? 0.9f : 1f;
            text.supportRichText = false;
        }

        private void OnButton1Clicked()
        {
            Logger.Debug("Popup button 1 clicked");
            string url = AdData?.click_url;
            OpenClickUrl();
            _onButton1Callback?.Invoke(url);
            Close();
        }

        private void OnButton2Clicked()
        {
            Logger.Debug("Popup button 2 clicked");
            _onButton2Callback?.Invoke(null);
            Close();
        }

        private void Close()
        {
            if (!_isVisible) return;
            _isVisible = false;

            AnimateOut(() =>
            {
                OnClose();
                Destroy();
            });
        }

        private void AnimateIn()
        {
            if (_card == null) return;

            var cardRect = _card.GetComponent<RectTransform>();
            float targetBottom = cardRect.offsetMin.y;  // final bottom offset
            float targetTop = cardRect.offsetMax.y;
            cardRect.offsetMin = new Vector2(cardRect.offsetMin.x, targetBottom);
            cardRect.offsetMax = new Vector2(cardRect.offsetMax.x, targetTop);
            cardRect.localScale = new Vector3(0.88f, 0.88f, 1f);

            // Fade overlay from transparent
            var overlayImg = _overlay?.GetComponent<Image>();
            if (overlayImg != null) overlayImg.color = new Color(0, 0, 0, 0);

            var flashRect = _goldFlash != null ? _goldFlash.GetComponent<RectTransform>() : null;
            var flashImg = _goldFlash != null ? _goldFlash.GetComponent<Image>() : null;
            Image glowImg = null;
            if (_goldFlash != null && _goldFlash.transform.childCount > 0)
                glowImg = _goldFlash.transform.GetChild(0).GetComponent<Image>();

            _animCoroutine = _canvas.StartCoroutine(AnimateCoroutine(0.48f, (t) =>
            {
                float ease = EaseOutBack(t);
                if (cardRect != null)
                {
                    cardRect.offsetMin = new Vector2(cardRect.offsetMin.x, targetBottom);
                    cardRect.offsetMax = new Vector2(cardRect.offsetMax.x, targetTop);
                    float scale = Mathf.LerpUnclamped(0.88f, 1f, ease);
                    cardRect.localScale = new Vector3(scale, scale, 1f);
                }
                if (overlayImg != null)
                    overlayImg.color = new Color(0, 0, 0, 0.4f * ease);

                float flash = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t));
                if (flashRect != null)
                {
                    float flashScale = Mathf.Lerp(0.82f, 1.18f, Mathf.Clamp01(t));
                    flashRect.localScale = new Vector3(flashScale, flashScale, 1f);
                }
                if (flashImg != null)
                    flashImg.color = new Color(1f, 0.69f, 0.08f, 0.58f * flash);
                if (glowImg != null)
                    glowImg.color = new Color(1f, 0.96f, 0.42f, 0.44f * flash);
            }, () =>
            {
                if (cardRect != null)
                    cardRect.localScale = Vector3.one;
                if (flashImg != null)
                    flashImg.color = new Color(1f, 0.74f, 0.12f, 0f);
                if (glowImg != null)
                    glowImg.color = new Color(1f, 0.95f, 0.48f, 0f);
            }));
        }

        private void AnimateOut(Action onComplete)
        {
            if (_card == null)
            {
                onComplete?.Invoke();
                return;
            }

            var cardRect = _card.GetComponent<RectTransform>();
            float startBottom = cardRect.offsetMin.y;
            float startTop = cardRect.offsetMax.y;
            float cardH = cardRect.sizeDelta.y;
            float endBottom = -cardH;
            float shift = endBottom - startBottom;
            float endTop = startTop + shift;

            var overlayImg = _overlay?.GetComponent<Image>();

            _animCoroutine = _canvas.StartCoroutine(AnimateCoroutine(0.2f, (t) =>
            {
                float ease = t * t; // ease in quad
                if (cardRect != null)
                {
                    float curBottom = Mathf.Lerp(startBottom, endBottom, ease);
                    float curTop = Mathf.Lerp(startTop, endTop, ease);
                    cardRect.offsetMin = new Vector2(cardRect.offsetMin.x, curBottom);
                    cardRect.offsetMax = new Vector2(cardRect.offsetMax.x, curTop);
                }
                if (overlayImg != null)
                    overlayImg.color = new Color(0, 0, 0, 0.4f * (1f - ease));
            }, onComplete));
        }

        private IEnumerator AnimateCoroutine(float duration, Action<float> onUpdate, Action onComplete = null)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                onUpdate?.Invoke(t);
                yield return null;
            }
            onUpdate?.Invoke(1f);
            onComplete?.Invoke();
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        public override void Destroy()
        {
            EndZeyWinSurface();

            if (_canvas != null)
            {
                if (_animCoroutine != null)
                    _canvas.StopCoroutine(_animCoroutine);
                _canvas.Destroy();
                _canvas = null;
            }

            _overlay = null;
            _card = null;
            _goldFlash = null;
            _isVisible = false;
            _onButton1Callback = null;
            _onButton2Callback = null;

            base.Destroy();
        }

        protected override void OnDestroy() { }

        private void BeginZeyWinSurface()
        {
            if (_zeyWinSurfaceActive)
                return;

            _zeyWinSurfaceActive = true;
            AdMediator.BeginZeyWinSurface("popup");
        }

        private void EndZeyWinSurface()
        {
            if (!_zeyWinSurfaceActive)
                return;

            _zeyWinSurfaceActive = false;
            AdMediator.EndZeyWinSurface("popup");
        }
    }
}
