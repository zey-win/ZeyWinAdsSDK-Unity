using System;
using System.Globalization;
using UnityEngine;

#if ZEYWIN_ADMOB
using GoogleMobileAds.Api;
#endif

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Keeps ad audio at a comfortable level without changing the user's device volume.
    /// </summary>
    internal static class AdAudioController
    {
        private const int DefaultAdMobVolumePercent = 35;
        private const int DefaultUnityDuckPercent = 45;

        private static int _duckDepth;
        private static bool _hasSavedUnityVolume;
        private static float _savedUnityVolume = 1f;
        private static float _lastAppliedAdMobVolume = -1f;

        public static void ApplyAdMobVolume(string reason)
        {
#if ZEYWIN_ADMOB
            float volume = ResolveAdMobVolume();
            if (Mathf.Approximately(_lastAppliedAdMobVolume, volume))
                return;

            try
            {
                MobileAds.SetApplicationVolume(volume);
                MobileAds.SetApplicationMuted(volume <= 0.001f);
                _lastAppliedAdMobVolume = volume;
                Logger.Log("Громкость AdMob применена: admob={0:0.00}, причина={1}", volume, SafeReason(reason));
            }
            catch (Exception e)
            {
                Logger.Warn("Не удалось применить громкость AdMob: {0}", e.Message);
            }
#endif
        }

        public static void BeginAdAudio(string reason, bool duckUnityAudio = true)
        {
            ApplyAdMobVolume(reason);

            if (!duckUnityAudio)
                return;

            _duckDepth++;
            if (_duckDepth > 1)
            {
                Logger.Debug("Глубина приглушения рекламы увеличена: {0}, причина={1}", _duckDepth, SafeReason(reason));
                return;
            }

            _savedUnityVolume = AudioListener.volume;
            _hasSavedUnityVolume = true;

            float targetVolume = Mathf.Min(_savedUnityVolume, ResolveUnityDuckVolume());
            AudioListener.volume = targetVolume;
            Logger.Log("Приглушение звука игры для рекламы началось: unity={0:0.00}->{1:0.00}, причина={2}",
                _savedUnityVolume,
                targetVolume,
                SafeReason(reason));
        }

        public static void EndAdAudio(string reason)
        {
            if (_duckDepth <= 0)
                return;

            _duckDepth--;
            if (_duckDepth > 0)
            {
                Logger.Debug("Глубина приглушения рекламы уменьшена: {0}, причина={1}", _duckDepth, SafeReason(reason));
                return;
            }

            if (_hasSavedUnityVolume)
            {
                AudioListener.volume = _savedUnityVolume;
                Logger.Log("Приглушение звука игры для рекламы закончено: unity восстановлен={0:0.00}, причина={1}",
                    _savedUnityVolume,
                    SafeReason(reason));
            }

            _hasSavedUnityVolume = false;
        }

        public static void Reset()
        {
            if (_hasSavedUnityVolume)
                AudioListener.volume = _savedUnityVolume;

            _duckDepth = 0;
            _hasSavedUnityVolume = false;
            _lastAppliedAdMobVolume = -1f;
            ApplyAdMobVolume("reset");
        }

        public static string WebMediaVolumeJavascript()
        {
            string volume = ResolveWebMediaVolume().ToString("0.###", CultureInfo.InvariantCulture);
            return @"(function(){
  try {
    var zeywinVolume = " + volume + @";
    var apply = function() {
      var media = document.querySelectorAll('video,audio');
      for (var i = 0; i < media.length; i++) {
        try { media[i].volume = zeywinVolume; } catch (e) {}
      }
    };
    apply();
    if (!window.__zeywinAdAudioLimiter) {
      window.__zeywinAdAudioLimiter = true;
      window.setInterval(apply, 1500);
    }
  } catch (e) {}
})();";
        }

        public static void ApplyUniWebViewMediaVolume(UniWebView webView, string reason)
        {
#if !UNITY_EDITOR
            if (webView == null)
                return;

            try
            {
                webView.EvaluateJavaScript(WebMediaVolumeJavascript());
                Logger.Debug("Громкость media в WebView применена через UniWebView: {0}", SafeReason(reason));
            }
            catch (Exception e)
            {
                Logger.Warn("Не удалось применить громкость media в UniWebView: {0}", e.Message);
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        public static void ApplyAndroidWebViewMediaVolume(AndroidJavaObject webView, string reason)
        {
            if (webView == null)
                return;

            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                    activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                    {
                        try
                        {
                            webView.Call("evaluateJavascript", WebMediaVolumeJavascript(), (AndroidJavaObject)null);
                            Logger.Debug("Громкость media в Android WebView применена: {0}", SafeReason(reason));
                        }
                        catch (Exception e)
                        {
                            Logger.Warn("Не удалось выполнить JS громкости media в Android WebView: {0}", e.Message);
                        }
                    }));
                }
            }
            catch (Exception e)
            {
                Logger.Warn("Не удалось применить громкость media в Android WebView: {0}", e.Message);
            }
        }
#endif

        private static float ResolveAdMobVolume()
        {
            return ResolvePercent("zeywin_admob_audio_volume_percent", DefaultAdMobVolumePercent);
        }

        private static float ResolveUnityDuckVolume()
        {
            return ResolvePercent("zeywin_ad_unity_duck_volume_percent", DefaultUnityDuckPercent);
        }

        private static float ResolveWebMediaVolume()
        {
            int fallback = Mathf.RoundToInt(ResolveAdMobVolume() * 100f);
            return ResolvePercent("zeywin_webview_media_volume_percent", fallback);
        }

        private static float ResolvePercent(string key, int fallback)
        {
            int configured = RemoteConfigBridge.GetInt(key, fallback);
            return Mathf.Clamp(configured, 0, 100) / 100f;
        }

        private static string SafeReason(string reason)
        {
            return string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }
    }
}
