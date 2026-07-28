using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ZeyWinAds.Editor
{
    /// <summary>
    /// ZeyWinAds talks to Firebase Messaging purely via reflection (see
    /// Core/FirebaseMessagingService.cs) - it never vendors or bundles Firebase
    /// itself, since a bundled copy inevitably collides with any Firebase
    /// Messaging install the consumer already has (duplicate assembly names,
    /// duplicate native Android/iOS plugin files - both hard Unity build
    /// failures with no safe automatic fix). That tradeoff only holds if the
    /// consumer has actually installed Firebase Messaging themselves, so this
    /// fails the build loudly instead of silently shipping a non-functional
    /// push notification feature.
    /// </summary>
    public class FirebasePostprocessor : IPreprocessBuildWithReport
    {
        // UnityLinker only collects link.xml files from under the consumer's own
        // Assets folder (or embedded/local packages) - not from a package resolved
        // via git/registry into the read-only Library/PackageCache. Shipping this
        // link.xml inside the package's own Runtime folder is therefore silently
        // ineffective: the assembly-preserve rule never reaches the linker, and
        // Firebase.Messaging gets stripped out of IL2CPP builds even though it's
        // installed. So it's written into the consumer's Assets/ZeyWinAds/ instead,
        // mirroring how GoogleMobileAds' own package places its link.xml under
        // Assets/GoogleMobileAds/ for the same reason.
        private const string LinkXmlAssetPath = "Assets/ZeyWinAds/link.xml";

        private const string LinkXmlContent =
            "<linker>\n" +
            "  <assembly fullname=\"Firebase.App\" preserve=\"all\" />\n" +
            "  <assembly fullname=\"Firebase.Messaging\" preserve=\"all\" />\n" +
            "  <assembly fullname=\"Firebase.Platform\" preserve=\"all\" />\n" +
            "  <assembly fullname=\"Firebase.TaskExtension\" preserve=\"all\" />\n" +
            "</linker>\n";

        public int callbackOrder => 100;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.Android || report.summary.platform == BuildTarget.iOS)
            {
                RequireFirebaseMessagingInstalled();
                EnsureLinkXmlInAssets();
            }
        }

        // The real Firebase Unity SDK ships Firebase.Messaging.FirebaseMessaging inside
        // an assembly named "Firebase.Messaging". A same-named type defined elsewhere
        // (e.g. a consumer's own stand-in stub for builds without Firebase) would
        // otherwise satisfy a name-only lookup and silently defeat this check.
        private const string RequiredAssemblyName = "Firebase.Messaging";

        private static void RequireFirebaseMessagingInstalled()
        {
            if (FindType("Firebase.Messaging.FirebaseMessaging", RequiredAssemblyName) != null)
                return;

            throw new BuildFailedException(
                "[ZeyWinAds] Firebase Messaging is required for push notification support but was not found in " +
                "this project. Install it via the Firebase Unity SDK (https://firebase.google.com/download/unity) " +
                "and add your google-services.json (Android) / GoogleService-Info.plist (iOS), then rebuild.");
        }

        private static void EnsureLinkXmlInAssets()
        {
            string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), LinkXmlAssetPath);

            if (File.Exists(fullPath) && File.ReadAllText(fullPath) == LinkXmlContent)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, LinkXmlContent);
            AssetDatabase.ImportAsset(LinkXmlAssetPath);
        }

        private static Type FindType(string fullName, string requiredAssemblyName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                if (assemblies[i].GetName().Name != requiredAssemblyName)
                    continue;

                Type type = assemblies[i].GetType(fullName);
                if (type != null)
                    return type;
            }

            return null;
        }
    }
}
