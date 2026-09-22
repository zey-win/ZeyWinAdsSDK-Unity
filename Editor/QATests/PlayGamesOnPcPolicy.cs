using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace ZeyWinAds.Editor.QATests
{
    // Google Play Games on PC compatibility rules, from "Unsupported Android features and
    // permissions" (developer.android.com/games/pgs/pc) — pasted into the workspace 2026-09-21.
    //
    // Two very different mechanisms are involved, and this class deliberately keeps them
    // separate rather than treating both as "manifest problems":
    //
    //  - Hardware FEATURES (<uses-feature>) are a real manifest concern: "Requests for any
    //    missing features on a PC automatically fail." A required (or default-required, i.e.
    //    missing android:required) declaration for anything on UnsupportedFeatures makes the
    //    app un-installable on PC. This is checked here, both against synthetic data (pure,
    //    unit-testable — see PlayGamesOnPcPolicyTests) and against a real built APK via
    //    ParseAaptBadging + PlayGamesOnPcBuildGuard.
    //
    //  - Runtime PERMISSIONS are explicitly NOT a manifest concern, per Google's own doc:
    //    "Don't request unsupported Android permissions at runtime ... You don't need to
    //    update your manifest." UnsupportedRuntimePermissions exists here as shared reference
    //    data (and to point anyone extending AndroidRuntimePermissions.cs at the same
    //    authoritative list DeviceInfo.IsGooglePlayGamesOnPC() must gate), not because a
    //    manifest scan of uses-permission entries would mean anything — it wouldn't.
    public static class PlayGamesOnPcPolicy
    {
        // Hardware features Play Games on PC does not support. A required declaration (or a
        // missing android:required, which defaults to true) for any of these blocks PC install
        // entirely. Superset of both the "Functional testing" and "Quality testing" feature
        // lists from Google's doc — required=false clears this check either way.
        public static readonly HashSet<string> UnsupportedFeatures = new HashSet<string>(StringComparer.Ordinal)
        {
            "android.hardware.wifi",
            "android.hardware.bluetooth",
            "android.hardware.camera",
            "android.hardware.camera.any",
            "android.hardware.camera.front",
            "android.hardware.camera.autofocus",
            "android.hardware.location",
            "android.hardware.location.gps",
            "android.hardware.location.network",
            "android.hardware.audio.pro",
            "android.hardware.consumerir",
            "android.hardware.microphone",
            "android.hardware.nfc",
            "android.hardware.sensor.light",
            "android.hardware.sensor.accelerometer",
            "android.hardware.sensor.barometer",
            "android.hardware.sensor.compass",
            "android.hardware.sensor.gyroscope",
            "android.hardware.sensor.proximity",
            "android.hardware.telephony",
            "android.hardware.touchscreen",
            "android.hardware.touchscreen.multitouch",
            "android.hardware.touchscreen.multitouch.distinct",
            "android.hardware.usb.accessory",
            "android.hardware.usb.host",
            "android.software.midi",
        };

        // Subset Google's "Quality testing requirements" says must be removed ENTIRELY (not
        // just required=false) before final Play Console submission. Not enforced as a hard
        // build failure by default (strict: false) — camera/microphone/touchscreen/accelerometer
        // are legitimately declared not-required today for mobile WebView/gameplay features, and
        // flipping this to a hard removal is a separate, bigger decision than a build guard
        // should make silently. Surfaced as a warning instead; see PlayGamesOnPcBuildGuard.
        public static readonly HashSet<string> MustBeAbsentForFinalSubmission = new HashSet<string>(StringComparer.Ordinal)
        {
            "android.hardware.audio.pro",
            "android.hardware.bluetooth",
            "android.hardware.camera",
            "android.hardware.consumerir",
            "android.hardware.location",
            "android.hardware.nfc",
            "android.hardware.sensor.light",
            "android.hardware.sensor.accelerometer",
            "android.hardware.sensor.barometer",
            "android.hardware.sensor.compass",
            "android.hardware.sensor.gyroscope",
            "android.hardware.sensor.proximity",
            "android.hardware.telephony",
            "android.hardware.touchscreen",
            "android.hardware.usb.accessory",
            "android.hardware.usb.host",
            "android.hardware.wifi",
            "android.software.midi",
        };

        // Runtime permissions unsupported on Play Games on PC. NOT a manifest-scan target —
        // see the class comment above. Kept here so AndroidRuntimePermissions.cs (or any future
        // runtime-permission call site) and its tests reference one shared authoritative list
        // instead of re-typing permission strings ad hoc.
        public static readonly HashSet<string> UnsupportedRuntimePermissions = new HashSet<string>(StringComparer.Ordinal)
        {
            "android.permission.ACCESS_COARSE_LOCATION",
            "android.permission.ACCESS_FINE_LOCATION",
            "android.permission.ACCESS_WIFI_STATE",
            "android.permission.BLUETOOTH",
            "android.permission.CAMERA",
            "android.permission.FOREGROUND_SERVICE",
            "android.permission.GET_ACCOUNTS",
            "android.permission.INSTALL_PACKAGES",
            "android.permission.READ_CONTACTS",
            "android.permission.READ_EXTERNAL_STORAGE",
            "android.permission.READ_PHONE_STATE",
            "android.permission.RECEIVE_BOOT_COMPLETED",
            "android.permission.REQUEST_INSTALL_PACKAGES",
            "android.permission.SYSTEM_ALERT_WINDOW",
            "android.permission.USE_CREDENTIALS",
            "android.permission.WRITE_EXTERNAL_STORAGE",
            "android.permission.WRITE_SETTINGS",
            "com.google.android.gms.permission.ACTIVITY_RECOGNITION",
        };

        public struct DeclaredFeature
        {
            public string Name;
            public bool Required;
        }

        // Pure, synchronous, no I/O — safe for an EditMode unit test to call directly with
        // synthetic data. `strict` additionally flags MustBeAbsentForFinalSubmission entries
        // that are present-but-not-required; default false keeps the hard-fail path limited to
        // what actually blocks PC install today.
        public static List<string> FindFeatureViolations(IEnumerable<DeclaredFeature> features, bool strict = false)
        {
            var errors = new List<string>();
            if (features == null)
                return errors;

            foreach (var feature in features)
            {
                if (string.IsNullOrEmpty(feature.Name) || !UnsupportedFeatures.Contains(feature.Name))
                    continue;

                if (feature.Required)
                {
                    errors.Add($"<uses-feature name=\"{feature.Name}\"> is required (or missing " +
                        "android:required) — Play Games on PC does not support this hardware and " +
                        "requests for missing features automatically fail install on PC. Add " +
                        "android:required=\"false\".");
                }
                else if (strict && MustBeAbsentForFinalSubmission.Contains(feature.Name))
                {
                    errors.Add($"<uses-feature name=\"{feature.Name}\"> is declared (as not-required) — " +
                        "Google's Quality testing requirements for Play Games on PC require this " +
                        "feature to be removed entirely before final submission, not just marked optional.");
                }
            }

            return errors;
        }

        // Parses `aapt dump badging <apk>` stdout into the DeclaredFeature list
        // FindFeatureViolations expects. aapt emits one of:
        //   uses-feature: name='android.hardware.screen.portrait'
        //   uses-feature-not-required: name='android.hardware.camera'
        // (a bare "uses-feature:" line is required=true; there is no separate required='true' text)
        private static readonly Regex FeatureLineRegex =
            new Regex(@"^uses-feature(-not-required)?:\s*name='([^']+)'", RegexOptions.Compiled);

        public static List<DeclaredFeature> ParseAaptBadging(string aaptOutput)
        {
            var features = new List<DeclaredFeature>();
            if (string.IsNullOrEmpty(aaptOutput))
                return features;

            foreach (string rawLine in aaptOutput.Split('\n'))
            {
                string line = rawLine.Trim();
                var match = FeatureLineRegex.Match(line);
                if (!match.Success)
                    continue;

                bool notRequired = match.Groups[1].Success;
                features.Add(new DeclaredFeature { Name = match.Groups[2].Value, Required = !notRequired });
            }

            return features;
        }

        // Convenience: violations directly from a real `aapt dump badging` output string.
        public static List<string> FindAaptBadgingViolations(string aaptOutput, bool strict = false)
        {
            return FindFeatureViolations(ParseAaptBadging(aaptOutput), strict);
        }

        private const string AndroidManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";

        // Scans the CURRENT source AndroidManifest.xml sitting in the project (Assets/Plugins/Android)
        // — as opposed to FindAaptBadgingViolations, which scans the FINAL built APK/AAB after a
        // real Android build. The two deliberately check different points in time:
        //
        //  - This method: static, no build required — runs instantly from the Test Runner ("Run
        //    QA Checks" / EditMode tests) and from QaTestsPreProcessor's pre-build gate (before
        //    Gradle even starts), so an obviously-wrong manifest entry (e.g. a hand-added
        //    android:required="true" on an unsupported feature) is visible immediately, not only
        //    after a full Android build finishes.
        //  - FindAaptBadgingViolations (via PlayGamesOnPcBuildGuard): authoritative, catches
        //    everything including features a third-party .aar contributes during Gradle's
        //    manifest merge — but only runs when an actual Android Player build is performed.
        //
        // One caveat this check can't remove: AdMobBuildPostprocessor.EnsureAndroidManifestSecurityQueries()
        // force-corrects camera/microphone to required="false" on every build regardless of what's
        // in the source file right now, so a fresh checkout with those two missing/required would
        // still build fine — this check flags it anyway, since "what does the file on disk say
        // right now" is the useful signal here, not "what will the build hook silently fix."
        // Everything else in UnsupportedFeatures (bluetooth, wifi, location, sensors, etc.) is NOT
        // auto-corrected by anything, so a flag from this check on any of those is real and won't
        // be quietly fixed at build time.
        //
        // Returns null when compliant (or when there's no manifest yet — nothing to flag),
        // otherwise a human-readable, joined error message.
        public static string ValidateSourceManifest()
        {
            string fullPath = Path.GetFullPath(AndroidManifestPath);
            if (!File.Exists(fullPath))
                return null;

            var doc = new XmlDocument();
            doc.Load(fullPath);

            var declared = new List<DeclaredFeature>();
            var featureNodes = doc.DocumentElement?.SelectNodes("uses-feature");
            if (featureNodes != null)
            {
                foreach (XmlNode node in featureNodes)
                {
                    if (!(node is XmlElement element))
                        continue;

                    string name = element.Attributes?.GetNamedItem("name", AndroidNs)?.Value;
                    if (string.IsNullOrEmpty(name))
                        continue;

                    string requiredAttr = element.GetAttribute("required", AndroidNs);
                    bool required = string.IsNullOrEmpty(requiredAttr) || requiredAttr == "true";
                    declared.Add(new DeclaredFeature { Name = name, Required = required });
                }
            }

            var violations = FindFeatureViolations(declared);
            if (violations.Count == 0)
                return null;

            return $"'{AndroidManifestPath}' has {violations.Count} Play Games on PC compatibility " +
                "issue(s): " + string.Join(" | ", violations);
        }
    }
}
