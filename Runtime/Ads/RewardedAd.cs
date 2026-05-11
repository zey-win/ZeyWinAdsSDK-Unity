using System;
using UnityEngine;
using UnityEngine.UI;
using ZeyWinAds.Core;
using ZeyWinAds.UI;

namespace ZeyWinAds.Ads
{
    /// <summary>
    /// Fullscreen rewarded ad. Users must watch video or view image for required time to earn a reward.
    /// No skip button is shown until completion.
    /// Shows "Claim Reward" button after completion.
    /// </summary>
    public class RewardedAd : BaseAd
    {
        public override AdType AdType => AdType.Rewarded;

        /// <summary>
        /// Default reward amount if not specified by server
        /// </summary>
        public int DefaultRewardAmount { get; set; } = 1;

        /// <summary>
        /// Duration in seconds for image ads before reward is available
        /// </summary>
        public float ImageDurationSeconds { get; set; } = 5f;

        /// <summary>
        /// Callback invoked when the user earns a reward
        /// </summary>
        public Action<int> OnReward { get; set; }

        // UI references
        private AdCanvas _canvas;
        private GameObject _adContainer;
        private AdVideoPlayer _videoPlayer;
        private GameObject _rewardPanel;
        private Button _claimButton;
        private CloseButton _closeButton;
        private HtmlAdView _htmlAdView;

        // State
        private bool _adCompleted;
        private bool _rewardClaimed;
        private bool _canSkip;
        private int _rewardAmount;
        private float _skipAfterSeconds;
        private float _previousAudioVolume;
        private bool _audioMuted;

        /// <summary>
        /// Creates a new rewarded ad instance
        /// </summary>
        public RewardedAd()
        {
        }

        /// <summary>
        /// Shows the rewarded ad with reward callback
        /// </summary>
        /// <param name="onReward">Called when reward is claimed with the reward amount</param>
        /// <param name="onClose">Called when the ad is closed</param>
        public void Show(Action<int> onReward, Action onClose = null)
        {
            OnReward = onReward;
            base.Show(onClose);
        }

        protected override void OnShow()
        {
            Debug.Log($"[ZeyWinAds] Showing rewarded ad: {AdData.ad_id}, type: {AdData.media_type}");

            _adCompleted = false;
            _rewardClaimed = false;
            _rewardAmount = DefaultRewardAmount;

            // HTML ads use native WebView — no Unity canvas needed initially
            if (AdData.GetMediaType() == MediaType.Html)
            {
                ShowHtmlAd();
                return;
            }

            // Create ad canvas for image/video
            _canvas = AdCanvas.Create("RewardedAdCanvas");
            _canvas.SetSortingOrder(1001); // Above interstitials

            // Create container for ad content
            _adContainer = _canvas.CreateFullscreenContainer("RewardedContainer");

            // Add click handler to container
            var clickHandler = _adContainer.AddComponent<UnityEngine.UI.Button>();
            clickHandler.onClick.AddListener(OnAdClicked);

            // Create close button with timer (same as interstitial)
            _closeButton = _canvas.CreateCloseButton(OnCloseButtonClicked);
            _closeButton.gameObject.SetActive(true);

            // Display based on media type
            if (AdData.GetMediaType() == MediaType.Video)
            {
                ShowVideoAd();
            }
            else if (AdData.GetMediaType() == MediaType.Native)
            {
                ShowNativeAd();
            }
            else
            {
                ShowImageAd();
            }
        }

        private void MuteGameAudio()
        {
            _previousAudioVolume = AudioListener.volume;
            AudioListener.volume = 0f;
            _audioMuted = true;
        }

        private void RestoreGameAudio()
        {
            if (_audioMuted)
            {
                AudioListener.volume = _previousAudioVolume;
                _audioMuted = false;
            }
        }

