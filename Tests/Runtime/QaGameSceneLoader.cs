using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZeyWinAds.Tests.Runtime
{
    // Unity Test Framework always boots a PlayMode run into its own injected scene, regardless of
    // what's in the project's Build Profile scene list ("Play mode tests always start in an
    // entirely blank scene... regardless of what scene you have currently loaded"). A normal build
    // has its game scene loaded from the very start, so once the loader/offer decides there's
    // nothing to show, the game is just sitting there, already running, instantly visible. A test
    // build has no game scene loaded at all — so it must be loaded at the same point, at boot, not
    // after the fact once some later event decides "no offer." There's no single reliable "no
    // offer, reveal now" signal to hook (the offer/eligibility decision chain has several exit
    // paths — see AdAudioController/ZeyWinAds.cs's [BlackScreenQA] logging), so instead of chasing
    // that, load the game scene as early as possible so it's already there the moment nothing
    // else covers it — same effect a normal build gets for free.
    //
    // Detected by what's already LOADED, not by a fixed index or a hardcoded "try 0, then 1"
    // guess: a native on-device Player has to boot into a real, indexed scene, so Unity's injected
    // test scene most likely occupies a real build index (probably 0) rather than reporting -1 the
    // way an Editor-only synthetic scene would — meaning the project's own first scene could end
    // up shifted to index 1, or could still be 0 if Unity's scene isn't counted at all. Rather than
    // assume which, walk every index in the Build Profile scene list and load the first one that
    // ISN'T already loaded — that's correct either way, self-adjusts to whatever Unity actually
    // does on this platform/version, and needs no scene name. Works unmodified across every game
    // regardless of what its scenes are actually called.
    internal static class QaGameSceneLoader
    {
        // A device test player exists only to run this suite, so load the game's scene at the
        // earliest possible point — alongside SDK init, not after it — so the game is already
        // running underneath by the time any offer/loader decision resolves. AfterSceneLoad (not
        // BeforeSceneLoad): SceneManager needs Unity's own injected scene to exist first so the
        // "what's already loaded" check below has something to compare against.
#if !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallEarly() => LoadGameSceneIfNeeded();
#endif

        // Also called from QaLogGuard.RunFinished as a safety net — idempotent (checks what's
        // already loaded first), so this is a harmless no-op if the early load above already ran.
        internal static void LoadGameSceneIfNeeded()
        {
            int totalScenes = SceneManager.sceneCountInBuildSettings;
            if (totalScenes <= 0)
            {
                Debug.LogWarning("[ZeyWinAds QA] No scenes in the Build Profile scene list — nothing to load for the test player.");
                return;
            }

            for (int buildIndex = 0; buildIndex < totalScenes; buildIndex++)
            {
                if (IsSceneLoaded(buildIndex))
                    continue; // Unity's own injected test scene, or one already loaded (incl. by an earlier call to this method).

                Debug.Log($"[ZeyWinAds QA] Loading build index {buildIndex} additively so the real game runs underneath the test player.");
                SceneManager.LoadScene(buildIndex, LoadSceneMode.Additive);
                return; // Only the first not-yet-loaded scene — that's the game's real entry point.
            }
        }

        private static bool IsSceneLoaded(int buildIndex)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene loaded = SceneManager.GetSceneAt(i);
                if (loaded.buildIndex == buildIndex && loaded.isLoaded)
                    return true;
            }
            return false;
        }
    }
}
