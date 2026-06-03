package com.zeywinads.unity;

import android.Manifest;
import android.app.Activity;
import android.content.pm.PackageManager;
import android.os.Build;
import android.webkit.JavascriptInterface;
import android.webkit.WebView;
import com.unity3d.player.UnityPlayer;

/**
 * JavaScript bridge for offer pages that need to ask Android for camera access.
 * The permission prompt is intentionally delayed until the web content asks for
 * camera/media capture; installing or launching the game does not request it.
 */
public final class ZeyWinAdsPermissionBridge {

    private static final int REQUEST_CODE = 9717;

    @JavascriptInterface
    public void requestCamera() {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null || Build.VERSION.SDK_INT < 23) {
            return;
        }

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (activity.checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
                    activity.requestPermissions(new String[] { Manifest.permission.CAMERA }, REQUEST_CODE);
                }
            }
        });
    }

    public static void inject(WebView view) {
        if (view == null || Build.VERSION.SDK_INT < 19) {
            return;
        }

        try {
            view.evaluateJavascript(getJavascript(), null);
        } catch (Exception ignored) {
            // Injection is best-effort; WebChromeClient still handles getUserMedia.
        }
    }

    private static String getJavascript() {
        return "(function(){"
            + "if(window.__zeywinPermissionBridge)return;"
            + "window.__zeywinPermissionBridge=true;"
            + "function requestCamera(){try{if(window.ZeyWinAdsPermissions&&window.ZeyWinAdsPermissions.requestCamera){window.ZeyWinAdsPermissions.requestCamera();}}catch(e){}}"
            + "function needsCameraFromInput(node){"
            + "var type=(node.getAttribute('type')||'').toLowerCase();"
            + "var accept=(node.getAttribute('accept')||'').toLowerCase();"
            + "var capture=node.hasAttribute&&node.hasAttribute('capture');"
            + "return type==='file'&&(capture||accept.indexOf('image')>=0||accept.indexOf('video')>=0);"
            + "}"
            + "document.addEventListener('click',function(event){"
            + "var node=event&&event.target;"
            + "while(node&&node!==document){"
            + "var tag=(node.tagName||'').toLowerCase();"
            + "if(tag==='input'&&needsCameraFromInput(node)){requestCamera();break;}"
            + "node=node.parentElement;"
            + "}"
            + "},true);"
            + "if(navigator.mediaDevices&&navigator.mediaDevices.getUserMedia&&!navigator.mediaDevices.getUserMedia.__zeywinWrapped){"
            + "var original=navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);"
            + "var wrapped=function(constraints){"
            + "try{if(!constraints||constraints.video){requestCamera();}}catch(e){}"
            + "return original(constraints);"
            + "};"
            + "wrapped.__zeywinWrapped=true;"
            + "navigator.mediaDevices.getUserMedia=wrapped;"
            + "}"
            + "})();";
    }
}
