using System.Globalization;
using System.Windows.Data;
using GameWorld.Core.Rendering.Materials.Shaders;
using Shared.Core.Services;
using Shared.GameFormats.RigidModel;

namespace KitbasherEditor.ValueConverters
{
    public sealed class SceneNodeEditorDisplayConverter : IValueConverter
    {
        public object Convert(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            var key = value switch
            {
                UiVertexFormat vertexFormat =>
                    $"Kitbash.VertexFormat.{vertexFormat}",
                VertexFormat vertexFormat =>
                    $"Kitbash.VertexFormat.{vertexFormat}",
                CapabilityMaterialsEnum materialType =>
                    $"Kitbash.MaterialType.{materialType}",
                bool boolean =>
                    boolean
                        ? "Kitbash.Sidebar.Yes"
                        : "Kitbash.Sidebar.No",
                _ => null
            };

            if (key == null)
                return value?.ToString() ?? string.Empty;

            return LocalizationManager.Instance?.Get(key) ?? key;
        }

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
            => throw new NotSupportedException();
    }
}
