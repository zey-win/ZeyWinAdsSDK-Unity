package com.zeywinads.unity;

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
        return false;
    }

    @Override
    public void onPageFinished(WebView view, String url) {
        super.onPageFinished(view, url);
        if (!initialLoadDone) {
            initialLoadDone = true;
            UnityPlayer.UnitySendMessage(gameObjectName, "OnWebViewPageLoaded", "");
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
}
