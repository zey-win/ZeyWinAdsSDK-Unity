package com.zeywinads.unity;

import android.animation.ValueAnimator;
import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.RectF;
import android.os.Build;
import android.view.Gravity;
import android.view.DisplayCutout;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.widget.FrameLayout;

public final class ZeyWinAdsLoadingOverlay extends FrameLayout {
    private final MoneyProgressView progressView;
    private final int baseBottomMargin;
    private int bottomInset;
    private boolean fadingOut;

    public ZeyWinAdsLoadingOverlay(Context context) {
        super(context);
        setBackgroundColor(Color.rgb(15, 33, 158));
        setClickable(true);
        setFocusable(true);
        setAlpha(1f);
        setElevation(100000f);
        setTranslationZ(100000f);

        progressView = new MoneyProgressView(context);
        baseBottomMargin = dp(58);
        LayoutParams params = new LayoutParams(dp(360), dp(150));
        params.gravity = Gravity.BOTTOM | Gravity.CENTER_HORIZONTAL;
        params.bottomMargin = baseBottomMargin;
        addView(progressView, params);
        progressView.start();
    }

    @Override
    protected void onAttachedToWindow() {
        super.onAttachedToWindow();
        bringToFront();
        if (Build.VERSION.SDK_INT >= 20) {
            requestApplyInsets();
        }
        progressView.start();
    }

    @Override
    protected void onDetachedFromWindow() {
        progressView.stop();
        super.onDetachedFromWindow();
    }

    @Override
    public WindowInsets onApplyWindowInsets(WindowInsets insets) {
        if (Build.VERSION.SDK_INT >= 20 && insets != null) {
            int bottom = insets.getSystemWindowInsetBottom();
            if (Build.VERSION.SDK_INT >= 28) {
                DisplayCutout cutout = insets.getDisplayCutout();
                if (cutout != null) {
                    bottom = Math.max(bottom, cutout.getSafeInsetBottom());
                }
            }
            bottomInset = bottom;
            updateProgressMargin();
        }

        return super.onApplyWindowInsets(insets);
    }

    public void fadeOutAndDetach() {
        if (fadingOut) {
            return;
        }

        fadingOut = true;
        bringToFront();
        animate().cancel();
        animate()
            .alpha(0f)
            .setDuration(3000L)
            .withEndAction(new Runnable() {
                @Override
                public void run() {
                    progressView.stop();
                    setVisibility(View.GONE);
                    ViewGroup parent = (ViewGroup) getParent();
                    if (parent != null) {
                        parent.removeView(ZeyWinAdsLoadingOverlay.this);
                    }
                }
            })
            .start();
    }

    private void updateProgressMargin() {
        ViewGroup.LayoutParams rawParams = progressView.getLayoutParams();
        if (!(rawParams instanceof LayoutParams)) {
            return;
        }

        LayoutParams params = (LayoutParams) rawParams;
        int margin = baseBottomMargin + Math.max(0, bottomInset);
        if (params.bottomMargin != margin) {
            params.bottomMargin = margin;
            progressView.setLayoutParams(params);
        }
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }

    private static final class MoneyProgressView extends View {
        private static final long DurationMs = 8000L;
        private static final float[] ProgressTimes = { 0f, 0.08f, 0.14f, 0.27f, 0.34f, 0.48f, 0.58f, 0.71f, 0.83f, 0.93f, 1f };
        private static final float[] ProgressValues = { 0f, 0.03f, 0.12f, 0.18f, 0.36f, 0.45f, 0.62f, 0.7f, 0.86f, 0.92f, 1f };

        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final RectF rect = new RectF();
        private final ValueAnimator animator;
        private float progress;

        MoneyProgressView(Context context) {
            super(context);
            animator = ValueAnimator.ofFloat(0f, 1f);
            animator.setDuration(DurationMs);
            animator.setRepeatCount(0);
            animator.addUpdateListener(new ValueAnimator.AnimatorUpdateListener() {
                @Override
                public void onAnimationUpdate(ValueAnimator animation) {
                    float time = (Float) animation.getAnimatedValue();
                    progress = evaluateSteppedProgress(time);
                    invalidate();
                }
            });
        }

        void start() {
            animator.cancel();
            progress = 0f;
            animator.start();
        }

