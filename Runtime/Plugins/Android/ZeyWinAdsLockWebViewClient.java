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
}
