using System.Globalization;
using System.Windows.Data;
using GameWorld.Core.SceneNodes;
using KitbasherEditor.Views;

namespace KitbasherEditor.ValueConverters
{
    public enum SceneNodeIconKind
    {
        Root,
        Skeleton,
        Model,
        Lod,
        Mesh,
        ReferenceGroup,
    }

    [ValueConversion(typeof(SceneExplorerNode), typeof(SceneNodeIconKind))]
    public class SceneNodeToIconKindConverter : IValueConverter
    {
        public object Convert(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            if (value is not SceneExplorerNode node)
                return SceneNodeIconKind.Root;

            if (node.IsReference)
                return SceneNodeIconKind.ReferenceGroup;

            return node.Content switch
            {
                VariantMeshNode or SkeletonNode => SceneNodeIconKind.Skeleton,
                Rmv2ModelNode => SceneNodeIconKind.Model,
                Rmv2LodNode => SceneNodeIconKind.Lod,
                Rmv2MeshNode => SceneNodeIconKind.Mesh,
                _ => SceneNodeIconKind.Root,
            };
        }

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
