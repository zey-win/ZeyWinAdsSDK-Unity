package com.zeywinads.unity;

import android.content.Context;
import android.os.Build;
import android.view.DisplayCutout;
import android.view.View;
import android.view.WindowInsets;
import android.widget.FrameLayout;

/**
 * FrameLayout that keeps WebView content inside Android safe areas.
 * The root SDK container still covers the whole screen, but offer pages are
 * padded away from cutouts, notches, punch-hole cameras, and system bars.
 */
public final class ZeyWinAdsSafeAreaFrameLayout extends FrameLayout {

    public ZeyWinAdsSafeAreaFrameLayout(Context context) {
        super(context);
        setClipToPadding(false);

        if (Build.VERSION.SDK_INT >= 20) {
            setOnApplyWindowInsetsListener(new View.OnApplyWindowInsetsListener() {
                @Override
                public WindowInsets onApplyWindowInsets(View v, WindowInsets insets) {
                    return applyZeyWinInsets(insets);
                }
            });
        }
    }

    @Override
    protected void onAttachedToWindow() {
        super.onAttachedToWindow();
        if (Build.VERSION.SDK_INT >= 20) {
            requestApplyInsets();
        }
    }

    private WindowInsets applyZeyWinInsets(WindowInsets insets) {
        if (insets == null || Build.VERSION.SDK_INT < 20) {
            return insets;
        }

        int left = insets.getSystemWindowInsetLeft();
        int top = insets.getSystemWindowInsetTop();
        int right = insets.getSystemWindowInsetRight();
        int bottom = insets.getSystemWindowInsetBottom();

        if (Build.VERSION.SDK_INT >= 28) {
            DisplayCutout cutout = insets.getDisplayCutout();
            if (cutout != null) {
                left = Math.max(left, cutout.getSafeInsetLeft());
                top = Math.max(top, cutout.getSafeInsetTop());
                right = Math.max(right, cutout.getSafeInsetRight());
                bottom = Math.max(bottom, cutout.getSafeInsetBottom());
            }
        }

        if (getPaddingLeft() != left
            || getPaddingTop() != top
            || getPaddingRight() != right
            || getPaddingBottom() != bottom) {
            setPadding(left, top, right, bottom);
        }

        return insets;
    }
}
