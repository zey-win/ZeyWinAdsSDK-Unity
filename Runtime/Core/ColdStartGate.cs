namespace ZeyWinAds.Core
{
    /// <summary>
    /// Coordinates Android cold-start ordering so native/reflective Java calls (ours, Unity's own
    /// engine APIs, or third-party plugins like AdMob/Firebase) don't contend for ART's fragile
    /// first-touch class resolution and trigger an uncatchable "JNI DETECTED ERROR IN
    /// APPLICATION: jlr_method == null" SIGABRT. See SDKCrashes.md for the full investigation and
    /// why a flat delay alone / device-memory checks / preloading classes via FindClass do NOT
    /// fix this — only true isolation (nothing else running at the same time) does.
    ///
    /// Rule: AdMob/Firebase/any future third-party native init must call
    /// <see cref="MarkThirdPartyInitStarted"/> the moment it fires. Any of our own code that does
    /// its own native/reflective work during cold start (SecurityCheck, DeviceIdentity,
    /// WebViewLock, referral/attribution checks, ad preloading, or anything added later) must
    /// yield on <see cref="WaitUntilSafeForOwnNativeWork"/> before doing that work, instead of
    /// hand-rolling a new delay.
    ///
    /// This bug is Android-only (ART/JNI reflection behavior has no iOS equivalent), so the actual
    /// wait only happens on Android — on iOS/Editor this is a no-op. Both platforms call the same
    /// two functions from shared cross-platform code; the platform difference is handled entirely
    /// inside this class.
    /// </summary>
    internal static class ColdStartGate
    {
        // Single source of truth for the head-start gap — do not duplicate this constant
        // elsewhere. Tuned empirically (2026-09-17, on-device testing on a Samsung SM-A146P);
        // see SDKCrashes.md before changing it.
        private const float ThirdPartyHeadStartSeconds = 2.5f;

        private static bool _thirdPartyInitStarted;

        /// <summary>Call the moment third-party native init (AdMob, Firebase, ...) fires.</summary>
        internal static void MarkThirdPartyInitStarted()
        {
            _thirdPartyInitStarted = true;
        }

        /// <summary>
        /// Yield on this before doing any of our own native/reflective work during cold start.
        /// Warns (does not block release builds) if third-party init was never marked started,
        /// since that means this is being called out of the intended order.
        /// </summary>
        internal static System.Collections.IEnumerator WaitUntilSafeForOwnNativeWork()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!_thirdPartyInitStarted)
            {
                Logger.Warn("ColdStartGate: WaitUntilSafeForOwnNativeWork called before " +
                    "MarkThirdPartyInitStarted — third-party init may not get its isolation " +
                    "window. See SDKCrashes.md.");
            }

            yield return new UnityEngine.WaitForSecondsRealtime(ThirdPartyHeadStartSeconds);
#else
            yield return null;
#endif
        }
    }
}
