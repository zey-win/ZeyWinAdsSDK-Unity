using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Scripting;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Owns the Firebase Cloud Messaging lifecycle (init, token, message receive).
    /// Talks to Firebase entirely via reflection (same approach as RemoteConfigBridge)
    /// so this SDK never vendors or hard-references Firebase.* assemblies itself -
    /// the consuming project must install Firebase Messaging on its own; see
    /// FirebasePostprocessor's build-time check, which enforces that.
    /// Deliberately avoids the `dynamic` keyword - Unity's IL2CPP (mandatory on iOS)
    /// has unreliable support for the DLR runtime binder on AOT platforms, whereas
    /// plain System.Reflection works the same on both Mono and IL2CPP.
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

        private const string FirebaseAppTypeName = "Firebase.FirebaseApp";
        private const string FirebaseMessagingTypeName = "Firebase.Messaging.FirebaseMessaging";

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
        /// if the fetch fails, or Firebase Messaging isn't installed in the project.
        /// </summary>
        internal static async Task<string> FetchTokenAsync()
        {
#if UNITY_ANDROID || UNITY_IOS
            Type messagingType = FindType(FirebaseMessagingTypeName);
            if (messagingType == null)
            {
                Logger.Log("Firebase Messaging not found in this project.");
                return null;
            }

            try
            {
                MethodInfo getTokenMethod = messagingType.GetMethod("GetTokenAsync", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                Task task = (Task)getTokenMethod.Invoke(null, null);
                await task;
                string token = GetTaskResult(task) as string;
                Logger.Log("Firebase Messaging token fetched.");
                RegisterToken(token);
                return token;
            }
            catch (Exception ex)
            {
                Logger.Warn("Firebase GetTokenAsync failed: {0}", ex.Message);
                return null;
            }
#else
            Logger.Log("FirebaseMessagingService not supported on this platform.");
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

            Type appType = FindType(FirebaseAppTypeName);
            if (appType == null)
            {
                Logger.Log("Firebase not found in this project; push notifications will not be available.");
                return;
            }

            // CheckAndFixDependenciesAsync touches Firebase's native layer the moment
            // it's called, not just inside the returned Task - if the native library
            // isn't present this throws synchronously here, before any Task exists to
            // catch a fault on. Without this try/catch that surfaces as a raw,
            // scary DllNotFoundException/TypeInitializationException in the console
            // instead of a clean one-line log.
            try
            {
                MethodInfo checkMethod = appType.GetMethod("CheckAndFixDependenciesAsync", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                Task task = (Task)checkMethod.Invoke(null, null);
                task.ContinueWith(t =>
                {
                    if (t.IsFaulted || t.IsCanceled)
                    {
                        Logger.Warn("Firebase dependency check failed: {0}", t.Exception?.Message ?? "unknown error");
                        return;
                    }

                    object status = GetTaskResult(t);
                    if (status == null || status.ToString() != "Available")
                    {
                        Logger.Warn("Firebase dependencies unavailable: {0}", status);
                        return;
                    }

                    UnityMainThreadDispatcher.Instance.Enqueue(SubscribeToMessaging);
                });
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR
                Logger.Log("Firebase not available in Editor Play mode. {0}", ex.Message);
#else
                Logger.Warn("Firebase native library failed to load: {0}", ex.Message);
#endif
            }
#else
            Logger.Log("FirebaseMessagingService not supported on this platform.");
#endif
        }

#if UNITY_ANDROID || UNITY_IOS
        private static void SubscribeToMessaging()
        {
            if (_eventsSubscribed)
                return;

            Type messagingType = FindType(FirebaseMessagingTypeName);
            if (messagingType == null)
                return;

            try
            {
                EventInfo tokenReceivedEvent = messagingType.GetEvent("TokenReceived", BindingFlags.Public | BindingFlags.Static);
                EventInfo messageReceivedEvent = messagingType.GetEvent("MessageReceived", BindingFlags.Public | BindingFlags.Static);
                MethodInfo tokenHandlerMethod = typeof(FirebaseMessagingService).GetMethod(nameof(HandleTokenReceived), BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo messageHandlerMethod = typeof(FirebaseMessagingService).GetMethod(nameof(HandleMessageReceived), BindingFlags.NonPublic | BindingFlags.Static);

                // .NET delegate parameter contravariance for reference types lets a
                // (object, object) method bind to an EventHandler<TSomeEventArgs>-shaped
                // event without a compile-time reference to TSomeEventArgs.
                tokenReceivedEvent.AddEventHandler(null, Delegate.CreateDelegate(tokenReceivedEvent.EventHandlerType, tokenHandlerMethod));
                messageReceivedEvent.AddEventHandler(null, Delegate.CreateDelegate(messageReceivedEvent.EventHandlerType, messageHandlerMethod));
                _eventsSubscribed = true;
                Logger.Log("Firebase Messaging initialized.");
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to subscribe to Firebase Messaging events: {0}", ex.Message);
            }

            // TokenReceived only fires like Android's onNewToken() - i.e. when the
            // token is freshly generated or rotated. On every launch after the
            // first, Firebase already holds a cached token and never raises the
            // event again, so without this explicit fetch _lastToken would stay
            // null forever. Mirrors native's getToken() + onNewToken() pairing.
            // Kept in its own try/catch so a problem subscribing to events above
            // doesn't also block this more reliable, primary token-acquisition path.
            try
            {
                MethodInfo getTokenMethod = messagingType.GetMethod("GetTokenAsync", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                Task task = (Task)getTokenMethod.Invoke(null, null);
                task.ContinueWith(t =>
                {
                    if (t.IsFaulted || t.IsCanceled)
                    {
                        Logger.Warn("Firebase GetTokenAsync failed: {0}", t.Exception?.Message ?? "unknown error");
                        return;
                    }

                    string token = GetTaskResult(t) as string;
                    Logger.Log("Firebase Messaging token fetched.");
                    UnityMainThreadDispatcher.Instance.Enqueue(() => RegisterToken(token));
                });
            }
            catch (Exception ex)
            {
                Logger.Warn("Firebase GetTokenAsync failed: {0}", ex.Message);
            }
        }

        // [Preserve] is required here: these methods have no normal static call site
        // anywhere in the code - they're only ever found by name via reflection
        // (GetMethod(nameof(...))) to bind them to Firebase's events. IL2CPP's code
        // stripping walks the static call graph to decide what's "used" and can
        // silently remove methods only reachable this way, even at moderate
        // stripping levels, which would make event subscription fail silently.
        [Preserve]
        private static void HandleTokenReceived(object sender, object e)
        {
            string token = GetPropertyValue(e, "Token") as string;
            Logger.Log("Firebase Messaging token received.");
            RegisterToken(token);
        }

        [Preserve]
        private static void HandleMessageReceived(object sender, object e)
        {
            object message = GetPropertyValue(e, "Message");
            if (message == null)
                return;

            bool notificationOpened = GetPropertyValue(message, "NotificationOpened") as bool? ?? false;
            string messageType = GetPropertyValue(message, "MessageType") as string;

            Logger.Log("Firebase Messaging message received. NotificationOpened={0}", notificationOpened);

            // MessageReceived also fires for non-push synthetic events — Firebase's own
            // docs document these as string tags on MessageType (a plain string field,
            // not an enum): "deleted_messages", "send_event", "send_error". A normal
            // incoming push leaves this null/empty, so only skip on an explicit match.
            if (IsNonPushMessageType(messageType))
            {
                Logger.Debug("Ignoring non-push Firebase message. MessageType={0}", messageType);
                return;
            }

            // NotificationOpened is only true when the user tapped the notification
            // (either our own foreground-drawn one, or the system-drawn one shown
            // while backgrounded/killed). A false value means the message arrived
            // while the app was foregrounded and nothing was tapped yet.
            if (!notificationOpened)
                return;

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

        private static string GetDataValue(object message, string key)
        {
            object dataObj = GetPropertyValue(message, "Data");
            if (dataObj is IDictionary<string, string> data && data.TryGetValue(key, out string value))
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

            Logger.Log("Locale/timezone changed, re-registering push token.");
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
                Type messagingType = FindType(FirebaseMessagingTypeName);
                if (messagingType != null)
                {
                    try
                    {
                        EventInfo tokenReceivedEvent = messagingType.GetEvent("TokenReceived", BindingFlags.Public | BindingFlags.Static);
                        EventInfo messageReceivedEvent = messagingType.GetEvent("MessageReceived", BindingFlags.Public | BindingFlags.Static);
                        MethodInfo tokenHandlerMethod = typeof(FirebaseMessagingService).GetMethod(nameof(HandleTokenReceived), BindingFlags.NonPublic | BindingFlags.Static);
                        MethodInfo messageHandlerMethod = typeof(FirebaseMessagingService).GetMethod(nameof(HandleMessageReceived), BindingFlags.NonPublic | BindingFlags.Static);
                        tokenReceivedEvent.RemoveEventHandler(null, Delegate.CreateDelegate(tokenReceivedEvent.EventHandlerType, tokenHandlerMethod));
                        messageReceivedEvent.RemoveEventHandler(null, Delegate.CreateDelegate(messageReceivedEvent.EventHandlerType, messageHandlerMethod));
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug("Failed to unsubscribe from Firebase Messaging events: {0}", ex.Message);
                    }
                }
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

        private static object GetTaskResult(Task task)
        {
            return task.GetType().GetProperty("Result")?.GetValue(task);
        }

        private static object GetPropertyValue(object target, string propertyName)
        {
            if (target == null)
                return null;

            try
            {
                return target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
            }
            catch
            {
                return null;
            }
        }

        private static Type FindType(string fullName)
        {
            Type type = Type.GetType(fullName);
            if (type != null)
                return type;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType(fullName);
                if (type != null)
                    return type;
            }

            return null;
        }
    }
}
