using System;
using System.Globalization;
using System.Windows;

namespace WindowsNotch
{
    public static class Localization
    {
        public const string DefaultLanguage = "id";

        private static ResourceDictionary? _languageDictionary;

        public static string CurrentLanguage { get; private set; } = DefaultLanguage;

        public static event EventHandler? LanguageChanged;

        public static void Apply(string? language)
        {
            string normalized = Normalize(language);

            var culture = new CultureInfo(normalized == "en" ? "en-US" : "id-ID");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            var dictionaries = Application.Current?.Resources.MergedDictionaries;
            if (dictionaries != null)
            {
                if (_languageDictionary != null)
                {
                    dictionaries.Remove(_languageDictionary);
                }

                _languageDictionary = new ResourceDictionary
                {
                    Source = new Uri(
                        $"pack://application:,,,/Strings.{normalized}.xaml",
                        UriKind.Absolute)
                };
                dictionaries.Add(_languageDictionary);
            }

            CurrentLanguage = normalized;
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        public static string Normalize(string? language)
        {
            return string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
                ? "en"
                : DefaultLanguage;
        }

        public static string Get(string key)
        {
            return Application.Current?.TryFindResource(key) as string ?? key;
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }
    }
}
