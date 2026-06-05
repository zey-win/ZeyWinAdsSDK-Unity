using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ZeyWinAds.Ads;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Configuration for ad preloading behavior.
    /// </summary>
    [Serializable]
    public class PreloadSettings
    {
        /// <summary>
        /// Whether to preload ads on SDK initialization.
        /// </summary>
        public bool preloadOnInitialize = true;

        /// <summary>
        /// Whether to automatically reload ads after they are shown.
        /// </summary>
        public bool autoReloadAfterShow = true;

        /// <summary>
        /// Whether to preload interstitial ads.
        /// </summary>
        public bool preloadInterstitial = true;

        /// <summary>
        /// Whether to preload rewarded ads.
        /// </summary>
        public bool preloadRewarded = true;

        /// <summary>
        /// Whether to preload banner ads.
        /// </summary>
        public bool preloadBanner = true;

        /// <summary>
        /// Whether to preload native ads.
        /// </summary>
        public bool preloadNative = true;

        /// <summary>
        /// Whether to preload popup ads.
        /// </summary>
        public bool preloadPopup = true;

        /// <summary>
        /// Delay in seconds before starting preload after initialization.
        /// </summary>
        public float preloadDelaySeconds = 0.0f;

        /// <summary>
        /// Delay in seconds before reloading after an ad is shown.
        /// </summary>
        public float reloadDelaySeconds = 1.0f;

        /// <summary>
        /// Maximum number of retry attempts for failed preloads.
        /// </summary>
        public int maxRetryAttempts = 2;

        /// <summary>
        /// Delay in seconds between retry attempts.
        /// </summary>
        public float retryDelaySeconds = 30.0f;
    }

    /// <summary>
    /// Manages ad preloading and caching for instant ad display.
    /// Singleton MonoBehaviour that handles background loading of ads.
    /// </summary>
    public class AdLoader : MonoBehaviour
    {
        private static AdLoader _instance;
        private static readonly object _lock = new object();

        /// <summary>
        /// Gets the singleton instance of the AdLoader.
        /// Creates one if it doesn't exist.
        /// </summary>
        public static AdLoader Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            GameObject go = new GameObject("ZeyWinAdsLoader");
                            _instance = go.AddComponent<AdLoader>();
                            DontDestroyOnLoad(go);
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// The ad cache for storing preloaded ads.
        /// </summary>
        public AdCache Cache { get; private set; }

        /// <summary>
        /// The preload settings configuration.
        /// </summary>
        public PreloadSettings Settings { get; private set; }

        // Track which ad types are currently loading
        private readonly HashSet<AdType> _loadingTypes = new HashSet<AdType>();

        // Track retry counts for failed loads
        private readonly Dictionary<AdType, int> _retryCounts = new Dictionary<AdType, int>();

        // Prevent older game loops from immediately re-requesting a failed ad type.
        private readonly Dictionary<AdType, float> _nextAllowedPreloadAt = new Dictionary<AdType, float>();

        // Events
        public event Action<AdType> OnAdPreloaded;
        public event Action<AdType, string> OnPreloadFailed;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            Cache = new AdCache();
            Settings = new PreloadSettings();

            Logger.Debug("AdLoader initialized");
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                Cache?.Clear();
                _instance = null;
            }
        }

        /// <summary>
        /// Configures the preload settings.
        /// </summary>
        /// <param name="settings">The settings to apply</param>
        public void Configure(PreloadSettings settings)
        {
            if (settings != null)
            {
                Settings = settings;
                Logger.Debug("AdLoader configured with custom settings");
            }
        }

        /// <summary>
        /// Called when the SDK is initialized. Starts eager loading of all ad types.
        /// </summary>
        public void OnSDKInitialize()
        {
            if (!Settings.preloadOnInitialize)
            {
                Logger.Debug("Preload on initialize is disabled");
                return;
            }

            Logger.Log("Starting ad preload on SDK initialization");
            StartCoroutine(PreloadAllWithDelay(Settings.preloadDelaySeconds));
        }

        /// <summary>
        /// Preloads all enabled ad types.
        /// </summary>
        public void PreloadAll()
        {
            Logger.Debug("Preloading all ad types");

            if (Settings.preloadInterstitial)
            {
                PreloadAd(AdType.Interstitial);
            }

            if (Settings.preloadRewarded)
            {
                PreloadAd(AdType.Rewarded);
            }

            if (Settings.preloadBanner)
            {
                PreloadAd(AdType.Banner);
            }

            if (Settings.preloadNative)
            {
                PreloadAd(AdType.Native);
            }

            if (Settings.preloadPopup)
            {
                PreloadAd(AdType.Popup);
            }
        }

        /// <summary>
        /// Preloads an ad of the specified type.
        /// </summary>
        /// <param name="type">The type of ad to preload</param>
        /// <param name="forceReload">Force reload even if already cached</param>
        public void PreloadAd(AdType type, bool forceReload = false)
        {
            // Check if already cached and not forcing reload
            if (!forceReload && Cache.Has(type))
            {
                Logger.Debug("{0} ad is already cached and ready", type);
                return;
            }

            // Check if already loading
            if (_loadingTypes.Contains(type))
            {
                Logger.Debug("{0} ad is already being loaded", type);
                return;
            }

            if (!forceReload && IsPreloadCoolingDown(type, out float cooldownRemainingSeconds))
            {
                Logger.Debug("{0} preload skipped for {1}s after recent failure",
                    type,
                    Mathf.CeilToInt(cooldownRemainingSeconds));
                return;
            }

            // Check if SDK is initialized
            if (!AdClient.Instance.IsInitialized)
            {
                Logger.Warn("Cannot preload {0} ad - SDK not initialized", type);
                return;
            }

            if (AdClient.Instance.IsAdRequestSuspended(out float remainingSeconds, out string reason))
            {
                Logger.Debug("{0} preload skipped; ZeyWin ad requests suspended for {1}s: {2}",
                    type,
                    Mathf.CeilToInt(remainingSeconds),
                    string.IsNullOrWhiteSpace(reason) ? "unknown" : reason);
                return;
            }

            _loadingTypes.Add(type);
            StartCoroutine(PreloadAdCoroutine(type));
        }

        /// <summary>
        /// Called when an ad has been shown. Triggers automatic reload if enabled.
        /// </summary>
        /// <param name="type">The type of ad that was shown</param>
        public void OnAdShown(AdType type)
        {
            Logger.Debug("{0} ad shown, triggering preload", type);

            // Reset retry count on successful show
            _retryCounts[type] = 0;
            _nextAllowedPreloadAt.Remove(type);

            if (Settings.autoReloadAfterShow)
            {
                StartCoroutine(ReloadWithDelay(type, Settings.reloadDelaySeconds));
            }
        }

        /// <summary>
        /// Gets a preloaded ad of the specified type.
        /// Returns null if no ad is cached.
        /// </summary>
        /// <param name="type">The type of ad to get</param>
        /// <returns>The preloaded ad or null</returns>
        public BaseAd GetPreloadedAd(AdType type)
        {
            return Cache.GetAndRemove(type);
        }

        /// <summary>
        /// Checks if an ad of the specified type is preloaded and ready.
        /// </summary>
        /// <param name="type">The type of ad to check</param>
        /// <returns>True if a preloaded ad is available</returns>
        public bool IsAdReady(AdType type)
        {
            return Cache.Has(type);
        }

        /// <summary>
        /// Checks if an ad of the specified type is currently loading.
        /// </summary>
        /// <param name="type">The type of ad to check</param>
        /// <returns>True if the ad is loading</returns>
        public bool IsLoading(AdType type)
        {
            return _loadingTypes.Contains(type);
        }

        /// <summary>
        /// Clears all cached ads.
        /// </summary>
        public void ClearCache()
        {
            Cache.Clear();
            _retryCounts.Clear();
            _nextAllowedPreloadAt.Clear();
            Logger.Debug("Ad cache cleared");
        }

        private IEnumerator PreloadAllWithDelay(float delay)
        {
            if (delay > 0)
            {
                yield return new WaitForSeconds(delay);
            }

            PreloadAll();
        }

        private IEnumerator ReloadWithDelay(float delay, AdType type)
        {
            if (delay > 0)
            {
                yield return new WaitForSeconds(delay);
            }

            PreloadAd(type);
        }

        private IEnumerator ReloadWithDelay(AdType type, float delay)
        {
            if (delay > 0)
            {
                yield return new WaitForSeconds(delay);
            }

            PreloadAd(type);
        }

        private IEnumerator PreloadAdCoroutine(AdType type)
        {
            Logger.Log("Preloading {0} ad...", type);

            BaseAd ad = CreateAdInstance(type);
            if (ad == null)
            {
                Logger.Error("Failed to create ad instance for type: {0}", type);
                _loadingTypes.Remove(type);
                OnPreloadFailed?.Invoke(type, "Failed to create ad instance");
                yield break;
            }

            bool loadComplete = false;
            bool loadSuccess = false;

            ad.Load((success) =>
            {
                loadComplete = true;
                loadSuccess = success;
            });

            // Wait for load to complete
            while (!loadComplete)
            {
                yield return null;
            }

            _loadingTypes.Remove(type);

            if (loadSuccess)
            {
                if (AdClient.Instance.IsBlocked)
                {
                    ad.Destroy();
                    Logger.Warn("{0} ad loaded after device was blocked; dropping it", type);
                    yield break;
                }

                Cache.Store(type, ad);
                _retryCounts[type] = 0;
                _nextAllowedPreloadAt.Remove(type);
                Logger.Log("{0} ad preloaded successfully", type);
                OnAdPreloaded?.Invoke(type);
            }
            else
            {
                ad.Destroy();
                HandlePreloadFailure(type);
            }
        }

        private void HandlePreloadFailure(AdType type)
        {
            if (AdClient.Instance.IsAdRequestSuspended(out float remainingSeconds, out string reason))
            {
                Logger.Warn("Preload stopped for {0}; ZeyWin ad requests suspended for {1}s: {2}",
                    type,
                    Mathf.CeilToInt(remainingSeconds),
                    string.IsNullOrWhiteSpace(reason) ? "unknown" : reason);
                OnPreloadFailed?.Invoke(type, reason ?? "ZeyWin ad requests suspended");
                return;
            }

            if (!_retryCounts.ContainsKey(type))
            {
                _retryCounts[type] = 0;
            }

            _retryCounts[type]++;

            int maxRetryAttempts = GetMaxRetryAttempts();
            float retryDelaySeconds = GetRetryDelaySeconds();
            _nextAllowedPreloadAt[type] = Time.realtimeSinceStartup + retryDelaySeconds;

            if (_retryCounts[type] < maxRetryAttempts)
            {
                Logger.Warn("Preload failed for {0}, retrying in {1}s (attempt {2}/{3})",
                    type, retryDelaySeconds, _retryCounts[type], maxRetryAttempts);

                StartCoroutine(RetryPreload(type, retryDelaySeconds));
            }
            else
            {
                Logger.Error("Preload failed for {0} after {1} attempts", type, maxRetryAttempts);
                OnPreloadFailed?.Invoke(type, $"Failed after {maxRetryAttempts} attempts");
            }
        }

        private int GetMaxRetryAttempts()
        {
            int configured = RemoteConfigBridge.GetInt("zeywin_preload_max_retry_attempts", Settings.maxRetryAttempts);
            return Mathf.Clamp(configured, 1, 10);
        }

        private float GetRetryDelaySeconds()
        {
            int configured = RemoteConfigBridge.GetInt("zeywin_preload_retry_delay_seconds", Mathf.RoundToInt(Settings.retryDelaySeconds));
            return Mathf.Clamp(configured, 5, 300);
        }

        private bool IsPreloadCoolingDown(AdType type, out float remainingSeconds)
        {
            remainingSeconds = 0f;
            if (!_nextAllowedPreloadAt.TryGetValue(type, out float nextAllowedAt))
                return false;

            remainingSeconds = nextAllowedAt - Time.realtimeSinceStartup;
            if (remainingSeconds <= 0f)
            {
                _nextAllowedPreloadAt.Remove(type);
                remainingSeconds = 0f;
                return false;
            }

            return true;
        }

        private IEnumerator RetryPreload(AdType type, float delay)
        {
            yield return new WaitForSeconds(delay);
            PreloadAd(type);
        }

        private BaseAd CreateAdInstance(AdType type)
        {
            return type switch
            {
                AdType.Interstitial => new InterstitialAd(),
                AdType.Rewarded => new RewardedAd(),
                AdType.Banner => new BannerAd(),
                AdType.Native => new NativeAd(),
                AdType.Popup => new PopupAd(),
                _ => null
            };
        }
    }
}
