using UnityEditor;
using UnityEditor.Build;

namespace ZeyWinAds.Editor.QATests
{
    // Managed code stripping should be High for every shipped build — it's one of the biggest
    // levers on app size (see the app-size QA check) and none of the bundled SDKs are known to
    // break under aggressive stripping. Lower levels are a common accidental leftover from local
    // debugging (stripping gets turned down to speed up iteration) that should never reach a
    // real build.
    //
    // ValidateStrippingLevel is the single source of truth for this policy: both the EditMode
    // test (StrippingLevelPolicyTests) and QaTestsPreProcessor call this same method.
    public static class StrippingLevelPolicy
    {
        public const ManagedStrippingLevel RequiredLevel = ManagedStrippingLevel.High;

        // Returns null when compliant, otherwise a human-readable error message.
        public static string ValidateStrippingLevel()
        {
            var level = PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Android);
            if (level != RequiredLevel)
            {
                return $"Managed stripping level is {level}, expected {RequiredLevel} — " +
                    "set Project Settings > Player > Android > Optimization > Managed Stripping Level to High.";
            }
            return null;
        }
    }
}
