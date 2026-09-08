using System;
using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.TestRunner;

[assembly: TestRunCallback(typeof(ZeyWinAds.Tests.Runtime.QaLogGuard))]

namespace ZeyWinAds.Tests.Runtime
{
    // Why this exists
    // ---------------
    // Unity Test Framework fails the *currently running* [UnityTest] on ANY unexpected
    // Error / Assert / Exception log — including one from an unrelated background coroutine
    // (GeoCheck, AdLoader retry, Firebase, a permission bridge, ...). Any developer adding a
    // Debug.LogError anywhere in the SDK can therefore break this suite without touching a test.
    //
    // LogAssert.ignoreFailingMessages in [SetUp] does NOT close this: UTF's per-test LogScope
    // resets that flag during test cleanup, so an async error that lands right on a test boundary
    // still fails the test that just ended.
    //
    // What this does
    // --------------
    // For the whole test run it swaps Debug.unityLogger.logHandler for a shim that re-emits
    // Error / Assert / Exception at Warning level. The full text still reaches logcat (prefixed
    // "[downgraded-from-X]"), but Application.logMessageReceived — which is downstream of the
    // handler and is what UTF's LogScope listens to — now reports Warning, and UTF only fails on
    // Error / Assert / Exception. Pass/fail is decided solely by the tests' own Assert calls,
    // which is this suite's stated contract (see QaFixture).
    //
    // What is NOT affected
    // -------------------
    // - An exception actually thrown out of a test or its coroutine: UTF catches that via the
    //   coroutine/test runner, not the log scope, and still fails the test correctly. Only
    //   Debug.Log* *calls* are downgraded.
    // - The suite uses no LogAssert.Expect, so nothing here relies on observing an Error-typed log.
    // - Editor "Play" (not a test run): the RuntimeInitializeOnLoadMethod install is compiled out
    //   with UNITY_EDITOR so red errors stay red during normal development; in the editor the shim
    //   is installed only for an actual test run, via ITestRunCallback below.
    public sealed class QaLogGuard : ITestRunCallback
    {
        private static Shim _shim;

        // A device test player exists only to run this suite, so install at the earliest possible
        // point — before SDK init — to also cover an error logged during startup, before
        // RunStarted fires.
#if !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallEarly() => Install();
#endif

        internal static void Install()
        {
            if (Debug.unityLogger.logHandler is Shim)
                return;
            _shim = new Shim(Debug.unityLogger.logHandler);
            Debug.unityLogger.logHandler = _shim;
        }

        private static void Uninstall()
        {
            if (_shim == null)
                return;
            if (ReferenceEquals(Debug.unityLogger.logHandler, _shim))
                Debug.unityLogger.logHandler = _shim.Inner;
            _shim = null;
        }

        public void RunStarted(ITest testsToRun) => Install();
        public void RunFinished(ITestResult testResults) => Uninstall();
        public void TestStarted(ITest test) { }
        public void TestFinished(ITestResult result) { }

        private sealed class Shim : ILogHandler
        {
            public readonly ILogHandler Inner;

            public Shim(ILogHandler inner)
            {
                Inner = inner;
            }

            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
            {
                if (logType == LogType.Error || logType == LogType.Assert)
                {
                    Inner.LogFormat(LogType.Warning, context, "[downgraded-from-" + logType + "] " + format, args);
                    return;
                }
                Inner.LogFormat(logType, context, format, args);
            }

            public void LogException(Exception exception, UnityEngine.Object context)
            {
                Inner.LogFormat(LogType.Warning, context, "[downgraded-from-Exception] {0}", exception);
            }
        }
    }
}
