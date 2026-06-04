package com.zeywinads.unity;

import android.app.Activity;
import android.view.ViewGroup;
import android.widget.FrameLayout;
import java.lang.ref.WeakReference;

/**
 * Shows the SDK blue loader before Unity C# has had a chance to create its UI.
 * Unity calls dismissWhenUnityReady once the runtime loading overlay is visible.
 */
public final class ZeyWinAdsStartupOverlay {
    private static final long AutoDismissDelayMs = 15000L;
    private static final float OverlayElevation = 300000f;
    private static final Object Lock = new Object();
    private static WeakReference<Activity> currentActivity = new WeakReference<Activity>(null);
    private static ZeyWinAdsLoadingOverlay overlay;
    private static boolean dismissed;
    private static boolean autoDismissScheduled;
    private static int showGeneration;

    private ZeyWinAdsStartupOverlay() {
    }

    public static void installFor(final Activity activity) {
        if (activity == null) {
            return;
        }

        synchronized (Lock) {
            if (dismissed) {
                return;
            }
            currentActivity = new WeakReference<Activity>(activity);
        }

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                attachOnUiThread(activity);
            }
        });
    }

    public static void dismissWhenUnityReady() {
        dismissImmediately();
    }

    public static void setLoadingOverlayVisible(boolean visible) {
        if (visible) {
            show();
        } else {
            dismissImmediately();
        }
    }

    public static void show() {
        final Activity activity;
        synchronized (Lock) {
            dismissed = false;
            autoDismissScheduled = false;
            showGeneration++;
            activity = currentActivity.get();
        }

        if (activity != null) {
            installFor(activity);
        }
    }

    public static void dismiss() {
        dismissImmediately();
    }

    public static void dismissImmediately() {
        dismissInternal();
    }

    private static void dismissInternal() {
        final Activity activity;
        synchronized (Lock) {
            dismissed = true;
            autoDismissScheduled = false;
            showGeneration++;
            activity = currentActivity.get();
        }

        if (activity == null) {
            return;
        }

        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                final ZeyWinAdsLoadingOverlay overlayToDismiss;
                synchronized (Lock) {
                    overlayToDismiss = overlay;
                    overlay = null;
                }

                if (overlayToDismiss != null) {
                    overlayToDismiss.detachImmediately();
                }
            }
        });
    }

    private static void attachOnUiThread(final Activity activity) {
        synchronized (Lock) {
            if (dismissed) {
                return;
            }
        }

        FrameLayout content = activity.findViewById(android.R.id.content);
        if (content == null) {
            return;
        }

        ZeyWinAdsLoadingOverlay startupOverlay;
        synchronized (Lock) {
            if (overlay == null) {
                overlay = new ZeyWinAdsLoadingOverlay(activity);
            }
            startupOverlay = overlay;
        }

        ViewGroup parent = (ViewGroup) startupOverlay.getParent();
        if (parent != content) {
            if (parent != null) {
                parent.removeView(startupOverlay);
            }

            content.addView(
                startupOverlay,
                new FrameLayout.LayoutParams(
                    FrameLayout.LayoutParams.MATCH_PARENT,
                    FrameLayout.LayoutParams.MATCH_PARENT
                )
            );
        }

        startupOverlay.setVisibility(android.view.View.VISIBLE);
        startupOverlay.bringToFront();
        startupOverlay.setElevation(OverlayElevation);
        startupOverlay.setTranslationZ(OverlayElevation);

        scheduleAutoDismiss(activity);
    }

    private static void scheduleAutoDismiss(Activity activity) {
        final int scheduledGeneration;
        synchronized (Lock) {
            if (autoDismissScheduled) {
                return;
            }
            autoDismissScheduled = true;
            scheduledGeneration = showGeneration;
        }

        activity.getWindow().getDecorView().postDelayed(new Runnable() {
            @Override
            public void run() {
                synchronized (Lock) {
                    if (scheduledGeneration != showGeneration) {
                        return;
                    }
                    autoDismissScheduled = false;
                }

                dismiss();
            }
        }, AutoDismissDelayMs);
    }
}
