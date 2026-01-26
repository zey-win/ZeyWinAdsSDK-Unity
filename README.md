# ZeyWin Ads SDK for Unity

Mobile advertising SDK for Unity games. Supports interstitial, rewarded, and banner ads.

## Installation

### Via Package Manager (Git URL)

1. Open **Window > Package Manager**
2. Click **+** button → **Add package from git URL**
3. Enter: `https://github.com/AdrianGroszworker/zeywinads.git?path=unity-sdk/Assets/ZeyWinAds`
4. Click **Add**

### Manual Installation

1. Download the package
2. Copy `ZeyWinAds` folder to your project's `Assets` folder

## Quick Start

```csharp
using ZeyWinAds;
using UnityEngine;

public class AdExample : MonoBehaviour
{
    void Start()
    {
        // Initialize SDK with your API key
        ZeyWinAds.Initialize("YOUR_API_KEY");
    }

    public void ShowInterstitial()
    {
        var ad = new InterstitialAd();
        ad.OnAdLoaded += () => ad.Show();
        ad.OnAdClosed += () => Debug.Log("Ad closed");
        ad.Load();
    }

    public void ShowRewarded()
    {
        var ad = new RewardedAd();
        ad.OnAdLoaded += () => ad.Show();
        ad.OnUserRewarded += (reward) => {
            Debug.Log($"User earned reward: {reward.amount} {reward.type}");
        };
        ad.Load();
    }
}
```

## Ad Types

- **InterstitialAd** - Full-screen ads
- **RewardedAd** - Full-screen ads with rewards
- **BannerAd** - Banner ads (top/bottom)

## Requirements

- Unity 2020.3 or later
- iOS 11+ / Android API 21+