        void stop() {
            animator.cancel();
        }

        @Override
        protected void onDraw(Canvas canvas) {
            super.onDraw(canvas);

            float width = getWidth();
            float barLeft = dp(8);
            float barRight = width - dp(8);
            float barTop = dp(24);
            float barBottom = dp(54);
            float radius = (barBottom - barTop) * 0.5f;
            float moneyX = barLeft + radius + (barRight - barLeft - radius * 2f) * progress;

            paint.setStyle(Paint.Style.FILL);
            paint.setColor(Color.rgb(238, 247, 255));
            rect.set(barLeft, barTop, barRight, barBottom);
            canvas.drawRoundRect(rect, radius, radius, paint);

            paint.setColor(Color.rgb(28, 42, 105));
            rect.set(barLeft + dp(4), barTop + dp(4), barRight - dp(4), barBottom - dp(4));
            canvas.drawRoundRect(rect, radius, radius, paint);

            paint.setColor(Color.rgb(255, 188, 41));
            rect.set(barLeft + dp(4), barTop + dp(4), Math.max(barLeft + dp(28), moneyX), barBottom - dp(4));
            canvas.drawRoundRect(rect, radius, radius, paint);

            canvas.save();
            canvas.translate(moneyX, barTop - dp(10));
            canvas.rotate(-9f);
            drawMoneyPack(canvas);
            canvas.restore();

            paint.setColor(Color.WHITE);
            paint.setTextAlign(Paint.Align.CENTER);
            paint.setFakeBoldText(true);
            paint.setTextSize(dp(25));
            canvas.drawText("Loading " + Math.round(progress * 100f) + "%", width * 0.5f, dp(112), paint);
            paint.setFakeBoldText(false);
        }

        private void drawMoneyPack(Canvas canvas) {
            drawBill(canvas, -dp(27), -dp(28), dp(54), dp(26), Color.rgb(72, 235, 84), 0f);
            drawBand(canvas, -dp(15), -dp(20), dp(30), dp(9));
            drawBill(canvas, -dp(27), -dp(2), dp(54), dp(26), Color.rgb(72, 235, 84), 0f);
        }

        private void drawBill(Canvas canvas, float x, float y, float width, float height, int color, float slant) {
            canvas.save();
            canvas.skew(slant / 100f, 0f);
            paint.setColor(Color.rgb(17, 133, 48));
            rect.set(x, y, x + width, y + height);
            canvas.drawRoundRect(rect, dp(4), dp(4), paint);
            paint.setColor(color);
            rect.set(x + dp(3), y + dp(3), x + width - dp(3), y + height - dp(3));
            canvas.drawRoundRect(rect, dp(3), dp(3), paint);
            paint.setColor(Color.rgb(20, 160, 45));
            canvas.drawCircle(x + width * 0.5f, y + height * 0.5f, dp(7), paint);
            canvas.restore();
        }

        private void drawBand(Canvas canvas, float x, float y, float width, float height) {
            paint.setColor(Color.rgb(255, 91, 108));
            rect.set(x, y, x + width, y + height);
            canvas.drawRoundRect(rect, dp(2), dp(2), paint);
            paint.setColor(Color.rgb(245, 238, 202));
            rect.set(x, y + dp(2), x + width, y + height - dp(2));
            canvas.drawRoundRect(rect, dp(2), dp(2), paint);
        }

        private static float evaluateSteppedProgress(float time) {
            time = Math.max(0f, Math.min(1f, time));

            for (int i = 1; i < ProgressTimes.length; i++) {
                if (time > ProgressTimes[i]) {
                    continue;
                }

                float segment = inverseLerp(ProgressTimes[i - 1], ProgressTimes[i], time);
                segment = segment * segment * (3f - 2f * segment);
                return lerp(ProgressValues[i - 1], ProgressValues[i], segment);
            }

            return 1f;
        }

        private static float inverseLerp(float a, float b, float value) {
            if (Math.abs(b - a) < 0.0001f) {
                return 0f;
            }
            return Math.max(0f, Math.min(1f, (value - a) / (b - a)));
        }

        private static float lerp(float a, float b, float t) {
            return a + (b - a) * t;
        }

        private int dp(int value) {
            return Math.round(value * getResources().getDisplayMetrics().density);
        }
    }
}
