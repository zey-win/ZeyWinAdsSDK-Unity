using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ZeyWinAds.Editor
{
    /// <summary>
    /// Replaces older game-owned Unity loading overlays with thin bridges to
    /// the SDK Java-native loader. This keeps old call sites compiling while
    /// guaranteeing that only one visible loader exists.
    /// </summary>
    [InitializeOnLoad]
    public static class LegacyOfferBootLoadingOverlayPatcher
    {
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
            foreach (string path in Directory.GetFiles(assetsRoot, "WebViewLoadingOverlay.cs", SearchOption.AllDirectories))
                modifiedAny |= PatchWebViewOverlay(path);
            foreach (string path in Directory.GetFiles(assetsRoot, "LoadingProgressUI.cs", SearchOption.AllDirectories))
                modifiedAny |= PatchLoadingProgressUi(path);
            foreach (string path in Directory.GetFiles(assetsRoot, "MoneyLoadingOverlayVisual.cs", SearchOption.AllDirectories))
                modifiedAny |= PatchMoneyLoadingVisual(path);

            if (modifiedAny)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
            else if (logWhenNoChanges)
            {
                Debug.Log("[ZeyWinAds] No legacy Unity loading overlay files needed patching.");
            }

            return modifiedAny;
        }

        private static bool PatchOverlay(string fullPath)
        {
            string text = File.ReadAllText(fullPath);
            if (!text.Contains("class OfferBootLoadingOverlay"))
                return false;

            string patched = BuildOfferBootBridge();
            if (text == patched)
                return false;

            File.WriteAllText(fullPath, patched, new UTF8Encoding(false));
            Debug.Log($"[ZeyWinAds] Replaced legacy OfferBootLoadingOverlay with Java-native loader bridge in {ToAssetPath(fullPath)}.");
            return true;
        }

        private static bool PatchWebViewOverlay(string fullPath)
        {
            string text = File.ReadAllText(fullPath);
            if (!text.Contains("class WebViewLoadingOverlay"))
                return false;

            string patched = BuildWebViewBridge();
            if (patched == text)
                return false;

            File.WriteAllText(fullPath, patched, new UTF8Encoding(false));
            Debug.Log($"[ZeyWinAds] Replaced legacy WebViewLoadingOverlay with Java-native loader bridge in {ToAssetPath(fullPath)}.");
            return true;
        }

        private static bool PatchLoadingProgressUi(string fullPath)
        {
            string text = File.ReadAllText(fullPath);
            if (!text.Contains("class LoadingProgressUI"))
                return false;

            string patched = BuildLoadingProgressUiBridge();
            if (patched == text)
                return false;

            File.WriteAllText(fullPath, patched, new UTF8Encoding(false));
            Debug.Log($"[ZeyWinAds] Disabled legacy scene LoadingProgressUI in {ToAssetPath(fullPath)} so only the Java-native loader can render.");
            return true;
        }

        private static bool PatchMoneyLoadingVisual(string fullPath)
        {
            string text = File.ReadAllText(fullPath);
            if (!text.Contains("class MoneyLoadingOverlayVisual"))
                return false;

            string patched = BuildMoneyLoadingVisualStub();
            if (patched == text)
                return false;

            File.WriteAllText(fullPath, patched, new UTF8Encoding(false));
            Debug.Log($"[ZeyWinAds] Replaced legacy MoneyLoadingOverlayVisual with a no-op compatibility stub in {ToAssetPath(fullPath)}.");
            return true;
        }

        private static string BuildOfferBootBridge()
        {
            return
@"using UnityEngine;

// ZeyWinAds native-loader bridge. Installed by the SDK configurator.
public sealed class OfferBootLoadingOverlay : MonoBehaviour
{
    public static void Show() => SetNativeOverlayVisible(true);
    public static void HoldForPossibleSdkWebView() => SetNativeOverlayVisible(true);
    public static void ExpectLocalWebView() => SetNativeOverlayVisible(true);
    public static void HideAfterNativeWebViewLocked() => SetNativeOverlayVisible(false);
    public static void HideNow() => SetNativeOverlayVisible(false);

    private static void SetNativeOverlayVisible(bool visible)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var startupOverlay = new AndroidJavaClass(""com.zeywinads.unity.ZeyWinAdsStartupOverlay""))
            {
                startupOverlay.CallStatic(""setLoadingOverlayVisible"", visible);
            }
        }
        catch
        {
        }
#endif
    }
}
";
        }

        private static string BuildWebViewBridge()
        {
            return
@"using UnityEngine;

// ZeyWinAds native-loader bridge. Installed by the SDK configurator.
public sealed class WebViewLoadingOverlay : MonoBehaviour
{
    public static void Show() => SetNativeOverlayVisible(true);
    public static void Hide() => SetNativeOverlayVisible(false);

    private static void SetNativeOverlayVisible(bool visible)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var startupOverlay = new AndroidJavaClass(""com.zeywinads.unity.ZeyWinAdsStartupOverlay""))
            {
                startupOverlay.CallStatic(""setLoadingOverlayVisible"", visible);
            }
        }
        catch
        {
        }
#endif
    }
}
";
        }

        private static string BuildLoadingProgressUiBridge()
        {
            return
@"using UnityEngine;

// ZeyWinAds compatibility stub. The visible loader is SDK-owned Java-native UI.
public sealed class LoadingProgressUI : MonoBehaviour
{
    private void Awake()
    {
        HideLegacyLoaderHierarchy();
    }

    private void OnEnable()
    {
        HideLegacyLoaderHierarchy();
    }

    public void Show()
    {
        HideLegacyLoaderHierarchy();
    }

    public void Hide()
    {
        HideLegacyLoaderHierarchy();
    }

    private void HideLegacyLoaderHierarchy()
    {
        var canvas = GetComponentInParent<Canvas>(true);
        if (canvas != null && canvas.gameObject.activeSelf)
        {
            canvas.gameObject.SetActive(false);
            return;
        }

        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }
}
";
        }

        private static string BuildMoneyLoadingVisualStub()
        {
            return
@"using UnityEngine;

// ZeyWinAds compatibility stub. Money loader drawing is Java-native now.
public sealed class MoneyLoadingOverlayVisual : MonoBehaviour
{
    public const float FillDurationSeconds = 8f;

    public static float EvaluateSteppedProgress(float value)
    {
        return Mathf.Clamp01(value);
    }

    public void Build(RectTransform _)
    {
    }
}
";
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
