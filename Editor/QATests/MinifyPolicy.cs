using UnityEditor;

namespace ZeyWinAds.Editor.QATests
{
    // Minify (R8) Release should be on for every shipped build — it's the single biggest lever on
    // dex size (collapsed Plinko_V1's dex from ~19.9 MB across 3 files to ~6.4 MB in one, this
    // session). It's also fragile: the Player Settings checkbox is easy to leave off after a
    // project revert or a fresh checkout, and there is no other guard that would catch a build
    // silently shipping without it.
    //
    // ValidateMinifyRelease is the single source of truth for this policy: both the EditMode test
    // (MinifyPolicyTests) and QaTestsPreProcessor call this same method.
    public static class MinifyPolicy
    {
        // Returns null when compliant, otherwise a human-readable error message.
        public static string ValidateMinifyRelease()
        {
            if (!PlayerSettings.Android.minifyRelease)
            {
                return "Android Minify Release is off — set Project Settings > Player > Android > " +
                    "Publishing Settings > Minify > Release to Proguard.";
            }
            return null;
        }
    }
}
