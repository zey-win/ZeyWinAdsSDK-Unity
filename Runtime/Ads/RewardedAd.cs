using System;
using UnityEngine;
using UnityEngine.UI;
using ZeyWinAds.Core;
using ZeyWinAds.UI;

namespace ZeyWinAds.Ads
{
    /// <summary>
    /// Fullscreen rewarded video ad. Users must watch the entire video to earn a reward.
    /// No skip button is shown until the video completes.
    /// Shows "Claim Reward" button after video ends.
    /// </summary>
    public class RewardedAd : BaseAd
    {
        public override AdType AdType => AdType.Rewarded;

        /// <summary>
        /// Default reward amount if not specified by server
        /// </summary>
        public int DefaultRewardAmount { get; set; } = 1;

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

        // State
        private bool _videoCompleted;
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
            Debug.Log($"[ZeyWinAds] Showing rewarded ad: {AdData.ad_id}");

            _videoCompleted = false;
            _rewardClaimed = false;
            _rewardAmount = DefaultRewardAmount;

            // Create ad canvas
            _canvas = AdCanvas.Create("RewardedAdCanvas");
            _canvas.SetSortingOrder(1001); // Above interstitials

            // Create container for ad content
            _adContainer = _canvas.CreateFullscreenContainer("RewardedContainer");

            // Create video player (rewarded ads are always video)
            var videoObj = new GameObject("RewardedVideoPlayer");
            videoObj.transform.SetParent(_adContainer.transform, false);

            _videoPlayer = videoObj.AddComponent<AdVideoPlayer>();
            _videoPlayer.OnVideoComplete += OnVideoComplete;
            _videoPlayer.OnVideoError += OnVideoError;
            _videoPlayer.OnVideoProgress += OnVideoProgress;

            // Setup fullscreen video
            var rectTransform = videoObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;

            // Create progress indicator (shows remaining time)
            CreateProgressIndicator();

            // Play video
            _videoPlayer.Play(AdData.media_url);

            // Note: No close button or skip button until video completes
        }

        private void CreateProgressIndicator()
        {
            var progressObj = new GameObject("ProgressIndicator");
            progressObj.transform.SetParent(_canvas.transform, false);

            var rectTransform = progressObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0, 1);
            rectTransform.anchorMax = new Vector2(0, 1);
            rectTransform.pivot = new Vector2(0, 1);
            rectTransform.anchoredPosition = new Vector2(20, -20);
            rectTransform.sizeDelta = new Vector2(100, 30);

            var text = progressObj.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 18;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.text = "";

            // Add shadow for visibility
            var shadow = progressObj.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.5f);
            shadow.effectDistance = new Vector2(1, -1);
        }

        private void OnVideoProgress(float progress, float duration)
        {
            // Update progress indicator
            var progressText = _canvas.transform.Find("ProgressIndicator")?.GetComponent<Text>();
            if (progressText != null)
            {
                int remaining = Mathf.CeilToInt(duration * (1 - progress));
                progressText.text = remaining > 0 ? $"{remaining}s" : "";
            }
        }

        private void OnVideoComplete()
        {
            Debug.Log("[ZeyWinAds] Rewarded video completed");

            _videoCompleted = true;

            // Track completion
            TrackComplete();

            // Hide progress indicator
            var progressIndicator = _canvas.transform.Find("ProgressIndicator");
            if (progressIndicator != null)
            {
                progressIndicator.gameObject.SetActive(false);
            }

            // Show reward panel
            ShowRewardPanel();
        }

        private void OnVideoError(string error)
        {
            Debug.LogWarning($"[ZeyWinAds] Rewarded video error: {error}");

            // On error, allow closing but no reward
            _videoCompleted = false;
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
            successText.text = "Video Complete!";
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
            // Show a simple close button when video fails
            var closeButton = _canvas.CreateCloseButton(Close);
            closeButton.gameObject.SetActive(true);
        }

        private void OnClaimButtonClicked()
        {
            if (_rewardClaimed)
                return;

            _rewardClaimed = true;

            Debug.Log($"[ZeyWinAds] Reward claimed: {_rewardAmount}");

            // Track reward
            if (AdData != null && !string.IsNullOrEmpty(AdData.reward_url))
            {
                AdClient.Instance.TrackEvent(AdData.reward_url);
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

            // Cleanup video player
            if (_videoPlayer != null)
            {
                _videoPlayer.Stop();
                _videoPlayer.OnVideoComplete -= OnVideoComplete;
                _videoPlayer.OnVideoError -= OnVideoError;
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
