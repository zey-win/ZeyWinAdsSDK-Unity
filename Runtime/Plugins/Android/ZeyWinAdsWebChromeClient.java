package com.zeywinads.unity;

import android.Manifest;
import android.app.Activity;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.Message;
import android.net.Uri;
import android.webkit.CookieManager;
import android.webkit.PermissionRequest;
import android.webkit.ValueCallback;
import android.webkit.WebSettings;
import android.webkit.WebChromeClient;
import android.webkit.WebView;
import android.webkit.WebViewClient;
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
    private static final long PERMISSION_POLL_INTERVAL_MS = 250L;
    private static final int PERMISSION_POLL_MAX_ATTEMPTS = 40;
    private static final Handler handler = new Handler(Looper.getMainLooper());

    @Override
    public void onPermissionRequest(final PermissionRequest request) {
        handlePermissionRequest(request);
    }

    @Override
    public void onPermissionRequestCanceled(PermissionRequest request) {
        super.onPermissionRequestCanceled(request);
        denyQuietly(request);
    }

    /**
     * Shared by this class and the popup child's own WebChromeClient (see
     * configurePopupWebView) - the base WebChromeClient.onPermissionRequest is a
     * no-op, so a popup that doesn't wire this up leaves getUserMedia() hanging
     * forever with no dialog, no grant, no deny, and no console error.
     */
    private static void handlePermissionRequest(final PermissionRequest request) {
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
                String[] resources = request.getResources();
                if (allAndroidPermissionsGranted(activity, resources)) {
                    grantRequest(request, resources);
                    return;
                }

                requestAndroidPermissionsIfNeeded(activity, resources);
                waitForAndroidPermissions(activity, request, resources, 0);
            }
        });
    }

    private static void denyQuietly(PermissionRequest request) {
        if (request == null) {
            return;
        }

        try {
            request.deny();
        } catch (Exception ignored) {
            // PermissionRequest may already be closed by WebView.
        }
    }

    @Override
    public boolean onShowFileChooser(
        WebView webView,
        final ValueCallback<Uri[]> filePathCallback,
        final WebChromeClient.FileChooserParams fileChooserParams
    ) {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null) {
            filePathCallback.onReceiveValue(null);
            return true;
        }

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                ZeyWinAdsFileChooserFragment fragment = ZeyWinAdsFileChooserFragment.attach(activity);
                if (fragment == null) {
                    filePathCallback.onReceiveValue(null);
                    return;
                }
                fragment.showChooser(fileChooserParams, filePathCallback);
            }
        });
        return true;
    }

    @Override
    public boolean onCreateWindow(final WebView view, boolean isDialog, boolean isUserGesture, Message resultMsg) {
        if (view == null || resultMsg == null || resultMsg.obj == null) {
            return false;
        }

        final WebView child = new WebView(view.getContext());
        configurePopupWebView(child, view);

        WebView.WebViewTransport transport = (WebView.WebViewTransport) resultMsg.obj;
        transport.setWebView(child);
        resultMsg.sendToTarget();
        return true;
    }

    private static void waitForAndroidPermissions(
        final Activity activity,
        final PermissionRequest request,
        final String[] resources,
        final int attempt
    ) {
        if (allAndroidPermissionsGranted(activity, resources)) {
            grantRequest(request, resources);
            return;
        }

        if (attempt >= PERMISSION_POLL_MAX_ATTEMPTS) {
            try {
                request.deny();
            } catch (Exception ignored) {
                // PermissionRequest may already be closed by WebView.
            }
            return;
        }

        handler.postDelayed(new Runnable() {
            @Override
            public void run() {
                waitForAndroidPermissions(activity, request, resources, attempt + 1);
            }
        }, PERMISSION_POLL_INTERVAL_MS);
    }

    private static void grantRequest(PermissionRequest request, String[] resources) {
        try {
            request.grant(resources);
        } catch (Exception ignored) {
            try {
                request.deny();
            } catch (Exception ignoredAgain) {
                // PermissionRequest may already be closed by WebView.
            }
        }
    }

    private static boolean allAndroidPermissionsGranted(Activity activity, String[] resources) {
        if (activity == null || Build.VERSION.SDK_INT < 23 || resources == null) {
            return true;
        }

        for (String resource : resources) {
            String permission = toAndroidPermission(resource);
            if (permission != null && activity.checkSelfPermission(permission) != PackageManager.PERMISSION_GRANTED) {
                return false;
            }
        }

        return true;
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

    private static void configurePopupWebView(final WebView child, final WebView parent) {
        WebSettings settings = child.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setLoadWithOverviewMode(true);
        settings.setUseWideViewPort(true);
        settings.setJavaScriptCanOpenWindowsAutomatically(true);
        settings.setSupportMultipleWindows(false);

        if (Build.VERSION.SDK_INT >= 21) {
            CookieManager.getInstance().setAcceptThirdPartyCookies(child, true);
            settings.setMixedContentMode(WebSettings.MIXED_CONTENT_ALWAYS_ALLOW);
        }

        child.setWebChromeClient(new WebChromeClient() {
            @Override
            public void onCloseWindow(WebView window) {
                destroyChild(child);
            }

            @Override
            public void onPermissionRequest(PermissionRequest request) {
                handlePermissionRequest(request);
            }

            @Override
            public void onPermissionRequestCanceled(PermissionRequest request) {
                super.onPermissionRequestCanceled(request);
                denyQuietly(request);
            }

            @Override
            public boolean onShowFileChooser(
                WebView webView,
                final ValueCallback<Uri[]> filePathCallback,
                final WebChromeClient.FileChooserParams fileChooserParams
            ) {
                final Activity activity = UnityPlayer.currentActivity;
                if (activity == null) {
                    filePathCallback.onReceiveValue(null);
                    return true;
                }

                activity.runOnUiThread(new Runnable() {
                    @Override
                    public void run() {
                        ZeyWinAdsFileChooserFragment fragment = ZeyWinAdsFileChooserFragment.attach(activity);
                        if (fragment == null) {
                            filePathCallback.onReceiveValue(null);
                            return;
                        }
                        fragment.showChooser(fileChooserParams, filePathCallback);
                    }
                });
                return true;
            }
        });

        child.setWebViewClient(new WebViewClient() {
            @Override
            public boolean shouldOverrideUrlLoading(WebView view, String url) {
                return routePopupUrl(parent, child, url);
            }

            @Override
            public boolean shouldOverrideUrlLoading(WebView view, android.webkit.WebResourceRequest request) {
                if (request == null || request.getUrl() == null) {
                    return false;
                }
                return routePopupUrl(parent, child, request.getUrl().toString());
            }

            @Override
            public void onPageFinished(WebView view, String url) {
                super.onPageFinished(view, url);
                if (ZeyWinAdsWebViewNavigation.isWebUrl(url)) {
                    parent.loadUrl(url);
                    destroyChild(child);
                }
            }
        });
    }

    private static boolean routePopupUrl(WebView parent, WebView child, String url) {
        Activity activity = UnityPlayer.currentActivity;
        if (ZeyWinAdsWebViewNavigation.shouldOpenExternally(url)) {
            ZeyWinAdsWebViewNavigation.openExternal(activity, url);
            destroyChild(child);
            return true;
        }

        if (ZeyWinAdsWebViewNavigation.isWebUrl(url)) {
            parent.loadUrl(url);
            destroyChild(child);
            return true;
        }

        return false;
    }

    private static void destroyChild(WebView child) {
        try {
            child.stopLoading();
            child.destroy();
        } catch (Exception ignored) {
            // The popup WebView can already be detached by the platform.
        }
    }
}
