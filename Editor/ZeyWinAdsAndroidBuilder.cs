using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ZeyWinAds.Editor
{
    /// <summary>
    /// Shared Android batchmode builder for fleet APK/AAB verification builds.
    /// </summary>
    public static class ZeyWinAdsAndroidBuilder
    {
        [MenuItem("ZeyWinAds/Build Android APK From Args", priority = 50)]
        public static void BuildApkFromCommandLine()
        {
            BuildFromCommandLine(buildAppBundle: false);
        }

        [MenuItem("ZeyWinAds/Build Android AAB From Args", priority = 51)]
        public static void BuildAabFromCommandLine()
        {
            BuildFromCommandLine(buildAppBundle: true);
        }

        public static void BuildFromCommandLine()
        {
            var args = ParseArgs(Environment.GetCommandLineArgs());
            string outputPath = GetAnyOrEnv(args, null, "OUTPUT_PATH", "outputPath", "buildPath", "customBuildPath");
            bool appBundle = !string.IsNullOrEmpty(outputPath)
                && string.Equals(Path.GetExtension(outputPath), ".aab", StringComparison.OrdinalIgnoreCase);

            BuildFromCommandLine(appBundle);
        }

        private static void BuildFromCommandLine(bool buildAppBundle)
        {
            var args = ParseArgs(Environment.GetCommandLineArgs());
            string outputPath = GetAnyOrEnv(args, null, "OUTPUT_PATH", "outputPath", "buildPath", "customBuildPath");
            if (string.IsNullOrEmpty(outputPath))
                Fail("Missing -outputPath, -buildPath, or OUTPUT_PATH.");

            outputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            ConfigureAndroid(args, buildAppBundle);

            var options = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                target = BuildTarget.Android,
                locationPathName = outputPath,
                options = GetBuildOptions(args)
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log("[ZeyWinAds] Android build result: " + summary.result + " size=" + summary.totalSize + " path=" + outputPath);

            switch (summary.result)
            {
                case BuildResult.Succeeded:
                    EditorApplication.Exit(0);
                    break;
                case BuildResult.Cancelled:
                    EditorApplication.Exit(102);
                    break;
                case BuildResult.Unknown:
                    EditorApplication.Exit(103);
                    break;
                case BuildResult.Failed:
                default:
                    EditorApplication.Exit(101);
                    break;
            }
        }

        private static void ConfigureAndroid(IDictionary<string, string> args, bool buildAppBundle)
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            EditorUserBuildSettings.buildAppBundle = buildAppBundle;

            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;

            string versionName = GetAnyOrEnv(args, null, "ANDROID_VERSION_NAME", "androidVersionName", "versionName", "buildVersion");
            if (!string.IsNullOrEmpty(versionName))
                PlayerSettings.bundleVersion = versionName;

            string versionCodeText = GetAnyOrEnv(args, null, "ANDROID_VERSION_CODE", "androidVersionCode", "versionCode", "buildVersionCode");
            if (int.TryParse(versionCodeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int versionCode))
                PlayerSettings.Android.bundleVersionCode = versionCode;

            string architectures = GetAnyOrEnv(args, null, "ANDROID_ARCHITECTURES", "androidArchitectures");
            if (!string.IsNullOrEmpty(architectures))
                ApplyArchitectures(architectures);

            string optimizedFramePacing = GetAnyOrEnv(args, "false", "ANDROID_OPTIMIZED_FRAME_PACING", "androidOptimizedFramePacing", "optimizedFramePacing");
            PlayerSettings.Android.optimizedFramePacing = ParseBool(optimizedFramePacing, false);

            ApplySigning(args);
        }

        private static void ApplyArchitectures(string value)
        {
            AndroidArchitecture selected = 0;
            foreach (string part in value.Split(','))
            {
                string normalized = part.Trim().ToUpperInvariant();
                if (normalized == "ARM64" || normalized == "ARM64V8A")
                    selected |= AndroidArchitecture.ARM64;
                else if (normalized == "ARMV7" || normalized == "ARMEABIV7A")
                    selected |= AndroidArchitecture.ARMv7;
            }

            if (selected != 0)
                PlayerSettings.Android.targetArchitectures = selected;
        }

        private static void ApplySigning(IDictionary<string, string> args)
        {
            bool hasExplicitKeystore = TryGetAnyOrEnv(args, out string keystorePath, "ANDROID_KEYSTORE_PATH", "androidKeystorePath", "keystorePath");
            if (!hasExplicitKeystore)
            {
                string existingPath = PlayerSettings.Android.keystoreName;
                if (!PlayerSettings.Android.useCustomKeystore || string.IsNullOrEmpty(existingPath))
                    return;

                existingPath = ResolveProjectPath(existingPath);
                if (File.Exists(existingPath))
                    return;

                Debug.LogWarning("[ZeyWinAds] Custom Android keystore is configured but missing; using debug signing for this build.");
                PlayerSettings.Android.useCustomKeystore = false;
                return;
            }

            keystorePath = ResolveProjectPath(keystorePath);
            if (!File.Exists(keystorePath))
                throw new FileNotFoundException("Android keystore not found: " + keystorePath, keystorePath);

            string keystorePass = GetAnyOrEnv(args, PlayerSettings.Android.keystorePass, "ANDROID_KEYSTORE_PASS", "androidKeystorePass", "keystorePass");
            string keyAlias = GetAnyOrEnv(args, PlayerSettings.Android.keyaliasName, "ANDROID_KEYALIAS_NAME", "androidKeyaliasName", "keyAlias");
            string keyAliasPass = GetAnyOrEnv(args, PlayerSettings.Android.keyaliasPass, "ANDROID_KEYALIAS_PASS", "androidKeyaliasPass", "keyAliasPass");

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystorePath;
            PlayerSettings.Android.keystorePass = keystorePass;
            PlayerSettings.Android.keyaliasName = keyAlias;
            PlayerSettings.Android.keyaliasPass = string.IsNullOrEmpty(keyAliasPass) ? keystorePass : keyAliasPass;
        }

        private static string ResolveProjectPath(string path)
        {
            const string InProjectPrefix = "{inproject}: ";
            if (path.StartsWith(InProjectPrefix, StringComparison.Ordinal))
                return Path.Combine(Directory.GetCurrentDirectory(), path.Substring(InProjectPrefix.Length));

            if (!Path.IsPathRooted(path))
                return Path.GetFullPath(path);

            return path;
        }

        private static string[] GetEnabledScenes()
        {
            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                    scenes.Add(scene.path);
            }

            if (scenes.Count == 0)
                Fail("No enabled scenes found in EditorBuildSettings.");

            return scenes.ToArray();
        }

        private static BuildOptions GetBuildOptions(IDictionary<string, string> args)
        {
            BuildOptions options = BuildOptions.None;
            string development = GetAnyOrEnv(args, null, "DEVELOPMENT_BUILD", "development", "developmentBuild");
            if (ParseBool(development, false))
                options |= BuildOptions.Development;

            return options;
        }

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.IsNullOrEmpty(arg) || !arg.StartsWith("-", StringComparison.Ordinal))
                    continue;

                string key = arg.TrimStart('-');
                string value = "true";
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    value = args[i + 1];
                    i++;
                }

                parsed[key] = value;
            }

            return parsed;
        }

        private static string GetAnyOrEnv(IDictionary<string, string> args, string fallback, string envName, params string[] keys)
        {
            if (TryGetAnyOrEnv(args, out string value, envName, keys))
                return value;

            return fallback;
        }

        private static bool TryGetAnyOrEnv(IDictionary<string, string> args, out string value, string envName, params string[] keys)
        {
            foreach (string key in keys)
            {
                if (args.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value))
                    return true;
            }

            string envValue = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                value = envValue;
                return true;
            }

            value = null;
            return false;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            if (string.IsNullOrEmpty(value))
                return fallback;

            if (bool.TryParse(value, out bool result))
                return result;

            if (value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase))
                return true;

            if (value == "0" || value.Equals("no", StringComparison.OrdinalIgnoreCase))
                return false;

            return fallback;
        }

        private static void Fail(string message)
        {
            Debug.LogError("[ZeyWinAds] " + message);
            EditorApplication.Exit(130);
            throw new InvalidOperationException(message);
        }
    }
}
