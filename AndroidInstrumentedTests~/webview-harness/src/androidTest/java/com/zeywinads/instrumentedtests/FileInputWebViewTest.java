package com.zeywinads.instrumentedtests;

import static androidx.test.espresso.Espresso.onView;
import static androidx.test.espresso.intent.Intents.intended;
import static androidx.test.espresso.intent.Intents.intending;
import static androidx.test.espresso.intent.matcher.IntentMatchers.hasAction;
import static androidx.test.espresso.matcher.ViewMatchers.isAssignableFrom;
import static org.hamcrest.Matchers.anyOf;
import static org.hamcrest.Matchers.containsString;
import static org.junit.Assert.assertThat;

import android.app.Activity;
import android.app.Instrumentation;
import android.content.Context;
import android.content.Intent;
import android.graphics.Bitmap;
import android.net.Uri;
import android.os.SystemClock;
import android.provider.MediaStore;
import android.util.Log;
import android.view.InputDevice;
import android.view.MotionEvent;
import android.webkit.WebView;

import androidx.test.core.app.ApplicationProvider;
import androidx.test.espresso.action.GeneralClickAction;
import androidx.test.espresso.action.Press;
import androidx.test.espresso.action.Tap;
import androidx.test.espresso.intent.Intents;
import androidx.test.ext.junit.rules.ActivityScenarioRule;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;

import org.hamcrest.Matcher;
import org.junit.After;
import org.junit.Before;
import org.junit.Rule;
import org.junit.Test;
import org.junit.runner.RunWith;

import java.io.File;
import java.io.FileOutputStream;

/**
 * Proves the SDK's {@code onShowFileChooser} contract end to end against the real
 * offer-WebView wiring.
 *
 * <p>The checklist page's {@code ZW_CHECKLIST.run("file-input-*")} only does
 * {@code <input>.click()}, which modern Android WebView ignores for
 * {@code onShowFileChooser} (no user activation). So this test lands a <b>real
 * MotionEvent tap</b> on the input (after pinning it to a known rect), which does
 * grant activation, then asserts:
 *
 * <ol>
 *   <li>the tap reaches the SDK — {@code fileChooserInvocations} bumps;</li>
 *   <li>the SDK launches a content-pick {@code Intent} (Espresso-Intents
 *       {@code intended(...)});</li>
 *   <li>Espresso-Intents answers it with a staged JPEG and the page's own
 *       {@code onChange} handler then flips the row to {@code pass} with the
 *       file's {@code name · type · size} — a genuine end-to-end pass.</li>
 * </ol>
 *
 * Covers checklist rows <b>file-input-generic</b> ("Any file upload") and
 * <b>file-input-capture</b> ("Photo / camera capture (file input)").
 */
@RunWith(AndroidJUnit4.class)
public class FileInputWebViewTest {

    private static final String TAG = "ZWChecklistFile";
    private static final String CHECKLIST_URL =
            "https://ads.zeywin.com/checklist/webview-test?runner=1&autorun=0";

    private static final long CHECKLIST_READY_MS = 60_000;
    private static final long CHOOSER_INVOKE_MS = 10_000;
    private static final long PICKER_INTENT_MS = 10_000;
    private static final long PAGE_SETTLE_MS = 10_000;

    private static final Matcher<Intent> ANY_PICKER = anyOf(
            hasAction(Intent.ACTION_CHOOSER),
            hasAction(Intent.ACTION_GET_CONTENT),
            hasAction(Intent.ACTION_OPEN_DOCUMENT),
            hasAction(MediaStore.ACTION_IMAGE_CAPTURE));

    @Rule
    public ActivityScenarioRule<WebViewHarnessActivity> activityRule =
            new ActivityScenarioRule<>(WebViewHarnessActivity.class);

    private WebViewChecklist checklist;

    @Before
    public void setUp() {
        WebViewHarnessActivity.fileChooserInvocations.set(0);
        Intents.init();

        WebViewHarnessActivity[] holder = new WebViewHarnessActivity[1];
        activityRule.getScenario().onActivity(a -> holder[0] = a);
        checklist = new WebViewChecklist(holder[0].webView);

        checklist.load(CHECKLIST_URL);
        checklist.waitForJsTrue(
                "window.ZW_CHECKLIST && typeof window.ZW_CHECKLIST.run === 'function'",
                CHECKLIST_READY_MS);
    }

    @After
    public void tearDown() {
        Intents.release();
    }

    @Test
    public void anyFileUpload_deliversPickedFileToInput() throws Exception {
        assertRowDeliversFile("file-input-generic", "zw-el-file-input-generic", "Any file upload",
                stageProbeImage("probe-file-input-generic.jpg"));
    }

    @Test
    public void photoCameraCapture_deliversCapturedImage() throws Exception {
        assertRowDeliversFile("file-input-capture", "zw-el-file-input-capture", "Photo / camera capture",
                stageProbeImage("probe-file-input-capture.jpg"));
    }

    // ---------------------------------------------------------------------

