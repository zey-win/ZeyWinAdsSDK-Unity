using System;
using UnityEngine;
using ZeyWinAds.Core;
using ZeyWinAds.UI;

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

        // State
        private bool _canClose;
        private float _showTime;
        private float _skipAfterSeconds;
        private float _previousAudioVolume;
        private bool _audioMuted;

        /// <summary>
        /// Creates a new interstitial ad instance
        /// </summary>
        public InterstitialAd()
        {
        }

        protected override void OnShow()
        {
            Debug.Log($"[ZeyWinAds] Showing interstitial ad: {AdData.ad_id}");

            _canClose = false;
            _showTime = Time.realtimeSinceStartup;

            // Create ad canvas
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
            else
            {
                ShowImageAd();
                // For images, show close button with timer
                _closeButton.gameObject.SetActive(true);
                _closeButton.StartTimer(ImageCloseDelay, () =>
                {
                    _canClose = true;
                });
            }
        }

        private void ShowImageAd()
        {
            Debug.Log($"[ZeyWinAds] Loading interstitial image: {AdData.media_url}");

            // Create image display
            var imageDisplay = _canvas.CreateImageDisplay(_adContainer.transform, AdData.media_url);
            imageDisplay.name = "AdImage";

            // Set to fill screen
            var rectTransform = imageDisplay.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;
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
            Debug.Log($"[ZeyWinAds] Loading interstitial video: {AdData.media_url}");

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
            Debug.Log($"[ZeyWinAds] Interstitial video prepared, skip_after={_skipAfterSeconds}s");

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
            Debug.Log("[ZeyWinAds] Skip now available");
            _canClose = true;
            _closeButton.SetInteractable(true);
            _closeButton.SetText("\u00D7");
        }

        private void OnVideoComplete()
        {
            Debug.Log("[ZeyWinAds] Interstitial video completed");

            _canClose = true;
            _closeButton.gameObject.SetActive(true);
            _closeButton.SetInteractable(true);
            _closeButton.SetText("\u00D7");

            // Track completion for video ads
            TrackComplete();
        }

        private void OnVideoError(string error)
        {
            Debug.LogWarning($"[ZeyWinAds] Interstitial video error: {error}");

            // Allow closing on error
            _canClose = true;
            _closeButton.gameObject.SetActive(true);
            _closeButton.SetInteractable(true);
            _closeButton.SetText("\u00D7");
        }

        private void OnAdClicked()
        {
            Debug.Log("[ZeyWinAds] Interstitial ad clicked");
            OpenClickUrl();
        }

        private void OnCloseButtonClicked()
        {
            if (!_canClose)
            {
                Debug.Log("[ZeyWinAds] Cannot close yet - waiting for close delay");
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

            Debug.Log("[ZeyWinAds] Closing interstitial ad");

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
