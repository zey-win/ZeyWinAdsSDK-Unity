using System.Collections.Generic;
using NUnit.Framework;
using ZeyWinAds.Editor.QATests;

namespace ZeyWinAds.Tests.Editor
{
    public class PlayGamesOnPcPolicyTests
    {
        private static PlayGamesOnPcPolicy.DeclaredFeature Feature(string name, bool required) =>
            new PlayGamesOnPcPolicy.DeclaredFeature { Name = name, Required = required };

        [Test]
        public void RequiredUnsupportedFeature_IsFlagged()
        {
            var errors = PlayGamesOnPcPolicy.FindFeatureViolations(new[]
            {
                Feature("android.hardware.camera", required: true),
            });

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("android.hardware.camera", errors[0]);
        }

        [Test]
        public void NotRequiredUnsupportedFeature_IsCompliant()
        {
            var errors = PlayGamesOnPcPolicy.FindFeatureViolations(new[]
            {
                Feature("android.hardware.camera", required: false),
                Feature("android.hardware.microphone", required: false),
                Feature("android.hardware.touchscreen", required: false),
            });

            Assert.IsEmpty(errors);
        }

        [Test]
        public void UnrelatedFeature_IsIgnored()
        {
            var errors = PlayGamesOnPcPolicy.FindFeatureViolations(new[]
            {
                Feature("android.hardware.vulkan.version", required: true),
                Feature("android.hardware.screen.portrait", required: true),
            });

            Assert.IsEmpty(errors);
        }

        [Test]
        public void EveryUnsupportedFeature_WhenRequired_IsFlaggedIndividually()
        {
            var declared = new List<PlayGamesOnPcPolicy.DeclaredFeature>();
            foreach (string name in PlayGamesOnPcPolicy.UnsupportedFeatures)
                declared.Add(Feature(name, required: true));

            var errors = PlayGamesOnPcPolicy.FindFeatureViolations(declared);

            Assert.AreEqual(PlayGamesOnPcPolicy.UnsupportedFeatures.Count, errors.Count);
        }

        [Test]
        public void StrictMode_FlagsNotRequiredFeatureThatMustBeAbsentForFinalSubmission()
        {
            var errors = PlayGamesOnPcPolicy.FindFeatureViolations(new[]
            {
                Feature("android.hardware.camera", required: false),
            }, strict: true);

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("Quality testing", errors[0]);
        }

        [Test]
        public void StrictMode_DoesNotFlagFeatureOutsideFinalSubmissionList()
        {
            // android.hardware.camera.front is unsupported but not in the stricter
            // MustBeAbsentForFinalSubmission subset (only the base camera feature is).
            var errors = PlayGamesOnPcPolicy.FindFeatureViolations(new[]
            {
                Feature("android.hardware.camera.front", required: false),
            }, strict: true);

            Assert.IsEmpty(errors);
        }

        [Test]
        public void ParseAaptBadging_ParsesRequiredAndNotRequiredFeatureLines()
        {
            const string aaptOutput =
                "package: name='com.example.app' versionCode='1'\n" +
                "uses-feature-not-required: name='android.hardware.camera'\n" +
                "uses-feature-not-required: name='android.hardware.microphone'\n" +
                "uses-feature: name='android.hardware.screen.portrait'\n" +
                "uses-implied-feature: name='android.hardware.screen.portrait' reason='...'\n" +
                "native-code: 'arm64-v8a'";

            var features = PlayGamesOnPcPolicy.ParseAaptBadging(aaptOutput);

            Assert.AreEqual(3, features.Count);
            Assert.IsFalse(features[0].Required);
            Assert.AreEqual("android.hardware.camera", features[0].Name);
            Assert.IsFalse(features[1].Required);
            Assert.IsTrue(features[2].Required);
            Assert.AreEqual("android.hardware.screen.portrait", features[2].Name);
        }

        [Test]
        public void FindAaptBadgingViolations_OnCleanPlinkoV1BaselineOutput_IsEmpty()
        {
            // Regression fixture: the real `aapt dump badging` feature-group lines captured
            // from Plinko_v1 baseline.apk this session (2026-09-21) — confirmed PC-compatible.
            const string aaptOutput =
                "uses-gl-es: '0x30000'\n" +
                "uses-feature-not-required: name='android.hardware.camera'\n" +
                "uses-feature-not-required: name='android.hardware.camera.front'\n" +
                "uses-feature-not-required: name='android.hardware.microphone'\n" +
                "uses-feature-not-required: name='android.hardware.sensor.accelerometer'\n" +
                "uses-feature-not-required: name='android.hardware.touchscreen'\n" +
                "uses-feature-not-required: name='android.hardware.touchscreen.multitouch'\n" +
                "uses-feature-not-required: name='android.hardware.touchscreen.multitouch.distinct'\n" +
                "uses-feature-not-required: name='android.hardware.vulkan.version'\n" +
                "uses-feature: name='android.hardware.screen.portrait'\n" +
                "uses-implied-feature: name='android.hardware.screen.portrait' reason='...'";

            var errors = PlayGamesOnPcPolicy.FindAaptBadgingViolations(aaptOutput);

            Assert.IsEmpty(errors);
        }

        [Test]
        public void AndroidManifest_HasNoUnsupportedRequiredFeatures()
        {
            var error = PlayGamesOnPcPolicy.ValidateSourceManifest();
            Assert.IsNull(error, error);
        }
    }
}