    private void assertRowDeliversFile(String checkId, String elementId, String title, Uri picked) {
        Intent result = new Intent().setData(picked)
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        intending(ANY_PICKER)
                .respondWith(new Instrumentation.ActivityResult(Activity.RESULT_OK, result));

        String pin = checklist.pinElementTopLeft(elementId);
        if (!"pinned".equals(pin)) {
            throw new AssertionError(title + " (" + checkId + "): could not pin #" + elementId
                    + " (got '" + pin + "') — the page layout changed.");
        }
        InstrumentationRegistry.getInstrumentation().waitForIdleSync();

        int before = WebViewHarnessActivity.fileChooserInvocations.get();
        realTapWebViewLocal(40, 40);

        // 1) real tap must reach the SDK's chrome client.
        if (!awaitCounter(before + 1, CHOOSER_INVOKE_MS)) {
            throw new AssertionError(title + " (" + checkId + "): a real tap on the <input> did not "
                    + "reach ZeyWinAdsWebChromeClient.onShowFileChooser within " + CHOOSER_INVOKE_MS
                    + "ms. page status=" + checklist.checkStatus(checkId)
                    + " detail=" + checklist.checkDetail(checkId));
        }

        // 2) the SDK must launch a content-pick Intent.
        InstrumentationRegistry.getInstrumentation().waitForIdleSync();
        if (!awaitIntent(ANY_PICKER, PICKER_INTENT_MS)) {
            throw new AssertionError(title + " (" + checkId + "): onShowFileChooser fired but no "
                    + "picker Intent left the app within " + PICKER_INTENT_MS
                    + "ms — ZeyWinAdsFileChooserFragment did not start one.");
        }
        intended(ANY_PICKER);

        // 3) the stub's file must reach the <input>; the page's onChange flips it to pass.
        String status = awaitTerminalStatus(checkId, PAGE_SETTLE_MS);
        String detail = checklist.checkDetail(checkId);
        Log.i(TAG, title + " (" + checkId + "): status=" + status + " detail=" + detail);

        if (!"pass".equals(status)) {
            throw new AssertionError(title + " (" + checkId + "): picker launched and was answered, "
                    + "but the page did not see the file (status=" + status + ", detail=" + detail
                    + "). If detail is 'no file', the SDK did not forward the result Uri to "
                    + "filePathCallback.onReceiveValue; if the WebView rejected the file:// Uri, "
                    + "switch stageProbeImage() to a FileProvider content:// Uri.");
        }
        assertThat("page should report the delivered file's metadata",
                detail, containsString("bytes"));
    }

    /** Real down/up MotionEvent at ({@code localX},{@code localY}) inside the WebView. */
    private void realTapWebViewLocal(int localX, int localY) {
        onView(isAssignableFrom(WebView.class)).perform(new GeneralClickAction(
                Tap.SINGLE,
                view -> {
                    int[] loc = new int[2];
                    view.getLocationOnScreen(loc);
                    return new float[] { loc[0] + localX, loc[1] + localY };
                },
                Press.FINGER,
                InputDevice.SOURCE_TOUCHSCREEN,
                MotionEvent.BUTTON_PRIMARY));
    }

    private static boolean awaitCounter(int target, long timeoutMs) {
        long deadline = SystemClock.uptimeMillis() + timeoutMs;
        while (SystemClock.uptimeMillis() < deadline) {
            if (WebViewHarnessActivity.fileChooserInvocations.get() >= target) return true;
            SystemClock.sleep(100);
        }
        return WebViewHarnessActivity.fileChooserInvocations.get() >= target;
    }

    /** Retry {@code intended(...)} (throws on no match) until it passes or times out. */
    private static boolean awaitIntent(Matcher<Intent> matcher, long timeoutMs) {
        long deadline = SystemClock.uptimeMillis() + timeoutMs;
        while (SystemClock.uptimeMillis() < deadline) {
            try {
                intended(matcher);
                return true;
            } catch (AssertionError notYet) {
                SystemClock.sleep(150);
            }
        }
        return false;
    }

    private String awaitTerminalStatus(String checkId, long timeoutMs) {
        long deadline = SystemClock.uptimeMillis() + timeoutMs;
        String status = "pending";
        while (SystemClock.uptimeMillis() < deadline) {
            status = checklist.checkStatus(checkId);
            if ("pass".equals(status) || "fail".equals(status) || "skip".equals(status)) return status;
            SystemClock.sleep(200);
        }
        return status;
    }

    /** Write a small valid JPEG into the app cache; hand back a file:// Uri. */
    private Uri stageProbeImage(String name) throws Exception {
        Context ctx = ApplicationProvider.getApplicationContext();
        File file = new File(ctx.getCacheDir(), name);
        Bitmap bmp = Bitmap.createBitmap(8, 8, Bitmap.Config.ARGB_8888);
        try (FileOutputStream os = new FileOutputStream(file)) {
            bmp.compress(Bitmap.CompressFormat.JPEG, 90, os);
        } finally {
            bmp.recycle();
        }
        return Uri.fromFile(file);
    }
}
