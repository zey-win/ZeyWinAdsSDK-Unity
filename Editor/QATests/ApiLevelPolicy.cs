using UnityEditor;

namespace ZeyWinAds.Editor.QATests
{
    // Update MinRequiredTargetSdk when Google Play raises its minimum targetSdk requirement.
    // Update RequiredMinSdk only if a bundled native dependency (Firebase, AdMob, EDM4U AARs,
    // UniWebView) raises its own minimum supported Android version.
    //
    // ValidateTargetSdk/ValidateMinSdk are the single source of truth for this policy: both the
    // EditMode test suite (ApiLevelPolicyTests) and the build preprocessor
    // (ApiLevelPolicyBuildCheck) call these same methods, so a local build and CI enforce
    // identical rules without duplicating the checks.
    public static class ApiLevelPolicy
    {
        public const int MinRequiredTargetSdk = 36; // Google Play's current minimum (Android 16)
        public const int RequiredMinSdk = 25;        // lowest level bundled deps actually support

        // Returns null when compliant, otherwise a human-readable error message.
        public static string ValidateTargetSdk()
        {
            var target = PlayerSettings.Android.targetSdkVersion;
            if (target == AndroidSdkVersions.AndroidApiLevelAuto)
            {
                return "targetSdkVersion is set to Automatic — pin an explicit API level for reproducible builds.";
            }
            if ((int)target < MinRequiredTargetSdk)
            {
                return $"targetSdkVersion {(int)target} is below the current Play Store minimum ({MinRequiredTargetSdk}).";
            }
            return null;
        }

        // Returns null when compliant, otherwise a human-readable error message.
        public static string ValidateMinSdk()
        {
            var minSdk = (int)PlayerSettings.Android.minSdkVersion;
            if (minSdk != RequiredMinSdk)
            {
                return $"minSdkVersion is {minSdk}, expected exactly {RequiredMinSdk} — " +
                    "raising it needlessly excludes supportable devices; lowering it may break bundled SDKs.";
            }
            return null;
        }
    }
}
