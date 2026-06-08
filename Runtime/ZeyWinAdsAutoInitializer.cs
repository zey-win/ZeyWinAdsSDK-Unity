using System;
using System.Collections;
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

            var runner = new GameObject("ZeyWinAds Startup Overlay Handoff");
            UnityEngine.Object.DontDestroyOnLoad(runner);
            runner.hideFlags = HideFlags.HideAndDontSave;
            runner.AddComponent<StartupSequenceRunner>();
        }

        private sealed class StartupSequenceRunner : MonoBehaviour
        {
            private IEnumerator Start()
            {
                yield return null;
#if UNITY_ANDROID && !UNITY_EDITOR
                TryDismiss();
#endif
                Destroy(gameObject);
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            private static void TryDismiss()
            {
                try
                {
                    using (var startupOverlay = new AndroidJavaClass("com.zeywinads.unity.ZeyWinAdsStartupOverlay"))
                    {
                        startupOverlay.CallStatic("dismissWhenUnityReady");
                    }
                }
                catch (Exception e)
                {
                    Core.Logger.Debug("Startup overlay dismiss bridge unavailable: {0}", e.Message);
                }
            }
#endif
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
