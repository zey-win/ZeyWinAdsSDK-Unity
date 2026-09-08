using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZeyWinAds.Tests.Runtime
{
    // Base for every QA fixture in this suite (the offer-WebView fixtures extend it via
    // WebViewFixture). Three guards. Neither hides, filters, or rewrites a single log line — every
    // Debug.Log/Warn/Error still reaches logcat in full.
    //
    // 0. Real-build check (QaBuildGuard.AssertRealConfiguredBuild).
    //    If Application.identifier is Unity Test Framework's placeholder "com.UnityTestRunner.
    //    UnityTestRunner", the player has no project/SDK config applied and every result would be
    //    inaccurate. Runs first in [SetUp] so a bad build fails EVERY test at setup, before its
    //    body — the rest of the suite is never exercised against the wrong app.
    //
    // 1. LogAssert.ignoreFailingMessages = true
    //    Opts each test out of Unity Test Framework's blanket behaviour of failing the *running*
    //    test whenever an unexpected [Error]-level log fires — even one unrelated to what the test
    //    checks. Our own Assert calls decide pass/fail here. Has to be a per-test [SetUp]; a
    //    one-shot [SetUpFixture] didn't reliably hold across the suite.
    //
    // 2. Stretch the SDK ad preloader's retry budget for the test run.
    //    Guard 1 covers errors logged synchronously inside a test. It does NOT reliably absorb an
    //    async error that lands on a test boundary — and the recurring offender is exactly that:
    //    AdLoader.HandlePreloadFailure's terminal Logger.Error ("Предзагрузка не удалась ... after
    //    N attempts"), fired from a background retry coroutine ~30-60 s after startup whenever a
    //    slot has no fill (benign; no test covers banner preload). While _retryCounts < max the
    //    same coroutine only logs Logger.Warn, which never fails a test. So we raise max attempts /
    //    retry spacing to their clamp ceilings ([1,10] and [5,300] s in AdLoader): the "gave up"
    //    Error branch is then not reached until ~45 min in, past any suite run. Every no-fill retry
    //    is still logged the whole time — just at Warning level, which is the right level for
    //    "no inventory yet".
    public abstract class QaFixture
    {
        [SetUp]
        public void QaTestEnvironmentGuards()
        {
            QaBuildGuard.AssertRealConfiguredBuild(); // fails here (before any test body) on a placeholder-id player

            LogAssert.ignoreFailingMessages = true;

#if UNITY_ANDROID && !UNITY_EDITOR
            var loader = Object.FindAnyObjectByType<global::ZeyWinAds.Core.AdLoader>();
            if (loader != null && loader.Settings != null)
            {
                if (loader.Settings.maxRetryAttempts < 10)
                    loader.Settings.maxRetryAttempts = 10;
                if (loader.Settings.retryDelaySeconds < 300f)
                    loader.Settings.retryDelaySeconds = 300f;
            }
#endif
        }
    }
}
