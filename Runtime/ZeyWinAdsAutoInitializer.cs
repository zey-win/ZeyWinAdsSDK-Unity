using UnityEngine;

namespace ZeyWinAds
{
    /// <summary>
    /// Starts the SDK from Resources/ZeyWinAdsSettings before the game scene
    /// loads so ZeyWin checks, sticky offers, and WebView locks stay ahead of
    /// game UI and secondary ad networks.
    /// </summary>
    internal static class ZeyWinAdsAutoInitializer
    {
        private static bool _startupSequenceScheduled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void PreloadNativeClasses()
        {
            // Forces ART to resolve/verify our own native helper classes as early as possible, so
            // WebViewLock's later `new AndroidJavaObject("com.zeywinads.unity.*")` construction
            // (once a WebView lock is actually shown) doesn't hit the empty-reflective-walk
            // cold-start abort. Unconditional (not gated by autoInitializeOnStartup) since it's a
            // cheap, side-effect-free touch, not real initialization.
#if UNITY_ANDROID && !UNITY_EDITOR
            Core.AndroidJniSafe.PreloadClasses(
                "com.zeywinads.unity.ZeyWinAdsWebChromeClient",
                "com.zeywinads.unity.ZeyWinAdsPermissionBridge",
                "com.zeywinads.unity.ZeyWinAdsLockWebViewClient",
                "com.zeywinads.unity.ZeyWinAdsSafeAreaFrameLayout",
                // AdMob's own MobileAds.Initialize/UnityMobileAds bridge is third-party
                // reflection we can't reroute, but a raw FindClass touch this early can still
                // help it resolve cleanly later — this is the historically documented
                // zw_init_stage=admob crash point.
                "com.google.android.gms.ads.MobileAds",
                "com.google.unity.ads.UnityMobileAds");
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyLowStartupQuality()
        {
            if (!IsAutoInitializeEnabled())
                return;

            var names = QualitySettings.names;
            var lowIndex = 0;

            if (names != null)
            {
                for (var i = 0; i < names.Length; i++)
                {
                    if (string.Equals(names[i], "Low", System.StringComparison.OrdinalIgnoreCase))
                    {
                        lowIndex = i;
                        break;
                    }
                }
            }

            if (QualitySettings.GetQualityLevel() != lowIndex)
                QualitySettings.SetQualityLevel(lowIndex, true);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void SeedLegacyNotificationPopupState()
        {
            if (!IsAutoInitializeEnabled())
                return;

            Core.NotificationPopupSuppressor.SeedLegacyPrefs();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void StartBeforeFirstSceneLoad()
        {
            if (_startupSequenceScheduled)
                return;

            _startupSequenceScheduled = true;

            if (!IsAutoInitializeEnabled())
                return;

            Core.NotificationPopupSuppressor.StartEarly();
            TryInitialize();
        }

        private static bool IsAutoInitializeEnabled()
        {
            var settings = ZeyWinAdsSettings.Load();
            return settings != null && settings.autoInitializeOnStartup;
        }

        private static void TryInitialize()
        {
            var settings = ZeyWinAdsSettings.Load();
            if (settings == null || !settings.autoInitializeOnStartup)
                return;

            if (string.IsNullOrEmpty(settings.apiKey))
            {
                Core.Logger.Warn("Auto initialize is enabled but ZeyWin API key is empty.");
                return;
            }

            ZeyWinAds.Initialize(settings.apiKey);
        }
    }
}