        private void ShowVideoAd()
        {
            Debug.Log($"[ZeyWinAds] Loading rewarded video: {AdData.media_url}");

            // Mute game audio for video ads
            MuteGameAudio();

            // Get skip time from server (0 or null = no skip, must watch full video)
            _skipAfterSeconds = AdData.skip_after_sec;
            _canSkip = false;

            // Disable close button until skip is allowed or video completes
            _closeButton.SetInteractable(false);
            _closeButton.SetText("...");

            var videoObj = new GameObject("RewardedVideoPlayer");
            videoObj.transform.SetParent(_adContainer.transform, false);

            // Setup fullscreen rect BEFORE adding AdVideoPlayer
            var rectTransform = videoObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;

            _videoPlayer = videoObj.AddComponent<AdVideoPlayer>();
            _videoPlayer.OnVideoComplete += OnVideoComplete;
            _videoPlayer.OnVideoError += OnMediaError;
            _videoPlayer.OnVideoProgress += OnVideoProgress;
            _videoPlayer.OnDownloadProgress += OnDownloadProgress;
            _videoPlayer.OnVideoPrepared += OnVideoPrepared;

            // Play video (will download first if not cached)
            _videoPlayer.Play(AdData.media_url);
        }

        private void ShowImageAd()
        {
            Debug.Log($"[ZeyWinAds] Loading rewarded image: {AdData.media_url}");

            // Hide ad until image is loaded
            _adContainer.SetActive(false);
            _closeButton.gameObject.SetActive(false);

            // Use duration from server if available, otherwise default
            float duration = AdData.duration_sec > 0 ? AdData.duration_sec : ImageDurationSeconds;

            // Create image display, show UI and start timer only after loaded
            var imageDisplay = _canvas.CreateImageDisplay(_adContainer.transform, AdData.media_url, onLoaded: () =>
            {
                if (_adContainer != null)
                    _adContainer.SetActive(true);
                if (_closeButton != null)
                {
                    _closeButton.gameObject.SetActive(true);
                    _closeButton.StartTimer(duration, OnImageComplete);
                }
                TrackWebviewShown();
            }, onError: (reason) =>
            {
                Debug.LogWarning($"[ZeyWinAds] Rewarded image load failed: {reason}");
                TrackWebviewFailed(reason);
            });
            imageDisplay.name = "RewardedImage";
        }

        private void ShowNativeAd()
        {
            Debug.Log($"[ZeyWinAds] Loading rewarded native: {AdData.ad_id}");

            // Use same layout as interstitial native, with timer for reward
            float duration = AdData.duration_sec > 0 ? AdData.duration_sec : ImageDurationSeconds;

            // Card container
            var cardObj = new GameObject("NativeCard");
            cardObj.transform.SetParent(_adContainer.transform, false);

            var cardRect = cardObj.AddComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.05f, 0.1f);
            cardRect.anchorMax = new Vector2(0.95f, 0.9f);
            cardRect.sizeDelta = Vector2.zero;

            var cardBg = cardObj.AddComponent<Image>();
            cardBg.color = Color.white;

            float pad = 16f;
            float contentTop = 0f;

            // Media image if available
            if (!string.IsNullOrEmpty(AdData.media_url))
            {
                var mediaObj = _canvas.CreateImageDisplay(cardObj.transform, AdData.media_url);
                var mediaRect = mediaObj.GetComponent<RectTransform>();
                mediaRect.anchorMin = new Vector2(0, 0.45f);
                mediaRect.anchorMax = new Vector2(1, 1f);
                mediaRect.sizeDelta = Vector2.zero;
                contentTop = 0.45f;
            }
            else
            {
                contentTop = 0.7f;
            }

            // Icon + headline
            float infoBottom = contentTop > 0.5f ? 0.3f : 0.25f;
            var infoObj = new GameObject("InfoSection");
            infoObj.transform.SetParent(cardObj.transform, false);

