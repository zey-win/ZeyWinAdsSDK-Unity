using System;
using System.IO;
using System.Text;
using UnityEditor.Android;
using UnityEngine;

namespace ZeyWinAds.Editor
{
    public sealed class LegacyGoogleMobileAdsGradlePostprocessor : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 10000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            try
            {
                string rootPath = ResolveGradleRoot(path);
                if (string.IsNullOrEmpty(rootPath))
                    return;

                bool referenced = GradleFileReferencesPackagingOptions(rootPath, "launcher/build.gradle")
                    || GradleFileReferencesPackagingOptions(rootPath, "unityLibrary/build.gradle");
                string androidLibPath = Path.Combine(rootPath, "unityLibrary", "GoogleMobileAdsPlugin.androidlib");
                if (!referenced && !Directory.Exists(androidLibPath))
                    return;

                Directory.CreateDirectory(androidLibPath);
                WriteFile(
                    Path.Combine(androidLibPath, "packaging_options.gradle"),
                    "android {\n" +
                    "    packagingOptions {\n" +
                    "        pickFirst \"META-INF/kotlinx_coroutines_core.version\"\n" +
                    "    }\n" +
                    "}\n");
                EnsureFile(
                    Path.Combine(androidLibPath, "AndroidManifest.xml"),
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                    "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\"com.google.unity.ads\">\n" +
                    "  <application />\n" +
                    "</manifest>\n");
                EnsureFile(
                    Path.Combine(androidLibPath, "project.properties"),
                    "target=android-31\nandroid.library=true\n");

                Debug.Log("[ZeyWinAds] Legacy Google Mobile Ads androidlib Gradle files verified.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ZeyWinAds] Could not verify legacy Google Mobile Ads Gradle files: " + e.Message);
            }
        }

        private static string ResolveGradleRoot(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            string fullPath = Path.GetFullPath(path);
            foreach (string candidate in new[]
            {
                fullPath,
                Directory.GetParent(fullPath)?.FullName
            })
            {
                if (string.IsNullOrEmpty(candidate))
                    continue;

                if (Directory.Exists(Path.Combine(candidate, "launcher")) &&
                    Directory.Exists(Path.Combine(candidate, "unityLibrary")))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool GradleFileReferencesPackagingOptions(string rootPath, string relativePath)
        {
            string path = Path.Combine(rootPath, relativePath);
            if (!File.Exists(path))
                return false;

            string content = File.ReadAllText(path);
            return content.Contains("GoogleMobileAdsPlugin.androidlib/packaging_options.gradle")
                || content.Contains("GoogleMobileAdsPlugin.androidlib\\packaging_options.gradle");
        }

        private static void EnsureFile(string path, string content)
        {
            if (File.Exists(path))
                return;

            WriteFile(path, content);
        }

        private static void WriteFile(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
    }
}
