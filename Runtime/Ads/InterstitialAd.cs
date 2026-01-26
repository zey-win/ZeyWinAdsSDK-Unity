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

            // Display based on media type
            if (AdData.GetMediaType() == MediaType.Video)
            {
                ShowVideoAd();
            }
            else
            {
                ShowImageAd();
            }

            // Create close button
            _closeButton = _canvas.CreateCloseButton(OnCloseButtonClicked);

            if (AdData.GetMediaType() == MediaType.Image)
            {
                // For images, show close button with timer (button visible but shows countdown)
                _closeButton.gameObject.SetActive(true);
                _closeButton.StartTimer(ImageCloseDelay, () =>
                {
                    _canClose = true;
                });
            }
            else
            {
                // For videos, hide close button until video completes
                _closeButton.gameObject.SetActive(false);
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

        private void ShowVideoAd()
        {
            Debug.Log($"[ZeyWinAds] Loading interstitial video: {AdData.media_url}");

            // Create video player
            var videoObj = new GameObject("AdVideoPlayer");
            videoObj.transform.SetParent(_adContainer.transform, false);

            _videoPlayer = videoObj.AddComponent<AdVideoPlayer>();
            _videoPlayer.OnVideoComplete += OnVideoComplete;
            _videoPlayer.OnVideoError += OnVideoError;

            // Setup fullscreen video
            var rectTransform = videoObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;

            // Play video
            _videoPlayer.Play(AdData.media_url);
        }

        private void OnVideoComplete()
        {
            Debug.Log("[ZeyWinAds] Interstitial video completed");

            _canClose = true;
            _closeButton.gameObject.SetActive(true);

            // Track completion for video ads
            TrackComplete();
        }

        private void OnVideoError(string error)
        {
            Debug.LogWarning($"[ZeyWinAds] Interstitial video error: {error}");

            // Allow closing on error
            _canClose = true;
            _closeButton.gameObject.SetActive(true);
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
