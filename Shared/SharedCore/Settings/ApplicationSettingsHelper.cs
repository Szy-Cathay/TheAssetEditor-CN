using Microsoft.Xna.Framework;
using Shared.Core.Services;

using System.Windows;
using FontFamily = System.Windows.Media.FontFamily;

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
        private static readonly Uri FontResourceBaseUri = new(
            "pack://application:,,,/AssetEditor.CN;component/");

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

        public static FontFamily? GetFontFamily(AppFontFamily font) => font switch
        {
            AppFontFamily.Default => null,
            AppFontFamily.AlibabaPuHuiTi => new FontFamily(
                FontResourceBaseUri,
                "./Fonts/#Alibaba PuHuiTi 3.0"),
            AppFontFamily.HarmonyOS => new FontFamily(
                FontResourceBaseUri,
                "./Fonts/#HarmonyOS Sans SC"),
            _ => null
        };

        public static FontWeight GetFontWeight(string? weight) => weight switch
        {
            "Thin" => FontWeight.FromOpenTypeWeight(250),
            "Light" => FontWeights.Light,
            "Medium" => FontWeights.Medium,
            "Bold" => FontWeights.Bold,
            "ExtraBold" => FontWeights.ExtraBold,
            "Black" => FontWeights.Black,
            _ => FontWeights.Normal,
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
