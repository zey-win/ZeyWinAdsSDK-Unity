package com.zeywinads.unity;

import android.Manifest;
import android.app.Activity;
import android.content.pm.PackageManager;
import android.os.Build;
import android.webkit.PermissionRequest;
import android.webkit.WebChromeClient;
import com.unity3d.player.UnityPlayer;
import java.util.ArrayList;
import java.util.List;

/**
 * WebChromeClient used by ZeyWin-owned WebViews.
 * It turns camera/microphone requests from web content into native Android
 * runtime permission prompts, then grants the WebView request when possible.
 */
public class ZeyWinAdsWebChromeClient extends WebChromeClient {

    private static final int REQUEST_CODE = 9716;

    @Override
    public void onPermissionRequest(final PermissionRequest request) {
        if (request == null) {
            return;
        }

        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null) {
            request.deny();
            return;
        }

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                requestAndroidPermissionsIfNeeded(activity, request.getResources());
                try {
                    request.grant(request.getResources());
                } catch (Exception ignored) {
                    request.deny();
                }
            }
        });
    }

    private static void requestAndroidPermissionsIfNeeded(Activity activity, String[] resources) {
        if (Build.VERSION.SDK_INT < 23 || resources == null) {
            return;
        }

        List<String> missing = new ArrayList<String>();
        for (String resource : resources) {
            String permission = toAndroidPermission(resource);
            if (permission == null) {
                continue;
            }

            if (activity.checkSelfPermission(permission) != PackageManager.PERMISSION_GRANTED) {
                missing.add(permission);
            }
        }

        if (!missing.isEmpty()) {
            activity.requestPermissions(missing.toArray(new String[0]), REQUEST_CODE);
        }
    }

    private static String toAndroidPermission(String resource) {
        if (PermissionRequest.RESOURCE_VIDEO_CAPTURE.equals(resource)) {
            return Manifest.permission.CAMERA;
        }

        if (PermissionRequest.RESOURCE_AUDIO_CAPTURE.equals(resource)) {
            return Manifest.permission.RECORD_AUDIO;
        }

        return null;
    }
}
