using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.TestTools;
using UnityEngine;

[assembly: TestPlayerBuildModifier(typeof(ZeyWinAds.Tests.Editor.QaTestPlayerBuildModifier))]

namespace ZeyWinAds.Tests.Editor
{
    // Single entry point for anything the on-device QA test player build needs that differs from
    // a normal build. One TestPlayerBuildModifier attribute is allowed per assembly (same
    // one-per-assembly rule as [assembly: TestRunCallback]), so this is the one place to add
    // future modifications rather than a second class — add a private method per concern below,
    // call it from ModifyOptions, and mirror its restore in OnPostprocessBuild.
    //
    // PlayerSettings values touched here (targetArchitectures, and anything added later) aren't
    // BuildPlayerOptions fields, so they can't be set via the options this method returns — they
    // are side effects, restored in OnPostprocessBuild so a real (non-test) Android build later in
    // the same Editor session never inherits a test-only value. OnPostprocessBuild fires for every
    // Android build, test or not; each restore is guarded so a normal build's callback here is a
    // no-op.
    //
    // Known edge case: if the build fails between ModifyOptions and OnPostprocessBuild (e.g.
    // QaTestsPreProcessor throws on a bad config), an override can be left applied until the next
    // successful build or domain reload. Low risk for what actually ships: every real factory
    // build re-asserts its own real values unconditionally (e.g. FactoryBuildPreprocessor forces
    // ARMv7|ARM64) regardless of prior state, so the ship path self-heals; only a local
    // non-factory build in between could see it, and a rebuild fixes it.
    public sealed class QaTestPlayerBuildModifier : ITestPlayerBuildModifier, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public BuildPlayerOptions ModifyOptions(BuildPlayerOptions playerOptions)
        {
            if (playerOptions.target != BuildTarget.Android)
                return playerOptions;

            NarrowToArm64Only();

            return playerOptions;
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            RestoreArchitectures();
        }

        // ---------------- ARM64-only test player ----------------
        //
        // Building for both ARMv7 and ARM64 doubles native compile time and the APK size pushed
        // over adb, for zero coverage benefit — the rig's phones (and virtually every real device
        // from the last ~8 years) are ARM64 only.

        private static AndroidArchitecture? _previousArchitectures;

        private static void NarrowToArm64Only()
        {
            _previousArchitectures = PlayerSettings.Android.targetArchitectures;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            Debug.Log($"[ZeyWinAds QA] Test player: narrowed targetArchitectures to ARM64 only " +
                $"(was {_previousArchitectures}) — restored after this build.");
        }

        private static void RestoreArchitectures()
        {
            if (_previousArchitectures == null)
                return;

            PlayerSettings.Android.targetArchitectures = _previousArchitectures.Value;
            Debug.Log($"[ZeyWinAds QA] Restored targetArchitectures to {_previousArchitectures.Value} after the test player build.");
            _previousArchitectures = null;
        }
    }
}
