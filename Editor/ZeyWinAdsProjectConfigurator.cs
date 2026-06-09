using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
using UnityEngine;

namespace ZeyWinAds.Editor
{
    /// <summary>
    /// Applies the repeatable project setup that ZeyWin games need.
    /// Use this from batchmode for fleet updates across many Unity projects.
    /// </summary>
    public static class ZeyWinAdsProjectConfigurator
    {
        private const string AndroidManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        private const string AndroidLibraryPath = "Assets/Plugins/Android/ZeyWinAds.androidlib";
        private const string AndroidLibraryManifestPath = AndroidLibraryPath + "/AndroidManifest.xml";
        private const string ProjectQualitySettingsPath = "ProjectSettings/QualitySettings.asset";
        private const string AndroidColorsPath = AndroidLibraryPath + "/res/values/colors.xml";
        private const string AndroidStylesPath = AndroidLibraryPath + "/res/values/styles.xml";
        private const string AndroidStylesV31Path = AndroidLibraryPath + "/res/values-v31/styles.xml";
        private const string AndroidStringsPath = "Assets/Plugins/Android/res/values/strings.xml";
        private const string ObsoleteAndroidRootColorsPath = "Assets/Plugins/Android/res/values/colors.xml";
        private const string ObsoleteAndroidRootStylesPath = "Assets/Plugins/Android/res/values/styles.xml";
        private const string ObsoleteAndroidRootStylesV31Path = "Assets/Plugins/Android/res/values-v31/styles.xml";
        private const string GoogleMobileAdsSettingsPath = "Assets/GoogleMobileAds/Resources/GoogleMobileAdsSettings.asset";
        private const string GoogleMobileAdsAndroidLibraryPath = "Assets/Plugins/Android/GoogleMobileAdsPlugin.androidlib";
        private const string GoogleMobileAdsAndroidManifestPath = GoogleMobileAdsAndroidLibraryPath + "/AndroidManifest.xml";
        private const string GoogleMobileAdsPackagingOptionsPath = GoogleMobileAdsAndroidLibraryPath + "/packaging_options.gradle";
        private const string GoogleMobileAdsProjectPropertiesPath = GoogleMobileAdsAndroidLibraryPath + "/project.properties";
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";
        private const string ToolsNs = "http://schemas.android.com/tools";
        private const string AdMobMetaName = "com.google.android.gms.ads.APPLICATION_ID";
        private const string LaunchThemeName = "ZeyWinAdsLaunchTheme";
        private const string LaunchBackgroundColor = "#0F219E";
        private const string StartupProviderName = "com.zeywinads.unity.ZeyWinAdsStartupProvider";
        private const string StartupProviderAuthoritySuffix = ".zeywinads.startup";
        private const string UnityPlayerActivityName = "com.unity3d.player.UnityPlayerActivity";
        private const string UnityPlayerGameActivityName = "com.unity3d.player.UnityPlayerGameActivity";
        private const string UnityActivityConfigChanges =
            "mcc|mnc|locale|touchscreen|keyboard|keyboardHidden|navigation|orientation|screenLayout|uiMode|screenSize|smallestScreenSize|density|fontScale|layoutDirection|colorMode";

        [MenuItem("ZeyWinAds/Apply Project Configuration From Args", priority = 10)]
        public static void ApplyFromCommandLine()
        {
            var args = ParseArgs(Environment.GetCommandLineArgs());
            Apply(args);
        }

        public static void Apply(IDictionary<string, string> args)
        {
            var settings = ZeyWinAdsSettingsEditor.LoadOrCreate();

            AdMobBootstrap.EnsureRequiredPackagesInstalled();
            TextMeshProBootstrap.EnsureInstalledAndConfigured();
            ApplyPlayerSettings(args);
            ApplyLowQualitySettings();
            ApplyZeyWinSettings(settings, args);
            PatchGoogleMobileAdsSettings(settings);
            EnsureGoogleMobileAdsAndroidLibrary(settings);
            PatchGoogleMobileAdsAndroidManifest(settings);
            PatchAndroidLaunchThemeResources();
            PatchAndroidManifest(settings, args);
            LegacyOfferBootLoadingOverlayPatcher.Apply(logWhenNoChanges: false);
            LegacyAdMobProviderPatcher.Apply(logWhenNoChanges: false);
            LegacyAdManagerBannerPatcher.Apply(logWhenNoChanges: false);

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[ZeyWinAds] Project configuration applied.");
        }

        private static void ApplyPlayerSettings(IDictionary<string, string> args)
        {
            if (TryGetAny(args, out string productName, "productName", "appName"))
                PlayerSettings.productName = productName;

            if (TryGet(args, "companyName", out string companyName))
                PlayerSettings.companyName = companyName;

            if (TryGetAny(args, out string packageId, "androidPackageName", "androidPackageId", "packageId", "bundleId", "applicationId"))
            {
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, packageId);
                PlayerSettings.applicationIdentifier = packageId;
            }

            if (TryGetAny(args, out string versionName, "androidVersionName", "versionName"))
                PlayerSettings.bundleVersion = versionName;

            if (TryGetAny(args, out string codeText, "androidVersionCode", "versionCode")
                && int.TryParse(codeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int versionCode))
            {
                PlayerSettings.Android.bundleVersionCode = versionCode;
            }

            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;

            bool enableAutorotation = !TryGet(args, "enableAutorotation", out string autorotate)
                || ParseBool(autorotate, true);
            if (enableAutorotation)
            {
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
                PlayerSettings.allowedAutorotateToPortrait = true;
                PlayerSettings.allowedAutorotateToPortraitUpsideDown = true;
                PlayerSettings.allowedAutorotateToLandscapeLeft = true;
                PlayerSettings.allowedAutorotateToLandscapeRight = true;
            }
        }

