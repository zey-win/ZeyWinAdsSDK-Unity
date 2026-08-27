using System;
using System.Collections;
using UnityEngine;
using ZeyWinAds.Core;

namespace ZeyWinAds.QATests
{
    // Logs a PASS/FAIL line via Debug.Log (visible in `adb logcat`, tag "Unity") for each ad
    // type reporting whether it preloaded within budget. Uses the same public ready-check API
    // (ZeyWinAds.IsInterstitialReady(), etc.) game code already calls before showing an ad — so
    // "PASS" here means "the game could actually show this ad right now." Lets this QA check be
    // confirmed on a real device build without Unity's Test Runner or any CI/emulator
    // infrastructure — just:
    //   adb logcat -s Unity | grep "ZeyWinAds QA"
    internal static class AdPreloadDiagnostic
    {
        private const float BudgetSeconds = 30f;
        private const float PollIntervalSeconds = 0.5f;
        private const string LogTag = "[ZeyWinAds QA]";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            Debug.Log($"{LogTag} watching ad preload status: Interstitial, Rewarded, Native, Popup (budget {BudgetSeconds:F0}s)");
#if UNITY_ANDROID && !UNITY_EDITOR
            UnityMainThreadDispatcher.Instance.StartCoroutine(PollLoop());
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static IEnumerator PollLoop()
        {
            var checks = new (string Name, Func<bool> IsReady)[]
            {
                ("Interstitial", global::ZeyWinAds.ZeyWinAds.IsInterstitialReady),
                ("Rewarded", global::ZeyWinAds.ZeyWinAds.IsRewardedReady),
                ("Native", global::ZeyWinAds.ZeyWinAds.IsNativeReady),
                ("Popup", global::ZeyWinAds.ZeyWinAds.IsPopupReady),
            };

            var reported = new bool[checks.Length];
            var startedAt = Time.realtimeSinceStartup;

            while (Time.realtimeSinceStartup - startedAt < BudgetSeconds)
            {
                for (var i = 0; i < checks.Length; i++)
                {
                    if (reported[i])
                    {
                        continue;
                    }

                    if (checks[i].IsReady())
                    {
                        var elapsed = Time.realtimeSinceStartup - startedAt;
                        Debug.Log($"{LogTag} PASS: {checks[i].Name} ad preloaded ({elapsed:F2}s)");
                        reported[i] = true;
                    }
                }

                if (Array.TrueForAll(reported, r => r))
                {
                    break;
                }

                yield return new WaitForSecondsRealtime(PollIntervalSeconds);
            }

            var passedCount = 0;
            for (var i = 0; i < checks.Length; i++)
            {
                if (reported[i])
                {
                    passedCount++;
                    continue;
                }

                Debug.LogWarning($"{LogTag} FAIL: {checks[i].Name} ad not preloaded within {BudgetSeconds:F0}s");
            }

            var summaryLevel = passedCount == checks.Length ? LogType.Log : LogType.Warning;
            Debug.LogFormat(summaryLevel, LogOption.NoStacktrace, null,
                "{0} Ad preload summary: {1}/{2} passed", LogTag, passedCount, checks.Length);
        }
#endif
    }
}
