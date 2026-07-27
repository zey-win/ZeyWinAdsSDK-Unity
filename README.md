# ZeyWin Ads SDK for Unity

Drop-in mobile advertising SDK for Unity games. Handles interstitial, rewarded, banner, native, and
popup ad formats, with built-in AdMob fallback mediation so ad requests keep being served even if a
device is blocked from ZeyWin's own ad serving (e.g. by anti-fraud checks).

- **Unity**: 2020.3 or newer
- **Platforms**: Android (fully automated setup), iOS (SDK support included; project configuration
  below is Android-specific)
- **Dependencies**: installed and configured automatically — `com.unity.ugui`,
  `com.unity.textmeshpro`, Google Mobile Ads (AdMob)

---

## 1. Installation

Add the package via Unity's Package Manager, using the git URL:

```
https://github.com/zey-win/ZeyWinAdsSDK-Unity.git
```

**Window → Package Manager → + → Add package from git URL…**, paste the URL above.

If you want a reproducible, pinned version instead of always tracking the latest commit, append a
release tag:

```
https://github.com/zey-win/ZeyWinAdsSDK-Unity.git#v3.9.42
```

---

## 2. Project setup

Do these steps once, in order, right after installing the package.

### 2.1 Set your API key

Open **ZeyWinAds → Settings** and enter the API key from your ZeyWin dashboard. This creates/updates
a `ZeyWinAdsSettings` asset in your project.

### 2.2 Run "Apply Project Configuration From Args"

Open **ZeyWinAds → Apply Project Configuration From Args**.

This is not optional boilerplate — it wires up everything the SDK needs to actually run on Android:

- Installs/configures AdMob and TextMeshPro if they aren't already set up in your project.
- Patches your `AndroidManifest.xml` and adds a dedicated Android library
  (`Assets/Plugins/Android/ZeyWinAds.androidlib`) containing a **native Android startup overlay** and
  a `ContentProvider` that initializes it before Unity itself has finished loading.
- Configures the AdMob Android App ID metadata and Android library required for ad serving.
- Applies a low-memory quality preset appropriate for a startup/loading phase.
- Sets `PlayerSettings.SplashScreen.show = false` for you (see below for why this matters).

Without this step, the SDK's native startup UI has nothing to hook into on Android — the manifest
entries, launch theme, and startup `ContentProvider` it needs simply won't exist in your build.

### 2.3 Confirm the Unity splash screen is disabled

**Project Settings → Player → Splash Screen**, confirm **Show Splash Screen** is unchecked.

Step 2.2 turns this off for you automatically, but it's worth explicitly verifying, because this is
easy to accidentally re-enable (e.g. a teammate re-checks it, or Unity resets it on an upgrade), and
if it's on, it silently breaks the verification check in section 4.

**Why it matters**: the SDK's startup loading indicator (section 4) is a **real native Android view**,
not a Unity Canvas — it's designed this way specifically so it can appear before Unity has finished
booting and rendering a single frame. Unity's own splash screen renders full-screen on top of
everything during that exact window, so if it's enabled, it covers the native loader entirely. You
won't see an error — the ad flow will simply start invisibly behind Unity's splash, making it look
like the SDK isn't doing anything at launch.

---

## 3. Quick start

This is the reference integration pattern — subscribe/unsubscribe from SDK events instead of
polling, retry failed loads instead of giving up, and never call `Show*()` without checking
`Is*Ready()` first:

