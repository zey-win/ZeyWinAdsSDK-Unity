using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEngine;

namespace ZeyWinAds.Editor.QATests
{
    // Confirms the Android launcher icons were set by the factory build, not left as whatever
    // the project shipped with.
    //
    // FactoryBuildPreprocessor copies factory/icon.png to Assets/Factory/icon.png, imports it,
    // and assigns THAT asset to every layer of every Legacy / Round / Adaptive slot. So the
    // check that actually proves the factory ran is: every slot/layer references the asset at
    // Assets/Factory/icon.png. A slot that is empty, or points at any other texture (a default
    // icon committed to the repo), means the factory processor did not set it — or failed to.
    //
    // Assets/Factory/icon.png is gitignored and only exists once the factory copy has run, so a
    // committed file can't fake a pass.
    //
    // Only runs for factory builds (factory/factory-config.json staged in the project root):
    // a plain local build keeps Unity's default-icon behaviour and is not gated. When it does
    // run, a pass means all three kinds carry the factory icon and nothing else.
    //
    // Single source of truth, same as ApiLevelPolicy / StrippingLevelPolicy: the EditMode test
    // (LauncherIconTests) and the build preprocessor (QaTestsPreProcessor, via QaChecksRunner)
    // both call this method.
    public static class LauncherIconPolicy
    {
        private const string FactoryIconPath = "Assets/Factory/icon.png";

        // Returns null when compliant, otherwise a human-readable error message.
        public static string ValidateAndroidIcons()
        {
            var factoryConfig = Path.Combine(Directory.GetCurrentDirectory(), "factory/factory-config.json");
            if (!File.Exists(factoryConfig))
                return null; // not a factory build — nothing to prove

            var factoryIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(FactoryIconPath);
            if (factoryIcon == null)
                return $"Factory build in progress but '{FactoryIconPath}' does not exist — " +
                       "FactoryBuildPreprocessor never imported factory/icon.png.";

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
                        var texture = icon.GetTexture(layer);
                        if (texture == null)
                            return $"Android {name} launcher icon slot {icon.width}x{icon.height} layer {layer} is empty — " +
                                   "the factory build did not assign factory/icon.png to it.";

                        var path = AssetDatabase.GetAssetPath(texture);
                        if (path != FactoryIconPath)
                            return $"Android {name} launcher icon slot {icon.width}x{icon.height} layer {layer} is '{path}', " +
                                   $"not the factory icon ('{FactoryIconPath}') — it was not set by the factory build.";
                    }
                }
            }

            return null;
        }
    }
}
