using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Xml;
using UnityEditor;
using UnityEngine;

namespace ZeyWinAds.Editor
{
    /// <summary>
    /// Editor tool that fetches active bundle IDs from the server
    /// and generates/updates the &lt;queries&gt; block in AndroidManifest.xml.
    /// Required for Android 11+ (API 30) PackageManager visibility.
    /// </summary>
    public class ReferralQueriesGenerator : EditorWindow
    {
        private static readonly string[] ApiEndpoints = new[]
        {
            "https://zeywin-ads-api.whiteapps.workers.dev/api/v1"
        };

        private const string ManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";

        private string _statusMessage = "";
        private bool _isWorking;

        [MenuItem("ZeyWinAds/Update AndroidManifest Queries")]
        public static void ShowWindow()
        {
            GetWindow<ReferralQueriesGenerator>("Referral Queries");
        }

        private void OnGUI()
        {
            GUILayout.Label("Cross-App Referral Setup", EditorStyles.boldLabel);
            GUILayout.Space(5);
            GUILayout.Label(
                "Fetches active app bundle IDs from the server and updates\n" +
                "AndroidManifest.xml with <queries> entries for PackageManager\n" +
                "visibility on Android 11+ (API 30).",
                EditorStyles.wordWrappedLabel);
            GUILayout.Space(10);

            EditorGUI.BeginDisabledGroup(_isWorking);
            if (GUILayout.Button("Fetch & Update Manifest", GUILayout.Height(30)))
            {
                FetchAndUpdate();
            }
            EditorGUI.EndDisabledGroup();

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                GUILayout.Space(10);
                EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
            }
        }

        private async void FetchAndUpdate()
        {
            _isWorking = true;
            _statusMessage = "Fetching bundle list from server...";
            Repaint();

            try
            {
                string[] bundles = await FetchBundles();
                if (bundles == null || bundles.Length == 0)
                {
                    _statusMessage = "No bundles returned from server.";
                    _isWorking = false;
                    Repaint();
                    return;
                }

                // Remove current app's bundle ID from the list
                string currentBundle = PlayerSettings.applicationIdentifier;
                var filtered = new List<string>();
                foreach (var b in bundles)
                {
                    if (!string.IsNullOrEmpty(b) && b != currentBundle)
                        filtered.Add(b);
                }

                // Add suspicious packages for security detection on Android 11+
                foreach (var sp in SecurityPackages)
                {
                    if (!filtered.Contains(sp))
                        filtered.Add(sp);
                }

                UpdateManifest(filtered);
                _statusMessage = $"Updated AndroidManifest.xml with {filtered.Count} package queries.\n" +
                                 $"(excluded own bundle: {currentBundle})";
            }
            catch (Exception e)
            {
                _statusMessage = $"Error: {e.Message}";
                Debug.LogError($"[ZeyWinAds] ReferralQueriesGenerator error: {e}");
            }

            _isWorking = false;
            Repaint();
        }

        private static async System.Threading.Tasks.Task<string[]> FetchBundles()
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(15);

