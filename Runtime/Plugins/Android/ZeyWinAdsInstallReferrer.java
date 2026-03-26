package com.zeywinads.unity;

import android.content.Context;
import android.os.Handler;
import android.os.Looper;
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
 * Retries up to 3 times with delays for slow devices where Play Services aren't ready immediately.
 */
public class ZeyWinAdsInstallReferrer {

    private static final String TAG = "ZeyWinAds";
    private static final int MAX_RETRIES = 3;
    private static final long[] RETRY_DELAYS_MS = {2000, 4000, 8000};

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

        attemptGetClickId(gameObjectName, callbackMethod, 0);
    }

    private static void attemptGetClickId(final String gameObjectName, final String callbackMethod, final int attempt) {
        try {
            Context context = UnityPlayer.currentActivity.getApplicationContext();
            final InstallReferrerClient referrerClient = InstallReferrerClient.newBuilder(context).build();

            referrerClient.startConnection(new InstallReferrerStateListener() {
                @Override
                public void onInstallReferrerSetupFinished(int responseCode) {
                    if (responseCode != InstallReferrerClient.InstallReferrerResponse.OK) {
                        Log.w(TAG, "Install referrer not available, response code: " + responseCode + " (attempt " + (attempt + 1) + ")");
                        referrerClient.endConnection();
                        retryOrFinish(gameObjectName, callbackMethod, attempt);
                        return;
                    }

                    try {
                        ReferrerDetails details = referrerClient.getInstallReferrer();
                        String referrerUrl = details.getInstallReferrer();
                        Log.i(TAG, "Install referrer: " + referrerUrl);

                        String clickId = extractClickId(referrerUrl);
                        referrerClient.endConnection();

                        if (clickId != null) {
                            cachedClickId = clickId;
                            checked = true;
                            UnityPlayer.UnitySendMessage(gameObjectName, callbackMethod, clickId);
                        } else {
                            // Referrer exists but no click_id — might not be ready yet
                            retryOrFinish(gameObjectName, callbackMethod, attempt);
                        }
                    } catch (RemoteException e) {
                        Log.e(TAG, "Failed to get install referrer: " + e.getMessage());
                        referrerClient.endConnection();
                        retryOrFinish(gameObjectName, callbackMethod, attempt);
                    }
                }

                @Override
                public void onInstallReferrerServiceDisconnected() {
                    Log.w(TAG, "Install referrer service disconnected (attempt " + (attempt + 1) + ")");
                    retryOrFinish(gameObjectName, callbackMethod, attempt);
                }
            });
        } catch (Exception e) {
            Log.e(TAG, "Install referrer setup failed: " + e.getMessage() + " (attempt " + (attempt + 1) + ")");
            retryOrFinish(gameObjectName, callbackMethod, attempt);
        }
    }

    private static void retryOrFinish(final String gameObjectName, final String callbackMethod, final int attempt) {
        if (attempt < MAX_RETRIES - 1) {
            long delay = RETRY_DELAYS_MS[attempt];
            Log.i(TAG, "Retrying install referrer in " + delay + "ms...");
            new Handler(Looper.getMainLooper()).postDelayed(new Runnable() {
                @Override
                public void run() {
                    attemptGetClickId(gameObjectName, callbackMethod, attempt + 1);
                }
            }, delay);
        } else {
            Log.w(TAG, "Install referrer: no click_id found after " + MAX_RETRIES + " attempts");
            checked = true;
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
