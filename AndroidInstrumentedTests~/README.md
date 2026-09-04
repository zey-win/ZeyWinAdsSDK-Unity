# AndroidInstrumentedTests~ — on-device WebView test lane

A standalone Gradle/AGP project that compiles a small slice of the SDK's Android
sources and runs **Espresso / Espresso-Intents** tests on a connected device
against the **real offer-WebView wiring**.

It exists for the checklist rows that cannot be verified from the Unity Editor
Test Runner because a real run pops a system surface (file picker, camera,
external browser). Espresso-Intents replaces that surface with a stubbed result.

> **The trailing `~` is deliberate.** This package is embedded (`file:`) into
> `ci-factory-test` and the game projects. Unity's asset pipeline ignores any
> folder whose name ends in `~` (same as this repo's `Samples~`), so the test
> `.java` and the `com.unity3d.player.UnityPlayer` stub never get imported as
> Android plugins — which would collide with the real Unity player in every
> game build. **Do not remove the `~`.** Git and Gradle don't care about it.

## What's here

```
AndroidInstrumentedTests~/
├── settings.gradle / build.gradle / gradle.properties
├── gradlew / gradlew.bat / gradle/wrapper/        # wrapper JAR IS committed (see TLS note)
└── webview-harness/
    ├── build.gradle                               # com.android.library + vendored SDK sources
    └── src/
        ├── main/
        │   ├── AndroidManifest.xml                # INTERNET
        │   └── java/com/unity3d/player/UnityPlayer.java   # test-only stub (2 members)
        └── androidTest/
            ├── AndroidManifest.xml                # declares WebViewHarnessActivity
            └── java/com/zeywinads/instrumentedtests/
                ├── WebViewHarnessActivity.java    # one WebView wired like WebViewLock.ShowAndroidWebView
                ├── WebViewChecklist.java          # drives window.ZW_CHECKLIST via evaluateJavascript
                ├── ChecklistMetaDiagnosticTest.java   # run FIRST: prints check ids/buckets to logcat
                └── FileInputWebViewTest.java      # file-input-generic + file-input-capture
run-tests.sh                                       # runs the tests, reports the REAL verdict (see UTP note)
```

### Vendored SDK sources

`webview-harness/build.gradle` has a `vendorSdkSources` `Copy` task that syncs
exactly five real files from `../Runtime/Plugins/Android` into the build dir every
build:

* `ZeyWinAdsWebChromeClient.java` — `onShowFileChooser`
* `ZeyWinAdsFileChooserFragment.java` — launches the chooser Intent
* `ZeyWinAdsWebViewNavigation.java`
* `ZeyWinAdsLockWebViewClient.java`
* `ZeyWinAdsPermissionBridge.java`

Nothing is copied into source control; the harness cannot drift from production.
The only non-framework symbol these files touch is
`com.unity3d.player.UnityPlayer` (`currentActivity`, `UnitySendMessage`) — the
stub under `src/main/java` supplies it.

## Versions

AGP **8.7.3**, Gradle **8.11.1** (wrapper), `compileSdk 35`, `buildTools 36.0.0`,
JDK **17**, `minSdk 24`. Verified building `assembleDebugAndroidTest` and running
`connectedDebugAndroidTest` on a Samsung SM-A146P (Android 13).

## One-time setup

1. **JDK 17.** The Unity 6000.3.x Android tooling bundles one:
   `…/6000.3.20f1/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK`. Point
   `JAVA_HOME` at it.
2. **Android SDK** — set `ANDROID_SDK_ROOT`, or put a `local.properties` next to
   `settings.gradle` (use **forward slashes** — backslashes are escape chars in a
   `.properties` file and will break `sdk.dir`):
   ```
   sdk.dir=C:/Program Files/Unity/Hub/Editor/6000.3.20f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK
   ```
3. A **device/emulator** connected and authorised (`adb devices`).
4. **TLS-intercepting antivirus (Avast/AVG/Kaspersky/corp proxy).** The Gradle
   wrapper and dependency downloads use Java's own truststore, which does *not*
   include the AV's MITM root, so the first `./gradlew` run fails with
   `PKIX path building failed`. Fix once — import the AV root into a JDK
   truststore copy and point Gradle at it:
   ```bash
   JDK="…/AndroidPlayer/OpenJDK"
   cp "$JDK/lib/security/cacerts" /tmp/cacerts-avast
   "$JDK/bin/keytool" -importcert -noprompt -trustcacerts -alias av-mitm \
     -file "C:/ProgramData/Avast Software/Avast/wscert.pem" \
     -keystore /tmp/cacerts-avast -storepass changeit
   export GRADLE_OPTS="-Djavax.net.ssl.trustStore=/tmp/cacerts-avast -Djavax.net.ssl.trustStorePassword=changeit"
   ```
   (A machine with no HTTPS interception needs none of step 4.)

## Running

```bash
# from AndroidInstrumentedTests~/
./run-tests.sh                     # all classes; exits on the REAL verdict
./run-tests.sh FileInputWebViewTest
```

`run-tests.sh` calls the gradle task then reads the UTP result proto for the true
pass/fail, because the task's own exit code is unreliable here (see UTP note
below). HTML report:
`webview-harness/build/reports/androidTests/connected/debug/index.html`
Raw device output:
`webview-harness/build/outputs/androidTest-results/connected/debug/<device>/test-result.textproto`

