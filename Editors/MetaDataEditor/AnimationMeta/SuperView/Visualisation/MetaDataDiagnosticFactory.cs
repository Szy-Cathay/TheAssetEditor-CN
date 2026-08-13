using Microsoft.Xna.Framework;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView.Visualisation
{
    internal static class MetaDataDiagnosticFactory
    {
        public static MetaDataBuildDiagnostic Create(
            ParsedMetadataAttribute source,
            MetaDataDocumentOwner owner,
            string reasonKey,
            string? resourcePath = null,
            string? boneName = null)
        {
            var timeRange = TryGetTimeRange(source);
            var position = TryGetPosition(source);
            return new(
                source,
                owner,
                MetaDataDiagnosticSeverity.Warning,
                reasonKey,
                timeRange,
                position,
                resourcePath,
                boneName);
        }

        private static MetaDataTimeRange? TryGetTimeRange(ParsedMetadataAttribute source)
        {
            try
            {
                return MetaDataTimeRange.TryCreate(source, out var range)
                    ? range
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static Vector3? TryGetPosition(ParsedMetadataAttribute source)
        {
            try
            {
                return SpatialMetaDataCatalog.TryCreate(source, out var binding)
                    ? binding.Position
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
