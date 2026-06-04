# Changelog

All notable changes to this package are documented in this file.

## 1.0.28

- Added an Android startup ContentProvider that installs the SDK blue native loading overlay before Unity finishes creating its first frame, removing the dark launch gap on fleet games.
- The Unity loading overlay now dismisses the native startup overlay with the existing 3 second fade once the C# loader is ready.
- Startup interstitial and AdMob fallback close callbacks now always clear startup loading state so games do not remain on `Loading 100%` after an auto fullscreen ad closes.

## 1.0.27

- Added SDK-owned Android launch theme installation so games show the blue ZeyWin loading background immediately from the Android launch preview window.
- The configurator now writes launch color/style resources, Android 12+ splash background attributes, and assigns the theme to the Unity launcher activity.

## 1.0.26

- Hide the SDK startup loading overlay immediately when startup offer loading falls back to Google ads while referral checks are still pending.
- This prevents games from remaining on `Loading 100%` behind an AdMob fullscreen or banner fallback.

## 1.0.25

- Added an SDK-owned runtime TextMesh Pro material guard so older games with imported TMP text objects missing material sources do not throw on Android.
- The guard also keeps TMP mobile distance-field shaders applied to runtime TMP materials after scenes load.

## 1.0.24

- Added a session cooldown for terminal ZeyWin API app configuration errors such as inactive or unknown bundle IDs, preventing fleet games from repeatedly hitting the server every few seconds.
- Added SDK-owned minimum intervals for automated fullscreen ads and legacy AdMob auto retries; rewarded button flows remain outside the fullscreen auto-show cooldown.
- Extended the legacy `AdMobProvider.cs` patcher to apply the AdMob interstitial cooldown and longer banner/interstitial retry delays automatically when the package is installed or updated.

## 1.0.22

- Made the legacy `AdMobProvider.cs` patcher repair partially patched providers and older variants that only import `GoogleMobileAds.Api`.
- Added banner-load and late-load-callback suppression patterns for older direct AdMob providers so they cannot record banner impressions under active ZeyWin surfaces.

## 1.0.21

- Added an SDK-owned legacy `AdMobProvider.cs` patcher so older games suppress direct Google banner creation while ZeyWin WebViews, popups, or banners are active.
- Wired the legacy banner guard into fleet project configuration so package updates apply the fix automatically alongside TMP, SafeArea, manifest, and AdMob setup.

## 1.0.20

- Destroy AdMob banners while ZeyWin WebView, popup, or banner surfaces are active so hidden Google banners cannot render underneath or record impressions.
- Suppress AdMob banner preload/show calls during active ZeyWin surfaces and restart banner preload only after the ZeyWin surface ends.

## 1.0.19

- Switched Android locked offers and HTML WebView ads to SDK-owned native WebViews so old and new games render the offer layer consistently after package update.
- Added an Android SafeArea WebView container for display cutouts, notches, punch-hole cameras, and system bars.
- Made the Java loading overlay stay above WebView and game UI, then fade out over 3 seconds after the page is ready.
- Added a delayed WebView camera permission bridge so Android asks for camera access only after web content requests camera/media capture.
- Added shared WebView navigation handling for popup windows and external schemes used by Google, Apple, Telegram, market, and intent-based login flows.

## 1.0.18

- Added SDK-owned TextMesh Pro shader build setup so installing the SDK also keeps TMP mobile shaders in Always Included Shaders.
- Made the TMP bootstrap retarget project TextMesh Pro material assets to the mobile distance field shader during configure/build.

## 1.0.17

- Added SDK-owned Android autorotation, HTTP/browser query, deeplink, camera/microphone, notification permission, and optional camera/microphone feature configuration.
- Added runtime Android notification permission prompting controlled by remote config keys and automatic suppression for common custom push notification popups.
- Added WebView camera permission bridge for UniWebView offers and a native Android WebChromeClient fallback for older WebView paths.
- Made ZeyWin WebView offers, locked WebViews, banners, and popups suppress AdMob banners while ZeyWin UI is active.
- Changed popup scheduling so server `popup_delay_sec` and `popup_repeat_sec` drive auto-show timing unless `ConfigurePopupSchedule` is explicitly used.

## 1.0.16

- Added automatic TextMesh Pro dependency/resource bootstrap so SDK installation imports TMP Essential Resources and Examples & Extras into each game project.
- Added build-time TMP resource verification before Android builds and fleet configuration.
- Made SDK UniWebView offers and locked WebViews respect `Screen.safeArea` so offer pages avoid camera cutouts and notches automatically.

## 1.0.15

- Fixed AdMob bootstrap detection so a generated `Assets/GoogleMobileAds` settings folder is not treated as a legacy Google Mobile Ads plugin.
- Added scoped registry repair for existing OpenUPM manifests that are missing the `com.google.ads.mobile` scope.

## 1.0.14

- Disabled Android optimized frame pacing by default in the fleet Android builder to avoid Unity 6 `games-frame-pacing/swappy` static STL Gradle failures during QA APK builds.

## 1.0.13

- Stopped writing `android:label` into the Unity library Android manifest during fleet configuration, preventing Unity 6 launcher manifest merge conflicts.

## 1.0.12

- Made the fleet Android builder fall back to debug signing for phone QA when a project has a stale custom keystore path and no explicit signing environment is provided.

## 1.0.11

- Added `ZeyWinAdsAndroidBuilder`, a shared Unity batchmode APK/AAB builder for fleet phone QA and release artifact checks.
- The Android builder supports output path selection, version name/code overrides, ARM64/ARMv7 selection, and custom keystore signing through `ANDROID_KEYSTORE_*` environment variables.

## 1.0.10

- Fixed TSV parsing in the fleet runner so empty optional columns are preserved and package/version fields cannot shift into the wrong Unity arguments.

## 1.0.9

- Added explicit `--unity-path` and `--require-unity6` fleet runner flags so old and mixed-version game projects can be configured only through an approved Unity 6 editor.
- Made the fleet runner skip rows with missing or invalid Unity editors instead of aborting the whole multi-game run.
- Synced the legacy Google Mobile Ads androidlib manifest App ID during fleet configuration to prevent mixed AdMob App IDs in older Unity projects.

## 1.0.8

- Added environment-variable fallbacks for fleet configuration secrets so API keys and AdMob IDs do not need to appear in Unity command-line logs.
- Added a TSV-driven fleet runner for discovering Unity projects, snapshotting non-git projects, pinning SDK packages, and invoking the configurator.

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
