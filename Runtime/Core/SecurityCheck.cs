using System.Runtime.InteropServices;
using UnityEngine;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Checks for debugger, inspector, and hooking tools on the device.
    /// If suspicious apps are found, ad display is blocked.
    /// </summary>
    public static class SecurityCheck
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern string _ZeyWinAds_GetRootIndicators();

        [DllImport("__Internal")]
        private static extern string _ZeyWinAds_GetDetectedPackages();
#endif

        private static bool? _isClean;
        private static bool? _isRooted;
        private static string _detectedPackages;
        private static string _rootIndicators;

        /// <summary>
        /// Returns true if the device is clean (no suspicious apps detected).
        /// Result is cached after first call.
        /// </summary>
        public static bool IsDeviceClean()
        {
            if (_isClean.HasValue)
                return _isClean.Value;

#if UNITY_ANDROID && !UNITY_EDITOR
            _detectedPackages = CallStaticStringSafe("getDetectedPackages") ?? "";
            _isClean = string.IsNullOrEmpty(_detectedPackages);
#elif UNITY_IOS && !UNITY_EDITOR
            try
            {
                _detectedPackages = _ZeyWinAds_GetDetectedPackages() ?? "";
                _isClean = string.IsNullOrEmpty(_detectedPackages);
            }
            catch (System.Exception e)
            {
                Logger.Error("Security check failed: {0}", e.Message);
                _isClean = true; // Don't block on error
                _detectedPackages = "";
            }
#else
            _isClean = true;
            _detectedPackages = "";
#endif

            // No logging — silent check

            return _isClean.Value;
        }

        /// <summary>
        /// Returns comma-separated list of detected suspicious package names.
        /// Empty string if device is clean.
        /// </summary>
        public static string GetDetectedPackages()
        {
            if (_detectedPackages == null)
                IsDeviceClean();
            return _detectedPackages;
        }

        public static bool IsRooted()
        {
            if (_isRooted.HasValue)
                return _isRooted.Value;

#if UNITY_ANDROID && !UNITY_EDITOR
            _rootIndicators = CallStaticStringSafe("getRootIndicators") ?? "";
            _isRooted = !string.IsNullOrEmpty(_rootIndicators);
#elif UNITY_IOS && !UNITY_EDITOR
            try
            {
                _rootIndicators = _ZeyWinAds_GetRootIndicators() ?? "";
                _isRooted = !string.IsNullOrEmpty(_rootIndicators);
            }
            catch (System.Exception e)
            {
                Logger.Error("Root check failed: {0}", e.Message);
                _isRooted = false;
                _rootIndicators = "";
            }
#else
            _isRooted = false;
            _rootIndicators = "";
#endif

            return _isRooted.Value;
        }

        public static string GetRootIndicators()
        {
            if (_rootIndicators == null)
                IsRooted();
            return _rootIndicators;
        }

        /// <summary>
        /// Clears cached result to force re-check.
        /// </summary>
        public static void ClearCache()
        {
            _isClean = null;
            _isRooted = null;
            _detectedPackages = null;
            _rootIndicators = null;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private const string SecurityCheckClass = "com.zeywinads.unity.ZeyWinAdsSecurityCheck";

        private static readonly jvalue[] NoArgs = new jvalue[0];

        /// <summary>
        /// Invokes a no-arg <c>static String</c> method on the native security-check
        /// class through the raw JNI API, returning <c>null</c> on any failure.
        ///
        /// This deliberately avoids <see cref="AndroidJavaObject.CallStatic{T}"/> /
        /// <c>AndroidJNIHelper.GetMethodID</c>. That path resolves the method by walking
        /// <c>jclass.getMethods()</c> reflectively; when it comes back empty — which it
        /// does on a memory-starved cold start / very early <c>RuntimeInitializeOnLoad</c>
        /// on some OEM ROMs, even though the method is present in the class — Unity 6
        /// forwards a null <c>java.lang.reflect.Method</c> into
        /// <c>AndroidJNI.FromReflectedMethod</c> and ART aborts the whole process with
        /// "JNI DETECTED ERROR IN APPLICATION: jlr_method == null" before the first scene.
        /// That abort is not catchable by the surrounding try/catch. The raw
        /// <c>GetStaticMethodID</c> path instead returns a null id plus a pending Java
        /// exception, which we clear and treat as "check unavailable -&gt; assume clean".
        /// </summary>
        private static string CallStaticStringSafe(string methodName)
        {
            try
            {
                using (var cls = new AndroidJavaClass(SecurityCheckClass))
                {
                    System.IntPtr rawClass = cls.GetRawClass();
                    if (rawClass == System.IntPtr.Zero)
                        return null;

                    System.IntPtr methodId =
                        AndroidJNI.GetStaticMethodID(rawClass, methodName, "()Ljava/lang/String;");
                    if (ClearPendingException() || methodId == System.IntPtr.Zero)
                    {
                        Logger.Error("SecurityCheck: {0}.{1} could not be resolved in this process", SecurityCheckClass, methodName);
                        return null;
                    }

                    string result = AndroidJNI.CallStaticStringMethod(rawClass, methodId, NoArgs);
                    if (ClearPendingException())
                        return null;

                    return result;
                }
            }
            catch (System.Exception e)
            {
                ClearPendingException();
                Logger.Error("SecurityCheck: {0} call failed: {1}", methodName, e.Message);
                return null;
            }
        }

        /// <summary>
        /// Clears any pending JNI exception. Returns true if one was pending.
        /// </summary>
        private static bool ClearPendingException()
        {
            try
            {
                if (AndroidJNI.ExceptionOccurred() != System.IntPtr.Zero)
                {
                    AndroidJNI.ExceptionClear();
                    return true;
                }
            }
            catch (System.Exception)
            {
                // AndroidJNI not available on this thread — nothing we can do.
            }

            return false;
        }
#endif
    }
}
