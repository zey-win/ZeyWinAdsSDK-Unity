using NUnit.Framework;
using UnityEditor;
using ZeyWinAds.Editor;

namespace ZeyWinAds.Tests.Editor
{
    public class ApiLevelPolicyTests
    {
        [Test]
        public void TargetSdkVersion_IsExplicitAndMeetsPlayStorePolicy()
        {
            var target = PlayerSettings.Android.targetSdkVersion;
            Assert.AreNotEqual(AndroidSdkVersions.AndroidApiLevelAuto, target,
                "targetSdkVersion is set to Automatic — pin an explicit API level for reproducible CI builds.");
            Assert.GreaterOrEqual((int)target, ApiLevelPolicy.MinRequiredTargetSdk,
                $"targetSdkVersion {(int)target} is below the current Play Store minimum ({ApiLevelPolicy.MinRequiredTargetSdk}).");
        }

        [Test]
        public void MinSdkVersion_MatchesSupportedFloorExactly()
        {
            var minSdk = (int)PlayerSettings.Android.minSdkVersion;
            Assert.AreEqual(ApiLevelPolicy.RequiredMinSdk, minSdk,
                $"minSdkVersion is {minSdk}, expected exactly {ApiLevelPolicy.RequiredMinSdk} — " +
                "raising it needlessly excludes supportable devices; lowering it may break bundled SDKs.");
        }
    }
}
