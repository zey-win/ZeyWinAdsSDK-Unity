using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ZeyWinAds.Editor
{
    /// <summary>
    /// Pre-build step that ensures play-services-ads-identifier is present
    /// in mainTemplate.gradle. Works as a fallback when EDM4U is not installed.
    /// </summary>
    public class GradleDependencyInjector : IPreprocessBuildWithReport
    {
        private const string Dependency = "com.google.android.gms:play-services-ads-identifier:18.0.1";
        private const string GradlePath = "Assets/Plugins/Android/mainTemplate.gradle";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;

            // Skip if EDM4U is handling dependencies
            if (IsEdm4uInstalled())
            {
                Debug.Log("[ZeyWinAds] EDM4U detected — skipping manual gradle injection.");
                return;
            }

            if (!File.Exists(GradlePath))
            {
                Debug.LogWarning(
                    $"[ZeyWinAds] {GradlePath} not found. Enable 'Custom Main Gradle Template' in " +
                    "Player Settings > Publishing Settings, or install EDM4U to auto-resolve " +
                    $"'{Dependency}'.");
                return;
            }

            string content = File.ReadAllText(GradlePath);

            if (content.Contains(Dependency))
            {
                Debug.Log("[ZeyWinAds] Gradle dependency already present.");
                return;
            }

            // Find the dependencies { ... } block and inject
            var match = Regex.Match(content, @"(dependencies\s*\{)");
            if (!match.Success)
            {
                Debug.LogWarning("[ZeyWinAds] Could not find 'dependencies {' block in mainTemplate.gradle.");
                return;
            }

            string injection = $"\n    implementation '{Dependency}'";
            content = content.Insert(match.Index + match.Length, injection);
            File.WriteAllText(GradlePath, content);

            Debug.Log($"[ZeyWinAds] Added '{Dependency}' to {GradlePath}");
            AssetDatabase.Refresh();
        }

        private static bool IsEdm4uInstalled()
        {
            // Check for EDM4U's main resolver class
            return System.Type.GetType("GooglePlayServices.PlayServicesResolver, Google.JarResolver") != null
                || System.Type.GetType("Google.AndroidResolverHelper, Google.JarResolver") != null;
        }
    }
}
