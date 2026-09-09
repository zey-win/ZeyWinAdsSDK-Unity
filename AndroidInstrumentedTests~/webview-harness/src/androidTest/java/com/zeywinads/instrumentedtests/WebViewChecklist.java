package com.zeywinads.instrumentedtests;

import android.os.SystemClock;
import android.webkit.WebView;

import androidx.test.platform.app.InstrumentationRegistry;

import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;

/**
 * Thin driver around the checklist page's {@code window.ZW_CHECKLIST} contract.
 *
 * <p>Everything runs through {@link WebView#evaluateJavascript} on the UI thread
 * — no Espresso-Web needed for "run a check, read its result". Espresso-Web is
 * only pulled in for the rows that need a synthetic DOM gesture (window.open,
 * anchor target=_blank, …).
 */
final class WebViewChecklist {

    private final WebView webView;

    WebViewChecklist(WebView webView) {
        this.webView = webView;
    }

    WebView webView() {
        return webView;
    }

    /**
     * Force one page element to a big fixed rect anchored at the WebView's
     * top-left, so a test can land a real touch on it without caring about page
     * scale or scroll. Returns 'pinned' / 'no-el'.
     */
    String pinElementTopLeft(String elementId) {
        return unquote(evalJs(
                "(function(){var el=document.getElementById(" + jsString(elementId) + ");"
              + "if(!el)return 'no-el';"
              + "el.style.cssText+=';position:fixed!important;left:0!important;top:0!important;"
              + "width:90vw!important;height:40vh!important;z-index:2147483647!important;"
              + "opacity:1!important;visibility:visible!important;display:block!important;"
              + "transform:none!important;margin:0!important;pointer-events:auto!important';"
              + "return 'pinned';})()"));
    }

    void load(final String url) {
        runOnUi(new Runnable() {
            @Override public void run() { webView.loadUrl(url); }
        });
    }

    /** Block until a JS expression evaluates truthy, or fail after {@code timeoutMs}. */
    void waitForJsTrue(String expr, long timeoutMs) {
        long deadline = SystemClock.uptimeMillis() + timeoutMs;
        String last = "(never evaluated)";
        while (SystemClock.uptimeMillis() < deadline) {
            last = evalJs("(function(){try{return (" + expr + ") ? 'true' : 'false';}"
                    + "catch(e){return 'err:'+e;}})()");
            if ("\"true\"".equals(last)) {
                return;
            }
            SystemClock.sleep(250);
        }
        throw new AssertionError("Timed out after " + timeoutMs + "ms waiting for JS `" + expr
                + "` (last value: " + last + ")");
    }

    /** Fire a single checklist check by id; do not wait for it to settle. */
    void fireCheck(String id) {
        evalJs("window.ZW_CHECKLIST.run(" + jsString(id) + ")");
    }

    /**
     * Current status of one check. {@code results()} returns an OBJECT keyed by
     * id (not an array): {@code {"<id>":{status,detail,bucket,...}}}. Returns
     * {@code pending} / {@code running} / {@code pass} / {@code fail} /
     * {@code skip}, or {@code missing} if the page has no such row.
     */
    String checkStatus(String id) {
        return unquote(evalJs(
                "(function(){"
              + "  var r = window.ZW_CHECKLIST.results && window.ZW_CHECKLIST.results();"
              + "  var e = r && r[" + jsString(id) + "];"
              + "  return e ? String(e.status) : 'missing';"
              + "})()"));
    }

    /** Detail string the page attached to a check (e.g. 'invoked: document picker'). */
    String checkDetail(String id) {
        return unquote(evalJs(
                "(function(){"
              + "  var r = window.ZW_CHECKLIST.results && window.ZW_CHECKLIST.results();"
              + "  var e = r && r[" + jsString(id) + "];"
              + "  return e && e.detail != null ? String(e.detail) : '';"
              + "})()"));
    }

    /**
     * Kick a check and poll {@link #checkStatus} until it settles. Returns the
     * final status, or the last non-terminal value if it never settled.
     */
    String runCheckAndAwait(String id, long timeoutMs) {
        fireCheck(id);
        long deadline = SystemClock.uptimeMillis() + timeoutMs;
        String status = "pending";
        while (SystemClock.uptimeMillis() < deadline) {
            status = checkStatus(id);
            if (!"pending".equals(status) && !"running".equals(status) && !"missing".equals(status)) {
                return status;
            }
            SystemClock.sleep(250);
        }
        return status;
    }

    /** JSON of the page's per-check metadata (id -> {title,bucket,description}). */
    String dumpMeta() {
        return evalJs("JSON.stringify(window.ZW_CHECKLIST && window.ZW_CHECKLIST.meta || null)");
    }

    String dumpResults() {
        return evalJs("JSON.stringify((window.ZW_CHECKLIST && window.ZW_CHECKLIST.results && "
                + "window.ZW_CHECKLIST.results()) || null)");
    }

    /** Synchronous evaluateJavascript; returns the raw JSON-encoded result string. */
    String evalJs(final String script) {
        final String[] out = new String[1];
        final CountDownLatch latch = new CountDownLatch(1);
        runOnUi(new Runnable() {
            @Override public void run() {
                webView.evaluateJavascript(script, value -> {
                    out[0] = value;
                    latch.countDown();
                });
            }
        });
        try {
            if (!latch.await(10, TimeUnit.SECONDS)) {
                throw new AssertionError("evaluateJavascript timed out: " + script);
            }
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new AssertionError("Interrupted while evaluating: " + script, e);
        }
        return out[0];
    }

    private static void runOnUi(Runnable r) {
        InstrumentationRegistry.getInstrumentation().runOnMainSync(r);
    }

    private static String jsString(String s) {
        return "\"" + s.replace("\\", "\\\\").replace("\"", "\\\"") + "\"";
    }

    /** evaluateJavascript wraps strings in quotes and escapes them; undo that. */
    private static String unquote(String s) {
        if (s == null) {
            return null;
        }
        if (s.length() >= 2 && s.charAt(0) == '"' && s.charAt(s.length() - 1) == '"') {
            s = s.substring(1, s.length() - 1);
        }
        return s.replace("\\\"", "\"").replace("\\\\", "\\");
    }
}
