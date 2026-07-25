using System;
using System.Globalization;
using System.Windows.Data;
using Shared.Core.Settings;
using static Shared.Core.Settings.ThemesController;
using static Shared.Core.Settings.ApplicationSettingsHelper;

namespace AssetEditor.Views.Settings
{
    public class SettingsEnumConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return value;
            else if (value is GameTypeEnum game)
                return GameInformationDatabase.GetEnumAsString(game);
            else if (value is ThemeType theme)
                return GetEnumAsString(theme);
            else if (value is BackgroundColour backgroundColour)
                return GetEnumAsString(backgroundColour);
            else if (value is AppFontFamily font)
                return FontSettingsHelper.GetFontDisplayName(font);
            else
                return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }

    /// <summary>
    /// Converts font weight strings (e.g. "Regular", "Bold") to localized display names.
    /// </summary>
    public class FontWeightNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string weight)
                return FontSettingsHelper.GetWeightDisplayName(weight);
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
}
