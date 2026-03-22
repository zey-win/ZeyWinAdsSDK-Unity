package com.zeywinads.unity;

import android.content.Context;
import android.os.RemoteException;
import android.util.Log;

import com.android.installreferrer.api.InstallReferrerClient;
import com.android.installreferrer.api.InstallReferrerStateListener;
import com.android.installreferrer.api.ReferrerDetails;
import com.unity3d.player.UnityPlayer;

import java.net.URLDecoder;

/**
 * Reads the Play Store install referrer to extract click_id for cross-app referral tracking.
 * Uses the Play Install Referrer API which works across different signing keys and developer accounts.
 */
public class ZeyWinAdsInstallReferrer {

    private static final String TAG = "ZeyWinAds";
    private static String cachedClickId = null;
    private static boolean checked = false;

    /**
     * Reads install referrer and extracts click_id if present.
     * Sends result to Unity via UnitySendMessage.
     * @param gameObjectName Unity GameObject name to receive the callback
     * @param callbackMethod Unity method name to call with the click_id (or empty string)
     */
    public static void getClickId(final String gameObjectName, final String callbackMethod) {
        if (checked) {
            UnityPlayer.UnitySendMessage(gameObjectName, callbackMethod, cachedClickId != null ? cachedClickId : "");
            return;
        }

        try {
            Context context = UnityPlayer.currentActivity.getApplicationContext();
            final InstallReferrerClient referrerClient = InstallReferrerClient.newBuilder(context).build();

            referrerClient.startConnection(new InstallReferrerStateListener() {
                @Override
                public void onInstallReferrerSetupFinished(int responseCode) {
                    checked = true;

                    if (responseCode != InstallReferrerClient.InstallReferrerResponse.OK) {
                        Log.w(TAG, "Install referrer not available, response code: " + responseCode);
                        referrerClient.endConnection();
                        UnityPlayer.UnitySendMessage(gameObjectName, callbackMethod, "");
                        return;
                    }

                    try {
                        ReferrerDetails details = referrerClient.getInstallReferrer();
                        String referrerUrl = details.getInstallReferrer();
                        Log.i(TAG, "Install referrer: " + referrerUrl);

                        cachedClickId = extractClickId(referrerUrl);
                        referrerClient.endConnection();

                        UnityPlayer.UnitySendMessage(gameObjectName, callbackMethod,
                            cachedClickId != null ? cachedClickId : "");
                    } catch (RemoteException e) {
                        Log.e(TAG, "Failed to get install referrer: " + e.getMessage());
                        referrerClient.endConnection();
                        UnityPlayer.UnitySendMessage(gameObjectName, callbackMethod, "");
                    }
                }

                @Override
                public void onInstallReferrerServiceDisconnected() {
                    // No retry needed — we only check once
                }
            });
        } catch (Exception e) {
            checked = true;
            Log.e(TAG, "Install referrer setup failed: " + e.getMessage());
            UnityPlayer.UnitySendMessage(gameObjectName, callbackMethod, "");
        }
    }

    /**
     * Extracts click_id from a referrer string like "utm_source=zeywinads&click_id=xxx"
     */
    private static String extractClickId(String referrer) {
        if (referrer == null || referrer.isEmpty()) return null;

        try {
            String decoded = URLDecoder.decode(referrer, "UTF-8");
            String[] params = decoded.split("&");
            for (String param : params) {
                String[] kv = param.split("=", 2);
                if (kv.length == 2 && "click_id".equals(kv[0])) {
                    return kv[1];
                }
            }
        } catch (Exception e) {
            Log.e(TAG, "Failed to parse referrer: " + e.getMessage());
        }
        return null;
    }
}
