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
        Native,
        Popup
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
        public string device_id;
        public string app_version;
        public bool has_sim;
        public string sim_country;

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
            device_id = DeviceIdentity.GetCachedGAID();
            app_version = UnityEngine.Application.version;
            has_sim = DeviceIdentity.HasSim();
            sim_country = DeviceIdentity.GetSimCountry();
        }

        private static string AdTypeToString(AdType type)
        {
            return type switch
            {
                AdType.Interstitial => "interstitial",
                AdType.Rewarded => "rewarded",
                AdType.Banner => "banner",
                AdType.Native => "native",
                AdType.Popup => "popup",
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
        public string cta_text_2;
        public int popup_delay_sec;
        public int popup_repeat_sec;
        public string store_url;
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
                "popup" => AdType.Popup,
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
        public string ad_type;
        public string event_type;
        public string device_model;
        public string os_version;
        public string sdk_version;
        public string device_id;
        public string bundle_id;

        public EventRequest(string bundleId, string apiKey, string adId, string eventType)
        {
            app_id = bundleId;
            bundle_id = bundleId;
            api_key = apiKey;
            ad_id = adId;
            event_type = eventType;
            device_model = DeviceInfo.GetDeviceModel();
            os_version = DeviceInfo.GetOSVersion();
            sdk_version = ZeyWinAdsConfig.SdkVersion;
            device_id = DeviceIdentity.GetCachedGAID();
        }
    }

    /// <summary>
    /// Webview render confirmation request. Tells the server whether the
    /// fullscreen ad surface actually rendered to the user (status="shown")
    /// or failed to render (status="failed" + a fail_reason).
    /// </summary>
    [Serializable]
    public class WebviewEventRequest
    {
        public string api_key;
        public string bundle_id;
        public string device_id;
        public string ad_id;
        public string ad_type;
        public string status;
        public string fail_reason;
    }

    /// <summary>
    /// Native ad info for custom rendering.
    /// Contains all data needed to build your own native ad UI.
    /// </summary>
    public class NativeAdInfo
    {
        /// <summary>Ad identifier</summary>
        public string AdId { get; set; }

        /// <summary>App icon URL</summary>
        public string IconUrl { get; set; }

        /// <summary>Headline text</summary>
        public string Headline { get; set; }

        /// <summary>Body/description text (may be null)</summary>
        public string Body { get; set; }

        /// <summary>Call-to-action button text, e.g. "Install" (may be null)</summary>
        public string CtaText { get; set; }

        /// <summary>Media image URL for large format (may be null)</summary>
        public string MediaUrl { get; set; }

        /// <summary>Click-through URL</summary>
        public string ClickUrl { get; set; }

        /// <summary>
        /// Call this when the ad becomes visible to the user.
        /// Tracks an impression event.
        /// </summary>
        public Action TrackImpression { get; set; }

        /// <summary>
        /// Call this when the user clicks/taps the ad.
        /// Tracks a click event and opens the click URL.
        /// </summary>
        public Action RegisterClick { get; set; }
    }

    /// <summary>
    /// Popup ad info for custom rendering.
    /// Contains all data needed to build your own popup UI.
    /// </summary>
    public class PopupAdInfo
    {
        /// <summary>Ad identifier</summary>
        public string AdId { get; set; }

        /// <summary>Popup title text</summary>
        public string Title { get; set; }

        /// <summary>Popup subtitle text (may be null)</summary>
        public string Subtitle { get; set; }

        /// <summary>Primary button text (e.g. "Accept")</summary>
        public string Button1Text { get; set; }

        /// <summary>Secondary button text (e.g. "Cancel", may be null)</summary>
        public string Button2Text { get; set; }

        /// <summary>Click-through URL</summary>
        public string ClickUrl { get; set; }

        /// <summary>Delay in seconds before showing the popup</summary>
        public int DelaySec { get; set; }

        /// <summary>Repeat interval in seconds (0 = no repeat)</summary>
        public int RepeatSec { get; set; }

        /// <summary>Optional image URL (may be null or empty)</summary>
        public string ImageUrl { get; set; }

        /// <summary>
        /// Call this when the popup becomes visible to the user.
        /// Tracks an impression event.
        /// </summary>
        public Action TrackImpression { get; set; }

        /// <summary>
        /// Call this when the user clicks the primary button / taps the ad.
        /// Tracks a click event and opens the click URL.
        /// </summary>
        public Action RegisterClick { get; set; }
    }

    // --- Cross-app referral models ---

    [Serializable]
    public class ClickRegisterRequest
    {
        public string api_key;
        public string bundle_id;
        public string ad_id;
        public string device_id;
        public string click_url;
        public string target_bundle_id;
    }

    [Serializable]
    public class ClickRegisterResponse
    {
        public string click_id;
        public bool registered;
    }

    [Serializable]
    public class ReferralCheckRequest
    {
        public string api_key;
        public string bundle_id;
        public string device_id;
        public string sim_country;
    }

    [Serializable]
    public class ReferralCheckByClickRequest
    {
        public string api_key;
        public string bundle_id;
        public string click_id;
        public string sim_country;
    }

    [Serializable]
    public class ReferralCheckResponse
    {
        public bool has_referral;
        public string offer_url;
        public string click_id;
        public string source_bundle_id;
    }

    [Serializable]
    public class ReferralDeliveredRequest
    {
        public string api_key;
        public string bundle_id;
        public string click_id;
        public string device_id;
    }

    [Serializable]
    public class ReferralDeliveredResponse
    {
        public bool delivered;
    }

    [Serializable]
    public class BundleListResponse
    {
        public string[] bundles;
    }

    /// <summary>
    /// SDK configuration constants
    /// </summary>
    public static class ZeyWinAdsConfig
    {
        public const string SdkVersion = "3.9.52";
        public const int DefaultRewardAmount = 1;
        public const float RequestTimeoutSeconds = 30f;
        public const int MaxRetries = 2;
    }
}
