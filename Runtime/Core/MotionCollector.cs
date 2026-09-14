using System;
using System.Collections;
using System.Text;
using UnityEngine;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Collects a short window of accelerometer data for the anti-fraud motion signal:
    /// a real, stationary phone still shows ~0.01-0.05 m/s^2 of natural sensor noise,
    /// while an emulator tends to report a constant value or exact zero. That contrast
    /// is the whole signal, so samples are sent as raw per-axis integers (millimeters/s^2,
    /// not floats, not m/s^2) - coarser quantization would collapse real jitter to zero
    /// and make every real device look like an emulator.
    ///
    /// Uses UnityEngine.Input (legacy Input Manager) uniformly on Android and iOS via a
    /// coroutine on UnityMainThreadDispatcher - no native plugin, no JNI. On platforms
    /// without an accelerometer (e.g. Editor), onDone fires immediately with empty/zeroed
    /// data instead of waiting out the window; that is itself the strongest "not a real
    /// device" signal, so it is reported right away rather than delayed.
    /// </summary>
    public static class MotionCollector
    {
        private const int WindowMs = 2000;
        private const int MaxFrames = 32;
        private const float MinFrameGapSeconds = 0.060f;
        private const int ClampMm = 40000;

        // UnityEngine.Input.acceleration reports g-units on both Android and iOS (unlike
        // the native platform APIs this replaced, which differed: Android's SensorEvent is
        // already m/s^2, iOS's CMAccelerometerData is g's) - always convert before scaling.
        private const double GToMetersPerSecondSquared = 9.80665;

        // One-shot latch: Input.acceleration throws InvalidOperationException on projects
        // whose Project Settings > Active Input Handling is "Input System Package (New)"
        // only. Mirrors WebViewLock.IsAndroidBackPressed's _legacyBackInputUnavailable
        // pattern so affected games (e.g. Bet-App, BlackJackNew) degrade to has_accel=false
        // once, quietly, instead of throwing/logging every frame.
        private static bool _legacyInputUnavailable;

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

        /// <summary>
        /// Starts motion collection (~2s, or fewer if capped/unavailable). Invokes onDone
        /// on the main thread once the result is ready. If there's no accelerometer at
        /// all, onDone fires immediately with empty/zeroed data.
        /// </summary>
        public static void Collect(Action<MotionData> onDone)
        {
            bool hasGyro = SystemInfo.supportsGyroscope;

            if (_legacyInputUnavailable || !SystemInfo.supportsAccelerometer)
            {
                onDone?.Invoke(new MotionData
                {
                    v = 1,
                    elapsed_ms = 0,
                    events = 0,
                    has_accel = false,
                    has_gyro = hasGyro,
                    s = ""
                });
                return;
            }

            try
            {
                UnityMainThreadDispatcher.Instance.StartCoroutine(SampleRoutine(onDone, hasGyro));
            }
            catch (Exception e)
            {
                Logger.Error("Failed to start motion collection: {0}", e.Message);
                onDone?.Invoke(new MotionData
                {
                    v = 1,
                    elapsed_ms = 0,
                    events = 0,
                    has_accel = false,
                    has_gyro = hasGyro,
                    s = ""
                });
            }
        }

        private static IEnumerator SampleRoutine(Action<MotionData> onDone, bool hasGyro)
        {
            var sb = new StringBuilder();
            int kept = 0;
            int events = 0;
            bool haveKept = false;
            bool inputThrew = false;
            float startTime = Time.unscaledTime;
            float lastKeptTime = 0f;

            while (true)
            {
                Vector3 a;
                try
                {
                    a = Input.acceleration;
                }
                catch (InvalidOperationException e)
                {
                    _legacyInputUnavailable = true;
                    inputThrew = true;
                    Logger.Warn("Legacy accelerometer input disabled because the project uses Input System only: {0}", e.Message);
                    break;
                }

                events++;
                float now = Time.unscaledTime;
                if (!haveKept || (now - lastKeptTime) >= MinFrameGapSeconds)
                {
                    if (haveKept) sb.Append(';');
                    sb.Append(Mm(a.x)).Append(',')
                      .Append(Mm(a.y)).Append(',')
                      .Append(Mm(a.z));
                    kept++;
                    haveKept = true;
                    lastKeptTime = now;
                }

                bool windowElapsed = (now - startTime) * 1000f >= WindowMs;
                if (windowElapsed || kept >= MaxFrames)
                    break;

                yield return null;
            }

            int elapsedMs = (int)((Time.unscaledTime - startTime) * 1000f);
            onDone?.Invoke(new MotionData
            {
                v = 1,
                elapsed_ms = elapsedMs,
                events = events,
                has_accel = !inputThrew && haveKept,
                has_gyro = hasGyro,
                s = sb.ToString()
            });
        }

        /// <summary>Raw acceleration (g's) to clamped integer millimeters/s^2.</summary>
        private static int Mm(float value)
        {
            long scaled = (long)Math.Round(value * GToMetersPerSecondSquared * 1000.0);
            if (scaled > ClampMm) return ClampMm;
            if (scaled < -ClampMm) return -ClampMm;
            return (int)scaled;
        }
    }
}
