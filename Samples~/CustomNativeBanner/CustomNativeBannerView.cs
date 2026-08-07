using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using ZeyWinAds.Core;

// Put this on the root of a self-contained banner prefab (with its own Canvas) and
// assign it to AdManager's "Custom Native Banner Prefab" field to fully control the
// native banner's design instead of using ZeyWin's built-in native ad rendering.
[RequireComponent(typeof(Canvas))]
public class CustomNativeBannerView : MonoBehaviour
{
    // Matches ZeyWinAds.Ads.NativeAd's own canvas sorting order, so this renders in the
    // same stacking tier as the native banner it replaces — above ordinary in-game UI
    // (which can otherwise cover it depending on a given scene's own canvas setup), but
    // still below interstitials/rewarded (1000/1001) and popups (32760).
    private const int CanvasSortingOrder = 998;

    [Header("Content")]
    [SerializeField] private RawImage iconImage;
    [SerializeField] private TMP_Text headlineText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private TMP_Text ctaText;
    
    private System.Action _registerClick;
    private Coroutine _iconLoadRoutine;
    private string _loadedIconUrl;

    private void Awake()
    {
        GetComponent<Canvas>().sortingOrder = CanvasSortingOrder;

    }

    public void Bind(NativeAdInfo info)
    {
        if (info == null) return;

        _registerClick = info.RegisterClick;

        if (headlineText != null) headlineText.text = info.Headline;
        if (bodyText != null) bodyText.text = info.Body;
        if (ctaText != null) ctaText.text = info.CtaText;

        if (iconImage != null && !string.IsNullOrEmpty(info.IconUrl) && info.IconUrl != _loadedIconUrl)
        {
            if (_iconLoadRoutine != null)
                StopCoroutine(_iconLoadRoutine);
            _iconLoadRoutine = StartCoroutine(LoadIcon(info.IconUrl));
        }

        info.TrackImpression?.Invoke();
    }

    private IEnumerator LoadIcon(string url)
    {
        using (var request = UnityWebRequestTexture.GetTexture(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success && iconImage != null)
            {
                iconImage.texture = DownloadHandlerTexture.GetContent(request);
                _loadedIconUrl = url;
            }
        }

        _iconLoadRoutine = null;
    }

    public void HandleClick()
    {
        _registerClick?.Invoke();
    }
}
