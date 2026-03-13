using System;
using UnityEngine;
using ZeyWinAds.Core;

namespace ZeyWinAds.Ads
{
    /// <summary>
    /// Popup ad type. Data-only — no SDK rendering.
    /// Use GetPopupAdInfo() to retrieve data and build your own popup UI.
    /// </summary>
    public class PopupAd : BaseAd
    {
        public override AdType AdType => AdType.Popup;

        protected override void OnShow()
        {
            // Popup ads are data-only, no SDK rendering.
            Debug.Log($"[ZeyWinAds] Popup ad ready (data-only): {AdData.ad_id}");
        }
    }
}