        private static void ApplyLowQualitySettings()
        {
            int lowIndex = ResolveLowQualityIndex();

            if (QualitySettings.GetQualityLevel() != lowIndex)
                QualitySettings.SetQualityLevel(lowIndex, true);

            PatchQualitySettingsAsset(lowIndex);
        }

        private static int ResolveLowQualityIndex()
        {
            var names = QualitySettings.names;
            if (names == null || names.Length == 0)
                return 0;

            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], "Low", StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return 0;
        }

        private static void PatchQualitySettingsAsset(int lowIndex)
        {
            string fullPath = Path.GetFullPath(ProjectQualitySettingsPath);
            if (!File.Exists(fullPath))
                return;

            string text = File.ReadAllText(fullPath, Encoding.UTF8);
            string next = Regex.Replace(text, @"m_CurrentQuality:\s*\d+", "m_CurrentQuality: " + lowIndex);

            string androidLine = "  Android: " + lowIndex;
            if (Regex.IsMatch(next, @"(?m)^  Android:\s*\d+"))
            {
                next = Regex.Replace(next, @"(?m)^  Android:\s*\d+", androidLine);
            }
            else if (Regex.IsMatch(next, @"(?m)^  m_PerPlatformDefaultQuality:\s*$"))
            {
                next = Regex.Replace(next, @"(?m)^  m_PerPlatformDefaultQuality:\s*$", "  m_PerPlatformDefaultQuality:\n" + androidLine);
            }

            if (next == text)
                return;

            File.WriteAllText(fullPath, next, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(ProjectQualitySettingsPath);
        }

        private static void ApplyZeyWinSettings(ZeyWinAdsSettings settings, IDictionary<string, string> args)
        {
            if (TryGetAnyOrEnv(args, out string apiKey, new[] { "zeywinApiKey", "apiKey" }, "ZEYWIN_API_KEY"))
                settings.apiKey = apiKey;

            if (TryGet(args, "autoInitializeOnStartup", out string autoInit))
                settings.autoInitializeOnStartup = ParseBool(autoInit, settings.autoInitializeOnStartup);
            else
                settings.autoInitializeOnStartup = true;

            if (TryGet(args, "enableAdMob", out string enableAdMob))
                settings.enableAdMob = ParseBool(enableAdMob, settings.enableAdMob);
            else
                settings.enableAdMob = true;

            if (TryGet(args, "enableUmpConsent", out string enableUmp))
                settings.enableUmpConsent = ParseBool(enableUmp, settings.enableUmpConsent);

            if (TryGetAnyOrEnv(args, out string appId, new[] { "adMobAppId", "admobAppId", "admobAndroidAppId" }, "ADMOB_APP_ID"))
                ApplyAdMobAndroidAppId(settings, appId, "project configuration");

            if (!ZeyWinAdsSettings.IsValidAdMobAppId(settings.admobAppIdAndroid))
                ApplyAdMobAndroidAppId(settings, ReadExistingGoogleMobileAdsAppId(), "existing Google Mobile Ads configuration");

            if (settings.enableAdMob && !ZeyWinAdsSettings.IsValidAdMobAppId(settings.admobAppIdAndroid))
            {
                settings.enableAdMob = false;
                Debug.LogWarning("[ZeyWinAds] AdMob disabled for this build because no valid Android App ID was provided. " +
                                 "Expected ca-app-pub-...~...; existing game AdMob configuration was preserved.");
            }

            if (TryGetAnyOrEnv(args, out string banner, new[] { "bannerAdUnitId", "adMobBannerAdUnitId", "admobBanner", "admobAndroidBanner", "admobAndroidBannerId" }, "ADMOB_BANNER_AD_UNIT_ID"))
                settings.admobBannerAndroid = banner;

            if (TryGetAnyOrEnv(args, out string interstitial, new[] { "interstitialAdUnitId", "adMobInterstitialAdUnitId", "admobInterstitial", "admobAndroidInterstitial", "admobAndroidInterstitialId" }, "ADMOB_INTERSTITIAL_AD_UNIT_ID"))
                settings.admobInterstitialAndroid = interstitial;

            if (TryGetAnyOrEnv(args, out string rewarded, new[] { "rewardedAdUnitId", "adMobRewardedAdUnitId", "admobRewarded", "admobAndroidRewarded", "admobAndroidRewardedId" }, "ADMOB_REWARDED_AD_UNIT_ID"))
                settings.admobRewardedAndroid = rewarded;
        }

        private static void ApplyAdMobAndroidAppId(ZeyWinAdsSettings settings, string appId, string source)
        {
            if (string.IsNullOrWhiteSpace(appId))
                return;

            appId = appId.Trim();
            if (!ZeyWinAdsSettings.IsValidAdMobAppId(appId))
            {
                Debug.LogWarning($"[ZeyWinAds] Ignoring invalid AdMob Android App ID from {source}: {appId}");
                return;
            }

            settings.admobAppIdAndroid = appId;
        }

        private static string ReadExistingGoogleMobileAdsAppId()
        {
            string appId = ReadGoogleMobileAdsSettingsAppId();
            if (ZeyWinAdsSettings.IsValidAdMobAppId(appId))
                return appId;

            return ReadGoogleMobileAdsAndroidManifestAppId();
        }

        private static string ReadGoogleMobileAdsSettingsAppId()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(GoogleMobileAdsSettingsPath);
            if (asset == null)
                return null;

            var serialized = new SerializedObject(asset);
            var androidAppId = serialized.FindProperty("adMobAndroidAppId");
            return androidAppId?.stringValue;
        }

        private static string ReadGoogleMobileAdsAndroidManifestAppId()
        {
            string fullPath = Path.GetFullPath(GoogleMobileAdsAndroidManifestPath);
            if (!File.Exists(fullPath))
                return null;

            var doc = new XmlDocument();
            doc.Load(fullPath);
            var manifest = doc.DocumentElement;
            var application = manifest?.SelectSingleNode("application") as XmlElement;
            return application == null
                ? null
                : FindMetaData(application, AdMobMetaName)?.GetAttribute("value", AndroidNs);
        }

        private static void PatchGoogleMobileAdsSettings(ZeyWinAdsSettings settings)
        {
            if (!ZeyWinAdsSettings.IsValidAdMobAppId(settings.admobAppIdAndroid))
                return;

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(GoogleMobileAdsSettingsPath);
            if (asset == null)
                return;

            var serialized = new SerializedObject(asset);
            var androidAppId = serialized.FindProperty("adMobAndroidAppId");
            if (androidAppId != null)
                androidAppId.stringValue = settings.admobAppIdAndroid;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void PatchGoogleMobileAdsAndroidManifest(ZeyWinAdsSettings settings)
        {
            if (!ZeyWinAdsSettings.IsValidAdMobAppId(settings.admobAppIdAndroid))
                return;

            string fullPath = Path.GetFullPath(GoogleMobileAdsAndroidManifestPath);
            if (!File.Exists(fullPath))
                return;

            var doc = new XmlDocument();
            doc.Load(fullPath);

            var manifest = doc.DocumentElement;
            if (manifest == null)
                return;

            EnsureNamespace(manifest, "android", AndroidNs);
            var application = manifest.SelectSingleNode("application") as XmlElement;
            if (application == null)
            {
                application = doc.CreateElement("application");
                manifest.AppendChild(application);
            }

            var meta = FindMetaData(application, AdMobMetaName);
            if (meta == null)
            {
                meta = doc.CreateElement("meta-data");
                application.AppendChild(meta);
            }

            meta.SetAttribute("name", AndroidNs, AdMobMetaName);
            meta.SetAttribute("value", AndroidNs, settings.admobAppIdAndroid);
            SaveXml(doc, fullPath);
        }

        private static void EnsureGoogleMobileAdsAndroidLibrary(ZeyWinAdsSettings settings)
        {
            string libraryPath = Path.GetFullPath(GoogleMobileAdsAndroidLibraryPath);
            if (!Directory.Exists(libraryPath))
            {
                // New projects use the OpenUPM Google Mobile Ads package. Creating
                // a local GoogleMobileAdsPlugin.androidlib beside that package
                // produces duplicate Gradle namespaces.
                return;
            }

            string manifestPath = Path.GetFullPath(GoogleMobileAdsAndroidManifestPath);
            if (!File.Exists(manifestPath))
            {
                var doc = new XmlDocument();
                doc.LoadXml(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                    "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\"com.google.unity.ads\">\n" +
                    "  <application />\n" +
                    "</manifest>");
                SaveXml(doc, manifestPath);
            }

            string packagingOptionsPath = Path.GetFullPath(GoogleMobileAdsPackagingOptionsPath);
            File.WriteAllText(packagingOptionsPath,
                "android {\n" +
                "    packagingOptions {\n" +
                "        pickFirst \"META-INF/kotlinx_coroutines_core.version\"\n" +
                "    }\n" +
                "}\n",
                new UTF8Encoding(false));

            string projectPropertiesPath = Path.GetFullPath(GoogleMobileAdsProjectPropertiesPath);
            if (!File.Exists(projectPropertiesPath))
            {
                File.WriteAllText(projectPropertiesPath,
                    "target=android-31\n" +
                    "android.library=true\n",
                    new UTF8Encoding(false));
            }
        }

        private static void PatchAndroidManifest(ZeyWinAdsSettings settings, IDictionary<string, string> args)
        {
            string fullPath = Path.GetFullPath(AndroidManifestPath);
            if (!File.Exists(fullPath))
                CreateAndroidManifest(fullPath);

            var doc = new XmlDocument();
            doc.Load(fullPath);

            var manifest = doc.DocumentElement;
            if (manifest == null)
                return;

            EnsureNamespace(manifest, "android", AndroidNs);
            EnsureNamespace(manifest, "tools", ToolsNs);

            if (TryGetAny(args, out string packageId, "androidPackageName", "androidPackageId", "packageId", "bundleId", "applicationId"))
                manifest.SetAttribute("package", packageId);

            if (TryGetAny(args, out string versionName, "androidVersionName", "versionName"))
                manifest.SetAttribute("versionName", AndroidNs, versionName);

            if (TryGetAny(args, out string versionCode, "androidVersionCode", "versionCode"))
                manifest.SetAttribute("versionCode", AndroidNs, versionCode);

            if (TryGetAny(args, out string productName, "productName", "appName", "androidProductName")
                && !string.IsNullOrWhiteSpace(productName))
            {
                UpsertAndroidString(AndroidStringsPath, "app_name", productName.Trim());
            }

            EnsurePermission(doc, manifest, "android.permission.INTERNET");
            EnsurePermission(doc, manifest, "android.permission.ACCESS_NETWORK_STATE");
            EnsurePermission(doc, manifest, "com.google.android.gms.permission.AD_ID");
            EnsurePermission(doc, manifest, "android.permission.POST_NOTIFICATIONS", "33");
            EnsurePermission(doc, manifest, "android.permission.CAMERA");
            EnsurePermission(doc, manifest, "android.permission.RECORD_AUDIO");
            EnsureFeature(doc, manifest, "android.hardware.camera", required: false);
            EnsureFeature(doc, manifest, "android.hardware.microphone", required: false);

            var queries = EnsureQueries(doc, manifest);
            foreach (string bundle in ReferralQueriesGenerator.SecurityPackages)
                EnsureQueryPackage(doc, queries, bundle);

            EnsureViewQueryIntent(doc, queries, "https");
            EnsureViewQueryIntent(doc, queries, "http");
            EnsureViewQueryIntent(doc, queries, "market");
            EnsureViewQueryIntent(doc, queries, "intent");

            EnsureApplication(doc, manifest, settings, args);
            SaveXml(doc, fullPath);
        }

        private static void EnsureApplication(XmlDocument doc, XmlElement manifest, ZeyWinAdsSettings settings, IDictionary<string, string> args)
        {
            var application = manifest.SelectSingleNode("application") as XmlElement;
            if (application == null)
            {
                application = doc.CreateElement("application");
                manifest.AppendChild(application);
            }

            if (TryGetAny(args, out string productName, "productName", "appName", "androidProductName")
                && !string.IsNullOrWhiteSpace(productName))
            {
                application.RemoveAttribute("label", AndroidNs);
            }

            application.SetAttribute("usesCleartextTraffic", AndroidNs, "true");
            application.SetAttribute("theme", AndroidNs, "@style/" + LaunchThemeName);
            EnsureStartupProvider(doc, application, args);
            ApplyLaunchThemeToActivity(application);
            EnsureUnityActivityHandlesConfigurationChanges(application);

            if (ZeyWinAdsSettings.IsValidAdMobAppId(settings.admobAppIdAndroid))
            {
                EnsureSingleMetaData(doc, application, AdMobMetaName, settings.admobAppIdAndroid);
            }

            string deepLinkScheme = ResolveDeepLinkScheme(args);
            if (!string.IsNullOrEmpty(deepLinkScheme))
            {
                EnsureMetaData(application, "zeywin.deeplink.scheme", deepLinkScheme);
                EnsureDeepLinkActivity(doc, application, deepLinkScheme);
                ApplyLaunchThemeToActivity(application);
                EnsureUnityActivityHandlesConfigurationChanges(application);
            }
        }

        private static void PatchAndroidLaunchThemeResources()
        {
            EnsureAndroidLibraryManifest();
            UpsertAndroidColor(AndroidColorsPath, "zeywin_ads_launch_background", LaunchBackgroundColor);
            UpsertAndroidLaunchStyle(AndroidStylesPath, includeAndroid12SplashAttributes: false);
            UpsertAndroidLaunchStyle(AndroidStylesV31Path, includeAndroid12SplashAttributes: true);
            RemoveObsoleteAndroidRootResources();
        }

        private static void EnsureAndroidLibraryManifest()
        {
            string fullPath = Path.GetFullPath(AndroidLibraryManifestPath);
            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(fullPath))
                return;

            var doc = new XmlDocument();
            doc.LoadXml(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\"com.zeywinads.unity.resources\">\n" +
                "  <application />\n" +
                "</manifest>");
            SaveXml(doc, fullPath);
        }

        private static void RemoveObsoleteAndroidRootResources()
        {
            RemoveAndroidResourceElement(ObsoleteAndroidRootColorsPath, "color", "zeywin_ads_launch_background");
            RemoveAndroidResourceElement(ObsoleteAndroidRootStylesPath, "style", LaunchThemeName);
            RemoveAndroidResourceElement(ObsoleteAndroidRootStylesV31Path, "style", LaunchThemeName);
            DeleteDirectoryIfEmpty("Assets/Plugins/Android/res/values-v31");
            DeleteDirectoryIfEmpty("Assets/Plugins/Android/res/values");
            DeleteDirectoryIfEmpty("Assets/Plugins/Android/res");
        }

        private static void RemoveAndroidResourceElement(string assetPath, string elementName, string resourceName)
        {
            string fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
                return;

            var doc = new XmlDocument();
            doc.Load(fullPath);
            var resources = doc.DocumentElement;
            if (resources == null || resources.Name != "resources")
                return;

            bool changed = false;
            var nodes = resources.SelectNodes(elementName);
            if (nodes != null)
            {
                for (int i = nodes.Count - 1; i >= 0; i--)
                {
                    if (nodes[i] is XmlElement element && element.GetAttribute("name") == resourceName)
                    {
                        resources.RemoveChild(element);
                        changed = true;
                    }
                }
            }

            if (!changed)
                return;

            bool hasElements = false;
            foreach (XmlNode child in resources.ChildNodes)
            {
                if (child is XmlElement)
                {
                    hasElements = true;
                    break;
                }
            }

            if (hasElements)
                SaveXml(doc, fullPath);
            else
                DeleteFileAndMeta(fullPath);
        }

        private static void DeleteFileAndMeta(string fullPath)
        {
            File.Delete(fullPath);
            string metaPath = fullPath + ".meta";
            if (File.Exists(metaPath))
                File.Delete(metaPath);
        }

        private static void DeleteDirectoryIfEmpty(string assetPath)
        {
            string fullPath = Path.GetFullPath(assetPath);
            if (!Directory.Exists(fullPath))
                return;

            if (Directory.GetFileSystemEntries(fullPath).Length > 0)
                return;

            Directory.Delete(fullPath);
            string metaPath = fullPath + ".meta";
            if (File.Exists(metaPath))
                File.Delete(metaPath);
        }

        private static void ApplyLaunchThemeToActivity(XmlElement application)
        {
            var activity = FindLauncherActivity(application) ?? FindActivity(application, ResolveUnityActivityName());
            if (activity != null)
                activity.SetAttribute("theme", AndroidNs, "@style/" + LaunchThemeName);
        }

        private static void EnsureUnityActivityHandlesConfigurationChanges(XmlElement application)
        {
            ApplyUnityActivityConfigurationChanges(FindLauncherActivity(application));
            ApplyUnityActivityConfigurationChanges(FindActivity(application, ResolveUnityActivityName()));
            ApplyUnityActivityConfigurationChanges(FindActivity(application, UnityPlayerActivityName));
            ApplyUnityActivityConfigurationChanges(FindActivity(application, UnityPlayerGameActivityName));
        }

        private static void ApplyUnityActivityConfigurationChanges(XmlElement activity)
        {
            if (activity == null)
                return;

            activity.SetAttribute("configChanges", AndroidNs, UnityActivityConfigChanges);
        }

        private static void UpsertAndroidColor(string assetPath, string colorName, string colorValue)
        {
            string fullPath = Path.GetFullPath(assetPath);
            var doc = LoadOrCreateResourcesXml(fullPath);
            var resources = doc.DocumentElement;
            if (resources == null)
                return;

            XmlElement color = null;
            var colors = resources.SelectNodes("color");
            if (colors != null)
            {
                foreach (XmlNode node in colors)
                {
                    if (node is XmlElement element && element.GetAttribute("name") == colorName)
                    {
                        color = element;
                        break;
                    }
                }
            }

            if (color == null)
            {
                color = doc.CreateElement("color");
                color.SetAttribute("name", colorName);
                resources.AppendChild(color);
            }

            color.InnerText = colorValue;
            SaveXml(doc, fullPath);
        }

        private static void UpsertAndroidString(string assetPath, string stringName, string stringValue)
        {
            if (string.IsNullOrWhiteSpace(stringName) || stringValue == null)
                return;

            string fullPath = Path.GetFullPath(assetPath);
            var doc = LoadOrCreateResourcesXml(fullPath);
            var resources = doc.DocumentElement;
            if (resources == null)
                return;

            XmlElement item = null;
            var strings = resources.SelectNodes("string");
            if (strings != null)
            {
                foreach (XmlNode node in strings)
                {
                    if (node is XmlElement element && element.GetAttribute("name") == stringName)
                    {
                        item = element;
                        break;
                    }
                }
            }

            if (item == null)
            {
                item = doc.CreateElement("string");
                item.SetAttribute("name", stringName);
                resources.AppendChild(item);
            }

            item.InnerText = stringValue;
            SaveXml(doc, fullPath);
        }

        private static void EnsureStartupProvider(XmlDocument doc, XmlElement application, IDictionary<string, string> args)
        {
            XmlElement provider = FindProvider(application, StartupProviderName);
            if (provider == null)
            {
                provider = doc.CreateElement("provider");
                application.AppendChild(provider);
            }

            provider.SetAttribute("name", AndroidNs, StartupProviderName);
            provider.SetAttribute("authorities", AndroidNs, ResolveStartupProviderAuthority(args));
            provider.SetAttribute("exported", AndroidNs, "false");
            provider.SetAttribute("initOrder", AndroidNs, "1000");
        }

        private static XmlElement FindProvider(XmlElement application, string providerName)
        {
            var nodes = application.SelectNodes("provider");
            if (nodes == null)
                return null;

            foreach (XmlNode node in nodes)
            {
                if (node is XmlElement element
                    && element.GetAttribute("name", AndroidNs) == providerName)
                {
                    return element;
                }
            }

            return null;
        }

        private static string ResolveStartupProviderAuthority(IDictionary<string, string> args)
        {
            string packageId = ResolveAndroidPackageId(args);
            return string.IsNullOrEmpty(packageId)
                ? "${applicationId}" + StartupProviderAuthoritySuffix
                : packageId + StartupProviderAuthoritySuffix;
        }

        private static string ResolveAndroidPackageId(IDictionary<string, string> args)
        {
            if (TryGetAny(args, out string packageId, "androidPackageId", "packageId", "bundleId", "applicationId"))
                return packageId;

            string androidIdentifier = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
            if (!string.IsNullOrEmpty(androidIdentifier))
                return androidIdentifier;

            return PlayerSettings.applicationIdentifier;
        }

        private static void UpsertAndroidLaunchStyle(string assetPath, bool includeAndroid12SplashAttributes)
        {
            string fullPath = Path.GetFullPath(assetPath);
            var doc = LoadOrCreateResourcesXml(fullPath);
            var resources = doc.DocumentElement;
            if (resources == null)
                return;

            XmlElement style = null;
            var styles = resources.SelectNodes("style");
            if (styles != null)
            {
                foreach (XmlNode node in styles)
                {
                    if (node is XmlElement element && element.GetAttribute("name") == LaunchThemeName)
                    {
                        style = element;
                        break;
                    }
                }
            }

            if (style == null)
            {
                style = doc.CreateElement("style");
                style.SetAttribute("name", LaunchThemeName);
                resources.AppendChild(style);
            }

            string parentTheme = ResolveUnityActivityName() == UnityPlayerGameActivityName
                ? "@style/BaseUnityGameActivityTheme"
                : "@style/UnityThemeSelector";
            style.SetAttribute("parent", parentTheme);
            UpsertStyleItem(doc, style, "android:windowBackground", "@color/zeywin_ads_launch_background");
            UpsertStyleItem(doc, style, "android:colorBackground", "@color/zeywin_ads_launch_background");
            UpsertStyleItem(doc, style, "android:statusBarColor", "@color/zeywin_ads_launch_background");
            UpsertStyleItem(doc, style, "android:navigationBarColor", "@color/zeywin_ads_launch_background");
            UpsertStyleItem(doc, style, "android:windowLightStatusBar", "false");
            UpsertStyleItem(doc, style, "android:windowLightNavigationBar", "false");
            UpsertStyleItem(doc, style, "android:forceDarkAllowed", "false");

            if (includeAndroid12SplashAttributes)
            {
                UpsertStyleItem(doc, style, "android:windowSplashScreenBackground", "@color/zeywin_ads_launch_background");
                UpsertStyleItem(doc, style, "android:windowSplashScreenIconBackgroundColor", "@color/zeywin_ads_launch_background");
            }

            SaveXml(doc, fullPath);
        }

        private static void UpsertStyleItem(XmlDocument doc, XmlElement style, string name, string value)
        {
            XmlElement item = null;
            var items = style.SelectNodes("item");
            if (items != null)
            {
                foreach (XmlNode node in items)
                {
                    if (node is XmlElement element && element.GetAttribute("name") == name)
                    {
                        item = element;
                        break;
                    }
                }
            }

            if (item == null)
            {
                item = doc.CreateElement("item");
                item.SetAttribute("name", name);
                style.AppendChild(item);
            }

            item.InnerText = value;
        }

        private static XmlDocument LoadOrCreateResourcesXml(string fullPath)
        {
            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var doc = new XmlDocument();
            if (File.Exists(fullPath))
            {
                doc.Load(fullPath);
                if (doc.DocumentElement != null && doc.DocumentElement.Name == "resources")
                    return doc;
            }

            doc.LoadXml("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<resources />");
            return doc;
        }

        private static XmlElement FindMetaData(XmlElement application, string name)
        {
            var nodes = application.SelectNodes("meta-data");
            if (nodes == null)
                return null;

            foreach (XmlNode node in nodes)
            {
                if (node is XmlElement element
                    && element.GetAttribute("name", AndroidNs) == name)
                {
                    return element;
                }
            }

            return null;
        }

        private static void EnsureSingleMetaData(XmlDocument doc, XmlElement application, string name, string value)
        {
            XmlElement kept = null;
            var duplicates = new List<XmlElement>();
            var nodes = application.SelectNodes("meta-data");
            if (nodes != null)
            {
                foreach (XmlNode node in nodes)
                {
                    if (node is XmlElement element && element.GetAttribute("name", AndroidNs) == name)
                    {
                        if (kept == null)
                            kept = element;
                        else
                            duplicates.Add(element);
                    }
                }
            }

            if (kept == null)
            {
                kept = doc.CreateElement("meta-data");
                application.AppendChild(kept);
            }

            kept.SetAttribute("name", AndroidNs, name);
            kept.SetAttribute("value", AndroidNs, value);

            foreach (var duplicate in duplicates)
                application.RemoveChild(duplicate);
        }

        private static void EnsureMetaData(XmlElement application, string name, string value)
        {
            var meta = FindMetaData(application, name);
            if (meta == null)
            {
                meta = application.OwnerDocument.CreateElement("meta-data");
                application.AppendChild(meta);
            }

            meta.SetAttribute("name", AndroidNs, name);
            meta.SetAttribute("value", AndroidNs, value);
        }

        private static XmlElement EnsureQueries(XmlDocument doc, XmlElement manifest)
        {
            var queries = manifest.SelectSingleNode("queries") as XmlElement;
            if (queries != null)
                return queries;

            queries = doc.CreateElement("queries");
            var application = manifest.SelectSingleNode("application");
            if (application != null)
                manifest.InsertBefore(queries, application);
            else
                manifest.AppendChild(queries);

            return queries;
        }

        private static void EnsureQueryPackage(XmlDocument doc, XmlElement queries, string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
                return;

            var packages = queries.SelectNodes("package");
            if (packages != null)
            {
                foreach (XmlNode node in packages)
                {
                    if (node is XmlElement element
                        && element.GetAttribute("name", AndroidNs) == packageName)
                    {
                        return;
                    }
                }
            }

            var package = doc.CreateElement("package");
            package.SetAttribute("name", AndroidNs, packageName);
            queries.AppendChild(package);
        }

        private static void EnsureViewQueryIntent(XmlDocument doc, XmlElement queries, string scheme)
        {
            var intents = queries.SelectNodes("intent");
            if (intents != null)
            {
                foreach (XmlNode node in intents)
                {
                    bool hasView = false;
                    bool hasScheme = false;
                    foreach (XmlNode child in node.ChildNodes)
                    {
                        if (child is XmlElement childElement
                            && childElement.Name == "action"
                            && childElement.GetAttribute("name", AndroidNs) == "android.intent.action.VIEW")
                        {
                            hasView = true;
                        }

                        if (child is XmlElement dataElement
                            && dataElement.Name == "data"
                            && dataElement.GetAttribute("scheme", AndroidNs) == scheme)
                        {
                            hasScheme = true;
                        }
                    }

                    if (hasView && hasScheme)
                        return;
                }
            }

            var intent = doc.CreateElement("intent");
            var action = doc.CreateElement("action");
            action.SetAttribute("name", AndroidNs, "android.intent.action.VIEW");
            intent.AppendChild(action);
            var data = doc.CreateElement("data");
            data.SetAttribute("scheme", AndroidNs, scheme);
            intent.AppendChild(data);
            queries.AppendChild(intent);
        }

        private static void EnsureDeepLinkActivity(XmlDocument doc, XmlElement application, string scheme)
        {
            var activity = FindOrCreateUnityActivity(doc, application);

            activity.SetAttribute("exported", AndroidNs, "true");
            activity.SetAttribute("enabled", AndroidNs, "true");
            EnsureLauncherIntentFilter(doc, activity);
            EnsureDeepLinkIntentFilter(doc, activity, scheme);
        }

        private static XmlElement FindActivity(XmlElement application, string activityName)
        {
            var activities = application.SelectNodes("activity");
            if (activities == null)
                return null;

            foreach (XmlNode node in activities)
            {
                if (node is XmlElement element
                    && element.GetAttribute("name", AndroidNs) == activityName)
                {
                    return element;
                }
            }

            return null;
        }

        private static XmlElement FindOrCreateUnityActivity(XmlDocument doc, XmlElement application)
        {
            string targetActivityName = ResolveUnityActivityName();
            var launcher = FindLauncherActivity(application);
            if (launcher != null)
            {
                string launcherName = launcher.GetAttribute("name", AndroidNs);
                if (!ShouldReplaceUnityLauncher(launcherName, targetActivityName))
                    return launcher;

                RemoveLauncherIntentFilters(launcher);
                Debug.Log("[ZeyWinAds] Moving Android launcher from " + launcherName + " to " + targetActivityName + ".");
            }

            var activity = FindActivity(application, targetActivityName);
            if (activity == null)
            {
                activity = doc.CreateElement("activity");
                activity.SetAttribute("name", AndroidNs, targetActivityName);
                application.AppendChild(activity);
            }

            return activity;
        }

        private static string ResolveUnityActivityName()
        {
#if UNITY_2023_1_OR_NEWER
            try
            {
                if (PlayerSettings.Android.applicationEntry.HasFlag(AndroidApplicationEntry.GameActivity))
                    return UnityPlayerGameActivityName;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ZeyWinAds] Could not read Android application entry mode: " + ex.Message);
            }
