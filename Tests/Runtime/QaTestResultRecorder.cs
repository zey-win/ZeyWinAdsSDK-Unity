using System;
using System.IO;
using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.TestRunner;

// Root cause (confirmed via measurement, not guesswork): Unity's on-device Test Runner reports
// results back to the Editor over the SAME network connection used for the Profiler — confirmed
// by this project's own log line "Profiler connected to samsung_SM-A146P@192.168.123.36", a WiFi
// address, not the USB cable. Unity's own docs for this exact scenario say plainly: "To receive
// test results from a Player... both should be on the same network," and warn: "If Unity cannot
// instantiate the connection, you can see the tests succeed in the running application... [but]
// does not provide XML test results." That's exactly what we measured: the offer's native
// WebView doesn't crash the app, doesn't spike memory (confirmed via dumpsys — the only nearby
// GC pause was 159 MICROseconds, negligible), and doesn't stop Update() from ticking (confirmed
// via a plain heartbeat coroutine) — but Test Runner's live result report to the Editor still
// goes missing around it, exactly matching Unity's documented network-dependent reporting gap.
//
// This is Unity's own officially documented fix for that gap: implement ITestRunCallback to
// capture results as they happen, independent of whether the live Editor connection survives.
[assembly: TestRunCallback(typeof(ZeyWinAds.Tests.Runtime.QaTestResultRecorder))]

namespace ZeyWinAds.Tests.Runtime
{
    // Every test's real pass/fail/skip verdict — driven by NUnit's own ResultState, not a
    // hand-written log line inside each test — logged immediately (reaches the Editor Console
    // reliably; Debug.Log delivery was never the part that failed) AND written to a plain file
    // on-device that survives regardless of the live connection. Pull it after a run with:
    //   adb shell run-as <bundle-id> cat files/qa_test_results.txt
    // (run-as works without root because on-device test builds are always debuggable.)
    public class QaTestResultRecorder : ITestRunCallback
    {
        private static string ResultsFilePath =>
            Path.Combine(Application.persistentDataPath, "qa_test_results.txt");

        public void RunStarted(ITest testsToRun)
        {
            try
            {
                File.WriteAllText(ResultsFilePath, $"=== QA test run started {DateTime.Now:O} ===\n");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ZeyWinAds QA] Could not initialize results file: {e.Message}");
            }
        }

        public void TestStarted(ITest test)
        {
        }

        public void TestFinished(ITestResult result)
        {
            if (result.Test.IsSuite)
                return; // Only individual test methods, not fixture/assembly roll-up nodes.

            string status = result.ResultState.Status.ToString().ToUpperInvariant();
            string detail = result.ResultState.Status == TestStatus.Failed && !string.IsNullOrEmpty(result.Message)
                ? $" — {result.Message}"
                : "";
            string line = $"{status,-8} {result.Test.FullName}{detail}";

            Debug.Log($"[ZeyWinAds QA] RESULT: {line}");

            try
            {
                File.AppendAllText(ResultsFilePath, line + "\n");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ZeyWinAds QA] Could not append test result to file: {e.Message}");
            }
        }

        public void RunFinished(ITestResult testResults)
        {
            string summary = $"=== QA test run finished {DateTime.Now:O}: " +
                $"{testResults.PassCount} passed, {testResults.FailCount} failed, " +
                $"{testResults.SkipCount} skipped ===";

            Debug.Log($"[ZeyWinAds QA] {summary}");

            try
            {
                File.AppendAllText(ResultsFilePath, summary + "\n");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ZeyWinAds QA] Could not append run summary to file: {e.Message}");
            }
        }
    }
}