            foreach (var endpoint in ApiEndpoints)
            {
                try
                {
                    string url = $"{endpoint}/apps/bundles";
                    string proxyUrl = "https://www.proxodi.com/v1/proxy?target=" +
                        Uri.EscapeDataString(url) + "&auth=asdjkasdkasdasd";
                    var request = new HttpRequestMessage(HttpMethod.Get, proxyUrl);
                    request.Headers.Add("X-Internal-Proxy-Auth", "asdjkasdkasdasd");
                    var resp = await http.SendAsync(request);
                    string json = await resp.Content.ReadAsStringAsync();

                    // Simple JSON parsing (avoid dependency on Newtonsoft)
                    // Expected: {"success":true,"data":{"bundles":["com.a","com.b"]}}
                    var response = JsonUtility.FromJson<BundleApiResponse>(json);
                    if (response != null && response.success && response.data != null)
                    {
                        return response.data.bundles;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ZeyWinAds] Failed to fetch from {endpoint}: {e.Message}");
                }
            }
            return null;
        }

        private static void UpdateManifest(List<string> bundles)
        {
            // Ensure directory exists
            string dir = Path.GetDirectoryName(ManifestPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            XmlDocument doc = new XmlDocument();

            if (File.Exists(ManifestPath))
            {
                doc.Load(ManifestPath);
            }
            else
            {
                // Create minimal manifest
                doc.LoadXml(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                    "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\">\n" +
                    "    <application>\n" +
                    "    </application>\n" +
                    "</manifest>");
            }

            XmlElement manifest = doc.DocumentElement;
            string ns = "http://schemas.android.com/apk/res/android";

            // Remove existing <queries> node if present
            var existingQueries = manifest.SelectNodes("queries");
            if (existingQueries != null)
            {
                foreach (XmlNode node in existingQueries)
                    manifest.RemoveChild(node);
            }

            // Create new <queries> block
            XmlElement queries = doc.CreateElement("queries");

            foreach (var bundle in bundles)
            {
                XmlElement pkg = doc.CreateElement("package");
                pkg.SetAttribute("name", ns, bundle);
                queries.AppendChild(pkg);
            }

            // Insert before <application>
            XmlNode appNode = manifest.SelectSingleNode("application");
            if (appNode != null)
                manifest.InsertBefore(queries, appNode);
            else
                manifest.AppendChild(queries);

            // Ensure AD_ID permission exists
            bool hasAdIdPerm = false;
            var permissions = manifest.SelectNodes("uses-permission");
            if (permissions != null)
            {
                foreach (XmlNode perm in permissions)
                {
                    var nameAttr = perm.Attributes?.GetNamedItem("name", ns);
                    if (nameAttr?.Value == "com.google.android.gms.permission.AD_ID")
                    {
                        hasAdIdPerm = true;
                        break;
                    }
                }
            }
            if (!hasAdIdPerm)
            {
                XmlElement perm = doc.CreateElement("uses-permission");
                perm.SetAttribute("name", ns, "com.google.android.gms.permission.AD_ID");
                manifest.InsertBefore(perm, manifest.FirstChild);
            }

            // Save
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "    ",
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace
            };
            using (XmlWriter writer = XmlWriter.Create(ManifestPath, settings))
            {
                doc.Save(writer);
            }

            AssetDatabase.Refresh();
            Debug.Log($"[ZeyWinAds] AndroidManifest.xml updated with {bundles.Count} queries entries at {ManifestPath}");
        }

        // Suspicious packages that must be in <queries> for Android 11+ detection
        private static readonly string[] SecurityPackages = new[]
        {
            // Hooking / Instrumentation
            "de.robv.android.xposed.installer",
            "de.robv.android.xposed",
            "org.meowcat.edxposed.manager",
            "org.lsposed.manager",
            "org.lsposed.lspatch",
            "io.va.exposed",
            "me.weishu.exp",
            "com.saurik.substrate",
            "re.frida.server",
            "com.dimonvideo.luckypatcher",
            "com.chelpus.lackypatch",
            "com.forpda.lp",
            "com.wind.xpatch",
            "mobi.acpm.inspeckage",
            "mobi.acpm.sslunpinning",
            "just.trust.me",
            // Network Inspectors / Proxies
            "com.xk72.charles",
            "tech.httptoolkit.android.v1",
            "com.guoshi.httpcanary",
            "com.guoshi.httpcanary.premium",
            "app.greyshirts.sslcapture",
            "jp.co.taosoftware.android.packetcapture",
            "com.egorovandreyrm.pcapremote",
            "com.emanuelef.remote_capture",
            "com.proxyman.android",
            "com.reqable.android",
            "com.minhui.networkcapture",
            "com.minhui.networkcapture.pro",
            "com.minhui.packetcapture",
            "com.telerik.fiddler",
            "org.sandroproxy.drony",
            "org.sandroproxy",
            // Memory Editors / Game Cheats
            "catch.monitor",
            "com.cih.game_cih",
            "com.killerapp.gamekiller",
            "org.sbtools.gamehack",
            "org.cheatengine.cegui",
            "com.leo.playcard",
            "org.creeplays.hack",
            "cc.madkite.freedom",
            "com.xmodgame",
            "com.cih.gamecih2",
            // Decompilers / RE Tools
            "bin.mt.plus",
            "bin.mt",
            "com.gmail.heagoo.apkeditor",
            "com.gmail.heagoo.apkeditor.pro",
            "com.gmail.heagoo.apkeditor.free",
            "com.njlabs.showjava",
            // Virtual Spaces / Cloners
            "io.virtualapp",
            "com.lbe.parallel.intl",
            "com.lbe.parallel",
            "com.ludashi.dualspace",
            "com.excelliance.multiaccounts",
            "com.polestar.super.clone",
            "com.vmos.app",
            "com.vmos.pro",
            "com.x8bit.biern",
            // Root Management
            "eu.chainfire.supersu",
            "com.topjohnwu.magisk",
            "com.kingroot.kinguser",
            "com.kingo.root",
            "com.koushikdutta.superuser",
            "com.noshufou.android.su",
            // App Inspectors
            "com.codex.appinspector",
            "com.jgba.appinspector",
            "com.ubqsoft.sec01",
            "de.szalkowski.activitylauncher",
            "io.github.muntashirakon.AppManager",
            // Termux
            "com.termux",
        };

        [Serializable]
        private class BundleApiResponse
        {
            public bool success;
            public BundleData data;
        }

        [Serializable]
        private class BundleData
        {
            public string[] bundles;
        }
    }
}
