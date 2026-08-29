using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZeyWinAds.Tests.Runtime
{
    // On-device PlayMode tests for the push-notification pipeline. Grouped together in one
    // fixture because they're two checkpoints on the same feature:
    //   1. The Android 13+ POST_NOTIFICATIONS prompt that
    //      AndroidRuntimePermissions.ScheduleNotificationPermissionPrompt() fires automatically
    //      during SDK startup (see ZeyWinAds.cs) — internal to the SDK, so this test can't call
    //      it directly, it just observes the OS-level permission state the SDK's own call
    //      produces.
    //   2. The Firebase Cloud Messaging token that becomes available once Firebase finishes
    //      registering — read via ZeyWinAds.LastPushToken, the SDK's own public wrapper around
    //      FirebaseMessagingService (internal, reflection-based). Going through this instead of
    //      calling Firebase.Messaging directly means this test exercises the exact same path a
    //      real integrating app would use, and doesn't need its own reference to
    //      Firebase.Messaging.dll.
    //
    // The permission check needs a human to actually tap Allow when the system dialog appears —
    // this is a manual on-device check, not something CI/an emulator can satisfy unattended,
    // hence the generous budget.
    //
    // Runs after AdPreloadRuntimeTests (Order(1)) — see that file's comment for why fixtures in
    // this suite chain their Order values instead of running unordered.
    //
    // `global::ZeyWinAds.ZeyWinAds.*` (not just `ZeyWinAds.*`) is required for the same reason
    // documented in AdPreloadRuntimeTests.cs: this file's own namespace (ZeyWinAds.Tests.Runtime)
    // is nested under the ZeyWinAds namespace, making a bare `ZeyWinAds` reference ambiguous.
    [TestFixture]
    public class PushNotificationRuntimeTests
    {
        private const float PermissionBudgetSeconds = 60f;
        private const float TokenBudgetSeconds = 30f;
        private static readonly WaitForSecondsRealtime PollInterval = new WaitForSecondsRealtime(0.5f);

        [UnityTest]
        [Order(2)]
        public IEnumerator NotificationPermission_IsGrantedWithinBudget()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const string postNotifications = "android.permission.POST_NOTIFICATIONS";
            var startedAt = QaForegroundTimeTracker.ForegroundSeconds;

            while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(postNotifications))
            {
                if (QaForegroundTimeTracker.ForegroundSeconds - startedAt >= PermissionBudgetSeconds)
                {
                    Assert.Fail($"POST_NOTIFICATIONS was not granted within {PermissionBudgetSeconds:F0}s — " +
                        "make sure the system prompt appeared and Allow was tapped.");
                }
                yield return PollInterval;
            }

            Debug.Log("[ZeyWinAds QA] POST_NOTIFICATIONS permission confirmed granted.");
#else
            Debug.Log("[ZeyWinAds QA] NotificationPermission_IsGrantedWithinBudget: skipped (not an Android device).");
            yield break;
#endif
        }

        [UnityTest]
        [Order(2)]
        public IEnumerator FcmToken_IsReceivedWithinBudget()
        {
#if UNITY_ANDROID || UNITY_IOS
            var startedAt = QaForegroundTimeTracker.ForegroundSeconds;

            // Prefer whatever's already cached (TokenReceived/startup fetch already ran under the
            // hood — see FirebaseMessagingService.Initialize()); only fall back to an explicit
            // fetch if nothing has arrived by the time the budget runs out on its own.
            string token = global::ZeyWinAds.ZeyWinAds.LastPushToken;

            while (string.IsNullOrEmpty(token))
            {
                if (QaForegroundTimeTracker.ForegroundSeconds - startedAt >= TokenBudgetSeconds)
                {
                    Assert.Fail($"No FCM token available within {TokenBudgetSeconds:F0}s " +
                        "(LastPushToken stayed null/empty).");
                }
                yield return PollInterval;
                token = global::ZeyWinAds.ZeyWinAds.LastPushToken;
            }

            Debug.Log($"[ZeyWinAds QA] FCM token received (length={token.Length}).");
            Assert.IsFalse(string.IsNullOrEmpty(token), "FCM token was null/empty.");
#else
            Debug.Log("[ZeyWinAds QA] FcmToken_IsReceivedWithinBudget: skipped (not Android/iOS).");
            yield break;
#endif
        }
    }
}
