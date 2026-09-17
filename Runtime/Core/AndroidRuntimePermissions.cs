using System.Collections;
using UnityEngine;

namespace ZeyWinAds.Core
{
    internal static class AndroidRuntimePermissions
    {
        private const string CameraPermission = "android.permission.CAMERA";
        private const string RecordAudioPermission = "android.permission.RECORD_AUDIO";
        private const string PostNotificationsPermission = "android.permission.POST_NOTIFICATIONS";
        private const string NotificationPromptedKey = "ZeyWinAds_PostNotificationsPrompted_v1";
#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool _notificationPromptStarted;

        // UnityEngine.Android.Permission.HasUserAuthorizedPermission's first cold-start touch is
        // itself a reflective JNI call into Unity's own engine-side permission bridge, and is a
        // separately observed trigger for the "jlr_method == null" abort. Route through raw JNI
        // (Context.checkSelfPermission) first; only fall back to Unity's API if that couldn't be
        // resolved.
        private static bool HasUserAuthorizedPermissionSafe(string permission)
        {
            bool? raw = AndroidJniSafe.HasSelfPermission(permission);
            return raw ?? UnityEngine.Android.Permission.HasUserAuthorizedPermission(permission);
        }
#endif

        public static void RequestCameraForWebView()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!RemoteConfigBridge.GetBool("zeywin_webview_camera_permission_enabled", true))
                return;

            RequestPermissionIfNeeded(CameraPermission, "WebView camera");

            if (RemoteConfigBridge.GetBool("zeywin_webview_microphone_permission_enabled", true))
                RequestPermissionIfNeeded(RecordAudioPermission, "WebView microphone");
#endif
        }

        public static void ScheduleNotificationPermissionPrompt()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_notificationPromptStarted)
                return;

            if (!RemoteConfigBridge.GetBool("zeywin_push_permission_enabled", true))
                return;

            if (DeviceInfo.GetAndroidApiLevel() < 33)
                return;

            if (HasUserAuthorizedPermissionSafe(PostNotificationsPermission))
                return;

            if (RemoteConfigBridge.GetBool("zeywin_push_permission_prompt_once", true)
                && PlayerPrefs.GetInt(NotificationPromptedKey, 0) == 1)
                return;

            int delay = Mathf.Clamp(RemoteConfigBridge.GetInt("zeywin_push_permission_delay_seconds", 2), 0, 600);
            _notificationPromptStarted = true;
            UnityMainThreadDispatcher.Instance.StartCoroutine(RequestNotificationPermissionAfterDelay(delay));
#endif
        }

        public static void ResetForTests()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _notificationPromptStarted = false;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static IEnumerator RequestNotificationPermissionAfterDelay(int delaySeconds)
        {
            if (delaySeconds > 0)
                yield return new WaitForSecondsRealtime(delaySeconds);

            if (HasUserAuthorizedPermissionSafe(PostNotificationsPermission))
                yield break;

            PlayerPrefs.SetInt(NotificationPromptedKey, 1);
            PlayerPrefs.Save();
            Logger.Log("Requesting native Android notification permission");
            UnityEngine.Android.Permission.RequestUserPermission(PostNotificationsPermission);
        }

        private static void RequestPermissionIfNeeded(string permission, string reason)
        {
            if (string.IsNullOrEmpty(permission) || DeviceInfo.GetAndroidApiLevel() < 23)
                return;

            if (HasUserAuthorizedPermissionSafe(permission))
                return;

            Logger.Log("Requesting Android permission for {0}", reason);
            UnityEngine.Android.Permission.RequestUserPermission(permission);
        }

#endif
    }
}
