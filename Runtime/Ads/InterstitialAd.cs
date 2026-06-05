using System;
using UnityEngine;
using UnityEngine.UI;
using ZeyWinAds.Core;
using ZeyWinAds.UI;
using Logger = ZeyWinAds.Core.Logger;

namespace ZeyWinAds.Ads
{
    /// <summary>
    /// Fullscreen interstitial ad that displays an image or video.
    /// Close button appears after a delay (3 seconds for images, after video ends).
    /// </summary>
    public class InterstitialAd : BaseAd
    {
        public override AdType AdType => AdType.Interstitial;

        /// <summary>
        /// Delay before close button appears for image ads (in seconds)
        /// </summary>
        public float ImageCloseDelay { get; set; } = 3f;

        // UI references
        private AdCanvas _canvas;
        private GameObject _adContainer;
        private CloseButton _closeButton;
        private AdVideoPlayer _videoPlayer;
        private HtmlAdView _htmlAdView;

        // State
        private bool _canClose;
        private float _showTime;
        private float _skipAfterSeconds;
        private bool _audioMuted;

        /// <summary>
        /// Creates a new interstitial ad instance
        /// </summary>
        public InterstitialAd()
        {
        }

        protected override void OnShow()
        {
            Logger.Debug("Showing interstitial ad");

            _canClose = false;
            _showTime = Time.realtimeSinceStartup;

            // HTML ads use native WebView — no Unity canvas needed
            if (AdData.GetMediaType() == MediaType.Html)
            {
                ShowHtmlAd();
                return;
            }

            // Create ad canvas for image/video
            _canvas = AdCanvas.Create("InterstitialAdCanvas");
            _canvas.SetSortingOrder(1000);

            // Create container for ad content
            _adContainer = _canvas.CreateFullscreenContainer("InterstitialContainer");

            // Add click handler to container
            var clickHandler = _adContainer.AddComponent<UnityEngine.UI.Button>();
            clickHandler.onClick.AddListener(OnAdClicked);

            // Create close button first (video ads need it for download progress)
            _closeButton = _canvas.CreateCloseButton(OnCloseButtonClicked);
            _closeButton.gameObject.SetActive(false);

            // Display based on media type
            if (AdData.GetMediaType() == MediaType.Video)
            {
                ShowVideoAd();
            }
            else if (AdData.GetMediaType() == MediaType.Native)
            {
                ShowNativeAd();
                _closeButton.gameObject.SetActive(true);
                _closeButton.StartTimer(ImageCloseDelay, () =>
                {
                    _canClose = true;
                });
            }
            else
            {
                // ShowImageAd handles visibility and close timer after image loads
                ShowImageAd();
            }
        }

