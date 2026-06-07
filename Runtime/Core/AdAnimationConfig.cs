namespace ZeyWinAds.Core
{
    /// <summary>
    /// Remote switches for SDK-owned ad animations. Defaults are intentionally off
    /// so new releases can enable motion from Firebase Remote Config after launch.
    /// </summary>
    internal static class AdAnimationConfig
    {
        private const bool DefaultEnabled = false;

        public static bool NativeBannerSlideEnabled =>
            NativeBannerAnimationsEnabled &&
            RemoteConfigBridge.GetBool("zeywin_native_banner_slide_enabled", DefaultEnabled);

        public static bool NativeBannerAttentionEnabled =>
            NativeBannerAnimationsEnabled &&
            RemoteConfigBridge.GetBool("zeywin_native_banner_attention_enabled", DefaultEnabled);

        public static bool NativeBannerShineEnabled =>
            NativeBannerAnimationsEnabled &&
            RemoteConfigBridge.GetBool("zeywin_native_banner_shine_enabled", DefaultEnabled);

        public static bool NativeBannerVariantRotationEnabled =>
            NativeBannerAnimationsEnabled &&
            RemoteConfigBridge.GetBool("zeywin_native_banner_variant_rotation_enabled", DefaultEnabled);

        public static bool PopupTransitionEnabled =>
            PopupAnimationsEnabled &&
            RemoteConfigBridge.GetBool("zeywin_popup_transition_enabled", DefaultEnabled);

        public static bool PopupGoldFlashEnabled =>
            PopupAnimationsEnabled &&
            RemoteConfigBridge.GetBool("zeywin_popup_gold_flash_enabled", DefaultEnabled);

        private static bool NativeBannerAnimationsEnabled =>
            RemoteConfigBridge.GetBool("zeywin_native_banner_animations_enabled", DefaultEnabled);

        private static bool PopupAnimationsEnabled =>
            RemoteConfigBridge.GetBool("zeywin_popup_animations_enabled", DefaultEnabled);
    }
}