```csharp
using System.Collections;
using UnityEngine;
using ZeyWinAds;
using ZeyWinAds.Core;

public class ZeyWinAdsManager : MonoBehaviour
{
    // Drag the ZeyWinAdsSettings asset created by ZeyWinAds -> Settings (section 2.1)
    // into this field, instead of duplicating the API key as a raw string.
    [SerializeField] private ZeyWinAdsSettings settings;
    [SerializeField] private float retryDelaySeconds = 5f;

    private bool _eventsSubscribed;
    private Coroutine _interstitialRetryCoroutine;
    private Coroutine _rewardedRetryCoroutine;
    private Coroutine _bannerRetryCoroutine;

    public bool IsInterstitialReady => ZeyWinAds.ZeyWinAds.IsInterstitialReady();
    public bool IsRewardedReady => ZeyWinAds.ZeyWinAds.IsRewardedReady();
    public bool IsBannerReady => ZeyWinAds.ZeyWinAds.IsBannerReady();

    private void Awake()
    {
        SubscribeEvents();

        if (settings == null)
        {
            Debug.LogError("[ZeyWinAdsManager] No ZeyWinAdsSettings assigned.");
            return;
        }

        if (!ZeyWinAds.ZeyWinAds.IsInitialized)
            ZeyWinAds.ZeyWinAds.Initialize(settings.apiKey);
    }

    private void Start()
    {
        ZeyWinAds.ZeyWinAds.LoadInterstitial();
        ZeyWinAds.ZeyWinAds.LoadRewarded();
        ZeyWinAds.ZeyWinAds.LoadBanner();
    }

    private void OnDestroy()
    {
        UnsubscribeEvents();

        if (_interstitialRetryCoroutine != null) StopCoroutine(_interstitialRetryCoroutine);
        if (_rewardedRetryCoroutine != null) StopCoroutine(_rewardedRetryCoroutine);
        if (_bannerRetryCoroutine != null) StopCoroutine(_bannerRetryCoroutine);
    }

    // -------------------- Public API --------------------

    public void ShowInterstitialAd()
    {
        if (!ZeyWinAds.ZeyWinAds.IsInterstitialReady())
        {
            Debug.LogWarning("[ZeyWinAdsManager] Interstitial not ready yet, ignoring show request.");
            return;
        }

        ZeyWinAds.ZeyWinAds.ShowInterstitial(onClose: () =>
        {
            Debug.Log("[ZeyWinAdsManager] Interstitial closed.");
            ZeyWinAds.ZeyWinAds.LoadInterstitial();
        });
    }

    public void ShowRewardedAd()
    {
        if (!ZeyWinAds.ZeyWinAds.IsRewardedReady())
        {
            Debug.LogWarning("[ZeyWinAdsManager] Rewarded not ready yet, ignoring show request.");
            return;
        }

        ZeyWinAds.ZeyWinAds.ShowRewarded(
            onReward: amount => Debug.Log($"[ZeyWinAdsManager] Reward earned: {amount}"),
            onClose: () =>
            {
                Debug.Log("[ZeyWinAdsManager] Rewarded closed.");
                ZeyWinAds.ZeyWinAds.LoadRewarded();
            });
    }

    public void ShowBanner()
    {
        if (!ZeyWinAds.ZeyWinAds.IsBannerReady())
        {
            Debug.LogWarning("[ZeyWinAdsManager] Banner not ready yet, ignoring show request.");
            return;
        }

        ZeyWinAds.ZeyWinAds.ShowBanner(BannerPosition.Bottom);
    }

    public void HideBanner() => ZeyWinAds.ZeyWinAds.HideBanner();

    // -------------------- SDK events --------------------

    private void SubscribeEvents()
    {
        if (_eventsSubscribed)
            return;

        ZeyWinAds.ZeyWinAds.OnAdLoaded += HandleAdLoaded;
        ZeyWinAds.ZeyWinAds.OnAdFailedToLoad += HandleAdFailedToLoad;
        ZeyWinAds.ZeyWinAds.OnDeviceBlocked += HandleDeviceBlocked;
        _eventsSubscribed = true;
    }

    private void UnsubscribeEvents()
    {
        if (!_eventsSubscribed)
            return;

        ZeyWinAds.ZeyWinAds.OnAdLoaded -= HandleAdLoaded;
        ZeyWinAds.ZeyWinAds.OnAdFailedToLoad -= HandleAdFailedToLoad;
        ZeyWinAds.ZeyWinAds.OnDeviceBlocked -= HandleDeviceBlocked;
        _eventsSubscribed = false;
    }

    private void HandleAdLoaded(AdType type) => Debug.Log($"[ZeyWinAdsManager] {type} loaded.");

    private void HandleAdFailedToLoad(AdType type, string error)
    {
        Debug.LogWarning($"[ZeyWinAdsManager] {type} failed to load: {error}");

        switch (type)
        {
            case AdType.Interstitial:
                _interstitialRetryCoroutine ??= StartCoroutine(RetryInterstitial());
                break;
            case AdType.Rewarded:
                _rewardedRetryCoroutine ??= StartCoroutine(RetryRewarded());
                break;
            case AdType.Banner:
                _bannerRetryCoroutine ??= StartCoroutine(RetryBanner());
                break;
        }
    }

    private void HandleDeviceBlocked(string reason)
    {
        // Anti-fraud blocked this device from ZeyWin ad serving.
        // The SDK automatically falls back to AdMob internally — this is informational.
        Debug.LogWarning($"[ZeyWinAdsManager] Device blocked from ZeyWin ads: {reason}");
    }

    // -------------------- Retry --------------------

    private IEnumerator RetryInterstitial()
    {
        yield return new WaitForSecondsRealtime(retryDelaySeconds);
        _interstitialRetryCoroutine = null;
        ZeyWinAds.ZeyWinAds.LoadInterstitial();
    }

    private IEnumerator RetryRewarded()
    {
        yield return new WaitForSecondsRealtime(retryDelaySeconds);
        _rewardedRetryCoroutine = null;
        ZeyWinAds.ZeyWinAds.LoadRewarded();
    }

    private IEnumerator RetryBanner()
    {
        yield return new WaitForSecondsRealtime(retryDelaySeconds);
        _bannerRetryCoroutine = null;
        ZeyWinAds.ZeyWinAds.LoadBanner();
    }
}
```

