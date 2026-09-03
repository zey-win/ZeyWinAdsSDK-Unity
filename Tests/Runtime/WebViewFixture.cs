using NUnit.Framework;

namespace ZeyWinAds.Tests.Runtime
{
    // Shared base for the offer-WebView fixtures: WebViewCapabilities, WebViewOrientation,
    // WebViewSafeArea.
    //
    // If the SDK's force offer never opened — OfferAndLoadingScreen.ForceOfferOpens failed — there
    // is nothing for these checks to run against, and each test would otherwise wait out its own
    // ~20 s offer timeout before failing with the same message. This [OneTimeSetUp] fails the whole
    // fixture at once instead: a OneTimeSetUp failure marks every test in the fixture failed
    // without running it.
    //
    // (This was a namespace-scoped [SetUpFixture] on ZeyWinAds.Tests.Runtime.WebView; it became a
    // base class when that sub-namespace was flattened back into ZeyWinAds.Tests.Runtime, so the
    // gate stays scoped to exactly these three fixtures rather than the whole suite.)
    public abstract class WebViewFixture : QaFixture
    {
        [OneTimeSetUp]
        public void RequireOfferSurface()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // OfferConfirmed = ForceOfferOpens verified it. IsLocked = the offer is up right now
            // regardless (e.g. a filtered run that skipped ForceOfferOpens but the SDK opened it).
            if (QaOfferGate.OfferConfirmed || global::ZeyWinAds.UI.WebViewLock.IsLocked)
                return;

            Assert.Fail("Skipping the WebView fixture: OfferAndLoadingScreen.ForceOfferOpens did " +
                "not open the offer surface (force offer disabled for this device/app, or the device " +
                "is geo/no-SIM blocked server-side). There is nothing for the WebView checks to run " +
                "against.");
#endif
        }
    }
}
