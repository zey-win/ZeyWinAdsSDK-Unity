using System.IO;
using System.Text.RegularExpressions;
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
            if (settings == null || !settings.enableAdMob)
                return;

            if (report.summary.platform == BuildTarget.Android)
            {
                PatchAndroidManifest(settings.admobAppIdAndroid);
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            var settings = ZeyWinAdsSettings.Load() ?? ZeyWinAdsSettingsEditor.LoadOrCreate();
            if (settings == null || !settings.enableAdMob)
                return;

#if UNITY_IOS
            if (report.summary.platform == BuildTarget.iOS)
            {
                PatchInfoPlist(report.summary.outputPath, settings.admobAppIdIOS);
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
                Debug.LogWarning($"[ZeyWinAds] {AndroidManifestPath} not found. Create it manually " +
                                 $"and add a meta-data tag with name=\"{AdMobMetaName}\" value=\"{appId}\".");
                return;
            }

            string xml = File.ReadAllText(fullPath);
            string metaPattern = $@"<meta-data\s+android:name=""{Regex.Escape(AdMobMetaName)}""[^/]*/>";
            string newMeta = $"<meta-data android:name=\"{AdMobMetaName}\" android:value=\"{appId}\"/>";

            if (Regex.IsMatch(xml, metaPattern))
            {
                xml = Regex.Replace(xml, metaPattern, newMeta);
            }
            else
            {
                // Insert before </application>
                int closingApp = xml.LastIndexOf("</application>", System.StringComparison.Ordinal);
                if (closingApp < 0)
                {
                    Debug.LogWarning("[ZeyWinAds] AndroidManifest.xml has no </application> tag — skipping AdMob meta.");
                    return;
                }
                xml = xml.Insert(closingApp, "    " + newMeta + "\n    ");
            }

            File.WriteAllText(fullPath, xml);
            Debug.Log($"[ZeyWinAds] Wrote AdMob App ID to AndroidManifest.xml: {appId}");
        }

#if UNITY_IOS
        private static void PatchInfoPlist(string buildPath, string appId)
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
            plist.WriteToFile(plistPath);
            Debug.Log($"[ZeyWinAds] Wrote AdMob App ID to Info.plist: {appId}");
        }
#endif
    }
}