### First — confirm the checklist contract

```bash
adb logcat -c
./gradlew :webview-harness:connectedDebugAndroidTest \
  -Pandroid.testInstrumentationRunnerArguments.class=com.zeywinads.instrumentedtests.ChecklistMetaDiagnosticTest
adb logcat -d -s ZWChecklistDiag
```

Prints `ZW_CHECKLIST.version`, `pageRevision`, and the full `meta` map. Current
deployed page: **version 3**, `pageRevision "dev"` (not yet a real stamp). If the
file-row ids ever change from `file-input-generic` / `file-input-capture`, update
`FileInputWebViewTest`.

## What the file tests assert

Both rows are in the page's `external` bucket. The page's own
`ZW_CHECKLIST.run("file-input-*")` is a **weak signal** — it just calls
`<input>.click()` and reports `pass` if that didn't throw; it never waits for a
file, and modern Android WebView ignores a *scripted* click for
`onShowFileChooser` (no user activation). So each test instead:

1. pins the target `<input>` to a known rect (injected CSS),
2. lands a **real MotionEvent tap** on it (`GeneralClickAction`) — this *does*
   grant user activation,
3. asserts the tap reached the SDK (`WebViewHarnessActivity.fileChooserInvocations`
   bumps — a wrapper over the real `ZeyWinAdsWebChromeClient.onShowFileChooser`),
4. asserts the SDK launched a content-pick `Intent`
   (`intended(ACTION_CHOOSER / GET_CONTENT / OPEN_DOCUMENT / IMAGE_CAPTURE)`),
5. answers that Intent via Espresso-Intents with an 8×8 JPEG staged in the app
   cache and returned as a `file://` Uri,
6. asserts the SDK forwarded it (`filePathCallback.onReceiveValue`) so the page's
   real `onChange` handler flips the row to `pass` with detail
   `"<name> · <type> · <size> bytes"`.

| Test | Row (id) |
|---|---|
| `anyFileUpload_deliversPickedFileToInput` | "Any file upload" (`file-input-generic`) |
| `photoCameraCapture_deliversCapturedImage` | "Photo / camera capture (file input)" (`file-input-capture`) |

Verified passing on a Samsung SM-A146P / Android 13
(`detail=probe-….jpg · image/jpeg · 839 bytes`).

## Known quirks

* **UTP result collection / task exit code.** With the read-only Unity-bundled
  SDK on Windows, AGP logs `Exception while marshalling … package.xml. Probably
  the SDK is read-only` and then `Failed to receive the UTP test results`, and the
  `connectedDebugAndroidTest` task exits non-zero **even when every test passed**.
  The `test-result.textproto` still holds the true verdict — `run-tests.sh` reads
  it and exits accordingly. A CI runner with a **writable standalone Android SDK**
  (as `docs/self-hosted-runner-setup.md` prescribes) most likely avoids the
  marshalling error entirely; keep using `run-tests.sh` regardless.
* **A real gesture is required.** `ZW_CHECKLIST.run()` cannot open the chooser and
  neither can Espresso-Web `webClick()` (also synthetic). Only a platform
  `MotionEvent` (what these tests send) grants the activation
  `onShowFileChooser` needs.
* **`file://` Uri acceptance.** Works today (`setAllowFileAccess(true)`, like
  production). If a future WebView rejects it, switch `stageProbeImage` to a
  `FileProvider` `content://` Uri (add `androidx.core` + a provider to the
  androidTest manifest, `grantUriPermission` on the result).
* **Fragment result routing.** `ZeyWinAdsFileChooserFragment` uses framework
  fragments; `WebViewHarnessActivity` is a plain `Activity` precisely so
  `getFragmentManager()` matches. Do not change it to `FragmentActivity`.
* **Windows long paths.** AGP's `build/intermediates/desugar_graph/…` paths can
  exceed `MAX_PATH`; the build itself is fine but `./gradlew clean` may fail to
  delete them. Enable once: `git config --global core.longpaths true` and the
  Windows `LongPathsEnabled` registry key (the runner setup covers this).

## CI

Runs on the self-hosted runner as a **non-blocking** lane
(`connectedDebugAndroidTest`), separate from the Unity Editor PlayMode tests and
from the `repository_dispatch` factory build. Needs network to `ads.zeywin.com`
from the device.

## Relationship to SDK change #4

`WebViewHarnessActivity.configureLikeProduction` is a hand-maintained mirror of
`WebViewLock.ShowAndroidWebView`. Once SDK change #4 lands a shared
"build configured offer WebView" factory, replace that method body with a call to
it so there is a single source of truth.
