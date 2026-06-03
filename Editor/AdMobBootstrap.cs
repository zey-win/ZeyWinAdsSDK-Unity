using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ZeyWinAds.Editor
{
    /// <summary>
    /// One-shot bootstrap that installs the Google Mobile Ads Unity package
    /// from the OpenUPM scoped registry on first import of this SDK.
    ///
    /// Why: ZeyWinAds is delivered as a UPM Git package, which cannot directly
    /// declare a dependency on a package from another registry. So we patch
    /// the consumer's Packages/manifest.json on first run.
    ///
    /// Idempotent: skips if the registry/dependency already exists, and writes
    /// a marker key in EditorPrefs so it never runs twice on the same project.
    /// </summary>
    [InitializeOnLoad]
    public static class AdMobBootstrap
    {
        private const string MarkerKey = "ZeyWinAds_AdMobBootstrap_Done";
        private const string RegistryName = "package.openupm.com";
        private const string RegistryUrl = "https://package.openupm.com";
        private const string AdMobPackage = "com.google.ads.mobile";
        private const string AdMobVersion = "9.4.0";
        private const string EdmPackage = "com.google.external-dependency-manager";
        private const string EdmVersion = "1.2.183";

        static AdMobBootstrap()
        {
            // Run once per project. EditorPrefs is per-machine but that's fine:
            // re-running the patch on a fresh checkout is a no-op anyway.
            string marker = Application.dataPath + "::" + MarkerKey;
            if (EditorPrefs.GetBool(marker, false))
                return;

            try
            {
                if (PatchManifest())
                {
                    Debug.Log("[ZeyWinAds] AdMob bootstrap added Google Mobile Ads to Packages/manifest.json. " +
                              "Unity will resolve packages on next reload.");
                    AssetDatabase.Refresh();
                }
                EditorPrefs.SetBool(marker, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ZeyWinAds] AdMob bootstrap failed: {e.Message}. " +
                                 "Add com.google.ads.mobile manually if you want AdMob fallback.");
            }
        }

        /// <summary>
        /// Manual trigger from the menu — useful after deleting Library or for re-runs.
        /// </summary>
        [MenuItem("ZeyWinAds/Install AdMob (com.google.ads.mobile)")]
        private static void InstallManually()
        {
            if (PatchManifest())
            {
                AssetDatabase.Refresh();
                Debug.Log("[ZeyWinAds] AdMob package added to manifest.");
            }
            else
            {
                Debug.Log("[ZeyWinAds] AdMob already present in manifest, nothing to do.");
            }
        }

        /// <summary>
        /// Edits Packages/manifest.json to add the OpenUPM scoped registry and
        /// the com.google.ads.mobile + EDM4U dependencies. Returns true if any
        /// modification was made.
        ///
        /// We intentionally don't use a JSON library — Unity's JsonUtility doesn't
        /// preserve the manifest's key order and breaks comments. Surgical text
        /// edits keep the file diff-friendly.
        /// </summary>
        private static bool PatchManifest()
        {
            string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
            manifestPath = Path.GetFullPath(manifestPath);

            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning($"[ZeyWinAds] manifest.json not found at {manifestPath}");
                return false;
            }

            string content = File.ReadAllText(manifestPath);
            bool modified = false;
            bool legacyAdMobAssetsPresent = LegacyAdMobAssetsPresent();

            if (legacyAdMobAssetsPresent)
            {
                string cleaned = RemoveDependency(content, AdMobPackage);
                if (cleaned != content)
                {
                    content = cleaned;
                    modified = true;
                    Debug.Log("[ZeyWinAds] Found Assets/GoogleMobileAds; removed duplicate com.google.ads.mobile package dependency.");
                }
            }
            else if (!content.Contains($"\"{AdMobPackage}\""))
            {
                content = AddDependency(content, AdMobPackage, AdMobVersion);
                modified = true;
            }

            if (!content.Contains($"\"{EdmPackage}\""))
            {
                content = AddDependency(content, EdmPackage, EdmVersion);
                modified = true;
            }
            string scoped = EnsureScopedRegistry(content);
            if (scoped != content)
            {
                content = scoped;
                modified = true;
            }

            if (modified)
            {
                File.WriteAllText(manifestPath, content, new UTF8Encoding(false));
            }
            return modified;
        }

        private static bool LegacyAdMobAssetsPresent()
        {
            string assetsRoot = Application.dataPath;
            return File.Exists(Path.Combine(assetsRoot, "GoogleMobileAds", "GoogleMobileAds.Core.dll"))
                || File.Exists(Path.Combine(assetsRoot, "GoogleMobileAds", "Editor", "GoogleMobileAdsSettings.cs"));
        }

        private static string AddDependency(string manifest, string package, string version)
        {
            int depsIdx = manifest.IndexOf("\"dependencies\"", StringComparison.Ordinal);
            if (depsIdx < 0)
            {
                Debug.LogWarning("[ZeyWinAds] manifest.json is missing a \"dependencies\" block — " +
                                 $"cannot add {package}. Add it manually.");
                return manifest;
            }
            int braceIdx = manifest.IndexOf('{', depsIdx);
            if (braceIdx < 0)
            {
                Debug.LogWarning($"[ZeyWinAds] malformed manifest.json near \"dependencies\" — cannot add {package}.");
                return manifest;
            }

            string entry = $"\n    \"{package}\": \"{version}\",";
            return manifest.Insert(braceIdx + 1, entry);
        }

        private static string RemoveDependency(string manifest, string package)
        {
            string key = $"\"{package}\"";
            int keyIdx = manifest.IndexOf(key, StringComparison.Ordinal);
            if (keyIdx < 0)
                return manifest;

            int lineStart = manifest.LastIndexOf('\n', keyIdx);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;

            int lineEnd = manifest.IndexOf('\n', keyIdx);
            if (lineEnd < 0)
                lineEnd = manifest.Length;
            else
                lineEnd += 1;

            return manifest.Remove(lineStart, lineEnd - lineStart);
        }

        private static string EnsureScopedRegistry(string manifest)
        {
            if (!manifest.Contains($"\"{RegistryUrl}\""))
                return AddScopedRegistry(manifest);

            string updated = AddScopeToExistingRegistry(manifest, AdMobPackage);
            updated = AddScopeToExistingRegistry(updated, EdmPackage);
            return updated;
        }

        private static string AddScopedRegistry(string manifest)
        {
            string registryBlock =
                "\n  \"scopedRegistries\": [\n" +
                "    {\n" +
                $"      \"name\": \"{RegistryName}\",\n" +
                $"      \"url\": \"{RegistryUrl}\",\n" +
                "      \"scopes\": [\n" +
                "        \"com.google.ads.mobile\",\n" +
                "        \"com.google.external-dependency-manager\"\n" +
                "      ]\n" +
                "    }\n" +
                "  ],";

            // If a scopedRegistries array already exists, merge the new scopes into it.
            int existingIdx = manifest.IndexOf("\"scopedRegistries\"", StringComparison.Ordinal);
            if (existingIdx >= 0)
            {
                // The registry already exists with different content — leave it alone
                // and let the user resolve manually. Avoid clobbering custom configs.
                Debug.LogWarning("[ZeyWinAds] scopedRegistries already present in manifest.json; " +
                                 "ensure com.google.* scopes are mapped to https://package.openupm.com.");
                return manifest;
            }

            // Insert before the dependencies block.
            int depsIdx = manifest.IndexOf("\"dependencies\"", StringComparison.Ordinal);
            if (depsIdx < 0) return manifest;

            // Find the indentation start
            int insertAt = manifest.LastIndexOf('\n', depsIdx) + 1;
            return manifest.Insert(insertAt, registryBlock.TrimStart('\n') + "\n  ");
        }

        private static string AddScopeToExistingRegistry(string manifest, string scope)
        {
            int registryIdx = manifest.IndexOf($"\"{RegistryUrl}\"", StringComparison.Ordinal);
            if (registryIdx < 0)
                return manifest;

            int scopesIdx = manifest.IndexOf("\"scopes\"", registryIdx, StringComparison.Ordinal);
            if (scopesIdx < 0)
                return manifest;

            int arrayStart = manifest.IndexOf('[', scopesIdx);
            if (arrayStart < 0)
                return manifest;

            int arrayEnd = manifest.IndexOf(']', arrayStart);
            if (arrayEnd < 0)
                return manifest;

            string scopesBlock = manifest.Substring(arrayStart, arrayEnd - arrayStart);
            if (scopesBlock.Contains($"\"{scope}\""))
                return manifest;

            return manifest.Insert(arrayStart + 1, $"\n        \"{scope}\",");
        }
    }
}
