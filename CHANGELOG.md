# Changelog

All notable changes to this package are documented in this file.

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
