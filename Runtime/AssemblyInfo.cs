using System.Runtime.CompilerServices;

// The QA PlayMode suite ("QA Runtime Tests") sets ZeyWinAds.Core.QaTestRun.InProgress from its
// ITestRunCallback (QaLogGuard) so AdAudioController can suppress the Offer WebView game-pause
// (Time.timeScale = 0) for the duration of a test run — otherwise the on-device result-sender
// coroutine, which yields on scaled time, freezes and the Editor Test Runner receives nothing.
[assembly: InternalsVisibleTo("QA Runtime Tests")]
