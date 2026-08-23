using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Collects a short window of accelerometer data via native platform code, for the
    /// anti-fraud motion signal. Implemented on Android and iOS; on other platforms
    /// (e.g. Editor), onDone fires immediately with empty/zeroed data.
    /// </summary>
    public static class MotionCollector
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void _ZeyWinAds_CollectMotion(string gameObjectName);
#endif

        [Serializable]
        public class MotionData
        {
            public int v;
            public int elapsed_ms;
            public int events;
            public bool has_accel;
            public bool has_gyro;
            public string s;
        }

        private static Action<MotionData> _pendingCallback;

        /// <summary>
        /// Starts native motion collection (~2s). Invokes onDone on the main thread once
        /// the result is ready. On platforms without a native implementation, onDone
        /// fires immediately with empty/zeroed data.
        /// </summary>
        public static void Collect(Action<MotionData> onDone)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _pendingCallback = onDone;
            try
            {
                using (var cls = new AndroidJavaClass("com.zeywinads.unity.ZeyWinAdsMotionCollector"))
                {
                    cls.CallStatic("collect", UnityMainThreadDispatcher.Instance.gameObject.name, "OnMotionCollected");
                }
            }
            catch (Exception)
            {
                _pendingCallback = null;
            }
#elif UNITY_IOS && !UNITY_EDITOR
            _pendingCallback = onDone;
            try
            {
                _ZeyWinAds_CollectMotion(UnityMainThreadDispatcher.Instance.gameObject.name);
            }
            catch (Exception e)
            {
                Logger.Error("Failed to start motion collection: {0}", e.Message);
                _pendingCallback = null;
            }
#else
            onDone?.Invoke(new MotionData
            {
                v = 1,
                elapsed_ms = 0,
                events = 0,
                has_accel = false,
                has_gyro = false,
                s = ""
            });
#endif
        }

        /// <summary>
        /// Called by UnityMainThreadDispatcher.OnMotionCollected via UnitySendMessage.
        /// </summary>
        public static void HandleNativeResult(string json)
        {
            var callback = _pendingCallback;
            _pendingCallback = null;
            if (callback == null)
                return;

            try
            {
                var data = JsonUtility.FromJson<MotionData>(json);
                callback.Invoke(data);
            }
            catch (Exception)
            {
            }
        }
    }
}
