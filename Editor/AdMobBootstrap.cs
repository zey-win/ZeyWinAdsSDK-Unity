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
        private const string MarkerKey = "ZeyWinAds_AdMobBootstrap_Done_v2";
        private const string RegistryName = "package.openupm.com";
        private const string RegistryUrl = "https://package.openupm.com";
        private const string DisableEnv = "ZEYWIN_DISABLE_ADMOB_BOOTSTRAP";
        private const string AdMobPackage = "com.google.ads.mobile";
        private const string AdMobVersion = "11.2.0";
        private const string EdmPackage = "com.google.external-dependency-manager";
        private const string EdmVersion = "1.2.187";
        private const string FirebaseAppPackage = "com.google.firebase.app";
        private const string FirebaseAnalyticsPackage = "com.google.firebase.analytics";
        private const string FirebaseRemoteConfigPackage = "com.google.firebase.remote-config";
        private const string FirebaseMessagingPackage = "com.google.firebase.messaging";
        private const string FirebaseVersion = "13.6.0";
        private const string FirebaseRegistryName = "Google Unity Package Registry";
        private const string FirebaseRegistryUrl = "https://dl.google.com/games/registry/unity";
        private const string FirebaseRegistryScope = "com.google.firebase";
        private const string AndroidPluginsPath = "Assets/Plugins/Android";
        private const string LegacyNoSdkStubsPath = "Assets/Scripts/SDKStubs/NoSdkStubs.cs";

        static AdMobBootstrap()
        {
            if (IsDisabledForCi())
                return;

            // Run once per project. EditorPrefs is per-machine but that's fine:
            // re-running the patch on a fresh checkout is a no-op anyway.
            string marker = Application.dataPath + "::" + MarkerKey;
            if (EditorPrefs.GetBool(marker, false))
                return;

            try
            {
                if (EnsureRequiredPackagesInstalled())
                {
                    Debug.Log("[ZeyWinAds] Bootstrap added required ad/Firebase packages to Packages/manifest.json. " +
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
            if (EnsureRequiredPackagesInstalled())
            {
                AssetDatabase.Refresh();
                Debug.Log("[ZeyWinAds] Required ad/Firebase packages added to manifest.");
            }
            else
            {
                Debug.Log("[ZeyWinAds] Required ad/Firebase packages already present in manifest, nothing to do.");
            }
        }

        public static bool EnsureRequiredPackagesInstalled()
        {
            return PatchManifest();
        }

        /// <summary>
        /// Edits Packages/manifest.json to add the OpenUPM scoped registry and
        /// the com.google.ads.mobile dependency. Returns true if any modification
        /// was made.
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

            if (!legacyAdMobAssetsPresent && RemoveLegacyInstallReferrerAars())
            {
                modified = true;
            }

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

            if (!legacyAdMobAssetsPresent)
            {
                string adMobUpdated = UpsertDependency(content, AdMobPackage, AdMobVersion);
                if (adMobUpdated != content)
                {
                    content = adMobUpdated;
                    modified = true;
                }
            }

            if (!legacyAdMobAssetsPresent)
            {
                string edmUpdated = UpsertDependency(content, EdmPackage, EdmVersion);
                if (edmUpdated != content)
                {
                    content = edmUpdated;
                    modified = true;
                }
            }

            string firebaseAppUpdated = UpsertDependency(content, FirebaseAppPackage, FirebaseVersion);
            if (firebaseAppUpdated != content)
            {
                content = firebaseAppUpdated;
                modified = true;
            }

            string firebaseAnalyticsUpdated = UpsertDependency(content, FirebaseAnalyticsPackage, FirebaseVersion);
            if (firebaseAnalyticsUpdated != content)
            {
                content = firebaseAnalyticsUpdated;
                modified = true;
            }

            string firebaseRemoteConfigUpdated = UpsertDependency(content, FirebaseRemoteConfigPackage, FirebaseVersion);
            if (firebaseRemoteConfigUpdated != content)
            {
                content = firebaseRemoteConfigUpdated;
                modified = true;
            }

            string firebaseMessagingUpdated = UpsertDependency(content, FirebaseMessagingPackage, FirebaseVersion);
            if (firebaseMessagingUpdated != content)
            {
                content = firebaseMessagingUpdated;
                modified = true;
            }

            string scoped = legacyAdMobAssetsPresent
                ? RemoveScopeFromExistingRegistry(RemoveScopeFromExistingRegistry(content, AdMobPackage), EdmPackage)
                : EnsureScopedRegistry(content);
            if (scoped != content)
            {
                content = scoped;
                modified = true;
            }

            if (modified)
            {
                File.WriteAllText(manifestPath, content, new UTF8Encoding(false));
            }
            if (HasRealFirebaseDependencies(content))
                RemoveLegacyFirebaseStubs();
            return modified;
        }

        private static bool HasRealFirebaseDependencies(string manifest)
        {
            return manifest.Contains($"\"{FirebaseAppPackage}\"", StringComparison.Ordinal)
                || manifest.Contains($"\"{FirebaseRemoteConfigPackage}\"", StringComparison.Ordinal)
                || manifest.Contains($"\"{FirebaseAnalyticsPackage}\"", StringComparison.Ordinal)
                || manifest.Contains($"\"{FirebaseMessagingPackage}\"", StringComparison.Ordinal);
        }

        private static void RemoveLegacyFirebaseStubs()
        {
            string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", LegacyNoSdkStubsPath));
            if (!File.Exists(fullPath))
                return;

            File.Delete(fullPath);
            string metaPath = fullPath + ".meta";
            if (File.Exists(metaPath))
                File.Delete(metaPath);

            Debug.Log("[ZeyWinAds] Removed legacy Firebase SDK stubs because real Firebase packages are installed.");
        }

        private static bool LegacyAdMobAssetsPresent()
        {
            string assetsRoot = Application.dataPath;
            return File.Exists(Path.Combine(assetsRoot, "GoogleMobileAds", "GoogleMobileAds.Core.dll"))
                || File.Exists(Path.Combine(assetsRoot, "GoogleMobileAds", "Editor", "GoogleMobileAdsSettings.cs"))
                || Directory.Exists(Path.Combine(assetsRoot, "Plugins", "Android", "GoogleMobileAdsPlugin.androidlib"));
        }

        private static bool RemoveLegacyInstallReferrerAars()
        {
            string pluginsPath = Path.GetFullPath(Path.Combine(Application.dataPath, "Plugins", "Android"));
            if (!Directory.Exists(pluginsPath))
                return false;

            bool removedAny = false;
            string[] files = Directory.GetFiles(pluginsPath, "*.aar", SearchOption.AllDirectories);
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                if (!name.StartsWith("installreferrer-", StringComparison.OrdinalIgnoreCase) &&
                    !name.StartsWith("play-install-referrer-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                    string metaPath = file + ".meta";
                    if (File.Exists(metaPath))
                        File.Delete(metaPath);

                    removedAny = true;
                    string relative = AndroidPluginsPath + file.Substring(pluginsPath.Length).Replace('\\', '/');
                    Debug.Log($"[ZeyWinAds] Removed legacy local {relative}; Google Mobile Ads resolves installreferrer from Maven.");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ZeyWinAds] Could not remove legacy installreferrer AAR '{file}': {e.Message}");
                }
            }

            return removedAny;
        }

        private static bool IsDisabledForCi()
        {
            string value = Environment.GetEnvironmentVariable(DisableEnv);
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string AddDependency(string manifest, string package, string version)
        {
            if (!TryFindDependenciesBlock(manifest, out int braceIdx, out int closeIdx))
            {
                Debug.LogWarning("[ZeyWinAds] manifest.json is missing a \"dependencies\" block — " +
                                 $"cannot add {package}. Add it manually.");
                return manifest;
            }

            string body = manifest.Substring(braceIdx + 1, closeIdx - braceIdx - 1);
            string entry = string.IsNullOrWhiteSpace(body)
                ? $"\n    \"{package}\": \"{version}\"\n  "
                : $"\n    \"{package}\": \"{version}\",";
            return manifest.Insert(braceIdx + 1, entry);
        }

        private static string UpsertDependency(string manifest, string package, string version)
        {
            if (!TryFindDependenciesBlock(manifest, out int braceIdx, out int closeIdx))
                return AddDependency(manifest, package, version);

            string key = $"\"{package}\"";
            int keyIdx = manifest.IndexOf(key, braceIdx, closeIdx - braceIdx, StringComparison.Ordinal);
            if (keyIdx < 0)
                return AddDependency(manifest, package, version);

            int colonIdx = manifest.IndexOf(':', keyIdx, closeIdx - keyIdx);
            if (colonIdx < 0)
                return manifest;

            int valueStart = manifest.IndexOf('"', colonIdx + 1, closeIdx - colonIdx - 1);
            if (valueStart < 0)
                return manifest;

            int valueEnd = manifest.IndexOf('"', valueStart + 1, closeIdx - valueStart - 1);
            if (valueEnd < 0)
                return manifest;

            string current = manifest.Substring(valueStart + 1, valueEnd - valueStart - 1);
            if (current == version)
                return manifest;

            return manifest.Substring(0, valueStart + 1) + version + manifest.Substring(valueEnd);
        }

        private static string RemoveDependency(string manifest, string package)
        {
            if (!TryFindDependenciesBlock(manifest, out int braceIdx, out int closeIdx))
                return manifest;

            string key = $"\"{package}\"";
            int keyIdx = manifest.IndexOf(key, braceIdx, closeIdx - braceIdx, StringComparison.Ordinal);
            if (keyIdx < 0)
                return manifest;

            int lineStart = manifest.LastIndexOf('\n', keyIdx);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;

            int lineEnd = manifest.IndexOf('\n', keyIdx);
            if (lineEnd < 0)
                lineEnd = manifest.Length;
            else
                lineEnd += 1;

            return NormalizeDanglingCommas(manifest.Remove(lineStart, lineEnd - lineStart));
        }

        private static bool TryFindDependenciesBlock(string manifest, out int braceIdx, out int closeIdx)
        {
            braceIdx = -1;
            closeIdx = -1;

            int depsIdx = manifest.IndexOf("\"dependencies\"", StringComparison.Ordinal);
            if (depsIdx < 0)
                return false;

            braceIdx = manifest.IndexOf('{', depsIdx);
            if (braceIdx < 0)
                return false;

            closeIdx = FindMatchingBrace(manifest, braceIdx);
            return closeIdx >= 0;
        }

        private static int FindMatchingBrace(string text, int openIdx)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = openIdx; i < text.Length; i++)
            {
                char c = text[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }
                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }
                if (inString)
                    continue;

                if (c == '{')
                    depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        private static string NormalizeDanglingCommas(string manifest)
        {
            return manifest
                .Replace(",\n  }", "\n  }")
                .Replace(",\n    }", "\n    }")
                .Replace(",\n      }", "\n      }")
                .Replace("{\n,", "{\n")
                .Replace("[\n,", "[\n");
        }

        private static string EnsureScopedRegistry(string manifest)
        {
            string updated = EnsureScopedRegistryEntry(
                manifest,
                RegistryName,
                RegistryUrl,
                new[] { AdMobPackage, EdmPackage });
            updated = EnsureScopedRegistryEntry(
                updated,
                FirebaseRegistryName,
                FirebaseRegistryUrl,
                new[] { FirebaseRegistryScope });
            return updated;
        }

        private static string EnsureScopedRegistryEntry(string manifest, string name, string url, string[] scopes)
        {
            if (manifest.Contains($"\"{url}\""))
            {
                string updated = manifest;
                foreach (string scope in scopes)
                    updated = AddScopeToRegistry(updated, url, scope);
                return updated;
            }

            string registryObject = BuildRegistryObject(name, url, scopes);
            int scopedIdx = manifest.IndexOf("\"scopedRegistries\"", StringComparison.Ordinal);
            if (scopedIdx >= 0)
            {
                int arrayStart = manifest.IndexOf('[', scopedIdx);
                if (arrayStart < 0)
                    return manifest;

                int arrayEnd = FindMatchingBracket(manifest, arrayStart);
                if (arrayEnd < 0)
                    return manifest;

                string existing = manifest.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
                string insert = string.IsNullOrWhiteSpace(existing)
                    ? "\n    " + registryObject + "\n  "
                    : "\n    " + registryObject + ",";
                return manifest.Insert(arrayStart + 1, insert);
            }

            int depsIdx = manifest.IndexOf("\"dependencies\"", StringComparison.Ordinal);
            if (depsIdx < 0)
                return manifest;

            string registryBlock =
                "  \"scopedRegistries\": [\n" +
                "    " + registryObject + "\n" +
                "  ],\n";
            int insertAt = manifest.LastIndexOf('\n', depsIdx) + 1;
            return manifest.Insert(insertAt, registryBlock);
        }

        private static string BuildRegistryObject(string name, string url, string[] scopes)
        {
            var builder = new StringBuilder();
            builder.Append("{\n");
            builder.Append($"      \"name\": \"{name}\",\n");
            builder.Append($"      \"url\": \"{url}\",\n");
            builder.Append("      \"scopes\": [\n");
            for (int i = 0; i < scopes.Length; i++)
            {
                string comma = i == scopes.Length - 1 ? "" : ",";
                builder.Append($"        \"{scopes[i]}\"{comma}\n");
            }
            builder.Append("      ]\n");
            builder.Append("    }");
            return builder.ToString();
        }

        private static string AddScopeToRegistry(string manifest, string registryUrl, string scope)
        {
            int registryIdx = manifest.IndexOf($"\"{registryUrl}\"", StringComparison.Ordinal);
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

        private static int FindMatchingBracket(string text, int openIdx)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = openIdx; i < text.Length; i++)
            {
                char c = text[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }
                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }
                if (inString)
                    continue;

                if (c == '[')
                    depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        private static string RemoveScopeFromExistingRegistry(string manifest, string scope)
        {
            string key = $"\"{scope}\"";
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

            return NormalizeDanglingCommas(manifest.Remove(lineStart, lineEnd - lineStart));
        }
    }
}
