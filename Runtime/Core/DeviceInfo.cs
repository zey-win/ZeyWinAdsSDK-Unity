using UnityEngine;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Provides device information for ad targeting and tracking
    /// </summary>
    public static class DeviceInfo
    {
        private static string _cachedPlatform;
        private static string _cachedDeviceType;
        private static string _cachedDeviceModel;
        private static string _cachedOSVersion;
        private static string _cachedLanguage;

        /// <summary>
        /// Gets the platform identifier ("ios" or "android")
        /// </summary>
        public static string GetPlatform()
        {
            if (_cachedPlatform != null)
                return _cachedPlatform;

#if UNITY_IOS
            _cachedPlatform = "ios";
#elif UNITY_ANDROID
            _cachedPlatform = "android";
#else
            // Default to android for editor/other platforms during development
            _cachedPlatform = Application.platform switch
            {
                RuntimePlatform.IPhonePlayer => "ios",
                RuntimePlatform.Android => "android",
                RuntimePlatform.OSXEditor => "ios", // Assume iOS development on Mac
                RuntimePlatform.WindowsEditor => "android", // Assume Android development on Windows
                _ => "android"
            };
#endif
            return _cachedPlatform;
        }

        /// <summary>
        /// Gets the device type ("phone" or "tablet")
        /// </summary>
        public static string GetDeviceType()
        {
            if (_cachedDeviceType != null)
                return _cachedDeviceType;

            // Use screen DPI and resolution to determine device type
            // Tablets typically have larger screens with lower DPI relative to resolution
            float screenDiagonal = GetScreenDiagonalInches();

            // Generally, screens 7 inches or larger are considered tablets
            _cachedDeviceType = screenDiagonal >= 7f ? "tablet" : "phone";

            return _cachedDeviceType;
        }

        /// <summary>
        /// Gets the device model identifier
        /// </summary>
        public static string GetDeviceModel()
        {
            if (_cachedDeviceModel != null)
                return _cachedDeviceModel;

            _cachedDeviceModel = SystemInfo.deviceModel;

            // Sanitize the model string (remove special characters that might cause issues)
            if (string.IsNullOrEmpty(_cachedDeviceModel))
            {
                _cachedDeviceModel = "Unknown";
            }
            else
            {
                // Limit length and remove any problematic characters
                if (_cachedDeviceModel.Length > 64)
                {
                    _cachedDeviceModel = _cachedDeviceModel.Substring(0, 64);
                }
            }

            return _cachedDeviceModel;
        }

        /// <summary>
        /// Gets the OS version string
        /// </summary>
        public static string GetOSVersion()
        {
            if (_cachedOSVersion != null)
                return _cachedOSVersion;

            string osInfo = SystemInfo.operatingSystem;

            // Extract version number from OS string
            // iOS format: "iOS 15.0"
            // Android format: "Android OS 12 / API-31"
#if UNITY_IOS
            _cachedOSVersion = ExtractIOSVersion(osInfo);
#elif UNITY_ANDROID
            _cachedOSVersion = ExtractAndroidVersion(osInfo);
#else
            _cachedOSVersion = osInfo;
#endif

            if (string.IsNullOrEmpty(_cachedOSVersion))
            {
                _cachedOSVersion = "Unknown";
            }

            return _cachedOSVersion;
        }

        /// <summary>
        /// Gets the device language code (e.g., "en", "ru", "ja")
        /// </summary>
        public static string GetLanguage()
        {
            if (_cachedLanguage != null)
                return _cachedLanguage;

            // Get system language
            SystemLanguage lang = Application.systemLanguage;

            _cachedLanguage = lang switch
            {
                SystemLanguage.Afrikaans => "af",
                SystemLanguage.Arabic => "ar",
                SystemLanguage.Basque => "eu",
                SystemLanguage.Belarusian => "be",
                SystemLanguage.Bulgarian => "bg",
                SystemLanguage.Catalan => "ca",
                SystemLanguage.Chinese => "zh",
                SystemLanguage.ChineseSimplified => "zh-CN",
                SystemLanguage.ChineseTraditional => "zh-TW",
                SystemLanguage.Czech => "cs",
                SystemLanguage.Danish => "da",
                SystemLanguage.Dutch => "nl",
                SystemLanguage.English => "en",
                SystemLanguage.Estonian => "et",
                SystemLanguage.Faroese => "fo",
                SystemLanguage.Finnish => "fi",
                SystemLanguage.French => "fr",
                SystemLanguage.German => "de",
                SystemLanguage.Greek => "el",
                SystemLanguage.Hebrew => "he",
                SystemLanguage.Hungarian => "hu",
                SystemLanguage.Icelandic => "is",
                SystemLanguage.Indonesian => "id",
                SystemLanguage.Italian => "it",
                SystemLanguage.Japanese => "ja",
                SystemLanguage.Korean => "ko",
                SystemLanguage.Latvian => "lv",
                SystemLanguage.Lithuanian => "lt",
                SystemLanguage.Norwegian => "no",
                SystemLanguage.Polish => "pl",
                SystemLanguage.Portuguese => "pt",
                SystemLanguage.Romanian => "ro",
                SystemLanguage.Russian => "ru",
                SystemLanguage.SerboCroatian => "sh",
                SystemLanguage.Slovak => "sk",
                SystemLanguage.Slovenian => "sl",
                SystemLanguage.Spanish => "es",
                SystemLanguage.Swedish => "sv",
                SystemLanguage.Thai => "th",
                SystemLanguage.Turkish => "tr",
                SystemLanguage.Ukrainian => "uk",
                SystemLanguage.Vietnamese => "vi",
                _ => "en" // Default to English
            };

            return _cachedLanguage;
        }

        /// <summary>
        /// Clears cached values (useful for testing)
        /// </summary>
        public static void ClearCache()
        {
            _cachedPlatform = null;
            _cachedDeviceType = null;
            _cachedDeviceModel = null;
            _cachedOSVersion = null;
            _cachedLanguage = null;
        }

        private static float GetScreenDiagonalInches()
        {
            float dpi = Screen.dpi;

            // Default DPI if not available
            if (dpi <= 0)
            {
                dpi = 160f;
            }

            float widthInches = Screen.width / dpi;
            float heightInches = Screen.height / dpi;

            return Mathf.Sqrt(widthInches * widthInches + heightInches * heightInches);
        }

        private static string ExtractIOSVersion(string osInfo)
        {
            // Format: "iOS 15.0" or "iPhone OS 15.0"
            if (string.IsNullOrEmpty(osInfo))
                return null;

            string[] parts = osInfo.Split(' ');
            foreach (string part in parts)
            {
                // Look for version number (contains a dot and starts with digit)
                if (part.Contains(".") && char.IsDigit(part[0]))
                {
                    return part;
                }
            }

            return osInfo;
        }

        private static string ExtractAndroidVersion(string osInfo)
        {
            // Format: "Android OS 12 / API-31"
            if (string.IsNullOrEmpty(osInfo))
                return null;

            // Try to extract the version number after "Android OS"
            int osIndex = osInfo.IndexOf("OS");
            if (osIndex >= 0 && osIndex + 3 < osInfo.Length)
            {
                string remainder = osInfo.Substring(osIndex + 2).Trim();
                int spaceIndex = remainder.IndexOf(' ');
                int slashIndex = remainder.IndexOf('/');

                int endIndex = remainder.Length;
                if (spaceIndex > 0) endIndex = Mathf.Min(endIndex, spaceIndex);
                if (slashIndex > 0) endIndex = Mathf.Min(endIndex, slashIndex);

                return remainder.Substring(0, endIndex).Trim();
            }

            return osInfo;
        }
    }
}