        private void ShowImageAd()
        {
            Logger.Debug("Loading interstitial image");

            // Hide ad until image is loaded
            _adContainer.SetActive(false);
            _closeButton.gameObject.SetActive(false);

            // Create image display with callback to show UI once ready
            var imageDisplay = _canvas.CreateImageDisplay(_adContainer.transform, AdData.media_url, onLoaded: () =>
            {
                if (_adContainer != null)
                    _adContainer.SetActive(true);

                // Show close button with timer after image is visible
                if (_closeButton != null)
                {
                    _closeButton.gameObject.SetActive(true);
                    _closeButton.StartTimer(ImageCloseDelay, () => { _canClose = true; });
                }

                // Image is now on-screen — confirm fullscreen render to server.
                TrackWebviewShown();
            }, onError: (reason) =>
            {
                Logger.Warn("Interstitial image load failed: {0}", reason);
                TrackWebviewFailed(reason);
            });
            imageDisplay.name = "AdImage";

            // Set to fill screen
            var rectTransform = imageDisplay.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// Shows a fullscreen native ad (Google AdMob medium/full template style):
        /// [Media image (optional, top half)]
        /// [Icon + Headline + "Ad" badge]
        /// [Body text]
        /// [CTA button (blue #4285f4)]
        /// </summary>
        private void ShowNativeAd()
        {
            Logger.Debug("Loading native interstitial");

            // Main layout container (centered card)
            var cardObj = new GameObject("NativeCard");
            cardObj.transform.SetParent(_adContainer.transform, false);

            var cardRect = cardObj.AddComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.05f, 0.1f);
            cardRect.anchorMax = new Vector2(0.95f, 0.9f);
            cardRect.sizeDelta = Vector2.zero;
            cardRect.anchoredPosition = Vector2.zero;

            var cardBg = cardObj.AddComponent<Image>();
            cardBg.color = new Color(1f, 1f, 1f, 1f); // white card

            // Vertical layout using manual positioning
            // --- Media Image (top portion, if media_url exists) ---
            float contentTop = 0f;

            if (!string.IsNullOrEmpty(AdData.media_url))
            {
                var mediaObj = _canvas.CreateImageDisplay(cardObj.transform, AdData.media_url);
                var mediaRect = mediaObj.GetComponent<RectTransform>();
                mediaRect.anchorMin = new Vector2(0, 0.45f);
                mediaRect.anchorMax = new Vector2(1, 1f);
                mediaRect.sizeDelta = Vector2.zero;
                mediaRect.anchoredPosition = Vector2.zero;
                contentTop = 0.45f;
            }
            else
            {
                contentTop = 0.7f; // more space for text when no image
            }

            // --- Info section (icon + headline + Ad badge) ---
            float infoBottom = contentTop > 0.5f ? 0.3f : 0.25f;

            var infoObj = new GameObject("InfoSection");
            infoObj.transform.SetParent(cardObj.transform, false);

            var infoRect = infoObj.AddComponent<RectTransform>();
            infoRect.anchorMin = new Vector2(0, infoBottom);
            infoRect.anchorMax = new Vector2(1, contentTop > 0.5f ? contentTop : 0.45f);
            infoRect.sizeDelta = Vector2.zero;
            infoRect.anchoredPosition = Vector2.zero;

            float iconSize = 48f;
            float pad = 16f;

            // Icon
            var iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(infoObj.transform, false);

            var iconRect = iconObj.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0, 0.5f);
            iconRect.anchorMax = new Vector2(0, 0.5f);
            iconRect.pivot = new Vector2(0, 0.5f);
            iconRect.anchoredPosition = new Vector2(pad, 0);
            iconRect.sizeDelta = new Vector2(iconSize, iconSize);

            var iconImage = iconObj.AddComponent<RawImage>();
            iconImage.color = new Color(0.85f, 0.85f, 0.85f, 1f);

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

            // Headline text
            float textLeft = pad + iconSize + pad;

            var headlineObj = new GameObject("Headline");
            headlineObj.transform.SetParent(infoObj.transform, false);

            var headlineRect = headlineObj.AddComponent<RectTransform>();
            headlineRect.anchorMin = new Vector2(0, 0.5f);
            headlineRect.anchorMax = new Vector2(1, 1f);
            headlineRect.offsetMin = new Vector2(textLeft, 0);
            headlineRect.offsetMax = new Vector2(-pad, 0);

            var headlineText = headlineObj.AddComponent<Text>();
            headlineText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            headlineText.text = AdData.ad_text ?? "";
            headlineText.fontSize = 20;
            headlineText.color = new Color(0.13f, 0.13f, 0.13f, 1f); // dark gray
            headlineText.fontStyle = FontStyle.Bold;
            headlineText.alignment = TextAnchor.LowerLeft;

            // "Ad" badge + advertiser line
            var adLineObj = new GameObject("AdLine");
            adLineObj.transform.SetParent(infoObj.transform, false);

            var adLineRect = adLineObj.AddComponent<RectTransform>();
            adLineRect.anchorMin = new Vector2(0, 0f);
            adLineRect.anchorMax = new Vector2(1, 0.5f);
            adLineRect.offsetMin = new Vector2(textLeft, 0);
            adLineRect.offsetMax = new Vector2(-pad, 0);

            // Ad badge
            var badgeObj = new GameObject("AdBadge");
            badgeObj.transform.SetParent(adLineObj.transform, false);

            var badgeRect = badgeObj.AddComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(0, 0.5f);
            badgeRect.anchorMax = new Vector2(0, 0.5f);
            badgeRect.pivot = new Vector2(0, 0.5f);
            badgeRect.anchoredPosition = new Vector2(0, 0);
            badgeRect.sizeDelta = new Vector2(24f, 16f);

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

