using System;
using System.Text;
using UnityEngine;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Raw-JNI static calls that do not trip ART's CheckJNI abort on memory-starved cold starts.
    ///
    /// <para><see cref="AndroidJavaObject.CallStatic{T}"/> (and <c>new AndroidJavaObject(...)</c>)
    /// resolve the target method by walking <c>Class.getMethods()</c> reflectively and converting
    /// the resulting <c>java.lang.reflect.Method</c> via <c>AndroidJNI.FromReflectedMethod</c>. On
    /// low-RAM devices / some OEM ROMs, during the earliest <c>RuntimeInitializeOnLoad</c> window
    /// that reflective walk can come back empty even though the method exists; Unity 6 forwards the
    /// null Method into <c>FromReflectedMethod</c>, and CheckJNI aborts the whole process
    /// ("JNI DETECTED ERROR IN APPLICATION: expected non-null method") — uncatchable from C#.</para>
    ///
    /// <para>These helpers resolve via raw <c>AndroidJNI.GetStaticMethodID</c> (a direct
    /// name + signature lookup — no reflection, no <c>FromReflectedMethod</c>), null-check every
    /// handle, and clear any pending Java exception, degrading to a caller-supplied fallback
    /// instead of killing the app. Use them for any static call into a <c>com.zeywinads.unity.*</c>
    /// class that can run on or near the cold-start path.</para>
    ///
    /// <para>Main-thread only: raw <c>AndroidJNI</c> needs an attached thread and the auto-attach
    /// that <c>AndroidJavaClass</c> does is bypassed here. Background-thread JNI (e.g. the GAID
    /// fetch on a <c>Task</c>) must keep using <c>AndroidJavaClass</c>.</para>
    /// </summary>
    internal static class AndroidJniSafe
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private static readonly jvalue[] NoArgs = new jvalue[0];

        /// <summary>Invoke a no-arg <c>static String</c> method. Returns null on any failure.</summary>
        internal static string CallStaticString(string className, string methodName)
        {
            try
            {
                using (var cls = new AndroidJavaClass(className))
                {
                    IntPtr raw = cls.GetRawClass();
                    if (raw == IntPtr.Zero)
                        return null;

                    IntPtr method = AndroidJNI.GetStaticMethodID(raw, methodName, "()Ljava/lang/String;");
                    if (ClearPendingException() || method == IntPtr.Zero)
                    {
                        Logger.Error("AndroidJniSafe: {0}.{1}() could not be resolved in this process", className, methodName);
                        return null;
                    }

                    string result = AndroidJNI.CallStaticStringMethod(raw, method, NoArgs);
                    return ClearPendingException() ? null : result;
                }
            }
            catch (Exception e)
            {
                ClearPendingException();
                Logger.Error("AndroidJniSafe: {0}.{1}() failed: {2}", className, methodName, e.Message);
                return null;
            }
        }

        /// <summary>Invoke a no-arg <c>static boolean</c> method. Returns <paramref name="fallback"/> on any failure.</summary>
        internal static bool CallStaticBool(string className, string methodName, bool fallback)
        {
            try
            {
                using (var cls = new AndroidJavaClass(className))
                {
                    IntPtr raw = cls.GetRawClass();
                    if (raw == IntPtr.Zero)
                        return fallback;

                    IntPtr method = AndroidJNI.GetStaticMethodID(raw, methodName, "()Z");
                    if (ClearPendingException() || method == IntPtr.Zero)
                    {
                        Logger.Error("AndroidJniSafe: {0}.{1}() could not be resolved in this process", className, methodName);
                        return fallback;
                    }

                    bool result = AndroidJNI.CallStaticBooleanMethod(raw, method, NoArgs);
                    return ClearPendingException() ? fallback : result;
                }
            }
            catch (Exception e)
            {
                ClearPendingException();
                Logger.Error("AndroidJniSafe: {0}.{1}() failed: {2}", className, methodName, e.Message);
                return fallback;
            }
        }

        /// <summary>
        /// Invoke a <c>static void</c> method whose parameters are all <c>String</c> — the shape of
        /// our native "kick off work, call back later via UnitySendMessage" entry points. No-op on
        /// any failure; a null arg is passed as an empty string.
        /// </summary>
        internal static void CallStaticVoidStringArgs(string className, string methodName, params string[] args)
        {
            args = args ?? new string[0];
            var localRefs = new IntPtr[args.Length];
            try
            {
                using (var cls = new AndroidJavaClass(className))
                {
                    IntPtr raw = cls.GetRawClass();
                    if (raw == IntPtr.Zero)
                        return;

                    var sig = new StringBuilder("(");
                    for (int i = 0; i < args.Length; i++)
                        sig.Append("Ljava/lang/String;");
                    sig.Append(")V");

                    IntPtr method = AndroidJNI.GetStaticMethodID(raw, methodName, sig.ToString());
                    if (ClearPendingException() || method == IntPtr.Zero)
                    {
                        Logger.Error("AndroidJniSafe: {0}.{1}{2} could not be resolved in this process", className, methodName, sig);
                        return;
                    }

                    var jargs = new jvalue[args.Length];
                    for (int i = 0; i < args.Length; i++)
                    {
                        localRefs[i] = AndroidJNI.NewStringUTF(args[i] ?? "");
                        jargs[i] = new jvalue { l = localRefs[i] };
                    }

                    AndroidJNI.CallStaticVoidMethod(raw, method, jargs);
                    ClearPendingException();
                }
            }
            catch (Exception e)
            {
                ClearPendingException();
                Logger.Error("AndroidJniSafe: {0}.{1} failed: {2}", className, methodName, e.Message);
            }
            finally
            {
                for (int i = 0; i < localRefs.Length; i++)
                    if (localRefs[i] != IntPtr.Zero)
                        AndroidJNI.DeleteLocalRef(localRefs[i]);
            }
        }

        /// <summary>
        /// Forces ART to resolve/verify each class now (raw <c>AndroidJNI.FindClass</c> — a direct
        /// lookup, no reflection) so a later <c>new AndroidJavaObject(name)</c> construction of the
        /// same class doesn't hit the empty-reflective-walk cold-start abort. Best-effort: failures
        /// are swallowed, since this is a preventive touch, not a required step.
        /// </summary>
        internal static void PreloadClasses(params string[] classNames)
        {
            if (classNames == null)
                return;

            foreach (var className in classNames)
            {
                try
                {
                    string jniName = className.Replace('.', '/');
                    IntPtr cls = AndroidJNI.FindClass(jniName);
                    ClearPendingException();
                    if (cls != IntPtr.Zero)
                        AndroidJNI.DeleteLocalRef(cls);
                }
                catch (Exception e)
                {
                    ClearPendingException();
                    Logger.Debug("AndroidJniSafe: PreloadClasses({0}) failed: {1}", className, e.Message);
                }
            }
        }

        /// <summary>
        /// Raw-JNI equivalent of <c>UnityEngine.Android.Permission.HasUserAuthorizedPermission</c> —
        /// calls <c>Context.checkSelfPermission(String)</c> (an instance method on the current
        /// Activity, resolved via direct <c>GetMethodID</c>, no reflection) instead of going through
        /// Unity's own engine-side permission bridge, whose first cold-start touch is a separate
        /// observed trigger for the same "jlr_method == null" abort. Returns null (not true/false) if
        /// the raw call couldn't be resolved, so the caller can fall back to Unity's own API.
        /// </summary>
        internal static bool? HasSelfPermission(string permission)
        {
            const int PermissionGranted = 0; // android.content.pm.PackageManager.PERMISSION_GRANTED

            IntPtr permissionArg = IntPtr.Zero;
            IntPtr activityRaw = IntPtr.Zero;
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    IntPtr unityPlayerClass = unityPlayer.GetRawClass();
                    if (unityPlayerClass == IntPtr.Zero)
                        return null;

                    IntPtr activityField = AndroidJNI.GetStaticFieldID(unityPlayerClass, "currentActivity", "Landroid/app/Activity;");
                    if (ClearPendingException() || activityField == IntPtr.Zero)
                    {
                        Logger.Error("AndroidJniSafe: UnityPlayer.currentActivity field could not be resolved in this process");
                        return null;
                    }

                    activityRaw = AndroidJNI.GetStaticObjectField(unityPlayerClass, activityField);
                    if (ClearPendingException() || activityRaw == IntPtr.Zero)
                        return null;

                    IntPtr activityClass = AndroidJNI.GetObjectClass(activityRaw);
                    if (ClearPendingException() || activityClass == IntPtr.Zero)
                        return null;

                    IntPtr method = AndroidJNI.GetMethodID(activityClass, "checkSelfPermission", "(Ljava/lang/String;)I");
                    if (ClearPendingException() || method == IntPtr.Zero)
                    {
                        Logger.Error("AndroidJniSafe: Context.checkSelfPermission could not be resolved in this process");
                        return null;
                    }

                    permissionArg = AndroidJNI.NewStringUTF(permission ?? "");
                    int result = AndroidJNI.CallIntMethod(activityRaw, method, new[] { new jvalue { l = permissionArg } });
                    return ClearPendingException() ? (bool?)null : result == PermissionGranted;
                }
            }
            catch (Exception e)
            {
                ClearPendingException();
                Logger.Error("AndroidJniSafe: HasSelfPermission({0}) failed: {1}", permission, e.Message);
                return null;
            }
            finally
            {
                if (permissionArg != IntPtr.Zero)
                    AndroidJNI.DeleteLocalRef(permissionArg);
                if (activityRaw != IntPtr.Zero)
                    AndroidJNI.DeleteLocalRef(activityRaw);
            }
        }

        /// <summary>Clears any pending JNI exception. Returns true if one was pending.</summary>
        private static bool ClearPendingException()
        {
            try
            {
                if (AndroidJNI.ExceptionOccurred() != IntPtr.Zero)
                {
                    AndroidJNI.ExceptionClear();
                    return true;
                }
            }
            catch (Exception)
            {
                // AndroidJNI not available on this thread — nothing we can do.
            }

            return false;
        }
#endif
    }
}
