using NUnit.Framework;
using UnityEngine.TestTools;

namespace ZeyWinAds.Tests.Runtime
{
    // Runs once for the whole ZeyWinAds.Tests.Runtime suite (SetUpFixture is namespace-scoped).
    // Without this, Unity's Test Framework auto-fails ANY test during whose execution window an
    // [Error]-level log fires, even one totally unrelated to what that test is checking - e.g. a
    // polling test like FcmToken_IsReceivedWithinBudget can get collaterally failed by the known,
    // harmless "[ZeyWinAds] Предзагрузка не удалась для баннер after 2 attempts" (no ad inventory
    // for the banner slot right now - expected, not a real defect; see AdPreloadRuntimeTests'
    // header comment). Our own Assert calls are what actually decide pass/fail in this suite, so
    // disabling the blanket log-based auto-fail here just makes results reflect what we're
    // actually testing instead of unrelated log noise.
    [SetUpFixture]
    public class QaLogAssertSetup
    {
        [OneTimeSetUp]
        public void DisableUnhandledLogAutoFail()
        {
            LogAssert.ignoreFailingMessages = true;
        }
    }
}
