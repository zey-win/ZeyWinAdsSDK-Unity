package com.zeywinads.unity;

import android.content.Intent;
import android.net.Uri;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import com.unity3d.player.UnityPlayer;

/**
 * Custom WebViewClient for HTML ads.
 * Intercepts link clicks and opens external URLs in system browser.
 * Allows only the original media URL to load in the WebView.
 */
public class ZeyWinAdsHtmlWebViewClient extends WebViewClient {

    private final String originalUrl;
    private final String gameObjectName;
    private boolean initialLoadDone = false;

    public ZeyWinAdsHtmlWebViewClient(String originalUrl, String gameObjectName) {
        this.originalUrl = originalUrl;
        this.gameObjectName = gameObjectName;
    }

    @Override
    public boolean shouldOverrideUrlLoading(WebView view, String url) {
        // Allow loading the original URL
        if (url.equals(originalUrl)) {
            return false;
        }

        // Open external HTTP/HTTPS links in system browser
        if (url.startsWith("http://") || url.startsWith("https://")) {
            try {
                Intent intent = new Intent(Intent.ACTION_VIEW, Uri.parse(url));
                intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                UnityPlayer.currentActivity.startActivity(intent);
            } catch (Exception e) {
                // Silently fail
            }
            return true;
        }

        return false;
    }

    @Override
    public void onPageFinished(WebView view, String url) {
        super.onPageFinished(view, url);
        if (!initialLoadDone) {
            initialLoadDone = true;
            UnityPlayer.UnitySendMessage(gameObjectName, "OnJsBridgePageLoaded", "");
        }
    }

    @Override
    @SuppressWarnings("deprecation")
    public void onReceivedError(WebView view, int errorCode, String description, String failingUrl) {
        super.onReceivedError(view, errorCode, description, failingUrl);
        if (!initialLoadDone && (failingUrl == null || failingUrl.equals(originalUrl))) {
            initialLoadDone = true;
            String message = description != null ? description : "WebView load error";
            UnityPlayer.UnitySendMessage(gameObjectName, "OnJsBridgeLoadError", message);
        }
    }
}
