using Microsoft.Xna.Framework;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView.Visualisation
{
    public enum CombatMetaDataPreviewCategory
    {
        Impact,
        Target,
        Fire,
        Splash
    }

    public interface IMetaDataPreview
    {
        ParsedMetadataAttribute Source { get; }
        bool IsSelected { get; set; }
    }

    public interface ICombatMetaDataPreview : ISpatialMetaDataPreview
    {
        CombatMetaDataPreviewCategory Category { get; }
        bool IsEnabled { get; set; }
    }

    public interface ITimedMetaDataPreview : IMetaDataPreview
    {
        bool ShowForEntireAnimation { get; set; }
    }

    public interface ISpatialMetaDataPreview : ITimedMetaDataPreview
    {
        Vector3 FocusPosition { get; }
        Matrix ReferenceWorldTransform { get; }
        Matrix WorldTransform { get; }
        int? HighlightedBoneIndex { get; }
    }
}
