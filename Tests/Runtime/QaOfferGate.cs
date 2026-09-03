namespace ZeyWinAds.Tests.Runtime
{
    // OfferAndLoadingScreen.ForceOfferOpens sets this the moment it confirms the SDK's force offer
    // opened its locking WebView. The ZeyWinAds.Tests.Runtime.WebView fixtures gate on it: if the
    // offer never came up there is nothing for them to check, so WebViewOfferPrecondition fails the
    // whole WebView group at once instead of each test burning its own offer-wait timeout.
    internal static class QaOfferGate
    {
        public static bool OfferConfirmed { get; private set; }

        public static void MarkOfferConfirmed() => OfferConfirmed = true;
    }
}
