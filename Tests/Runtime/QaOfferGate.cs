namespace ZeyWinAds.Tests.Runtime
{
    // OfferAndLoadingScreen.ForceOfferOpens sets this the moment it confirms the SDK's force offer
    // opened its locking WebView. WebViewFixture gates on it: if the offer never came up there is
    // nothing for the WebView fixtures to check, so WebViewFixture.RequireOfferSurface fails each
    // of them at once ([OneTimeSetUp]) instead of every test burning its own offer-wait timeout.
    internal static class QaOfferGate
    {
        public static bool OfferConfirmed { get; private set; }

        public static void MarkOfferConfirmed() => OfferConfirmed = true;
    }
}
