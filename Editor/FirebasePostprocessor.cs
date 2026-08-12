using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

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
    public class FirebasePostprocessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.Android || report.summary.platform == BuildTarget.iOS)
            {
                RequireFirebaseMessagingInstalled();

                // Belt-and-suspenders: normally already done by
                // ZeyWinAdsAssetsBootstrap's [InitializeOnLoad] hook, but a fresh
                // checkout built straight away in batch mode may never have run it.
                ZeyWinAdsAssetsBootstrap.EnsureAssetsInstalled();
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
#if UNITY_IOS
            if (report.summary.platform != BuildTarget.iOS)
                return;

            // Firebase Messaging's iOS notification-permission flow (RequestPermissionAsync,
            // see Core/FirebaseMessagingService.cs) goes through UNUserNotificationCenter.
            // With this project's static Pod linkage, that symbol isn't guaranteed to resolve
            // in the app target unless the system framework is linked explicitly - and Unity
            // regenerates the Xcode project from scratch on every export, so a framework added
            // by hand in Xcode doesn't survive the next build.
            string pbxPath = PBXProject.GetPBXProjectPath(report.summary.outputPath);
            var pbxProject = new PBXProject();
            pbxProject.ReadFromFile(pbxPath);

            string targetGuid = pbxProject.GetUnityMainTargetGuid();
            pbxProject.AddFrameworkToProject(targetGuid, "UserNotifications.framework", false);

            pbxProject.WriteToFile(pbxPath);
#endif
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