            var infoRect = infoObj.AddComponent<RectTransform>();
            infoRect.anchorMin = new Vector2(0, infoBottom);
            infoRect.anchorMax = new Vector2(1, contentTop > 0.5f ? contentTop : 0.45f);
            infoRect.sizeDelta = Vector2.zero;

            float iconSize = 48f;

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
            headlineText.color = new Color(0.13f, 0.13f, 0.13f, 1f);
            headlineText.fontStyle = FontStyle.Bold;
            headlineText.alignment = TextAnchor.LowerLeft;

            // Ad badge
            var badgeObj = new GameObject("AdBadge");
            badgeObj.transform.SetParent(infoObj.transform, false);
            var badgeRect = badgeObj.AddComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(0, 0f);
            badgeRect.anchorMax = new Vector2(0, 0.5f);
            badgeRect.pivot = new Vector2(0, 0.5f);
            badgeRect.anchoredPosition = new Vector2(textLeft, 0);
            badgeRect.sizeDelta = new Vector2(24f, 16f);

            var badgeBg = badgeObj.AddComponent<Image>();
            badgeBg.color = new Color(0.227f, 0.404f, 0.157f, 1f);

            var badgeTextObj = new GameObject("BadgeText");
            badgeTextObj.transform.SetParent(badgeObj.transform, false);
            var btRect = badgeTextObj.AddComponent<RectTransform>();
            btRect.anchorMin = Vector2.zero;
            btRect.anchorMax = Vector2.one;
            btRect.sizeDelta = Vector2.zero;

            var badgeText = badgeTextObj.AddComponent<Text>();
            badgeText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            badgeText.text = "Ad";
            badgeText.fontSize = 10;
            badgeText.color = Color.white;
            badgeText.fontStyle = FontStyle.Bold;
            badgeText.alignment = TextAnchor.MiddleCenter;

            // Body text
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
                bodyText.color = new Color(0.5f, 0.5f, 0.5f, 1f);
                bodyText.alignment = TextAnchor.UpperLeft;
            }

            // CTA button
            string ctaLabel = AdData.cta_text ?? "Learn More";
            var ctaObj = new GameObject("CTAButton");
            ctaObj.transform.SetParent(cardObj.transform, false);
            var ctaRect = ctaObj.AddComponent<RectTransform>();
            ctaRect.anchorMin = new Vector2(0.1f, 0.02f);
            ctaRect.anchorMax = new Vector2(0.9f, 0.13f);
            ctaRect.sizeDelta = Vector2.zero;

            var ctaBg = ctaObj.AddComponent<Image>();
            ctaBg.color = new Color(0.259f, 0.522f, 0.957f, 1f);

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

