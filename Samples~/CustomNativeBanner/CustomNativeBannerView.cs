using System.Collections;
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

    // Legacy UI.Text + the SDK's own OS-backed dynamic font instead of TextMeshPro: ad
    // copy language is unpredictable (whatever the ad network serves), and TMP has no
    // complex-script shaping (Tamil, Arabic, Thai, CJK, ...) no matter which font/atlas
    // mode is used — glyphs may exist but render unshaped or not at all. Routing through
    // the OS's own text renderer sidesteps that entirely. Using ZeyWinAds.GetPreferredFont()
    // (rather than duplicating its family list here) means this stays in sync with
    // whatever the SDK's own built-in ad views use, automatically.
    [Header("Content")]
    [SerializeField] private RawImage iconImage;
    [SerializeField] private Text headlineText;
    [SerializeField] private Text bodyText;
    [SerializeField] private Text ctaText;

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

        SetText(headlineText, info.Headline);
        SetText(bodyText, info.Body);
        SetText(ctaText, info.CtaText);

        if (iconImage != null && !string.IsNullOrEmpty(info.IconUrl) && info.IconUrl != _loadedIconUrl)
        {
            if (_iconLoadRoutine != null)
                StopCoroutine(_iconLoadRoutine);
            _iconLoadRoutine = StartCoroutine(LoadIcon(info.IconUrl));
        }

        info.TrackImpression?.Invoke();
    }

    private static void SetText(Text field, string value)
    {
        if (field == null) return;

        Font preferredFont = ZeyWinAds.ZeyWinAds.GetPreferredFont();
        if (preferredFont != null)
            field.font = preferredFont;

        // Ad copy comes from the ad network and is untrusted — a stray '<' in a creative
        // (e.g. "Save <3 now") gets parsed as a rich text tag and swallows the rest of the
        // string, which is why some creatives rendered fine and others silently lost text.
        field.supportRichText = false;
        field.text = value;
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
