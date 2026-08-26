using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZeyWinAds.Editor.QATests
{
    // Adds a "Run QA Checks" button to the ZeyWinAdsSettings inspector so the same checks
    // QaTestsPreProcessor enforces at build time can be spot-checked on demand, without
    // triggering an actual build or opening the Test Runner window.
    [CustomEditor(typeof(ZeyWinAdsSettings))]
    public class ZeyWinAdsSettingsQaInspector : UnityEditor.Editor
    {
        private List<QaCheckResult> _lastResults;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("QA Checks", EditorStyles.boldLabel);

            if (GUILayout.Button("Run QA Checks"))
            {
                _lastResults = QaChecksRunner.RunAll();
            }

            if (_lastResults == null)
            {
                return;
            }

            var passed = _lastResults.Count(r => r.Passed);
            var failed = _lastResults.Count - passed;

            EditorGUILayout.HelpBox(
                $"{passed} passed, {failed} failed",
                failed == 0 ? MessageType.Info : MessageType.Error);

            foreach (var result in _lastResults)
            {
                var icon = result.Passed ? "✅" : "❌";
                var label = result.Passed ? result.Name : $"{result.Name}: {result.Error}";
                EditorGUILayout.LabelField($"{icon} {label}", EditorStyles.wordWrappedLabel);
            }
        }
    }
}
