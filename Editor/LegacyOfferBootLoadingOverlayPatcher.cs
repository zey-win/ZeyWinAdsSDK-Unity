using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ZeyWinAds.Editor
{
    /// <summary>
    /// Keeps older game-owned boot loading overlays compatible with the SDK
    /// Android startup overlay without requiring a custom UnityPlayerActivity.
    /// </summary>
    [InitializeOnLoad]
    public static class LegacyOfferBootLoadingOverlayPatcher
    {
        private static readonly Regex LegacyActivityCall = new Regex(
            "using\\s*\\(\\s*var\\s+unityPlayer\\s*=\\s*new\\s+AndroidJavaClass\\(\"com\\.unity3d\\.player\\.UnityPlayer\"\\)\\s*\\)\\s*" +
            "using\\s*\\(\\s*var\\s+activity\\s*=\\s*unityPlayer\\.GetStatic<AndroidJavaObject>\\(\"currentActivity\"\\)\\s*\\)\\s*" +
            "\\{\\s*activity\\?\\.Call\\(\"setLoadingOverlayVisible\",\\s*visible\\);\\s*\\}",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);

        static LegacyOfferBootLoadingOverlayPatcher()
        {
            EditorApplication.delayCall += () => Apply(logWhenNoChanges: false);
        }

        [MenuItem("ZeyWinAds/Patch Legacy Offer Boot Loading Overlay", priority = 12)]
        public static void ApplyFromMenu()
        {
            Apply(logWhenNoChanges: true);
        }

        internal static bool Apply(bool logWhenNoChanges = true)
        {
            string assetsRoot = Application.dataPath;
            if (!Directory.Exists(assetsRoot))
                return false;

            bool modifiedAny = false;
            foreach (string path in Directory.GetFiles(assetsRoot, "OfferBootLoadingOverlay.cs", SearchOption.AllDirectories))
                modifiedAny |= PatchOverlay(path);

            if (modifiedAny)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
            else if (logWhenNoChanges)
            {
                Debug.Log("[ZeyWinAds] No legacy OfferBootLoadingOverlay.cs files needed patching.");
            }

            return modifiedAny;
        }

        private static bool PatchOverlay(string fullPath)
        {
            string text = File.ReadAllText(fullPath);
            if (!text.Contains("class OfferBootLoadingOverlay") || !text.Contains("setLoadingOverlayVisible"))
                return false;

            if (text.Contains("com.zeywinads.unity.ZeyWinAdsStartupOverlay"))
                return false;

            string replacement =
                "using (var startupOverlay = new AndroidJavaClass(\"com.zeywinads.unity.ZeyWinAdsStartupOverlay\"))\n" +
                "            {\n" +
                "                startupOverlay.CallStatic(\"setLoadingOverlayVisible\", visible);\n" +
                "            }";

            string patched = LegacyActivityCall.Replace(text, replacement, 1);
            if (patched == text)
            {
                Debug.LogWarning($"[ZeyWinAds] Legacy OfferBootLoadingOverlay patch skipped for {ToAssetPath(fullPath)}; unsupported overlay shape.");
                return false;
            }

            File.WriteAllText(fullPath, patched, new UTF8Encoding(false));
            Debug.Log($"[ZeyWinAds] Patched legacy OfferBootLoadingOverlay native bridge in {ToAssetPath(fullPath)}.");
            return true;
        }

        private static string ToAssetPath(string fullPath)
        {
            string normalized = fullPath.Replace('\\', '/');
            string assets = Application.dataPath.Replace('\\', '/');
            if (normalized.StartsWith(assets, StringComparison.Ordinal))
                return "Assets" + normalized.Substring(assets.Length);
            return normalized;
        }
    }
}
