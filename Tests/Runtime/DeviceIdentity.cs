using NUnit.Framework;
using UnityEngine;

namespace ZeyWinAds.Tests.Runtime
{
    // On-device PlayMode tests — run via Window > General > Test Runner > PlayMode tab, targeting
    // a connected real device (or emulator). Unlike EditMode tests, these get compiled into a
    // temporary test player, installed via adb, and executed for real on-device; results stream
    // back to the Editor's Test Runner window live.
    //
    // Ships with the SDK (Tests/Runtime) so every consuming project gets the same QA suite —
    // enable it by adding "com.zeywin.ads" to the project's Packages/manifest.json "testables".
    public class DeviceIdentity
    {
        [Test]
        public void RunsOnAndroidDevice()
        {
            Debug.Log($"[ZeyWinAds QA] Running on: {SystemInfo.deviceModel}, " +
                $"platform={Application.platform}");

            if (Application.platform != RuntimePlatform.Android)
            {
                Assert.Ignore($"On-device QA suite runs against Android; current platform is {Application.platform}.");
            }
        }
    }
}
