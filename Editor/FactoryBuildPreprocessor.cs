#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
#if UNITY_ANDROID
// The Android editor extension module is not installed on iOS-only build
// machines (the BlackJack ad-hoc CI runner, dev Macs without the Android
// module). Everything it provides — Unity.Android.Types.DebugSymbolLevel,
// UnityEditor.Android.UserBuildSettings, UnityEditor.AndroidPlatformIconKind —
// is used only on the Android branch below, so it all lives behind this guard.
using Unity.Android.Types;
using UnityEditor.Android;
#endif

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

        internal const string LogPrefix = "[FactoryBuildPreprocessor]";
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

        // Minify (R8) Release should be on for every shipped build — see QATests/MinifyPolicy.cs
        // for why. Applied here (not just gated on) so a build never fails over a setting this
        // preprocessor can just fix itself — the QA gate stays as a backstop in case this method
        // is ever bypassed, and as the check surfaced to the Test Runner / manual "Run QA Checks".
        private static void EnsureMinifyEnabled()
        {
            if (PlayerSettings.Android.minifyRelease)
                return;

            PlayerSettings.Android.minifyRelease = true;
            Debug.Log($"{LogPrefix} Enabled Android Minify Release (was off).");
        }

        // ZeyWinAds' JNI bridge (com.zeywinads.unity.*) is called by name from native/C# code
        // (see Runtime/Core/AndroidJniSafe.cs), invisible to R8's static analysis — without this
        // rule, a consuming game enabling Minify/R8 can have R8 silently strip or rename those
        // classes, causing a NoSuchMethodError/ClassNotFoundException at runtime instead of a
        // build failure. Unity auto-merges any file at this exact path into the release build's
        // consumer proguard rules. Staged here (not committed per base repo) so it can't silently
        // go missing on a project revert — ships identically to every consuming game, same
        // reasoning as everything else in this preprocessor.
        private const string ProguardKeepRulePath = "Assets/Plugins/Android/proguard-user.txt";
        private const string ZeyWinAdsKeepRule = "-keep class com.zeywinads.unity.** { *; }";

        // Idempotent: appends only if the rule isn't already present, so a game's own additional
        // proguard-user.txt rules are preserved rather than overwritten. Runs unconditionally for
        // every Android build (factory or local) — Minify safety shouldn't depend on whether a
        // factory config happens to be staged.
        private static void StageMinifyKeepRules()
        {
            Directory.CreateDirectory("Assets/Plugins/Android");

            var existing = File.Exists(ProguardKeepRulePath) ? File.ReadAllText(ProguardKeepRulePath) : string.Empty;
            if (existing.Contains(ZeyWinAdsKeepRule))
                return;

            var separator = existing.Length > 0 && !existing.EndsWith("\n") ? "\n" : string.Empty;
            File.AppendAllText(ProguardKeepRulePath, separator + ZeyWinAdsKeepRule + "\n");
            AssetDatabase.ImportAsset(ProguardKeepRulePath);
            Debug.Log($"{LogPrefix} Ensured ZeyWinAds JNI keep rule is present in '{ProguardKeepRulePath}'.");
        }

        // play-services-ads (GoogleMobileAds) has an implicit runtime dependency on
        // androidx.work.impl.WorkDatabase that Unity's Android Resolver doesn't pull in
        // automatically — without it, the app crashes on every launch ("Failed to create an
        // instance of androidx.work.impl.WorkDatabase" inside androidx.startup.InitializationProvider,
        // confirmed on-device this session). Only staged for games that actually use
        // com.google.ads.mobile — a game without AdMob never hits this crash and shouldn't carry
        // an unexplained extra dependency. Idempotent, same pattern as StageMinifyKeepRules.
        private const string WorkManagerDepsPath = "Assets/Editor/WorkManagerDependencies.xml";
        private const string WorkManagerSpec = "androidx.work:work-runtime:2.11.2";
        private const string AdMobPackageId = "com.google.ads.mobile";

        private static void StageWorkManagerDependencyFix()
        {
            const string manifestPath = "Packages/manifest.json";
            if (!File.Exists(manifestPath) || !File.ReadAllText(manifestPath).Contains(AdMobPackageId))
                return; // This project doesn't use AdMob, so it can't hit this crash.

            if (File.Exists(WorkManagerDepsPath) && File.ReadAllText(WorkManagerDepsPath).Contains("androidx.work:work-runtime"))
                return; // Already present (possibly a different pinned version — don't clobber it).

            Directory.CreateDirectory("Assets/Editor");
            File.WriteAllText(WorkManagerDepsPath,
                "<!-- play-services-ads (GoogleMobileAds) has an implicit runtime dependency on\n" +
                "     androidx.work.impl.WorkDatabase that Unity's Android Resolver doesn't pull\n" +
                "     in automatically, since it's never explicitly declared in\n" +
                "     GoogleMobileAdsDependencies.xml. Without it, the app crashes on every\n" +
                "     launch: \"Failed to create an instance of androidx.work.impl.WorkDatabase\"\n" +
                "     inside androidx.startup.InitializationProvider. Declaring it explicitly\n" +
                "     here ensures the full WorkManager runtime (including the Room-generated\n" +
                "     WorkDatabase_Impl) is actually bundled. Auto-staged by FactoryBuildPreprocessor\n" +
                "     — safe to edit the pinned version below, but don't delete this file. -->\n\n" +
                "<dependencies>\n" +
                "  <androidPackages>\n" +
                $"    <androidPackage spec=\"{WorkManagerSpec}\">\n" +
                "    </androidPackage>\n" +
                "  </androidPackages>\n" +
                "</dependencies>\n");
            AssetDatabase.ImportAsset(WorkManagerDepsPath);
            Debug.Log($"{LogPrefix} Staged '{WorkManagerDepsPath}' ({WorkManagerSpec}) — this project uses " +
                $"{AdMobPackageId}, which needs it to avoid a WorkDatabase crash on launch.");
        }

        // Absolute path to this script on disk, resolved by the compiler at compile time — works
        // correctly regardless of how the SDK package is installed (git package cache, local
        // `file:` path for dev, embedded), since Unity recompiles from the package's current
        // on-disk location every domain reload. Used to locate the bundled launcherTemplate.gradle
        // sitting next to this file under GradleTemplates/, without a Resources-folder or
        // AssetDatabase-GUID workaround.
        private static string SdkTemplateSourcePath([CallerFilePath] string callerFilePath = "") =>
            Path.Combine(Path.GetDirectoryName(callerFilePath), "GradleTemplates", "launcherTemplate.gradle");

        private const string LauncherTemplatePath = "Assets/Plugins/Android/launcherTemplate.gradle";

        // Stages the SDK's launcherTemplate.gradle (Crashlytics Gradle plugin applied +
        // firebaseCrashlytics.mappingFileUploadEnabled) into the consuming game if it doesn't
        // already have one of its own. Unity's default Android export has no launcherTemplate.gradle
        // at all unless one exists at this path — Unity auto-uses it once it's there. Never
        // overwrites a game's own customized launcher template; warns instead so the gap is visible
        // rather than silently skipping mapping upload.
        private static void StageCrashlyticsLauncherTemplate()
        {
            if (File.Exists(LauncherTemplatePath))
            {
                var contents = File.ReadAllText(LauncherTemplatePath);
                if (!contents.Contains("com.google.firebase.crashlytics"))
                    Debug.LogWarning($"{LogPrefix} '{LauncherTemplatePath}' already exists and doesn't " +
                        "reference the Crashlytics Gradle plugin — not overwriting it, so automatic " +
                        $"mapping-file upload is NOT wired in. Merge '{SdkTemplateSourcePath()}' in " +
                        "manually if you want it.");
                return;
            }

            Directory.CreateDirectory("Assets/Plugins/Android");
            File.Copy(SdkTemplateSourcePath(), LauncherTemplatePath);
            AssetDatabase.ImportAsset(LauncherTemplatePath);
            Debug.Log($"{LogPrefix} Staged '{LauncherTemplatePath}' from the SDK — enables automatic " +
                "Crashlytics mapping-file upload during release builds. " +
                "NOTE: firebaseCrashlytics.googleServicesResourceRoot in this template is unverified " +
                "against a real build yet — check the build log if mapping upload fails.");
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            var group = ActiveTargetGroup;

            if (group == BuildTargetGroup.Android)
            {
                EnsureMinifyEnabled();
                StageMinifyKeepRules();
                StageWorkManagerDependencyFix();
                StageCrashlyticsLauncherTemplate();
            }

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
#if UNITY_ANDROID
                UserBuildSettings.DebugSymbols.level = DebugSymbolLevel.SymbolTable;
#endif
            }
            // iOS symbol/arch settings aren't wired here — Xcode's own build settings (and
            // AdMobBuildPostprocessor's Info.plist writes) cover what's needed for BlackJack today.
            // Add an iOS branch here if/when a specific PlayerSettings.iOS.* value needs to be forced.

            // Icon: import into Assets so Unity can assign it.
            Directory.CreateDirectory("Assets/Factory");
            File.Copy("factory/icon.png", "Assets/Factory/icon.png", true);

            // Firebase config. The Firebase Editor plugin generates the Android
            // google-services.xml string resources from whatever google-services.json it can
            // find under Assets/ (or the project root), and when more than one exists it does
            // NOT prefer the one we just wrote. A stale copy committed into a base repo (seen
            // at Assets/Plugins/Android/google-services.json) would therefore silently
            // override the factory Firebase config and ship the build pointed at the wrong
            // Firebase project. Remove every other copy first, then write the factory one as
            // the single source of truth. CI-only: this is inside the factory-config.json
            // branch, so local / Test Runner builds are never touched.
            PurgeStrayGoogleServicesJson("Assets/google-services.json");
            File.Copy("factory/google-services.json", "Assets/google-services.json", true);

            AssetDatabase.Refresh();

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Factory/icon.png");
            if (icon != null)
            {
                PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon });

                if (group == BuildTargetGroup.Android)
                {
                    // Explicitly wire Legacy, Round, AND Adaptive icon slots to the same square
                    // icon. Leaving any of them to Unity's automatic derivation from the generic
                    // default set above makes Unity shrink-and-pad the square icon (and, for
                    // Adaptive, default the background layer to white) so nothing can clip when
                    // it's masked into a circle — on round-icon launchers that renders the logo
                    // as a tiny square floating in a white circle instead of filling it. Setting
                    // the slots directly makes Unity just scale the square icon edge-to-edge into
                    // each required size/layer, no padding, no synthesized white background.
                    //
                    // Adaptive matters most here: on API 26+ (virtually every device in the
                    // field) launchers mask the Adaptive icon (foreground+background), not the
                    // Legacy/Round resource — Round is only consulted on API 25 (Android 7.1),
                    // which is effectively extinct. So Adaptive being auto-derived is what
                    // actually causes the "small icon, white bg" symptom on real round-icon
                    // phones even after Legacy/Round are fixed.
#if UNITY_ANDROID
                    SetAndroidIcon(AndroidPlatformIconKind.Legacy, icon);
                    SetAndroidIcon(AndroidPlatformIconKind.Round, icon);
                    SetAndroidIcon(AndroidPlatformIconKind.Adaptive, icon);
#endif
                }
            }

            AssetDatabase.SaveAssets();
        }

        // Delete every google-services.json under Assets/ (recursively) and next to the
        // project root, except the canonical one the factory build owns. Called only from the
        // factory branch of OnPreprocessBuild, so it never runs for a local or Test Runner
        // build. Uses raw File.Delete (+ .meta) like the sibling File.Copy above; the
        // AssetDatabase.Refresh() that follows reconciles the removals. The generated
        // google-services.xml is left alone — the Firebase plugin regenerates it from the
        // single remaining json during the same build.
        private static void PurgeStrayGoogleServicesJson(string canonicalRelPath)
        {
            var canonicalFull = Path.GetFullPath(canonicalRelPath);

            var candidates = new List<string>();
            if (Directory.Exists("Assets"))
                candidates.AddRange(Directory.GetFiles("Assets", "google-services.json", SearchOption.AllDirectories));
            var rootFile = Path.Combine(Directory.GetCurrentDirectory(), "google-services.json");
            if (File.Exists(rootFile))
                candidates.Add(rootFile);

            foreach (var path in candidates)
            {
                if (string.Equals(Path.GetFullPath(path), canonicalFull, StringComparison.OrdinalIgnoreCase))
                    continue;

                File.Delete(path);
                var meta = path + ".meta";
                if (File.Exists(meta))
                    File.Delete(meta);

                Debug.Log($"{LogPrefix} Removed stray google-services.json at '{path}' — the factory " +
                          $"Firebase config ('{canonicalRelPath}') is authoritative.");
            }
        }

