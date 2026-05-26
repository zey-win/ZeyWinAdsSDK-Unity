package com.zeywinads.unity;

import android.content.Context;
import android.content.pm.PackageManager;
import android.os.Build;
import android.provider.Settings;
import android.telephony.SubscriptionInfo;
import android.telephony.SubscriptionManager;
import android.telephony.TelephonyManager;

import com.google.android.gms.ads.identifier.AdvertisingIdClient;
import com.google.android.gms.common.GooglePlayServicesNotAvailableException;
import com.google.android.gms.common.GooglePlayServicesRepairableException;

import com.unity3d.player.UnityPlayer;

import java.io.IOException;
import java.util.List;

public class ZeyWinAdsDevice {

    /**
     * Gets the Google Advertising ID (GAID).
     * Falls back to Android ID if GAID is unavailable or ad tracking is limited.
     * MUST be called from a background thread — blocks until result is available.
     */
    public static String getGAID() {
        try {
            Context context = UnityPlayer.currentActivity.getApplicationContext();
            AdvertisingIdClient.Info adInfo = AdvertisingIdClient.getAdvertisingIdInfo(context);
            if (!adInfo.isLimitAdTrackingEnabled()) {
                String gaid = adInfo.getId();
                if (gaid != null && !gaid.isEmpty()) {
                    return gaid;
                }
            }
        } catch (Exception e) {
            // GAID unavailable, fall through to Android ID
        }

        // Fallback: Android ID (persistent per device, reset on factory reset)
        try {
            Context context = UnityPlayer.currentActivity.getApplicationContext();
            String androidId = Settings.Secure.getString(context.getContentResolver(), Settings.Secure.ANDROID_ID);
            if (androidId != null && !androidId.isEmpty()) {
                return "aid_" + androidId;
            }
        } catch (Exception e) {
            // Android ID fallback failed.
        }

        return "";
    }

    /**
     * Gets Android ID synchronously for fast startup referral matching.
     * This avoids waiting for Google Play Services when the server can match the
     * same fallback ID format used by getGAID().
     */
    public static String getAndroidId() {
        try {
            Context context = UnityPlayer.currentActivity.getApplicationContext();
            String androidId = Settings.Secure.getString(context.getContentResolver(), Settings.Secure.ANDROID_ID);
            if (androidId != null && !androidId.isEmpty()) {
                return "aid_" + androidId;
            }
        } catch (Exception e) {
            // Android ID fallback failed.
        }

        return "";
    }

    /**
     * Gets the SIM card country ISO code (lowercase, e.g. "us", "mm").
     * Does not require any permissions.
     */
    public static String getSimCountryIso() {
        try {
            Context context = UnityPlayer.currentActivity.getApplicationContext();
            TelephonyManager tm = (TelephonyManager) context.getSystemService(Context.TELEPHONY_SERVICE);
            if (tm == null) return "";
            String country = tm.getSimCountryIso();
            return country != null ? country.toLowerCase() : "";
        } catch (Exception e) {
            return "";
        }
    }

    /**
     * Checks if a SIM card is present and ready.
     */
    public static boolean hasSim() {
        try {
            Context context = UnityPlayer.currentActivity.getApplicationContext();
            TelephonyManager tm = (TelephonyManager) context.getSystemService(Context.TELEPHONY_SERVICE);
            if (tm == null) return false;

            if (hasActiveSubscription(context)) return true;

            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                int phoneCount = tm.getPhoneCount();
                for (int slot = 0; slot < phoneCount; slot++) {
                    if (tm.getSimState(slot) == TelephonyManager.SIM_STATE_READY) {
                        return true;
                    }
                }
            }

            return tm.getSimState() == TelephonyManager.SIM_STATE_READY;
        } catch (Exception e) {
            return false;
        }
    }

    private static boolean hasActiveSubscription(Context context) {
        try {
            SubscriptionManager sm = (SubscriptionManager) context.getSystemService(Context.TELEPHONY_SUBSCRIPTION_SERVICE);
            if (sm == null) return false;

            List<SubscriptionInfo> subscriptions = sm.getActiveSubscriptionInfoList();
            return subscriptions != null && !subscriptions.isEmpty();
        } catch (SecurityException e) {
            return false;
        } catch (Exception e) {
            return false;
        }
    }

    /**
     * Checks if a given package is installed on the device.
     * Requires <queries> declarations in AndroidManifest for Android 11+.
     */
    public static boolean isAppInstalled(String packageName) {
        try {
            Context context = UnityPlayer.currentActivity.getApplicationContext();
            PackageManager pm = context.getPackageManager();
            pm.getPackageInfo(packageName, 0);
            return true;
        } catch (PackageManager.NameNotFoundException e) {
            return false;
        } catch (Exception e) {
            return false;
        }
    }
}
