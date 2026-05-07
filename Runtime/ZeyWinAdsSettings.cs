using UnityEngine;

namespace ZeyWinAds
{
    /// <summary>
    /// SDK configuration. Stored at Resources/ZeyWinAdsSettings.asset.
    /// Created and edited via Window > ZeyWinAds > Settings.
    /// Loaded at runtime via Resources.Load.
    /// </summary>
    public class ZeyWinAdsSettings : ScriptableObject
    {
        public const string ResourcePath = "ZeyWinAdsSettings";

        [Header("AdMob")]
        [Tooltip("Master switch. When disabled, AdMob fallback is never used.")]
        public bool enableAdMob = true;

        [Tooltip("AdMob App ID for Android (ca-app-pub-XXX~YYY). Auto-written into AndroidManifest at build.")]
        public string admobAppIdAndroid;

        [Tooltip("AdMob App ID for iOS (ca-app-pub-XXX~YYY). Auto-written into Info.plist at build.")]
        public string admobAppIdIOS;

        [Header("AdMob Ad Unit IDs — Android")]
        public string admobBannerAndroid;
        public string admobInterstitialAndroid;
        public string admobRewardedAndroid;

        [Header("AdMob Ad Unit IDs — iOS")]
        public string admobBannerIOS;
        public string admobInterstitialIOS;
        public string admobRewardedIOS;

        private static ZeyWinAdsSettings _instance;

        /// <summary>
        /// Loads the settings from Resources. Returns null if no asset exists.
        /// </summary>
        public static ZeyWinAdsSettings Load()
        {
            if (_instance != null)
                return _instance;

            _instance = Resources.Load<ZeyWinAdsSettings>(ResourcePath);
            return _instance;
        }

        /// <summary>
        /// Platform-aware AdMob App ID for the current runtime.
        /// </summary>
        public string GetAppId()
        {
#if UNITY_ANDROID
            return admobAppIdAndroid;
#elif UNITY_IOS
            return admobAppIdIOS;
#else
            return null;
#endif
        }

        public string GetBannerUnitId()
        {
#if UNITY_ANDROID
            return admobBannerAndroid;
#elif UNITY_IOS
            return admobBannerIOS;
#else
            return null;
#endif
        }

        public string GetInterstitialUnitId()
        {
#if UNITY_ANDROID
            return admobInterstitialAndroid;
#elif UNITY_IOS
            return admobInterstitialIOS;
#else
            return null;
#endif
        }

        public string GetRewardedUnitId()
        {
#if UNITY_ANDROID
            return admobRewardedAndroid;
#elif UNITY_IOS
            return admobRewardedIOS;
#else
            return null;
#endif
        }

        /// <summary>
        /// True if AdMob is enabled and at least the App ID for the current platform is set.
        /// </summary>
        public bool IsAdMobConfigured()
        {
            return enableAdMob && !string.IsNullOrEmpty(GetAppId());
        }
    }
}
