using NUnit.Framework;
using ZeyWinAds.Editor.QATests;

namespace ZeyWinAds.Tests.Editor
{
    public class ApiLevelPolicyTests
    {
        [Test]
        public void TargetSdkVersion_IsExplicitAndMeetsPlayStorePolicy()
        {
            var error = ApiLevelPolicy.ValidateTargetSdk();
            Assert.IsNull(error, error);
        }

        [Test]
        public void MinSdkVersion_MatchesSupportedFloorExactly()
        {
            var error = ApiLevelPolicy.ValidateMinSdk();
            Assert.IsNull(error, error);
        }
    }
}
