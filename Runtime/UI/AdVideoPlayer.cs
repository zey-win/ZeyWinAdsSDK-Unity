using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;

namespace ZeyWinAds.UI
{
    /// <summary>
    /// Video player wrapper for ad playback.
    /// Downloads video to cache first to handle videos with moov atom at end.
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
        /// Called during download with progress (0-1)
        /// </summary>
        public event Action<float> OnDownloadProgress;

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
        private GameObject _imageContainer;
        private RawImage _renderImage;
        private RenderTexture _renderTexture;
        private AudioSource _audioSource;
        private Coroutine _progressCoroutine;
        private Coroutine _downloadCoroutine;
        private bool _hasCompleted;
        private string _cachedVideoPath;
        private static string CacheDirectory => Path.Combine(Application.temporaryCachePath, "ZeyWinAds", "VideoCache");

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
            _videoPlayer.aspectRatio = VideoAspectRatio.NoScaling; // We handle aspect ratio manually
            _videoPlayer.source = VideoSource.Url;

            // Setup audio
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _videoPlayer.SetTargetAudioSource(0, _audioSource);

            // Add mask to this container to clip overflow
            gameObject.AddComponent<RectMask2D>();

            // Create child image that will be sized for aspect fill
            _imageContainer = new GameObject("VideoImage");
            _imageContainer.transform.SetParent(transform, false);

            var imageRect = _imageContainer.AddComponent<RectTransform>();
            imageRect.anchorMin = new Vector2(0.5f, 0.5f);
            imageRect.anchorMax = new Vector2(0.5f, 0.5f);
            imageRect.pivot = new Vector2(0.5f, 0.5f);
            imageRect.anchoredPosition = Vector2.zero;

            _renderImage = _imageContainer.AddComponent<RawImage>();
            _renderImage.color = Color.clear;

            // Subscribe to events
            _videoPlayer.prepareCompleted += OnPrepareCompleted;
            _videoPlayer.loopPointReached += OnLoopPointReached;
            _videoPlayer.errorReceived += OnErrorReceived;
        }

        /// <summary>
        /// Plays a video from URL. Downloads to cache first for reliable playback.
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

            // Check if video is already cached
            string cachedPath = GetCachedPath(url);
            if (File.Exists(cachedPath))
            {
                Debug.Log($"[ZeyWinAds] Playing from cache: {cachedPath}");
                PlayFromFile(cachedPath);
            }
            else
            {
                // Download video first
                _downloadCoroutine = StartCoroutine(DownloadAndPlayCoroutine(url, cachedPath));
            }
        }

        private string GetCachedPath(string url)
        {
            // Create hash of URL for cache filename
            string hash = url.GetHashCode().ToString("X8");
            string extension = Path.GetExtension(new Uri(url).AbsolutePath);
            if (string.IsNullOrEmpty(extension))
                extension = ".mp4";
            return Path.Combine(CacheDirectory, hash + extension);
        }

        private IEnumerator DownloadAndPlayCoroutine(string url, string cachePath)
        {
            Debug.Log($"[ZeyWinAds] Downloading video: {url}");

            // Ensure cache directory exists
            try
            {
                Directory.CreateDirectory(CacheDirectory);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ZeyWinAds] Failed to create cache directory: {e.Message}");
                OnVideoError?.Invoke($"Failed to create cache: {e.Message}");
                yield break;
            }

            using (var request = UnityWebRequest.Get(url))
            {
                // Use DownloadHandlerFile to save directly to disk (memory efficient)
                request.downloadHandler = new DownloadHandlerFile(cachePath);

                var operation = request.SendWebRequest();

                // Track download progress
                while (!operation.isDone)
                {
                    OnDownloadProgress?.Invoke(request.downloadProgress);
                    yield return null;
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[ZeyWinAds] Failed to download video: {request.error}");

                    // Clean up partial download
                    try
                    {
                        if (File.Exists(cachePath))
                            File.Delete(cachePath);
                    }
                    catch { }

                    OnVideoError?.Invoke($"Download failed: {request.error}");
                    yield break;
                }

                Debug.Log($"[ZeyWinAds] Video downloaded to: {cachePath}");
            }

            // Play from downloaded file
            PlayFromFile(cachePath);
        }

        private void PlayFromFile(string filePath)
        {
            _cachedVideoPath = filePath;
            _videoPlayer.url = "file://" + filePath;
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
            if (_downloadCoroutine != null)
            {
                StopCoroutine(_downloadCoroutine);
                _downloadCoroutine = null;
            }

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
            Debug.Log($"[ZeyWinAds] Video prepared: {source.width}x{source.height}, starting playback");

            // Create render texture at video's native resolution
            int videoWidth = (int)source.width;
            int videoHeight = (int)source.height;
            _renderTexture = new RenderTexture(videoWidth, videoHeight, 0);
            _videoPlayer.targetTexture = _renderTexture;
            _renderImage.texture = _renderTexture;

            // Apply aspect fill scaling
            ApplyAspectFill();

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

        private void ApplyAspectFill()
        {
            if (_videoPlayer == null || _imageContainer == null)
                return;

            var containerRect = GetComponent<RectTransform>();
            var imageRect = _imageContainer.GetComponent<RectTransform>();

            if (containerRect == null || imageRect == null)
                return;

            // Get container size (screen size)
            float containerWidth = containerRect.rect.width > 0 ? containerRect.rect.width : Screen.width;
            float containerHeight = containerRect.rect.height > 0 ? containerRect.rect.height : Screen.height;

            // Get video dimensions
            float videoWidth = _videoPlayer.width;
            float videoHeight = _videoPlayer.height;

            if (videoWidth <= 0 || videoHeight <= 0)
                return;

            float videoAspect = videoWidth / videoHeight;
            float containerAspect = containerWidth / containerHeight;

            float width, height;

            // Aspect Fill: scale to cover container completely (crop edges)
            if (videoAspect > containerAspect)
            {
                // Video is wider - match height, overflow width
                height = containerHeight;
                width = height * videoAspect;
            }
            else
            {
                // Video is taller - match width, overflow height
                width = containerWidth;
                height = width / videoAspect;
            }

            imageRect.sizeDelta = new Vector2(width, height);
            Debug.Log($"[ZeyWinAds] Video aspect fill: container={containerWidth}x{containerHeight}, video={videoWidth}x{videoHeight}, result={width}x{height}");
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
            // Cleanup coroutines
            if (_downloadCoroutine != null)
            {
                StopCoroutine(_downloadCoroutine);
            }

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
            OnDownloadProgress = null;
        }

        /// <summary>
        /// Clears the video cache directory
        /// </summary>
        public static void ClearCache()
        {
            try
            {
                if (Directory.Exists(CacheDirectory))
                {
                    Directory.Delete(CacheDirectory, true);
                    Debug.Log("[ZeyWinAds] Video cache cleared");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ZeyWinAds] Failed to clear video cache: {e.Message}");
            }
        }

        /// <summary>
        /// Gets the total size of cached videos in bytes
        /// </summary>
        public static long GetCacheSize()
        {
            try
            {
                if (!Directory.Exists(CacheDirectory))
                    return 0;

                long size = 0;
                foreach (var file in Directory.GetFiles(CacheDirectory))
                {
                    size += new FileInfo(file).Length;
                }
                return size;
            }
            catch
            {
                return 0;
            }
        }
    }
}
