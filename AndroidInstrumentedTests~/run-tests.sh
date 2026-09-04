#!/usr/bin/env bash
# Runs the on-device WebView instrumented tests and reports the REAL verdict from
# the UTP result proto — AGP's connectedAndroidTest task exit code is unreliable
# on Windows with a read-only (Unity-bundled) Android SDK ("Failed to receive the
# UTP test results"). CI should call this, not the gradle task directly.
#
# Usage:
#   ./run-tests.sh                # all test classes
#   ./run-tests.sh FileInputWebViewTest
#
# Requires JAVA_HOME (JDK 17), ANDROID_SDK_ROOT, a connected authorised device,
# and — behind TLS-intercepting AV/proxy — GRADLE_OPTS with a truststore that
# trusts it (see README §setup step 4).
set -uo pipefail
cd "$(dirname "$0")"

CLASS_FILTER=""
if [ $# -ge 1 ]; then
    CLASS_FILTER="-Pandroid.testInstrumentationRunnerArguments.class=com.zeywinads.instrumentedtests.$1"
fi

./gradlew --no-daemon :webview-harness:connectedDebugAndroidTest $CLASS_FILTER
GRADLE_RC=$?

RESULT_DIR="webview-harness/build/outputs/androidTest-results/connected/debug"
PROTO=$(find "$RESULT_DIR" -name 'test-result.textproto' 2>/dev/null | head -1)

if [ -z "$PROTO" ]; then
    echo "run-tests: no test-result.textproto found — the run did not produce results." >&2
    exit "${GRADLE_RC:-1}"
fi

# Top-level test_status line is the suite verdict.
SUITE_STATUS=$(grep -m1 -aoE '^test_status: [A-Z]+' "$PROTO" | awk '{print $2}')
FAIL_COUNT=$(grep -acE '^  test_status: (FAILED|ERROR)' "$PROTO")
CASE_COUNT=$(grep -m1 -aoE 'scheduled_test_case_count: [0-9]+' "$PROTO" | awk '{print $2}')

echo "----------------------------------------------------------------"
echo "run-tests: suite=$SUITE_STATUS  cases=$CASE_COUNT  failed_cases=$FAIL_COUNT  (gradle rc=$GRADLE_RC)"
echo "report: file://$PWD/webview-harness/build/reports/androidTests/connected/debug/index.html"
echo "----------------------------------------------------------------"
grep -aoE 'error_message: "[^"]{0,300}' "$PROTO" | sed 's/^/  /' || true

if [ "$SUITE_STATUS" = "PASSED" ] && [ "$FAIL_COUNT" -eq 0 ]; then
    exit 0
fi
exit 1
