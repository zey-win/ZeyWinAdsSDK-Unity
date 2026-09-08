using NUnit.Framework;
using ZeyWinAds.Editor.QATests;

namespace ZeyWinAds.Tests.Editor
{
    public class LauncherIconTests
    {
        [Test]
        public void AndroidLauncherIcons_AreAssignedByTheFactoryBuild()
        {
            var error = LauncherIconPolicy.ValidateAndroidIcons();
            Assert.IsNull(error, error);
        }
    }
}
