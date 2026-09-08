using NUnit.Framework;
using ZeyWinAds.Editor.QATests;

namespace ZeyWinAds.Tests.Editor
{
    public class StrippingLevelPolicyTests
    {
        [Test]
        public void ManagedStrippingLevel_IsHigh()
        {
            var error = StrippingLevelPolicy.ValidateStrippingLevel();
            Assert.IsNull(error, error);
        }
    }
}
