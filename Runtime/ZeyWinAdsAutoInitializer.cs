using System;
using System.Collections;
using UnityEngine;

namespace ZeyWinAds
{
    /// <summary>
    /// Starts the SDK from Resources/ZeyWinAdsSettings before the first scene loads.
    /// </summary>
    internal static class ZeyWinAdsAutoInitializer
    {
        private static bool _startupOverlayDismissScheduled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void DismissAndroidStartupOverlayAfterFirstScene()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_startupOverlayDismissScheduled)
                return;

            _startupOverlayDismissScheduled = true;

            var runner = new GameObject("ZeyWinAds Startup Overlay Dismisser");
            UnityEngine.Object.DontDestroyOnLoad(runner);
            runner.hideFlags = HideFlags.HideAndDontSave;
            runner.AddComponent<StartupOverlayDismissRunner>();
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class StartupOverlayDismissRunner : MonoBehaviour
        {
            private IEnumerator Start()
            {
                yield return null;
                TryDismiss();
                Destroy(gameObject);
            }

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
        }
#endif
    }
}
