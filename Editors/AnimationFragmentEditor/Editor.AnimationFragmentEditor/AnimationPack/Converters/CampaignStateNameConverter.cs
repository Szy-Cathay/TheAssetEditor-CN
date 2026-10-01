using System.Globalization;
using System.Windows.Data;
using Shared.Core.Services;

namespace Editors.AnimationFragmentEditor.AnimationPack.Converters
{
    public class CampaignStateNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var name = value as string ?? string.Empty;
            if (string.IsNullOrEmpty(name)) return LocalizationManager.Instance?.Get("AnimPack.Campaign.State.None") ?? string.Empty;
            var key = $"AnimPack.Campaign.State.{name}";
            return LocalizationManager.Instance?.GetOrDefault(key, name) ?? name;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
