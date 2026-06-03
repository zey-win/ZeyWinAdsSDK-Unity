using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
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
        private const string GoogleMobileAdsSettingsPath = "Assets/GoogleMobileAds/Resources/GoogleMobileAdsSettings.asset";
        private const string GoogleMobileAdsAndroidManifestPath = "Assets/Plugins/Android/GoogleMobileAdsPlugin.androidlib/AndroidManifest.xml";
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";
        private const string ToolsNs = "http://schemas.android.com/tools";
        private const string AdMobMetaName = "com.google.android.gms.ads.APPLICATION_ID";

        [MenuItem("ZeyWinAds/Apply Project Configuration From Args", priority = 10)]
        public static void ApplyFromCommandLine()
        {
            var args = ParseArgs(Environment.GetCommandLineArgs());
            Apply(args);
        }

        public static void Apply(IDictionary<string, string> args)
        {
            var settings = ZeyWinAdsSettingsEditor.LoadOrCreate();

            TextMeshProBootstrap.EnsureInstalledAndConfigured();
            ApplyPlayerSettings(args);
            ApplyZeyWinSettings(settings, args);
            PatchGoogleMobileAdsSettings(settings);
            PatchGoogleMobileAdsAndroidManifest(settings);
            PatchAndroidManifest(settings, args);

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

            if (TryGetAny(args, out string packageId, "androidPackageId", "packageId", "bundleId", "applicationId"))
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
                settings.admobAppIdAndroid = appId;

            if (TryGetAnyOrEnv(args, out string banner, new[] { "bannerAdUnitId", "adMobBannerAdUnitId", "admobBanner", "admobAndroidBanner" }, "ADMOB_BANNER_AD_UNIT_ID"))
                settings.admobBannerAndroid = banner;

            if (TryGetAnyOrEnv(args, out string interstitial, new[] { "interstitialAdUnitId", "adMobInterstitialAdUnitId", "admobInterstitial", "admobAndroidInterstitial" }, "ADMOB_INTERSTITIAL_AD_UNIT_ID"))
                settings.admobInterstitialAndroid = interstitial;

            if (TryGetAnyOrEnv(args, out string rewarded, new[] { "rewardedAdUnitId", "adMobRewardedAdUnitId", "admobRewarded", "admobAndroidRewarded" }, "ADMOB_REWARDED_AD_UNIT_ID"))
                settings.admobRewardedAndroid = rewarded;
        }

        private static void PatchGoogleMobileAdsSettings(ZeyWinAdsSettings settings)
        {
            if (string.IsNullOrEmpty(settings.admobAppIdAndroid))
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
            if (string.IsNullOrEmpty(settings.admobAppIdAndroid))
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

            if (TryGetAny(args, out string packageId, "androidPackageId", "packageId", "bundleId", "applicationId"))
                manifest.SetAttribute("package", packageId);

            if (TryGetAny(args, out string versionName, "androidVersionName", "versionName"))
                manifest.SetAttribute("versionName", AndroidNs, versionName);

            if (TryGetAny(args, out string versionCode, "androidVersionCode", "versionCode"))
                manifest.SetAttribute("versionCode", AndroidNs, versionCode);

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

            if (application.HasAttribute("label", AndroidNs))
                application.RemoveAttribute("label", AndroidNs);

            application.SetAttribute("usesCleartextTraffic", AndroidNs, "true");

            if (!string.IsNullOrEmpty(settings.admobAppIdAndroid))
            {
                var meta = FindMetaData(application, AdMobMetaName);
                if (meta == null)
                {
                    meta = doc.CreateElement("meta-data");
                    application.AppendChild(meta);
                }

                meta.SetAttribute("name", AndroidNs, AdMobMetaName);
                meta.SetAttribute("value", AndroidNs, settings.admobAppIdAndroid);
            }

            string deepLinkScheme = ResolveDeepLinkScheme(args);
            if (!string.IsNullOrEmpty(deepLinkScheme))
            {
                EnsureMetaData(application, "zeywin.deeplink.scheme", deepLinkScheme);
                EnsureDeepLinkActivity(doc, application, deepLinkScheme);
            }
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
            var activity = FindActivity(application, "com.unity3d.player.UnityPlayerActivity") ?? FindLauncherActivity(application);
            if (activity == null)
            {
                activity = doc.CreateElement("activity");
                activity.SetAttribute("name", AndroidNs, "com.unity3d.player.UnityPlayerActivity");
                application.AppendChild(activity);
            }

            activity.SetAttribute("exported", AndroidNs, "true");
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
                "  <application android:label=\"@string/app_name\" android:usesCleartextTraffic=\"true\" />\n" +
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
