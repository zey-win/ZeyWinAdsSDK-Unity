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
    // The permission check only verifies that the SDK *requested* POST_NOTIFICATIONS during
    // startup (or that it's already granted / not applicable) — NOT that a human tapped Allow.
    // Whether the user grants it is the user's choice, not the SDK's job; forcing a manual tap
    // is what made this test un-runnable unattended. "Requested" is read from the durable
    // PlayerPrefs marker AndroidRuntimePermissions sets right before it fires the request.
    //
    // Runs after AdPreload (Order(1)) — see that file's comment for why fixtures in this suite
    // chain their Order values instead of running unordered.
    //
    // `global::ZeyWinAds.ZeyWinAds.*` (not just `ZeyWinAds.*`) is required for the same reason
    // documented in AdPreload.cs: this file's own namespace (ZeyWinAds.Tests.Runtime) is nested
    // under the ZeyWinAds namespace, making a bare `ZeyWinAds` reference ambiguous.
    [TestFixture]
    public class PushNotifications : QaFixture
    {
        private const float PermissionBudgetSeconds = 20f;
        private const float TokenBudgetSeconds = 20f;
        private static readonly WaitForSecondsRealtime PollInterval = new WaitForSecondsRealtime(0.5f);

        [UnityTest]
        [Order(2)]
        public IEnumerator PermissionRequestedAtStartup()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const string postNotifications = "android.permission.POST_NOTIFICATIONS";
            // AndroidRuntimePermissions.RequestNotificationPermissionAfterDelay() sets this the
            // moment before it calls Permission.RequestUserPermission — a durable "the SDK asked"
            // marker that survives the "prompt once" short-circuit on later launches.
            const string promptedKey = "ZeyWinAds_PostNotificationsPrompted_v1";
            var budget = new QaBudget(PermissionBudgetSeconds);

            while (true)
            {
                bool requested = PlayerPrefs.GetInt(promptedKey, 0) == 1;
                // On Android < 13 POST_NOTIFICATIONS isn't a runtime permission and this returns
                // true (auto-granted) — nothing for the SDK to request, so that's a pass too.
                bool grantedOrNotApplicable =
                    UnityEngine.Android.Permission.HasUserAuthorizedPermission(postNotifications);

                if (requested || grantedOrNotApplicable)
                {
                    Debug.Log($"[ZeyWinAds QA] POST_NOTIFICATIONS: SDK requested={requested}, granted/n-a={grantedOrNotApplicable}.");
                    yield break;
                }

                if (budget.Expired)
                {
                    Assert.Fail($"The SDK's startup path did not request POST_NOTIFICATIONS within {budget.Describe()} " +
                        $"(PlayerPrefs '{promptedKey}' never set and the permission isn't already granted). " +
                        "Check AndroidRuntimePermissions.ScheduleNotificationPermissionPrompt() and the " +
                        "zeywin_push_permission_enabled remote-config flag.");
                }
                yield return PollInterval;
            }
#else
            Debug.Log("[ZeyWinAds QA] PermissionRequestedAtStartup: skipped (not an Android device).");
            yield break;
#endif
        }

        [UnityTest]
        [Order(2)]
        public IEnumerator FcmTokenReceivedWithinBudget()
        {
#if UNITY_ANDROID || UNITY_IOS
            var budget = new QaBudget(TokenBudgetSeconds);

            // Prefer whatever's already cached (TokenReceived/startup fetch already ran under the
            // hood — see FirebaseMessagingService.Initialize()); only fall back to an explicit
            // fetch if nothing has arrived by the time the budget runs out on its own.
            string token = global::ZeyWinAds.ZeyWinAds.LastPushToken;

            while (string.IsNullOrEmpty(token))
            {
                if (budget.Expired)
                {
                    Assert.Fail($"No FCM token available within {budget.Describe()} " +
                        "(LastPushToken stayed null/empty).");
                }
                yield return PollInterval;
                token = global::ZeyWinAds.ZeyWinAds.LastPushToken;
            }

            Debug.Log($"[ZeyWinAds QA] FCM token received (length={token.Length}).");
            Assert.IsFalse(string.IsNullOrEmpty(token), "FCM token was null/empty.");
#else
            Debug.Log("[ZeyWinAds QA] FcmTokenReceivedWithinBudget: skipped (not Android/iOS).");
            yield break;
#endif
        }
    }
}
