using NUnit.Framework;
using ZeyWinAds.Editor.QATests;

namespace ZeyWinAds.Tests.Editor
{
    public class MinifyPolicyTests
    {
        [Test]
        public void AndroidMinifyRelease_IsOn()
        {
            var error = MinifyPolicy.ValidateMinifyRelease();
            Assert.IsNull(error, error);
        }
    }
}