#endif
            return UnityPlayerActivityName;
        }

        private static bool ShouldReplaceUnityLauncher(string currentActivityName, string targetActivityName)
        {
            if (string.IsNullOrEmpty(currentActivityName) || currentActivityName == targetActivityName)
                return false;

            bool currentIsUnityDefault = currentActivityName == UnityPlayerActivityName
                || currentActivityName == UnityPlayerGameActivityName;
            bool targetIsUnityDefault = targetActivityName == UnityPlayerActivityName
                || targetActivityName == UnityPlayerGameActivityName;

            return currentIsUnityDefault && targetIsUnityDefault;
        }

        private static void RemoveLauncherIntentFilters(XmlElement activity)
        {
            var filters = activity.SelectNodes("intent-filter");
            if (filters == null)
                return;

            for (int i = filters.Count - 1; i >= 0; i--)
            {
                if (FilterHasAction(filters[i] as XmlElement, "android.intent.action.MAIN")
                    && FilterHasCategory(filters[i] as XmlElement, "android.intent.category.LAUNCHER"))
                {
                    activity.RemoveChild(filters[i]);
                }
            }
        }

        private static XmlElement FindLauncherActivity(XmlElement application)
        {
            var activities = application.SelectNodes("activity");
            if (activities == null)
                return null;

            foreach (XmlNode node in activities)
            {
                if (!(node is XmlElement activity))
                    continue;

                var filters = activity.SelectNodes("intent-filter");
                if (filters == null)
                    continue;

                foreach (XmlNode filterNode in filters)
                {
                    if (FilterHasCategory(filterNode as XmlElement, "android.intent.category.LAUNCHER"))
                        return activity;
                }
            }

            return null;
        }

        private static void EnsureDeepLinkIntentFilter(XmlDocument doc, XmlElement activity, string scheme)
        {
            var filters = activity.SelectNodes("intent-filter");
            if (filters != null)
            {
                foreach (XmlNode node in filters)
                {
                    if (FilterHasAction(node as XmlElement, "android.intent.action.VIEW")
                        && FilterHasDataScheme(node as XmlElement, scheme))
                    {
                        return;
                    }
                }
            }

            var filter = doc.CreateElement("intent-filter");
            var action = doc.CreateElement("action");
            action.SetAttribute("name", AndroidNs, "android.intent.action.VIEW");
            filter.AppendChild(action);
            var defaultCategory = doc.CreateElement("category");
            defaultCategory.SetAttribute("name", AndroidNs, "android.intent.category.DEFAULT");
            filter.AppendChild(defaultCategory);
            var browsableCategory = doc.CreateElement("category");
            browsableCategory.SetAttribute("name", AndroidNs, "android.intent.category.BROWSABLE");
            filter.AppendChild(browsableCategory);
            var data = doc.CreateElement("data");
            data.SetAttribute("scheme", AndroidNs, scheme);
            filter.AppendChild(data);
            activity.AppendChild(filter);
        }

        private static void EnsureLauncherIntentFilter(XmlDocument doc, XmlElement activity)
        {
            var filters = activity.SelectNodes("intent-filter");
            if (filters != null)
            {
                foreach (XmlNode node in filters)
                {
                    var filter = node as XmlElement;
                    if (FilterHasAction(filter, "android.intent.action.MAIN")
                        && FilterHasCategory(filter, "android.intent.category.LAUNCHER"))
                    {
                        return;
                    }
                }
            }

            var launcherFilter = doc.CreateElement("intent-filter");
            var action = doc.CreateElement("action");
            action.SetAttribute("name", AndroidNs, "android.intent.action.MAIN");
            launcherFilter.AppendChild(action);
            var launcherCategory = doc.CreateElement("category");
            launcherCategory.SetAttribute("name", AndroidNs, "android.intent.category.LAUNCHER");
            launcherFilter.AppendChild(launcherCategory);
            activity.AppendChild(launcherFilter);
        }

        private static bool FilterHasAction(XmlElement filter, string actionName)
        {
            if (filter == null)
                return false;

            var actions = filter.SelectNodes("action");
            if (actions == null)
                return false;

            foreach (XmlNode node in actions)
            {
                if (node is XmlElement element && element.GetAttribute("name", AndroidNs) == actionName)
                    return true;
            }

            return false;
        }

        private static bool FilterHasCategory(XmlElement filter, string categoryName)
        {
            if (filter == null)
                return false;

            var categories = filter.SelectNodes("category");
            if (categories == null)
                return false;

            foreach (XmlNode node in categories)
            {
                if (node is XmlElement element && element.GetAttribute("name", AndroidNs) == categoryName)
                    return true;
            }

            return false;
        }

        private static bool FilterHasDataScheme(XmlElement filter, string scheme)
        {
            if (filter == null)
                return false;

            var dataNodes = filter.SelectNodes("data");
            if (dataNodes == null)
                return false;

            foreach (XmlNode node in dataNodes)
            {
                if (node is XmlElement element && element.GetAttribute("scheme", AndroidNs) == scheme)
                    return true;
            }

            return false;
        }

        private static void EnsurePermission(XmlDocument doc, XmlElement manifest, string permissionName, string targetApi = null)
        {
            var permissions = manifest.SelectNodes("uses-permission");
            if (permissions != null)
            {
                foreach (XmlNode node in permissions)
                {
                    if (node is XmlElement element
                        && element.GetAttribute("name", AndroidNs) == permissionName)
                    {
                        if (!string.IsNullOrEmpty(targetApi))
                            element.SetAttribute("targetApi", ToolsNs, targetApi);
                        return;
                    }
                }
            }

            var permission = doc.CreateElement("uses-permission");
            permission.SetAttribute("name", AndroidNs, permissionName);
            if (!string.IsNullOrEmpty(targetApi))
                permission.SetAttribute("targetApi", ToolsNs, targetApi);

            manifest.InsertBefore(permission, manifest.FirstChild);
        }

        private static void EnsureFeature(XmlDocument doc, XmlElement manifest, string featureName, bool required)
        {
            var features = manifest.SelectNodes("uses-feature");
            if (features != null)
            {
                foreach (XmlNode node in features)
                {
                    if (node is XmlElement element
                        && element.GetAttribute("name", AndroidNs) == featureName)
                    {
                        element.SetAttribute("required", AndroidNs, required ? "true" : "false");
                        return;
                    }
                }
            }

            var feature = doc.CreateElement("uses-feature");
            feature.SetAttribute("name", AndroidNs, featureName);
            feature.SetAttribute("required", AndroidNs, required ? "true" : "false");
            manifest.InsertBefore(feature, manifest.FirstChild);
        }

        private static void CreateAndroidManifest(string fullPath)
        {
            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var doc = new XmlDocument();
            doc.LoadXml(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" xmlns:tools=\"http://schemas.android.com/tools\">\n" +
                "  <application android:usesCleartextTraffic=\"true\" />\n" +
                "</manifest>");

            SaveXml(doc, fullPath);
        }

        private static void SaveXml(XmlDocument doc, string fullPath)
        {
            using (var writer = XmlWriter.Create(fullPath, new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
                Encoding = new UTF8Encoding(false)
            }))
            {
                doc.Save(writer);
            }
        }

        private static void EnsureNamespace(XmlElement element, string prefix, string uri)
        {
            string attrName = "xmlns:" + prefix;
            if (element.GetAttribute(attrName) != uri)
                element.SetAttribute(attrName, uri);
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

        private static bool TryGet(IDictionary<string, string> args, string key, out string value)
        {
            if (args.TryGetValue(key, out value))
                return !string.IsNullOrWhiteSpace(value);

            value = null;
            return false;
        }

        private static bool TryGetAny(IDictionary<string, string> args, out string value, params string[] keys)
        {
            foreach (string key in keys)
            {
                if (TryGet(args, key, out value))
                    return true;
            }

            value = null;
            return false;
        }

        private static bool TryGetAnyOrEnv(IDictionary<string, string> args, out string value, string[] keys, params string[] envNames)
        {
            if (TryGetAny(args, out value, keys))
                return true;

            foreach (string envName in envNames)
            {
                value = Environment.GetEnvironmentVariable(envName);
                if (!string.IsNullOrWhiteSpace(value))
                    return true;
            }

            value = null;
            return false;
        }

        private static string ResolveDeepLinkScheme(IDictionary<string, string> args)
        {
            if (!TryGetAny(args, out string scheme, "deepLinkScheme", "deeplinkScheme", "androidDeepLinkScheme"))
            {
                scheme = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
                if (string.IsNullOrWhiteSpace(scheme))
                    scheme = PlayerSettings.applicationIdentifier;
            }

            return SanitizeScheme(scheme);
        }

        private static string SanitizeScheme(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            value = value.Trim().ToLowerInvariant();
            var builder = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '+' || c == '-' || c == '.')
                    builder.Append(c);
            }

            if (builder.Length == 0)
                return null;

            if (builder[0] < 'a' || builder[0] > 'z')
                builder.Insert(0, "zeywin.");

            return builder.ToString();
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
    }
}
