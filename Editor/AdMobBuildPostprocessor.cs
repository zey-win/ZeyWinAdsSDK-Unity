using System;
using System.IO;
using System.Collections.Generic;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace ZeyWinAds.Editor
{
    /// <summary>
    /// Writes the AdMob App ID into AndroidManifest.xml and Info.plist at build time.
    /// Source of truth is the user's ZeyWinAdsSettings asset — they don't need to
    /// also fill GoogleMobileAdsSettings.
    /// </summary>
    public class AdMobBuildPostprocessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport, IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 100;

        // Path that ZeyWinAdsAndroidManifestPatcher writes/edits.
        private const string AndroidManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        private const string AdMobMetaName = "com.google.android.gms.ads.APPLICATION_ID";
        private const string SafeAdMobTestAppIdAndroid = "ca-app-pub-3940256099942544~3347511713";
        private const string UnityPlayerActivityName = "com.unity3d.player.UnityPlayerActivity";
        private const string UnityPlayerGameActivityName = "com.unity3d.player.UnityPlayerGameActivity";
        private const string UnityActivityConfigChanges =
            "mcc|mnc|locale|touchscreen|keyboard|keyboardHidden|navigation|orientation|screenLayout|uiMode|screenSize|smallestScreenSize|density|fontScale|layoutDirection|colorMode";
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";
        private const string ToolsNs = "http://schemas.android.com/tools";

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = ZeyWinAdsSettingsEditor.LoadOrCreate();

            if (report.summary.platform == BuildTarget.Android)
            {
                EnsureAndroidManifestSecurityQueries();

                if (settings != null && settings.enableAdMob)
                    PatchAndroidManifest(ResolveAdMobAppIdForManifest(settings.admobAppIdAndroid));
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            var settings = ZeyWinAdsSettings.Load() ?? ZeyWinAdsSettingsEditor.LoadOrCreate();

#if UNITY_IOS
            if (report.summary.platform == BuildTarget.iOS)
            {
                PatchInfoPlistForWebViewPermissions(report.summary.outputPath);
            }
#endif

            if (settings == null || !settings.enableAdMob)
                return;

#if UNITY_IOS
            if (report.summary.platform == BuildTarget.iOS)
            {
                PatchInfoPlist(report.summary.outputPath, settings.admobAppIdIOS, settings);
            }
#endif
        }

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var settings = ZeyWinAdsSettings.Load() ?? ZeyWinAdsSettingsEditor.LoadOrCreate();
            string appId = settings != null && settings.enableAdMob
                ? ResolveAdMobAppIdForManifest(settings.admobAppIdAndroid)
                : null;
            string productName = Environment.GetEnvironmentVariable("ANDROID_PRODUCT_NAME");

            foreach (string manifestPath in EnumerateGeneratedManifestPaths(path))
            {
                PatchGeneratedAndroidManifest(manifestPath, appId, productName);
            }
        }

        private static void PatchAndroidManifest(string appId)
        {
            if (!ZeyWinAdsSettings.IsValidAdMobAppId(appId))
            {
                Debug.LogWarning("[ZeyWinAds] AdMob Android App ID is invalid — manifest not patched. " +
                                 "Expected ca-app-pub-...~..., existing game AdMob config is preserved.");
                return;
            }

            string fullPath = Path.GetFullPath(AndroidManifestPath);
            if (!File.Exists(fullPath))
            {
                CreateAndroidManifest(fullPath);
            }

            var doc = new XmlDocument();
            doc.Load(fullPath);

            XmlElement manifest = doc.DocumentElement;
            if (manifest == null)
                return;

            XmlElement application = manifest.SelectSingleNode("application") as XmlElement;
            if (application == null)
            {
                application = doc.CreateElement("application");
                manifest.AppendChild(application);
            }

            XmlElement meta = null;
            var metaNodes = application.SelectNodes("meta-data");
            if (metaNodes != null)
            {
                foreach (XmlNode node in metaNodes)
                {
                    if (node is XmlElement element
                        && element.Attributes?.GetNamedItem("name", AndroidNs)?.Value == AdMobMetaName)
                    {
                        meta = element;
                        break;
                    }
                }
            }

            if (meta == null)
            {
                meta = doc.CreateElement("meta-data");
                application.AppendChild(meta);
            }

            meta.SetAttribute("name", AndroidNs, AdMobMetaName);
            meta.SetAttribute("value", AndroidNs, appId);

            SaveXml(doc, fullPath);
        }

        private static string ResolveAdMobAppIdForManifest(string appId)
        {
            if (ZeyWinAdsSettings.IsValidAdMobAppId(appId))
                return appId.Trim();

            Debug.LogWarning("[ZeyWinAds] AdMob is enabled but no valid Android App ID was provided; using the safe Google test App ID for this build.");
            return SafeAdMobTestAppIdAndroid;
        }

        private static IEnumerable<string> EnumerateGeneratedManifestPaths(string path)
        {
            if (string.IsNullOrEmpty(path))
                yield break;

            string fullPath = Path.GetFullPath(path);
            var candidates = new List<string>
            {
                Path.Combine(fullPath, "src", "main", "AndroidManifest.xml"),
                Path.Combine(fullPath, "unityLibrary", "src", "main", "AndroidManifest.xml"),
                Path.Combine(fullPath, "launcher", "src", "main", "AndroidManifest.xml")
            };

            var parent = Directory.GetParent(fullPath)?.FullName;
            if (!string.IsNullOrEmpty(parent))
            {
                candidates.Add(Path.Combine(parent, "unityLibrary", "src", "main", "AndroidManifest.xml"));
                candidates.Add(Path.Combine(parent, "launcher", "src", "main", "AndroidManifest.xml"));
            }

            var seen = new HashSet<string>();
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrEmpty(candidate) || !seen.Add(candidate))
                    continue;

                if (File.Exists(candidate))
                    yield return candidate;
            }
        }

        private static void PatchGeneratedAndroidManifest(string fullPath, string appId, string productName)
        {
            var doc = new XmlDocument();
            doc.Load(fullPath);

            XmlElement manifest = doc.DocumentElement;
            if (manifest == null)
                return;

            XmlElement application = manifest.SelectSingleNode("application") as XmlElement;
            if (application == null)
            {
                application = doc.CreateElement("application");
                manifest.AppendChild(application);
            }

            if (ZeyWinAdsSettings.IsValidAdMobAppId(appId))
                EnsureMetaData(doc, application, AndroidNs, AdMobMetaName, appId.Trim());

            if (!string.IsNullOrWhiteSpace(productName))
            {
                UpsertGeneratedStringResource(fullPath, "app_name", productName.Trim());

                if (IsUnityLibraryManifest(fullPath))
                {
                    application.RemoveAttribute("label", AndroidNs);
                    RemoveToolsReplace(application, "android:label");
                }
                else
                {
                    EnsureToolsNamespace(manifest);
                    application.SetAttribute("label", AndroidNs, "@string/app_name");
                    EnsureToolsReplace(application, "android:label");
                }
            }

            XmlElement activity = FindOrCreateUnityActivity(doc, application, AndroidNs);

            activity.SetAttribute("enabled", AndroidNs, "true");
            activity.SetAttribute("exported", AndroidNs, "true");
            EnsureUnityActivityConfigurationChanges(application, activity, AndroidNs);
            EnsureLauncherIntentFilter(doc, activity, AndroidNs);

            SaveXml(doc, fullPath);
            Debug.Log("[ZeyWinAds] Final Android manifest launch metadata verified: " + fullPath);
        }

        private static bool IsUnityLibraryManifest(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return false;

            string normalized = fullPath.Replace('\\', '/');
            return normalized.EndsWith("/unityLibrary/src/main/AndroidManifest.xml", StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureToolsNamespace(XmlElement manifest)
        {
            if (manifest == null || manifest.HasAttribute("xmlns:tools"))
                return;

            manifest.SetAttribute("xmlns:tools", ToolsNs);
        }

        private static void EnsureToolsReplace(XmlElement element, string value)
        {
            if (element == null || string.IsNullOrWhiteSpace(value))
                return;

            string existing = element.GetAttribute("replace", ToolsNs);
            if (string.IsNullOrWhiteSpace(existing))
            {
                element.SetAttribute("replace", ToolsNs, value);
                return;
            }

            foreach (string item in existing.Split(','))
            {
                if (string.Equals(item.Trim(), value, StringComparison.Ordinal))
                    return;
            }

            element.SetAttribute("replace", ToolsNs, existing.TrimEnd() + "," + value);
        }

        private static void RemoveToolsReplace(XmlElement element, string value)
        {
            if (element == null || string.IsNullOrWhiteSpace(value))
                return;

            string existing = element.GetAttribute("replace", ToolsNs);
            if (string.IsNullOrWhiteSpace(existing))
                return;

            var kept = new List<string>();
            foreach (string item in existing.Split(','))
            {
                string trimmed = item.Trim();
                if (trimmed.Length == 0 || string.Equals(trimmed, value, StringComparison.Ordinal))
                    continue;

                kept.Add(trimmed);
            }

            if (kept.Count == 0)
                element.RemoveAttribute("replace", ToolsNs);
            else
                element.SetAttribute("replace", ToolsNs, string.Join(",", kept.ToArray()));
        }

        private static void UpsertGeneratedStringResource(string manifestPath, string stringName, string stringValue)
        {
            if (string.IsNullOrWhiteSpace(manifestPath)
                || string.IsNullOrWhiteSpace(stringName)
                || stringValue == null)
            {
                return;
            }

            string manifestDir = Path.GetDirectoryName(manifestPath);
            if (string.IsNullOrEmpty(manifestDir))
                return;

            string valuesDir = Path.Combine(manifestDir, "res", "values");
            Directory.CreateDirectory(valuesDir);
            string stringsPath = Path.Combine(valuesDir, "strings.xml");

            var doc = new XmlDocument();
            if (File.Exists(stringsPath))
            {
                doc.Load(stringsPath);
                if (doc.DocumentElement == null || doc.DocumentElement.Name != "resources")
                    doc.LoadXml("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<resources />");
            }
            else
            {
                doc.LoadXml("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<resources />");
            }

            XmlElement resources = doc.DocumentElement;
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
            SaveXml(doc, stringsPath);
        }

        private static void EnsureAndroidManifestSecurityQueries()
        {
            string fullPath = Path.GetFullPath(AndroidManifestPath);
            if (!File.Exists(fullPath))
                CreateAndroidManifest(fullPath);

            var doc = new XmlDocument();
            doc.Load(fullPath);

            XmlElement manifest = doc.DocumentElement;
            if (manifest == null)
                return;

            EnsurePermission(doc, manifest, AndroidNs, "android.permission.INTERNET");
            EnsurePermission(doc, manifest, AndroidNs, "android.permission.ACCESS_NETWORK_STATE");
            EnsurePermission(doc, manifest, AndroidNs, "com.google.android.gms.permission.AD_ID");
            EnsurePermission(doc, manifest, AndroidNs, "android.permission.POST_NOTIFICATIONS");
            EnsurePermission(doc, manifest, AndroidNs, "android.permission.CAMERA");
            EnsurePermission(doc, manifest, AndroidNs, "android.permission.RECORD_AUDIO");
            EnsureFeature(doc, manifest, AndroidNs, "android.hardware.camera", required: false);
            EnsureFeature(doc, manifest, AndroidNs, "android.hardware.microphone", required: false);

            XmlElement queries = manifest.SelectSingleNode("queries") as XmlElement;
            if (queries == null)
            {
                queries = doc.CreateElement("queries");
                XmlNode appNode = manifest.SelectSingleNode("application");
                if (appNode != null)
                    manifest.InsertBefore(queries, appNode);
                else
                    manifest.AppendChild(queries);
            }

            var existingPackages = new HashSet<string>();
            var packageNodes = queries.SelectNodes("package");
            if (packageNodes != null)
            {
                foreach (XmlNode node in packageNodes)
                {
                    var name = node.Attributes?.GetNamedItem("name", AndroidNs)?.Value;
                    if (!string.IsNullOrEmpty(name))
                        existingPackages.Add(name);
                }
            }

            foreach (string bundle in ReferralQueriesGenerator.SecurityPackages)
            {
                if (string.IsNullOrEmpty(bundle) || existingPackages.Contains(bundle))
                    continue;

                XmlElement pkg = doc.CreateElement("package");
                pkg.SetAttribute("name", AndroidNs, bundle);
                queries.AppendChild(pkg);
            }

            EnsureViewQueryIntent(doc, queries, AndroidNs, "https");
            EnsureViewQueryIntent(doc, queries, AndroidNs, "http");
            EnsureViewQueryIntent(doc, queries, AndroidNs, "market");
            EnsureViewQueryIntent(doc, queries, AndroidNs, "intent");

            EnsureDeepLink(doc, manifest, AndroidNs);

            SaveXml(doc, fullPath);
        }

        private static void EnsureViewQueryIntent(XmlDocument doc, XmlElement queries, string ns, string scheme)
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
                            && childElement.Attributes?.GetNamedItem("name", ns)?.Value == "android.intent.action.VIEW")
                        {
                            hasView = true;
                        }

                        if (child is XmlElement dataElement
                            && dataElement.Name == "data"
                            && dataElement.Attributes?.GetNamedItem("scheme", ns)?.Value == scheme)
                        {
                            hasScheme = true;
                        }
                    }

                    if (hasView && hasScheme)
                        return;
                }
            }

            XmlElement intent = doc.CreateElement("intent");
            XmlElement action = doc.CreateElement("action");
            action.SetAttribute("name", ns, "android.intent.action.VIEW");
            intent.AppendChild(action);
            XmlElement data = doc.CreateElement("data");
            data.SetAttribute("scheme", ns, scheme);
            intent.AppendChild(data);
            queries.AppendChild(intent);
        }

        private static void EnsureDeepLink(XmlDocument doc, XmlElement manifest, string ns)
        {
            XmlElement application = manifest.SelectSingleNode("application") as XmlElement;
            if (application == null)
            {
                application = doc.CreateElement("application");
                manifest.AppendChild(application);
            }

            string scheme = SanitizeScheme(PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android));
            if (string.IsNullOrEmpty(scheme))
                scheme = SanitizeScheme(PlayerSettings.applicationIdentifier);
            if (string.IsNullOrEmpty(scheme))
                return;

            EnsureMetaData(doc, application, ns, "zeywin.deeplink.scheme", scheme);

            XmlElement activity = FindOrCreateUnityActivity(doc, application, ns);

            activity.SetAttribute("exported", ns, "true");
            activity.SetAttribute("enabled", ns, "true");
            EnsureLauncherIntentFilter(doc, activity, ns);
            EnsureDeepLinkIntentFilter(doc, activity, ns, scheme);
        }

        private static void EnsureMetaData(XmlDocument doc, XmlElement application, string ns, string name, string value)
        {
            XmlElement meta = null;
            var metaNodes = application.SelectNodes("meta-data");
            if (metaNodes != null)
            {
                foreach (XmlNode node in metaNodes)
                {
                    if (node is XmlElement element
                        && element.Attributes?.GetNamedItem("name", ns)?.Value == name)
                    {
                        meta = element;
                        break;
                    }
                }
            }

            if (meta == null)
            {
                meta = doc.CreateElement("meta-data");
                application.AppendChild(meta);
            }

            meta.SetAttribute("name", ns, name);
            meta.SetAttribute("value", ns, value);
        }

        private static XmlElement FindActivity(XmlElement application, string ns, string activityName)
        {
            var activities = application.SelectNodes("activity");
            if (activities == null)
                return null;

            foreach (XmlNode node in activities)
            {
                if (node is XmlElement element
                    && element.Attributes?.GetNamedItem("name", ns)?.Value == activityName)
                {
                    return element;
                }
            }

            return null;
        }

        private static void EnsureUnityActivityConfigurationChanges(XmlElement application, XmlElement primaryActivity, string ns)
        {
            ApplyUnityActivityConfigurationChanges(primaryActivity, ns);
            ApplyUnityActivityConfigurationChanges(FindLauncherActivity(application, ns), ns);
            ApplyUnityActivityConfigurationChanges(FindActivity(application, ns, UnityPlayerActivityName), ns);
            ApplyUnityActivityConfigurationChanges(FindActivity(application, ns, UnityPlayerGameActivityName), ns);
        }

        private static void ApplyUnityActivityConfigurationChanges(XmlElement activity, string ns)
        {
            if (activity == null)
                return;

            activity.SetAttribute("configChanges", ns, UnityActivityConfigChanges);
        }

        private static XmlElement FindOrCreateUnityActivity(XmlDocument doc, XmlElement application, string ns)
        {
            string targetActivityName = ResolveUnityActivityName();
            XmlElement launcher = FindLauncherActivity(application, ns);
            if (launcher != null)
            {
                string launcherName = GetAndroidName(launcher, ns);
                if (!ShouldReplaceUnityLauncher(launcherName, targetActivityName))
                    return launcher;

                RemoveLauncherIntentFilters(launcher, ns);
                Debug.Log("[ZeyWinAds] Moving Android launcher from " + launcherName + " to " + targetActivityName + ".");
            }

            XmlElement activity = FindActivity(application, ns, targetActivityName);
            if (activity == null)
            {
                activity = doc.CreateElement("activity");
                activity.SetAttribute("name", ns, targetActivityName);
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

        private static string GetAndroidName(XmlElement element, string ns)
        {
            return element?.Attributes?.GetNamedItem("name", ns)?.Value;
        }

        private static void RemoveLauncherIntentFilters(XmlElement activity, string ns)
        {
            var filters = activity.SelectNodes("intent-filter");
            if (filters == null)
                return;

            for (int i = filters.Count - 1; i >= 0; i--)
            {
                if (FilterHasAction(filters[i] as XmlElement, ns, "android.intent.action.MAIN")
                    && FilterHasCategory(filters[i] as XmlElement, ns, "android.intent.category.LAUNCHER"))
                {
                    activity.RemoveChild(filters[i]);
                }
            }
        }

        private static XmlElement FindLauncherActivity(XmlElement application, string ns)
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
                    if (FilterHasCategory(filterNode as XmlElement, ns, "android.intent.category.LAUNCHER"))
                        return activity;
                }
            }

            return null;
        }

        private static void EnsureDeepLinkIntentFilter(XmlDocument doc, XmlElement activity, string ns, string scheme)
        {
            var filters = activity.SelectNodes("intent-filter");
            if (filters != null)
            {
                foreach (XmlNode node in filters)
                {
                    if (FilterHasAction(node as XmlElement, ns, "android.intent.action.VIEW")
                        && FilterHasDataScheme(node as XmlElement, ns, scheme))
                    {
                        return;
                    }
                }
            }

            XmlElement filter = doc.CreateElement("intent-filter");
            XmlElement action = doc.CreateElement("action");
            action.SetAttribute("name", ns, "android.intent.action.VIEW");
            filter.AppendChild(action);
            XmlElement defaultCategory = doc.CreateElement("category");
            defaultCategory.SetAttribute("name", ns, "android.intent.category.DEFAULT");
            filter.AppendChild(defaultCategory);
            XmlElement browsableCategory = doc.CreateElement("category");
            browsableCategory.SetAttribute("name", ns, "android.intent.category.BROWSABLE");
            filter.AppendChild(browsableCategory);
            XmlElement data = doc.CreateElement("data");
            data.SetAttribute("scheme", ns, scheme);
            filter.AppendChild(data);
            activity.AppendChild(filter);
        }

        private static void EnsureLauncherIntentFilter(XmlDocument doc, XmlElement activity, string ns)
        {
            var filters = activity.SelectNodes("intent-filter");
            if (filters != null)
            {
                foreach (XmlNode node in filters)
                {
                    var filter = node as XmlElement;
                    if (FilterHasAction(filter, ns, "android.intent.action.MAIN")
                        && FilterHasCategory(filter, ns, "android.intent.category.LAUNCHER"))
                    {
                        return;
                    }
                }
            }

            XmlElement launcherFilter = doc.CreateElement("intent-filter");
            XmlElement action = doc.CreateElement("action");
            action.SetAttribute("name", ns, "android.intent.action.MAIN");
            launcherFilter.AppendChild(action);
            XmlElement launcherCategory = doc.CreateElement("category");
            launcherCategory.SetAttribute("name", ns, "android.intent.category.LAUNCHER");
            launcherFilter.AppendChild(launcherCategory);
            activity.AppendChild(launcherFilter);
        }

        private static bool FilterHasAction(XmlElement filter, string ns, string actionName)
        {
            if (filter == null)
                return false;

            var actions = filter.SelectNodes("action");
            if (actions == null)
                return false;

            foreach (XmlNode node in actions)
            {
                if (node is XmlElement element && element.Attributes?.GetNamedItem("name", ns)?.Value == actionName)
                    return true;
            }

            return false;
        }

        private static bool FilterHasCategory(XmlElement filter, string ns, string categoryName)
        {
            if (filter == null)
                return false;

            var categories = filter.SelectNodes("category");
            if (categories == null)
                return false;

            foreach (XmlNode node in categories)
            {
                if (node is XmlElement element && element.Attributes?.GetNamedItem("name", ns)?.Value == categoryName)
                    return true;
            }

            return false;
        }

        private static bool FilterHasDataScheme(XmlElement filter, string ns, string scheme)
        {
            if (filter == null)
                return false;

            var dataNodes = filter.SelectNodes("data");
            if (dataNodes == null)
                return false;

            foreach (XmlNode node in dataNodes)
            {
                if (node is XmlElement element && element.Attributes?.GetNamedItem("scheme", ns)?.Value == scheme)
                    return true;
            }

            return false;
        }

        private static void EnsurePermission(XmlDocument doc, XmlElement manifest, string ns, string permissionName)
        {
            var permissions = manifest.SelectNodes("uses-permission");
            if (permissions != null)
            {
                foreach (XmlNode perm in permissions)
                {
                    if (perm.Attributes?.GetNamedItem("name", ns)?.Value == permissionName)
                        return;
                }
            }

            XmlElement permission = doc.CreateElement("uses-permission");
            permission.SetAttribute("name", ns, permissionName);
            manifest.InsertBefore(permission, manifest.FirstChild);
        }

        private static void EnsureFeature(XmlDocument doc, XmlElement manifest, string ns, string featureName, bool required)
        {
            var features = manifest.SelectNodes("uses-feature");
            if (features != null)
            {
                foreach (XmlNode node in features)
                {
                    if (node is XmlElement element
                        && element.Attributes?.GetNamedItem("name", ns)?.Value == featureName)
                    {
                        element.SetAttribute("required", ns, required ? "true" : "false");
                        return;
                    }
                }
            }

            XmlElement feature = doc.CreateElement("uses-feature");
            feature.SetAttribute("name", ns, featureName);
            feature.SetAttribute("required", ns, required ? "true" : "false");
            manifest.InsertBefore(feature, manifest.FirstChild);
        }

        private static string SanitizeScheme(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            value = value.Trim().ToLowerInvariant();
            var builder = new System.Text.StringBuilder();
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

        private static void CreateAndroidManifest(string fullPath)
        {
            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var doc = new XmlDocument();
            doc.LoadXml(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\">\n" +
                "    <uses-permission android:name=\"com.google.android.gms.permission.AD_ID\" />\n" +
                "    <application>\n" +
                "    </application>\n" +
                "</manifest>");

            SaveXml(doc, fullPath);
        }

        private static void SaveXml(XmlDocument doc, string fullPath)
        {
            using (var writer = XmlWriter.Create(fullPath, new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "    ",
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace
            }))
            {
                doc.Save(writer);
            }
        }

