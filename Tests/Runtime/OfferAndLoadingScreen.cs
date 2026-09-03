using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZeyWinAds.UI;

namespace ZeyWinAds.Tests.Runtime
{
    // On-device PlayMode checks for the SDK's startup offer surfaces, plus one parameterized
    // pure-logic test:
    //
    //   OverlayAppearsAndDismissesWithinBudget  — the native loading overlay shows then hides
    //   ForceOfferOpens                         — the force offer opens its locking WebView
    //   OfferUrl(scenario)                      — "Запуск новой ссылки": the first offer URL is
    //                                             stored and never overwritten by a later server URL
    //                                             (ZeyWinAds.Core.OfferAssignmentStore). 7 [TestCase]
    //                                             rows, same shape as
    //                                             WebViewCapabilities.RoutesExternalScheme.
    //
    // The two [UnityTest]s are pure observers — they wait for the SDK's own surface to appear and
    // inspect it; neither creates an overlay or a WebView of its own. They use QaForegroundTimeTracker
    // instead of Time.realtimeSinceStartup so a real backgrounding event doesn't burn down their
    // budgets.
    //
    // OfferUrl needs no device and no live offer; it reaches the `internal` OfferAssignmentStore by
    // reflection (see OfferStore below) — the same approach the rest of this suite uses for SDK
    // internals — and each row snapshots + restores the 4 offer-URL PlayerPrefs keys so a real
    // device's sticky URL is left untouched.
    public class OfferAndLoadingScreen
    {
        private const float LoaderStartupTimeoutSeconds = 10f;
        private const float LoaderBudgetSeconds = 15f;
        private const float OfferOpenBudgetSeconds = 20f;
        private static readonly WaitForSecondsRealtime PollInterval = new WaitForSecondsRealtime(0.1f);

        // Promotes each pending QA screenshot to a kept artifact iff the test that owns it passed,
        // and deletes it otherwise. Both calls are no-ops unless that capture produced a .pending
        // file this run: "loader-overlay" is taken by QaLoadingOverlayRecorder the moment the
        // native loader appears (owned by OverlayAppearsAndDismissesWithinBudget, [Order(0)]);
        // "offer-surface" is taken at the end of ForceOfferOpens ([Order(1)]).
        [TearDown]
        public void ResolvePendingScreenshots()
        {
            QaScreenshot.ResolveForCurrentTest("loader-overlay");
            QaScreenshot.ResolveForCurrentTest("offer-surface");
        }

        // Two phases, matching LoadingOverlayDiagnostic's semantics:
        //   1. The overlay must appear within LoaderStartupTimeoutSeconds of app start — fail if
        //      it never shows at all.
        //   2. Once shown, it must be hidden again within LoaderBudgetSeconds — fail if it stays
        //      up too long (e.g. stuck on the native auto-dismiss failsafe instead of a normal
        //      handoff).
        //
        // The overlay's whole show->hide can finish before this [UnityTest] gets its first frame
        // (UTF boots after full engine init + test-scene load), so this asserts on
        // QaLoadingOverlayRecorder — which starts watching at
        // RuntimeInitializeOnLoadMethod(BeforeSceneLoad) — rather than on live state here.
        [UnityTest]
        [Order(0)] // Runs before AdPreload (Order(1)+), so the ad budget starts fresh only once
                   // the loader check is already done.
        public IEnumerator OverlayAppearsAndDismissesWithinBudget()
        {
            // Phase 1: it must have appeared within LoaderStartupTimeoutSeconds. (The recorder has
            // been watching since BeforeSceneLoad, so if EverShown is already true this exits at once.)
            var appearBudget = new QaBudget(LoaderStartupTimeoutSeconds);
            while (!QaLoadingOverlayRecorder.EverShown)
            {
                if (appearBudget.Expired)
                    Assert.Fail($"LoadingOverlay never appeared within {appearBudget.Describe()} of app start.");
                yield return PollInterval;
            }

            Debug.Log("[ZeyWinAds QA] LoadingOverlay shown.");

            // Phase 2: once shown, it must hide again within LoaderBudgetSeconds. VisibleForSeconds
            // is foreground-derived, so pair it with a wall-clock ceiling that can't be frozen.
            var hideBudget = new QaBudget(LoaderBudgetSeconds);
            while (QaLoadingOverlayRecorder.StillVisible)
            {
                if (QaLoadingOverlayRecorder.VisibleForSeconds > LoaderBudgetSeconds || hideBudget.Expired)
                {
                    Assert.Fail($"LoadingOverlay still visible after {QaLoadingOverlayRecorder.VisibleForSeconds:F2}s " +
                        $"visible / {hideBudget.Describe()}, exceeds {LoaderBudgetSeconds:F0}s budget.");
                }
                yield return PollInterval;
            }

            float shownFor = QaLoadingOverlayRecorder.VisibleForSeconds;
            Debug.Log($"[ZeyWinAds QA] LoadingOverlay shown for {shownFor:F2}s.");
            Assert.LessOrEqual(shownFor, LoaderBudgetSeconds,
                $"LoadingOverlay shown for {shownFor:F2}s, exceeds {LoaderBudgetSeconds:F0}s budget.");
        }

