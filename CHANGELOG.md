# Changelog

All notable changes to this package are documented in this file.

## 1.0.7

- Fixed the runtime `ZeyWinAdsConfig.SdkVersion` constant so ad requests report the published package version.

## 1.0.6

- Added canonical fleet configurator argument names: `adMobAppId`, `bannerAdUnitId`, `interstitialAdUnitId`, and `rewardedAdUnitId`.
- Kept compatibility with existing `admobAndroid...` and `admob...` command-line aliases so older automation does not break.

## 1.0.5

- Disabled the full Unity splash screen from the fleet project configurator, not only the Unity logo, so Unity 6000 game installs keep `m_ShowUnitySplashScreen` and `m_ShowUnitySplashLogo` off.

## 1.0.4

- Added `ZeyWinAdsProjectConfigurator`, a batchmode-friendly Unity editor installer for fleet updates across many games.
- The configurator writes ZeyWin API key, AdMob App ID and ad unit IDs, Android package ID, product name, version name/code, `app_name` resources, Google Mobile Ads settings, Android manifest permissions, package queries, and AdMob manifest metadata.
- Documented the command-line flow so each game can be configured by changing only project identifiers and ad keys.
- Added GitHub-ready README visuals and release packaging guidance for product-style distribution.
- Preserved compatibility with existing game integrations that use popup gates, block status, and locked WebView URL access.
- Avoided duplicate Google Mobile Ads package installation when a project already contains legacy `Assets/GoogleMobileAds`.

## 1.0.3

- Replaced the SDK WebView loading spinner with the blue money-progress loading screen used by the Plinko app.
- Replaced Android native WebView loading overlays with the same blue lower progress treatment so WebView startup no longer flashes a black spinner.
- Added the money-pack loading asset as a package resource so consuming games do not need their own copy.
- Documented the one-SDK install flow for clean Unity game repositories.

## 1.0.2

- Fixed UniWebView and ZeyWin runtime assembly references for Unity 6000 editor compilation.
- Reduced default ZeyWin preload retry pressure and added Firebase Remote Config controls for retry attempts and retry delay.
- Made the Remote Config bridge ignore empty stub/default values so `zeywin_ads_enabled` does not accidentally disable ZeyWin ads.
- Avoided duplicate SDK-owned AdMob preload requests during first initialization.

## 1.0.1

- Added optional Firebase Remote Config support for ZeyWin ad serving flags, request timeout, and API endpoint overrides without requiring Firebase as a hard package dependency.

## 1.0.0

- Added safer Android UniWebView handling for offer pages: download links open outside the WebView, context-menu download callbacks are disabled, and the bundled UniWebView receiver registration uses Android 13+ receiver flags.
- Added startup auto-initialization from `ZeyWinAdsSettings`.
- Added SDK-owned WebView loading overlay with a spinner and `Loading` label.
- Added automated native banner rendering: white 80% width card, 150 px minimum adaptive height, top/bottom slide-in, and Google Play redirect through `store_url`.
- Added fast startup offer flow with Google AdMob fallback handling.
- Added Android and iOS WebView load callbacks for hiding the loading overlay.
- Added AdMob build-time Android and iOS configuration helpers.
- Added UMP consent and iOS ATT support.
- Added CrashGuard dependency bootstrap support.
- Removed hardcoded proxy credentials and reduced sensitive runtime logging.
- Added MIT license file.
