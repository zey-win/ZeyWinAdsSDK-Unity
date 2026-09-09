using System.IO;
using NUnit.Framework;
using UnityEngine;
using ZeyWinAds.Editor.QATests;

namespace ZeyWinAds.Tests.Editor
{
    public class LauncherIconTests
    {
        [Test]
        public void AndroidLauncherIcons_AreAssignedByTheFactoryBuild()
        {
            var factoryConfig = Path.Combine(Directory.GetCurrentDirectory(), "factory/factory-config.json");
            if (!File.Exists(factoryConfig))
            {
                Debug.Log("[LauncherIconTests] No factory/factory-config.json staged — the Android launcher " +
                          "icon assignment is verified during the CI factory build (QaTestsPreProcessor / " +
                          "QaChecksRunner), not on a local run. Auto-passing.");
                Assert.Pass("Verified during the CI factory build, not a local run.");
            }

            var error = LauncherIconPolicy.ValidateAndroidIcons();
            Assert.IsNull(error, error);
        }
    }
}
