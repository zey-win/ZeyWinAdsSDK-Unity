using System.Collections;
using UnityEngine;
using ZeyWinAds.Core;
using ZeyWinAds.UI;

namespace ZeyWinAds.QATests
{
    // Logs a PASS/FAIL line via Debug.Log (visible in `adb logcat`, tag "Unity") reporting how
    // long the native loading overlay was actually visible on screen. Polls
    // LoadingOverlay.IsNativeOverlayVisible() directly rather than relying on C# Show()/Hide()
    // call sites, since dismissal can happen through paths that never call back into C# at all
    // (e.g. the SDK's native auto-dismiss failsafe). Lets this QA check be confirmed on a real
    // device build without Unity's Test Runner or any CI/emulator infrastructure — just:
    //   adb logcat -s Unity | grep "ZeyWinAds QA"
    internal static class LoadingOverlayDiagnostic
    {
        private const float BudgetSeconds = 15f;
        private const float StartupTimeoutSeconds = 10f;
        private const float PollIntervalSeconds = 0.1f;
        private const string LogTag = "[ZeyWinAds QA]";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            Debug.Log($"{LogTag} watching native LoadingOverlay visibility (budget {BudgetSeconds:F0}s)");
#if UNITY_ANDROID && !UNITY_EDITOR
            UnityMainThreadDispatcher.Instance.StartCoroutine(PollLoop());
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static IEnumerator PollLoop()
        {
            var wasVisible = false;
            var everShown = false;
            var reportedNeverAppeared = false;
            var shownAt = 0f;
            var loopStartedAt = Time.realtimeSinceStartup;

            while (true)
            {
                var isVisible = LoadingOverlay.IsNativeOverlayVisible();

                if (isVisible && !wasVisible)
                {
                    everShown = true;
                    shownAt = Time.realtimeSinceStartup;
                    Debug.Log($"{LogTag} LoadingOverlay shown");
                }
                else if (!isVisible && wasVisible)
                {
                    var elapsed = Time.realtimeSinceStartup - shownAt;
                    if (elapsed <= BudgetSeconds)
                    {
                        Debug.Log($"{LogTag} PASS: LoadingOverlay shown for {elapsed:F2}s (budget {BudgetSeconds:F0}s)");
                    }
                    else
                    {
                        Debug.LogWarning($"{LogTag} FAIL: LoadingOverlay shown for {elapsed:F2}s, exceeds {BudgetSeconds:F0}s budget");
                    }
                }

                if (!everShown && !reportedNeverAppeared &&
                    Time.realtimeSinceStartup - loopStartedAt >= StartupTimeoutSeconds)
                {
                    Debug.LogWarning($"{LogTag} FAIL: LoadingOverlay never appeared within {StartupTimeoutSeconds:F0}s of app start");
                    reportedNeverAppeared = true;
                }

                wasVisible = isVisible;
                yield return new WaitForSecondsRealtime(PollIntervalSeconds);
            }
        }
#endif
    }
}
