using UnityEngine;

namespace ZeyWinAds.Tests.Runtime
{
    // Tracks only the time this app has actually been in the foreground, so a [UnityTest]'s
    // budget isn't unfairly burned by time spent in another Activity.
    //
    // Why this exists: the real referral offer WebView (ReferralManager/WebViewLock) can
    // legitimately hand off to another app entirely — ZeyWinAdsWebViewNavigation.openExternal
    // launches market://, tg://, whatsapp:// etc. via startActivity(FLAG_ACTIVITY_NEW_TASK)
    // whenever the offer page asks for it. That's real, correct behavior for real users. Once
    // that happens this Activity is genuinely backgrounded, and Android/Unity does not call
    // Update() at all while backgrounded — so any coroutine polling Time.realtimeSinceStartup
    // would count that dead time as if the SDK were slow, even though nothing was actually
    // running. This tracker exposes a clock that only ever advances while the app can actually
    // execute, so the offer is free to send the user anywhere and the ad-preload / loader budgets
    // still measure only real elapsed app time.
    [DefaultExecutionOrder(-10000)]
    public class QaForegroundTimeTracker : MonoBehaviour
    {
        private static QaForegroundTimeTracker _instance;
        private static float _accumulated;
        private static float _lastResumedAt;
        private static bool _isForeground = true;

        /// <summary>Total seconds this app has spent in the foreground since Bootstrap ran.</summary>
        public static float ForegroundSeconds =>
            _accumulated + (_isForeground ? Time.realtimeSinceStartup - _lastResumedAt : 0f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
                return;

            var go = new GameObject("QaForegroundTimeTracker");
            Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<QaForegroundTimeTracker>();
            _accumulated = 0f;
            _lastResumedAt = Time.realtimeSinceStartup;
            _isForeground = true;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                if (!_isForeground)
                    return;

                _accumulated += Time.realtimeSinceStartup - _lastResumedAt;
                _isForeground = false;
                Debug.LogWarning("[ZeyWinAds QA] App backgrounded (likely a real offer sending " +
                    $"the user externally, e.g. to the Play Store) — freezing test budgets at " +
                    $"{_accumulated:F2}s of real foreground time so far. This is expected when " +
                    "the referral offer is genuinely shown; checks resume once the app returns.");
            }
            else
            {
                if (_isForeground)
                    return;

                _lastResumedAt = Time.realtimeSinceStartup;
                _isForeground = true;
                Debug.Log("[ZeyWinAds QA] App back in foreground — test budgets resuming from " +
                    $"{_accumulated:F2}s.");
            }
        }
    }
}
