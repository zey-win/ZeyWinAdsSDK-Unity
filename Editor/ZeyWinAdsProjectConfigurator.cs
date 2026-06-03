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

            EnsurePermission(doc, manifest, "com.google.android.gms.permission.AD_ID");
            EnsurePermission(doc, manifest, "android.permission.POST_NOTIFICATIONS", "33");
            EnsurePermission(doc, manifest, "android.permission.CAMERA");

            var queries = EnsureQueries(doc, manifest);
            foreach (string bundle in ReferralQueriesGenerator.SecurityPackages)
                EnsureQueryPackage(doc, queries, bundle);

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
