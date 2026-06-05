using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ZeyWinAds.Editor
{
    /// <summary>
    /// Patches older game-side AdMobProvider scripts so direct Google ad
    /// display cannot run under a ZeyWin-owned WebView, popup, or banner.
    /// New SDK integrations use AdMediator directly; this keeps legacy games
    /// consistent after simply installing/updating the package.
    /// </summary>
    [InitializeOnLoad]
    public static class LegacyAdMobProviderPatcher
    {
        private const string Marker = "ZeyWinAds legacy AdMob surface guard";

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
            if (!text.Contains("class AdMobProvider") || !text.Contains("GoogleMobileAds.Api") || !text.Contains("BannerView"))
                return false;

            bool hadHelper = text.Contains("SuppressBannerForZeyWinSurface");
            string original = text;
            int guardCount = 0;

            if (!text.Contains("using ZeyWinAds.Mediation;"))
            {
                if (text.Contains("using GoogleMobileAds.Common;\n"))
                    text = text.Replace("using GoogleMobileAds.Common;\n", "using GoogleMobileAds.Common;\nusing ZeyWinAds.Mediation;\n");
                else
                    text = text.Replace("using GoogleMobileAds.Api;\n", "using GoogleMobileAds.Api;\nusing ZeyWinAds.Mediation;\n");
            }

            text = PatchBannerReady(text, ref guardCount);
            text = PatchFullscreenReady(text, ref guardCount);
            text = InsertHelper(text, ref guardCount);
            text = PatchAutoRetryDelays(text, ref guardCount);
            text = PatchInterstitialAutoShowCooldown(text, ref guardCount);
            text = PatchFullscreenLoadSuppression(text, ref guardCount);
            text = PatchFullscreenSurfaceSuppression(text, ref guardCount);
            text = PatchSimpleAdMobProvider(text, ref guardCount);
            text = ReplaceOnce(text,
                "        _bannerVisible = show;\n\n        if (_bannerView != null",
                "        _bannerVisible = show;\n\n        if (SuppressBannerForZeyWinSurface(\"load\"))\n            return;\n\n        if (_bannerView != null",
                ref guardCount);
            text = ReplaceOnce(text,
                "        _bannerVisible = show;\n\n        //",
                "        _bannerVisible = show;\n\n        if (SuppressBannerForZeyWinSurface(\"load\"))\n            return;\n\n        //",
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
                "            _bannerIsLoading = false;\n            _bannerLoaded = true;",
                "            _bannerIsLoading = false;\n\n            if (SuppressBannerForZeyWinSurface(\"late load callback\"))\n                return;\n\n            _bannerLoaded = true;",
                ref guardCount);
            text = ReplaceOnce(text,
                "        _bannerRetryCoroutine = null;\n\n        if (_bannerVisible && !NoAdsManager.IsOwned)",
                "        _bannerRetryCoroutine = null;\n\n        if (SuppressBannerForZeyWinSurface(\"retry\"))\n            yield break;\n\n        if (_bannerVisible && !NoAdsManager.IsOwned)",
                ref guardCount);
            text = ReplaceOnce(text,
                "        if (!_initialized) return;\n        if (_bannerIsLoading) return;\n        if (NoAdsManager.IsOwned) return;\n\n        if (_bannerView != null && _bannerLoaded)",
                "        if (!_initialized) return;\n        if (_bannerIsLoading) return;\n        if (NoAdsManager.IsOwned) return;\n        if (SuppressBannerForZeyWinSurface(\"preload\"))\n            return;\n\n        if (_bannerView != null && _bannerLoaded)",
                ref guardCount);

            if (!hadHelper && guardCount < 5)
            {
                Debug.LogWarning($"[ZeyWinAds] Legacy AdMobProvider patch skipped for {ToAssetPath(fullPath)}; unsupported provider shape.");
                return false;
            }

            if (text == original)
                return false;

            File.WriteAllText(fullPath, text, new UTF8Encoding(false));
            Debug.Log($"[ZeyWinAds] Patched legacy AdMobProvider surface suppression in {ToAssetPath(fullPath)}.");
            return true;
        }

        private static string PatchBannerReady(string text, ref int guardCount)
        {
            const string oldLine = "    public bool BannerIsReady => _bannerView != null && _bannerLoaded && !_bannerIsLoading;";
            const string newLine = "    public bool BannerIsReady => _bannerView != null && _bannerLoaded && !_bannerIsLoading && !AdMediator.IsZeyWinSurfaceActive;";
            text = ReplaceOnce(text, oldLine, newLine, ref guardCount);
            text = ReplaceOnce(text,
                "    public bool BannerIsReady => _initialized && _banner != null && _bannerLoaded;",
                "    public bool BannerIsReady => _initialized && _banner != null && _bannerLoaded && !AdMediator.IsZeyWinSurfaceActive;",
                ref guardCount);
            return text;
        }

        private static string PatchFullscreenReady(string text, ref int guardCount)
        {
            text = ReplaceOnce(text,
                "    public bool IsInterstitialReady => _initialized && _interstitial != null && _interstitial.CanShowAd();",
                "    public bool IsInterstitialReady => _initialized && !AdMediator.IsZeyWinSurfaceActive && _interstitial != null && _interstitial.CanShowAd();",
                ref guardCount);
            text = ReplaceOnce(text,
                "    public bool IsRewardedReady => _initialized && _rewarded != null && _rewarded.CanShowAd();",
                "    public bool IsRewardedReady => _initialized && !AdMediator.IsZeyWinSurfaceActive && _rewarded != null && _rewarded.CanShowAd();",
                ref guardCount);
            return text;
        }

        private static string InsertHelper(string text, ref int guardCount)
        {
            if (text.Contains("SuppressBannerForZeyWinSurface"))
                return text;

            string anchor = text.Contains("    public void LoadBannerAd(bool show = true)\n")
                ? "    public void LoadBannerAd(bool show = true)\n"
                : "    public void PreloadBannerAd()\n";
            int index = text.IndexOf(anchor, StringComparison.Ordinal);
            if (index < 0)
                return text;

            string helper = text.Contains("_bannerLoading") && text.Contains("DestroyBanner()")
                ? BuildSimpleProviderHelper()
                : BuildLegacyProviderHelper();

            guardCount++;
            return text.Insert(index, helper);
        }

        private static string BuildLegacyProviderHelper()
        {
            return
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
        }

        private static string BuildSimpleProviderHelper()
        {
            return
                "    // " + Marker + "\n" +
                "    private bool SuppressBannerForZeyWinSurface(string source)\n" +
                "    {\n" +
                "        if (!AdMediator.IsZeyWinSurfaceActive)\n" +
                "            return false;\n\n" +
                "        _bannerLoading = false;\n" +
                "        _bannerLoaded = false;\n" +
                "        if (_bannerRetry != null)\n" +
                "        {\n" +
                "            StopCoroutine(_bannerRetry);\n" +
                "            _bannerRetry = null;\n" +
                "        }\n" +
                "        DestroyBanner();\n" +
                "        Debug.Log($\"[AdMobProvider] Banner {source} suppressed while ZeyWin surface is active.\");\n" +
                "        return true;\n" +
                "    }\n\n";
        }

        private static string PatchSimpleAdMobProvider(string text, ref int guardCount)
        {
            text = ReplaceOnce(text,
                "        if (!_initialized || !_adsEnabled || _bannerLoading || string.IsNullOrEmpty(_bannerId))\n            return;\n\n        if (BannerIsReady)",
                "        if (!_initialized || !_adsEnabled || _bannerLoading || string.IsNullOrEmpty(_bannerId))\n            return;\n\n        if (SuppressBannerForZeyWinSurface(\"preload\"))\n            return;\n\n        if (BannerIsReady)",
                ref guardCount);
            text = ReplaceOnce(text,
                "            _bannerLoading = false;\n            _bannerLoaded = true;",
                "            _bannerLoading = false;\n\n            if (SuppressBannerForZeyWinSurface(\"late load callback\"))\n                return;\n\n            _bannerLoaded = true;",
                ref guardCount);
            text = ReplaceOnce(text,
                "        if (!_adsEnabled)\n            return;\n\n        if (BannerIsReady)",
                "        if (!_adsEnabled)\n            return;\n\n        if (SuppressBannerForZeyWinSurface(\"show\"))\n            return;\n\n        if (BannerIsReady)",
                ref guardCount);
            text = ReplaceOnce(text,
                "        if (_bannerRetry == null && _adsEnabled)\n            _bannerRetry = StartCoroutine(RetryRoutine(() =>",
                "        if (SuppressBannerForZeyWinSurface(\"retry\"))\n            return;\n\n        if (_bannerRetry == null && _adsEnabled)\n            _bannerRetry = StartCoroutine(RetryRoutine(() =>",
                ref guardCount);
            return text;
        }

        private static string PatchAutoRetryDelays(string text, ref int guardCount)
        {
            text = ReplaceOnce(text,
                "        var delay = Mathf.Min(60f, Mathf.Pow(2f, _interstitialLoadAttempt));",
                "        var delay = Mathf.Min(300f, Mathf.Max(AdMediator.MinimumAdMobAutoRetrySeconds, Mathf.Pow(2f, _interstitialLoadAttempt)));",
                ref guardCount);
            text = ReplaceOnce(text,
                "        var delay = Mathf.Min(60f, Mathf.Pow(2f, _bannerLoadAttempt));",
                "        var delay = Mathf.Min(300f, Mathf.Max(AdMediator.MinimumAdMobAutoRetrySeconds, Mathf.Pow(2f, _bannerLoadAttempt)));",
                ref guardCount);
            return text;
        }

        private static string PatchInterstitialAutoShowCooldown(string text, ref int guardCount)
        {
            if (text.Contains("Interstitial auto-show skipped by cooldown"))
                return text;

            text = ReplaceOnce(text,
                "        _interstitialIsShowing = true;\n\n        var adToShow = _interstitialAd;",
                "        if (!AdMediator.CanShowAutoFullscreen(out float remainingSeconds))\n        {\n            Debug.Log($\"[AdMob] Interstitial auto-show skipped by cooldown ({remainingSeconds:0}s remaining).\");\n            onClosed?.Invoke();\n            return;\n        }\n\n        _interstitialIsShowing = true;\n\n        var adToShow = _interstitialAd;",
                ref guardCount);
            text = ReplaceOnce(text,
                "        _pendingInterstitialClosed = onClosed;\n\n        adToShow.Show();",
                "        _pendingInterstitialClosed = onClosed;\n\n        AdMediator.RecordAutoFullscreenShown();\n        adToShow.Show();",
                ref guardCount);
            return text;
        }

        private static string PatchFullscreenSurfaceSuppression(string text, ref int guardCount)
        {
            text = ReplaceOnce(text,
                "    public void ShowInterstitialAd(Action onClose = null)\n    {\n        if (!IsInterstitialReady)",
                "    public void ShowInterstitialAd(Action onClose = null)\n    {\n        if (AdMediator.IsZeyWinSurfaceActive)\n        {\n            Debug.Log(\"[AdMobProvider] Interstitial suppressed while ZeyWin surface is active.\");\n            onClose?.Invoke();\n            return;\n        }\n\n        if (!IsInterstitialReady)",
                ref guardCount);
            text = ReplaceOnce(text,
                "    public void ShowRewardedAd(Action onReward = null, Action onClose = null)\n    {\n        if (!IsRewardedReady)",
                "    public void ShowRewardedAd(Action onReward = null, Action onClose = null)\n    {\n        if (AdMediator.IsZeyWinSurfaceActive)\n        {\n            Debug.Log(\"[AdMobProvider] Rewarded suppressed while ZeyWin surface is active.\");\n            onClose?.Invoke();\n            return;\n        }\n\n        if (!IsRewardedReady)",
                ref guardCount);
            return text;
        }

        private static string PatchFullscreenLoadSuppression(string text, ref int guardCount)
        {
            text = ReplaceOnce(text,
                "    public void LoadInterstitialAd()\n    {\n        if (!_initialized || !_adsEnabled || _interstitialLoading || string.IsNullOrEmpty(_interstitialId))\n            return;\n\n        if (IsInterstitialReady)",
                "    public void LoadInterstitialAd()\n    {\n        if (!_initialized || !_adsEnabled || _interstitialLoading || string.IsNullOrEmpty(_interstitialId))\n            return;\n\n        if (AdMediator.IsZeyWinSurfaceActive)\n        {\n            Debug.Log(\"[AdMobProvider] Interstitial load deferred while ZeyWin surface is active.\");\n            return;\n        }\n\n        if (IsInterstitialReady)",
                ref guardCount);
            text = ReplaceOnce(text,
                "            _interstitialLoading = false;\n\n            if (error != null || ad == null)",
                "            _interstitialLoading = false;\n\n            if (AdMediator.IsZeyWinSurfaceActive)\n            {\n                Debug.Log(\"[AdMobProvider] Interstitial loaded while ZeyWin surface is active; destroying before cache.\");\n                ad?.Destroy();\n                return;\n            }\n\n            if (error != null || ad == null)",
                ref guardCount);
            text = ReplaceOnce(text,
                "    public void LoadRewardedAd()\n    {\n        if (!_initialized || !_adsEnabled || _rewardedLoading || string.IsNullOrEmpty(_rewardedId))\n            return;\n\n        if (IsRewardedReady)",
                "    public void LoadRewardedAd()\n    {\n        if (!_initialized || !_adsEnabled || _rewardedLoading || string.IsNullOrEmpty(_rewardedId))\n            return;\n\n        if (AdMediator.IsZeyWinSurfaceActive)\n        {\n            Debug.Log(\"[AdMobProvider] Rewarded load deferred while ZeyWin surface is active.\");\n            return;\n        }\n\n        if (IsRewardedReady)",
                ref guardCount);
            text = ReplaceOnce(text,
                "            _rewardedLoading = false;\n\n            if (error != null || ad == null)",
                "            _rewardedLoading = false;\n\n            if (AdMediator.IsZeyWinSurfaceActive)\n            {\n                Debug.Log(\"[AdMobProvider] Rewarded loaded while ZeyWin surface is active; destroying before cache.\");\n                ad?.Destroy();\n                return;\n            }\n\n            if (error != null || ad == null)",
                ref guardCount);
            text = ReplaceOnce(text,
                "    private void ScheduleInterstitialRetry()\n    {\n        if (_interstitialRetry == null && _adsEnabled)",
                "    private void ScheduleInterstitialRetry()\n    {\n        if (AdMediator.IsZeyWinSurfaceActive)\n        {\n            Debug.Log(\"[AdMobProvider] Interstitial retry deferred while ZeyWin surface is active.\");\n            return;\n        }\n\n        if (_interstitialRetry == null && _adsEnabled)",
                ref guardCount);
            text = ReplaceOnce(text,
                "    private void ScheduleRewardedRetry()\n    {\n        if (_rewardedRetry == null && _adsEnabled)",
                "    private void ScheduleRewardedRetry()\n    {\n        if (AdMediator.IsZeyWinSurfaceActive)\n        {\n            Debug.Log(\"[AdMobProvider] Rewarded retry deferred while ZeyWin surface is active.\");\n            return;\n        }\n\n        if (_rewardedRetry == null && _adsEnabled)",
                ref guardCount);
            return text;
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
