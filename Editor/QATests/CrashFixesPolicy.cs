using System.IO;

namespace ZeyWinAds.Editor.QATests
{
    // Guards against two specific, previously-shipped crashes silently regressing (both found
    // and fixed in the same session as MinifyPolicy — see SDKCrashes.md and each game's own
    // CLAUDE.md for the incident write-ups):
    //
    //   1. ZeyWinAds' JNI bridge (com.zeywinads.unity.*) is called by name from native/C# code,
    //      invisible to R8's static analysis — without a -keep rule, Minify can silently strip or
    //      rename it, causing a NoSuchMethodError/ClassNotFoundException at runtime.
    //      FactoryBuildPreprocessor.StageMinifyKeepRules() auto-stages this rule on every Android
    //      build, but only right before a build runs — this check catches the case where nothing
    //      has built yet since a revert removed it (exactly what happened this session).
    //
    //   2. play-services-ads (GoogleMobileAds) has an implicit runtime dependency on
    //      androidx.work.impl.WorkDatabase that Unity's Android Resolver doesn't pull in
    //      automatically. Without it, the app crashes on every launch with "Failed to create an
    //      instance of androidx.work.impl.WorkDatabase" inside androidx.startup.InitializationProvider
    //      — confirmed on-device this session, fixed by declaring androidx.work:work-runtime
    //      explicitly in Assets/Editor/WorkManagerDependencies.xml. Only checked for games that
    //      actually use com.google.ads.mobile — a game without AdMob never hits this crash.
    //
    // ValidateAll is the single source of truth for this policy: both the EditMode test
    // (CrashFixesPolicyTests) and QaTestsPreProcessor call this same method.
    public static class CrashFixesPolicy
    {
        private const string ProguardUserPath = "Assets/Plugins/Android/proguard-user.txt";
        private const string ZeyWinAdsKeepRule = "-keep class com.zeywinads.unity.** { *; }";

        private const string WorkManagerDepsPath = "Assets/Editor/WorkManagerDependencies.xml";
        private const string WorkManagerSpec = "androidx.work:work-runtime";

        private const string ManifestPath = "Packages/manifest.json";
        private const string AdMobPackageId = "com.google.ads.mobile";

        // Returns null when compliant, otherwise a human-readable error message.
        public static string ValidateAll()
        {
            var proguardError = ValidateZeyWinAdsKeepRule();
            if (proguardError != null)
                return proguardError;

            return ValidateWorkManagerFix();
        }

        private static string ValidateZeyWinAdsKeepRule()
        {
            if (!File.Exists(ProguardUserPath) || !File.ReadAllText(ProguardUserPath).Contains(ZeyWinAdsKeepRule))
            {
                return $"'{ProguardUserPath}' is missing the ZeyWinAds JNI keep rule " +
                    $"('{ZeyWinAdsKeepRule}') — without it, Minify can strip or rename the JNI " +
                    "bridge and crash at runtime. Run a build once (FactoryBuildPreprocessor " +
                    "auto-stages it), or add it manually.";
            }
            return null;
        }

        private static string ValidateWorkManagerFix()
        {
            if (!UsesAdMob())
                return null; // The WorkManager crash only affects games using play-services-ads.

            if (!File.Exists(WorkManagerDepsPath) || !File.ReadAllText(WorkManagerDepsPath).Contains(WorkManagerSpec))
            {
                return $"This project uses {AdMobPackageId} but '{WorkManagerDepsPath}' is missing " +
                    $"or doesn't declare '{WorkManagerSpec}' — without it, the app crashes on every " +
                    "launch (\"Failed to create an instance of androidx.work.impl.WorkDatabase\"). " +
                    "See SDKCrashes.md / this project's CLAUDE.md for the fix.";
            }
            return null;
        }

        private static bool UsesAdMob() =>
            File.Exists(ManifestPath) && File.ReadAllText(ManifestPath).Contains(AdMobPackageId);
    }
}
