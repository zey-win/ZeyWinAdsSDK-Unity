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

    // Single registry of every pre-build QA check (API level today, more later). Both
    // QaTestsPreProcessor (build-blocking) and the "Run QA Checks" button in the
    // ZeyWinAdsSettings inspector call RunAll() so there is exactly one place that lists which
    // checks exist.
    public static class QaChecksRunner
    {
        public static List<QaCheckResult> RunAll()
        {
            var results = new List<QaCheckResult>
            {
                new QaCheckResult("Target SDK version", ApiLevelPolicy.ValidateTargetSdk()),
                new QaCheckResult("Min SDK version", ApiLevelPolicy.ValidateMinSdk()),
                // Future pre-build QA checks (e.g. adaptive icon) add another entry here.
            };
            return results;
        }
    }
}
