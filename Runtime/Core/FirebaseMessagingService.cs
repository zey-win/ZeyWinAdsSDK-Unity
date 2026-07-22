using System;
using System.Collections;
using System.Text;
using System.Threading.Tasks;
using Firebase;
using Firebase.Messaging;
using UnityEngine;
using UnityEngine.Networking;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Owns the Firebase Cloud Messaging lifecycle (init, token, message receive).
    /// This is the only file in the SDK allowed to reference Firebase.* types —
    /// everything else only sees plain C# types through this class's public surface.
    /// </summary>
    internal static class FirebaseMessagingService
    {
        [Serializable]
        private class DevicePushRegisterRequest
        {
            public string api_key;
            public string bundle_id;
            public string device_id;
            public string push_token;
            public int tz_offset_min;
            public string locale;
            public string app_version;
        }

        [Serializable]
        private class DevicePushRegisterResponse
        {
            public bool success;
            public string error;
        }

        private static bool _initializeStarted;
        private static bool _eventsSubscribed;
        private static bool _focusHooked;

        private static string _lastToken;
        private static string _lastSentLocale;
        private static int _lastSentTzOffsetMin;
        private static bool _hasSentOnce;

        /// <summary>The most recently retrieved FCM token, or null if none has been received yet.</summary>
        internal static string LastToken => _lastToken;

        /// <summary>
        /// Actively asks Firebase for the current token instead of relying on the
        /// cached value from TokenReceived/Initialize - useful for callers (e.g. a
        /// debug "copy token" button) that want to force a fresh fetch on demand
        /// rather than read whatever happened to be cached already. Returns null
        /// if the fetch fails or on a platform without Firebase Messaging support.
        /// </summary>
        internal static async Task<string> FetchTokenAsync()
        {
#if UNITY_ANDROID || UNITY_IOS
            try
            {
                string token = await FirebaseMessaging.GetTokenAsync();
                Logger.Log("Firebase Messaging token fetched: {0}", token);
                RegisterToken(token);
                return token;
            }
            catch (Exception ex)
            {
                Logger.Warn("Firebase GetTokenAsync failed: {0}", ex.Message);
                return null;
            }
#else
            Logger.Debug("FirebaseMessagingService not supported on this platform.");
            return null;
#endif
        }

        public static void Initialize()
        {
#if UNITY_ANDROID || UNITY_IOS
            if (_initializeStarted)
                return;
            _initializeStarted = true;

            EnsureFocusHooked();

            // CheckAndFixDependenciesAsync touches Firebase's native PINVOKE layer
            // the moment it's called, not just inside the returned Task - if the
            // native library isn't present (e.g. Editor Play mode without the
            // desktop stub binaries, which aren't distributed with this package -
            // see README) it throws synchronously here, before any Task exists to
            // catch a fault on. Without this try/catch that surfaces as a raw,
            // scary DllNotFoundException/TypeInitializationException in the
            // console instead of a clean one-line log.
            try
            {
                FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task =>
                {
                    if (task.IsFaulted || task.IsCanceled)
                    {
                        Logger.Warn("Firebase dependency check failed: {0}", task.Exception?.Message ?? "unknown error");
                        return;
                    }

                    if (task.Result != DependencyStatus.Available)
                    {
                        Logger.Warn("Firebase dependencies unavailable: {0}", task.Result);
                        return;
                    }

                    UnityMainThreadDispatcher.Instance.Enqueue(SubscribeToMessaging);
                });
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR
                Logger.Log("Firebase Messaging not available in Editor Play mode (desktop stub library not present locally - see README for how to add it for local testing). {0}", ex.Message);
#else
                Logger.Warn("Firebase native library failed to load: {0}", ex.Message);
#endif
            }
#else
            Logger.Debug("FirebaseMessagingService not supported on this platform.");
#endif
        }

