package com.zeywinads.unity;

import android.app.Activity;
import android.app.Application;
import android.content.ContentProvider;
import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;

/**
 * Installed into the host app manifest by the SDK configurator. ContentProvider
 * startup happens before UnityPlayerActivity, giving the SDK a native blue
 * loading surface as soon as the launcher opens the app.
 */
public final class ZeyWinAdsStartupProvider extends ContentProvider {
    @Override
    public boolean onCreate() {
        Context context = getContext();
        Context appContext = context == null ? null : context.getApplicationContext();
        if (!(appContext instanceof Application)) {
            return true;
        }

        ((Application) appContext).registerActivityLifecycleCallbacks(new Application.ActivityLifecycleCallbacks() {
            @Override
            public void onActivityCreated(Activity activity, Bundle savedInstanceState) {
                ZeyWinAdsStartupOverlay.installFor(activity);
            }

            @Override
            public void onActivityStarted(Activity activity) {
                ZeyWinAdsStartupOverlay.installFor(activity);
            }

            @Override
            public void onActivityResumed(Activity activity) {
                ZeyWinAdsStartupOverlay.installFor(activity);
            }

            @Override
            public void onActivityPaused(Activity activity) {
            }

            @Override
            public void onActivityStopped(Activity activity) {
            }

            @Override
            public void onActivitySaveInstanceState(Activity activity, Bundle outState) {
            }

            @Override
            public void onActivityDestroyed(Activity activity) {
            }
        });

        return true;
    }

    @Override
    public Cursor query(Uri uri, String[] projection, String selection, String[] selectionArgs, String sortOrder) {
        return null;
    }

    @Override
    public String getType(Uri uri) {
        return null;
    }

    @Override
    public Uri insert(Uri uri, ContentValues values) {
        return null;
    }

    @Override
    public int delete(Uri uri, String selection, String[] selectionArgs) {
        return 0;
    }

    @Override
    public int update(Uri uri, ContentValues values, String selection, String[] selectionArgs) {
        return 0;
    }
}
