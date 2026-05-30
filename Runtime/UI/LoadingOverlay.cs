using UnityEngine;
using UnityEngine.UI;

namespace ZeyWinAds.UI
{
    /// <summary>
    /// Fullscreen SDK-owned loading overlay shown while startup checks or SDK WebViews are loading.
    /// </summary>
    public class LoadingOverlay : MonoBehaviour
    {
        private const string GameObjectName = "ZeyWinAds_LoadingOverlay";
        private static readonly Color BackgroundColor = new Color(0.06f, 0.13f, 0.62f, 1f);
        private static LoadingOverlay _instance;

        private GameObject _root;
        private MoneyLoadingOverlayVisual _visual;
        private AudioSource _musicSource;
        private AudioClip _musicClip;
        private int _showCount;
        private float _musicStartedAt;

        public static void Show()
        {
            if (_instance == null)
            {
                var go = new GameObject(GameObjectName);
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<LoadingOverlay>();
            }

            _instance.ShowInternal();
        }

        public static void Hide()
        {
            if (_instance == null)
                return;

            _instance.HideInternal();
        }

        public static void ForceHide()
        {
            if (_instance == null)
                return;

            _instance._showCount = 0;
            _instance.SetVisible(false);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            Build();
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            UpdateLoadingMusic();
        }

        private void ShowInternal()
        {
            _showCount++;
            SetVisible(true);
        }

        private void HideInternal()
        {
            _showCount = Mathf.Max(0, _showCount - 1);
            if (_showCount == 0)
                SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (_root != null)
                _root.SetActive(visible);

            if (visible)
            {
                _visual?.Restart();
                StartLoadingMusic();
            }
            else
            {
                StopLoadingMusic();
            }
        }

        private void Build()
        {
            _root = new GameObject("Root");
            _root.transform.SetParent(transform, false);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32767;

            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _root.AddComponent<GraphicRaycaster>();

            var bg = new GameObject("Background");
            bg.transform.SetParent(_root.transform, false);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = BackgroundColor;
            bgImage.raycastTarget = true;

            _visual = bg.AddComponent<MoneyLoadingOverlayVisual>();
            _visual.Build(bgRect);

            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.playOnAwake = false;
            _musicSource.loop = true;
            _musicSource.ignoreListenerPause = true;
            _musicSource.spatialBlend = 0f;
            _musicSource.volume = 0f;
        }

        private void StartLoadingMusic()
        {
            if (_musicSource == null)
                return;

            if (_musicClip == null)
                _musicClip = CreateLoadingMusicClip();

            _musicSource.clip = _musicClip;
            _musicSource.volume = 0f;
            _musicStartedAt = Time.unscaledTime;
            if (!_musicSource.isPlaying)
                _musicSource.Play();
        }

        private void StopLoadingMusic()
        {
            if (_musicSource == null)
                return;

            _musicSource.Stop();
            _musicSource.volume = 0f;
        }

        private void UpdateLoadingMusic()
        {
            if (_musicSource == null || !_musicSource.isPlaying)
                return;

            float elapsed = Time.unscaledTime - _musicStartedAt;
            const float targetVolume = 0.16f;
            const float fadeInSeconds = 2f;
            const float fadeOutStartSeconds = 14f;
            const float fadeOutSeconds = 6f;

            if (elapsed < fadeInSeconds)
            {
                _musicSource.volume = Mathf.Lerp(0f, targetVolume, elapsed / fadeInSeconds);
            }
            else if (elapsed < fadeOutStartSeconds)
            {
                _musicSource.volume = targetVolume;
            }
            else
            {
                float t = Mathf.Clamp01((elapsed - fadeOutStartSeconds) / fadeOutSeconds);
                _musicSource.volume = Mathf.Lerp(targetVolume, 0f, t);
                if (t >= 1f)
                    StopLoadingMusic();
            }
        }

        private static AudioClip CreateLoadingMusicClip()
        {
            const int sampleRate = 22050;
            const int durationSeconds = 20;
            int sampleCount = sampleRate * durationSeconds;
            var data = new float[sampleCount];
            float[] notes = { 220f, 261.63f, 329.63f, 392f };

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                int chordIndex = Mathf.FloorToInt(t / 4f) % 4;
                float baseNote = notes[chordIndex];
                float value =
                    Mathf.Sin(2f * Mathf.PI * baseNote * t) * 0.22f +
                    Mathf.Sin(2f * Mathf.PI * baseNote * 1.5f * t) * 0.12f +
                    Mathf.Sin(2f * Mathf.PI * baseNote * 2f * t) * 0.08f;

                float pulse = 0.72f + 0.28f * Mathf.Sin(2f * Mathf.PI * 0.5f * t);
                data[i] = value * pulse * 0.18f;
            }

            var clip = AudioClip.Create("ZeyWinAds_LoadingMusic", sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
