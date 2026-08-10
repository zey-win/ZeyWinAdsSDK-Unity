using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZeyWinAds.Editor
{
    /// <summary>
    /// Owns everything that needs to exist under the consumer's Assets/ZeyWinAds/ folder:
    /// link.xml, and the editable Custom Native Banner starter (script + prefab + texture,
    /// pulled from this package's Samples~ folder - which Unity's AssetDatabase never
    /// copies into a project automatically on its own). Runs on every Editor domain reload
    /// via [InitializeOnLoad], and is also called defensively from FirebasePostprocessor
    /// right before a build in case that never got a chance to run (e.g. a fresh checkout
    /// built straight away in batch mode).
    /// Only ever writes files that are missing or out of date, so a consumer's own edits to
    /// their copy of the banner sample are never overwritten by a later reinstall.
    /// </summary>
    [InitializeOnLoad]
    public static class ZeyWinAdsAssetsBootstrap
    {
        private const string TargetRoot = "Assets/ZeyWinAds";

        // UnityLinker only collects link.xml files from under the consumer's own Assets
        // folder (or embedded/local packages) - not from a package resolved via git/registry
        // into the read-only Library/PackageCache. Shipping this inside the package's own
        // Runtime folder is therefore silently ineffective: the assembly-preserve rule never
        // reaches the linker, and Firebase.Messaging gets stripped out of IL2CPP builds even
        // though it's installed. So it's written into the consumer's Assets/ZeyWinAds/
        // instead, mirroring how GoogleMobileAds' own package places its link.xml under
        // Assets/GoogleMobileAds/ for the same reason.
        private const string LinkXmlAssetPath = TargetRoot + "/link.xml";

        private const string LinkXmlContent =
            "<linker>\n" +
            "  <assembly fullname=\"Firebase.App\" preserve=\"all\" />\n" +
            "  <assembly fullname=\"Firebase.Messaging\" preserve=\"all\" />\n" +
            "  <assembly fullname=\"Firebase.Platform\" preserve=\"all\" />\n" +
            "  <assembly fullname=\"Firebase.TaskExtension\" preserve=\"all\" />\n" +
            "</linker>\n";

        private const string SampleRelativePath = "Samples~/CustomNativeBanner";

        private static readonly (string source, string target)[] SampleFiles =
        {
            ("CustomNativeBannerView.cs", "Scripts/CustomNativeBannerView.cs"),
            ("Prefabs/CustomNativeBannerCanvas (Horizontal).prefab",
                "Prefabs/CustomNativeBanner/CustomNativeBannerCanvas (Horizontal).prefab"),
            ("Prefabs/CustomNativeBannerCanvas (Vertical).prefab",
                "Prefabs/CustomNativeBanner/CustomNativeBannerCanvas (Vertical).prefab"),
            ("Textures/native_banner_bg.jpg", "Textures/CustomNativeBanner/native_banner_bg.jpg"),
        };

        static ZeyWinAdsAssetsBootstrap()
        {
            EditorApplication.delayCall += EnsureAssetsInstalled;
        }

        // Only ever fills in whatever's missing (e.g. a file a consumer accidentally
        // deleted) — never overwrites an existing file, so a consumer's own edits to
        // their copy of the banner sample are never at risk, whether this runs
        // automatically on domain reload or is triggered from this menu item by hand.
        [MenuItem("ZeyWinAds/Install Custom Native Banner Sample", priority = 3)]
        public static void InstallSampleFromMenu()
        {
            EnsureAssetsInstalled();
        }

        internal static void EnsureAssetsInstalled()
        {
            try
            {
                bool changed = false;
                changed |= EnsureLinkXml();
                changed |= EnsureCustomNativeBannerSample();

                if (changed)
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ZeyWinAds] Assets bootstrap failed: {e.Message}");
            }
        }

        private static bool EnsureLinkXml()
        {
            string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), LinkXmlAssetPath);

            if (File.Exists(fullPath) && File.ReadAllText(fullPath) == LinkXmlContent)
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, LinkXmlContent);
            AssetDatabase.ImportAsset(LinkXmlAssetPath);
            return true;
        }

        private static bool EnsureCustomNativeBannerSample()
        {
            string sampleRoot = FindSampleRoot();
            if (string.IsNullOrEmpty(sampleRoot))
                return false;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            bool copiedAny = false;

            foreach (var (source, target) in SampleFiles)
            {
                string sourceFile = Path.Combine(sampleRoot, source);
                if (!File.Exists(sourceFile))
                    continue;

                string targetFile = Path.Combine(projectRoot, TargetRoot, target);
                if (File.Exists(targetFile))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(targetFile));
                File.Copy(sourceFile, targetFile, overwrite: false);

                string sourceMeta = sourceFile + ".meta";
                if (File.Exists(sourceMeta))
                    File.Copy(sourceMeta, targetFile + ".meta", overwrite: false);

                copiedAny = true;
            }

            if (copiedAny)
                Debug.Log($"[ZeyWinAds] Custom Native Banner sample installed into {TargetRoot}/.");

            return copiedAny;
        }

        private static string FindSampleRoot()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ZeyWinAdsAssetsBootstrap).Assembly);
            if (packageInfo == null || string.IsNullOrEmpty(packageInfo.resolvedPath))
                return null;

            string sampleRoot = Path.Combine(packageInfo.resolvedPath, SampleRelativePath);
            return Directory.Exists(sampleRoot) ? sampleRoot : null;
        }
    }
}
