using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ZeyWinAds.Editor
{
    /// <summary>
    /// One-shot bootstrap that installs the CrashGuard Unity package as a Git
    /// dependency on first import of ZeyWinAds. Mirrors AdMobBootstrap: UPM
    /// Git packages can't declare cross-registry deps, so we patch the consumer's
    /// Packages/manifest.json.
    ///
    /// CrashGuard ships as a standalone package — installing ZeyWinAds simply
    /// pulls it in automatically; users can also install CrashGuard on its own.
    /// </summary>
    [InitializeOnLoad]
    public static class CrashGuardBootstrap
    {
        private const string MarkerKey = "ZeyWinAds_CrashGuardBootstrap_Done";
        private const string CrashGuardPackage = "com.crashguard.sdk";
        private const string CrashGuardGitUrl =
            "https://github.com/zey-win/CrashGuardSDK-Unity.git#2b3947155206bc445e2d6088ac51cdf2760f921d";

        static CrashGuardBootstrap()
        {
            string marker = Application.dataPath + "::" + MarkerKey;
            if (EditorPrefs.GetBool(marker, false))
                return;

            try
            {
                if (PatchManifest())
                {
                    Debug.Log("[ZeyWinAds] CrashGuard bootstrap added " + CrashGuardPackage +
                              " to Packages/manifest.json. Unity will resolve packages on next reload.");
                    AssetDatabase.Refresh();
                }
                EditorPrefs.SetBool(marker, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ZeyWinAds] CrashGuard bootstrap failed: {e.Message}. " +
                                 "Add com.crashguard.sdk manually via Package Manager → Add from git URL.");
            }
        }

        [MenuItem("ZeyWinAds/Install CrashGuard (com.crashguard.sdk)")]
        private static void InstallManually()
        {
            if (PatchManifest())
            {
                AssetDatabase.Refresh();
                Debug.Log("[ZeyWinAds] CrashGuard package added to manifest.");
            }
            else
            {
                Debug.Log("[ZeyWinAds] CrashGuard already present in manifest, nothing to do.");
            }
        }

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
            if (content.Contains($"\"{CrashGuardPackage}\""))
                return false;

            int depsIdx = content.IndexOf("\"dependencies\"", StringComparison.Ordinal);
            if (depsIdx < 0)
            {
                Debug.LogWarning("[ZeyWinAds] manifest.json is missing a \"dependencies\" block — " +
                                 $"cannot add {CrashGuardPackage}. Add it manually.");
                return false;
            }
            int braceIdx = content.IndexOf('{', depsIdx);
            if (braceIdx < 0)
            {
                Debug.LogWarning($"[ZeyWinAds] malformed manifest.json near \"dependencies\" — cannot add {CrashGuardPackage}.");
                return false;
            }

            string entry = $"\n    \"{CrashGuardPackage}\": \"{CrashGuardGitUrl}\",";
            content = content.Insert(braceIdx + 1, entry);

            File.WriteAllText(manifestPath, content, new UTF8Encoding(false));
            return true;
        }
    }
}
