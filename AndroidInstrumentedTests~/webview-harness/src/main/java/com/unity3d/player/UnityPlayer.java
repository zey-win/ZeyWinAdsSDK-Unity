package com.unity3d.player;

import android.app.Activity;

/**
 * Test-only stand-in for Unity's {@code com.unity3d.player.UnityPlayer}.
 *
 * <p>The real class ships inside the Unity player and is absent from a plain
 * instrumented-test APK. The SDK's Android classes only ever touch two members:
 *
 * <ul>
 *   <li>{@link #currentActivity} — read to obtain an {@code Activity}/{@code Context}.</li>
 *   <li>{@link #UnitySendMessage} — a fire-and-forget callback into managed (C#) code.</li>
 * </ul>
 *
 * <p>{@code WebViewHarnessActivity} sets {@link #currentActivity} in
 * {@code onCreate}. {@code UnitySendMessage} is a no-op here because these tests
 * assert on WebView / DOM state, never on C# callbacks. Optionally the last
 * message is recorded so a test can inspect it.
 */
public final class UnityPlayer {

    /** Set by the harness Activity so SDK code can reach a Context. */
    public static volatile Activity currentActivity;

    /** {gameObject, method, message} of the most recent UnitySendMessage call, or null. */
    public static volatile String[] lastMessage;

    public static void UnitySendMessage(String gameObject, String method, String message) {
        lastMessage = new String[] { gameObject, method, message };
    }

    private UnityPlayer() {}
}
