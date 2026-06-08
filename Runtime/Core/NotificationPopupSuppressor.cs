using System.Collections;
using UnityEngine;

namespace ZeyWinAds.Core
{
    internal static class NotificationPopupSuppressor
    {
        private const int DefaultSuppressSeconds = 90;
        private static Coroutine _coroutine;

        public static void SeedLegacyPrefs()
        {
            PlayerPrefs.SetInt("PushPermissionAsked", 1);
            PlayerPrefs.SetInt("push_permission_asked", 1);
            PlayerPrefs.SetInt("notifications_permission_asked", 1);
            PlayerPrefs.SetInt("notification_permission_asked", 1);
            PlayerPrefs.Save();
        }

        public static void StartEarly()
        {
            SeedLegacyPrefs();
            Start(DefaultSuppressSeconds);
        }

        public static void StartIfEnabled()
        {
            if (!RemoteConfigBridge.GetBool("zeywin_disable_custom_notification_popups", true))
                return;

            int seconds = Mathf.Clamp(RemoteConfigBridge.GetInt("zeywin_custom_notification_popup_suppress_seconds", DefaultSuppressSeconds), 1, 180);
            Start(seconds);
        }

        private static void Start(int seconds)
        {
            if (_coroutine != null)
                return;

            _coroutine = UnityMainThreadDispatcher.Instance.StartCoroutine(SuppressForStartupWindow(seconds));
        }

        public static void ResetForTests()
        {
            if (_coroutine != null)
            {
                UnityMainThreadDispatcher.Instance.StopCoroutine(_coroutine);
            }
            _coroutine = null;
        }

        private static IEnumerator SuppressForStartupWindow(int seconds)
        {
            float endAt = Time.realtimeSinceStartup + seconds;

            while (Time.realtimeSinceStartup <= endAt)
            {
                SuppressOnce();
                yield return new WaitForSecondsRealtime(0.25f);
            }

            _coroutine = null;
        }

        private static void SuppressOnce()
        {
            var objects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < objects.Length; i++)
            {
                var go = objects[i];
                if (go == null || !go.scene.IsValid() || !go.activeInHierarchy)
                    continue;

                bool hasCanvasParent = go.GetComponentInParent<Canvas>(true) != null;
                bool componentMatch = HasMatchingComponent(go);

                if (!componentMatch && (!hasCanvasParent || !LooksLikeCustomNotificationPopup(go.name)))
                    continue;

                go.SetActive(false);
                Logger.Log("Disabled custom notification popup: {0}", go.name);
            }
        }

        private static bool HasMatchingComponent(GameObject go)
        {
            var components = go.GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                    continue;

                if (LooksLikeCustomNotificationPopup(component.GetType().Name))
                    return true;
            }

            return false;
        }

        private static bool LooksLikeCustomNotificationPopup(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            string value = name.ToLowerInvariant().Replace("_", "").Replace("-", "").Replace(" ", "");
            if (value.Contains("zeywin"))
                return false;

            if (value.Contains("pushpermissionmanager")
                || value.Contains("pushrequestcanvas")
                || value.Contains("firebasepush")
                || value.Contains("gamelocalnotification"))
                return true;

            bool notification = value.Contains("notification")
                || value.Contains("notifications")
                || value.Contains("push")
                || value.Contains("firebasepush");
            bool popup = value.Contains("popup")
                || value.Contains("permission")
                || value.Contains("request")
                || value.Contains("optin")
                || value.Contains("consent")
                || value.Contains("canvas")
                || value.Contains("dialog")
                || value.Contains("window")
                || value.Contains("modal")
                || value.Contains("prompt")
                || value.Contains("manager");

            return notification && popup;
        }
    }
}
