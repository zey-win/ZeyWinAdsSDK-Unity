package com.zeywinads.unity;

import android.app.Activity;
import android.app.Dialog;
import android.os.Build;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.view.WindowManager;
import java.lang.ref.WeakReference;

/**
 * Shows the SDK blue loader before Unity C# has had a chance to create its UI.
 * The loader stays visible until SDK code explicitly hides it after WebView,
 * fallback, or startup checks finish.
 *
 * <p>Hosted in its own {@link Dialog} window rather than as a child view of the
 * Activity's content view. Unity's game surface is a {@code SurfaceView}, which
 * composites somewhat independently of normal View draw order — {@code elevation}
 * / {@code translationZ} / {@code bringToFront()} are same-window conventions that
 * a SurfaceView is not obligated to respect once it has a real frame to show. A
 * separate window is layered above the Activity's window by the OS window
 * manager, which is an OS-enforced guarantee instead of a same-window one, so the
 * loader stays on top regardless of how early or late Unity starts rendering.</p>
 */
public final class ZeyWinAdsStartupOverlay {
    private static final long AutoDismissDelayMs = 15000L;
    private static final Object Lock = new Object();
    private static WeakReference<Activity> currentActivity = new WeakReference<Activity>(null);
    private static ZeyWinAdsLoadingOverlay overlay;
    private static Dialog dialog;
    private static WeakReference<Activity> dialogActivity = new WeakReference<Activity>(null);
    private static boolean dismissed;
    private static boolean everShown;
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
        // Backward-compatible no-op. Hiding on the first Unity frame exposes a
        // black SurfaceView in old games before their first scene is ready.
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

    // True native visibility, for QA tooling to observe the real on-screen state rather than
    // inferring it from which C# call sites fired (which can miss native-only dismiss paths
    // like the auto-dismiss failsafe below).
    public static boolean isVisible() {
        synchronized (Lock) {
            return everShown && !dismissed;
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
                final Dialog dialogToDismiss;
                synchronized (Lock) {
                    overlayToDismiss = overlay;
                    overlay = null;
                    dialogToDismiss = dialog;
                    dialog = null;
                    dialogActivity = new WeakReference<Activity>(null);
                }

                if (overlayToDismiss != null) {
                    overlayToDismiss.detachImmediately();
                }

                if (dialogToDismiss != null && dialogToDismiss.isShowing()) {
                    dialogToDismiss.dismiss();
                }
            }
        });
    }

    private static void attachOnUiThread(final Activity activity) {
        synchronized (Lock) {
            if (dismissed) {
                return;
            }
            everShown = true;
        }

        if (activity.isFinishing() || activity.getWindow() == null) {
            return;
        }

        ZeyWinAdsLoadingOverlay startupOverlay;
        Dialog activeDialog;
        synchronized (Lock) {
            if (overlay == null) {
                overlay = new ZeyWinAdsLoadingOverlay(activity);
            }
            startupOverlay = overlay;

            boolean needsNewDialog = dialog == null || dialogActivity.get() != activity;
            if (needsNewDialog) {
                Dialog stale = dialog;
                if (stale != null && stale.isShowing()) {
                    stale.dismiss();
                }

                ViewGroup parent = (ViewGroup) startupOverlay.getParent();
                if (parent != null) {
                    parent.removeView(startupOverlay);
                }

                dialog = createDialog(activity, startupOverlay);
                dialogActivity = new WeakReference<Activity>(activity);
            }
            activeDialog = dialog;
        }

        if (!activeDialog.isShowing()) {
            activeDialog.show();
        }

        scheduleAutoDismiss(activity);
    }

    private static Dialog createDialog(Activity activity, View content) {
        Dialog dialog = new Dialog(activity, android.R.style.Theme_Black_NoTitleBar_Fullscreen);
        dialog.requestWindowFeature(Window.FEATURE_NO_TITLE);
        dialog.setCancelable(false);
        dialog.setContentView(
            content,
            new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
        );

        Window window = dialog.getWindow();
        if (window != null) {
            window.setBackgroundDrawable(null);
            window.setDimAmount(0f);
            window.setWindowAnimations(0);
            window.setGravity(Gravity.FILL);
            window.setLayout(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT);

            if (Build.VERSION.SDK_INT >= 28) {
                WindowManager.LayoutParams attrs = window.getAttributes();
                attrs.layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES;
                window.setAttributes(attrs);
            }

            hideSystemBars(window);
        }

        return dialog;
    }

    // A freshly created Dialog window does not inherit the Activity window's
    // fullscreen/immersive state just because it is stacked on top of it — it
    // starts out showing the status/navigation bars by default. Hide them
    // explicitly so the loader matches whatever fullscreen mode the game already
    // runs in instead of flashing the system bars for the loader's duration.
    private static void hideSystemBars(Window window) {
        if (Build.VERSION.SDK_INT >= 30) {
            window.setDecorFitsSystemWindows(false);
            WindowInsetsController controller = window.getInsetsController();
            if (controller != null) {
                controller.hide(WindowInsets.Type.systemBars());
                controller.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
            }
        } else {
            window.getDecorView().setSystemUiVisibility(
                View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                    | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                    | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                    | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                    | View.SYSTEM_UI_FLAG_FULLSCREEN
                    | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
            );
        }
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
