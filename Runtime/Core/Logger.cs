using System;
using System.Collections.Generic;
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
        private static readonly Dictionary<string, string> ExactRu = new Dictionary<string, string>
        {
            { "API key cannot be null or empty", "API-ключ не может быть пустым" },
            { "Initialize called more than once, ignoring duplicate call", "Инициализация уже была запущена, повторный вызов пропущен" },
            { "Restoring sticky assigned offer after eligibility passed", "Восстанавливаем закреплённый оффер после проверки устройства" },
            { "Restoring persisted WebView lock after eligibility passed", "Восстанавливаем сохранённый WebView-lock после проверки устройства" },
            { "Starting automatic SDK surface preload after eligibility", "Запускаем автоматическую предзагрузку рекламных поверхностей SDK" },
            { "Interstitial already preloaded", "Межстраничная реклама уже предзагружена" },
            { "Rewarded already preloaded", "Rewarded-реклама уже предзагружена" },
            { "Banner already preloaded", "Баннер уже предзагружен" },
            { "Banner hidden", "Баннер скрыт" },
            { "Banner disabled", "Баннер отключён" },
            { "Banner enabled", "Баннер включён" },
            { "Native already preloaded", "Native-реклама уже предзагружена" },
            { "Popup already preloaded", "Popup-реклама уже предзагружена" },
            { "Popup repeat loop stopped", "Повторный цикл popup остановлен" },
            { "Opening click URL with lock_webview", "Открываем ссылку клика через lock_webview" },
            { "Device blocked - ZeyWin ad requests will be rejected", "Устройство заблокировано, запросы рекламы ZeyWin будут отклонены" },
            { "Using direct route", "Используется прямой маршрут" },
            { "AdLoader initialized", "AdLoader инициализирован" },
            { "Ad cache cleared", "Кэш рекламы очищен" },
            { "Preload on initialize is disabled", "Предзагрузка при инициализации отключена" },
            { "Preloading all ad types", "Предзагружаем все типы рекламы" },
            { "Starting ad preload on SDK initialization", "Запускаем предзагрузку рекламы при инициализации SDK" },
            { "Requesting native Android notification permission", "Запрашиваем нативное Android-разрешение на уведомления" },
            { "ATT request skipped: not running on iOS device", "ATT-запрос пропущен: это не iOS-устройство" },
            { "[AdMob] Initializing", "[AdMob] Инициализация" },
            { "[AdMob] Initialized", "[AdMob] Инициализирован" },
            { "[AdMob] Interstitial loaded", "[AdMob] Межстраничная реклама загружена" },
            { "[AdMob] Rewarded loaded", "[AdMob] Rewarded-реклама загружена" },
            { "[AdMob] Banner loaded", "[AdMob] Баннер загружен" },
            { "[Mediator] No ZeyWinAdsSettings asset — AdMob fallback disabled", "[Mediator] Нет ZeyWinAdsSettings asset, fallback AdMob отключён" },
            { "Auto initialize is enabled but ZeyWin API key is empty.", "Автоинициализация включена, но ZeyWin API-ключ пустой." },
            { "HTML ad: close() called from JS", "HTML-реклама: JS вызвал close()" },
            { "HTML ad: complete() called from JS", "HTML-реклама: JS вызвал complete()" },
            { "HTML ad: openUrl() called from JS", "HTML-реклама: JS вызвал openUrl()" },
            { "HTML ad: page loaded", "HTML-реклама: страница загружена" },
            { "UniWebView HTML ad created and loading", "UniWebView HTML-реклама создана и загружается" },
            { "Android HTML WebView created", "Android HTML WebView создан" },
            { "Android HTML WebView destroyed", "Android HTML WebView уничтожен" },
            { "WebView lock removed", "WebView-lock снят" },
            { "Locked WebView page loaded", "Locked WebView: страница загружена" },
            { "Android WebView created and loading", "Android WebView создан и загружается" },
            { "Android WebView destroyed", "Android WebView уничтожен" },
            { "Showing popup ad", "Показываем popup-рекламу" },
            { "Popup card layout created", "Разметка popup-карточки создана" },
            { "Native ad layout created", "Разметка native-рекламы создана" },
            { "Native ad clicked", "Клик по native-рекламе" },
            { "Hiding native ad", "Скрываем native-рекламу" }
        };

        private static readonly KeyValuePair<string, string>[] PhraseRu =
        {
            new KeyValuePair<string, string>("Startup loading force-hidden after", "Стартовый загрузчик принудительно скрыт через"),
            new KeyValuePair<string, string>("Google fallback skipped by auto-show cooldown:", "Google fallback пропущен по cooldown автопоказа:"),
            new KeyValuePair<string, string>("Showing Google fallback:", "Показываем Google fallback:"),
            new KeyValuePair<string, string>("Google fallback was requested but no AdMob interstitial became ready:", "Google fallback был запрошен, но AdMob interstitial не стал готов:"),
            new KeyValuePair<string, string>("Interstitial skipped while ZeyWin surface is active.", "Interstitial пропущен, пока активна поверхность ZeyWin."),
            new KeyValuePair<string, string>("Interstitial auto-show skipped by cooldown:", "Автопоказ interstitial пропущен по cooldown:"),
            new KeyValuePair<string, string>("No interstitial ad available", "Нет доступной interstitial-рекламы"),
            new KeyValuePair<string, string>("Rewarded skipped while ZeyWin surface is active.", "Rewarded пропущен, пока активна поверхность ZeyWin."),
            new KeyValuePair<string, string>("No rewarded ad available", "Нет доступной rewarded-рекламы"),
            new KeyValuePair<string, string>("Banner is disabled. Call EnableBanner() first.", "Баннер отключён. Сначала вызови EnableBanner()."),
            new KeyValuePair<string, string>("Banner shown at", "Баннер показан в позиции"),
            new KeyValuePair<string, string>("AdMob banner shown at", "AdMob-баннер показан в позиции"),
            new KeyValuePair<string, string>("AdMob banner reloading at", "AdMob-баннер перезагружается в позиции"),
            new KeyValuePair<string, string>("No banner ad available", "Нет доступной баннер-рекламы"),
            new KeyValuePair<string, string>("Native ad shown at", "Native-реклама показана в позиции"),
            new KeyValuePair<string, string>("No native ad", "Нет native-рекламы"),
            new KeyValuePair<string, string>("Native ad info provided for custom rendering:", "Данные native-рекламы отданы для кастомного рендера:"),
            new KeyValuePair<string, string>("Popup show skipped because a ZeyWin popup is already visible", "Popup не показан, потому что popup ZeyWin уже открыт"),
            new KeyValuePair<string, string>("Popup show delayed by cooldown:", "Показ popup отложен по cooldown:"),
            new KeyValuePair<string, string>("No popup ad", "Нет popup-рекламы"),
            new KeyValuePair<string, string>("ShowPopup requires preloader.", "ShowPopup требует preloader."),
            new KeyValuePair<string, string>("Popup repeat scheduled in", "Повтор popup запланирован через"),
            new KeyValuePair<string, string>("Popup repeat: loading next popup ad", "Popup repeat: загружаем следующую popup-рекламу"),
            new KeyValuePair<string, string>("Popup repeat: failed to load ad", "Popup repeat: не удалось загрузить рекламу"),
            new KeyValuePair<string, string>("Popup repeat waiting for cooldown:", "Popup repeat ждёт cooldown:"),
            new KeyValuePair<string, string>("Popup repeat: showing popup", "Popup repeat: показываем popup"),
            new KeyValuePair<string, string>("Popup auto-show waiting for app gate", "Автопоказ popup ждёт gate приложения"),
            new KeyValuePair<string, string>("Popup auto-show skipped because app gate is still closed", "Автопоказ popup пропущен: gate приложения всё ещё закрыт"),
            new KeyValuePair<string, string>("Popup auto-show waiting for cooldown:", "Автопоказ popup ждёт cooldown:"),
            new KeyValuePair<string, string>("Auto-showing popup after", "Автоматически показываем popup после"),
            new KeyValuePair<string, string>("Popup auto-show scheduled in", "Автопоказ popup запланирован через"),
            new KeyValuePair<string, string>("Preload failed for", "Предзагрузка не удалась для"),
            new KeyValuePair<string, string>("SDK not initialized", "SDK не инициализирован"),
            new KeyValuePair<string, string>("ad is already loading", "реклама уже загружается"),
            new KeyValuePair<string, string>("Loading", "Загрузка"),
            new KeyValuePair<string, string>("ad loaded successfully", "реклама успешно загружена"),
            new KeyValuePair<string, string>("Failed to load", "Не удалось загрузить"),
            new KeyValuePair<string, string>("Showing", "Показываем"),
            new KeyValuePair<string, string>("Device blocked by server:", "Устройство заблокировано сервером:"),
            new KeyValuePair<string, string>("CrashGuard.Start failed:", "CrashGuard.Start не сработал:"),
            new KeyValuePair<string, string>("Startup loading handed off to WebView lock", "Стартовый загрузчик передан WebView-lock"),
            new KeyValuePair<string, string>("Server error:", "Ошибка сервера:"),
            new KeyValuePair<string, string>("Request failed:", "Запрос не удался:"),
            new KeyValuePair<string, string>("Failed to parse response:", "Не удалось разобрать ответ:"),
            new KeyValuePair<string, string>("Retrying with next endpoint", "Пробуем следующий endpoint"),
            new KeyValuePair<string, string>("Tracking event", "Трекинг события"),
            new KeyValuePair<string, string>("Event tracked successfully", "Событие успешно отправлено"),
            new KeyValuePair<string, string>("Event tracking failed", "Трекинг события не удался"),
            new KeyValuePair<string, string>("Requesting", "Запрашиваем"),
            new KeyValuePair<string, string>("Suspending ZeyWin ad requests", "Приостанавливаем запросы рекламы ZeyWin"),
            new KeyValuePair<string, string>("ZeyWin ad request skipped", "Запрос рекламы ZeyWin пропущен"),
            new KeyValuePair<string, string>("Cannot", "Нельзя"),
            new KeyValuePair<string, string>("Failed to", "Не удалось"),
            new KeyValuePair<string, string>("skipped", "пропущено"),
            new KeyValuePair<string, string>("loaded", "загружено"),
            new KeyValuePair<string, string>("created", "создано"),
            new KeyValuePair<string, string>("destroyed", "уничтожено"),
            new KeyValuePair<string, string>("active", "активна"),
            new KeyValuePair<string, string>("unknown", "неизвестно")
        };

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
                UnityEngine.Debug.Log($"{TAG} {Localize(message)}");
            }
        }

        /// <summary>
        /// Logs a debug message with formatting. Only shown when LogLevel is Debug.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("ZEYWINADS_DEBUG")]
        public static void Debug(string format, params object[] args)
        {
            if (_currentLogLevel >= LogLevel.Debug)
            {
                UnityEngine.Debug.Log($"{TAG} {BuildMessage(format, args)}");
            }
        }

        /// <summary>
        /// Logs an info message. Shown when LogLevel is Info or higher.
        /// </summary>
        public static void Log(string message)
        {
            if (_currentLogLevel >= LogLevel.Info)
            {
                UnityEngine.Debug.Log($"{TAG} {Localize(message)}");
            }
        }

        /// <summary>
        /// Logs an info message with formatting. Shown when LogLevel is Info or higher.
        /// </summary>
        public static void Log(string format, params object[] args)
        {
            if (_currentLogLevel >= LogLevel.Info)
            {
                UnityEngine.Debug.Log($"{TAG} {BuildMessage(format, args)}");
            }
        }

        /// <summary>
        /// Logs a warning message. Shown when LogLevel is Warning or higher.
        /// </summary>
        public static void Warn(string message)
        {
            if (_currentLogLevel >= LogLevel.Warning)
            {
                UnityEngine.Debug.LogWarning($"{TAG} {Localize(message)}");
            }
        }

        /// <summary>
        /// Logs a warning message with formatting. Shown when LogLevel is Warning or higher.
        /// </summary>
        public static void Warn(string format, params object[] args)
        {
            if (_currentLogLevel >= LogLevel.Warning)
            {
                UnityEngine.Debug.LogWarning($"{TAG} {BuildMessage(format, args)}");
            }
        }

        /// <summary>
        /// Logs an error message. Always shown unless LogLevel is None.
        /// </summary>
        public static void Error(string message)
        {
            if (_currentLogLevel >= LogLevel.Error)
            {
                UnityEngine.Debug.LogError($"{TAG} {Localize(message)}");
            }
        }

        /// <summary>
        /// Logs an error message with formatting. Always shown unless LogLevel is None.
        /// </summary>
        public static void Error(string format, params object[] args)
        {
            if (_currentLogLevel >= LogLevel.Error)
            {
                UnityEngine.Debug.LogError($"{TAG} {BuildMessage(format, args)}");
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
                    ? $"{TAG} Исключение: {exception.Message}"
                    : $"{TAG} {Localize(context)}: {exception.Message}";

                UnityEngine.Debug.LogError(message);
                UnityEngine.Debug.LogException(exception);
            }
        }

        private static string BuildMessage(string format, params object[] args)
        {
            string localizedFormat = Localize(format);
            object[] localizedArgs = LocalizeArgs(args);

            try
            {
                return string.Format(localizedFormat, localizedArgs);
            }
            catch
            {
                try
                {
                    return string.Format(format, args);
                }
                catch
                {
                    return localizedFormat;
                }
            }
        }

        private static object[] LocalizeArgs(object[] args)
        {
            if (args == null || args.Length == 0)
                return args;

            object[] localized = new object[args.Length];
            for (int i = 0; i < args.Length; i++)
                localized[i] = LocalizeArg(args[i]);
            return localized;
        }

        private static object LocalizeArg(object arg)
        {
            if (arg == null)
                return null;

            string text = arg.ToString();
            switch (text)
            {
                case "Interstitial": return "межстраничная";
                case "Rewarded": return "rewarded";
                case "Banner": return "баннер";
                case "Native": return "native";
                case "Popup": return "popup";
                case "Top": return "сверху";
                case "Bottom": return "снизу";
                case "html_webview": return "HTML WebView";
                case "locked_webview": return "locked WebView";
                case "locked_webview_promote": return "поднятие locked WebView";
                case "html_webview_promote": return "поднятие HTML WebView";
                case "popup": return "popup";
                case "admob_initialize": return "инициализация AdMob";
                case "admob_interstitial": return "AdMob interstitial";
                case "admob_rewarded": return "AdMob rewarded";
                case "zeywin_interstitial_video": return "видео interstitial ZeyWin";
                case "zeywin_rewarded_video": return "видео rewarded ZeyWin";
                default: return arg;
            }
        }

        private static string Localize(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;

            if (ExactRu.TryGetValue(message, out string exact))
                return exact;

            string result = message;
            for (int i = 0; i < PhraseRu.Length; i++)
            {
                if (result.Contains(PhraseRu[i].Key))
                    result = result.Replace(PhraseRu[i].Key, PhraseRu[i].Value);
            }

            return result;
        }

#if UNITY_EDITOR || ZEYWINADS_DEBUG
        /// <summary>
        /// Logs verbose debug information. Only available in Editor or with ZEYWINADS_DEBUG defined.
        /// </summary>
        public static void Verbose(string message)
        {
            if (_currentLogLevel >= LogLevel.Debug)
            {
                UnityEngine.Debug.Log($"{TAG} [ПОДРОБНО] {Localize(message)}");
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
