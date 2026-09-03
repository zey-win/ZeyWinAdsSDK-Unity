using NUnit.Framework;

namespace ZeyWinAds.Tests.Runtime.WebView
{
    // Runs once before any test in the ZeyWinAds.Tests.Runtime.WebView group (a namespace-scoped
    // [SetUpFixture]; the parent namespace's QaLogAssertSetup still runs first). If the SDK's force
    // offer never opened — OfferAndLoadingScreen.ForceOfferOpens failed — every WebView test would
    // otherwise wait out its own ~20 s offer timeout and fail with the same message. Fail the whole
    // group here instead, instantly: a OneTimeSetUp failure marks every contained test failed
    // without running it.
    [SetUpFixture]
    public class WebViewOfferPrecondition
    {
        [OneTimeSetUp]
        public void RequireOfferSurface()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // OfferConfirmed = ForceOfferOpens verified it. IsLocked = the offer is up right now
            // regardless (e.g. a filtered run that skipped ForceOfferOpens but the SDK opened it).
            if (QaOfferGate.OfferConfirmed || global::ZeyWinAds.UI.WebViewLock.IsLocked)
                return;

            Assert.Fail("Skipping the WebView test group: OfferAndLoadingScreen.ForceOfferOpens did " +
                "not open the offer surface (force offer disabled for this device/app, or the device " +
                "is geo/no-SIM blocked server-side). There is nothing for the WebView checks to run " +
                "against.");
#endif
        }
    }
}
