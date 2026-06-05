package com.zeywinads.unity;

import android.animation.ValueAnimator;
import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.RectF;
import android.os.Build;
import android.util.Base64;
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

    public ZeyWinAdsLoadingOverlay(Context context) {
        super(context);
        setBackgroundColor(Color.rgb(15, 33, 158));
        setClickable(true);
        setFocusable(true);
        setClipChildren(false);
        setClipToPadding(false);
        setAlpha(1f);
        setElevation(100000f);
        setTranslationZ(100000f);

        progressView = new MoneyProgressView(context);
        baseBottomMargin = dp(58);
        LayoutParams params = new LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(240));
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

    public void detachImmediately() {
        animate().cancel();
        progressView.stop();
        setVisibility(View.GONE);

        ViewGroup parent = (ViewGroup) getParent();
        if (parent != null) {
            parent.removeView(this);
        }
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
        private static final String MoneyImageBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAADQAAABgCAYAAABSZ1EKAAAgAElEQVR4nMW8Waxl2Xke9q1pj2c+5851a+yu6nlgk2JzECVKlChKSGjBhgfoQYDtxE+GgQAJED8Eecn0EAR+8IPfHCBOgEAyBQNWLEuWGFGiOXWzyZ675uHWnc+85zUE/zq3msVmMxTjBFmFW/fWuefsvf/1j9/3/6vYJz7177Ax" +
            "PMHD0zV0kgVirqEbBS0Y7t3fFf/kf/zPN+dZuvff/Hf/ZbS3v1O+8vL30etNEIYV1jaOkMYl3v/gCfzKF76B137wIk6O1/DGW88hjSqsrZ0gTZbotDIkSYF2Osf64Bg1t3j2xe/jv/6v/if8t//kP8PXfv9VMDnCnfd2cfP6LvrrGZRq0OmViFON/kaJzd1TvPEXz6HOhrjyzF20ugsYI/DRJbZ2" +
            "fhetJMeiSBHIBqHQsFZgkbf4xtrxf/HCCz/860mgf+vGnQthElW90/FQdLvzQVnGtZBaDfrTZjrrIQga9HtTnIyHaBqF0/EQnXQJIQ0kN1CygQFHO8rR6ACDtUMsl23cuX0Nv/obf4Q/+5NP48q1MT54axtJW8M5QEgHziwYZ+gMcnBucfxwDUIK9NamsIZ/vEBpXCArYyRRgbJI0EmzYJBO/+Fv" +
            "/87v/bXnX/neV7vDw5c+8+lv/s1nX/jBcy89/9ZXaqM+0WnNny/K5KpS+ok4qvS9e7udJy/fmhyPh9xo6U5Ph/4GcVRBCBLIQAgLzp0XdLbo4vmXvof//X/7Xfz23/oa3nzjAroDjfmkhdmkhSAyYACCcPU5IRmGm1McPRihKVN0+wtIqeH8uz4i0Kg/wd29XWxtHmAyGSKQ+lNf/c0//Gef+PJf" +
            "XFh74r6AmHOTTPHsCzd2Ouu3zn/68994+fyVWy9cvfbuZ1Sc/8ZoePLVk9PBb14+f/9ituxsB0oPP7j+5L4QJqnr0KVpbp3liMIaxnH0WktkeYKLT1zH9Q+eRqNTPPfS9/D6d57H7sU53n9zB61O47UkpYPgFoyRVhbQtcDkeIhAAd3hDPYjZieGa38P2+tHmM76AHe801p8+atf+cN/9vJXvtm1" +
            "Vw7T/UKx3DCUziKzDC6s+JLV2Lx4EiWjg9a1F95qjbb2Rp//5T/eTdLlFy5fvvHLvd7kN7/4S3/xm4rrzd2d/Vdv3LxSDPqTK00TdbnUCzgWpknWlHWIJ66+j6/9/t/G7/zdf4Gv/7uXsXtpgf17Q2+WQllvcmR69CUDh/5ojoN7a9B1gv5oho8oCOLlF38dJ9MRdtYPcePu5Wd/+7f+4B//2hf/" +
            "+DPrn7ge3uYKY8OhuYEJJayUqEUAKzhmjUDDG5bRRToFatGgTUFg/Tjevvhu99nn37r06U/+5S8988w7v/j5V7/16X5/9is7G/uv3nlwYXdn6+FXuOWDybybXXvq7cV3vvU5N9qYYDh6gDt3LmAwKnH7g020OjWsI7Oz4HBwEBhtTbGcxZiPe4jTBq3uEtb+yJfEFz77GVy/8wTrtrLzv/r5P/uf" +
            "/9p//AcvXXzyTnxsgZnhCGwDYSzAASYYNAdq52BIOBagZhKkwQYcSwM00qARFhm3KNKK9dZPZGtwf/tzn39td7R+59ovfe7Pnjl37uGX253TT82z9t9pd+fXqir97IMHV+yXvvInp3/6R6+E5y8v63s3RzCOtKTBGfPBhbQUJRpJu8Tx3hqcjdBfm8C5H6lJXLv2VYpq/XPbD//x3/4bv/cfXX32" +
            "3XbBLI4rhsgYyNqi1ThI66CcBZyGNQbGMq8xyyQcZ3DQYJJBOwHDOTI45LHDggEZczjNgLibgYWL9vnLB4jbB70XP/PtYZC4T+0++fbLo7XsPx1u3n3pxvXtL69tzIb7964wbRFks7QOo5oBXCiljXMK6+fGmBy3kc16aLULREkJ51ZaYq98+t/Iz7z4nX/wK1/4+v/wxa/8ZZqxCuMc0AEg6T0c" +
            "0DW86muvJaAGUABohPShGNxCgMyCvhikAQxnsPSHNo8BgVFAHSJwHKYC2kEGxwyGQ+B4IhAmwHLexWzc0ycH52WrPXrvL//i6oNisXb95lvn3owTXUxPOje7o+LecGsxb6pw8s53nsPmdoaLT92GsQKgIHL/4Bwubt0enTt307KowmSu0EQSlavRChlQaoQJQ105KLI8B28CijmUVkMzwBmAMUAy" +
            "gDnnf++0BKyGIN0JB+EsjLJwkOBSYNq0IXiD2WkDGWssjEO0NkZ/Yyo3nj1CAPvU9ktff8o161863d8aH+9t1/PTrb17N9fvMCQ3J2O3OHfl6N+f7G3sF3l8L06qJeP0DMzpWdl6Z5y33NGco7EWizBB5lLogB68guAGXDgUeY5ASmijIZWCNBaCAdY6OG2hSIMOq+AhAzCr4GwF6QwYSY0CltWw" +
            "isHJAJZM1SpoyWG4wbTSsBEJv0RaA4ksEQQnEJduDi48kaDUg81fZ+rle7efcrpZm8/HD+tb7z3/vSqrXv/ha9E/nU7Fodxe30OZJ8ezPArIrGbO4d25w1QztAOJRAGjTg+Ba9AZAIsmA9lMyDm0MH7nmdeahqWfrUFD5ig5nKXfCzRupSHrnPc1UqeVFoYJCMfAvN7IXAGjgZKsuGQw0iHnDMZV" +
            "aGQFoSbIWMgvPvEAc73evyav4omXT37r+putL/7L//Xzfz4bJ4cyjZdoaql0HUz7Lbt5uxjhgwXw9r0FsHSQMTAaklACO60WRt11cJdhPUpQVXP0Q4WmLiEjB24NXFnB8QJgFYkFy5g3x1pTLbOyAA5BLu6zvLEOzKw2JjZuZdKW/JCj0Qaa7JjDJ9nGAg2r0IgKuslxd1FBocFkfFV1e3NlSgZ5" +
            "694VtFvzaZHHPd4osHYPgWQ4vjHD/f0GSwt0jscIpMEoSbHdVVgLBTbbNdqxwCiVaMcpQsnRDRW4oFJngdgW4JoeqgYjk+SAYSv/oihpa+EjjGCSAhhIT2AGwloIw73mAANHPkmqkxSTJRoj0egGZW6wmGQwyyXGD4MFTMW7/SWXQVBDCj3NT0ZvZfPOJ4uuQqMrJLFAGTag+x5L2kVgb5zh7oIh" +
            "qhzWAoGgNhimLSTK4tzWEL20wdpQIE5SdKIYQ7LFpAK3FRLWoG4qRIaejWFR1WAQMI55f2QkMaOwb6BJAuEgOWnXwUnAUQ7UEWrdQVbkWCxmKJc5Elaj25u1223XvXenBdlNZ1Ci0roKmYwMFC/QVDMo3UBKQMUMOqZQxr3OG+3QS4GQklytMJ0VmGiLe3v3EYQKkirhYYA04djoh+i2BXaGKdpC" +
            "o9+JEVuNiHNw1fjsbyrjNUbhnkyPouaCCTDhEEnt/YtMljMBYyWaSmI+dxgvKaI1iNMxVHdRL2adcHw8sHI676PKw9PlpNMz2qDPTnCeN/jArqrdhiIFLBx3PgeRjXe6wLMXE1zp96BnDlnWIJ/n2D8yKJYG070F9hnHe5h5X+l0BToRx9pAYnvI0AsttkcBUqEwSh2ayiHiFnFTYso4FipCyDgs" +
            "y8D00uc3QT5oLYp8ieV86c3YCoamkSiquNJMj4Oo5FIbibKJ6qxIbjlWXaG0mWhG/gtOZk3pxIOTlWOSNcQSWG8znGsLjNoKFQRyHcCWFtWpQZNb3J0zjKsKe0c1cmNw836FWw8cosBBCo7RSKCTBDi3HaPfDtGPJIYqQB1zHyENWggQ+bTA7dJbRzEpsJhQimBQFPINRxoBpyYWaXfebw8CKwfp" +
            "BOut4552/KB00BqQFH+oSnAC8HWfII8kDVlAO8QAVFVAmRk4l2iZBryyWA85kj6gtls4rjkmro3ccTwYl8hqi5PJHEcLg+Nxg3Fj8M7NHGy/ghQCa6MIo0QgWbPod4z3vzYHhlGEfkj3LbEsG+QZWYkEDyhoSugqggoNy5et2+P985CVCXFwtHMwXnTr6bItOjiFYwIlt2hC58WDCFcZUxc+/E4X" +
            "QDbTYL0coZJAUaJlHJCRsBxNmaEbRQgsQ9AdYCu0sDL2Znt3Acwai4PS4OYJ8PqtU9ze0/hgWSEJJfh9jX4K9Ooa5zvAJy8HaO8KxEpCSQ3BOOiP32KKmOSHTqfGJL4el3mdeHM6nQ6PVXvGGCQEFLgtV4UbSwAWAtLCUVFH9sy9vsCg/A0EJUz6EkAGi8JSGirgZTxaIAxiX9cFicITsUTZFtiC" +
            "Qm+gkCHGaVMgtxY5YR4J7E2AeV5Bz4Dttsal7QRJIBGGAhl3cM3Krzk3sCBI78ac4Dr3lmURpTm40o02cRPCqZADCWMI8xBLKwFSOdO0Jd6myRwjKl9c5JOfsxJWN77arQLmK3GwANZxcFPDUC4yBqyuoCJydot2HGEAhaG0aAnKUwK6y2AYQ4uSqXMIWg6Fc6gJvjAOJUM4ul6jEcbOv1Y1OeJO" +
            "2XOCwwWG6mSGZZWgFjzLZiMpKJkJ7Ws3ySj/1IDOAbf0mb+sGZqaU3UBVxs0NWAs1WUCdUDVmkPOLHKmUTCgUoHP9j6XUHI1FjVl/FojUhy80RCVQ2ytr/noeUoIsICjodQUOdSW6kABxkLvN8xHXIaqIY+vIRMoLl1XawVpuUWYzPmDg93XmlwS+E+toDc6SFWCce5tn0KmB1CU/Oj62sI1JQKT" +
            "INQrk9SyBFmDJgAIA0Zma1efZXaFlilSUuRpggSVo68KjWlQBatqgqKQ1gyNW5VMdoW04Ot28hnuwNWZ71ABQVoqwgMuqma0tc+4klQPMceM6+jG2YRVUJ6UAErjVh8jYdwKEbLAoeEWS6bRCA3BHZRhkA0QGiCVAWLaxdpCEdvjLAILhBQZLaAMEDOGgEmUlULZSBjSJOmfbsrowalMsqiM3zef" +
            "VAWis+LWeqFoe6UiiQM0ph465GJ20nGckJyuAxcFOsjzXrCgupdxBFQlrMopeNxtpEd7dCECelUIFLFDFmkUqUQRCETpAE6miKMWummKSIYIwBAYILIrgegqATmioesH6CYJIrJ/xmAQeD8lRyAOjm5dauZBpIH0QtKXw2p/KU8qwRElD5kM8udnpz1/fbTaGRbzzbems0vvV3bvBc0naKh4I40Q" +
            "mWcC7/jM1RC6BkVqGQFGCVTCAaFAZdooHLC0DZyx6MYCRkv02gwy0+CV9gGFnW0Qk2eh2AkoMmtCZ1wABdV+FoHACmu5lTZoBwwzK4EsQCmRzN9qII6nwlbpv9++cAg56OcQkhMbaavmQin4m2iwxNKUvvTnPILVLR8qhWmQKqAXAb0E6AvmocBho3GwCHFyeILDzMBag1GbYdBTeHbIsUEfEkBi" +
            "OZw2Hi4HKgB3BWKz8AGIfo9iVZkTyjVnKDgUzPsd6cuxGkI5UCwgL/CcnHLQRdKsbSx2FTuAFCqE9bvfTFXELlU6QRCGcGyJykfGCMy1CfB77LHeAp4eMZyPOBJnwRvg4djgtTsz3HpQ4DQHFoY4A2Czb3BwgeOFNYGdVGI9kt4pKDk3TKPWGawrEAQGzuN74X2C2BG6F8F5ym+acBE0eGDB1YrQ" +
            "YJb5aj0MJzDZDmCTvCy6kMRpqaAhnFIdPNx640qV/ppJFXgE1NahsQZcWQ/gusrh/IjjWl/gihLYLDROSo29rIvrh0e4PwVI9pILLI3D4QSY1AbjhcVzmwLPbUi0lPI0V+0yj5UoSrjwLPyRmngMYxvafnCirrzZkxeVHkuRZfp8aCW4C6BsiUi5oCr6bHZ0FbI/zH3JXyy7rixaS2EjZ2rBUiER" +
            "UN5pMo/9W32DqzsJXt6R2E4ajJxDlDs0Jce4lJgvDWoiT1ICaxIyCD0suD9dQh4679b9pMG5XnAWFEr0CTS2gEQx8KmDDTlkIGFrg6yyaCQhXXhAR6FBCLdSInc+PShhYXWAOt8umd0cc1lBNtUIi2mL7HERI8x7zchshans1wFadYOZ1ojSGUYDgUvnU1zuReg7DZ7VkNqgVgZWTwGuoRIGI0IY" +
            "TbGNSqoGTuXYLyySE4fdnkYvdlhPJRJO/F2FnUiiZ2OkxQKFaSBDhyawqCXH1BkqD1HzFd/HqZZihJkcjLBQYYGGriPkTEVzvblbgNd1z0OCIguMbC4fnnfn5cvBNTxfb+JqLrAxAy5ah91QYxRWCHzGKCCMhlMcdUti1J5jY+gQBCsuzkVEgsCTkDaKUUmBZQ0czHwQA7cSqWFoGWAj6OD5bg9P" +
            "KYl4qeFysogGLnFoFDzhEpAfow3pEria+4BAmqpigyZ1MGqxEaR7TxsdQp7ul5gcOQy2AnVyXS3E+5E7t9tjv5yvoSmPcN5mqJYWW47jCS2wjZASMQTtmrKIEuDpocPJKTDLDLK8Aj0po3aIFGCeL+AocoOydsgrB1dTXrJQjsEFAS6tRTiYKTyoNA6y8sydnC8yqkagrmKE6CDmmU/M3JcIxhey" +
            "HG0Edb8gkmXQ3WPy4c3K108IjFm89U5665+/nl0ePmh13AyfWGp0AoeAcwR3BJ7bPI8uiyFabXB+gpwV2GEWaRyg2DCo8xr6yOLuYQ0MGqrLwSPhYyyXVGVY7+gEIinXWO1QoMBwEGD3ksU28Rdj59kdigZUYlF9BpMgsBESs/Q1X7xKTLCOIW1SbKe9OKzT8c0Phk7KgGozjjjbv9rfe/v55uDb" +
            "0UF55JPepa5G3AbmHYd0x6F1v8To2T4wcog2UuQYo0ekJBx2zrdwtZXh23en+MM7pziZWh8BkWoMI2C7D1zYDHyi5Vz7BEkEiGFzJInB9pbFVclxz1ocF4Br4Kt2LiS0W9FfDjWIzG25FX9na4eNOkB8iCY2drMs5KpSoNJ9k88uXMzM0+28L0e2gLRLJCccA2ExjoDibaD57jEONidQ2xzyvEN8" +
            "KYHYcRj0ImztruHScBeffMLi0+dm+MHtO7hXWDws9nF+kGC41uD8IMZ6xKGKAo6oKLGqph2W6MccT65JHFUWr98ymCydd/7cWSxdjZwgjWjApPMMLddAxyhsLRhS+boU+nOzzYEkOtoiZJlITLG5213rpPZVyOoGpL2Lopl44mKjJMIkRn6gUF+3KPQY5QYwHU0gt2KUsUP38hhyu4fRU5v40oUU" +
            "v9p9DrNLCvfUGKGsEbbowRdQJkdFbL1TnuuurfZguMUsLscxJl2NBy5DngMqAWyjURFhKSiaMZ9CiAkKA2CkW1i/F2J60rDNltm4H0rIHpuACeO6SdXfuTLo9sQr6ExjZIsW3PIe5tUCyXKB0DJ0HZVBFhUcssMc2aGDfbdAL1ZY/PED8O4BJpu3EVxNwM8N0LvWx7mNBrtPbSOPKrS2+qhOj2A6" +
            "KRBpzNQULV4gJ3qMR8hsih1U6GYZujmw1EBETS9bwbgIHAE4cRoaaDcK5+stmG9LfOvtsVl/RgX9sIEMVO1JqlY72O08sa3ba1CYFAgLgaDsAIsZHDV3juawlYFYFODGAylwmyG0Ep1cgQp3U1rgtMH0zWPU7Qn21xmqloQ5/xDlOkN6KUFKrM6mws7VNlLTYO2pIao5ULU6/itkE9wMKuhqjtuE" +
            "uTRDRNV9WYPVEn1Cc6lGJ0vAf2Dxg3/9AFwZPl8ze+fbDXUfgGUlE+FEywquKNzaDiMyG7psECRkWwVY7sAXGjiZQhQlwnqBKjuFzgvkWQ1dVuiZAMtmii3EqGYF5BxYuAb2XW800BsSDwMOM4pwkjhs9wWiKwlUv4V0g4NfbHmHL5Z9tE2OC7JBPx7gmuhjMx6ho0v0OutYZkdo9hocfO0Qkw8m" +
            "GL74ZL1wQS+joDAvHQlUVmW5p3WQI88S1BWcKIHAoagLqE7sIxPb2UKTj4hCQZJXCMocpixRn45RzcaoCof69ACLXEPqGMtshh4EdFmjT8XLXQ3qxdW3aiwVMC6BeGOBsjpC98IMbP0horbC82KJQQDsrwFJGmAnTLHZ2kK73YMZHyA/WOLt33+A0XcaJCIAZ9vm3sO19x4uUiaJ/S+pJGPOsbCJ" +
            "qT9jKw1XN1CCwFgAkxvPBJWzBfJWDGUZWmkA2ep4/jnc3EFSFd6Bw9OJ70CUZQU5nWE+y+AWE/AiQ1BWiI1FahxCW6KisH1oECOAmeZgwQIzViNOLTYih40B+egYnXMV+k8ZRIM1LIk5fWeG/JsOcbYiKEU7SQKIq3cnw39D9AUGQd6rFds9soydEwJcSQgtwYmtoXajCSi+gqsQrCSobYGcNEjk" +
            "CJX0CjwNIEQA1R54GB3oGryoUJc16tkMqsyRjWdgywKL8dgn19pkcEuLuBaIohZM+RAt5TxpGWeAPQXqmxXCToX6TydobOS57yBv8Oy4hw02RNxTqEWoTY1poipqw7RRmmRa6vF4bjErnOtSJo4M9zUX9adA/UsuwZlCxBSUzSCoAGyMD7moG1/ieKAihefnBA989Rx1BwhGI/CmAUEZtswhsgL5" +
            "MoedL9AsczRFhaaqIBcCtj5AP64hJwWM5UiJk6Du+iGl1cI3xc55an+EbrxD+4xMh7KemnJyQiSQkdB6iazE640zSStqg08lGPEIJAiVGfSEEF5rTBLFVaxwjPUtYnhGkSCmxaorRUSH4ES9wJoaTAnPC4ggBBtESHoNImKPSg1uNcqiRF03cMsC5fQEs2wOtMfg8ymiOkdRZL5jKKmhBo0+E54+" +
            "li0LtCSWYg19Y4baOsimoeqZO6dNqUuR1TnvdZCAmZnnkz1bTzBRr8zQsRJGlmDMeMqYExgTZ+0WomnpO9VuxEUQMU5dAtIk43B15ZkX5grwkEMpByYlWu0uTQv4n8tyF85o1NMpdEYxagG3mKMZT2GKHHp5TO13z+khnUEzC8kvoRXw5rkuwQ9hwF0NpaR0NYq4dj2vHWpfPJo78dwvMRa+UeNJ" +
            "Rf8rwvWMqgjrWRvnQTH3XB73713NBTDSJDGNlCOI4SDbqwo/v+MqElL4l8kiIqWAVoiwtQE0DSqmgCyDaQxMtoQ4PYC7PYfLDFDX/jqynKDD677lIWSkGixdwDIRFxVveEX52J0xMETzCgdG2yeUZ049DLaRl4/AlhHGN4dXVIzz3WwSzRLmd3qF/T1l2ngt+p/to44GByMKS+sVjVXWEKIG15Uv" +
            "SqlYijmZVeA3p9noAb0OWHGC5vo98IoQ7gDBPNZPBuMLC3lbyiPXgbXKFW62MFwug1a6AR3RGBRctaJwPWFM5kfIhkg5k3gY7F/3pKSBZRqO21VzDGf9JBhPQ3Hf6288JcZtsPJJz+rwVZtGEgNE7yOTtrBm9X4isJhpwIiuoksS/UW+JzQaVyGkTWliKK1EacX7zjInaQJk3Zzw0Jk0PjrV4Sbt" +
            "WO5bghQA0Il8n4FoLOYU3Jx5VoYTy0eBgK80Q4nXD3NQGUxsDQl4NmXyKK6Qq3lOzkg4Z86o2NVfpFUfwsBR6xqS85WFe01S85jaoRpKM5S8ghENqLpzbglm+ugl7OKNfGTkZ6p3EaJm5VStTV+701kc3kQcHEGOaug+OX8MHqdwDT1RBNbqASXZfQBUZzMzlJca7q10xdw4WC5X/sVon/VKMD+T" +
            "QOGfryShz7oztIaziSo/08A9aiWky87aNj4fku5dBWNK4j7BWQDmUjge4GqPm0tFzmSIBnNCFlUdq3qaPHznFi6rBbQ4Qt5tIM71IeMYNIzDeQssHUH0OgCLwaJwJYCgZFAAQvicRA5GeYdRo8xRwG58C4XihKEGjpeZecHYo0mqR9w5iUztRjJpR1YhfLONggwxRw1BcwWfCgjxCuqpq4SdVMpd" +
            "6lonb7MLWPIu6yF/cxp9f3G31h3e7LF+M0FVztGaTPwHKd7TTtkw8onS9bpQnR7Q7oG1OhBhBCcUWCtaaYwnngUl4VhT+ofXJkMTAaw2PlF78pqqdm++btUlJKGI7PdB1p1FVyIWrc8MNNxrAqKQKVeKVSLXHDyfCY0eZCM6aDPtSpd+/4fi0v9x6ZXx3yunb7PD8RQxTV3kS4RIoOfHvrdKS4/3" +
            "oKlL3erAhCnCQR+60/btcdEZQKQpkLTBSGNEgseJj5i+emCVj5z2bBzNj4Xx1ZAQyU+b8Gj6jbTouXDrpTwLOMwHF1c5P0tEaq+yDHvZcZaVc8iU2ifCwEhTjNeu/OkTf6v6nYuqShZv/TnkYQV5VyAv2giOLLJ8TrgMvKlWN50WRNCCH94HiwNo6vp1e1hGCZLhGnSSQHX7EL01sDgEUwZBpwPb" +
            "5EASwy4XEFEMZ1YmyVXgQ/cj31mZJvc+6X0pCqGKxk96MepUkKDa4Kg8dFPjejx9ltzDQCRANLRs3m3frV9NDqJrw8vy7iXMbuwhf48iyDZOfjDGBaNxfH8P8WEGkxVQmUGeEVdtURYUdeDnCChh2ts3vUAmieHSNmSrC9kKgdEIxKe7Xh88ioEyg2ilQF16sxWWe7hNiZ3EoPzEzJmDGYeAZlFZ" +
            "4qe5KLHOyjFuLKbmNBXLBX4YyPSSQjRy6Gyduv2p0mX7X7b300Oc7o4x7xRoehPsXt3F/asOx80G+guF+s4SvdyiPlhi7UBhMp6gOS1g5hbKOizGFdYEkOcLbFRLzE6OwGWImiu4VhsqCqHbLfAwAhsOwVoxRNKCzpeQMgRvt8AitapWqHdDIZ76mFRK1avCt3IGk+oY9/MFTkIp51sJO2xfa+Sl" +
            "z9zAaOMGGtbg9T/67Qc2uKnm47vIuhs4SQRUD7jt/gTzl4gzjXGvYhi+zOGKBsO6g9GyDflwA+rQQR6XmGYl6gcL3JxmiIkrOJxDjDnEsvK5Ro8PfIFEgzGRjKCJjExjlEkMtLvQgUK8tgYXxwj7AyCKoZJoJZDqALEC+AmW9SneX97EHirMhxt2+Klu/+Hb4PLSU9/w7ZRA1tjeOhSH+xdutfv3" +
            "P/GD0wpLJzwBssU1eu3CR6k0ZlgMLUUT3AxPcFJ1oF/h6E3PY3EaoKz62F8IdGd96Mrg8tEO9A8neOWewuGdU0SZglmUqG2NSFOrpEFcLoAx5Z4D8ECgCm7AhRHyNIUY9CG6PaSqBRkPwNI2yuURTuqHOJBLZNsc3RdiPvps97Z6adPKqkxR1BGSKMM/+Dv/VCQoJsne79YAAA4gSURBVKUY4D0d" +
            "4s7NA1xtG+xcCn3+42fJT5lV8oxriULMfWvj4dqbEN0UJ3XoQ/QdqrNYjKMxx4VXB3h73+DJwx1MD0+QnND42h7Coo3FvTFELqBnNSJe+pAe1gHs8hRunKC8dxdNK0HGu4iSLlwnwv3pIW5kJ8g2gN7nWtj60pWpuzR6f30eO5nGOX7hqTfwzPkb6PQO7k6qtb270QDGMLfkASSvWCKc1/iHy31Y" +
            "s3hh/JEATc6cYycufPhdD7hHl1USYG6Bu1cV9ps9bKoNVEcVYnsee3cbsH2F+l6D1nULuVd4/NScVEgLgeowR1coLJdThLCo5wvMDwweYoppD0heFlj/xQTps7Kz2NCviR84yN/90u8hDkuUVYwyT8N2qneqQCOuDKLQIE2YL+N+4tTER5YPrATosEIJNC8UMOfnRmmuWosSLGTY4zchL0aY6COo" +
            "JxKYqUHaiyHuOYxmEXDEkR5HyI4N9JHG4rsamEuEbzPYUYE5VeILYPQ0x+izIdiLCtNBPtbs8PJi0zyQNO2xLJOVKSG4YrnayHmFsF5iljdOtwUjLFWbnyXSYwp0j77/aB+k8eUnQipMWePziq6nYIQ4mwzuqsQyA9TLDO2lAKsVbMHQDRPM386QHFpkH5T+pIrOFdrrEumTEaZDgTv5RBpdfrPp" +
            "3qDxCuq7EgYSxBfLOWtYjxVo1zmj4fOHy1UN+R+6Hr/Eo/qNpg9WF2dwU+2nFuvcYRI5LEML1bKoeQn3FRqaqH2ZU49D2NMUY7NElSmc5A3GerFM5OQiA27IFg0uMKJlM0QZsoLbNECNFqNJemAYcz/88PMs9hEB/iqLdv6Rb9LYEI3UZAJoXI5gWkGnDNIKmHWFQjaYPaxxqjQyo8BDc1CzW/es" +
            "m0MeqimWvPaweMNFtgprKWlo3K0sZmkMqN9aWbdC5f83S9KYjbBY1ApJYFA1fIW63c8noB+zkDFqFaGwLVTVqR/DofBTjysslhaLQkBTwUqzE9wZJ7KIOVFLEsaXfQ6Yj5qBEIwrSEytsxV1yQLGbheWxQKI+YpnNh95QD93LR3ujlvYX6RIVINAWlzqL/2IZ1fRmAvDo6L6ZwpE3fcic+M6dJKF" +
            "6PEe12KJOisxn9auziSryhBCBavWf1ncYWZcg4R+dFCF2nyFNEPVhENhIqTI7cIZ9q0l+BuNwVABn2pxvNzmPxHytOX4y+tr+FfvnEfeBJhrYHPrGJu9BM+sz9ENNJ5pNUiFw0cPmXn53Gr69/HXoqZk+3t7JrORe3Kb63YScPDa6cbyqpRM0UijTxmaO0dzAjmxon7ieLUocTbYkELMKydHhWF2" +
            "6Zj8oDCsyp2Hz28sHf6RAK7GHI9MXjCH9ydtfO2Hl/DuPIKJM/DBBIe2xjsZ8Ed3U5yPNa6lGl8alnil06xg0IeADmfHzh5THWNoE8WVWX7jeK7bkcKF9ciocDVgCUu0knPO1jTWT7TudSrRnaO54TPCxtZ0RsdNlELkRNOMjbEHHJx6QXQ/wmy3c4f/c+pwNcaHvjGtJP7VB1vu/Uwxd/4eWGvp" +
            "H44L62cJSJ/3coETzf3xg0uJRkd81O5+/N+PxBt2wZoTiNw6V2rnWpxqEMUEY8x6ToKz1b46sjy9srRqCDu7Bp0FdKRBOmNrwavqxBpbPB6vz2iAO6VDZj/cYffN25v47p0NprszoLVcESs0WeRWqMZ/lM4wGOD7C4W3lwoMP37S7KN5+xEjSN2bqAXwELzxSDDg3IXMUrhizp+TOnvrex8+pm6+" +
            "CGt2wcwATofXam0HhdN6aVZJ/6N3ItOrqPDljpq69sEyrRbLCCKsz46mnJGSH7MkdzisfvKopl2Nm//Ya3zFabLMginpmBCCOyZpEp8RZD/bLE8+P/6cnNoJLlDgbpQye042Nr3TOLWYr6Ylf2LRUCDdTDvmL97rZSZJa2fzCEyvxqHBPj6U0XYOlf2xKEk3sZ4j//HPUO6j+XDCq5W2jOZYnZXM" +
            "+HxC5ubOmv/eJW/8SCDPFwVw7HIGtjsTYr2e6VZ9Un2MNAw4MQ4nZ8VdO9D8Un8WbPRnzi26EJOhZ0fdxwrEcC01uJyYj9jbGS/+keXnxv2wIDzRz0TInJHMrf6ceTbMmVD6MYE4dHSKanAYNym/UiPcWdSpXlbiJyVaMUs4PhOosRxPtnP51WfvswtpjXDaAzteh1umngp2vvvAvBY+3a3xO1u5" +
            "n+F+/MLGCQhq15/9mwaCxVkQohHN0jAERBfTRCOnBoFZ3Zxo15UP7QM4fnQ9WQzegVUZuFNPNI5/mWnZymemB6fkY4J/uOhEZPCIdQIwCDV+dfeUnUtLvPdwhENrca+SOLUhFqLBy8MCn+zWeKXd+AT7uDBkZuKMjHwUSY9q58ecaTx06YcvPKHsSUsKZAbUFVee+jozBArkyw8FcrIiOohZZp+L" +
            "Jd/OCuNqw0VdC/UTPkQThny1g/yRR1JTSlo8O8zwic0ZjpYxYtXgvUmKzbRCS1ofDOKz2Z3HjdF3K9jKp97LHb41szhtHNYDhhfbHPN6lf9IQ3Rak2bHuaSoQMK4sy3Fg8eDglxJ6QLu5KW8Ud13T0Pz+iQKi7JmPM2I//6RvzpgLQC2gtUI8qPXS7OKObWWGMa1H2n57MYcSy38DUgvP63koU3Z" +
            "rxy+dmTwxtJipoGuBN7JLEYV82Rs20q0Z5LOtDq5mliifhJjKzHIjE5/5EOrVXGwoDE2e+2gJb55cxAVR1tC1cmPK4gxbEmJkP34Az7aHj+TbVfykzD+bo79VPjBzioG0s53F84LQz5E328WwNsZmZ/AjomQVByiVIQUfUvmkTQAHhJg/lAg72GMxZbZk2mJ5vuHCR4uVBDnA4jxNmNV7FsH9PlE" +
            "R7gWBOgEHxvRf+7lzjbhTml9EfsoDInVaVNoLvB0kmKLmgSrc3xOUzvHEuX64RNkZ8dqVyZ3trMVzcl3QqN+YWtO0Nm+edSGa86zgHqwydS/ue9SbG1PXcArZsz/M9zz+KIHL3zUXLVmH1XKPh4zh6YJ3cUoQY9LlllHR3aYKYXzAPFHO7r/+DXl2Xc6LHWuFdj0b1w7cdcGedZYxm6dJml+uIso" +
            "WmOV5jhyEje6D9h2rHGxv/S75n3nLDHan1IhfNzyzXPDcVgKHJbWY6nHlzESbdtmikkXCjJD5upaMNP8xBZOPk4gQttvMeBWJO2lV7fnNhA2ePekZe7OY3FvrlByicM8wOv3N7CsFK6tTzFKK3SjBv2kQqpWxwZo+dMpP1MihmkZ4t1JjIOy+TBDPlIRtwLL003XnJ+zQjd+Ql8ZOsZimPvxmuzo" +
            "YzUE4OsO+O+NwxcYY7/44np24clB6SPYuFDkV3ZSBmxRcbdsYnz7VoufFApJWOF8L8NT61O8sD22AbesHTUotWCc/XSt+bM+NNXLtK8NxUeKBWYChDZih8sGT/dr/1JEg+981Th8bJ38NIHoF/8LgO9oy77HGH4lkebpNDDtUdR0nx44py1TjeMoao77ixAfTGL31lHKv3lrk72xN8TXb27yF7am" +
            "5lxvyZ/ZmLlYaaa4QW1WXXP3mLXQz52g8YPnhKmo0uKPW1ORwhZ0jmbh9UFoO1SMxQFHXtpHvkshe/FxAuGsLqJo8QMA7zuHP7VgT8HiWQY8pS17VgnXU84maWJEL9LhK9sLfmsrcm8dt8Wf3+/i/rjrfrg/ELvdHDv9BX7tyX2c7y2QKkf/LYhv8RjHzvAL85q/PqGzqUsqJx+TliakQ8xL5Qrj" +
            "D5AwJQi++R9h8WFApE72wU8T6PFFb3zTURXr8AcOuArgcmXYLzDgXGPZOQ58oqnE4FKvQi8y7sWNhXvzqGVuT0N7d5bI42nP/vPvdPiTazOc62ZYSws3apfoR43tBLZcaoY7k/6yriMm+MnQuB+5EMlrirbnL04ytZoCcIwwamlWZ25C30RaKcD8VQT6UPFn398GcJ0B/xbAJRLKAX8fwK8vG45Y" +
            "mn47NM1GOglOc4HSCndvGvHjXJnr05R/47jDFrViUdBQKSSe355hGNrTaZWcbgUFWzeJuO+qgZ/ZcMzxomPtcuB6oeWBMJySNZ3qIu7El5HOUweEYuY/LWz/VVZ99h5Chx9gdbFvMGBoHNswGk8G3K33Y/N8IJqHO63qXKk5e7WWuD2N6u8+7MiHi5Aby83bD4fRouYbHeXaJY+Lza6uTnTcFCKX" +
            "0irXHJ1nPUTuqVFmXlgveCys046VhMBDxbO8pnMz6J75j3r82dm/vb37c8j0E6tzJmiLiBoHXOHAExYYBMI9Yyw+myibFlrslJrNj3PVmZXUwkZ9nCl+axpL8qPDkhs6d7BXWsWq2NF/5NCPG/2fvHTgXlpf0mE201i2xxmGy9LMTxeNKGtL5c63APzGWVD7uTX0cWt+9lqJVW544FZak7VhOwy4" +
            "sqjFLyjunlEcL1wbFmHRVFc5c9w4Ll9t5j4wnJZKjHMlFpXEUWEZZxk2W5V8dpQZCu/a8NIBpXOYxIHotBMclU39Jpz75uPC/L+hoZ+1HkHkiwA2LfCi4u5559jLnLmudZhK7lqN4WtS2KDSQuYNbzWW1W2lk0Q51zhGjA6huiUcplT3LGt87WhSfV9r8xqAtx5/hv9QDf2s9SgC3QFwnwPfMpbt" +
            "EHq3jl0mDTc0UMHcOWNYN5ImkdxtC+b6DO5y4/iVs03ZYXATMPxQcHadwf4L69x9B3byUS7i/2uBHl+PhNs7e8j3fDT2p9M8FohqwwrOMNKEsx27BmCNouqZj9Ln9q11bzPnDh/nEf7/Eujx9Ui4R99pm32KcA4nZ+whOTzN3tCD988qmQ+h18deFcD/Bc1bkxgTs5gPAAAAAElFTkSuQmCC";

        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final RectF rect = new RectF();
        private final ValueAnimator animator;
        private final Bitmap moneyBitmap;
        private float progress;

        MoneyProgressView(Context context) {
            super(context);
            moneyBitmap = decodeMoneyBitmap();
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
            float barInset = dp(24);
            float barLeft = barInset;
            float barRight = Math.max(barLeft + dp(96), width - barInset);
            float barTop = dp(64);
            float barBottom = dp(94);
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

            drawMoneyPack(canvas, moneyX, barTop);

            paint.setColor(Color.WHITE);
            paint.setTextAlign(Paint.Align.CENTER);
            paint.setFakeBoldText(true);
            paint.setTextSize(dp(25));
            canvas.drawText("Loading " + Math.round(progress * 100f) + "%", width * 0.5f, dp(150), paint);
            paint.setFakeBoldText(false);
        }

        private void drawMoneyPack(Canvas canvas, float centerX, float barTop) {
            float targetHeight = dp(116);
            float targetWidth = moneyBitmap != null && moneyBitmap.getHeight() > 0
                ? targetHeight * moneyBitmap.getWidth() / moneyBitmap.getHeight()
                : dp(64);
            float minX = targetWidth * 0.5f + dp(22);
            float maxX = Math.max(minX, getWidth() - targetWidth * 0.5f - dp(22));
            centerX = Math.max(minX, Math.min(maxX, centerX));

            if (moneyBitmap != null) {
                float centerY = barTop + dp(16);
                rect.set(
                    centerX - targetWidth * 0.5f,
                    centerY - targetHeight * 0.5f,
                    centerX + targetWidth * 0.5f,
                    centerY + targetHeight * 0.5f);
                canvas.drawBitmap(moneyBitmap, null, rect, paint);
                return;
            }

            canvas.save();
            canvas.translate(centerX, barTop + dp(16));
            canvas.scale(1.55f, 1.55f);
            drawBill(canvas, -dp(28), -dp(31), dp(56), dp(24), Color.rgb(96, 238, 105), -6f);
            drawBill(canvas, -dp(30), -dp(15), dp(60), dp(25), Color.rgb(78, 224, 93), 3f);
            drawBill(canvas, -dp(27), dp(1), dp(54), dp(24), Color.rgb(88, 236, 96), -4f);
            drawBand(canvas, -dp(12), -dp(19), dp(24), dp(34));
            canvas.restore();
        }

        private static Bitmap decodeMoneyBitmap() {
            try {
                byte[] bytes = Base64.decode(MoneyImageBase64, Base64.DEFAULT);
                return BitmapFactory.decodeByteArray(bytes, 0, bytes.length);
            } catch (Throwable ignored) {
                return null;
            }
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