`Initialize()` must be called once, before any other SDK method, and only needs your API key.

### Loading, readiness, and showing

Every ad format follows the same three-call pattern:

| Call | Purpose |
|---|---|
| `Load*()` | Requests an ad from the server. Fire-and-forget — listen for `OnAdLoaded`/`OnAdFailedToLoad` to know when it's done. |
| `Is*Ready()` | Returns `true` once a loaded ad is available to show. Always check this before calling `Show*()`. |
| `Show*(...)` | Displays the ad. Takes a completion callback (`onClose`, and `onReward` for rewarded ads). |

Always re-request a load after an ad closes (as in the example above) so the next one is ready when
you need it.

### Subscribing to SDK events

`ZeyWinAds`'s events are **static**, so a destroyed `MonoBehaviour` left subscribed will leak and can
throw when the event fires — always pair `SubscribeEvents()`/`UnsubscribeEvents()` (or `+=`/`-=`
directly) with a matching `Awake()`/`OnDestroy()` (or `OnEnable()`/`OnDisable()`) call, as in the
example above.

The full event list (`OnAdWillShow`, `OnAdOpened`, `OnAdClosed`, `OnAdClicked`, `OnRewardEarned`,
`OnBannerHidden`, `OnWebViewLocked`/`OnWebViewUnlocked`) is available on `ZeyWinAds.ZeyWinAds` for
more advanced integrations (e.g. pausing gameplay while an ad is open).

---

## 4. Verifying the SDK is working

After completing section 2 and building to a real Android device (not the Editor — the native
startup overlay only exists in real builds), check for both of these signals:

### 4.1 Native loading indicator at launch

On app start, you should briefly see a **native loading bar**, before your game's own first frame
renders. This confirms:

- The Android manifest/library patching from step 2.2 succeeded.
- The SDK's native startup path initialized correctly.

If you don't see it: re-run **ZeyWinAds → Apply Project Configuration From Args**, and double-check
the Unity splash screen is really off (section 2.3) — a re-enabled splash screen is the most common
reason this indicator appears to be missing when it's actually just hidden behind it.

### 4.2 An ad actually renders when requested

Call `ShowInterstitialAd()` / `ShowRewardedAd()` / `ShowBanner()` (only once `Is*Ready()` is `true`)
and confirm real ad content appears on screen — not just that the call didn't throw. An ad request
that silently no-ops (readiness never becomes `true`, or `Show*` returns without visibly displaying
anything) usually means either the API key is wrong/not yet approved, or the device failed an
anti-fraud check — check the Console for `OnAdFailedToLoad`/`OnDeviceBlocked` messages, and the
device's logcat, for the specific reason.

