using NUnit.Framework;
using ZeyWinAds.Editor.QATests;

namespace ZeyWinAds.Tests.Editor
{
    public class CrashFixesPolicyTests
    {
        [Test]
        public void KnownCrashFixes_ArePresent()
        {
            var error = CrashFixesPolicy.ValidateAll();
            Assert.IsNull(error, error);
        }
    }
}
