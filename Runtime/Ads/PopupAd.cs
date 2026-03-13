using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using ZeyWinAds.Core;
using ZeyWinAds.UI;

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
        private bool _isVisible;
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
            Debug.Log($"[ZeyWinAds] Showing popup ad: {AdData.ad_id}");
            _isVisible = true;

            _canvas = AdCanvas.Create("PopupAdCanvas");
            _canvas.SetSortingOrder(999);

            CreateOverlay();
            CreateCard();
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
            closeText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            closeText.text = "\u00D7";
            closeText.fontSize = 44;
            closeText.color = CloseColor;
            closeText.alignment = TextAnchor.MiddleCenter;

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
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            titleText.text = AdData.ad_text ?? "";
            titleText.fontSize = 42;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = TitleColor;
            titleText.alignment = TextAnchor.MiddleLeft;
            titleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            titleText.verticalOverflow = VerticalWrapMode.Truncate;

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
                subText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                subText.text = AdData.ad_body;
                subText.fontSize = 30;
                subText.color = SubtitleColor;
                subText.alignment = TextAnchor.UpperLeft;
                subText.horizontalOverflow = HorizontalWrapMode.Wrap;
                subText.verticalOverflow = VerticalWrapMode.Truncate;

                yOffset -= subtitleHeight;
            }

            yOffset -= gapAfterText;

            // === Buttons row ===
            float buttonsWidth = cardWidth - padding * 2;

            if (hasBtn2)
            {
                float singleBtnWidth = (buttonsWidth - btnGap) / 2f;

                // Button 1 (left — light gray-blue)
                CreateButton(_card.transform,
                    padding, yOffset, singleBtnWidth, btnHeight,
                    AdData.cta_text ?? "Button 1", 30,
                    Btn1Bg, Btn1Text,
                    OnButton1Clicked);

                // Button 2 (right — green)
                CreateButton(_card.transform,
                    padding + singleBtnWidth + btnGap, yOffset, singleBtnWidth, btnHeight,
                    AdData.cta_text_2, 30,
                    Btn2Bg, Color.white,
                    OnButton2Clicked);
            }
            else
            {
                // Single button full width
                CreateButton(_card.transform,
                    padding, yOffset, buttonsWidth, btnHeight,
                    AdData.cta_text ?? "OK", 30,
                    Btn2Bg, Color.white,
                    OnButton1Clicked);
            }

            Debug.Log("[ZeyWinAds] Popup card layout created");
        }

        private void CreateButton(Transform parent, float x, float y, float w, float h,
            string label, int fontSize, Color bgColor, Color textColor, Action onClick)
        {
            var btnObj = new GameObject("Btn_" + label);
            btnObj.transform.SetParent(parent, false);

            var btnRect = btnObj.AddComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0, 1);
            btnRect.anchorMax = new Vector2(0, 1);
            btnRect.pivot = new Vector2(0, 1);
            btnRect.anchoredPosition = new Vector2(x, y);
            btnRect.sizeDelta = new Vector2(w, h);

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
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = label;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.color = textColor;
            text.alignment = TextAnchor.MiddleCenter;
        }

        private void OnButton1Clicked()
        {
            Debug.Log("[ZeyWinAds] Popup button 1 clicked");
            string url = AdData?.click_url;
            OpenClickUrl();
            _onButton1Callback?.Invoke(url);
        }

        private void OnButton2Clicked()
        {
            Debug.Log("[ZeyWinAds] Popup button 2 clicked");
            string url = AdData?.click_url;
            OpenClickUrl();
            _onButton2Callback?.Invoke(url);
        }

        private void Close()
        {
            if (!_isVisible) return;
            _isVisible = false;

            AnimateOut(() =>
            {
                OnClose();
            });
        }

        private void AnimateIn()
        {
            if (_card == null) return;

            var cardRect = _card.GetComponent<RectTransform>();
            float targetBottom = cardRect.offsetMin.y;  // final bottom offset
            float cardH = cardRect.sizeDelta.y;
            float offScreenBottom = -cardH;             // start below screen

            // Start off-screen — shift both offsets equally
            float shift = offScreenBottom - targetBottom;
            cardRect.offsetMin = new Vector2(cardRect.offsetMin.x, offScreenBottom);
            cardRect.offsetMax = new Vector2(cardRect.offsetMax.x, cardRect.offsetMax.y + shift);

            // Fade overlay from transparent
            var overlayImg = _overlay?.GetComponent<Image>();
            if (overlayImg != null) overlayImg.color = new Color(0, 0, 0, 0);

            float startOffMin = cardRect.offsetMin.y;
            float startOffMax = cardRect.offsetMax.y;
            float targetTop = startOffMax - shift; // restore original top

            _animCoroutine = _canvas.StartCoroutine(AnimateCoroutine(0.3f, (t) =>
            {
                float ease = 1f - Mathf.Pow(1f - t, 3f); // ease out cubic
                if (cardRect != null)
                {
                    float curBottom = Mathf.Lerp(startOffMin, targetBottom, ease);
                    float curTop = Mathf.Lerp(startOffMax, targetTop, ease);
                    cardRect.offsetMin = new Vector2(cardRect.offsetMin.x, curBottom);
                    cardRect.offsetMax = new Vector2(cardRect.offsetMax.x, curTop);
                }
                if (overlayImg != null)
                    overlayImg.color = new Color(0, 0, 0, 0.4f * ease);
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

        public override void Destroy()
        {
            if (_canvas != null)
            {
                if (_animCoroutine != null)
                    _canvas.StopCoroutine(_animCoroutine);
                _canvas.Destroy();
                _canvas = null;
            }

            _overlay = null;
            _card = null;
            _isVisible = false;
            _onButton1Callback = null;
            _onButton2Callback = null;

            base.Destroy();
        }

        protected override void OnDestroy() { }
    }
}