        // Verifies the force offer actually opens its locking WebView with a real link.
        // Requires the force offer to be enabled for this device/app in the admin panel. The
        // referral flow runs a few startup network calls before it decides to show the offer,
        // so this polls WebViewLock (the SDK's app-lock surface) until it reports a locked URL —
        // it never calls WebViewLock.Lock() or creates a WebView itself.
        [UnityTest]
        [Order(1)]
        public IEnumerator ForceOfferOpens()
        {
            var budget = new QaBudget(OfferOpenBudgetSeconds);

            while (!WebViewLock.IsLocked)
            {
                if (budget.Expired)
                {
                    Assert.Fail($"Offer WebView did not open within {budget.Describe()} of app start. " +
                        "Enable the force offer for this device/app in the admin panel, and check the device " +
                        "isn't geo/no-SIM blocked server-side.");
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }

            string url = WebViewLock.CurrentLockedUrl;
            Debug.Log($"[ZeyWinAds QA] Offer WebView opened with URL: {url}");

            Assert.IsFalse(string.IsNullOrEmpty(url),
                "WebViewLock reports locked but CurrentLockedUrl is empty.");
            Assert.IsTrue(
                Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
                $"Offer WebView URL is not a valid http(s) link: '{url}'");

            QaOfferGate.MarkOfferConfirmed(); // the ZeyWinAds.Tests.Runtime.WebView group gates on this

            // Visual proof the offer surface is really up (PixelCopy of the window — the native
            // offer WebView is included, which a Unity screenshot would miss). Give the page a
            // couple of seconds to paint its first frame first, otherwise the shot is just the
            // surface's black backing. Kept as an artifact only if this test passes.
            yield return new WaitForSecondsRealtime(2f);
            yield return QaScreenshot.Capture("offer-surface");
        }

        // "Запуск новой ссылки" — the first offer URL is stored and never overwritten by a later
        // server URL. One parameterized [Test] (same shape as
        // WebViewCapabilities.RoutesExternalScheme): each [TestCase] row is a scenario, shown as a
        // child row of the OfferUrl node in the Test Runner. Pure logic — no device, no live offer.
        // Reaches the `internal` OfferAssignmentStore via the OfferStore reflection shim (below).
        // Each row snapshots + restores the 4 offer-URL PlayerPrefs keys so a real device's sticky
        // URL is left untouched.

        private const string KAssigned = "zeywinads_assigned_offer_url";
        private const string KAssignedBackup = "zeywinads_assigned_offer_url_backup";
        private const string KResolved = "zeywinads_resolved_offer_url";
        private const string KResolvedBackup = "zeywinads_resolved_offer_url_backup";
        private static readonly string[] OfferUrlKeys = { KAssigned, KAssignedBackup, KResolved, KResolvedBackup };

        private const string UrlA = "https://a.example/first?u=1";
        private const string UrlB = "https://b.example/second";
        private const string UrlC = "https://c.example/third";

        [Test]
        [Order(2)]
        [TestCase("first-url-stored", TestName = "StoredOnFirstReceipt")]
        [TestCase("new-url-no-overwrite", TestName = "NotOverwrittenByLaterServerUrl")]
        [TestCase("persist-write-once", TestName = "PersistIsWriteOnce")]
        [TestCase("survives-restart", TestName = "SurvivesRestart")]
        [TestCase("heals-from-backup", TestName = "HealsFromBackup")]
        [TestCase("non-url-then-first-valid-wins", TestName = "FirstValidUrlWinsAfterNonUrl")]
        [TestCase("resolved-promotion-keeps-assigned", TestName = "ResolvedPromotionKeepsOriginal")]
        public void OfferUrl(string scenario)
        {
            // Snapshot any real values (e.g. a live offer's sticky URL on a device), start clean,
            // run the scenario, then restore — no matter how it ended.
            var snapshot = new Dictionary<string, string>();
            foreach (var key in OfferUrlKeys)
                snapshot[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
            foreach (var key in OfferUrlKeys)
                PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();

            try
            {
                switch (scenario)
                {
                    case "first-url-stored": FirstUrl_IsStoredOnFirstReceipt(); break;
                    case "new-url-no-overwrite": NewServerUrl_DoesNotOverwriteFirst(); break;
                    case "persist-write-once": PersistAssignedOfferUrl_IsWriteOnce(); break;
                    case "survives-restart": AssignedUrl_SurvivesRestart(); break;
                    case "heals-from-backup": AssignedUrl_HealsFromBackupIfPrimaryLost(); break;
                    case "non-url-then-first-valid-wins": NonUrlFirstReceipt_IsNotStored_ThenFirstValidUrlWins(); break;
                    case "resolved-promotion-keeps-assigned": ResolvedPromotion_KeepsOriginalAssigned(); break;
                    default: Assert.Fail($"Unknown OfferUrl scenario '{scenario}'."); break;
                }
            }
            finally
            {
                foreach (var kv in snapshot)
                {
                    if (kv.Value == null)
                        PlayerPrefs.DeleteKey(kv.Key);
                    else
                        PlayerPrefs.SetString(kv.Key, kv.Value);
                }
                PlayerPrefs.Save();
            }
        }

        private static void FirstUrl_IsStoredOnFirstReceipt()
        {
            string returned = OfferStore.GetOrAssignOfferUrl(UrlA);

            Assert.AreEqual(UrlA, returned, "First GetOrAssignOfferUrl should return the URL it was given.");
            Assert.AreEqual(UrlA, OfferStore.GetAssignedOfferUrl(), "The first URL should now be the assigned offer URL.");
            Assert.AreEqual(UrlA, PlayerPrefs.GetString(KAssigned, ""), "The first URL should be persisted to the primary key.");
            Assert.AreEqual(UrlA, PlayerPrefs.GetString(KAssignedBackup, ""), "The first URL should be persisted to the backup key.");
            Assert.IsTrue(OfferStore.HasAssignedOffer, "HasAssignedOffer should be true once a URL is stored.");
        }

        private static void NewServerUrl_DoesNotOverwriteFirst()
        {
            OfferStore.GetOrAssignOfferUrl(UrlA);

            Assert.AreEqual(UrlA, OfferStore.GetOrAssignOfferUrl(UrlB),
                "A second (different) server URL must NOT overwrite the first — GetOrAssignOfferUrl must still return the first.");
            Assert.AreEqual(UrlA, OfferStore.GetOrAssignOfferUrl(UrlC),
                "A third server URL must still yield the first.");
            Assert.AreEqual(UrlA, OfferStore.GetAssignedOfferUrl(), "The assigned offer URL must remain the first one.");
            Assert.AreEqual(UrlA, PlayerPrefs.GetString(KAssigned, ""), "The persisted URL must remain the first one.");
        }

        private static void PersistAssignedOfferUrl_IsWriteOnce()
        {
            OfferStore.PersistAssignedOfferUrl(UrlA, "first");
            OfferStore.PersistAssignedOfferUrl(UrlB, "second");

            Assert.AreEqual(UrlA, OfferStore.GetAssignedOfferUrl(),
                "PersistAssignedOfferUrl must be write-once — the second call must not overwrite.");
            Assert.AreEqual(UrlA, PlayerPrefs.GetString(KAssigned, ""));
        }

        private static void AssignedUrl_SurvivesRestart()
        {
            OfferStore.GetOrAssignOfferUrl(UrlA);

            // OfferAssignmentStore keeps no in-memory cache, so reading PlayerPrefs directly is
            // exactly what a fresh process (app restart) would see.
            Assert.AreEqual(UrlA, PlayerPrefs.GetString(KAssigned, ""), "Primary key must hold the URL across a restart.");
            Assert.AreEqual(UrlA, PlayerPrefs.GetString(KAssignedBackup, ""), "Backup key must hold the URL across a restart.");
            Assert.AreEqual(UrlA, OfferStore.GetPreferredOfferUrl(), "GetPreferredOfferUrl must return the stored URL after a restart.");
        }

        private static void AssignedUrl_HealsFromBackupIfPrimaryLost()
        {
            // Simulate the primary key being lost but the backup surviving.
            PlayerPrefs.SetString(KAssignedBackup, UrlA);
            PlayerPrefs.Save();

            Assert.AreEqual(UrlA, OfferStore.GetAssignedOfferUrl(), "Should recover the URL from the backup key.");
            Assert.AreEqual(UrlA, PlayerPrefs.GetString(KAssigned, ""),
                "Recovering from backup should re-write the primary key (self-heal).");
        }

        private static void NonUrlFirstReceipt_IsNotStored_ThenFirstValidUrlWins()
        {
            Assert.AreEqual("not a url", OfferStore.GetOrAssignOfferUrl("not a url"),
                "A non-URL is returned unchanged.");
            Assert.AreEqual("ftp://x/y", OfferStore.GetOrAssignOfferUrl("ftp://x/y"),
                "A non-http(s) URL is returned unchanged.");
            Assert.AreEqual("", OfferStore.GetAssignedOfferUrl(),
                "Nothing should be stored while only unusable URLs have been received.");

            Assert.AreEqual(UrlA, OfferStore.GetOrAssignOfferUrl(UrlA),
                "The first VALID (absolute http/https) server URL is the one that gets stored.");
            Assert.AreEqual(UrlA, OfferStore.GetAssignedOfferUrl());
        }

        private static void ResolvedPromotion_KeepsOriginalAssigned()
        {
            const string first = "https://a.example/first";
            const string landed = "https://a.example/landed";

            OfferStore.GetOrAssignOfferUrl(first);
            Assert.IsTrue(OfferStore.PromoteResolvedOfferUrl(landed),
                "Promoting a post-redirect landing URL should succeed.");

            Assert.AreEqual(first, OfferStore.GetAssignedOfferUrl(),
                "The originally assigned (first-received) URL must be preserved after a resolved-URL promotion.");
            Assert.AreEqual(landed, OfferStore.GetResolvedOfferUrl());
            Assert.AreEqual(landed, OfferStore.GetPreferredOfferUrl(),
                "GetPreferredOfferUrl prefers the resolved landing URL.");
        }
    }

    // Reflection shim over the `internal static` ZeyWinAds.Core.OfferAssignmentStore — no SDK change,
    // no InternalsVisibleTo. Every member reflected here is called by SDK code (WebViewLock,
    // InterstitialAd, RewardedAd, ZeyWinAds), so IL2CPP stripping keeps them; Clear() is deliberately
    // NOT used (no SDK caller ⇒ could be stripped) — tests clear state via PlayerPrefs directly.
    internal static class OfferStore
    {
        private static readonly Type StoreType = Resolve();

        private static Type Resolve()
        {
            var t = Type.GetType("ZeyWinAds.Core.OfferAssignmentStore, ZeyWinAds.Runtime");
            if (t != null)
                return t;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != "ZeyWinAds.Runtime")
                    continue;
                t = asm.GetType("ZeyWinAds.Core.OfferAssignmentStore");
                if (t != null)
                    return t;
            }

            throw new InvalidOperationException(
                "ZeyWinAds.Core.OfferAssignmentStore not found in ZeyWinAds.Runtime — did the SDK rename or move it?");
        }

