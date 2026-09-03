using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using UnityEngine;

namespace ZeyWinAds.Tests.Runtime
{
    // Saves a PNG of the REAL composited device screen so a *passing* run leaves visual proof on
    // disk (upload it as a CI artifact). Unity's ScreenCapture.CaptureScreenshot only sees Unity's
    // own frame buffer — it would miss the two things worth proving here: the SDK's native loading
    // overlay and the native offer WebView, both of which are Android Views layered on top of
    // Unity. This copies the Activity window's surface with android.view.PixelCopy (API 26+; the
    // QA device is API 33), native layers included.
    //
    // Lifecycle:
    //   * a capture writes  "<name>.pending.png"
    //   * the owning test's [TearDown] calls ResolveForCurrentTest(name), which renames it to
    //     "<name>.png" iff the test passed and deletes it otherwise.
    //
    // Output folder: <persistentDataPath>/qa-screenshots/
    //   on Android:  /sdcard/Android/data/<package>/files/qa-screenshots/
    //   collect with:  adb pull /sdcard/Android/data/<package>/files/qa-screenshots ./qa-artifacts
    //
    // Every failure path here is logged, never thrown — a capture problem must not turn a passing
    // SDK check red.
    internal static class QaScreenshot
    {
        public static string Folder => Path.Combine(Application.persistentDataPath, "qa-screenshots");
        public static string PendingPath(string name) => Path.Combine(Folder, name + ".pending.png");
        public static string FinalPath(string name) => Path.Combine(Folder, name + ".png");

        // Mutated from the PixelCopy callback (Android main thread) and read from a coroutine /
        // the calling thread — volatile so the poll loop actually sees the completion.
        private sealed class CaptureState
        {
            public volatile bool Done;
            public volatile bool Ok;
            public volatile string Error;
        }

        /// <summary>
        /// Fire-and-forget capture for callers with no coroutine (e.g. QaLoadingOverlayRecorder,
        /// which must grab the overlay the instant it appears). Returns immediately; the PNG lands
        /// a frame or two later.
        /// </summary>
        public static void CaptureDetached(string name)
        {
            try
            {
                Request(name, new CaptureState());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[QaScreenshot] '{name}' capture could not start: {e.Message}");
            }
        }

        /// <summary>
        /// Coroutine capture: kicks off the PixelCopy and yields (real time) until it finishes or
        /// <paramref name="timeoutSeconds"/> elapses. Never fails the test.
        /// </summary>
        public static IEnumerator Capture(string name, float timeoutSeconds = 5f)
        {
            var state = new CaptureState();
            bool started = false;
            try
            {
                Request(name, state);
                started = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[QaScreenshot] '{name}' capture could not start: {e.Message}");
            }

            if (!started)
                yield break;

            float t0 = Time.realtimeSinceStartup;
            while (!state.Done && Time.realtimeSinceStartup - t0 < timeoutSeconds)
                yield return null;

            if (!state.Done)
                Debug.LogWarning($"[QaScreenshot] '{name}' capture timed out after {timeoutSeconds:F0}s.");
            else if (state.Error != null)
                Debug.LogWarning($"[QaScreenshot] '{name}' capture failed: {state.Error}");
            // on success the "screenshot saved -> <path>" log comes from the PixelCopy callback;
            // no Error and not Ok => not an Android player build, nothing to say
        }

        /// <summary>
        /// Call from a fixture [TearDown]. No-op unless a "&lt;name&gt;.pending.png" exists for this
        /// run. Promotes it to "&lt;name&gt;.png" if the just-finished test passed; deletes it
        /// otherwise.
        /// </summary>
        public static void ResolveForCurrentTest(string name)
        {
            string pending = PendingPath(name);
            if (!File.Exists(pending))
                return;

            TestStatus status = TestContext.CurrentContext.Result.Outcome.Status;
            try
            {
                if (status == TestStatus.Passed)
                {
                    string final = FinalPath(name);
                    if (File.Exists(final))
                        File.Delete(final);
                    File.Move(pending, final);
                    Debug.Log($"[QaScreenshot] '{name}' kept ({status}) -> {final}");
                }
                else
                {
                    File.Delete(pending);
                    Debug.Log($"[QaScreenshot] discarded '{name}' — test outcome was {status}.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[QaScreenshot] resolve '{name}' failed: {e.Message}");
            }
        }

        private static void Request(string name, CaptureState state)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Directory.CreateDirectory(Folder);
            string outPath = PendingPath(name);

            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                if (activity == null)
                {
                    state.Error = "UnityPlayer.currentActivity is null";
                    state.Done = true;
                    return;
                }

                // PixelCopy and its Handler must be driven from a Looper thread; use the Android
                // main thread. The PNG encode then also runs there (~tens of ms for a phone-sized
                // frame) — fine for a QA capture and it keeps the AndroidJavaProxy callback on a
                // thread Unity's marshalling fully supports.
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                    CaptureOnUiThread(activity, outPath, state)));
            }
