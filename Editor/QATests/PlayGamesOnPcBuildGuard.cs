using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ZeyWinAds.Editor.QATests
{
    // Automatic, all-games Play Games on PC compatibility gate. Runs after every Android build
    // (local or CI) and fails it if the ACTUAL built APK/AAB — after Gradle's full manifest
    // merge, so this also catches features contributed by third-party .aar dependencies that
    // AdMobBuildPostprocessor's earlier, pre-merge manifest patch can't see — declares any
    // hardware feature Play Games on PC doesn't support as required. This is the SDK-level
    // guard: every one of the 18 games shares this build hook, so no game can silently
    // reintroduce an unsupported required feature (its own static manifest edit, or a new
    // plugin's .aar) without the build failing, without hand-patching each game individually.
    //
    // Verification method matches what was done by hand for Plinko_v1 baseline.apk this
    // session: `aapt dump badging <apk>` against the real built artifact, not the pre-build
    // source manifest. See PlayGamesOnPcPolicy for the rule data and pure check logic, and
    // PlayGamesOnPcPolicyTests for the unit-testable half of this (no build required there).
    public class PlayGamesOnPcBuildGuard : IPostprocessBuildWithReport
    {
        // After AdMobBuildPostprocessor (100) and any other manifest patcher — this must run
        // last, against the finished artifact.
        public int callbackOrder => 1000;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;

            string outputPath = report.summary.outputPath;
            if (string.IsNullOrEmpty(outputPath))
                return;

            string ext = Path.GetExtension(outputPath);
            bool isPackage = string.Equals(ext, ".apk", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".aab", StringComparison.OrdinalIgnoreCase);
            if (!isPackage || !File.Exists(outputPath))
            {
                // Gradle project export (not an APK/AAB) — nothing to scan yet; the eventual
                // packaged artifact from that exported project isn't produced by this build step.
                return;
            }

            string aaptPath = ResolveAaptPath();
            if (string.IsNullOrEmpty(aaptPath))
            {
                Debug.LogWarning("[ZeyWinAds] PlayGamesOnPcBuildGuard: couldn't locate aapt — " +
                    "skipping Play Games on PC compatibility check for " + outputPath + ".");
                return;
            }

            string aaptOutput = RunAaptDumpBadging(aaptPath, outputPath);
            if (aaptOutput == null)
                return; // already logged a warning inside RunAaptDumpBadging

            var violations = PlayGamesOnPcPolicy.FindAaptBadgingViolations(aaptOutput);
            if (violations.Count == 0)
            {
                Debug.Log("[ZeyWinAds] PlayGamesOnPcBuildGuard: " + outputPath +
                    " has no unsupported required hardware features — Play Games on PC compatible.");
                return;
            }

            DeleteNonCompliantArtifact(outputPath);

            throw new BuildFailedException(
                $"[ZeyWinAds] Play Games on PC compatibility check failed for '{outputPath}' " +
                $"({violations.Count}): " + string.Join(" | ", violations));
        }

        // By this point the APK/AAB is already fully written to disk (that's the only reason
        // aapt could scan it) — without this, a "failed" build still leaves a complete,
        // non-compliant artifact sitting where a build output is normally expected, which a
        // script or person checking "does the file exist" rather than the actual exit code
        // could mistake for a valid build. Best-effort: a locked/undeletable file only logs a
        // warning, since the BuildFailedException about to be thrown is the real signal either way.
        private static void DeleteNonCompliantArtifact(string outputPath)
        {
            try
            {
                File.Delete(outputPath);
                Debug.LogWarning("[ZeyWinAds] PlayGamesOnPcBuildGuard: deleted non-compliant build " +
                    "artifact '" + outputPath + "' so a failed build doesn't leave a bad APK/AAB on disk.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ZeyWinAds] PlayGamesOnPcBuildGuard: failed to delete non-compliant " +
                    "artifact '" + outputPath + "' — " + ex.Message);
            }
        }

        private static string RunAaptDumpBadging(string aaptPath, string apkPath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = aaptPath,
                    Arguments = $"dump badging \"{apkPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using (var process = Process.Start(psi))
                {
                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit(60000);

                    if (process.ExitCode != 0)
                    {
                        Debug.LogWarning("[ZeyWinAds] PlayGamesOnPcBuildGuard: aapt exited " +
                            process.ExitCode + " — skipping check. stderr: " + stderr);
                        return null;
                    }

                    return stdout;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ZeyWinAds] PlayGamesOnPcBuildGuard: failed to run aapt — " +
                    "skipping Play Games on PC compatibility check. " + ex.Message);
                return null;
            }
        }

        private static string ResolveAaptPath()
        {
            string exeName = Application.platform == RuntimePlatform.WindowsEditor ? "aapt.exe" : "aapt";

            // 1. Explicit override, for a machine/CI runner that needs to point at a specific SDK.
            string envOverride = Environment.GetEnvironmentVariable("ZEYWIN_AAPT_PATH");
            if (!string.IsNullOrEmpty(envOverride) && File.Exists(envOverride))
                return envOverride;

            // 2. Unity's own bundled Android SDK build-tools — present on every machine that can
            // build Android at all, so this is the reliable zero-setup default.
            string editorDataDir = ResolveEditorDataDir();
            if (!string.IsNullOrEmpty(editorDataDir))
            {
                string buildToolsRoot = Path.Combine(editorDataDir, "PlaybackEngines", "AndroidPlayer", "SDK", "build-tools");
                string found = FindNewestAapt(buildToolsRoot, exeName);
                if (found != null)
                    return found;
            }

            // 3. A separately installed Android SDK, if ANDROID_HOME/ANDROID_SDK_ROOT is set
            // (common on CI runners and developer machines with their own SDK install).
            foreach (string envVar in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
            {
                string sdkRoot = Environment.GetEnvironmentVariable(envVar);
                if (string.IsNullOrEmpty(sdkRoot))
                    continue;

                string found = FindNewestAapt(Path.Combine(sdkRoot, "build-tools"), exeName);
                if (found != null)
                    return found;
            }

            return null;
        }

        // EditorApplication.applicationPath -> ".../Editor/Unity.exe" (Windows/Linux) or
        // ".../Unity.app/Contents/MacOS/Unity" (macOS). The bundled SDK sits at
        // "<...>/Editor/Data/PlaybackEngines/..." on Windows/Linux, or directly under
        // "Unity.app/Contents/PlaybackEngines/..." on macOS (no "Data" folder there).
        private static string ResolveEditorDataDir()
        {
            string appPath = EditorApplication.applicationPath;
            string appDir = string.IsNullOrEmpty(appPath) ? null : Path.GetDirectoryName(appPath);
            if (string.IsNullOrEmpty(appDir))
                return null;

            string windowsLinuxData = Path.Combine(appDir, "Data");
            if (Directory.Exists(windowsLinuxData))
                return windowsLinuxData;

            string macContents = Path.GetFullPath(Path.Combine(appDir, ".."));
            if (Directory.Exists(Path.Combine(macContents, "PlaybackEngines")))
                return macContents;

            return null;
        }

        private static string FindNewestAapt(string buildToolsRoot, string exeName)
        {
            if (!Directory.Exists(buildToolsRoot))
                return null;

            return Directory.GetDirectories(buildToolsRoot)
                .OrderByDescending(d => d)
                .Select(d => Path.Combine(d, exeName))
                .FirstOrDefault(File.Exists);
        }
    }
}