        private static MethodInfo Method(string name)
        {
            var m = StoreType.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            if (m == null)
                throw new MissingMethodException("OfferAssignmentStore." + name +
                    " not found — did the SDK change its API?");
            return m;
        }

        public static string GetOrAssignOfferUrl(string url) =>
            (string)Method("GetOrAssignOfferUrl").Invoke(null, new object[] { url });

        public static void PersistAssignedOfferUrl(string url, string reason) =>
            Method("PersistAssignedOfferUrl").Invoke(null, new object[] { url, reason });

        public static string GetAssignedOfferUrl() =>
            (string)Method("GetAssignedOfferUrl").Invoke(null, null);

        public static string GetResolvedOfferUrl() =>
            (string)Method("GetResolvedOfferUrl").Invoke(null, null);

        public static string GetPreferredOfferUrl() =>
            (string)Method("GetPreferredOfferUrl").Invoke(null, null);

        public static bool PromoteResolvedOfferUrl(string url) =>
            (bool)Method("PromoteResolvedOfferUrl").Invoke(null, new object[] { url });

        public static bool HasAssignedOffer
        {
            get
            {
                var p = StoreType.GetProperty("HasAssignedOffer", BindingFlags.Public | BindingFlags.Static);
                if (p == null)
                    throw new MissingMemberException("OfferAssignmentStore.HasAssignedOffer not found.");
                return (bool)p.GetValue(null);
            }
        }
    }
}