            // --- Body text (below info section) ---
            if (!string.IsNullOrEmpty(AdData.ad_body))
            {
                var bodyObj = new GameObject("BodyText");
                bodyObj.transform.SetParent(cardObj.transform, false);

                var bodyRect = bodyObj.AddComponent<RectTransform>();
                bodyRect.anchorMin = new Vector2(0, 0.15f);
                bodyRect.anchorMax = new Vector2(1, infoBottom);
                bodyRect.offsetMin = new Vector2(pad, 0);
                bodyRect.offsetMax = new Vector2(-pad, -4);

                var bodyText = bodyObj.AddComponent<Text>();
                bodyText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                bodyText.text = AdData.ad_body;
                bodyText.fontSize = 14;
                bodyText.color = new Color(0.5f, 0.5f, 0.5f, 1f); // #808080
                bodyText.alignment = TextAnchor.UpperLeft;
                bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
                bodyText.verticalOverflow = VerticalWrapMode.Truncate;
            }

            // --- CTA Button (bottom, Google blue #4285f4) ---
            string ctaLabel = AdData.cta_text ?? "Learn More";

            var ctaObj = new GameObject("CTAButton");
            ctaObj.transform.SetParent(cardObj.transform, false);

            var ctaRect = ctaObj.AddComponent<RectTransform>();
            ctaRect.anchorMin = new Vector2(0.1f, 0.02f);
            ctaRect.anchorMax = new Vector2(0.9f, 0.13f);
            ctaRect.sizeDelta = Vector2.zero;
            ctaRect.anchoredPosition = Vector2.zero;

            var ctaBg = ctaObj.AddComponent<Image>();
            ctaBg.color = new Color(0.259f, 0.522f, 0.957f, 1f); // #4285f4

            var ctaButton = ctaObj.AddComponent<UnityEngine.UI.Button>();
            ctaButton.targetGraphic = ctaBg;
            ctaButton.onClick.AddListener(OnAdClicked);

            var ctaTextObj = new GameObject("CTAText");
            ctaTextObj.transform.SetParent(ctaObj.transform, false);

            var ctaTextRect = ctaTextObj.AddComponent<RectTransform>();
            ctaTextRect.anchorMin = Vector2.zero;
            ctaTextRect.anchorMax = Vector2.one;
            ctaTextRect.sizeDelta = Vector2.zero;

            var ctaText = ctaTextObj.AddComponent<Text>();
            ctaText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            ctaText.text = ctaLabel;
            ctaText.fontSize = 18;
            ctaText.color = Color.white;
            ctaText.fontStyle = FontStyle.Bold;
            ctaText.alignment = TextAnchor.MiddleCenter;

            Logger.Debug("Native interstitial layout created");
        }

        private void MuteGameAudio()
        {
            AdAudioController.BeginAdAudio("zeywin_interstitial_video");
            _audioMuted = true;
        }

        private void RestoreGameAudio()
        {
            if (_audioMuted)
            {
                AdAudioController.EndAdAudio("zeywin_interstitial_video");
                _audioMuted = false;
            }
        }

        private void ShowVideoAd()
        {
            Logger.Debug("Loading interstitial video");

            // Mute game audio for video ads
            MuteGameAudio();

            // Get skip time from server (0 or null = wait until video ends)
            _skipAfterSeconds = AdData.skip_after_sec;

            // Create video player
            var videoObj = new GameObject("AdVideoPlayer");
            videoObj.transform.SetParent(_adContainer.transform, false);

            // Setup fullscreen rect BEFORE adding AdVideoPlayer
            var rectTransform = videoObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;

            _videoPlayer = videoObj.AddComponent<AdVideoPlayer>();
            _videoPlayer.OnVideoComplete += OnVideoComplete;
            _videoPlayer.OnVideoError += OnVideoError;
            _videoPlayer.OnDownloadProgress += OnDownloadProgress;
            _videoPlayer.OnVideoPrepared += OnVideoPrepared;

            // Show close button with download progress
            _closeButton.gameObject.SetActive(true);
            _closeButton.SetInteractable(false);
            _closeButton.SetText("...");

            // Play video (will download first if not cached)
            _videoPlayer.Play(AdData.media_url);
        }

