using System.Collections.Generic;

namespace ZeyWinAds.Editor.QATests
{
    // One named result from a single QA check.
    public struct QaCheckResult
    {
        public readonly string Name;
        public readonly string Error; // null when the check passed

        public bool Passed => Error == null;

        public QaCheckResult(string name, string error)
        {
            Name = name;
            Error = error;
        }
    }

    // Single registry of every pre-build QA check. Both QaTestsPreProcessor (build-blocking) and
    // the "Run QA Checks" button in the ZeyWinAdsSettings inspector call RunAll() so there is
    // exactly one place that lists which checks exist.
    public static class QaChecksRunner
    {
        public static List<QaCheckResult> RunAll()
        {
            var results = new List<QaCheckResult>
            {
                new QaCheckResult("Target SDK version", ApiLevelPolicy.ValidateTargetSdk()),
                new QaCheckResult("Min SDK version", ApiLevelPolicy.ValidateMinSdk()),
                new QaCheckResult("Managed stripping level", StrippingLevelPolicy.ValidateStrippingLevel()),
                new QaCheckResult("Android launcher icons", LauncherIconPolicy.ValidateAndroidIcons()),
                // Minify/R8 suspected of causing recurring CI runner deaths (Wheel-Fortune,
                // 2026-09-29) — commented out for some period, not removed. Re-enable together
                // with FactoryBuildPreprocessor's EnsureMinifyEnabled/StageMinifyKeepRules. See
                // ZeyWinAdsSDK-Unity/CLAUDE.md before re-enabling.
                // new QaCheckResult("Minify Release", MinifyPolicy.ValidateMinifyRelease()),
                // new QaCheckResult("Custom proguard file (required when minify is on)", MinifyPolicy.ValidateCustomProguardFileWhenMinified()),
                new QaCheckResult("Known crash fixes present", CrashFixesPolicy.ValidateAll()),
                new QaCheckResult("Play Games on PC manifest compatibility", PlayGamesOnPcPolicy.ValidateSourceManifest()),
                // Future pre-build QA checks add another entry here.
            };
            return results;
        }
    }
}
