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

        public static void RequestIfEnabled()
        {
            var settings = ZeyWinAdsSettings.Load();
            if (settings == null || !settings.requestAppTrackingTransparency)
                return;

#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                _ZeyWinAds_RequestTrackingAuthorization(UnityMainThreadDispatcher.Instance.gameObject.name);
            }
            catch (Exception e)
            {
                Logger.Warn("ATT request failed: {0}", e.Message);
            }
#else
            Logger.Debug("ATT request skipped: not running on iOS device");
#endif
        }
    }
}
