package com.zeywinads.unity;

import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.pm.ResolveInfo;
import com.unity3d.player.UnityPlayer;
import java.io.File;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

public class ZeyWinAdsSecurityCheck {

    private static final String[] ROOT_PACKAGES = {
        "eu.chainfire.supersu",
        "com.topjohnwu.magisk",
        "com.kingroot.kinguser",
        "com.kingo.root",
        "com.koushikdutta.superuser",
        "com.noshufou.android.su"
    };

    private static final String[] ROOT_BINARIES = {
        "/system/bin/su",
        "/system/xbin/su",
        "/sbin/su",
        "/system/app/Superuser.apk",
        "/system/app/SuperSU.apk",
        "/system/framework/XposedBridge.jar"
    };

    private static final String[] SUSPICIOUS_PACKAGES = {
        // Hooking / Instrumentation
        "de.robv.android.xposed.installer",
        "de.robv.android.xposed",
        "org.meowcat.edxposed.manager",
        "org.lsposed.manager",
        "org.lsposed.lspatch",
        "io.va.exposed",
        "me.weishu.exp",
        "com.saurik.substrate",
        "re.frida.server",
        "com.dimonvideo.luckypatcher",
        "com.chelpus.lackypatch",
        "com.forpda.lp",
        "com.wind.xpatch",
        "mobi.acpm.inspeckage",
        "mobi.acpm.sslunpinning",
        "just.trust.me",

        // Network Inspectors / Proxies
        "com.xk72.charles",
        "tech.httptoolkit.android.v1",
        "com.guoshi.httpcanary",
        "com.guoshi.httpcanary.premium",
        "app.greyshirts.sslcapture",
        "jp.co.taosoftware.android.packetcapture",
        "com.egorovandreyrm.pcapremote",
        "com.emanuelef.remote_capture",
        "com.proxyman.android",
        "com.reqable.android",
        "com.minhui.networkcapture",
        "com.minhui.networkcapture.pro",
        "com.minhui.packetcapture",
        "com.telerik.fiddler",
        "org.sandroproxy.drony",
        "org.sandroproxy",

        // Memory Editors / Game Cheats
        "catch.monitor",
        "com.cih.game_cih",
        "com.killerapp.gamekiller",
        "org.sbtools.gamehack",
        "org.cheatengine.cegui",
        "com.leo.playcard",
        "org.creeplays.hack",
        "cc.madkite.freedom",
        "com.xmodgame",
        "com.cih.gamecih2",

        // Decompilers / RE Tools
        "bin.mt.plus",
        "bin.mt",
        "com.gmail.heagoo.apkeditor",
        "com.gmail.heagoo.apkeditor.pro",
        "com.gmail.heagoo.apkeditor.free",
        "com.njlabs.showjava",

        // Virtual Spaces / Cloners
        "io.virtualapp",
        "com.lbe.parallel.intl",
        "com.lbe.parallel",
        "com.ludashi.dualspace",
        "com.excelliance.multiaccounts",
        "com.polestar.super.clone",
        "com.vmos.app",
        "com.vmos.pro",
        "com.x8bit.biern",

        // Root Management
        "eu.chainfire.supersu",
        "com.topjohnwu.magisk",
        "com.kingroot.kinguser",
        "com.kingo.root",
        "com.koushikdutta.superuser",
        "com.noshufou.android.su",

        // App Inspectors
        "com.codex.appinspector",
        "com.jgba.appinspector",
        "com.ubqsoft.sec01",
        "de.szalkowski.activitylauncher",
        "io.github.muntashirakon.AppManager",

        // Termux
        "com.termux"
    };

    /**
     * Checks if any suspicious debugger/inspector apps are installed.
     * Uses both PackageManager and filesystem checks for Android 11+ compatibility.
     * Returns a comma-separated list of found package names, or empty string if clean.
     */
    public static String getDetectedPackages() {
        Context context = UnityPlayer.currentActivity.getApplicationContext();
        PackageManager pm = context.getPackageManager();
        Set<String> found = new HashSet<>();

        for (String pkg : SUSPICIOUS_PACKAGES) {
            // Method 1: PackageManager (works on Android < 11, or if queries declared)
            try {
                pm.getPackageInfo(pkg, 0);
                found.add(pkg);
                continue;
            } catch (PackageManager.NameNotFoundException e) {
                // Not found via PM
            } catch (Exception e) {
                // Ignore
            }

            // Method 2: Try to resolve launch intent (works with <queries> on Android 11+)
            try {
                Intent launchIntent = pm.getLaunchIntentForPackage(pkg);
                if (launchIntent != null) {
                    found.add(pkg);
                    continue;
                }
            } catch (Exception e) {
                // Ignore
            }

            // Method 3: Check /data/data/<pkg> directory existence (may work on older Android)
            try {
                File dataDir = new File("/data/data/" + pkg);
                if (dataDir.exists()) {
                    found.add(pkg);
                }
            } catch (Exception e) {
                // Ignore
            }
        }

        for (String path : ROOT_BINARIES) {
            try {
                if (new File(path).exists()) {
                    found.add("binary:" + path);
                }
            } catch (Exception e) {
                // Ignore
            }
        }

        StringBuilder sb = new StringBuilder();
        boolean first = true;
        for (String pkg : found) {
            if (!first) sb.append(",");
            sb.append(pkg);
            first = false;
        }
        return sb.toString();
    }

    /**
     * Quick check — returns true if device is clean (no suspicious apps).
     */
    public static boolean isDeviceClean() {
        return getDetectedPackages().isEmpty();
    }

    public static boolean isRooted() {
        return !getRootIndicators().isEmpty();
    }

    public static String getRootIndicators() {
        Context context = UnityPlayer.currentActivity.getApplicationContext();
        PackageManager pm = context.getPackageManager();
        Set<String> found = new HashSet<>();

        for (String pkg : ROOT_PACKAGES) {
            try {
                pm.getPackageInfo(pkg, 0);
                found.add(pkg);
                continue;
            } catch (PackageManager.NameNotFoundException e) {
                // Not found via PM
            } catch (Exception e) {
                // Ignore
            }

            try {
                Intent launchIntent = pm.getLaunchIntentForPackage(pkg);
                if (launchIntent != null) {
                    found.add(pkg);
                    continue;
                }
            } catch (Exception e) {
                // Ignore
            }

            try {
                File dataDir = new File("/data/data/" + pkg);
                if (dataDir.exists()) {
                    found.add(pkg);
                }
            } catch (Exception e) {
                // Ignore
            }
        }

        for (String path : ROOT_BINARIES) {
            try {
                if (new File(path).exists()) {
                    found.add("binary:" + path);
                }
            } catch (Exception e) {
                // Ignore
            }
        }

        StringBuilder sb = new StringBuilder();
        boolean first = true;
        for (String item : found) {
            if (!first) sb.append(",");
            sb.append(item);
            first = false;
        }
        return sb.toString();
    }
}
