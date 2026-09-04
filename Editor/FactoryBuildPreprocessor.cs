#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Android.Types;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ZeyWinAds.Editor
{
    // Factory contract v1 (docs/factory in the admin repo). CI places these files
    // in the working copy of every base repo before the Unity build starts:
    //   factory/factory-config.json   — {schema, build, app, sdk} (no secrets)
    //   factory/icon.png              — app icon
    //   factory/google-services.json  — Firebase config
    // Keystore/signing values never touch disk — unity-builder consumes them via
    // its androidKeystore* inputs.
    //
    // Ships from the SDK (not per base repo) so every consuming project gets the same factory
    // wiring and bundle-id healing automatically. Runs for whichever platform is active
    // (EditorUserBuildSettings.activeBuildTarget) — see the per-target notes below on the two
    // places that are still Android-specific.
    //
    // SDK wiring: bundle id / product name go to PlayerSettings, sdk.* goes into
    // ZeyWinAdsSettings + GoogleMobileAdsSettings (see ApplySdkConfig).
    public class FactoryBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;

        private const string LogPrefix = "[FactoryBuildPreprocessor]";
        private const string GoogleMobileAdsSettingsPath = "Assets/GoogleMobileAds/Resources/GoogleMobileAdsSettings.asset";

        // Bundle id for builds that run WITHOUT a staged factory/ config — a developer building
        // locally, or the on-device Test Runner. CI release / QA-crawl builds stage
        // factory/factory-config.json first and go through the factory branch in OnPreprocessBuild.
        //
        // We can't just read the "real" id inside OnPreprocessBuild: Unity Test Framework's
        // PlatformSetup swaps the application identifier for a placeholder
        // ("com.UnityTestRunner.UnityTestRunner") for the duration of an on-device test build — before
        // OnPreprocessBuild runs, without a domain reload — and only restores it in CleanUp() after the
        // build. So a background hook (KeepBundleIdSnapshotFresh, an EditorApplication.update tick — a
        // domain-reload-only snapshot would go stale the moment a developer edits the id in Player
        // Settings and immediately runs tests) records the live real id into SessionState + EditorPrefs,
        // and ResolveTestBundleId() resolves it from, in order: an explicit CI override, the current
        // ProjectSettings value, that snapshot. If none is available the build FAILS with a pointed
        // message — no id is ever guessed.
        //
        // Scoped per project (PlayerSettings.productGUID) and per target group: this same class now
        // ships identically to every base repo, so an unscoped EditorPrefs key would leak one project's
        // bundle id into another's build on the same developer machine. SessionState doesn't need the
        // same guard (it's per Editor process, i.e. already per open project) but is scoped too for
        // consistency.
        private const string BundleIdOverrideEnvVar = "FACTORY_TEST_BUNDLE_ID";
        private const string TestRunnerPlaceholderId = "com.UnityTestRunner.UnityTestRunner";

        private static string BundleIdPrefKey(BuildTargetGroup group) =>
            $"FactoryBuildPreprocessor.RealBundleId.{PlayerSettings.productGUID}.{group}";

        private static BuildTargetGroup ActiveTargetGroup =>
            BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);

        [System.Serializable] private class AppCfg { public string bundle_id; public string app_name; }
        [System.Serializable] private class SdkConfig
        {
            public string api_key;
            public string admob_app_id;
            public string banner_unit_id;
            public string interstitial_unit_id;
            public string rewarded_unit_id;
        }
        [System.Serializable] private class Root { public AppCfg app; public SdkConfig sdk; }

        private static bool IsRealBundleId(string id) =>
            !string.IsNullOrWhiteSpace(id) &&
            id.IndexOf("UnityTestRunner", StringComparison.OrdinalIgnoreCase) < 0;

        // The bundle id to build a no-factory-config player with, or null if none is available.
        // Priority:
        //   1. the FACTORY_TEST_BUNDLE_ID env var    — explicit CI override
        //   2. whatever ProjectSettings holds now    — unless UTF already swapped in its placeholder
        //   3. the id snapshotted before UTF's swap  — this session (SessionState) or machine (EditorPrefs)
        private static string ResolveTestBundleId(BuildTargetGroup group)
        {
            var fromCi = Environment.GetEnvironmentVariable(BundleIdOverrideEnvVar);
            if (IsRealBundleId(fromCi))
                return fromCi.Trim();

            var current = PlayerSettings.GetApplicationIdentifier(group);
            if (IsRealBundleId(current))
                return current;

            var key = BundleIdPrefKey(group);

            var session = SessionState.GetString(key, string.Empty);
            if (IsRealBundleId(session))
                return session;

            var persisted = EditorPrefs.GetString(key, string.Empty);
            if (IsRealBundleId(persisted))
                return persisted;

            return null;
        }

        [InitializeOnLoadMethod]
        private static void InstallBundleIdHooks()
        {
            // One-time on load: heal an identifier left as the placeholder by an interrupted test run,
            // and seed the snapshot from the current real value.
            HealBundleIdIfPlaceholder();
            KeepBundleIdSnapshotFresh();

            // Ongoing: keep the snapshot current with the developer's live Player Settings value, since
            // UTF swaps in its placeholder just before the test build with no domain reload in between.
            EditorApplication.update -= KeepBundleIdSnapshotFresh;
            EditorApplication.update += KeepBundleIdSnapshotFresh;
        }

        private static double _nextBundleIdSnapshotCheck;

        // Throttled EditorApplication.update tick. While the active target group's bundle id is a real
        // value, mirror it into SessionState + EditorPrefs so ResolveTestBundleId() has it after UTF
        // swaps in its placeholder. Only writes on an actual change.
        private static void KeepBundleIdSnapshotFresh()
        {
            if (EditorApplication.timeSinceStartup < _nextBundleIdSnapshotCheck)
                return;
            _nextBundleIdSnapshotCheck = EditorApplication.timeSinceStartup + 1.0;

            var group = ActiveTargetGroup;
            var current = PlayerSettings.GetApplicationIdentifier(group);
            if (!IsRealBundleId(current))
                return;

            var key = BundleIdPrefKey(group);
            if (SessionState.GetString(key, string.Empty) != current)
                SessionState.SetString(key, current);
            if (EditorPrefs.GetString(key, string.Empty) != current)
                EditorPrefs.SetString(key, current);
        }

        // If the identifier is currently the placeholder — an interrupted test run persisted it, or UTF
        // just ran — put the best real value back. Never runs during an active test build (called only
        // on load), so it can't fight UTF's own swap. Leaves the placeholder if no real value is known;
        // OnPreprocessBuild then fails the build with a pointed message rather than guessing an id.
        private static void HealBundleIdIfPlaceholder()
        {
            var group = ActiveTargetGroup;
            var current = PlayerSettings.GetApplicationIdentifier(group);
            if (IsRealBundleId(current))
                return;

            var restore = ResolveTestBundleId(group);
            if (!string.IsNullOrEmpty(restore) && restore != current)
            {
                PlayerSettings.SetApplicationIdentifier(group, restore);
                Debug.Log($"{LogPrefix} {group} bundle id was '{current}' (Test Framework placeholder) — restored to '{restore}'.");
            }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            var group = ActiveTargetGroup;
            var cfgPath = Path.Combine(Directory.GetCurrentDirectory(), "factory/factory-config.json");
            if (!File.Exists(cfgPath))
            {
                // Local editor / Test Runner build with no factory input. Unity Test Framework's
                // PlatformSetup has already replaced the application identifier with its placeholder
                // by this point; set a real id back so on-device test players install under a stable
                // package name and QaBuildGuard's RunsOnRealBundleId doesn't fail the suite. productName
                // is left untouched — whatever ProjectSettings currently holds.
                var testBundleId = ResolveTestBundleId(group);
                if (string.IsNullOrEmpty(testBundleId))
                    Fail($"No factory/factory-config.json and no usable {group} bundle id available. Set the " +
                         $"{BundleIdOverrideEnvVar} environment variable, or assign a real bundle id in " +
                         "Project Settings > Player before building. (Unity Test Framework's placeholder " +
                         "'" + TestRunnerPlaceholderId + "' is not a valid build identity.)");

                PlayerSettings.SetApplicationIdentifier(group, testBundleId);
                Debug.Log($"{LogPrefix} No factory/factory-config.json — building with {group} bundle id '{testBundleId}'.");
                return;
            }

            var cfg = JsonUtility.FromJson<Root>(File.ReadAllText(cfgPath));

            // ---- SDK keys: ADAPT PER BASE REPO ----
            // Checked and applied before any factory file copying below — a bad/missing SDK config
            // fails the build outright rather than shipping a build with broken ad serving.
            // NOTE: only wires the Android AdMob unit ids (ZeyWinAdsSettings.admob*Android) — the
            // factory-config.json contract doesn't carry separate iOS unit ids yet. Extend both this
            // and the contract's SdkConfig together if/when iOS ad units need to differ from Android's.
            ApplySdkConfig(cfg.sdk, cfgPath);
            // ---- end SDK keys ----

            PlayerSettings.SetApplicationIdentifier(group, cfg.app.bundle_id);
            PlayerSettings.productName = cfg.app.app_name;
            PlayerSettings.SetScriptingBackend(group, ScriptingImplementation.IL2CPP);

            if (group == BuildTargetGroup.Android)
            {
                // Play-ready binary settings (AAB uploads require 64-bit).
                PlayerSettings.Android.targetArchitectures = UnityEditor.AndroidArchitecture.ARMv7 | UnityEditor.AndroidArchitecture.ARM64;

                // Native debug symbols: emit a "public" (symbol-table) symbols package next to
                // the build output. Crashlytics needs it to symbolicate native / IL2CPP crash
                // frames — CI uploads the resulting <product>-<version>-v<code>.symbols.zip via
                // `firebase crashlytics:symbols:upload`. SymbolTable keeps the package small;
                // Full would add line-level info at a much larger size.
                UserBuildSettings.DebugSymbols.level = DebugSymbolLevel.SymbolTable;
            }
            // iOS symbol/arch settings aren't wired here — Xcode's own build settings (and
            // AdMobBuildPostprocessor's Info.plist writes) cover what's needed for BlackJack today.
            // Add an iOS branch here if/when a specific PlayerSettings.iOS.* value needs to be forced.

            // Icon: import into Assets so Unity can assign it.
            Directory.CreateDirectory("Assets/Factory");
            File.Copy("factory/icon.png", "Assets/Factory/icon.png", true);

            // google-services.json where Firebase tooling expects it.
            File.Copy("factory/google-services.json", "Assets/google-services.json", true);

            AssetDatabase.Refresh();

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Factory/icon.png");
            if (icon != null)
            {
                PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon });
            }

            AssetDatabase.SaveAssets();
        }

        private static void ApplySdkConfig(SdkConfig sdk, string cfgPath)
        {
            var missing = new List<string>();
            if (sdk == null || string.IsNullOrWhiteSpace(sdk.api_key)) missing.Add("sdk.api_key");
            if (sdk == null || string.IsNullOrWhiteSpace(sdk.admob_app_id)) missing.Add("sdk.admob_app_id");
            if (sdk == null || string.IsNullOrWhiteSpace(sdk.banner_unit_id)) missing.Add("sdk.banner_unit_id");
            if (sdk == null || string.IsNullOrWhiteSpace(sdk.interstitial_unit_id)) missing.Add("sdk.interstitial_unit_id");
            if (sdk == null || string.IsNullOrWhiteSpace(sdk.rewarded_unit_id)) missing.Add("sdk.rewarded_unit_id");

            if (missing.Count > 0)
                Fail($"'{cfgPath}' has empty/missing field(s): {string.Join(", ", missing)}.");

            if (!ZeyWinAdsSettings.IsValidAdMobAppId(sdk.admob_app_id))
                Fail($"'{cfgPath}' field 'sdk.admob_app_id' is not a valid AdMob App ID " +
                     $"(expected 'ca-app-pub-<digits>~<digits>'), got '{sdk.admob_app_id}'.");

            var settings = FindZeyWinAdsSettings();
            settings.apiKey = sdk.api_key;
            settings.admobAppIdAndroid = sdk.admob_app_id;
            settings.admobBannerAndroid = sdk.banner_unit_id;
            settings.admobInterstitialAndroid = sdk.interstitial_unit_id;
            settings.admobRewardedAndroid = sdk.rewarded_unit_id;
            EditorUtility.SetDirty(settings);

            ApplyGoogleMobileAdsAppId(sdk.admob_app_id);

            Debug.Log($"{LogPrefix} OK — populated ZeyWinAdsSettings and GoogleMobileAdsSettings from " +
                      $"'{cfgPath}' (admob_app_id={sdk.admob_app_id}, banner/interstitial/rewarded unit ids set).");
        }

        private static ZeyWinAdsSettings FindZeyWinAdsSettings()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(ZeyWinAdsSettings)}");

            if (guids.Length == 0)
                Fail("ZeyWinAdsSettings asset not found in the project. Create it via " +
                     "ZeyWinAds > Settings before building.");

            if (guids.Length > 1)
                Fail("Multiple ZeyWinAdsSettings assets found in the project: " +
                     string.Join(", ", guids.Select(AssetDatabase.GUIDToAssetPath)) +
                     ". There must be exactly one.");

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var settings = AssetDatabase.LoadAssetAtPath<ZeyWinAdsSettings>(path);

            if (settings == null)
                Fail($"Failed to load ZeyWinAdsSettings asset at '{path}'.");

            return settings;
        }

        private static void ApplyGoogleMobileAdsAppId(string admobAppId)
        {
            // GoogleMobileAdsSettings (GoogleMobileAds.Editor) is an internal type, so it can't be
            // referenced directly from this assembly — go through SerializedObject instead, which
            // works off the serialized field names regardless of C# accessibility.
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(GoogleMobileAdsSettingsPath);
            if (asset == null)
                Fail($"GoogleMobileAdsSettings asset not found at '{GoogleMobileAdsSettingsPath}'. " +
                     "Open Assets > Google Mobile Ads > Settings once to create it.");

            var serialized = new SerializedObject(asset);
            var androidAppId = serialized.FindProperty("adMobAndroidAppId");

            if (androidAppId == null)
                Fail($"'{GoogleMobileAdsSettingsPath}' is missing the expected 'adMobAndroidAppId' field " +
                     "— GoogleMobileAdsSettings' layout may have changed.");

            androidAppId.stringValue = admobAppId;
            serialized.ApplyModifiedProperties();
        }

        private static void Fail(string reason)
        {
            Debug.LogError($"{LogPrefix} Build aborted — {reason}");
            throw new BuildFailedException($"{LogPrefix} {reason}");
        }
    }
}
#endif
