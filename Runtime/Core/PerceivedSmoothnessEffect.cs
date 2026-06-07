using UnityEngine;

namespace ZeyWinAds.Core
{
    internal sealed class PerceivedSmoothnessEffect : MonoBehaviour
    {
        private static bool _installAttempted;
        private Material _material;
        private RenderTexture _history;

        public static void EnsureInstalled()
        {
            if (_installAttempted)
                return;

            _installAttempted = true;
            if (!RemoteConfigBridge.GetBool("zeywin_perceived_smoothness_enabled", false))
                return;

            Camera[] cameras = Camera.allCameras;
            if (cameras == null || cameras.Length == 0)
            {
                Camera.onPreRender += InstallWhenCameraAppears;
                return;
            }

            for (int i = 0; i < cameras.Length; i++)
                InstallOnCamera(cameras[i]);
        }

        private static void InstallWhenCameraAppears(Camera camera)
        {
            Camera.onPreRender -= InstallWhenCameraAppears;
            InstallOnCamera(camera);
        }

        private static void InstallOnCamera(Camera camera)
        {
            if (camera == null || camera.GetComponent<PerceivedSmoothnessEffect>() != null)
                return;

            camera.gameObject.AddComponent<PerceivedSmoothnessEffect>();
            Logger.Log("Пост-эффект плавности SDK установлен на камеру: {0}", camera.name);
        }

        private void OnEnable()
        {
            Shader shader = Resources.Load<Shader>("ZeyWinPerceivedSmoothness");
            if (shader == null || !shader.isSupported)
            {
                enabled = false;
                Logger.Warn("Пост-эффект плавности SDK недоступен: shader не найден или не поддерживается");
                return;
            }

            _material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (source == null || destination == null || source.width <= 0 || source.height <= 0)
                return;

            if (_material == null || !RemoteConfigBridge.GetBool("zeywin_perceived_smoothness_enabled", false))
            {
                Graphics.Blit(source, destination);
                return;
            }

            if (!EnsureHistory(source))
            {
                Graphics.Blit(source, destination);
                return;
            }

            _material.SetTexture("_HistoryTex", _history);
            _material.SetFloat("_Blend", ResolveBlend());
            Graphics.Blit(source, destination, _material);
            Graphics.Blit(source, _history);
        }

        private bool EnsureHistory(RenderTexture source)
        {
            if (_history != null && _history.width == source.width && _history.height == source.height)
                return true;

            ReleaseHistory();
            _history = new RenderTexture(source.width, source.height, 0, source.format)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _history.Create();

            if (!_history.IsCreated())
            {
                ReleaseHistory();
                return false;
            }

            Graphics.Blit(source, _history);
            return true;
        }

        private static float ResolveBlend()
        {
            int percent = RemoteConfigBridge.GetInt("zeywin_perceived_smoothness_blend_percent", 12);
            return Mathf.Clamp(percent, 0, 35) / 100f;
        }

        private void OnDisable()
        {
            ReleaseHistory();
            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
        }

        private void OnDestroy()
        {
            OnDisable();
        }

        private void ReleaseHistory()
        {
            if (_history == null)
                return;

            _history.Release();
            Destroy(_history);
            _history = null;
        }
    }
}
