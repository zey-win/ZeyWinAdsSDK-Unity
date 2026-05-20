package com.zeywinads.unity;

import android.content.Context;
import android.os.Handler;
import android.os.Looper;
import android.os.RemoteException;

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

    private static final int MAX_RETRIES = 3;
    private static final long[] RETRY_DELAYS_MS = {2000, 4000, 8000};

    private static String cachedClickId = null;
    private static boolean checked = false;

    // Independent cache for the raw referrer string used by getReferrerParam().
    // Kept separate from cachedClickId so we don't disturb the existing
    // referral retry flow.
    private static String cachedReferrerRaw = null;
    private static boolean rawFetched = false;

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
                        referrerClient.endConnection();
                        retryOrFinish(gameObjectName, callbackMethod, attempt);
                        return;
                    }

                    try {
                        ReferrerDetails details = referrerClient.getInstallReferrer();
                        String referrerUrl = details.getInstallReferrer();

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
                        referrerClient.endConnection();
                        retryOrFinish(gameObjectName, callbackMethod, attempt);
                    }
                }

                @Override
                public void onInstallReferrerServiceDisconnected() {
                    retryOrFinish(gameObjectName, callbackMethod, attempt);
                }
            });
        } catch (Exception e) {
            retryOrFinish(gameObjectName, callbackMethod, attempt);
        }
    }

    private static void retryOrFinish(final String gameObjectName, final String callbackMethod, final int attempt) {
        if (attempt < MAX_RETRIES - 1) {
            long delay = RETRY_DELAYS_MS[attempt];
            new Handler(Looper.getMainLooper()).postDelayed(new Runnable() {
                @Override
                public void run() {
                    attemptGetClickId(gameObjectName, callbackMethod, attempt + 1);
                }
            }, delay);
        } else {
            checked = true;
            UnityPlayer.UnitySendMessage(gameObjectName, callbackMethod, "");
        }
    }

    /**
     * Extracts click_id from a referrer string like "utm_source=zeywinads&click_id=xxx"
     */
    private static String extractClickId(String referrer) {
        return extractParam(referrer, "click_id");
    }

    /**
     * Extracts an arbitrary query parameter from a referrer string.
     * Returns null if the param is missing.
     */
    private static String extractParam(String referrer, String paramName) {
        if (referrer == null || referrer.isEmpty() || paramName == null) return null;

        try {
            String decoded = URLDecoder.decode(referrer, "UTF-8");
            String[] params = decoded.split("&");
            for (String param : params) {
                String[] kv = param.split("=", 2);
                if (kv.length == 2 && paramName.equals(kv[0])) {
                    return kv[1];
                }
            }
        } catch (Exception e) {
        }
        return null;
    }

    /**
     * Reads install referrer and extracts a single query parameter (e.g. "gclid").
     * Sends the value to Unity via UnitySendMessage. Empty string means
     * "referrer fetched but param missing" or "referrer unavailable".
     *
     * Independent of getClickId() — uses a separate cache so the existing
     * cross-app referral flow is unaffected.
     *
     * @param paramName The query parameter name to extract from the referrer.
     * @param gameObjectName Unity GameObject name to receive the callback.
     * @param callbackMethod Unity method name to call with the param value.
     */
    public static void getReferrerParam(final String paramName, final String gameObjectName, final String callbackMethod) {
        if (rawFetched) {
            String value = extractParam(cachedReferrerRaw, paramName);
            UnityPlayer.UnitySendMessage(gameObjectName, callbackMethod, value != null ? value : "");
            return;
        }
        attemptGetReferrerRaw(paramName, gameObjectName, callbackMethod, 0);
    }

    /**
     * Reads the raw install referrer string and sends it to Unity verbatim.
     * Lets the Unity side parse multiple parameters from a single fetch
     * without triggering parallel Install Referrer connections.
     *
     * Empty string means "referrer unavailable after retries".
     *
     * @param gameObjectName Unity GameObject name to receive the callback.
     * @param callbackMethod Unity method name to call with the raw referrer.
     */
    public static void getReferrerRaw(final String gameObjectName, final String callbackMethod) {
        if (rawFetched) {
            UnityPlayer.UnitySendMessage(gameObjectName, callbackMethod, cachedReferrerRaw != null ? cachedReferrerRaw : "");
            return;
        }
        // Reuse the same fetch path; passing null paramName signals "raw mode"
        // to attemptGetReferrerRaw — it sends back the whole referrer instead
        // of an extracted param.
        attemptGetReferrerRaw(null, gameObjectName, callbackMethod, 0);
    }

    private static void attemptGetReferrerRaw(final String paramName, final String gameObjectName, final String callbackMethod, final int attempt) {
        try {
            Context context = UnityPlayer.currentActivity.getApplicationContext();
            final InstallReferrerClient referrerClient = InstallReferrerClient.newBuilder(context).build();

            referrerClient.startConnection(new InstallReferrerStateListener() {
                @Override
                public void onInstallReferrerSetupFinished(int responseCode) {
                    if (responseCode != InstallReferrerClient.InstallReferrerResponse.OK) {
                        referrerClient.endConnection();
                        retryRawOrFinish(paramName, gameObjectName, callbackMethod, attempt);
                        return;
                    }

                    try {
                        ReferrerDetails details = referrerClient.getInstallReferrer();
                        String referrerUrl = details.getInstallReferrer();
                        referrerClient.endConnection();

                        if (referrerUrl == null || referrerUrl.isEmpty()) {
                            // Empty referrer — Play Services may not have it yet, retry.
                            retryRawOrFinish(paramName, gameObjectName, callbackMethod, attempt);
                            return;
                        }

                        // Got a referrer string — cache it and resolve the request.
                        // Missing param is NOT a retry condition (organic installs have no gclid).
                        cachedReferrerRaw = referrerUrl;
                        rawFetched = true;

                        // paramName == null means "raw mode" — return the whole string.
                        String result;
                        if (paramName == null) {
                            result = referrerUrl;
                        } else {
                            String extracted = extractParam(referrerUrl, paramName);
                            result = extracted != null ? extracted : "";
                        }
                        UnityPlayer.UnitySendMessage(gameObjectName, callbackMethod, result);
                    } catch (RemoteException e) {
                        referrerClient.endConnection();
                        retryRawOrFinish(paramName, gameObjectName, callbackMethod, attempt);
                    }
                }

                @Override
                public void onInstallReferrerServiceDisconnected() {
                    retryRawOrFinish(paramName, gameObjectName, callbackMethod, attempt);
                }
            });
        } catch (Exception e) {
            retryRawOrFinish(paramName, gameObjectName, callbackMethod, attempt);
        }
    }

    private static void retryRawOrFinish(final String paramName, final String gameObjectName, final String callbackMethod, final int attempt) {
        if (attempt < MAX_RETRIES - 1) {
            long delay = RETRY_DELAYS_MS[attempt];
            new Handler(Looper.getMainLooper()).postDelayed(new Runnable() {
                @Override
                public void run() {
                    attemptGetReferrerRaw(paramName, gameObjectName, callbackMethod, attempt + 1);
                }
            }, delay);
        } else {
            rawFetched = true; // mark as checked to avoid repeated fetch
            UnityPlayer.UnitySendMessage(gameObjectName, callbackMethod, "");
        }
    }
}
