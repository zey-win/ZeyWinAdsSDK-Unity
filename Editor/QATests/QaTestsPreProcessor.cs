using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace ZeyWinAds.Editor.QATests
{
    // Fails any real Android build (local or CI) if a pre-build QA check fails. Pulls its check
    // list from QaChecksRunner, the same registry the "Run QA Checks" inspector button uses, so
    // there's exactly one place that lists which checks exist.
    //
    // Checks that need a built artifact (e.g. app size) or an emulator (e.g. WebView behavior)
    // do NOT belong here — those stay as their own CI steps, since a build preprocessor runs
    // before any artifact exists and has no device to run against.
    public class QaTestsPreProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
            {
                return;
            }

            List<string> errors = QaChecksRunner.RunAll()
                .Where(result => !result.Passed)
                .Select(result => result.Error)
                .ToList();

            if (errors.Count > 0)
            {
                throw new BuildFailedException(
                    "QA checks failed:\n- " + string.Join("\n- ", errors));
            }
        }
    }
}
