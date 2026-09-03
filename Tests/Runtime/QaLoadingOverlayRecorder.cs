using System.Collections;
using UnityEngine;
using ZeyWinAds.UI;

namespace ZeyWinAds.Tests.Runtime
{
    // The native loading overlay's whole show->hide cycle can finish during startup — before the
    // OfferAndLoadingScreen.OverlayAppearsAndDismissesWithinBudget [UnityTest] gets its first frame, since
    // Unity Test Framework boots after full engine init + test-scene load. This recorder installs
    // at RuntimeInitializeOnLoadMethod(BeforeSceneLoad) — the same early point the SDK's own
    // LoadingOverlayDiagnostic uses — and captures the lifecycle so the test can assert on what
    // was recorded instead of trying to catch a transient live state.
    [DefaultExecutionOrder(-9000)] // after QaForegroundTimeTracker (-10000)
    public class QaLoadingOverlayRecorder : MonoBehaviour
    {
        private static QaLoadingOverlayRecorder _instance;
        private float _shownAtForeground;

        /// <summary>Foreground time (QaForegroundTimeTracker) when this recorder began watching.</summary>
        public static float StartedForegroundSeconds { get; private set; }

        /// <summary>True once the native overlay has been observed visible at least once.</summary>
        public static bool EverShown { get; private set; }

        /// <summary>True while the native overlay is currently visible.</summary>
        public static bool StillVisible { get; private set; }

        /// <summary>
        /// While StillVisible: how long it has been visible so far. After it has hidden again:
        /// the total time it was visible. Zero before it is first shown.
        /// </summary>
        public static float VisibleForSeconds { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
                return;

            StartedForegroundSeconds = QaForegroundTimeTracker.ForegroundSeconds;

            var go = new GameObject("QaLoadingOverlayRecorder");
            Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<QaLoadingOverlayRecorder>();
        }

        private void Awake()
        {
            // One synchronous sample in case the overlay is already up this instant, then poll.
            Sample(LoadingOverlay.IsNativeOverlayVisible());
            StartCoroutine(PollLoop());
        }

        private IEnumerator PollLoop()
        {
            var wait = new WaitForSecondsRealtime(0.1f);
            while (true)
            {
                Sample(LoadingOverlay.IsNativeOverlayVisible());
                yield return wait;
            }
        }

        private void Sample(bool isVisible)
        {
            if (isVisible)
            {
                if (!EverShown)
                {
                    EverShown = true;
                    _shownAtForeground = QaForegroundTimeTracker.ForegroundSeconds;
                }

                StillVisible = true;
                VisibleForSeconds = QaForegroundTimeTracker.ForegroundSeconds - _shownAtForeground;
            }
            else if (StillVisible)
            {
                StillVisible = false;
                VisibleForSeconds = QaForegroundTimeTracker.ForegroundSeconds - _shownAtForeground;
            }
        }
    }
}
