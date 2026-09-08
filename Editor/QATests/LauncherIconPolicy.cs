using UnityEditor;
using UnityEditor.Android;

namespace ZeyWinAds.Editor.QATests
{
    // The factory build assigns the Android launcher icons from factory/icon.png (see
    // FactoryBuildPreprocessor). ValidateAndroidIcons confirms every Legacy, Round and Adaptive
    // icon slot has a texture on every layer in Player Settings — an unset slot fails the build.
    //
    // Single source of truth, same as ApiLevelPolicy / StrippingLevelPolicy: the EditMode test
    // (LauncherIconTests) and the build preprocessor (QaTestsPreProcessor, via QaChecksRunner)
    // both call this method.
    public static class LauncherIconPolicy
    {
        // Returns null when compliant, otherwise a human-readable error message.
        public static string ValidateAndroidIcons()
        {
            var kinds = new (string Name, PlatformIconKind Kind)[]
            {
                ("Legacy", AndroidPlatformIconKind.Legacy),
                ("Round", AndroidPlatformIconKind.Round),
                ("Adaptive", AndroidPlatformIconKind.Adaptive),
            };

            foreach (var (name, kind) in kinds)
            {
                var icons = PlayerSettings.GetPlatformIcons(BuildTargetGroup.Android, kind);
                if (icons == null || icons.Length == 0)
                    return $"Android {name} launcher icon has no slots — cannot confirm factory/icon.png was applied.";

                foreach (var icon in icons)
                {
                    for (var layer = 0; layer < icon.maxLayerCount; layer++)
                    {
                        if (icon.GetTexture(layer) == null)
                            return $"Android {name} launcher icon slot {icon.width}x{icon.height} layer {layer} has no texture — " +
                                   "FactoryBuildPreprocessor must assign factory/icon.png to every launcher icon slot.";
                    }
                }
            }

            return null;
        }
    }
}
