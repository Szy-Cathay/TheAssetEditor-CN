using Editors.Shared.Core.Common;
using GameWorld.Core.Animation.AnimationChange;
using Microsoft.Xna.Framework;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView.Visualisation
{
    public enum MetaDataDocumentOwner
    {
        Persistent,
        Animation,
    }

    public enum MetaDataDiagnosticSeverity
    {
        Warning,
        Error,
    }

    public sealed record MetaDataBuildDiagnostic(
        ParsedMetadataAttribute Source,
        MetaDataDocumentOwner Owner,
        MetaDataDiagnosticSeverity Severity,
        string ReasonKey,
        MetaDataTimeRange? TimeRange = null,
        Vector3? Position = null,
        string? ResourcePath = null,
        string? BoneName = null);

    public sealed class MetaDataBuildResult
    {
        public IReadOnlyList<IMetaDataInstance> Instances { get; }
        public IReadOnlyList<IAnimationChangeRule> AnimationRules { get; }
        public IReadOnlyList<MetaDataBuildDiagnostic> Diagnostics { get; }

        public MetaDataBuildResult(
            IReadOnlyList<IMetaDataInstance> instances,
            IReadOnlyList<IAnimationChangeRule> animationRules,
            IReadOnlyList<MetaDataBuildDiagnostic> diagnostics)
        {
            Instances = instances.ToArray();
            AnimationRules = animationRules.ToArray();
            Diagnostics = diagnostics.ToArray();
        }
    }

    public sealed record MetaDataPreviewBuildResult(
        bool IsSupported,
        IMetaDataInstance? Instance,
        IReadOnlyList<MetaDataBuildDiagnostic> Diagnostics);
}
