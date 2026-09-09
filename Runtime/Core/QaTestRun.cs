namespace ZeyWinAds.Core
{
    // "A Unity Test Framework run is in progress" — the explicit, Unity-intended signal.
    //
    // Set to true by the QA PlayMode suite's ITestRunCallback (QaLogGuard, in the
    // "QA Runtime Tests" assembly): from RunStarted, and — on a device test player — from an
    // earlier RuntimeInitializeOnLoadMethod so the window between player boot and RunStarted is
    // also covered. Cleared from RunFinished.
    //
    // AdAudioController reads this to suppress the Offer WebView game-pause (Time.timeScale = 0)
    // while tests run: that pause freezes RemoteTestResultSender's send coroutine (it yields on
    // scaled time via WaitForSeconds), so the Editor Test Runner never receives results even
    // though the run itself completes.
    //
    // Replaces the old reflection heuristic (Type.GetType on a UTF attribute string), which
    // IL2CPP managed-code stripping could silently drop on device. The "QA Runtime Tests"
    // assembly is compiled only under UNITY_INCLUDE_TESTS, so nothing in a shipped game ever
    // sets this — it stays false in production and the pause behaves exactly as before.
    //
    // Written and read on the main thread only (the test callback and AdAudioController both run
    // there), so no synchronisation is needed.
    internal static class QaTestRun
    {
        internal static bool InProgress;
    }
}
