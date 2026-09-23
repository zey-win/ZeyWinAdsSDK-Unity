using UnityEditor;
using UnityEngine;

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

        // Custom Proguard File must be on whenever minify is on: proguard-user.txt carries the
        // -keep rule for com.zeywinads.unity.* (called by name via JNI/reflection from
        // AndroidJniSafe, invisible to R8's static analysis without it). Minify on with this off
        // means R8 is free to strip or rename those classes — a runtime ClassNotFoundException
        // instead of a build failure, so this needs its own explicit gate rather than relying on
        // minify's own check to imply it.
        //
        // useCustomProguardFile has no public PlayerSettings.Android property (confirmed: CS0117
        // on PlayerSettings.Android.useCustomProguardFile) and PlayerSettings.GetSerializedObject()
        // doesn't exist either (confirmed: CS0117 there too). PlayerSettings is itself a
        // UnityEngine.Object-derived singleton asset — Resources.FindObjectsOfTypeAll<PlayerSettings>()
        // is the standard way to get that instance to wrap in a SerializedObject and reach fields
        // with no public accessor, same as the raw "useCustomProguardFile" key seen directly in
        // ProjectSettings.asset.
        public static string ValidateCustomProguardFileWhenMinified()
        {
            if (!PlayerSettings.Android.minifyRelease)
                return null;

            var settingsAssets = Resources.FindObjectsOfTypeAll<PlayerSettings>();
            SerializedProperty prop = null;
            if (settingsAssets != null && settingsAssets.Length > 0)
                prop = new SerializedObject(settingsAssets[0]).FindProperty("useCustomProguardFile");

            if (prop == null || !prop.boolValue)
            {
                return "Android Minify Release is on but Custom Proguard File is off — set " +
                    "Project Settings > Player > Android > Publishing Settings > Custom Proguard " +
                    "File, so proguard-user.txt's ZeyWinAds keep rule actually applies.";
            }
            return null;
        }
    }
}
