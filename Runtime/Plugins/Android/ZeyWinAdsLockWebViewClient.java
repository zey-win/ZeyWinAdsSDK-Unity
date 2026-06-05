package com.zeywinads.unity;

import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import com.unity3d.player.UnityPlayer;

/**
 * WebViewClient for locked offer WebViews.
 * Keeps navigation inside the WebView and notifies Unity when the first page is visible.
 */
public class ZeyWinAdsLockWebViewClient extends WebViewClient {

    private final String gameObjectName;
    private boolean initialLoadDone = false;

    public ZeyWinAdsLockWebViewClient(String gameObjectName) {
        this.gameObjectName = gameObjectName;
    }

    @Override
    public boolean shouldOverrideUrlLoading(WebView view, String url) {
        if (ZeyWinAdsWebViewNavigation.shouldOpenExternally(url)) {
            return ZeyWinAdsWebViewNavigation.openExternal(UnityPlayer.currentActivity, url);
        }
        return false;
    }

    @Override
    public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
        if (request != null && request.getUrl() != null) {
            String url = request.getUrl().toString();
            if (ZeyWinAdsWebViewNavigation.shouldOpenExternally(url)) {
                return ZeyWinAdsWebViewNavigation.openExternal(UnityPlayer.currentActivity, url);
            }
        }
        return false;
    }

    @Override
    public void onPageFinished(WebView view, String url) {
        super.onPageFinished(view, url);
        ZeyWinAdsPermissionBridge.inject(view);
        String finishedUrl = url != null ? url : "";
        UnityPlayer.UnitySendMessage(gameObjectName, "OnWebViewNavigationFinished", finishedUrl);
        if (!initialLoadDone) {
            initialLoadDone = true;
            UnityPlayer.UnitySendMessage(gameObjectName, "OnWebViewPageLoaded", finishedUrl);
        }
    }

    @Override
    @SuppressWarnings("deprecation")
    public void onReceivedError(WebView view, int errorCode, String description, String failingUrl) {
        super.onReceivedError(view, errorCode, description, failingUrl);
        if (!initialLoadDone) {
            initialLoadDone = true;
            String message = description != null ? description : "WebView load error";
            UnityPlayer.UnitySendMessage(gameObjectName, "OnWebViewLoadError", message);
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
        UnityPlayer.UnitySendMessage(gameObjectName, "OnWebViewLoadError", message);
    }
}
