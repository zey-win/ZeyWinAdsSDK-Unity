package com.zeywinads.instrumentedtests;

import android.util.Log;

import androidx.test.ext.junit.rules.ActivityScenarioRule;
import androidx.test.ext.junit.runners.AndroidJUnit4;

import org.junit.Rule;
import org.junit.Test;
import org.junit.runner.RunWith;

/**
 * Not a pass/fail test — a one-shot probe you run first to confirm the checklist
 * page is reachable from the device and to print the exact check ids + buckets
 * the page exposes. Read them from logcat:
 *
 * <pre>adb logcat -s ZWChecklistDiag</pre>
 *
 * Use the printed ids verbatim in {@link FileInputWebViewTest} etc.
 */
@RunWith(AndroidJUnit4.class)
public class ChecklistMetaDiagnosticTest {

    private static final String TAG = "ZWChecklistDiag";
    private static final String CHECKLIST_URL =
            "https://ads.zeywin.com/checklist/webview-test?runner=1&autorun=0";

    @Rule
    public ActivityScenarioRule<WebViewHarnessActivity> activityRule =
            new ActivityScenarioRule<>(WebViewHarnessActivity.class);

    @Test
    public void printChecklistMetaAndResults() {
        WebViewHarnessActivity[] holder = new WebViewHarnessActivity[1];
        activityRule.getScenario().onActivity(a -> holder[0] = a);
        WebViewChecklist checklist = new WebViewChecklist(holder[0].webView);

        checklist.load(CHECKLIST_URL);
        checklist.waitForJsTrue(
                "window.ZW_CHECKLIST && typeof window.ZW_CHECKLIST.run === 'function'", 60_000);

        Log.i(TAG, "ZW_CHECKLIST.version = "
                + checklist.evalJs("String(window.ZW_CHECKLIST.version)"));
        Log.i(TAG, "ZW_CHECKLIST.pageRevision = "
                + checklist.evalJs("String(window.ZW_CHECKLIST.pageRevision)"));
        logChunked("meta", checklist.dumpMeta());
        logChunked("results(initial)", checklist.dumpResults());
    }

    /** logcat truncates long lines; split into readable chunks. */
    private static void logChunked(String label, String value) {
        if (value == null) {
            Log.i(TAG, label + " = null");
            return;
        }
        int chunk = 3500;
        for (int i = 0; i < value.length(); i += chunk) {
            Log.i(TAG, label + "[" + i + "] " + value.substring(i, Math.min(value.length(), i + chunk)));
        }
    }
}
