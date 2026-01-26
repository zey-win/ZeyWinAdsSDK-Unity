using System;
using UnityEngine;

namespace ZeyWinAds.Core
{
    /// <summary>
    /// Log level for ZeyWin Ads SDK logging
    /// </summary>
    public enum LogLevel
    {
        None = 0,
        Error = 1,
        Warning = 2,
        Info = 3,
        Debug = 4
    }

    /// <summary>
    /// Simple logging utility for ZeyWin Ads SDK.
    /// Provides conditional logging with configurable log levels.
    /// </summary>
    public static class Logger
    {
        private const string TAG = "[ZeyWinAds]";

        private static LogLevel _currentLogLevel = LogLevel.Info;

        /// <summary>
        /// Gets or sets the current log level.
        /// Messages above this level will not be logged.
        /// </summary>
        public static LogLevel CurrentLogLevel
        {
            get => _currentLogLevel;
            set => _currentLogLevel = value;
        }

        /// <summary>
        /// Sets the log level.
        /// </summary>
        /// <param name="level">The log level to set</param>
        public static void SetLogLevel(LogLevel level)
        {
            _currentLogLevel = level;
        }

        /// <summary>
        /// Logs a debug message. Only shown when LogLevel is Debug.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("ZEYWINADS_DEBUG")]
        public static void Debug(string message)
        {
            if (_currentLogLevel >= LogLevel.Debug)
            {
                UnityEngine.Debug.Log($"{TAG} {message}");
            }
        }

        /// <summary>
        /// Logs an info message. Shown when LogLevel is Info or higher.
        /// </summary>
        public static void Log(string message)
        {
            if (_currentLogLevel >= LogLevel.Info)
            {
                UnityEngine.Debug.Log($"{TAG} {message}");
            }
        }

        /// <summary>
        /// Logs an info message with formatting. Shown when LogLevel is Info or higher.
        /// </summary>
        public static void Log(string format, params object[] args)
        {
            if (_currentLogLevel >= LogLevel.Info)
            {
                UnityEngine.Debug.Log($"{TAG} {string.Format(format, args)}");
            }
        }

        /// <summary>
        /// Logs a warning message. Shown when LogLevel is Warning or higher.
        /// </summary>
        public static void Warn(string message)
        {
            if (_currentLogLevel >= LogLevel.Warning)
            {
                UnityEngine.Debug.LogWarning($"{TAG} {message}");
            }
        }

        /// <summary>
        /// Logs a warning message with formatting. Shown when LogLevel is Warning or higher.
        /// </summary>
        public static void Warn(string format, params object[] args)
        {
            if (_currentLogLevel >= LogLevel.Warning)
            {
                UnityEngine.Debug.LogWarning($"{TAG} {string.Format(format, args)}");
            }
        }

        /// <summary>
        /// Logs an error message. Always shown unless LogLevel is None.
        /// </summary>
        public static void Error(string message)
        {
            if (_currentLogLevel >= LogLevel.Error)
            {
                UnityEngine.Debug.LogError($"{TAG} {message}");
            }
        }

        /// <summary>
        /// Logs an error message with formatting. Always shown unless LogLevel is None.
        /// </summary>
        public static void Error(string format, params object[] args)
        {
            if (_currentLogLevel >= LogLevel.Error)
            {
                UnityEngine.Debug.LogError($"{TAG} {string.Format(format, args)}");
            }
        }

        /// <summary>
        /// Logs an exception. Always shown unless LogLevel is None.
        /// </summary>
        public static void Exception(Exception exception, string context = null)
        {
            if (_currentLogLevel >= LogLevel.Error)
            {
                string message = string.IsNullOrEmpty(context)
                    ? $"{TAG} Exception: {exception.Message}"
                    : $"{TAG} {context}: {exception.Message}";

                UnityEngine.Debug.LogError(message);
                UnityEngine.Debug.LogException(exception);
            }
        }

#if UNITY_EDITOR || ZEYWINADS_DEBUG
        /// <summary>
        /// Logs verbose debug information. Only available in Editor or with ZEYWINADS_DEBUG defined.
        /// </summary>
        public static void Verbose(string message)
        {
            if (_currentLogLevel >= LogLevel.Debug)
            {
                UnityEngine.Debug.Log($"{TAG} [VERBOSE] {message}");
            }
        }
#else
        /// <summary>
        /// Verbose logging is stripped in release builds.
        /// </summary>
        public static void Verbose(string message) { }
#endif
    }
}