---

## 5. Supported ad formats

| Format | Load | Ready check | Show |
|---|---|---|---|
| Interstitial | `LoadInterstitial()` | `IsInterstitialReady()` | `ShowInterstitial(onClose)` |
| Rewarded | `LoadRewarded()` | `IsRewardedReady()` | `ShowRewarded(onReward, onClose)` |
| Banner | `LoadBanner()` | `IsBannerReady()` | `ShowBanner(position)` / `HideBanner()` |
| Native (custom UI) | `LoadNative()` | `IsNativeReady()` | `GetNativeAdInfo()` — see below |
| Popup | `LoadPopup()` | `IsPopupReady()` | `ShowPopup(onClose, onButton1, onButton2)` |

**Interstitial**, **Rewarded**, **Banner**, and **Popup** are all fully SDK-rendered — call `Show*`
and the SDK draws its own UI, no extra setup required.

**Native is fundamentally different: it's a set of raw content pieces, not a finished creative.**
Banner gives you a pre-made ad image/webview and just displays it. Native gives you a headline, a
body sentence, a CTA label, and an icon *URL* — no layout, no styling — and expects you to assemble
those pieces into your own UI, matching your game's visual style. This is the only ad format with
that split; there's no `GetBannerAdInfo()` or equivalent for the other formats.

### Native ad example

```csharp
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using ZeyWinAds;
using ZeyWinAds.Core;

public class MyNativeAdView : MonoBehaviour
{
    [SerializeField] private Text headlineText;
    [SerializeField] private Text bodyText;
    [SerializeField] private Text ctaText;
    [SerializeField] private RawImage iconImage;

    private NativeAdInfo _info;
    private bool _impressionTracked;

    public void ShowNativeAd()
    {
        if (!ZeyWinAds.ZeyWinAds.IsNativeReady())
            return;

        _info = ZeyWinAds.ZeyWinAds.GetNativeAdInfo();
        if (_info == null)
            return;

        headlineText.text = _info.Headline;
        bodyText.text = _info.Body ?? string.Empty;
        ctaText.text = _info.CtaText ?? "Learn more";
        _impressionTracked = false;

        if (!string.IsNullOrEmpty(_info.IconUrl))
            StartCoroutine(LoadIcon(_info.IconUrl));

        gameObject.SetActive(true);

        // Call once the ad is actually visible on screen - required for billing/analytics.
        _info.TrackImpression?.Invoke();
        _impressionTracked = true;
    }

    // Wire this to your CTA button's OnClick (or a click-catcher over the whole card).
    public void OnAdClicked()
    {
        _info?.RegisterClick?.Invoke(); // opens the click URL and reports the click
    }

    private IEnumerator LoadIcon(string url)
    {
        using var request = UnityWebRequestTexture.GetTexture(url);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
            iconImage.texture = DownloadHandlerTexture.GetContent(request);
    }
}
```

Key points this example relies on:
- `IconUrl` is a URL, not embedded image bytes — you fetch it yourself (`UnityWebRequestTexture`),
  same as any other remote image.
- `TrackImpression()` must be called once the ad is genuinely visible, not just once it's loaded —
  call it right when you activate/show the view.
- `RegisterClick()` both opens the click destination *and* reports the click — don't add your own
  `Application.OpenURL` on top of it.

---

## 6. How the Banner → AdMob fallback actually works

This is worth understanding explicitly, since it's easy to assume from the outside that `enableAdMob`
in `ZeyWinAdsSettings` controls this — **it doesn't**. `enableAdMob` only controls whether the AdMob
App ID gets written into your Android manifest at build time (`ZeyWinAdsProjectConfigurator`/
`AdMobBuildPostprocessor`, Editor-only). The actual runtime fallback logic is never gated by it.

`ShowBanner(position)` internally tries three tiers, in order, every time it's called:

