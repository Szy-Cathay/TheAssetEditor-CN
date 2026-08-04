using System.Windows;
using System.Windows.Media;
using Shared.Core.Services;

namespace Shared.Core.Settings
{
    public enum ThemeType
    {
        DarkTheme,
        LightTheme,
        ChromeDark,
        VSCodeDark,
        HighContrastDark,
        WarmDark,
        CoolDark,
        ChromeLight,
        VSCodeLight,
        HighContrastLight,
    }

    public static class ThemesController
    {
        private static FontFamily? _defaultFontFamily;
        private static FontWeight _defaultFontWeight = FontWeights.Normal;

        public static ThemeType CurrentTheme { get; set; } = ThemeType.DarkTheme;

        // Callback for external theme systems (e.g. WPF-UI) that live outside Shared.Core
        public static Action<ThemeType>? ExternalThemeApplier { get; set; }

        private static ResourceDictionary FindDictionary(string partialSource)
        {
            foreach (var d in Application.Current.Resources.MergedDictionaries)
            {
                if (d.Source != null && d.Source.OriginalString.Contains(partialSource))
                    return d;
            }
            return null;
        }

        private static ResourceDictionary ThemeDictionary
        {
            get => FindDictionary("ColourDictionaries/");
            set
            {
                var existing = FindDictionary("ColourDictionaries/");
                if (existing != null)
                {
                    var idx = Application.Current.Resources.MergedDictionaries.IndexOf(existing);
                    Application.Current.Resources.MergedDictionaries[idx] = value;
                }
                else
                {
                    Application.Current.Resources.MergedDictionaries.Insert(0, value);
                }
            }
        }

        private static ResourceDictionary ControlColours
        {
            get => FindDictionary("ControlColours.xaml");
            set
            {
                var existing = FindDictionary("ControlColours.xaml");
                if (existing != null)
                {
                    var idx = Application.Current.Resources.MergedDictionaries.IndexOf(existing);
                    Application.Current.Resources.MergedDictionaries[idx] = value;
                }
                else
                {
                    Application.Current.Resources.MergedDictionaries.Insert(1, value);
                }
            }
        }

        private static void RefreshControls()
        {
            var dictionary = FindDictionary("Controls.xaml");
            if (dictionary == null) return;
            var merged = Application.Current.Resources.MergedDictionaries;
            var idx = merged.IndexOf(dictionary);
            merged.RemoveAt(idx);
            merged.Insert(idx, dictionary);
        }

        public static void SetTheme(ThemeType theme)
        {
            var themeName = theme.ToString();
            if (string.IsNullOrEmpty(themeName))
                return;
            CurrentTheme = theme;
            ThemeDictionary = new ResourceDictionary() { Source = new Uri($"Themes/ColourDictionaries/{themeName}.xaml", UriKind.Relative) };
            ControlColours = new ResourceDictionary() { Source = new Uri("Themes/ControlColours.xaml", UriKind.Relative) };
            _defaultFontFamily =
                ControlColours["AppFontFamily"] as FontFamily;
            _defaultFontWeight =
                ControlColours["AppFontWeight"] is FontWeight weight
                    ? weight
                    : FontWeights.Normal;
            RefreshControls();

            // Re-apply custom font since ControlColours.xaml was recreated
            ReApplyCustomFont();

            // Notify external theme system (WPF-UI)
            ExternalThemeApplier?.Invoke(theme);
        }

        public static object GetResource(object key)
        {
            return ThemeDictionary[key];
        }

        public static SolidColorBrush GetBrush(string name)
        {
            return GetResource(name) is SolidColorBrush brush ? brush : new SolidColorBrush(Colors.White);
        }

        public static string GetEnumAsString(ThemeType theme)
        {
            var key = "Theme." + theme;
            if (LocalizationManager.Instance != null)
                return LocalizationManager.Instance.Get(key);
            return theme.ToString();
        }

        /// <summary>
        /// Apply a custom font to the AppFontFamily resource.
        /// Call after SetTheme() and on startup to persist font across theme switches.
        /// </summary>
        public static FontFamily? CurrentFontFamily { get; private set; }
        public static FontWeight CurrentFontWeight { get; private set; } =
            FontWeights.Normal;

        public static void ApplyCustomFont(
            FontFamily? fontFamily,
            FontWeight fontWeight)
        {
            CurrentFontFamily = fontFamily;
            CurrentFontWeight = fontWeight;
            var colours = FindDictionary("ControlColours.xaml");
            if (colours == null)
                return;

            _defaultFontFamily ??=
                colours["AppFontFamily"] as FontFamily;
            if (fontFamily != null)
            {
                colours["AppFontFamily"] = fontFamily;
                colours["AppFontWeight"] = fontWeight;
                return;
            }

            if (_defaultFontFamily != null)
                colours["AppFontFamily"] = _defaultFontFamily;
            colours["AppFontWeight"] = _defaultFontWeight;
        }

        internal static void ReApplyCustomFont()
        {
            if (CurrentFontFamily != null)
            {
                var colours = FindDictionary("ControlColours.xaml");
                if (colours != null)
                {
                    colours["AppFontFamily"] = CurrentFontFamily;
                    colours["AppFontWeight"] = CurrentFontWeight;
                }
            }
        }
    }
}
