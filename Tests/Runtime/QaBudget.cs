using UnityEngine;

namespace ZeyWinAds.Tests.Runtime
{
    // A poll-loop deadline that can't be frozen by the app losing focus.
    //
    // Most on-device tests time their wait off QaForegroundTimeTracker.ForegroundSeconds so a real
    // backgrounding event (the offer WebView sending the user to Play Store / Telegram) doesn't burn
    // the budget while the app can't run at all. But that clock also stops for reasons that are NOT
    // "the test is legitimately paused" — a system permission dialog taking focus, the screen
    // locking, an ANR. When it stops, `foreground - start >= budget` is never true and the loop
    // spins until the assembly-wide [Timeout] (90 s) eventually kills it with a useless message.
    //
    // QaBudget keeps the foreground-aware budget AND adds a wall-clock ceiling (default 2x the
    // budget). Whichever trips first ends the wait, and Describe() says which — so a focus-loss
    // stall fails in ~2x the intended time with a clear diagnosis instead of an opaque timeout.
    internal readonly struct QaBudget
    {
        private readonly float _foregroundStart;
        private readonly float _realtimeStart;
        private readonly float _foregroundBudget;
        private readonly float _realtimeCeiling;

        public QaBudget(float foregroundBudgetSeconds, float realtimeCeilingMultiplier = 2f)
        {
            _foregroundStart = QaForegroundTimeTracker.ForegroundSeconds;
            _realtimeStart = Time.realtimeSinceStartup;
            _foregroundBudget = foregroundBudgetSeconds;
            _realtimeCeiling = foregroundBudgetSeconds * realtimeCeilingMultiplier;
        }

        public float ForegroundElapsed => QaForegroundTimeTracker.ForegroundSeconds - _foregroundStart;
        public float RealtimeElapsed => Time.realtimeSinceStartup - _realtimeStart;

        public bool Expired => ForegroundElapsed >= _foregroundBudget || RealtimeElapsed >= _realtimeCeiling;

        // The realtime ceiling tripped while the foreground budget still had room left — i.e. the
        // app was out of focus for the difference and the wait never really got to run.
        public bool StalledByFocusLoss =>
            RealtimeElapsed >= _realtimeCeiling && ForegroundElapsed < _foregroundBudget;

        public string Describe() =>
            StalledByFocusLoss
                ? $"real time {RealtimeElapsed:F0}s hit the {_realtimeCeiling:F0}s ceiling with only " +
                  $"{ForegroundElapsed:F0}s / {_foregroundBudget:F0}s foreground — the app lost focus " +
                  "(system dialog / screen lock / ANR?) and the wait never ran"
                : $"{ForegroundElapsed:F0}s foreground (budget {_foregroundBudget:F0}s)";
    }
}
