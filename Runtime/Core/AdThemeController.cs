using System;
using UnityEngine;

namespace ZeyWinAds.Core
{
    internal enum AdThemeMode
    {
        Light,
        Dark
    }

    internal struct AdThemePalette
    {
        public AdThemeMode Mode;
        public bool IsDark;
        public Color Surface;
        public Color SurfaceHighlight;
        public Color SurfacePressed;
        public Color TextPrimary;
        public Color TextSecondary;
        public Color TextMuted;
        public Color ImagePlaceholder;
        public Color BadgeBackground;
        public Color BadgeText;
        public Color Accent;
        public Color PrimaryButton;
        public Color PrimaryButtonHighlight;
        public Color PrimaryButtonPressed;
        public Color PrimaryButtonText;
        public Color SecondaryButton;
        public Color SecondaryButtonHighlight;
        public Color SecondaryButtonPressed;
        public Color SecondaryButtonText;
        public Color CloseText;
        public Color Overlay;
        public Color FullscreenBackground;
        public Color AdMobFullscreenBackground;
    }

    internal static class AdThemeController
    {
        private const string ThemeRemoteConfigKey = "zeywin_ad_theme_mode";
        private static string _lastLogSignature;

        public static AdThemePalette Current
        {
            get
            {
                var palette = IsDarkTheme() ? DarkPalette() : LightPalette();
                LogResolvedTheme(palette);
                return palette;
            }
        }

        public static bool IsDarkTheme()
        {
            string mode = RemoteConfigBridge.GetString(ThemeRemoteConfigKey, "system");
            mode = string.IsNullOrEmpty(mode) ? "system" : mode.Trim().ToLowerInvariant();

            if (mode == "dark" || mode == "black" || mode == "night")
                return true;
            if (mode == "light" || mode == "white" || mode == "day")
                return false;

            return TryReadAndroidSystemDarkTheme(out bool androidDark) && androidDark;
        }

        private static bool TryReadAndroidSystemDarkTheme(out bool dark)
        {
            dark = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var resources = activity.Call<AndroidJavaObject>("getResources"))
                using (var configuration = resources.Call<AndroidJavaObject>("getConfiguration"))
                {
                    const int uiModeNightMask = 0x30;
                    const int uiModeNightYes = 0x20;
                    int uiMode = configuration.Get<int>("uiMode");
                    dark = (uiMode & uiModeNightMask) == uiModeNightYes;
                    return true;
                }
            }
            catch (Exception e)
            {
                Logger.Debug("Ad theme Android system read failed: {0}", e.Message);
            }
#endif
            return false;
        }

        private static AdThemePalette LightPalette()
        {
            return new AdThemePalette
            {
                Mode = AdThemeMode.Light,
                IsDark = false,
                Surface = Hex(0xFFFFFFFF),
                SurfaceHighlight = Hex(0xF4F7FAFF),
                SurfacePressed = Hex(0xE8EEF5FF),
                TextPrimary = Hex(0x101820FF),
                TextSecondary = Hex(0x3A4654FF),
                TextMuted = Hex(0x687486CC),
                ImagePlaceholder = Hex(0xE7EEF5FF),
                BadgeBackground = Hex(0xE8EEF5FF),
                BadgeText = Hex(0x5A6674FF),
                Accent = Hex(0x1DAA59FF),
                PrimaryButton = Hex(0x19A64BFF),
                PrimaryButtonHighlight = Hex(0x22BD59FF),
                PrimaryButtonPressed = Hex(0x12883CFF),
                PrimaryButtonText = Color.white,
                SecondaryButton = Hex(0xDCE7F0FF),
                SecondaryButtonHighlight = Hex(0xEAF2F8FF),
                SecondaryButtonPressed = Hex(0xC8D7E5FF),
                SecondaryButtonText = Hex(0x2D3B4BFF),
                CloseText = Hex(0x6A7582FF),
                Overlay = new Color(0f, 0f, 0f, 0.40f),
                FullscreenBackground = Hex(0xF7F9FCFF),
                AdMobFullscreenBackground = Color.white
            };
        }

        private static AdThemePalette DarkPalette()
        {
            return new AdThemePalette
            {
                Mode = AdThemeMode.Dark,
                IsDark = true,
                Surface = Hex(0x101821FF),
                SurfaceHighlight = Hex(0x172433FF),
                SurfacePressed = Hex(0x0B1119FF),
                TextPrimary = Hex(0xF4F7FBFF),
                TextSecondary = Hex(0xC8D3DFFF),
                TextMuted = Hex(0x95A3B4DD),
                ImagePlaceholder = Hex(0x1D2A38FF),
                BadgeBackground = Hex(0x243547FF),
                BadgeText = Hex(0xCAD6E4FF),
                Accent = Hex(0x23C966FF),
                PrimaryButton = Hex(0x22C55EFF),
                PrimaryButtonHighlight = Hex(0x30D46DFF),
                PrimaryButtonPressed = Hex(0x159447FF),
                PrimaryButtonText = Color.white,
                SecondaryButton = Hex(0x273647FF),
                SecondaryButtonHighlight = Hex(0x34465AFF),
                SecondaryButtonPressed = Hex(0x1A2634FF),
                SecondaryButtonText = Hex(0xE7EEF8FF),
                CloseText = Hex(0xB6C1CEFF),
                Overlay = new Color(0f, 0f, 0f, 0.62f),
                FullscreenBackground = Hex(0x05070AFF),
                AdMobFullscreenBackground = Color.black
            };
        }

        private static Color Hex(uint rgba)
        {
            return new Color(
                ((rgba >> 24) & 0xFF) / 255f,
                ((rgba >> 16) & 0xFF) / 255f,
                ((rgba >> 8) & 0xFF) / 255f,
                (rgba & 0xFF) / 255f);
        }

        private static void LogResolvedTheme(AdThemePalette palette)
        {
            string signature = palette.Mode.ToString();
            if (_lastLogSignature == signature)
                return;

            _lastLogSignature = signature;
            Logger.Debug("Ad theme resolved: {0}", palette.IsDark ? "dark" : "light");
        }
    }
}
