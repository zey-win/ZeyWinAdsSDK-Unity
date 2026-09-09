using NUnit.Framework;

// Hard wall-clock backstop for every PlayMode test in this assembly. NUnit's [Timeout] is enforced
// on real elapsed time — it does not depend on QaForegroundTimeTracker, the coroutine state, or a
// test's own internal budget. If a poll loop stalls (e.g. the foreground clock freezes because a
// system permission dialog took focus, or a subsystem that's disabled server-side never reaches the
// state the loop waits on), the test FAILS with "Timeout exceeded" and the run continues instead of
// hanging every test after it.
//
// A test may set its own [Timeout] — a method-level value always wins over this assembly default.
// This is the "genuinely hung" backstop, not the normal failure path: per-test wait budgets
// (offer/ads/push ~20 s, driven by QaBudget) fail long before this. It only needs to clear the
// slowest legit run — hence 90 s here, with PassesChecklist carrying its own [Timeout(300 * 1000)].
[assembly: Timeout(90 * 1000)] // 90s
