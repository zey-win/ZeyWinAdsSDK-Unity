using System.IO;
using System.Collections.Generic;
using System.Xml;
using UnityEditor;
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
    public class AdMobBuildPostprocessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        // Path that ZeyWinAdsAndroidManifestPatcher writes/edits.
        private const string AndroidManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        private const string AdMobMetaName = "com.google.android.gms.ads.APPLICATION_ID";

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = ZeyWinAdsSettingsEditor.LoadOrCreate();

            if (report.summary.platform == BuildTarget.Android)
            {
                EnsureAndroidManifestSecurityQueries();

                if (settings != null && settings.enableAdMob)
                    PatchAndroidManifest(settings.admobAppIdAndroid);
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

        private static void PatchAndroidManifest(string appId)
        {
            if (string.IsNullOrEmpty(appId))
            {
                Debug.LogWarning("[ZeyWinAds] AdMob Android App ID is empty — manifest not patched. " +
                                 "AdMob will refuse to initialize.");
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

            string ns = "http://schemas.android.com/apk/res/android";
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
                        && element.Attributes?.GetNamedItem("name", ns)?.Value == AdMobMetaName)
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

            meta.SetAttribute("name", ns, AdMobMetaName);
            meta.SetAttribute("value", ns, appId);

            SaveXml(doc, fullPath);
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

            string ns = "http://schemas.android.com/apk/res/android";
            EnsurePermission(doc, manifest, ns, "android.permission.INTERNET");
            EnsurePermission(doc, manifest, ns, "android.permission.ACCESS_NETWORK_STATE");
            EnsurePermission(doc, manifest, ns, "com.google.android.gms.permission.AD_ID");
            EnsurePermission(doc, manifest, ns, "android.permission.POST_NOTIFICATIONS");
            EnsurePermission(doc, manifest, ns, "android.permission.CAMERA");
            EnsurePermission(doc, manifest, ns, "android.permission.RECORD_AUDIO");
            EnsureFeature(doc, manifest, ns, "android.hardware.camera", required: false);
            EnsureFeature(doc, manifest, ns, "android.hardware.microphone", required: false);

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
                    var name = node.Attributes?.GetNamedItem("name", ns)?.Value;
                    if (!string.IsNullOrEmpty(name))
                        existingPackages.Add(name);
                }
            }

            foreach (string bundle in ReferralQueriesGenerator.SecurityPackages)
            {
                if (string.IsNullOrEmpty(bundle) || existingPackages.Contains(bundle))
                    continue;

                XmlElement pkg = doc.CreateElement("package");
                pkg.SetAttribute("name", ns, bundle);
                queries.AppendChild(pkg);
            }

            EnsureViewQueryIntent(doc, queries, ns, "https");
            EnsureViewQueryIntent(doc, queries, ns, "http");
            EnsureViewQueryIntent(doc, queries, ns, "market");
            EnsureViewQueryIntent(doc, queries, ns, "intent");

            EnsureDeepLink(doc, manifest, ns);

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

            XmlElement activity = FindActivity(application, ns, "com.unity3d.player.UnityPlayerActivity") ?? FindLauncherActivity(application, ns);
            if (activity == null)
            {
                activity = doc.CreateElement("activity");
                activity.SetAttribute("name", ns, "com.unity3d.player.UnityPlayerActivity");
                application.AppendChild(activity);
            }

            activity.SetAttribute("exported", ns, "true");
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
