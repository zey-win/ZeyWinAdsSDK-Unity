using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using Logger = ZeyWinAds.Core.Logger;

namespace ZeyWinAds.UI
{
    /// <summary>
    /// Downloads and decodes an ad image into a memory-lean texture: no mip chain (fixed-size
    /// UI never needs one), non-readable (drops the CPU copy, which otherwise doubles the
    /// cost), and optionally capped to a max side length. The caller owns the returned
    /// texture and must Destroy it — Unity never frees a Texture2D when its RawImage goes away.
    /// </summary>
    public static class AdImageLoader
    {
        /// <summary>Max side for small ad icons; a full 1024px icon costs ~8MB decoded.</summary>
        public const int IconMaxSize = 512;

        /// <summary>maxSize &lt;= 0 keeps the source resolution.</summary>
        public static IEnumerator Load(string url, Action<Texture2D> onLoaded, int maxSize = 0)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Logger.Warn("Failed to load image: {0}", request.error);
                    onLoaded?.Invoke(null);
                    yield break;
                }

                onLoaded?.Invoke(Decode(request.downloadHandler.data, maxSize));
            }
        }

        public static Texture2D Decode(byte[] data, int maxSize = 0)
        {
            var full = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (data == null || !full.LoadImage(data, false))
            {
                UnityEngine.Object.Destroy(full);
                return null;
            }

            int longest = Mathf.Max(full.width, full.height);
            if (maxSize <= 0 || longest <= maxSize)
            {
                full.Apply(false, true);
                return full;
            }

            float scale = (float)maxSize / longest;
            int width = Mathf.Max(1, Mathf.RoundToInt(full.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(full.height * scale));

            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            try
            {
                full.filterMode = FilterMode.Bilinear;
                Graphics.Blit(full, target);
                RenderTexture.active = target;

                var small = new Texture2D(width, height, TextureFormat.RGBA32, false);
                small.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                small.Apply(false, true);
                return small;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(full);
            }
        }
    }
}