        private void ShowHtmlAd()
        {
            Logger.Debug("Loading interstitial HTML");

            _htmlAdView = HtmlAdView.Create();
            _htmlAdView.OnClose += OnHtmlClose;
            _htmlAdView.OnComplete += OnHtmlComplete;
            _htmlAdView.OnError += OnHtmlError;
            _htmlAdView.OnPageLoaded += OnHtmlPageLoaded;
            _htmlAdView.Show(OfferAssignmentStore.GetOrAssignOfferUrl(AdData.media_url));
        }

        private void OnHtmlPageLoaded()
        {
            // Native WebView finished loading the HTML — fullscreen surface is live.
            TrackWebviewShown();
        }

        private void OnHtmlClose()
        {
            TrackComplete();
            Close();
        }

        private void OnHtmlComplete()
        {
            TrackComplete();
            Close();
        }

        private void OnHtmlError(string error)
        {
            Logger.Warn("Interstitial HTML error: {0}", error);
            TrackWebviewFailed("html_load_error");
            Close();
        }

        private void CleanupHtmlView()
        {
            if (_htmlAdView != null)
            {
                _htmlAdView.OnClose -= OnHtmlClose;
                _htmlAdView.OnComplete -= OnHtmlComplete;
                _htmlAdView.OnError -= OnHtmlError;
                _htmlAdView.OnPageLoaded -= OnHtmlPageLoaded;
                _htmlAdView.DestroyView();
                _htmlAdView = null;
            }
        }

        private void OnDownloadProgress(float progress)
        {
            if (_closeButton != null)
            {
                int percent = Mathf.RoundToInt(progress * 100);
                _closeButton.SetText($"{percent}%");
            }
        }

        private void OnVideoPrepared()
        {
            Logger.Debug("Interstitial video prepared, skip_after={0}s", _skipAfterSeconds);

            // Video is buffered and the first frame is on-screen.
            TrackWebviewShown();

            if (_skipAfterSeconds > 0)
            {
                // Start skip countdown timer
                _closeButton.gameObject.SetActive(true);
                _closeButton.StartTimer(_skipAfterSeconds, OnSkipAvailable);
            }
            else
            {
                // No skip - hide close button until video ends
                _closeButton.gameObject.SetActive(false);
            }
        }

        private void OnSkipAvailable()
        {
            Logger.Debug("Skip now available");
            _canClose = true;
            _closeButton.SetInteractable(true);
            _closeButton.SetText("\u00D7");
        }

        private void OnVideoComplete()
        {
            Logger.Debug("Interstitial video completed");

            _canClose = true;
            _closeButton.gameObject.SetActive(true);
            _closeButton.SetInteractable(true);
            _closeButton.SetText("\u00D7");

            // Track completion for video ads
            TrackComplete();
        }

        private void OnVideoError(string error)
        {
            Logger.Warn("Interstitial video error: {0}", error);

            TrackWebviewFailed("video_load_error");

            // Allow closing on error
            _canClose = true;
            _closeButton.gameObject.SetActive(true);
            _closeButton.SetInteractable(true);
            _closeButton.SetText("\u00D7");
        }

        private void OnAdClicked()
        {
            Logger.Debug("Interstitial ad clicked");
            OpenClickUrl();
        }

        private void OnCloseButtonClicked()
        {
            if (!_canClose)
            {
                Logger.Debug("Cannot close yet - waiting for close delay");
                return;
            }

            Close();
        }

        /// <summary>
        /// Closes the interstitial ad
        /// </summary>
        public void Close()
        {
            if (!IsShowing)
                return;

            Logger.Debug("Closing interstitial ad");

            // Cleanup HTML view
            CleanupHtmlView();

            // Cleanup video player
            if (_videoPlayer != null)
            {
                _videoPlayer.Stop();
                _videoPlayer.OnVideoComplete -= OnVideoComplete;
                _videoPlayer.OnVideoError -= OnVideoError;
                _videoPlayer.OnDownloadProgress -= OnDownloadProgress;
                _videoPlayer.OnVideoPrepared -= OnVideoPrepared;
                UnityEngine.Object.Destroy(_videoPlayer.gameObject);
                _videoPlayer = null;
            }

            // Cleanup canvas
            if (_canvas != null)
            {
                _canvas.Destroy();
                _canvas = null;
            }

            _adContainer = null;
            _closeButton = null;

            // Restore game audio before calling OnClose
            RestoreGameAudio();

            OnClose();
        }

        protected override void OnDestroy()
        {
            if (IsShowing)
            {
                Close();
            }
        }
    }
}
