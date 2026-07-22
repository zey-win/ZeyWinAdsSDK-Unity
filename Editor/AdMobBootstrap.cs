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
        private const string MarkerKey = "ZeyWinAds_AdMobBootstrap_Done_v3";
        private const string RegistryName = "package.openupm.com";
        private const string RegistryUrl = "https://package.openupm.com";
        private const string DisableEnv = "ZEYWIN_DISABLE_ADMOB_BOOTSTRAP";
        private const string AdMobPackage = "com.google.ads.mobile";
        private const string AdMobVersion = "10.5.0";
        private const string EdmPackage = "com.google.external-dependency-manager";
        private const string EdmVersion = "1.2.186";
        private const string FirebaseLocalEnv = "ZEYWIN_FIREBASE_SDK_DIR";
        private const string DefaultFirebaseLocalPath = "/Users/admin/Documents/Data/Unity/sdk/FireBase";
        private const string AndroidPluginsPath = "Assets/Plugins/Android";
        private const string AndroidGoogleServicesPath = "Assets/Plugins/Android/google-services.json";
        private const string LegacyNoSdkStubsPath = "Assets/Scripts/SDKStubs/NoSdkStubs.cs";
        // FirebaseMessaging is intentionally not listed here — it now ships
        // vendored inside ThirdParty/Firebase/ and is always present for any
        // consumer of this package, so it no longer needs this local-folder
        // import mechanism (and never reliably worked for it anyway, see
        // ImportLocalFirebaseUnityPackages).
        private static readonly string[] FirebaseUnityPackageNames =
        {
            "FirebaseAnalytics_13.0.0.unitypackage",
            "FirebaseRemoteConfig_13.0.0.unitypackage"
        };
        private static readonly string[] GoogleServicesCandidatePaths =
        {
            "Assets/Plugins/Android/google-services.json",
            "Assets/google-services.json",
            "Assets/StreamingAssets/google-services-desktop.json",
            "google-services.json"
        };

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
            bool changed = PatchManifest();
            changed |= EnsureGoogleServicesJsonPreserved();
            changed |= ImportLocalFirebaseUnityPackages();
            changed |= EnsureFirebasePythonScriptsCopied();
            return changed;
        }

        /// <summary>
        /// Firebase's own compiled Editor tooling (Firebase.Editor.dll) invokes a
        /// couple of small Python helper scripts by a hardcoded path relative to
        /// the project root — "Assets/Firebase/Editor/&lt;script&gt;.py" — assuming
        /// the classic .unitypackage-style install where Firebase's Editor folder
        /// sits directly under Assets. Since we vendor Firebase under
        /// ThirdParty/Firebase/ inside this package instead (for portability
        /// across file:/git/registry references), that hardcoded lookup fails
        /// with "No such file or directory" for anyone not using the legacy
        /// layout. This copies just those loose utility scripts (plain Python
        /// text, not compiled code, so no duplicate-assembly risk) to the exact
        /// Assets-rooted path Firebase's tooling expects — the rest of Firebase
        /// (DLLs, native libs, asmdef-relevant content) stays vendored as-is.
        /// </summary>
        private static bool EnsureFirebasePythonScriptsCopied([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            string editorDir = Path.GetDirectoryName(sourceFilePath);
            string packageRoot = Path.GetDirectoryName(editorDir);
            string sourceScriptsDir = Path.Combine(packageRoot, "ThirdParty", "Firebase", "Editor");

            if (!Directory.Exists(sourceScriptsDir))
                return false;

            string targetScriptsDir = Path.Combine(Application.dataPath, "Firebase", "Editor");
            bool changed = false;

            foreach (string sourceScript in Directory.GetFiles(sourceScriptsDir, "*.py"))
            {
                string fileName = Path.GetFileName(sourceScript);
                string targetScript = Path.Combine(targetScriptsDir, fileName);

                if (File.Exists(targetScript) && FilesAreIdentical(sourceScript, targetScript))
                    continue;

                Directory.CreateDirectory(targetScriptsDir);
                File.Copy(sourceScript, targetScript, overwrite: true);

                string sourceMeta = sourceScript + ".meta";
                if (File.Exists(sourceMeta))
                    File.Copy(sourceMeta, targetScript + ".meta", overwrite: true);

                changed = true;
                Debug.Log($"[ZeyWinAds] Copied Firebase Editor helper script: Assets/Firebase/Editor/{fileName}");
            }

            return changed;
        }

        private static bool FilesAreIdentical(string a, string b)
        {
            byte[] bytesA = File.ReadAllBytes(a);
            byte[] bytesB = File.ReadAllBytes(b);
            if (bytesA.Length != bytesB.Length)
                return false;

            for (int i = 0; i < bytesA.Length; i++)
            {
                if (bytesA[i] != bytesB[i])
                    return false;
            }

            return true;
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
            if (HasFirebaseAssetsInstalled())
                RemoveLegacyFirebaseStubs();
            return modified;
        }

        private static bool EnsureGoogleServicesJsonPreserved()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string androidTarget = Path.Combine(projectRoot, AndroidGoogleServicesPath);

            if (File.Exists(androidTarget))
            {
                Debug.Log("[ZeyWinAds] Existing Assets/Plugins/Android/google-services.json preserved.");
                return false;
            }

            string source = FindExistingGoogleServicesJson(projectRoot, androidTarget);
            if (string.IsNullOrEmpty(source))
            {
                Debug.Log("[ZeyWinAds] google-services.json not found in project; Firebase packages are installed but Firebase runtime requires the app-specific file.");
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(androidTarget));
            File.Copy(source, androidTarget, overwrite: false);
            Debug.Log($"[ZeyWinAds] Copied existing Firebase config into Android build: {ToUnityPath(source, projectRoot)} -> {AndroidGoogleServicesPath}");
            return true;
        }

        private static string FindExistingGoogleServicesJson(string projectRoot, string androidTarget)
        {
            foreach (string candidate in GoogleServicesCandidatePaths)
            {
                string fullPath = Path.GetFullPath(Path.Combine(projectRoot, candidate));
                if (PathsEqual(fullPath, androidTarget))
                    continue;

                if (File.Exists(fullPath))
                    return fullPath;
            }

            try
            {
                foreach (string fullPath in Directory.GetFiles(projectRoot, "google-services.json", SearchOption.AllDirectories))
                {
                    if (fullPath.IndexOf($"{Path.DirectorySeparatorChar}Library{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                    if (fullPath.IndexOf($"{Path.DirectorySeparatorChar}Temp{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                    if (fullPath.IndexOf($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                    if (PathsEqual(fullPath, androidTarget))
                        continue;

                    return fullPath;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ZeyWinAds] Failed while searching google-services.json: {e.Message}");
            }

            return null;
        }

        private static bool ImportLocalFirebaseUnityPackages()
        {
            if (HasFirebaseAssetsInstalled())
            {
                RemoveLegacyFirebaseStubs();
                return false;
            }

            string firebaseRoot = Environment.GetEnvironmentVariable(FirebaseLocalEnv);
            if (string.IsNullOrWhiteSpace(firebaseRoot))
                firebaseRoot = DefaultFirebaseLocalPath;

            if (string.IsNullOrWhiteSpace(firebaseRoot) || !Directory.Exists(firebaseRoot))
            {
                Debug.Log("[ZeyWinAds] Firebase SDK folder not found; set ZEYWIN_FIREBASE_SDK_DIR or install Firebase Unity packages in CI.");
                return false;
            }

            bool importedAny = false;
            foreach (string packageName in FirebaseUnityPackageNames)
            {
                string packagePath = FindFileByName(firebaseRoot, packageName);
                if (string.IsNullOrEmpty(packagePath))
                {
                    Debug.LogWarning($"[ZeyWinAds] Firebase Unity package not found in {firebaseRoot}: {packageName}");
                    continue;
                }

                AssetDatabase.ImportPackage(packagePath, interactive: false);
                importedAny = true;
                Debug.Log($"[ZeyWinAds] Imported Firebase Unity package: {packagePath}");
            }

            if (importedAny)
            {
                AssetDatabase.Refresh();
                RemoveLegacyFirebaseStubs();
            }

            return importedAny;
        }

        // Messaging is deliberately excluded from this check — it's vendored
        // inside ThirdParty/Firebase/ (see FirebaseUnityPackageNames above),
        // not something this method needs to detect/import.
        private static bool HasFirebaseAssetsInstalled()
        {
            string assetsRoot = Application.dataPath;
            return File.Exists(Path.Combine(assetsRoot, "Firebase", "Plugins", "Firebase.App.dll"))
                && File.Exists(Path.Combine(assetsRoot, "Firebase", "Plugins", "Firebase.RemoteConfig.dll"))
                && File.Exists(Path.Combine(assetsRoot, "Firebase", "Plugins", "Firebase.Analytics.dll"));
        }

        private static string FindFileByName(string root, string fileName)
        {
            try
            {
                foreach (string path in Directory.GetFiles(root, fileName, SearchOption.AllDirectories))
                    return path;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ZeyWinAds] Failed while searching Firebase package {fileName}: {e.Message}");
            }

            return null;
        }

        private static bool PathsEqual(string a, string b)
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string ToUnityPath(string fullPath, string projectRoot)
        {
            string relative = fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(projectRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : fullPath;
            return relative.Replace('\\', '/');
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
            return EnsureScopedRegistryEntry(
                manifest,
                RegistryName,
                RegistryUrl,
                new[] { AdMobPackage, EdmPackage });
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
