using Microsoft.Xna.Framework;
using Shared.Core.Services;

namespace Shared.Core.Settings
{
    public enum BackgroundColour
    {
        DarkGrey,
        LegacyBlue,
        Green,
        Custom,
    }

    public enum AppFontFamily
    {
        Default,
        AlibabaPuHuiTi,
        HarmonyOS,
    }

    public class ApplicationSettingsHelper
    {
        public static string GetEnumAsString(BackgroundColour colour)
        {
            var key = "BackgroundColour." + colour;
            if (LocalizationManager.Instance != null)
                return LocalizationManager.Instance.Get(key);
            return colour.ToString();
        }
        public static Color GetEnumAsColour(BackgroundColour colour) => colour switch
        {
            BackgroundColour.DarkGrey => new Color(50, 50, 50),
            BackgroundColour.LegacyBlue => new Color(94, 150, 239),
            BackgroundColour.Green => new Color(0, 177, 64),
            BackgroundColour.Custom => Color.Magenta, // placeholder, actual value comes from CustomBackgroundColour
            _ => throw new NotImplementedException(),
        };

        /// <summary>
        /// Parse a "R,G,B" string (e.g. "50,50,50") into an XNA Color.
        /// Returns DarkGrey as fallback on parse failure.
        /// </summary>
        public static Color ParseCustomBackgroundColour(string rgb)
        {
            if (string.IsNullOrWhiteSpace(rgb))
                return new Color(50, 50, 50);
            var parts = rgb.Split(',');
            if (parts.Length == 3
                && byte.TryParse(parts[0].Trim(), out byte r)
                && byte.TryParse(parts[1].Trim(), out byte g)
                && byte.TryParse(parts[2].Trim(), out byte b))
                return new Color(r, g, b);
            return new Color(50, 50, 50);
        }
    }

    public static class FontSettingsHelper
    {
        public static string[] GetAvailableWeights(AppFontFamily font) => font switch
        {
            AppFontFamily.Default => [],
            AppFontFamily.AlibabaPuHuiTi => ["Regular", "Medium", "ExtraBold"],
            AppFontFamily.HarmonyOS => ["Thin", "Light", "Regular", "Medium", "Bold", "Black"],
            _ => []
        };

        public static string GetDefaultWeight(AppFontFamily font) => font switch
        {
            AppFontFamily.AlibabaPuHuiTi => "Regular",
            AppFontFamily.HarmonyOS => "Regular",
            _ => "Regular"
        };

        /// <summary>
        /// Returns the WPF pack URI for the given font+weight, or null for system default.
        /// </summary>
        public static string? GetFontFamilyUri(AppFontFamily font, string? weight) => (font, weight) switch
        {
            (AppFontFamily.Default, _) => null,
            (AppFontFamily.AlibabaPuHuiTi, "Regular") => "pack://application:,,,/Fonts/#阿里巴巴普惠体 3.0 55 Regular",
            (AppFontFamily.AlibabaPuHuiTi, "Medium") => "pack://application:,,,/Fonts/#阿里巴巴普惠体 3.0 65 Medium",
            (AppFontFamily.AlibabaPuHuiTi, "ExtraBold") => "pack://application:,,,/Fonts/#阿里巴巴普惠体 3.0 95 ExtraBold",
            (AppFontFamily.HarmonyOS, "Thin") => "pack://application:,,,/Fonts/HarmonyOS_Sans_SC_Thin.ttf#HarmonyOS Sans SC Thin",
            (AppFontFamily.HarmonyOS, "Light") => "pack://application:,,,/Fonts/HarmonyOS_Sans_SC_Light.ttf#HarmonyOS Sans SC Light",
            (AppFontFamily.HarmonyOS, "Regular") => "pack://application:,,,/Fonts/HarmonyOS_Sans_SC_Regular.ttf#HarmonyOS Sans SC",
            (AppFontFamily.HarmonyOS, "Medium") => "pack://application:,,,/Fonts/HarmonyOS_Sans_SC_Medium.ttf#HarmonyOS Sans SC Medium",
            (AppFontFamily.HarmonyOS, "Bold") => "pack://application:,,,/Fonts/HarmonyOS_Sans_SC_Bold.ttf#HarmonyOS Sans SC",
            (AppFontFamily.HarmonyOS, "Black") => "pack://application:,,,/Fonts/HarmonyOS_Sans_SC_Black.ttf#HarmonyOS Sans SC Black",
            _ => null
        };

        public static string GetFontDisplayName(AppFontFamily font)
        {
            var key = "Font." + font;
            if (LocalizationManager.Instance != null)
                return LocalizationManager.Instance.Get(key);
            return font.ToString();
        }

        public static string GetWeightDisplayName(string weight)
        {
            var key = "FontWeight." + weight;
            if (LocalizationManager.Instance != null)
                return LocalizationManager.Instance.Get(key);
            return weight;
        }
    }
}
