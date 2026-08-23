using System;
using System.Runtime.InteropServices;

namespace ZeyWinAds.Core
{
    internal static class AppTrackingTransparency
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void _ZeyWinAds_RequestTrackingAuthorization(string gameObjectName);
#endif

        // Guards against the native ATT request firing more than once per
        // process (e.g. if RequestIfEnabled is ever called from more than one
        // place) — a second in-flight request could stack another system
        // dialog on top of / interrupt the first.
        private static bool _requested;
        private static Action<string> _pendingCompletion;

        /// <summary>
        /// Requests ATT authorization if enabled in settings. <paramref name="onComplete"/>
        /// always fires exactly once — with the resolved status on iOS, or immediately with
        /// a sentinel elsewhere/when skipped — so callers can defer their own permission
        /// prompts (AdMob UMP, push notifications) until ATT is fully resolved, avoiding
        /// multiple native dialogs competing for the screen at once.
        /// </summary>
        public static void RequestIfEnabled(Action<string> onComplete = null)
        {
            var settings = ZeyWinAdsSettings.Load();
            if (settings == null || !settings.requestAppTrackingTransparency)
            {
                onComplete?.Invoke("skipped");
                return;
            }

            if (_requested)
            {
                onComplete?.Invoke("already_requested");
                return;
            }
            _requested = true;

#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                _pendingCompletion = onComplete;
                _ZeyWinAds_RequestTrackingAuthorization(UnityMainThreadDispatcher.Instance.gameObject.name);
            }
            catch (Exception e)
            {
                Logger.Warn("ATT request failed: {0}", e.Message);
                var cb = _pendingCompletion;
                _pendingCompletion = null;
                cb?.Invoke("error");
            }
#else
            Logger.Debug("ATT request skipped: not running on iOS device");
            onComplete?.Invoke("not_ios");
#endif
        }

        /// <summary>
        /// Called from UnityMainThreadDispatcher when the native ATT completion handler fires.
        /// </summary>
        internal static void HandleNativeStatus(string status)
        {
            var cb = _pendingCompletion;
            _pendingCompletion = null;
            cb?.Invoke(status);
        }
    }
}
