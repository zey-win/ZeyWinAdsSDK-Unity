using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZeyWinAds
{
    /// <summary>
    /// Repairs imported TMP text objects that have a font but no material source.
    /// This keeps older games from throwing when TMP lazily clones fontMaterial on device.
    /// </summary>
    internal static class ZeyWinAdsTmpRuntimeGuard
    {
        private static Shader _tmpMobileShader;
        private static TMP_FontAsset _defaultFontAsset;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Install()
        {
            _tmpMobileShader = Shader.Find("TextMeshPro/Mobile/Distance Field") ??
                               Shader.Find("TextMeshPro/Distance Field");
            _defaultFontAsset = TMP_Settings.defaultFontAsset;

            SceneManager.sceneLoaded += (_, __) => ApplyOnNextFrames();
            ApplyOnNextFrames();
        }

        private static void ApplyOnNextFrames()
        {
            var runner = new GameObject("ZeyWinAds TMP Runtime Guard");
            Object.DontDestroyOnLoad(runner);
            runner.hideFlags = HideFlags.HideAndDontSave;
            runner.AddComponent<Runner>().Run();
        }

        private static void Apply()
        {
            foreach (var fontAsset in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (fontAsset != null)
                    ApplyMaterial(fontAsset.material);
            }

            foreach (var text in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (text == null)
                    continue;

                if (text.font == null && _defaultFontAsset != null)
                    text.font = _defaultFontAsset;

                Material sharedMaterial = text.fontSharedMaterial;
                if (sharedMaterial == null && text.font != null)
                {
                    sharedMaterial = text.font.material;
                    if (sharedMaterial != null)
                        text.fontSharedMaterial = sharedMaterial;
                }

                ApplyMaterial(sharedMaterial);
                ApplyTextMaterialInstance(text, sharedMaterial);

                try
                {
                    text.UpdateMeshPadding();
                    text.SetAllDirty();
                }
                catch (MissingReferenceException)
                {
                }
            }
        }

        private static void ApplyMaterial(Material material)
        {
            if (material == null || material.shader == null)
                return;

            if (_tmpMobileShader != null &&
                material.shader.name.StartsWith("TextMeshPro/") &&
                material.shader != _tmpMobileShader)
            {
                material.shader = _tmpMobileShader;
            }
        }

        private static void ApplyTextMaterialInstance(TMP_Text text, Material fallbackMaterial)
        {
            if (fallbackMaterial == null)
                return;

            try
            {
                ApplyMaterial(text.fontMaterial);
            }
            catch (System.ArgumentNullException)
            {
                text.fontSharedMaterial = fallbackMaterial;
            }
            catch (MissingReferenceException)
            {
            }
        }

        private sealed class Runner : MonoBehaviour
        {
            public void Run()
            {
                StartCoroutine(ApplyCoroutine());
            }

            private IEnumerator ApplyCoroutine()
            {
                Apply();
                yield return null;
                Apply();
                yield return null;
                Apply();
                Destroy(gameObject);
            }
        }
    }
}
