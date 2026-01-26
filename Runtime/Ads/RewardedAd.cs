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

        // State
        private bool _adCompleted;
        private bool _rewardClaimed;
        private int _rewardAmount;

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

            // Create ad canvas
            _canvas = AdCanvas.Create("RewardedAdCanvas");
            _canvas.SetSortingOrder(1001); // Above interstitials

            // Create container for ad content
            _adContainer = _canvas.CreateFullscreenContainer("RewardedContainer");

            // Create close button with timer (same as interstitial)
            _closeButton = _canvas.CreateCloseButton(OnCloseButtonClicked);
            _closeButton.gameObject.SetActive(true);

            // Display based on media type
            if (AdData.GetMediaType() == MediaType.Video)
            {
                ShowVideoAd();
            }
            else
            {
                ShowImageAd();
            }
        }

        private void ShowVideoAd()
        {
            Debug.Log($"[ZeyWinAds] Loading rewarded video: {AdData.media_url}");

            // Disable close button until video completes
            _closeButton.SetInteractable(false);
            _closeButton.SetText("");

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

            // Play video
            _videoPlayer.Play(AdData.media_url);
        }

        private void ShowImageAd()
        {
            Debug.Log($"[ZeyWinAds] Loading rewarded image: {AdData.media_url}");

            // Create image display
            var imageDisplay = _canvas.CreateImageDisplay(_adContainer.transform, AdData.media_url);
            imageDisplay.name = "RewardedImage";

            // Use duration from server if available, otherwise default
            float duration = AdData.duration_sec.HasValue ? AdData.duration_sec.Value : ImageDurationSeconds;

            // Start timer on close button (same style as interstitial)
            _closeButton.StartTimer(duration, OnImageComplete);
        }

        private void OnVideoProgress(float progress, float duration)
        {
            if (_closeButton != null)
            {
                int remaining = Mathf.CeilToInt(duration * (1 - progress));
                _closeButton.SetText(remaining > 0 ? $"{remaining}" : "");
            }
        }

        private void OnCloseButtonClicked()
        {
            // For rewarded ads, close button click is ignored until ad completes
            // After completion, user must claim reward via the reward panel
            if (!_adCompleted)
            {
                Debug.Log("[ZeyWinAds] Cannot close rewarded ad yet - must complete viewing");
                return;
            }

            // If reward panel is showing, let user claim via that
            if (_rewardPanel != null)
            {
                return;
            }

            Close();
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
            _rewardPanel = new GameObject("RewardPanel");
            _rewardPanel.transform.SetParent(_canvas.transform, false);

            // Setup panel rect
            var panelRect = _rewardPanel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(300, 200);

            // Panel background
            var panelImage = _rewardPanel.AddComponent<Image>();
            panelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);

            // Add layout
            var layout = _rewardPanel.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 20;
            layout.padding = new RectOffset(20, 20, 30, 30);
            layout.childControlWidth = true;
            layout.childControlHeight = false;

            // Success text
            var successObj = new GameObject("SuccessText");
            successObj.transform.SetParent(_rewardPanel.transform, false);
            var successText = successObj.AddComponent<Text>();
            successText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            successText.fontSize = 24;
            successText.color = Color.white;
            successText.alignment = TextAnchor.MiddleCenter;
            successText.text = "Ad Complete!";
            var successLayout = successObj.AddComponent<LayoutElement>();
            successLayout.preferredHeight = 40;

            // Reward text
            var rewardObj = new GameObject("RewardText");
            rewardObj.transform.SetParent(_rewardPanel.transform, false);
            var rewardText = rewardObj.AddComponent<Text>();
            rewardText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            rewardText.fontSize = 18;
            rewardText.color = new Color(0.8f, 0.8f, 0.8f);
            rewardText.alignment = TextAnchor.MiddleCenter;
            rewardText.text = $"You earned {_rewardAmount} reward(s)";
            var rewardLayout = rewardObj.AddComponent<LayoutElement>();
            rewardLayout.preferredHeight = 30;

            // Claim button
            var buttonObj = new GameObject("ClaimButton");
            buttonObj.transform.SetParent(_rewardPanel.transform, false);

            var buttonRect = buttonObj.AddComponent<RectTransform>();
            buttonRect.sizeDelta = new Vector2(200, 50);

            var buttonImage = buttonObj.AddComponent<Image>();
            buttonImage.color = new Color(0.2f, 0.7f, 0.3f);

            _claimButton = buttonObj.AddComponent<Button>();
            _claimButton.targetGraphic = buttonImage;
            _claimButton.onClick.AddListener(OnClaimButtonClicked);

            var buttonLayout = buttonObj.AddComponent<LayoutElement>();
            buttonLayout.preferredHeight = 50;
            buttonLayout.preferredWidth = 200;

            // Button text
            var buttonTextObj = new GameObject("ButtonText");
            buttonTextObj.transform.SetParent(buttonObj.transform, false);
            var buttonTextRect = buttonTextObj.AddComponent<RectTransform>();
            buttonTextRect.anchorMin = Vector2.zero;
            buttonTextRect.anchorMax = Vector2.one;
            buttonTextRect.sizeDelta = Vector2.zero;

            var buttonText = buttonTextObj.AddComponent<Text>();
            buttonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            buttonText.fontSize = 20;
            buttonText.color = Color.white;
            buttonText.alignment = TextAnchor.MiddleCenter;
            buttonText.text = "Claim Reward";
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
            if (AdData != null)
            {
                AdClient.Instance.TrackEvent("reward", AdData.ad_id,
                    onSuccess: () => Debug.Log($"[ZeyWinAds] Reward tracked for ad: {AdData.ad_id}"),
                    onError: (error) => Debug.LogError($"[ZeyWinAds] Failed to track reward: {error}")
                );

                // Also try URL-based tracking if available
                if (!string.IsNullOrEmpty(AdData.reward_url))
                {
                    AdClient.Instance.TrackEvent(AdData.reward_url);
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
