using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ZeyWinAds.UI
{
    /// <summary>
    /// Close button component with optional timer functionality.
    /// Shows a countdown before the button becomes active.
    /// </summary>
    public class CloseButton : MonoBehaviour
    {
        private Button _button;
        private Text _text;
        private bool _isTimerRunning;
        private float _remainingTime;
        private Action _onTimerComplete;
        private Coroutine _timerCoroutine;

        /// <summary>
        /// Whether the button is currently interactable
        /// </summary>
        public bool IsInteractable => _button != null && _button.interactable;

        /// <summary>
        /// Initializes the close button with references
        /// </summary>
        public void Initialize(Button button, Text text)
        {
            _button = button;
            _text = text;
        }

        /// <summary>
        /// Starts a countdown timer. Button is disabled during countdown.
        /// </summary>
        /// <param name="duration">Duration in seconds</param>
        /// <param name="onComplete">Called when timer completes</param>
        public void StartTimer(float duration, Action onComplete = null)
        {
            if (_isTimerRunning)
            {
                StopTimer();
            }

            _remainingTime = duration;
            _onTimerComplete = onComplete;
            _isTimerRunning = true;

            // Disable button during timer
            if (_button != null)
            {
                _button.interactable = false;
            }

            // Start countdown coroutine
            _timerCoroutine = StartCoroutine(TimerCoroutine());
        }

        /// <summary>
        /// Stops the timer if running
        /// </summary>
        public void StopTimer()
        {
            if (_timerCoroutine != null)
            {
                StopCoroutine(_timerCoroutine);
                _timerCoroutine = null;
            }

            _isTimerRunning = false;
            _remainingTime = 0;

            // Reset text
            if (_text != null)
            {
                _text.text = "\u00D7";
            }

            // Re-enable button
            if (_button != null)
            {
                _button.interactable = true;
            }
        }

        private IEnumerator TimerCoroutine()
        {
            while (_remainingTime > 0)
            {
                // Update text to show countdown
                if (_text != null)
                {
                    int seconds = Mathf.CeilToInt(_remainingTime);
                    _text.text = seconds.ToString();
                }

                yield return null;
                _remainingTime -= Time.deltaTime;
            }

            // Timer complete
            _isTimerRunning = false;

            // Reset text to X
            if (_text != null)
            {
                _text.text = "\u00D7";
            }

            // Enable button
            if (_button != null)
            {
                _button.interactable = true;
            }

            // Invoke callback
            _onTimerComplete?.Invoke();
            _onTimerComplete = null;
        }

        /// <summary>
        /// Sets the button interactable state
        /// </summary>
        public void SetInteractable(bool interactable)
        {
            if (_button != null)
            {
                _button.interactable = interactable;
            }
        }

        /// <summary>
        /// Sets the button text
        /// </summary>
        public void SetText(string text)
        {
            if (_text != null)
            {
                _text.text = text;
            }
        }

        /// <summary>
        /// Sets button visibility
        /// </summary>
        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        private void OnDisable()
        {
            // Stop timer when disabled
            if (_isTimerRunning)
            {
                StopTimer();
            }
        }

        private void OnDestroy()
        {
            StopTimer();
            _button = null;
            _text = null;
            _onTimerComplete = null;
        }
    }
}
