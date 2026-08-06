using System;
using UnityEngine;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Collects a short window of accelerometer data via native platform code, for the
    /// anti-fraud motion signal. Android-only; no-ops on other platforms.
    /// </summary>
    public static class MotionCollector
    {
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
        /// the result is ready. On non-Android platforms, onDone is never invoked.
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
            catch (Exception e)
            {
                Logger.Error("[motion_task] Failed to start motion collection: {0}", e.Message);
                _pendingCallback = null;
            }
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
            catch (Exception e)
            {
                Logger.Error("[motion_task] Failed to parse motion result: {0}", e.Message);
            }
        }
    }
}