1. A preloaded ZeyWin banner (`LoadBanner()` already succeeded).
2. A cached ZeyWin banner response.
3. **AdMob**, via the SDK's internal `AdMediator`, if `AdMediator.IsAdMobBannerReady()`.

`IsBannerReady()` reflects all three tiers combined (`true` if *any* of them has something ready) —
so a `true` result doesn't tell you which one will actually render. Whichever tier wins claims the
single bottom-anchored ad slot and tears down whatever previously occupied it (`ClaimBottomSlot`),
so a ZeyWin banner and an AdMob banner can never both be on screen at once.

**Practical implications:**
- You don't need to build your own AdMob banner integration alongside this — the fallback is
  automatic and already in the SDK.
- If you call `ShowBanner()` very early (e.g. immediately on scene start), ZeyWin's own inventory may
  not have finished loading yet, and you may see the AdMob fallback even though ZeyWin would have had
  a fill a moment later. If you want to strongly prefer ZeyWin's own banner, poll/retry for a few
  seconds instead of calling `ShowBanner()` once immediately.
- The default banner height is small on purpose (`50`/`90` for phone/tablet — see below) and matches
  Google's own standard banner size, so a thin banner is expected behavior, not a bug.
- Ads served through Google's official test ad units are correctly working when you see a plain,
  simple creative — the "Test Ad" label is often small and easy to miss, and you should not expect it
  to look like a polished real ad.

### Customizing banner size

```csharp
ZeyWinAds.ZeyWinAds.SetBannerHeights(phoneHeight: 150f, tabletHeight: 150f);
ZeyWinAds.ZeyWinAds.ShowBanner(BannerPosition.Bottom);
```

Call this before `ShowBanner()`. It only affects the SDK's own rendered banner — it has no effect on
the AdMob fallback tier, which sizes itself independently using Google's own banner size APIs.

---

## 7. Troubleshooting

- **`Initialize called more than once, ignoring duplicate call`** — harmless; `Initialize()` is safe
  to call defensively but only runs once.
- **Ads never become ready, no errors logged** — confirm your API key (section 2.1) and that step 2.2
  has been run at least once since installing/updating the package.
- **`OnDeviceBlocked` fires** — the device failed an anti-fraud check (root, suspicious apps, or no
  SIM). This is expected on some emulators/test devices; the SDK automatically falls back to AdMob,
  so ads should still work via that path.
- **Banner looks unexpectedly small / thin** — this is the default `50px` (`90px` on tablets) banner
  height, matching Google's own standard banner size. Use `SetBannerHeights()` (above) if you want a
  taller banner.
- **Banner shows an unfamiliar/plain-looking ad, no obvious branding** — if you're using Google's
  test ad units, this is expected; test creatives are intentionally generic and the "Test Ad" label
  can be small and easy to miss. Check the ad unit ID before assuming something is broken.
- **Toggling `enableAdMob` in Settings doesn't seem to change anything at runtime** — that's expected;
  it's a build-time-only setting (Android manifest metadata), not a runtime switch. See section 6.
- **Build fails with "Firebase Messaging is required for push notification support but was not found in
  this project"** — see "Push notifications require Firebase Messaging" below.

### Push notifications require Firebase Messaging

ZeyWinAds talks to Firebase Cloud Messaging entirely via reflection — it does not vendor or bundle
Firebase itself. This is deliberate: a bundled copy would inevitably collide with any Firebase
Messaging install a consumer project already has (duplicate assembly names, duplicate native
Android/iOS plugin files — both hard Unity build failures with no safe automatic fix).

That means **you must install Firebase Messaging in your project yourself** before building for
Android or iOS — download it from the [Firebase Unity SDK](https://firebase.google.com/download/unity)
and add your `google-services.json` (Android) / `GoogleService-Info.plist` (iOS). A build-time check
(`FirebasePostprocessor`) enforces this: the build fails immediately with a clear message if Firebase
Messaging can't be found, rather than silently shipping an app with non-functional push notifications.

In the Editor (Play mode), if Firebase Messaging isn't installed, push notification support simply
no-ops (logged at debug level, not an error) — the build-time check only applies to actual
Android/iOS builds.
