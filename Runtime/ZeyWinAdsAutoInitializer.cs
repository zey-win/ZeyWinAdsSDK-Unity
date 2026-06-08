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
        private static void ApplyLowStartupQuality()
        {
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
            Core.NotificationPopupSuppressor.SeedLegacyPrefs();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void StartBeforeFirstSceneLoad()
        {
            if (_startupSequenceScheduled)
                return;

            _startupSequenceScheduled = true;
            Core.NotificationPopupSuppressor.StartEarly();
            TryInitialize();
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
