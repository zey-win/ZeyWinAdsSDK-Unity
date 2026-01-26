using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace ZeyWinAds.UI
{
    /// <summary>
    /// Video player wrapper for ad playback.
    /// Uses Unity's VideoPlayer component for video rendering.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class AdVideoPlayer : MonoBehaviour
    {
        /// <summary>
        /// Called when video finishes playing
        /// </summary>
        public event Action OnVideoComplete;

        /// <summary>
        /// Called when video encounters an error
        /// </summary>
        public event Action<string> OnVideoError;

        /// <summary>
        /// Called periodically with progress (0-1) and duration in seconds
        /// </summary>
        public event Action<float, float> OnVideoProgress;

        /// <summary>
        /// Called when video is prepared and ready to play
        /// </summary>
        public event Action OnVideoPrepared;

        /// <summary>
        /// Whether the video is currently playing
        /// </summary>
        public bool IsPlaying => _videoPlayer != null && _videoPlayer.isPlaying;

        /// <summary>
        /// Whether the video is prepared and ready
        /// </summary>
        public bool IsPrepared => _videoPlayer != null && _videoPlayer.isPrepared;

        /// <summary>
        /// Current playback progress (0-1)
        /// </summary>
        public float Progress
        {
            get
            {
                if (_videoPlayer == null || _videoPlayer.frameCount == 0)
                    return 0f;
                return (float)_videoPlayer.frame / (float)_videoPlayer.frameCount;
            }
        }

        /// <summary>
        /// Total video duration in seconds
        /// </summary>
        public float Duration => _videoPlayer != null ? (float)_videoPlayer.length : 0f;

        /// <summary>
        /// Current playback time in seconds
        /// </summary>
        public float CurrentTime => _videoPlayer != null ? (float)_videoPlayer.time : 0f;

        private VideoPlayer _videoPlayer;
        private RawImage _renderImage;
        private RenderTexture _renderTexture;
        private AudioSource _audioSource;
        private Coroutine _progressCoroutine;
        private bool _hasCompleted;

        private void Awake()
        {
            SetupComponents();
        }

        private void SetupComponents()
        {
            // Create video player
            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.aspectRatio = VideoAspectRatio.FitOutside; // Scale to fill (crop edges)
            _videoPlayer.source = VideoSource.Url;

            // Setup audio
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _videoPlayer.SetTargetAudioSource(0, _audioSource);

            // Create render image (transparent until video is ready)
            _renderImage = gameObject.AddComponent<RawImage>();
            _renderImage.color = Color.clear;

            // Subscribe to events
            _videoPlayer.prepareCompleted += OnPrepareCompleted;
            _videoPlayer.loopPointReached += OnLoopPointReached;
            _videoPlayer.errorReceived += OnErrorReceived;
        }

        /// <summary>
        /// Plays a video from URL
        /// </summary>
        /// <param name="url">URL of the video to play</param>
        public void Play(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                OnVideoError?.Invoke("Video URL is empty");
                return;
            }

            Debug.Log($"[ZeyWinAds] Playing video: {url}");

            _hasCompleted = false;

            // Setup render texture based on screen size
            int width = Mathf.Min(Screen.width, 1920);
            int height = Mathf.Min(Screen.height, 1080);
            _renderTexture = new RenderTexture(width, height, 0);
            _videoPlayer.targetTexture = _renderTexture;
            _renderImage.texture = _renderTexture;

            // Set URL and prepare
            _videoPlayer.url = url;
            _videoPlayer.Prepare();
        }

        /// <summary>
        /// Pauses video playback
        /// </summary>
        public void Pause()
        {
            if (_videoPlayer != null && _videoPlayer.isPlaying)
            {
                _videoPlayer.Pause();
            }
        }

        /// <summary>
        /// Resumes video playback
        /// </summary>
        public void Resume()
        {
            if (_videoPlayer != null && _videoPlayer.isPrepared && !_videoPlayer.isPlaying)
            {
                _videoPlayer.Play();
            }
        }

        /// <summary>
        /// Stops video playback
        /// </summary>
        public void Stop()
        {
            if (_progressCoroutine != null)
            {
                StopCoroutine(_progressCoroutine);
                _progressCoroutine = null;
            }

            if (_videoPlayer != null)
            {
                _videoPlayer.Stop();
            }
        }

        /// <summary>
        /// Seeks to a specific time in the video
        /// </summary>
        /// <param name="time">Time in seconds</param>
        public void Seek(float time)
        {
            if (_videoPlayer != null && _videoPlayer.isPrepared)
            {
                _videoPlayer.time = time;
            }
        }

        /// <summary>
        /// Sets the volume (0-1)
        /// </summary>
        public void SetVolume(float volume)
        {
            if (_audioSource != null)
            {
                _audioSource.volume = Mathf.Clamp01(volume);
            }
        }

        /// <summary>
        /// Mutes or unmutes the video
        /// </summary>
        public void SetMuted(bool muted)
        {
            if (_audioSource != null)
            {
                _audioSource.mute = muted;
            }
        }

        private void OnPrepareCompleted(VideoPlayer source)
        {
            Debug.Log("[ZeyWinAds] Video prepared, starting playback");

            // Make render image visible now that video is ready
            if (_renderImage != null)
            {
                _renderImage.color = Color.white;
            }

            OnVideoPrepared?.Invoke();

            // Start playback
            _videoPlayer.Play();

            // Start progress tracking
            if (_progressCoroutine != null)
            {
                StopCoroutine(_progressCoroutine);
            }
            _progressCoroutine = StartCoroutine(TrackProgressCoroutine());
        }

        private void OnLoopPointReached(VideoPlayer source)
        {
            if (_hasCompleted)
                return;

            _hasCompleted = true;
            Debug.Log("[ZeyWinAds] Video playback complete");

            if (_progressCoroutine != null)
            {
                StopCoroutine(_progressCoroutine);
                _progressCoroutine = null;
            }

            OnVideoComplete?.Invoke();
        }

        private void OnErrorReceived(VideoPlayer source, string message)
        {
            Debug.LogError($"[ZeyWinAds] Video error: {message}");

            if (_progressCoroutine != null)
            {
                StopCoroutine(_progressCoroutine);
                _progressCoroutine = null;
            }

            OnVideoError?.Invoke(message);
        }

        private IEnumerator TrackProgressCoroutine()
        {
            while (_videoPlayer != null && _videoPlayer.isPlaying)
            {
                float progress = Progress;
                float duration = Duration;

                OnVideoProgress?.Invoke(progress, duration);

                yield return new WaitForSeconds(0.25f);
            }
        }

        private void OnDestroy()
        {
            // Cleanup
            if (_progressCoroutine != null)
            {
                StopCoroutine(_progressCoroutine);
            }

            if (_videoPlayer != null)
            {
                _videoPlayer.prepareCompleted -= OnPrepareCompleted;
                _videoPlayer.loopPointReached -= OnLoopPointReached;
                _videoPlayer.errorReceived -= OnErrorReceived;
                _videoPlayer.Stop();
            }

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }

            // Clear events
            OnVideoComplete = null;
            OnVideoError = null;
            OnVideoProgress = null;
            OnVideoPrepared = null;
        }
    }
}