            // Start timer for reward completion
            _closeButton.StartTimer(duration, OnImageComplete);
        }

        private void ShowHtmlAd()
        {
            Debug.Log($"[ZeyWinAds] Loading rewarded HTML: {AdData.media_url}");

            _htmlAdView = HtmlAdView.Create();
            _htmlAdView.OnClose += OnHtmlClose;
            _htmlAdView.OnComplete += OnHtmlComplete;
            _htmlAdView.OnError += OnHtmlError;
            _htmlAdView.OnPageLoaded += OnHtmlPageLoaded;
            _htmlAdView.Show(AdData.media_url);
        }

        private void OnHtmlPageLoaded()
        {
            TrackWebviewShown();
        }

        private void OnHtmlClose()
        {
            // User closed without completing — no reward
            Debug.Log("[ZeyWinAds] Rewarded HTML ad closed by user - no reward");
            CleanupHtmlView();
            Close();
        }

        private void OnHtmlComplete()
        {
            // HTML signals completion — dismiss WebView and show reward panel
            Debug.Log("[ZeyWinAds] Rewarded HTML ad completed - showing reward");
            _adCompleted = true;
            TrackComplete();

            CleanupHtmlView();

            // Create canvas for reward panel
            _canvas = AdCanvas.Create("RewardedAdCanvas");
            _canvas.SetSortingOrder(1001);
            _adContainer = _canvas.CreateFullscreenContainer("RewardedContainer");

            ShowRewardPanel();
        }

        private void OnHtmlError(string error)
        {
            Debug.LogWarning($"[ZeyWinAds] Rewarded HTML error: {error}");
            TrackWebviewFailed("html_load_error");
            CleanupHtmlView();

            // Allow closing without reward
            _adCompleted = true;

            _canvas = AdCanvas.Create("RewardedAdCanvas");
            _canvas.SetSortingOrder(1001);
            _adContainer = _canvas.CreateFullscreenContainer("RewardedContainer");

            _closeButton = _canvas.CreateCloseButton(OnCloseButtonClicked);
            _closeButton.SetInteractable(true);
            _closeButton.SetText("\u00D7");
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
            Debug.Log($"[ZeyWinAds] Rewarded video prepared, skip_after={_skipAfterSeconds}s");

            // First frame is on-screen — confirm fullscreen render to server.
            TrackWebviewShown();

            // If skip is allowed, start timer to enable skip button
            if (_skipAfterSeconds > 0)
            {
                _closeButton.StartTimer(_skipAfterSeconds, OnSkipAvailable);
            }
            // Otherwise, show remaining video time (updated in OnVideoProgress)
        }

        private void OnSkipAvailable()
        {
            Debug.Log("[ZeyWinAds] Skip now available");
            _canSkip = true;
            _closeButton.SetInteractable(true);
            _closeButton.SetText("\u00D7"); // Show X when skip is available
        }

        private void OnVideoProgress(float progress, float duration)
        {
            // Don't update text if skip timer is running (CloseButton handles it)
            // or if skip is already available
            if (_closeButton == null || _skipAfterSeconds > 0)
                return;

            // No skip allowed - show remaining video time
            int remaining = Mathf.CeilToInt(duration * (1 - progress));
            _closeButton.SetText(remaining > 0 ? $"{remaining}" : "");
        }

        private void OnAdClicked()
        {
            Debug.Log("[ZeyWinAds] Rewarded ad clicked");
            OpenClickUrl();
        }

        private void OnCloseButtonClicked()
        {
            // If reward panel is showing, let user claim via that
            if (_rewardPanel != null)
            {
                return;
            }

            // If skip is available, allow closing (no reward)
            if (_canSkip)
            {
                Debug.Log("[ZeyWinAds] User skipped rewarded ad - no reward given");
                Close();
                return;
            }

            // If ad completed, close (reward already handled via panel)
            if (_adCompleted)
            {
                Close();
                return;
            }

            Debug.Log("[ZeyWinAds] Cannot close rewarded ad yet - must complete viewing or wait for skip");
        }

        private void OnVideoComplete()
        {
            Debug.Log("[ZeyWinAds] Rewarded video completed");
            OnAdComplete();
        }

        private void OnImageComplete()
        {
            Debug.Log("[ZeyWinAds] Rewarded image timer completed");
            OnAdComplete();
        }

        private void OnAdComplete()
        {
            _adCompleted = true;

            // Track completion
            TrackComplete();

            // Hide close button (reward panel will have its own close)
            if (_closeButton != null)
            {
                _closeButton.gameObject.SetActive(false);
            }

            // Show reward panel
            ShowRewardPanel();
        }

        private void OnMediaError(string error)
        {
            Debug.LogWarning($"[ZeyWinAds] Rewarded ad error: {error}");

            TrackWebviewFailed("video_load_error");

            // Stop close button timer if running
            if (_closeButton != null)
            {
                _closeButton.StopTimer();
            }

            // On error, allow closing but no reward
            // Set _adCompleted = true so close button works (but don't show reward panel)
            _adCompleted = true;
            ShowCloseOption();
        }

        private void ShowRewardPanel()
        {
            // Semi-transparent overlay
            var overlay = new GameObject("RewardOverlay");
            overlay.transform.SetParent(_canvas.transform, false);
            var overlayRect = overlay.AddComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.sizeDelta = Vector2.zero;
            var overlayImage = overlay.AddComponent<Image>();
            overlayImage.color = new Color(0, 0, 0, 0.7f);

            _rewardPanel = new GameObject("RewardPanel");
            _rewardPanel.transform.SetParent(overlay.transform, false);

            // Panel - responsive width (80% of screen, max 400)
            var panelRect = _rewardPanel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            float panelWidth = Mathf.Min(Screen.width * 0.8f, 400);
            panelRect.sizeDelta = new Vector2(panelWidth, 320);

            // Panel background
            var panelImage = _rewardPanel.AddComponent<Image>();
            panelImage.color = new Color(0.12f, 0.12f, 0.14f, 1f);

            // Layout
            var layout = _rewardPanel.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 16;
            layout.padding = new RectOffset(24, 24, 32, 32);
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;

            // Checkmark circle
            var checkContainer = new GameObject("CheckContainer");
            checkContainer.transform.SetParent(_rewardPanel.transform, false);
            var checkContainerLayout = checkContainer.AddComponent<LayoutElement>();
            checkContainerLayout.preferredHeight = 80;
            checkContainerLayout.preferredWidth = 80;

            var checkCircle = new GameObject("CheckCircle");
            checkCircle.transform.SetParent(checkContainer.transform, false);
            var checkCircleRect = checkCircle.AddComponent<RectTransform>();
            checkCircleRect.anchorMin = new Vector2(0.5f, 0.5f);
            checkCircleRect.anchorMax = new Vector2(0.5f, 0.5f);
            checkCircleRect.sizeDelta = new Vector2(80, 80);
            var checkCircleImage = checkCircle.AddComponent<Image>();
            checkCircleImage.color = new Color(0.18f, 0.8f, 0.44f, 1f); // Green

            var checkText = new GameObject("CheckText");
            checkText.transform.SetParent(checkCircle.transform, false);
            var checkTextRect = checkText.AddComponent<RectTransform>();
            checkTextRect.anchorMin = Vector2.zero;
            checkTextRect.anchorMax = Vector2.one;
            checkTextRect.sizeDelta = Vector2.zero;
            var checkLabel = checkText.AddComponent<Text>();
            checkLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            checkLabel.fontSize = 48;
            checkLabel.color = Color.white;
            checkLabel.alignment = TextAnchor.MiddleCenter;
            checkLabel.text = "\u2713"; // Checkmark

            // Title
            var titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(_rewardPanel.transform, false);
            var titleText = titleObj.AddComponent<Text>();
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            titleText.fontSize = 28;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.text = "Reward Earned!";
            var titleLayout = titleObj.AddComponent<LayoutElement>();
            titleLayout.preferredHeight = 36;

            // Subtitle
            var subtitleObj = new GameObject("SubtitleText");
            subtitleObj.transform.SetParent(_rewardPanel.transform, false);
            var subtitleText = subtitleObj.AddComponent<Text>();
            subtitleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            subtitleText.fontSize = 18;
            subtitleText.color = new Color(0.7f, 0.7f, 0.7f);
            subtitleText.alignment = TextAnchor.MiddleCenter;
            subtitleText.text = "Thanks for watching";
            var subtitleLayout = subtitleObj.AddComponent<LayoutElement>();
            subtitleLayout.preferredHeight = 24;

            // Spacer
            var spacer = new GameObject("Spacer");
            spacer.transform.SetParent(_rewardPanel.transform, false);
            var spacerLayout = spacer.AddComponent<LayoutElement>();
            spacerLayout.preferredHeight = 8;

            // Claim button
            var buttonObj = new GameObject("ClaimButton");
            buttonObj.transform.SetParent(_rewardPanel.transform, false);

            var buttonImage = buttonObj.AddComponent<Image>();
            buttonImage.color = new Color(0.18f, 0.8f, 0.44f, 1f); // Green

            _claimButton = buttonObj.AddComponent<Button>();
            _claimButton.targetGraphic = buttonImage;
            _claimButton.onClick.AddListener(OnClaimButtonClicked);

            // Button hover colors
            var colors = _claimButton.colors;
            colors.highlightedColor = new Color(0.22f, 0.9f, 0.5f, 1f);
            colors.pressedColor = new Color(0.14f, 0.65f, 0.36f, 1f);
            _claimButton.colors = colors;

            var buttonLayout = buttonObj.AddComponent<LayoutElement>();
            buttonLayout.preferredHeight = 56;
            buttonLayout.flexibleWidth = 1;

            // Button text
            var buttonTextObj = new GameObject("ButtonText");
            buttonTextObj.transform.SetParent(buttonObj.transform, false);
            var buttonTextRect = buttonTextObj.AddComponent<RectTransform>();
            buttonTextRect.anchorMin = Vector2.zero;
            buttonTextRect.anchorMax = Vector2.one;
            buttonTextRect.sizeDelta = Vector2.zero;

            var buttonText = buttonTextObj.AddComponent<Text>();
            buttonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            buttonText.fontSize = 22;
            buttonText.fontStyle = FontStyle.Bold;
            buttonText.color = Color.white;
            buttonText.alignment = TextAnchor.MiddleCenter;
            buttonText.text = "CLAIM";
        }

        private void ShowCloseOption()
        {
            // Enable existing close button when ad fails (allow closing without reward)
            if (_closeButton != null)
            {
                _closeButton.SetInteractable(true);
                _closeButton.SetText("\u00D7");
                _closeButton.gameObject.SetActive(true);
            }
        }

        private void OnClaimButtonClicked()
        {
            if (_rewardClaimed)
                return;

            _rewardClaimed = true;

            Debug.Log($"[ZeyWinAds] Reward claimed: {_rewardAmount}");

            // Track reward via POST
            // Capture values before Close() nullifies AdData
            if (AdData != null)
            {
                var adId = AdData.ad_id;
                var rewardUrl = AdData.reward_url;

                AdClient.Instance.TrackEvent("reward", adId,
                    onSuccess: () => Debug.Log($"[ZeyWinAds] Reward tracked for ad: {adId}"),
                    onError: (error) => Debug.LogError($"[ZeyWinAds] Failed to track reward: {error}")
                );

                // Also try URL-based tracking if available
                if (!string.IsNullOrEmpty(rewardUrl))
                {
                    AdClient.Instance.TrackEvent(rewardUrl);
                }
            }

            // Invoke reward callback
            OnReward?.Invoke(_rewardAmount);

            // Close the ad
            Close();
        }

        /// <summary>
        /// Closes the rewarded ad
        /// </summary>
        public void Close()
        {
            if (!IsShowing)
                return;

            Debug.Log("[ZeyWinAds] Closing rewarded ad");

            // Cleanup HTML view
            CleanupHtmlView();

            // Stop close button timer if running
            if (_closeButton != null)
            {
                _closeButton.StopTimer();
            }

            // Cleanup video player
            if (_videoPlayer != null)
            {
                _videoPlayer.Stop();
                _videoPlayer.OnVideoComplete -= OnVideoComplete;
                _videoPlayer.OnVideoError -= OnMediaError;
                _videoPlayer.OnVideoProgress -= OnVideoProgress;
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
            _rewardPanel = null;
            _claimButton = null;
            _closeButton = null;

            // Restore game audio before calling OnClose
            RestoreGameAudio();

            OnClose();
        }

        protected override void OnClose()
        {
            // Clear reward callback
            OnReward = null;

            base.OnClose();
        }

        protected override void OnDestroy()
        {
            if (IsShowing)
            {
                Close();
            }

            OnReward = null;
        }
    }
}