#else
            state.Done = true; // not an Android player build — nothing to capture
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // PixelCopy calls the listener back asynchronously (~a frame later). Nothing on the managed
        // side references the listener once CaptureOnUiThread returns, so root it here until it
        // fires, otherwise GC can collect it (and its captured Bitmap wrapper) mid-copy.
        private static readonly HashSet<object> _pendingListeners = new HashSet<object>();

        private static void CaptureOnUiThread(AndroidJavaObject activity, string outPath, CaptureState state)
        {
            AndroidJavaObject bitmap = null;
            try
            {
                var window = activity.Call<AndroidJavaObject>("getWindow");
                var decor = window.Call<AndroidJavaObject>("getDecorView");
                int w = decor.Call<int>("getWidth");
                int h = decor.Call<int>("getHeight");
                if (w <= 0 || h <= 0)
                {
                    state.Error = $"decor view has no size yet ({w}x{h})";
                    state.Done = true;
                    return;
                }

                using (var configCls = new AndroidJavaClass("android.graphics.Bitmap$Config"))
                using (var argb = configCls.GetStatic<AndroidJavaObject>("ARGB_8888"))
                using (var bitmapCls = new AndroidJavaClass("android.graphics.Bitmap"))
                    bitmap = bitmapCls.CallStatic<AndroidJavaObject>("createBitmap", w, h, argb);

                AndroidJavaObject handler;
                using (var looperCls = new AndroidJavaClass("android.os.Looper"))
                using (var mainLooper = looperCls.CallStatic<AndroidJavaObject>("getMainLooper"))
                    handler = new AndroidJavaObject("android.os.Handler", mainLooper);

                var bitmapForCallback = bitmap;
                PixelCopyListener listener = null;
                listener = new PixelCopyListener(result =>
                {
                    try
                    {
                        if (result == 0) // PixelCopy.SUCCESS
                        {
                            using (var fos = new AndroidJavaObject("java.io.FileOutputStream", outPath))
                            using (var fmtCls = new AndroidJavaClass("android.graphics.Bitmap$CompressFormat"))
                            using (var png = fmtCls.GetStatic<AndroidJavaObject>("PNG"))
                            {
                                bitmapForCallback.Call<bool>("compress", png, 100, fos);
                                fos.Call("flush");
                            }
                            state.Ok = true;
                            Debug.Log($"[QaScreenshot] screenshot saved -> {outPath}");
                        }
                        else
                        {
                            state.Error = "PixelCopy result code " + result;
                        }
                    }
                    catch (Exception e)
                    {
                        state.Error = "write failed: " + e.Message;
                    }
                    finally
                    {
                        try { bitmapForCallback.Call("recycle"); } catch { /* best effort */ }
                        lock (_pendingListeners) _pendingListeners.Remove(listener);
                        state.Done = true;
                    }
                });
                lock (_pendingListeners) _pendingListeners.Add(listener);

                // PixelCopy.request(Window, Bitmap, OnPixelCopyFinishedListener, Handler).
                // Called through low-level JNI with an explicit signature: AndroidJavaObject would
                // derive arg 0's type from window's concrete class (com.android.internal.policy.
                // PhoneWindow) and fail to match the Window-typed overload.
                IntPtr cls = AndroidJNI.FindClass("android/view/PixelCopy");
                IntPtr mid = AndroidJNI.GetStaticMethodID(cls, "request",
                    "(Landroid/view/Window;Landroid/graphics/Bitmap;" +
                    "Landroid/view/PixelCopy$OnPixelCopyFinishedListener;Landroid/os/Handler;)V");

                IntPtr listenerRef = AndroidJNIHelper.CreateJavaProxy(listener);

                var args = new jvalue[4];
                args[0].l = window.GetRawObject();
                args[1].l = bitmap.GetRawObject();
                args[2].l = listenerRef;
                args[3].l = handler.GetRawObject();
                AndroidJNI.CallStaticVoidMethod(cls, mid, args);

                AndroidJNI.DeleteLocalRef(listenerRef);
                AndroidJNI.DeleteLocalRef(cls);
            }
            catch (Exception e)
            {
                state.Error = e.Message;
                if (bitmap != null)
                {
                    try { bitmap.Call("recycle"); } catch { /* best effort */ }
                }
                state.Done = true;
            }
        }

        private sealed class PixelCopyListener : AndroidJavaProxy
        {
            private readonly Action<int> _onFinished;

            public PixelCopyListener(Action<int> onFinished)
                : base("android.view.PixelCopy$OnPixelCopyFinishedListener")
            {
                _onFinished = onFinished;
            }

            // Called by Android with PixelCopy.SUCCESS (0) or an ERROR_* code.
            public void onPixelCopyFinished(int copyResult) => _onFinished(copyResult);
        }
#endif
    }
}
