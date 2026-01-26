using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace ZeyWinAds.UI
{
    /// <summary>
    /// Manages the canvas for displaying ads.
    /// Provides utilities for creating UI elements for ad display.
    /// </summary>
    public class AdCanvas : MonoBehaviour
    {
        private Canvas _canvas;
        private CanvasScaler _scaler;
        private GraphicRaycaster _raycaster;
        private GameObject _root;

        /// <summary>
        /// Gets the canvas transform
        /// </summary>
        public Transform transform => _root.transform;

        /// <summary>
        /// Creates a new AdCanvas instance
        /// </summary>
        /// <param name="name">Name for the canvas GameObject</param>
        /// <returns>The created AdCanvas</returns>
        public static AdCanvas Create(string name = "AdCanvas")
        {
            var go = new GameObject(name);
            var adCanvas = go.AddComponent<AdCanvas>();
            adCanvas.Initialize(go);
            return adCanvas;
        }

        private void Initialize(GameObject root)
        {
            _root = root;

            // Create canvas component
            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;

            // Canvas scaler for responsive UI
            _scaler = _root.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1080, 1920);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = 0.5f;

            // Raycaster for UI interaction
            _raycaster = _root.AddComponent<GraphicRaycaster>();

            // Don't destroy on load
            DontDestroyOnLoad(_root);
        }

        /// <summary>
        /// Sets the sorting order of the canvas
        /// </summary>
        public void SetSortingOrder(int order)
        {
            if (_canvas != null)
            {
                _canvas.sortingOrder = order;
            }
        }

        /// <summary>
        /// Creates a fullscreen container with a dark background
        /// </summary>
        /// <param name="name">Name for the container</param>
        /// <returns>The created container GameObject</returns>
        public GameObject CreateFullscreenContainer(string name = "Container")
        {
            var container = new GameObject(name);
            container.transform.SetParent(_root.transform, false);

            var rectTransform = container.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;

            // Dark background
            var image = container.AddComponent<Image>();
            image.color = new Color(0, 0, 0, 0.95f);

            return container;
        }

        /// <summary>
        /// Creates a close button in the top-right corner
        /// </summary>
        /// <param name="onClick">Callback when button is clicked</param>
        /// <returns>The created CloseButton component</returns>
        public CloseButton CreateCloseButton(Action onClick)
        {
            var buttonObj = new GameObject("CloseButton");
            buttonObj.transform.SetParent(_root.transform, false);

            var rectTransform = buttonObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(1, 1);
            rectTransform.anchorMax = new Vector2(1, 1);
            rectTransform.pivot = new Vector2(1, 1);
            rectTransform.anchoredPosition = new Vector2(-20, -20);
            rectTransform.sizeDelta = new Vector2(44, 44);

            // Button background
            var buttonImage = buttonObj.AddComponent<Image>();
            buttonImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);

            // Make it circular
            // Note: For a truly circular button, you'd use a sprite mask or custom shader
            // This is a simple approximation

            // Button component
            var button = buttonObj.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            button.onClick.AddListener(() => onClick?.Invoke());

            // Close "X" text
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(buttonObj.transform, false);

            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            var text = textObj.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 28;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = "\u00D7"; // Multiplication sign as X

            // Add CloseButton component
            var closeButton = buttonObj.AddComponent<CloseButton>();
            closeButton.Initialize(button, text);

            return closeButton;
        }

        /// <summary>
        /// Creates an image display element
        /// </summary>
        /// <param name="parent">Parent transform</param>
        /// <param name="imageUrl">URL of the image to load</param>
        /// <returns>The created GameObject</returns>
        public GameObject CreateImageDisplay(Transform parent, string imageUrl)
        {
            var imageObj = new GameObject("ImageDisplay");
            imageObj.transform.SetParent(parent, false);

            var rectTransform = imageObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;

            var rawImage = imageObj.AddComponent<RawImage>();
            rawImage.color = Color.white;

            // Load image
            LoadImage(imageUrl, (texture) =>
            {
                if (texture != null && rawImage != null)
                {
                    rawImage.texture = texture;
                }
            });

            return imageObj;
        }

        /// <summary>
        /// Loads an image from a URL
        /// </summary>
        /// <param name="url">URL of the image</param>
        /// <param name="callback">Called with the loaded texture (or null on failure)</param>
        public void LoadImage(string url, Action<Texture2D> callback)
        {
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogWarning("[ZeyWinAds] Cannot load image - URL is empty");
                callback?.Invoke(null);
                return;
            }

            StartCoroutine(LoadImageCoroutine(url, callback));
        }

        private IEnumerator LoadImageCoroutine(string url, Action<Texture2D> callback)
        {
            using (var request = UnityWebRequestTexture.GetTexture(url))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var texture = DownloadHandlerTexture.GetContent(request);
                    callback?.Invoke(texture);
                }
                else
                {
                    Debug.LogWarning($"[ZeyWinAds] Failed to load image: {request.error}");
                    callback?.Invoke(null);
                }
            }
        }

        /// <summary>
        /// Destroys the canvas and all child objects
        /// </summary>
        public void Destroy()
        {
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }

        private void OnDestroy()
        {
            _canvas = null;
            _scaler = null;
            _raycaster = null;
        }
    }
}
