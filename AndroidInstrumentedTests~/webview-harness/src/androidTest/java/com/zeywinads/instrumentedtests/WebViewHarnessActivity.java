package com.zeywinads.instrumentedtests;

import android.app.Activity;
import android.graphics.Color;
import android.net.Uri;
import android.os.Bundle;
import android.util.Log;
import android.webkit.CookieManager;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.widget.FrameLayout;

import com.unity3d.player.UnityPlayer;
import com.zeywinads.unity.ZeyWinAdsLockWebViewClient;
import com.zeywinads.unity.ZeyWinAdsPermissionBridge;
import com.zeywinads.unity.ZeyWinAdsWebChromeClient;

import java.util.concurrent.atomic.AtomicInteger;

/**
 * Minimal host Activity for the on-device WebView tests.
 *
 * <p>It stands up ONE {@link WebView} wired exactly like the production offer
 * surface ({@code WebViewLock.ShowAndroidWebView}) so Espresso / Espresso-Intents
 * can drive the real {@link ZeyWinAdsWebChromeClient} /
 * {@link ZeyWinAdsLockWebViewClient} code paths without Unity in the process.
 *
 * <p>A plain {@link Activity} (not {@code FragmentActivity}) is deliberate:
 * {@code ZeyWinAdsFileChooserFragment} attaches through the framework
 * {@code getFragmentManager()}, which only a plain Activity exposes.
 */
public class WebViewHarnessActivity extends Activity {

    private static final String TAG = "ZWHarness";

    /** Matches WebViewLock.GAME_OBJECT_NAME; only used for UnitySendMessage routing (no-op here). */
    public static final String GAME_OBJECT_NAME = "ZeyWinAdsWebViewLock";

    /**
     * Bumped every time the WebView asks the SDK to show a file chooser. Lets a
     * test tell "the page's input.click() actually reached the SDK" apart from
     * "WebView swallowed it (needs a user gesture)". Reset it in the test's
     * setup.
     */
    public static final AtomicInteger fileChooserInvocations = new AtomicInteger(0);

    public WebView webView;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        // SDK Android code reads UnityPlayer.currentActivity for a Context.
        UnityPlayer.currentActivity = this;

        FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(Color.BLACK);

        webView = new WebView(this);
        configureLikeProduction(webView);

        root.addView(webView, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT));
        setContentView(root);
    }

    /**
     * Line-for-line mirror of {@code WebViewLock.ShowAndroidWebView}
     * (Runtime/UI/WebViewLock.cs): every {@code getSettings()} call plus the
     * chrome / webview / JS-bridge wiring the real offer surface applies.
     *
     * <p>TODO(SDK change #4): replace this body with a call to the shared
     * "build configured offer WebView" factory once it exists, so there is a
     * single source of truth for both production and this harness.
     */
    private void configureLikeProduction(WebView wv) {
        wv.setBackgroundColor(Color.BLACK);
        // LAYER_TYPE_HARDWARE = 2 (critical for WebGL content in production).
        wv.setLayerType(WebView.LAYER_TYPE_HARDWARE, null);

        WebSettings s = wv.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);
        s.setLoadWithOverviewMode(true);
        s.setUseWideViewPort(true);
        s.setSupportZoom(true);
        s.setBuiltInZoomControls(true);
        s.setDisplayZoomControls(false);
        s.setMediaPlaybackRequiresUserGesture(false);
        s.setAllowFileAccess(true);
        s.setJavaScriptCanOpenWindowsAutomatically(true);
        s.setSupportMultipleWindows(true);
        s.setMixedContentMode(WebSettings.MIXED_CONTENT_ALWAYS_ALLOW);

        CookieManager cm = CookieManager.getInstance();
        cm.setAcceptCookie(true);
        cm.setAcceptThirdPartyCookies(wv, true);

        // The REAL chrome client, only wrapped to count file-chooser invocations.
        wv.setWebChromeClient(new CountingChromeClient());
        wv.addJavascriptInterface(new ZeyWinAdsPermissionBridge(), "ZeyWinAdsPermissions");
        wv.setWebViewClient(new ZeyWinAdsLockWebViewClient(GAME_OBJECT_NAME));
    }

    /**
     * {@link ZeyWinAdsWebChromeClient} verbatim — the override only records that
     * {@code onShowFileChooser} fired, then delegates to {@code super} so the SDK
     * path (fragment attach + chooser Intent) runs unchanged.
     */
    static final class CountingChromeClient extends ZeyWinAdsWebChromeClient {
        @Override
        public boolean onShowFileChooser(WebView webView,
                                         ValueCallback<Uri[]> filePathCallback,
                                         FileChooserParams fileChooserParams) {
            int n = fileChooserInvocations.incrementAndGet();
            Log.i(TAG, "onShowFileChooser #" + n
                    + " mode=" + (fileChooserParams != null ? fileChooserParams.getMode() : "?"));
            return super.onShowFileChooser(webView, filePathCallback, fileChooserParams);
        }
    }
}