#if UNITY_ANDROID
        private static void SetAndroidIcon(PlatformIconKind kind, Texture2D icon)
        {
            var slots = PlayerSettings.GetPlatformIcons(BuildTargetGroup.Android, kind);
            foreach (var slot in slots)
                slot.SetTextures(Enumerable.Repeat(icon, slot.maxLayerCount).ToArray());
            PlayerSettings.SetPlatformIcons(BuildTargetGroup.Android, kind, slots);
        }
#endif

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

#if UNITY_ANDROID
    // The com.google.gms.google-services Gradle plugin (required by the Crashlytics Gradle
    // plugin v3 — see launcherTemplate.gradle) expects a google-services.json sitting directly
    // next to the launcher module's build.gradle, the same way a normal (non-Unity) Android
    // project has it next to app/build.gradle. Unity's own Firebase integration doesn't put it
    // there — it only bakes google-services.json into string resources via its own
    // GenerateXmlFromGoogleServicesJson tool (see the "google-services.json gotcha" notes on
    // FactoryBuildPreprocessor above). This runs after Unity generates the Gradle project (the
    // launcher/unityLibrary folder structure exists by this point, unlike in
    // OnPreprocessBuild) and copies the same canonical json Unity's own tooling uses.
    //
    // IPostGenerateGradleAndroidProject is deprecated in favor of OnModifyAndroidProjectFiles /
    // AndroidProjectFilesModifier, but deliberately used here anyway: it's simpler, well
    // understood, and Unity keeps it functional for a long transition window — safer than the
    // newer, less-documented API for a first attempt at this specific wiring.
    public class FactoryGoogleServicesGradleStager : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // `path` is the generated unityLibrary module's directory; the launcher module is
            // its sibling.
            var launcherDir = Path.GetFullPath(Path.Combine(path, "..", "launcher"));
            if (!Directory.Exists(launcherDir))
            {
                Debug.LogWarning($"{FactoryBuildPreprocessor.LogPrefix} Expected launcher module " +
                    $"at '{launcherDir}' but it doesn't exist — skipping google-services.json " +
                    "staging. Crashlytics mapping upload will fail without it.");
                return;
            }

            var src = FindGoogleServicesJson();
            if (src == null)
            {
                Debug.LogWarning($"{FactoryBuildPreprocessor.LogPrefix} No 'google-services.json' found " +
                    "anywhere under Assets/ — skipping staging into the launcher module. Crashlytics " +
                    "mapping upload will fail without it.");
                return;
            }

            File.Copy(src, Path.Combine(launcherDir, "google-services.json"), true);
            Debug.Log($"{FactoryBuildPreprocessor.LogPrefix} Copied '{src}' into the " +
                $"launcher module ('{launcherDir}') for the google-services Gradle plugin.");
        }

        // Resolution order: Assets/Plugins/Android/google-services.json (the conventional
        // per-project location a developer would drop a real Firebase config for local/non-factory
        // builds) → Assets/google-services.json (the canonical location factory CI writes to,
        // per FactoryBuildPreprocessor.PurgeStrayGoogleServicesJson — that method purges every
        // other copy under Assets/, including Plugins/Android, so on a factory build this is
        // exactly what's left) → any other google-services.json anywhere under Assets/, as a last
        // resort for a project that hasn't adopted either convention yet.
        private static string FindGoogleServicesJson()
        {
            const string pluginsAndroidSrc = "Assets/Plugins/Android/google-services.json";
            if (File.Exists(pluginsAndroidSrc))
                return pluginsAndroidSrc;

            const string canonicalSrc = "Assets/google-services.json";
            if (File.Exists(canonicalSrc))
                return canonicalSrc;

            return Directory.EnumerateFiles("Assets", "google-services.json", SearchOption.AllDirectories)
                .FirstOrDefault();
        }
    }
#endif
}
#endif
