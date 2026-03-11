using System;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Types of ads supported by ZeyWin Ads
    /// </summary>
    public enum AdType
    {
        Interstitial,
        Rewarded,
        Banner,
        Native
    }

    /// <summary>
    /// Banner position on screen
    /// </summary>
    public enum BannerPosition
    {
        Top,
        Bottom
    }

    /// <summary>
    /// Media type for ad content
    /// </summary>
    public enum MediaType
    {
        Image,
        Video,
        Html,
        Native
    }

    /// <summary>
    /// Request sent to the ad server
    /// </summary>
    [Serializable]
    public class AdRequest
    {
        public string bundle_id;
        public string api_key;
        public string ad_type;
        public string country;
        public string language;
        public string platform;
        public string device_type;
        public string device_model;
        public string os_version;
        public string sdk_version;

        public AdRequest(string bundleId, string apiKey, AdType adType)
        {
            bundle_id = bundleId;
            api_key = apiKey;
            ad_type = AdTypeToString(adType);
            language = DeviceInfo.GetLanguage();
            platform = DeviceInfo.GetPlatform();
            device_type = DeviceInfo.GetDeviceType();
            device_model = DeviceInfo.GetDeviceModel();
            os_version = DeviceInfo.GetOSVersion();
            sdk_version = ZeyWinAdsConfig.SdkVersion;
        }

        private static string AdTypeToString(AdType type)
        {
            return type switch
            {
                AdType.Interstitial => "interstitial",
                AdType.Rewarded => "rewarded",
                AdType.Banner => "banner",
                AdType.Native => "native",
                _ => "interstitial"
            };
        }
    }

    /// <summary>
    /// Response from the ad server
    /// </summary>
    [Serializable]
    public class AdResponse
    {
        public string ad_id;
        public string ad_type;
        public string media_type;
        public string media_url;
        public string click_url;
        public int duration_sec;
        public int skip_after_sec;
        public bool lock_webview;
        public string icon_url;
        public string ad_text;
        public string ad_body;
        public string cta_text;
        public string impression_url;
        public string click_tracking_url;
        public string complete_url;
        public string reward_url;

        public AdType GetAdType()
        {
            return ad_type switch
            {
                "interstitial" => AdType.Interstitial,
                "rewarded" => AdType.Rewarded,
                "banner" => AdType.Banner,
                "native" => AdType.Native,
                _ => AdType.Interstitial
            };
        }

        public MediaType GetMediaType()
        {
            return media_type switch
            {
                "image" => MediaType.Image,
                "video" => MediaType.Video,
                "html" => MediaType.Html,
                "native" => MediaType.Native,
                _ => MediaType.Image
            };
        }
    }

    /// <summary>
    /// Generic API response wrapper
    /// </summary>
    [Serializable]
    public class ApiResponse<T>
    {
        public bool success;
        public T data;
        public string error;
    }

    /// <summary>
    /// Event request for tracking
    /// </summary>
    [Serializable]
    public class EventRequest
    {
        public string app_id;
        public string api_key;
        public string ad_id;
        public string event_type;
        public string device_model;
        public string os_version;
        public string sdk_version;

        public EventRequest(string bundleId, string apiKey, string adId, string eventType)
        {
            app_id = bundleId;
            api_key = apiKey;
            ad_id = adId;
            event_type = eventType;
            device_model = DeviceInfo.GetDeviceModel();
            os_version = DeviceInfo.GetOSVersion();
            sdk_version = ZeyWinAdsConfig.SdkVersion;
        }
    }

    /// <summary>
    /// SDK configuration constants
    /// </summary>
    public static class ZeyWinAdsConfig
    {
        public const string SdkVersion = "1.0.0";
        public const int DefaultRewardAmount = 1;
        public const float RequestTimeoutSeconds = 30f;
        public const int MaxRetries = 2;
    }
}
