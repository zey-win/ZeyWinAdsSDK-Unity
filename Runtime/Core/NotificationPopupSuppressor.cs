using System.Collections;
using UnityEngine;

namespace ZeyWinAds.Core
{
    internal static class NotificationPopupSuppressor
    {
        private static Coroutine _coroutine;

        public static void StartIfEnabled()
        {
            if (!RemoteConfigBridge.GetBool("zeywin_disable_custom_notification_popups", true))
                return;

            if (_coroutine != null)
                return;

            _coroutine = UnityMainThreadDispatcher.Instance.StartCoroutine(SuppressForStartupWindow());
        }

        public static void ResetForTests()
        {
            if (_coroutine != null)
            {
                UnityMainThreadDispatcher.Instance.StopCoroutine(_coroutine);
            }
            _coroutine = null;
        }

        private static IEnumerator SuppressForStartupWindow()
        {
            int seconds = Mathf.Clamp(RemoteConfigBridge.GetInt("zeywin_custom_notification_popup_suppress_seconds", 45), 1, 180);
            float endAt = Time.realtimeSinceStartup + seconds;

            while (Time.realtimeSinceStartup <= endAt)
            {
                SuppressOnce();
                yield return new WaitForSecondsRealtime(1f);
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

                if (go.GetComponentInParent<Canvas>(true) == null)
                    continue;

                if (!LooksLikeCustomNotificationPopup(go.name) && !HasMatchingComponent(go))
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

            bool notification = value.Contains("notification") || value.Contains("push");
            bool popup = value.Contains("popup")
                || value.Contains("permission")
                || value.Contains("optin")
                || value.Contains("consent")
                || value.Contains("dialog")
                || value.Contains("modal")
                || value.Contains("prompt");

            return notification && popup;
        }
    }
}
