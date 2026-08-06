package com.zeywinads.unity;

import android.content.Context;
import android.hardware.Sensor;
import android.hardware.SensorEvent;
import android.hardware.SensorEventListener;
import android.hardware.SensorManager;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.SystemClock;

import com.unity3d.player.UnityPlayer;

import org.json.JSONException;
import org.json.JSONObject;

import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Collects a short window of accelerometer samples for the anti-fraud motion signal.
 * Real devices show natural jitter even at rest; emulators tend to report a constant
 * value, a repeating triplet, or no accelerometer at all.
 */
public class ZeyWinAdsMotionCollector {

    private static final int MAX_FRAMES = 32;
    private static final long MIN_FRAME_GAP_MS = 60L;
    private static final long WINDOW_MS = 2000L;

    /**
     * Samples the accelerometer for up to WINDOW_MS (or MAX_FRAMES, whichever comes first)
     * on a dedicated background thread, then sends the result to Unity via UnitySendMessage.
     * Never blocks the calling thread. If there's no accelerometer, calls back immediately.
     *
     * @param gameObjectName Unity GameObject name to receive the callback
     * @param callbackMethod Unity method name to call with the JSON-encoded motion block
     */
    public static void collect(final String gameObjectName, final String callbackMethod) {
        Context context = UnityPlayer.currentActivity.getApplicationContext();
        final SensorManager sm = (SensorManager) context.getSystemService(Context.SENSOR_SERVICE);
        final Sensor accel = sm != null ? sm.getDefaultSensor(Sensor.TYPE_ACCELEROMETER) : null;
        final boolean hasGyro = sm != null && sm.getDefaultSensor(Sensor.TYPE_GYROSCOPE) != null;

        if (sm == null || accel == null) {
            sendResult(gameObjectName, callbackMethod, 0, 0, false, hasGyro, "");
            return;
        }

        final HandlerThread thread = new HandlerThread("ZeyWinAdsMotion");
        thread.start();
        final Handler handler = new Handler(thread.getLooper());

        final StringBuilder frames = new StringBuilder();
        final int[] kept = {0};
        final int[] events = {0};
        final long[] lastKeptMs = {0L};
        final long startedMs = SystemClock.elapsedRealtime();
        final AtomicBoolean finished = new AtomicBoolean(false);
        final SensorEventListener[] listenerHolder = new SensorEventListener[1];

        final Runnable finisher = new Runnable() {
            @Override
            public void run() {
                if (!finished.compareAndSet(false, true)) return;
                sm.unregisterListener(listenerHolder[0]);
                thread.quitSafely();
                int elapsedMs = (int) (SystemClock.elapsedRealtime() - startedMs);
                sendResult(gameObjectName, callbackMethod, elapsedMs, events[0], true, hasGyro, frames.toString());
            }
        };

        listenerHolder[0] = new SensorEventListener() {
            @Override
            public void onSensorChanged(SensorEvent e) {
                events[0]++; // counts every callback, not just kept frames
                long nowMs = SystemClock.elapsedRealtime();
                if (kept[0] > 0 && nowMs - lastKeptMs[0] < MIN_FRAME_GAP_MS) return;
                lastKeptMs[0] = nowMs;

                if (kept[0] > 0) frames.append(';');
                frames.append(mm(e.values[0])).append(',')
                        .append(mm(e.values[1])).append(',')
                        .append(mm(e.values[2]));
                kept[0]++;

                if (kept[0] >= MAX_FRAMES) finisher.run();
            }

            @Override
            public void onAccuracyChanged(Sensor sensor, int accuracy) {}
        };

        sm.registerListener(listenerHolder[0], accel, SensorManager.SENSOR_DELAY_GAME, handler);
        handler.postDelayed(finisher, WINDOW_MS); // window always closes, whichever trigger comes first
    }

    private static void sendResult(String gameObjectName, String callbackMethod, int elapsedMs, int events,
                                    boolean hasAccel, boolean hasGyro, String s) {
        try {
            JSONObject json = new JSONObject();
            json.put("v", 1);
            json.put("elapsed_ms", elapsedMs);
            json.put("events", events);
            json.put("has_accel", hasAccel);
            json.put("has_gyro", hasGyro);
            json.put("s", s);
            UnityPlayer.UnitySendMessage(gameObjectName, callbackMethod, json.toString());
        } catch (JSONException e) {
            // Malformed result — drop it. The C# side never receives a callback, which is
            // the same safe "phase 2 lost" outcome as a network failure.
        }
    }

    /** m/s^2 as an integer millimeter/s^2, clamped to the range the server accepts. */
    private static int mm(float value) {
        long scaled = Math.round(value * 1000f);
        if (scaled > 40000) return 40000;
        if (scaled < -40000) return -40000;
        return (int) scaled;
    }
}
