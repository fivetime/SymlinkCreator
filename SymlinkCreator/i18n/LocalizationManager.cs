using System;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using System.Threading;

namespace SymlinkCreator.i18n
{
    /// <summary>
    /// 管理应用程序的多语言切换。
    /// 使用 .resx 资源文件，支持运行时切换语言。
    /// </summary>
    public static class LocalizationManager
    {
        #region constants

        public const string LangEnglish = "en";
        public const string LangChineseSimplified = "zh-CN";

        #endregion


        #region fields

        private static ResourceManager _resourceManager;

        /// <summary>
        /// 当语言切换时触发，UI 可订阅此事件刷新显示。
        /// </summary>
        public static event EventHandler LanguageChanged;

        #endregion


        #region properties

        /// <summary>
        /// 所有支持的语言列表（语言代码 → 显示名称）。
        /// </summary>
        public static IReadOnlyList<(string Code, string DisplayName)> SupportedLanguages { get; } =
            new List<(string, string)>
            {
                (LangEnglish, "English"),
                (LangChineseSimplified, "中文（简体）"),
            };

        /// <summary>
        /// 当前使用的语言代码。
        /// </summary>
        public static string CurrentLanguage { get; private set; }

        #endregion


        #region initialization

        static LocalizationManager()
        {
            _resourceManager = new ResourceManager(
                "SymlinkCreator.i18n.Strings",
                typeof(LocalizationManager).Assembly);
        }

        /// <summary>
        /// 应用启动时调用，根据系统语言自动选择语言，若不支持则回退到英文。
        /// </summary>
        public static void InitializeFromSystemCulture()
        {
            string systemLang = CultureInfo.CurrentUICulture.Name;

            string matched = LangEnglish;
            foreach (var (code, _) in SupportedLanguages)
            {
                if (systemLang.Equals(code, StringComparison.OrdinalIgnoreCase))
                {
                    matched = code;
                    break;
                }
                if (systemLang.StartsWith(code.Split('-')[0], StringComparison.OrdinalIgnoreCase))
                {
                    matched = code;
                }
            }

            ApplyLanguage(matched);
        }

        #endregion


        #region public methods

        /// <summary>
        /// 切换到指定语言代码，并触发 LanguageChanged 事件。
        /// </summary>
        public static void ApplyLanguage(string languageCode)
        {
            CurrentLanguage = languageCode;

            CultureInfo culture = languageCode == LangEnglish
                ? CultureInfo.InvariantCulture
                : new CultureInfo(languageCode);

            Thread.CurrentThread.CurrentUICulture = culture;

            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// 获取当前语言的字符串资源。
        /// </summary>
        public static string Get(string key)
        {
            try
            {
                string value = _resourceManager.GetString(key, Thread.CurrentThread.CurrentUICulture);
                return value ?? $"[{key}]";
            }
            catch
            {
                return $"[{key}]";
            }
        }

        /// <summary>
        /// 获取格式化字符串（支持 string.Format 参数）。
        /// </summary>
        public static string Get(string key, params object[] args)
        {
            string template = Get(key);
            try
            {
                return string.Format(template, args);
            }
            catch
            {
                return template;
            }
        }

        #endregion
    }
}