#if UNITY_IOS
        private static void PatchInfoPlist(string buildPath, string appId, ZeyWinAdsSettings settings)
        {
            if (string.IsNullOrEmpty(appId))
            {
                Debug.LogWarning("[ZeyWinAds] AdMob iOS App ID is empty — Info.plist not patched. " +
                                 "AdMob will refuse to initialize.");
                return;
            }

            string plistPath = Path.Combine(buildPath, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString("GADApplicationIdentifier", appId);
            PatchIosPrivacy(plist, settings);
            PatchIosWebViewPermissions(plist);
            plist.WriteToFile(plistPath);
        }

        private static void PatchInfoPlistForWebViewPermissions(string buildPath)
        {
            string plistPath = Path.Combine(buildPath, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            PatchIosWebViewPermissions(plist);
            plist.WriteToFile(plistPath);
        }

        private static void PatchIosWebViewPermissions(PlistDocument plist)
        {
            if (!plist.root.values.ContainsKey("NSCameraUsageDescription"))
                plist.root.SetString("NSCameraUsageDescription", "Camera access is required by web content.");

            if (!plist.root.values.ContainsKey("NSMicrophoneUsageDescription"))
                plist.root.SetString("NSMicrophoneUsageDescription", "Microphone access is required by web content.");
        }

        private static void PatchIosPrivacy(PlistDocument plist, ZeyWinAdsSettings settings)
        {
            if (!string.IsNullOrEmpty(settings.trackingUsageDescription))
            {
                plist.root.SetString("NSUserTrackingUsageDescription", settings.trackingUsageDescription);
            }

            if (!settings.addGoogleSkAdNetworkIds)
                return;

            PlistElementArray items;
            if (plist.root.values.TryGetValue("SKAdNetworkItems", out var existing) && existing is PlistElementArray existingArray)
                items = existingArray;
            else
                items = plist.root.CreateArray("SKAdNetworkItems");

            foreach (string id in GoogleSkAdNetworkIds)
            {
                if (HasSkAdNetworkId(items, id))
                    continue;

                var dict = items.AddDict();
                dict.SetString("SKAdNetworkIdentifier", id);
            }
        }

        private static bool HasSkAdNetworkId(PlistElementArray items, string id)
        {
            foreach (var item in items.values)
            {
                if (item is PlistElementDict dict
                    && dict.values.TryGetValue("SKAdNetworkIdentifier", out var value)
                    && value.AsString() == id)
                {
                    return true;
                }
            }

            return false;
        }

        private static readonly string[] GoogleSkAdNetworkIds =
        {
            "cstr6suwn9.skadnetwork",
            "4fzdc2evr5.skadnetwork",
            "2fnua5tdw4.skadnetwork",
            "ydx93a7ass.skadnetwork",
            "p78axxw29g.skadnetwork",
            "v72qych5uu.skadnetwork",
            "ludvb6z3bs.skadnetwork",
            "cp8zw746q7.skadnetwork",
            "3sh42y64q3.skadnetwork",
            "c6k4g5qg8m.skadnetwork",
            "s39g8k73mm.skadnetwork",
            "wg4vff78zm.skadnetwork",
            "3qy4746246.skadnetwork",
            "f38h382jlk.skadnetwork",
            "hs6bdukanm.skadnetwork",
            "mlmmfzh3r3.skadnetwork",
            "v4nxqhlyqp.skadnetwork",
            "wzmmz9fp6w.skadnetwork",
            "su67r6k2v3.skadnetwork",
            "yclnxrl5pm.skadnetwork",
            "t38b2kh725.skadnetwork",
            "7ug5zh24hu.skadnetwork",
            "gta9lk7p23.skadnetwork",
            "vutu7akeur.skadnetwork",
            "y5ghdn5j9k.skadnetwork",
            "v9wttpbfk9.skadnetwork",
            "n38lu8286q.skadnetwork",
            "47vhws6wlr.skadnetwork",
            "kbd757ywx3.skadnetwork",
            "9t245vhmpl.skadnetwork",
            "a2p9lx4jpn.skadnetwork",
            "22mmun2rn5.skadnetwork",
            "44jx6755aq.skadnetwork",
            "k674qkevps.skadnetwork",
            "4468km3ulz.skadnetwork",
            "2u9pt9hc89.skadnetwork",
            "8s468mfl3y.skadnetwork",
            "klf5c3l5u5.skadnetwork",
            "ppxm28t8ap.skadnetwork",
            "kbmxgpxpgc.skadnetwork",
            "uw77j35x4d.skadnetwork",
            "578prtvx9j.skadnetwork",
            "4dzt52r2t5.skadnetwork",
            "tl55sbb4fm.skadnetwork",
            "c3frkrj4fj.skadnetwork",
            "e5fvkxwrpn.skadnetwork",
            "8c4e2ghe7u.skadnetwork",
            "3rd42ekr43.skadnetwork",
            "97r2b46745.skadnetwork",
            "3qcr597p9d.skadnetwork"
        };
#endif
    }
}
