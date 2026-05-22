package com.zeywinads.unity;

import android.content.Intent;
import android.net.Uri;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import com.unity3d.player.UnityPlayer;

/**
 * Custom WebViewClient for HTML ads.
 * Keeps normal offer redirects inside the WebView. Only non-web schemes that
 * cannot render in WebView are opened externally.
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
        return shouldOpenExternally(url);
    }

    @Override
    public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
        if (request == null || request.getUrl() == null) {
            return false;
        }

        return shouldOpenExternally(request.getUrl().toString());
    }

    private boolean shouldOpenExternally(String url) {
        if (url == null || url.length() == 0) {
            return false;
        }

        String lower = url.toLowerCase();
        if (lower.startsWith("http://") || lower.startsWith("https://")) {
            return false;
        }

        if (lower.startsWith("market://") ||
            lower.startsWith("intent://") ||
            lower.startsWith("mailto:") ||
            lower.startsWith("tel:") ||
            lower.startsWith("sms:")) {
            openExternal(url);
            return true;
        }

        return false;
    }

    private void openExternal(String url) {
        try {
            Intent intent = new Intent(Intent.ACTION_VIEW, Uri.parse(url));
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            UnityPlayer.currentActivity.startActivity(intent);
        } catch (Exception e) {
            // Keep the WebView stable if the device cannot resolve the scheme.
        }
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

    @Override
    public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
        super.onReceivedError(view, request, error);
        if (initialLoadDone || request == null || !request.isForMainFrame()) {
            return;
        }

        initialLoadDone = true;
        String message = "WebView load error";
        if (error != null && error.getDescription() != null) {
            message = error.getDescription().toString();
        }
        UnityPlayer.UnitySendMessage(gameObjectName, "OnJsBridgeLoadError", message);
    }
}
