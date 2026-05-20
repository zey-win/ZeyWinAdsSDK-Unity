using UnityEngine;

namespace ZeyWinAds
{
    /// <summary>
    /// Starts the SDK from Resources/ZeyWinAdsSettings before the first scene loads.
    /// </summary>
    internal static class ZeyWinAdsAutoInitializer
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeBeforeSceneLoad()
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
