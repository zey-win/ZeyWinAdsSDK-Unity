using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ZeyWinAds.Editor
{
    /// <summary>
    /// Patches older game-side AdMobProvider scripts so direct Google banner
    /// creation cannot run under a ZeyWin-owned WebView, popup, or banner.
    /// New SDK integrations use AdMediator directly; this keeps legacy games
    /// consistent after simply installing/updating the package.
    /// </summary>
    [InitializeOnLoad]
    public static class LegacyAdMobProviderPatcher
    {
        private const string Marker = "ZeyWinAds legacy AdMob banner guard";

        static LegacyAdMobProviderPatcher()
        {
            EditorApplication.delayCall += () => Apply(logWhenNoChanges: false);
        }

        [MenuItem("ZeyWinAds/Patch Legacy AdMobProvider", priority = 11)]
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
            foreach (string path in Directory.GetFiles(assetsRoot, "AdMobProvider.cs", SearchOption.AllDirectories))
            {
                modifiedAny |= PatchProvider(path);
            }

            if (modifiedAny)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
            else if (logWhenNoChanges)
            {
                Debug.Log("[ZeyWinAds] No legacy AdMobProvider.cs files needed patching.");
            }

            return modifiedAny;
        }

        private static bool PatchProvider(string fullPath)
        {
            string text = File.ReadAllText(fullPath);
            if (text.Contains(Marker))
                return false;

            if (!text.Contains("class AdMobProvider") || !text.Contains("GoogleMobileAds.Api") || !text.Contains("BannerView"))
                return false;

            if (text.Contains("SuppressBannerForZeyWinSurface"))
            {
                Debug.Log($"[ZeyWinAds] Legacy AdMobProvider already has banner suppression in {ToAssetPath(fullPath)}.");
                return false;
            }

            string original = text;
            int guardCount = 0;

            if (!text.Contains("using ZeyWinAds.Mediation;"))
                text = text.Replace("using GoogleMobileAds.Common;\n", "using GoogleMobileAds.Common;\nusing ZeyWinAds.Mediation;\n");

            text = PatchBannerReady(text, ref guardCount);
            text = InsertHelper(text, ref guardCount);
            text = ReplaceOnce(text,
                "        _bannerVisible = show;\n\n        if (_bannerView != null",
                "        _bannerVisible = show;\n\n        if (SuppressBannerForZeyWinSurface(\"load\"))\n            return;\n\n        if (_bannerView != null",
                ref guardCount);
            text = ReplaceOnce(text,
                "        _bannerVisible = true;\n\n        if (NoAdsManager.IsOwned)",
                "        _bannerVisible = true;\n\n        if (SuppressBannerForZeyWinSurface(\"show\"))\n            return;\n\n        if (NoAdsManager.IsOwned)",
                ref guardCount);
            text = ReplaceOnce(text,
                "        if (!_initialized) return;\n        if (NoAdsManager.IsOwned) return;\n\n        if (_bannerView == null)",
                "        if (!_initialized) return;\n        if (NoAdsManager.IsOwned) return;\n        if (SuppressBannerForZeyWinSurface(\"ensure\"))\n            return;\n\n        if (_bannerView == null)",
                ref guardCount);
            text = ReplaceOnce(text,
                "        while (_bannerVisible && !NoAdsManager.IsOwned)\n        {\n            EnsureBannerVisibleNow();",
                "        while (_bannerVisible && !NoAdsManager.IsOwned)\n        {\n            if (SuppressBannerForZeyWinSurface(\"ensure loop\"))\n                yield break;\n\n            EnsureBannerVisibleNow();",
                ref guardCount);
            text = ReplaceOnce(text,
                "                _bannerIsLoading = false;\n                _bannerLoaded = true;",
                "                _bannerIsLoading = false;\n\n                if (SuppressBannerForZeyWinSurface(\"late load callback\"))\n                    return;\n\n                _bannerLoaded = true;",
                ref guardCount);
            text = ReplaceOnce(text,
                "        _bannerRetryCoroutine = null;\n\n        if (_bannerVisible && !NoAdsManager.IsOwned)",
                "        _bannerRetryCoroutine = null;\n\n        if (SuppressBannerForZeyWinSurface(\"retry\"))\n            yield break;\n\n        if (_bannerVisible && !NoAdsManager.IsOwned)",
                ref guardCount);
            text = ReplaceOnce(text,
                "        if (!_initialized) return;\n        if (_bannerIsLoading) return;\n        if (NoAdsManager.IsOwned) return;\n\n        if (_bannerView != null && _bannerLoaded)",
                "        if (!_initialized) return;\n        if (_bannerIsLoading) return;\n        if (NoAdsManager.IsOwned) return;\n        if (SuppressBannerForZeyWinSurface(\"preload\"))\n            return;\n\n        if (_bannerView != null && _bannerLoaded)",
                ref guardCount);

            if (guardCount < 5)
            {
                Debug.LogWarning($"[ZeyWinAds] Legacy AdMobProvider patch skipped for {ToAssetPath(fullPath)}; unsupported provider shape.");
                return false;
            }

            if (text == original)
                return false;

            File.WriteAllText(fullPath, text, new UTF8Encoding(false));
            Debug.Log($"[ZeyWinAds] Patched legacy AdMobProvider banner suppression in {ToAssetPath(fullPath)}.");
            return true;
        }

        private static string PatchBannerReady(string text, ref int guardCount)
        {
            const string oldLine = "    public bool BannerIsReady => _bannerView != null && _bannerLoaded && !_bannerIsLoading;";
            const string newLine = "    public bool BannerIsReady => _bannerView != null && _bannerLoaded && !_bannerIsLoading && !AdMediator.IsZeyWinSurfaceActive;";
            return ReplaceOnce(text, oldLine, newLine, ref guardCount);
        }

        private static string InsertHelper(string text, ref int guardCount)
        {
            if (text.Contains("SuppressBannerForZeyWinSurface"))
                return text;

            const string anchor = "    public void LoadBannerAd(bool show = true)\n";
            int index = text.IndexOf(anchor, StringComparison.Ordinal);
            if (index < 0)
                return text;

            string helper =
                "    // " + Marker + "\n" +
                "    private bool SuppressBannerForZeyWinSurface(string source)\n" +
                "    {\n" +
                "        if (!AdMediator.IsZeyWinSurfaceActive)\n" +
                "            return false;\n\n" +
                "        _bannerIsLoading = false;\n" +
                "        _bannerLoaded = false;\n" +
                "        StopBannerEnsureLoop();\n" +
                "        DestroyBannerAd();\n" +
                "        Debug.Log($\"[AdMob] Banner {source} suppressed while ZeyWin surface is active.\");\n" +
                "        return true;\n" +
                "    }\n\n";

            guardCount++;
            return text.Insert(index, helper);
        }

        private static string ReplaceOnce(string text, string search, string replacement, ref int guardCount)
        {
            int index = text.IndexOf(search, StringComparison.Ordinal);
            if (index < 0)
                return text;

            guardCount++;
            return text.Remove(index, search.Length).Insert(index, replacement);
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
