package com.zeywinads.unity;

import android.app.Activity;
import android.app.Fragment;
import android.app.FragmentManager;
import android.content.Intent;
import android.net.Uri;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;

/**
 * Headless fragment used to receive the file-picker Activity result.
 * UnityPlayerActivity is not subclassed by this SDK, so onActivityResult
 * cannot be intercepted directly on the Activity; a plain android.app.Fragment
 * attached to it receives onActivityResult automatically instead.
 */
public class ZeyWinAdsFileChooserFragment extends Fragment {

    private static final String TAG = "ZeyWinAdsFileChooserFragment";
    private static final int REQUEST_CODE = 9718;

    private ValueCallback<Uri[]> filePathCallback;

    public static ZeyWinAdsFileChooserFragment attach(Activity activity) {
        if (activity == null) {
            return null;
        }

        FragmentManager fm = activity.getFragmentManager();
        ZeyWinAdsFileChooserFragment existing = (ZeyWinAdsFileChooserFragment) fm.findFragmentByTag(TAG);
        if (existing != null) {
            return existing;
        }

        ZeyWinAdsFileChooserFragment fragment = new ZeyWinAdsFileChooserFragment();
        try {
            fm.beginTransaction().add(fragment, TAG).commitAllowingStateLoss();
            fm.executePendingTransactions();
        } catch (Exception e) {
            return null;
        }
        return fragment;
    }

    public void showChooser(WebChromeClient.FileChooserParams params, ValueCallback<Uri[]> callback) {
        if (this.filePathCallback != null) {
            // A previous chooser never resolved (e.g. the user backgrounded the app);
            // cancel it so its caller does not hang forever.
            this.filePathCallback.onReceiveValue(null);
        }
        this.filePathCallback = callback;

        Intent intent;
        try {
            intent = params.createIntent();
        } catch (Exception e) {
            intent = new Intent(Intent.ACTION_GET_CONTENT);
            intent.addCategory(Intent.CATEGORY_OPENABLE);
            intent.setType("*/*");
        }

        if (params.getMode() == WebChromeClient.FileChooserParams.MODE_OPEN_MULTIPLE) {
            intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
        }

        try {
            startActivityForResult(Intent.createChooser(intent, params.getTitle()), REQUEST_CODE);
        } catch (Exception e) {
            this.filePathCallback = null;
            callback.onReceiveValue(null);
        }
    }

    @Override
    public void onActivityResult(int requestCode, int resultCode, Intent data) {
        if (requestCode != REQUEST_CODE) {
            super.onActivityResult(requestCode, resultCode, data);
            return;
        }

        if (filePathCallback == null) {
            return;
        }

        Uri[] results = WebChromeClient.FileChooserParams.parseResult(resultCode, data);
        filePathCallback.onReceiveValue(results);
        filePathCallback = null;
    }
}
