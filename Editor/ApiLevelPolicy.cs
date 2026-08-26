namespace ZeyWinAds.Editor
{
    // Update MinRequiredTargetSdk when Google Play raises its minimum targetSdk requirement.
    // Update RequiredMinSdk only if a bundled native dependency (Firebase, AdMob, EDM4U AARs,
    // UniWebView) raises its own minimum supported Android version.
    public static class ApiLevelPolicy
    {
        public const int MinRequiredTargetSdk = 36; // Google Play's current minimum (Android 16)
        public const int RequiredMinSdk = 25;        // lowest level bundled deps actually support
    }
}
