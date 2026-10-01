using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace Editors.AnimationFragmentEditor.AnimationPack.Converters
{
    public class ResourceNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is string path ? Path.GetFileName(path.Replace('\\', '/')) : string.Empty;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
