using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ZeyWinAds.Core;
using Logger = ZeyWinAds.Core.Logger;

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
        private readonly List<Texture2D> _ownedTextures = new List<Texture2D>();

        /// <summary>
        /// Gets the canvas transform
        /// </summary>
        public new Transform transform => _root.transform;

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
        /// Gets the safe area insets (top, bottom, left, right) in screen pixels
        /// </summary>
        public static (float top, float bottom, float left, float right) GetSafeAreaInsets()
        {
            Rect safeArea = Screen.safeArea;
            float top = Screen.height - (safeArea.y + safeArea.height);
            float bottom = safeArea.y;
            float left = safeArea.x;
            float right = Screen.width - (safeArea.x + safeArea.width);
            return (top, bottom, left, right);
        }

        /// <summary>
        /// Gets the canvas scale factor for converting screen pixels to canvas units
        /// </summary>
        public float GetScaleFactor()
        {
            return _canvas != null ? _canvas.scaleFactor : 1f;
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

            var theme = AdThemeController.Current;

            var image = container.AddComponent<Image>();
            image.color = theme.FullscreenBackground;

            return container;
        }

        /// <summary>
        /// Creates a close button in the top-right corner (respects safe area)
        /// </summary>
        /// <param name="onClick">Callback when button is clicked</param>
        /// <returns>The created CloseButton component</returns>
        public CloseButton CreateCloseButton(Action onClick)
        {
            var buttonObj = new GameObject("CloseButton");
            buttonObj.transform.SetParent(_root.transform, false);

            // Get safe area insets and convert to canvas units
            Rect safeArea = Screen.safeArea;
            float topInset = Screen.height - (safeArea.y + safeArea.height);
            float rightInset = Screen.width - (safeArea.x + safeArea.width);
            float scaleFactor = GetScaleFactor();

            var rectTransform = buttonObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(1, 1);
            rectTransform.anchorMax = new Vector2(1, 1);
            rectTransform.pivot = new Vector2(1, 1);
            rectTransform.anchoredPosition = new Vector2(-40 - rightInset / scaleFactor, -40 - topInset / scaleFactor);
            rectTransform.sizeDelta = new Vector2(100, 100);

            var theme = AdThemeController.Current;

            var buttonImage = buttonObj.AddComponent<Image>();
            buttonImage.color = new Color(theme.SecondaryButton.r, theme.SecondaryButton.g, theme.SecondaryButton.b, 0.92f);

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
            text.font = ZeyWinFont.GetPreferred();
            text.fontSize = 56;
            text.color = theme.SecondaryButtonText;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = "\u00D7"; // Multiplication sign as X

            // Add CloseButton component
            var closeButton = buttonObj.AddComponent<CloseButton>();
            closeButton.Initialize(button, text);

            return closeButton;
        }

        /// <summary>
        /// Creates an image display element with aspect ratio fill (covers screen, crops edges)
        /// </summary>
        /// <param name="parent">Parent transform</param>
        /// <param name="imageUrl">URL of the image to load</param>
        /// <returns>The created GameObject</returns>
        public GameObject CreateImageDisplay(Transform parent, string imageUrl, Action onLoaded = null, Action<string> onError = null)
        {
            // Create a container that clips content
            var containerObj = new GameObject("ImageContainer");
            containerObj.transform.SetParent(parent, false);

            var containerRect = containerObj.AddComponent<RectTransform>();
            containerRect.anchorMin = Vector2.zero;
            containerRect.anchorMax = Vector2.one;
            containerRect.sizeDelta = Vector2.zero;
            containerRect.anchoredPosition = Vector2.zero;

            // Add mask to clip overflowing content
            var mask = containerObj.AddComponent<RectMask2D>();

            // Create image inside container
            var imageObj = new GameObject("ImageDisplay");
            imageObj.transform.SetParent(containerObj.transform, false);

            var rectTransform = imageObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;

            var rawImage = imageObj.AddComponent<RawImage>();
            rawImage.color = Color.white;

            // Load image and apply aspect fill scaling
            LoadImage(imageUrl, (texture) =>
            {
                if (texture != null && rawImage != null && containerRect != null)
                {
                    rawImage.texture = texture;
                    ApplyAspectFill(rectTransform, containerRect, texture.width, texture.height);
                    onLoaded?.Invoke();
                }
                else if (texture == null)
                {
                    onError?.Invoke("image_load_error");
                }
            });

            return containerObj;
        }

        /// <summary>
        /// Applies aspect fill scaling (image covers container, edges may be cropped)
        /// </summary>
        private void ApplyAspectFill(RectTransform imageRect, RectTransform containerRect, float imageWidth, float imageHeight)
        {
            float containerWidth = containerRect.rect.width > 0 ? containerRect.rect.width : Screen.width;
            float containerHeight = containerRect.rect.height > 0 ? containerRect.rect.height : Screen.height;

            float imageAspect = imageWidth / imageHeight;
            float containerAspect = containerWidth / containerHeight;

            float width, height;

            if (imageAspect > containerAspect)
            {
                // Image is wider - match height, overflow width
                height = containerHeight;
                width = height * imageAspect;
            }
            else
            {
                // Image is taller - match width, overflow height
                width = containerWidth;
                height = width / imageAspect;
            }

            imageRect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>
        /// Loads an image from a URL
        /// </summary>
        /// <param name="url">URL of the image</param>
        /// <param name="callback">Called with the loaded texture (or null on failure)</param>
        /// <param name="maxSize">Max side in px (0 = source resolution). Use
        /// <see cref="AdImageLoader.IconMaxSize"/> for icons.</param>
        public void LoadImage(string url, Action<Texture2D> callback, int maxSize = 0)
        {
            if (string.IsNullOrEmpty(url))
            {
                Logger.Warn("Cannot load image - URL is empty");
                callback?.Invoke(null);
                return;
            }

            StartCoroutine(AdImageLoader.Load(url, texture =>
            {
                // Textures outlive the RawImage that shows them, so this canvas owns and frees them.
                if (texture != null)
                    _ownedTextures.Add(texture);
                callback?.Invoke(texture);
            }, maxSize));
        }

        /// <summary>
        /// Frees a texture returned by <see cref="LoadImage"/> before the canvas itself goes away.
        /// </summary>
        public void ReleaseTexture(Texture2D texture)
        {
            if (texture == null)
                return;

            _ownedTextures.Remove(texture);
            UnityEngine.Object.Destroy(texture);
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
            for (int i = 0; i < _ownedTextures.Count; i++)
            {
                if (_ownedTextures[i] != null)
                    UnityEngine.Object.Destroy(_ownedTextures[i]);
            }
            _ownedTextures.Clear();

            _canvas = null;
            _scaler = null;
            _raycaster = null;
        }
    }
}
