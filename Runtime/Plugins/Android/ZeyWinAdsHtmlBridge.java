package com.zeywinads.unity;

import android.webkit.JavascriptInterface;
import com.unity3d.player.UnityPlayer;

/**
 * JavaScript interface injected into HTML ad WebView.
 * HTML pages call these methods via window.ZeyWinAds.close(), etc.
 */
public class ZeyWinAdsHtmlBridge {

    private final String gameObjectName;

    public ZeyWinAdsHtmlBridge(String gameObjectName) {
        this.gameObjectName = gameObjectName;
    }

    @JavascriptInterface
    public void close() {
        UnityPlayer.UnitySendMessage(gameObjectName, "OnJsBridgeClose", "");
    }

    @JavascriptInterface
    public void complete() {
        UnityPlayer.UnitySendMessage(gameObjectName, "OnJsBridgeComplete", "");
    }

    @JavascriptInterface
    public void openUrl(final String url) {
        UnityPlayer.UnitySendMessage(gameObjectName, "OnJsBridgeOpenUrl", url);
    }
}
