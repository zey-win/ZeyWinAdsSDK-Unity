using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ZeyWinAds.Editor
{
    /// <summary>
    /// Keeps older game-side banner rotators from hiding the SDK native banner
    /// and falling into an empty AdMob interval.
    /// </summary>
    [InitializeOnLoad]
    public static class LegacyAdManagerBannerPatcher
    {
        private const string Marker = "ZeyWinAds legacy sticky native banner";

        static LegacyAdManagerBannerPatcher()
        {
            EditorApplication.delayCall += () => Apply(logWhenNoChanges: false);
        }

        [MenuItem("ZeyWinAds/Patch Legacy AdManager Banner Rotation", priority = 12)]
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
            foreach (string path in Directory.GetFiles(assetsRoot, "AdManager.cs", SearchOption.AllDirectories))
                modifiedAny |= PatchAdManager(path);

            if (modifiedAny)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
            else if (logWhenNoChanges)
            {
                Debug.Log("[ZeyWinAds] No legacy AdManager.cs banner rotators needed patching.");
            }

            return modifiedAny;
        }

        private static bool PatchAdManager(string fullPath)
        {
            string text = File.ReadAllText(fullPath);
            if (!text.Contains("class AdManager")
                || !text.Contains("BannerRotationLoop")
                || !text.Contains("ShowBannerForced"))
            {
                return false;
            }

            string original = text;
            int changes = 0;

            text = ReplaceOnce(text,
                "        if (!_bannerShownOnce)\n            _currentBannerNet = ChooseNetwork(AdFormat.Banner);",
                "        if (!_bannerShownOnce)\n            _currentBannerNet = AdNetwork.ZeyWin;",
                ref changes);

            text = ReplaceOnce(text,
                "        Load(AdNetwork.AdMob, AdFormat.Banner);\n        Load(AdNetwork.ZeyWin, AdFormat.Banner);",
                "        _currentBannerNet = AdNetwork.ZeyWin;\n        Load(AdNetwork.ZeyWin, AdFormat.Banner);",
                ref changes);

            text = ReplaceMethod(text,
                "    public void ShowBanner()",
                "    public void ShowBanner()\n" +
                "    {\n" +
                "        _bannerRequestedVisible = true;\n\n" +
                "        if (!AdsEnabled) return;\n" +
                "        if (_isPopupShowing) return;\n\n" +
                "        _currentBannerNet = AdNetwork.ZeyWin;\n\n" +
                "        if (rotateBannersIfBothReady)\n" +
                "        {\n" +
                "            StartBannerRotation();\n" +
                "            return;\n" +
                "        }\n\n" +
                "        HideBanner(AdNetwork.AdMob);\n" +
                "        zeyWin?.ShowBannerBottom();\n" +
                "    }",
                ref changes);

            text = ReplaceOnce(text,
                "                _currentBannerNet = Other(_currentBannerNet);\n                ShowBannerForced(_currentBannerNet);",
                "                _currentBannerNet = AdNetwork.ZeyWin;\n                ShowBannerForced(_currentBannerNet);",
                ref changes);

            text = ReplaceOnce(text,
                "            var other = Other(_currentBannerNet);\n            bool otherReady = IsReady(other, AdFormat.Banner);",
                "            var other = AdNetwork.ZeyWin;\n            bool otherReady = IsReady(other, AdFormat.Banner);",
                ref changes);

            text = ReplaceOnce(text,
                "                Debug.Log($\"[BannerRotation] switch to {other}\");\n                _currentBannerNet = other;\n                ShowBannerForced(_currentBannerNet);\n                ArmBannerRotationTimer();",
                "                Debug.Log(\"[BannerRotation] keep ZeyWin native banner active\");\n                _currentBannerNet = AdNetwork.ZeyWin;\n                ShowBannerForced(_currentBannerNet);\n                ArmBannerRotationTimer();",
                ref changes);

            text = ReplaceOnce(text,
                "                Debug.Log($\"[BannerRotation] other not ready, preload {other}\");\n                Load(other, AdFormat.Banner);",
                "                Debug.Log(\"[BannerRotation] ZeyWin native banner not ready, preload\");\n                Load(AdNetwork.ZeyWin, AdFormat.Banner);",
                ref changes);

            text = ReplaceOnce(text,
                "        var other = Other(_currentBannerNet);\n\n        if (IsReady(other, AdFormat.Banner))\n        {\n            _currentBannerNet = other;\n            Debug.Log($\"[BannerRotation] restore fallback switch to {_currentBannerNet}\");\n            ShowBannerForced(_currentBannerNet);\n            return;\n        }",
                "        if (IsReady(AdNetwork.ZeyWin, AdFormat.Banner))\n        {\n            _currentBannerNet = AdNetwork.ZeyWin;\n            Debug.Log(\"[BannerRotation] restore ZeyWin native banner\");\n            ShowBannerForced(_currentBannerNet);\n            return;\n        }",
                ref changes);

            text = ReplaceOnce(text,
                "        Debug.Log(\"[BannerRotation] restore failed, preload both\");\n        Load(AdNetwork.ZeyWin, AdFormat.Banner);\n        Load(AdNetwork.AdMob, AdFormat.Banner);",
                "        Debug.Log(\"[BannerRotation] restore failed, preload ZeyWin native banner\");\n        Load(AdNetwork.ZeyWin, AdFormat.Banner);",
                ref changes);

            text = ReplaceMethod(text,
                "    private void ShowBannerForced(AdNetwork net)",
                "    private void ShowBannerForced(AdNetwork net)\n" +
                "    {\n" +
                "        if (!AdsEnabled) return;\n\n" +
                "        // " + Marker + "\n" +
                "        _currentBannerNet = AdNetwork.ZeyWin;\n" +
                "        HideBanner(AdNetwork.AdMob);\n" +
                "        zeyWin?.ShowBannerBottom();\n" +
                "    }",
                ref changes);

            text = ReplaceOnce(text,
                "        HideBanner(AdNetwork.ZeyWin);\n        HideBanner(AdNetwork.AdMob);",
                "        HideBanner(AdNetwork.AdMob);",
                ref changes);

            if (text == original)
                return false;

            if (!text.Contains(Marker))
            {
                Debug.LogWarning($"[ZeyWinAds] Legacy AdManager banner patch skipped for {ToAssetPath(fullPath)}; unsupported rotator shape.");
                return false;
            }

            File.WriteAllText(fullPath, text, new UTF8Encoding(false));
            Debug.Log($"[ZeyWinAds] Patched legacy AdManager sticky native banner rotation in {ToAssetPath(fullPath)}.");
            return true;
        }

        private static string ReplaceMethod(string text, string signature, string replacement, ref int changes)
        {
            int signatureIndex = text.IndexOf(signature, StringComparison.Ordinal);
            if (signatureIndex < 0)
                return text;

            int openBraceIndex = text.IndexOf('{', signatureIndex);
            if (openBraceIndex < 0)
                return text;

            int depth = 0;
            for (int index = openBraceIndex; index < text.Length; index++)
            {
                if (text[index] == '{')
                    depth++;
                else if (text[index] == '}')
                    depth--;

                if (depth == 0)
                {
                    int methodLength = index - signatureIndex + 1;
                    string currentMethod = text.Substring(signatureIndex, methodLength);
                    if (currentMethod == replacement)
                        return text;

                    changes++;
                    return text.Substring(0, signatureIndex) + replacement + text.Substring(index + 1);
                }
            }

            return text;
        }

        private static string ReplaceOnce(string text, string oldValue, string newValue, ref int changes)
        {
            int index = text.IndexOf(oldValue, StringComparison.Ordinal);
            if (index < 0)
                return text;

            changes++;
            return text.Remove(index, oldValue.Length).Insert(index, newValue);
        }

        private static string ToAssetPath(string fullPath)
        {
            string assetsRoot = Application.dataPath.Replace("\\", "/");
            string normalized = fullPath.Replace("\\", "/");
            if (normalized.StartsWith(assetsRoot, StringComparison.Ordinal))
                return "Assets" + normalized.Substring(assetsRoot.Length);

            return normalized;
        }
    }
}
