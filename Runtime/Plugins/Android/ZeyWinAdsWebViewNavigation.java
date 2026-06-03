package com.zeywinads.unity;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;

/**
 * Shared navigation rules for SDK-owned offer WebViews.
 */
public final class ZeyWinAdsWebViewNavigation {

    private ZeyWinAdsWebViewNavigation() {
    }

    public static boolean isWebUrl(String url) {
        if (url == null) {
            return false;
        }

        String lower = url.toLowerCase();
        return lower.startsWith("http://")
            || lower.startsWith("https://")
            || lower.startsWith("about:")
            || lower.startsWith("data:")
            || lower.startsWith("javascript:");
    }

    public static boolean shouldOpenExternally(String url) {
        if (url == null || url.length() == 0 || isWebUrl(url)) {
            return false;
        }

        String lower = url.toLowerCase();
        return lower.startsWith("intent://")
            || lower.startsWith("market://")
            || lower.startsWith("tg://")
            || lower.startsWith("telegram://")
            || lower.startsWith("whatsapp://")
            || lower.startsWith("viber://")
            || lower.startsWith("mailto:")
            || lower.startsWith("tel:")
            || lower.startsWith("sms:");
    }

    public static boolean openExternal(Activity activity, String url) {
        if (activity == null || url == null || url.length() == 0) {
            return false;
        }

        try {
            Intent intent;
            if (url.toLowerCase().startsWith("intent://")) {
                intent = Intent.parseUri(url, Intent.URI_INTENT_SCHEME);
                String fallback = intent.getStringExtra("browser_fallback_url");
                if (intent.resolveActivity(activity.getPackageManager()) == null && fallback != null && fallback.length() > 0) {
                    intent = new Intent(Intent.ACTION_VIEW, Uri.parse(fallback));
                }
            } else {
                intent = new Intent(Intent.ACTION_VIEW, Uri.parse(url));
            }

            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            activity.startActivity(intent);
            return true;
        } catch (Exception ignored) {
            return false;
        }
    }
}
