# ZeyWin Ads SDK for Unity

Mobile advertising SDK for Unity games. Supports interstitial, rewarded, and banner ads with **automatic Google AdMob fallback**.

## Installation

### Via Package Manager (Git URL)

1. Open **Window > Package Manager**
2. Click **+** button → **Add package from git URL**
3. Enter: `https://github.com/thewhiteapps/ZeyWinAdsSDK-Unity.git`
4. Click **Add**

On first import the SDK will automatically add the Google Mobile Ads (AdMob) package via the OpenUPM scoped registry. Unity will reload and resolve `com.google.ads.mobile` for you.

### Manual Installation

1. Download the package
2. Copy `ZeyWinAds` folder to your project's `Assets` folder

## Setup

1. Open **ZeyWinAds > Settings** from the Unity menu — this creates `Assets/Resources/ZeyWinAdsSettings.asset`.
2. Fill in your AdMob **App ID** and **Ad Unit IDs** for Android and iOS. They are written into `AndroidManifest.xml` and `Info.plist` automatically at build time.
3. Set **Enable AdMob** to `false` if you want to disable the fallback entirely.

If your project does not yet have `Assets/Plugins/Android/AndroidManifest.xml`, create one (or run **ZeyWinAds > Update AndroidManifest Queries** which generates it).

## Quick Start

```csharp
using ZeyWinAds;
using UnityEngine;

public class AdExample : MonoBehaviour
{
    void Start()
    {
        ZeyWinAds.Initialize("YOUR_API_KEY");
    }

    public void ShowInterstitial()
    {
        if (ZeyWinAds.IsInterstitialReady())
            ZeyWinAds.ShowInterstitial(onClose: () => Debug.Log("Interstitial closed"));
    }

    public void ShowRewarded()
    {
        if (ZeyWinAds.IsRewardedReady())
            ZeyWinAds.ShowRewarded(
                onReward: amount => Debug.Log($"Earned: {amount}"),
                onClose:  () => Debug.Log("Rewarded closed"));
    }

    public void ShowBanner() => ZeyWinAds.ShowBanner(BannerPosition.Bottom);
    public void HideBanner() => ZeyWinAds.HideBanner();
}
```

## Mediation behaviour

- ZeyWin and AdMob preload **in parallel** at SDK init.
- On every `Show*` call: ZeyWin's ad is shown first if available; otherwise AdMob's. If neither is ready, the close callback is invoked immediately.
- AdMob is **not** subject to ZeyWin's anti-fraud / geo / SIM gating — if the device is blocked from ZeyWin ads, AdMob still works.
- `IsInterstitialReady()` / `IsRewardedReady()` / `IsBannerReady()` return true if **either** network has an ad ready.
- `HideBanner()` hides whichever network is currently showing the banner.

## Ad Types

- **InterstitialAd** — full-screen ads (ZeyWin → AdMob fallback)
- **RewardedAd** — full-screen ads with rewards (ZeyWin → AdMob fallback)
- **BannerAd** — banner ads, top/bottom (ZeyWin → AdMob fallback)
- **NativeAd**, **PopupAd** — ZeyWin-only

## Requirements

- Unity 2020.3 or later
- iOS 11+ / Android API 21+
- Google Mobile Ads Unity package (auto-installed)