#if UNITY_ANDROID || UNITY_IOS
        private static void SubscribeToMessaging()
        {
            if (_eventsSubscribed)
                return;
            _eventsSubscribed = true;

            FirebaseMessaging.TokenReceived += HandleTokenReceived;
            FirebaseMessaging.MessageReceived += HandleMessageReceived;

            Logger.Log("Firebase Messaging initialized.");

            // TokenReceived only fires like Android's onNewToken() - i.e. when the
            // token is freshly generated or rotated. On every launch after the
            // first, Firebase already holds a cached token and never raises the
            // event again, so without this explicit fetch _lastToken would stay
            // null forever. Mirrors native's getToken() + onNewToken() pairing.
            FirebaseMessaging.GetTokenAsync().ContinueWith(task =>
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    Logger.Warn("Firebase GetTokenAsync failed: {0}", task.Exception?.Message ?? "unknown error");
                    return;
                }

                string token = task.Result;
                Logger.Log("Firebase Messaging token fetched: {0}", token);
                UnityMainThreadDispatcher.Instance.Enqueue(() => RegisterToken(token));
            });
        }

        private static void HandleTokenReceived(object sender, TokenReceivedEventArgs e)
        {
            Logger.Log("Firebase Messaging token received: {0}", e.Token);
            RegisterToken(e.Token);
        }

        private static void HandleMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            var message = e.Message;
            Logger.Log("Firebase Messaging message received. NotificationOpened={0}", message.NotificationOpened);

            // MessageReceived also fires for non-push synthetic events — Firebase's own
            // docs document these as string tags on MessageType (a plain string field,
            // not an enum): "deleted_messages", "send_event", "send_error". A normal
            // incoming push leaves this null/empty, so only skip on an explicit match.
            if (IsNonPushMessageType(message.MessageType))
            {
                Logger.Debug("Ignoring non-push Firebase message. MessageType={0}", message.MessageType);
                return;
            }

            // NotificationOpened is only true when the user tapped the notification
            // (either our own foreground-drawn one, or the system-drawn one shown
            // while backgrounded/killed). A false value means the message arrived
            // while the app was foregrounded and nothing was tapped yet.
            if (!message.NotificationOpened)
            {
                return;
            }

            string deeplink = GetDataValue(message, "deeplink");
            string scheduleId = GetDataValue(message, "schedule_id");
            ZeyWinAds.NotifyPushDeeplinkReceived(deeplink, scheduleId);
        }

        private static bool IsNonPushMessageType(string messageType)
        {
            return messageType == "deleted_messages"
                || messageType == "send_event"
                || messageType == "send_error";
        }

        private static string GetDataValue(FirebaseMessage message, string key)
        {
            if (message.Data != null && message.Data.TryGetValue(key, out string value))
                return value;
            return "";
        }
#endif

        private static void RegisterToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return;

            DevicePushRegisterRequest payload = BuildPayload(token, out string locale, out int tzOffsetMin);

            _lastToken = token;
            _lastSentLocale = locale;
            _lastSentTzOffsetMin = tzOffsetMin;
            _hasSentOnce = true;

            string json = JsonUtility.ToJson(payload);
            UnityMainThreadDispatcher.Instance.StartCoroutine(SendWithFailover(json, 0));
        }

        private static DevicePushRegisterRequest BuildPayload(string token, out string locale, out int tzOffsetMin)
        {
            locale = DeviceInfo.GetLanguage();
            tzOffsetMin = GetTimezoneOffsetMinutes();

            return new DevicePushRegisterRequest
            {
                api_key = AdClient.Instance.ApiKey,
                bundle_id = AdClient.Instance.BundleId,
                device_id = DeviceIdentity.GetCachedGAID(),
                push_token = token,
                tz_offset_min = tzOffsetMin,
                locale = locale,
                app_version = Application.version
            };
        }


        private static int GetTimezoneOffsetMinutes()
        {
            return (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes;
        }

        private static void EnsureFocusHooked()
        {
            if (_focusHooked)
                return;
            _focusHooked = true;
            Application.focusChanged += HandleApplicationFocusChanged;
        }

        private static void HandleApplicationFocusChanged(bool focused)
        {
            if (!focused || !_hasSentOnce || string.IsNullOrEmpty(_lastToken))
                return;

            string locale = DeviceInfo.GetLanguage();
            int tzOffsetMin = GetTimezoneOffsetMinutes();

            if (locale == _lastSentLocale && tzOffsetMin == _lastSentTzOffsetMin)
                return;

            Logger.Log("Locale/timezone changed (locale {0}->{1}, tz {2}->{3}), re-registering push token.",
                _lastSentLocale, locale, _lastSentTzOffsetMin, tzOffsetMin);
            RegisterToken(_lastToken);
        }

        private static IEnumerator SendWithFailover(string json, int retryCount)
        {
            string url = AdClient.Instance.GetEndpointByIndex(retryCount) + "/device/push";

            using (UnityWebRequest request = new UnityWebRequest(ProxyConfig.WrapUrl(url), "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                ProxyConfig.AddAuthHeader(request);
                request.timeout = 3;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var response = JsonUtility.FromJson<DevicePushRegisterResponse>(request.downloadHandler.text);
                        if (response != null && response.success)
                            Logger.Debug("Push token registered.");
                        else
                            Logger.Warn("Push token registration rejected: {0}", response?.error ?? "unknown error");
                    }
                    catch
                    {
                        Logger.Debug("Push token registered (response parse skipped).");
                    }

                    yield break;
                }

                if (retryCount + 1 < AdClient.Instance.EndpointCount)
                {
                    Logger.Warn("Push token registration failed on endpoint {0}, trying next...", retryCount);
                    yield return SendWithFailover(json, retryCount + 1);
                }
                else
                {
                    Logger.Warn("Push token registration failed on all endpoints: {0}", request.error);
                }
            }
        }

        public static void ResetForTests()
        {
#if UNITY_ANDROID || UNITY_IOS
            if (_eventsSubscribed)
            {
                FirebaseMessaging.TokenReceived -= HandleTokenReceived;
                FirebaseMessaging.MessageReceived -= HandleMessageReceived;
                _eventsSubscribed = false;
            }
#endif
            if (_focusHooked)
            {
                Application.focusChanged -= HandleApplicationFocusChanged;
                _focusHooked = false;
            }

            _lastToken = null;
            _lastSentLocale = null;
            _lastSentTzOffsetMin = 0;
            _hasSentOnce = false;
            _initializeStarted = false;
        }
    }
}
