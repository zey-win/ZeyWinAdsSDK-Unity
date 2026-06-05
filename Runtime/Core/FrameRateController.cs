using System;
using UnityEngine;

namespace ZeyWinAds.Core
{
    internal static class FrameRateController
    {
        private const int DefaultTargetFps = 60;
        private static int _lastAppliedFps = -1;

        public static void Apply(string reason)
        {
            if (!RemoteConfigBridge.GetBool("zeywin_high_fps_enabled", true))
                return;

            int target = ResolveTargetFrameRate("zeywin_target_frame_rate", DefaultTargetFps);
            ApplyTargetFrameRate(target, reason);
        }

        public static void ApplyWebView(string reason)
        {
            if (!RemoteConfigBridge.GetBool("zeywin_high_fps_enabled", true))
                return;

            int fallback = ResolveTargetFrameRate("zeywin_target_frame_rate", DefaultTargetFps);
            int target = ResolveTargetFrameRate("zeywin_webview_target_frame_rate", fallback);
            ApplyTargetFrameRate(target, reason);
        }

        private static void ApplyTargetFrameRate(int targetFps, string reason)
        {
            targetFps = Mathf.Clamp(targetFps, 30, 120);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFps;
            PerceivedSmoothnessEffect.EnsureInstalled();

            if (_lastAppliedFps != targetFps)
            {
                _lastAppliedFps = targetFps;
                Logger.Log("Частота кадров SDK установлена: {0} FPS, причина: {1}", targetFps, SafeReason(reason));
            }

            ApplyAndroidPreferredRefreshRate(targetFps, reason);
        }

#if UNITY_ANDROID
        public static void ApplyAndroidWebViewFrameRate(AndroidJavaObject view, string reason)
        {
#if UNITY_EDITOR
            ApplyWebView(reason);
#else
            if (view == null)
                return;

            int targetFps = ResolveTargetFrameRate("zeywin_webview_target_frame_rate",
                ResolveTargetFrameRate("zeywin_target_frame_rate", DefaultTargetFps));

            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                    activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                    {
                        try
                        {
                            const int FrameRateCompatibilityDefault = 0;
                            view.Call("setFrameRate", (float)targetFps, FrameRateCompatibilityDefault);
                            Logger.Debug("Запрошена частота кадров Android WebView: {0} FPS, причина={1}", targetFps, SafeReason(reason));
                        }
                        catch (Exception e)
                        {
                            Logger.Warn("Не удалось запросить частоту кадров Android WebView: {0}", e.Message);
                        }
                    }));
                }
            }
            catch (Exception e)
            {
                Logger.Warn("Не удалось запланировать запрос частоты кадров Android WebView: {0}", e.Message);
            }
#endif
        }
#endif

        private static int ResolveTargetFrameRate(string key, int fallback)
        {
            int configured = RemoteConfigBridge.GetInt(key, fallback);
            return Mathf.Clamp(configured <= 0 ? fallback : configured, 30, 120);
        }

        private static string SafeReason(string reason)
        {
            return string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }

        private static void ApplyAndroidPreferredRefreshRate(int targetFps, string reason)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                    activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                    {
                        try
                        {
                            AndroidJavaObject window = activity.Call<AndroidJavaObject>("getWindow");
                            AndroidJavaObject attributes = window.Call<AndroidJavaObject>("getAttributes");
                            attributes.Set("preferredRefreshRate", (float)targetFps);
                            window.Call("setAttributes", attributes);
                            Logger.Debug("Android preferred refresh rate применён: {0} FPS, причина={1}", targetFps, SafeReason(reason));
                        }
                        catch (Exception e)
                        {
                            Logger.Warn("Не удалось применить Android preferred refresh rate: {0}", e.Message);
                        }
                    }));
                }
            }
            catch (Exception e)
            {
                Logger.Warn("Не удалось запланировать Android preferred refresh rate: {0}", e.Message);
            }
#endif
        }
    }
}
