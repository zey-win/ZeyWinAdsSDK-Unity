using UnityEngine;
using UnityEngine.UI;

namespace ZeyWinAds.UI
{
    public sealed class MoneyLoadingOverlayVisual : MonoBehaviour
    {
        public const float FillDurationSeconds = 8f;

        private const string MoneyTextureResourcePath = "Loading/LoadingMoneyPack";
        private static readonly float[] ProgressTimes = { 0f, 0.08f, 0.14f, 0.27f, 0.34f, 0.48f, 0.58f, 0.71f, 0.83f, 0.93f, 1f };
        private static readonly float[] ProgressValues = { 0f, 0.03f, 0.12f, 0.18f, 0.36f, 0.45f, 0.62f, 0.7f, 0.86f, 0.92f, 1f };

        private static Sprite _roundedSprite;
        private static Sprite _moneySprite;

        private RectTransform _trackRect;
        private RectTransform _fillRect;
        private RectTransform _moneyRect;
        private Text _label;
        private float _startRealtime;

        public void Build(Transform root)
        {
            var group = CreateRect("MoneyLoadingGroup", root);
            group.anchorMin = new Vector2(0.5f, 0f);
            group.anchorMax = new Vector2(0.5f, 0f);
            group.pivot = new Vector2(0.5f, 0f);
            group.anchoredPosition = new Vector2(0f, 220f);
            group.sizeDelta = new Vector2(820f, 210f);

            _trackRect = CreateRect("Track", group);
            _trackRect.anchorMin = new Vector2(0.5f, 0.5f);
            _trackRect.anchorMax = new Vector2(0.5f, 0.5f);
            _trackRect.pivot = new Vector2(0.5f, 0.5f);
            _trackRect.anchoredPosition = new Vector2(0f, 80f);
            _trackRect.sizeDelta = new Vector2(690f, 54f);

            var track = _trackRect.gameObject.AddComponent<Image>();
            track.sprite = RoundedSprite;
            track.type = Image.Type.Sliced;
            track.color = new Color(0.94f, 0.97f, 1f, 1f);
            track.raycastTarget = false;

            var inner = CreateRect("InnerTrack", _trackRect);
            inner.anchorMin = Vector2.zero;
            inner.anchorMax = Vector2.one;
            inner.offsetMin = new Vector2(8f, 8f);
            inner.offsetMax = new Vector2(-8f, -8f);

            var innerImage = inner.gameObject.AddComponent<Image>();
            innerImage.sprite = RoundedSprite;
            innerImage.type = Image.Type.Sliced;
            innerImage.color = new Color(0.13f, 0.21f, 0.47f, 1f);
            innerImage.raycastTarget = false;

            _fillRect = CreateRect("Fill", inner);
            _fillRect.anchorMin = new Vector2(0f, 0f);
            _fillRect.anchorMax = new Vector2(0f, 1f);
            _fillRect.pivot = new Vector2(0f, 0.5f);
            _fillRect.offsetMin = Vector2.zero;
            _fillRect.offsetMax = Vector2.zero;

            var fill = _fillRect.gameObject.AddComponent<Image>();
            fill.sprite = RoundedSprite;
            fill.type = Image.Type.Sliced;
            fill.color = new Color(1f, 0.74f, 0.16f, 1f);
            fill.raycastTarget = false;

            _moneyRect = CreateRect("MoneyPack", group);
            _moneyRect.anchorMin = new Vector2(0.5f, 0.5f);
            _moneyRect.anchorMax = new Vector2(0.5f, 0.5f);
            _moneyRect.pivot = new Vector2(0.5f, 0.5f);
            _moneyRect.anchoredPosition = new Vector2(0f, 108f);
            _moneyRect.sizeDelta = new Vector2(82f, 100f);

            var money = _moneyRect.gameObject.AddComponent<Image>();
            money.sprite = MoneySprite;
            money.preserveAspect = true;
            money.raycastTarget = false;

            var labelRect = CreateRect("Label", group);
            labelRect.anchorMin = new Vector2(0f, 0.5f);
            labelRect.anchorMax = new Vector2(1f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = new Vector2(0f, 18f);
            labelRect.sizeDelta = new Vector2(0f, 66f);

            _label = labelRect.gameObject.AddComponent<Text>();
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _label.text = "Loading 0%";
            _label.alignment = TextAnchor.MiddleCenter;
            _label.fontSize = 42;
            _label.fontStyle = FontStyle.Bold;
            _label.color = new Color(1f, 1f, 1f, 0.97f);
            _label.raycastTarget = false;

            Restart();
        }

        public void Restart()
        {
            _startRealtime = Time.realtimeSinceStartup;
            ApplyMotion(0f);
        }

        private void Update()
        {
            float elapsed01 = Mathf.Clamp01((Time.realtimeSinceStartup - _startRealtime) / FillDurationSeconds);
            ApplyMotion(EvaluateSteppedProgress(elapsed01));
        }

        private void ApplyMotion(float value01)
        {
            if (_trackRect == null || _moneyRect == null || _fillRect == null)
                return;

            float minX = -_trackRect.rect.width * 0.5f + 34f;
            float maxX = _trackRect.rect.width * 0.5f - 34f;
            float x = Mathf.Lerp(minX, maxX, value01);

            _moneyRect.anchoredPosition = new Vector2(x, 108f);
            _moneyRect.localRotation = Quaternion.Euler(0f, 0f, -9f);

            float trackWidth = Mathf.Max(1f, _trackRect.rect.width - 16f);
            _fillRect.sizeDelta = new Vector2(Mathf.Lerp(26f, trackWidth, value01), 0f);

            if (_label != null)
                _label.text = $"Loading {Mathf.RoundToInt(value01 * 100f)}%";
        }

        public static float EvaluateSteppedProgress(float time01)
        {
            time01 = Mathf.Clamp01(time01);

            for (int i = 1; i < ProgressTimes.Length; i++)
            {
                if (time01 > ProgressTimes[i])
                    continue;

                float segment = Mathf.InverseLerp(ProgressTimes[i - 1], ProgressTimes[i], time01);
                segment = Mathf.SmoothStep(0f, 1f, segment);
                return Mathf.Lerp(ProgressValues[i - 1], ProgressValues[i], segment);
            }

            return 1f;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            return obj.AddComponent<RectTransform>();
        }

        private static Sprite RoundedSprite
        {
            get
            {
                if (_roundedSprite != null)
                    return _roundedSprite;

                const int size = 64;
                const float radius = 28f;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.wrapMode = TextureWrapMode.Clamp;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = Mathf.Max(radius - x, x - (size - 1 - radius), 0f);
                        float dy = Mathf.Max(radius - y, y - (size - 1 - radius), 0f);
                        float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy));
                        texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                }

                texture.Apply();
                _roundedSprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, size, size),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect,
                    new Vector4(28f, 28f, 28f, 28f));
                _roundedSprite.name = "MoneyLoadingRounded";
                return _roundedSprite;
            }
        }

        private static Sprite MoneySprite
        {
            get
            {
                if (_moneySprite != null)
                    return _moneySprite;

                var texture = Resources.Load<Texture2D>(MoneyTextureResourcePath);
                if (texture == null)
                    return RoundedSprite;

                _moneySprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                _moneySprite.name = "MoneyLoadingPack";
                return _moneySprite;
            }
        }
    }
}
