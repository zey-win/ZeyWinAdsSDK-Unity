using System;
using NUnit.Framework;
using UnityEngine;

namespace ZeyWinAds.Tests.Runtime
{
    // A PlayMode test player that was NOT produced from a real configured build carries Unity Test
    // Framework's placeholder application id — "com.UnityTestRunner.UnityTestRunner". In that player
    // none of the project/SDK configuration is applied (bundle id, ZeyWinAdsSettings.asset, AdMob
    // ids, AndroidManifest permissions/queries/deep-links, StartupProvider authority, ...), so it is
    // a bare shell, not the real app. Every downstream QA assertion graded against it is meaningless
    // — a green run there proves nothing.
    //
    // AssertRealConfiguredBuild() is called from:
    //   * QaFixture's per-test [SetUp]  -> a bad build fails EVERY test at setup, before its body
    //     runs, so the rest of the suite is never actually exercised against the fake app;
    //   * WebViewFixture's [OneTimeSetUp] -> so those fixtures report THIS reason, not their own
    //     "offer surface never opened" one;
    //   * DeviceIdentity.RunsOnRealBundleId() -> a single headline row for the real cause.
    internal static class QaBuildGuard
    {
        internal const string TestRunnerPlaceholderId = "com.UnityTestRunner.UnityTestRunner";

        internal static void AssertRealConfiguredBuild()
        {
            string id = Application.identifier;

            bool isPlaceholder =
                string.IsNullOrEmpty(id) ||
                string.Equals(id, TestRunnerPlaceholderId, StringComparison.OrdinalIgnoreCase) ||
                id.IndexOf("UnityTestRunner", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isPlaceholder)
                return;

            Assert.Fail(
                $"Application.identifier is '{(string.IsNullOrEmpty(id) ? "<empty>" : id)}' — Unity Test " +
                "Framework's placeholder test-player id, not a real configured build. The project/SDK " +
                "configuration (bundle id, ZeyWinAdsSettings, AdMob ids, AndroidManifest entries, deep " +
                "links, StartupProvider authority) is NOT applied in this player, so it is a bare shell " +
                "and every QA result here is inaccurate. Produce a proper player build with the real " +
                "bundle id and run the suite against that. Aborting: the rest of the suite is failed at " +
                "setup so nothing runs against the wrong app.");
        }
    }
}
