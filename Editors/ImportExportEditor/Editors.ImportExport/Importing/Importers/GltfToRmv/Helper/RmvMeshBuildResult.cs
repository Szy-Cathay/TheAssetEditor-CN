using Shared.GameFormats.RigidModel;

namespace Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;

internal sealed record RmvMeshBuildResult(
    RmvFile? File,
    RmvMeshImportSummary Summary);

internal sealed record RmvMeshImportSummary(
    IReadOnlyList<RmvMeshSegmentImportSummary> Segments)
{
    public int TotalAffectedVertices => Segments.Sum(segment => segment.AffectedVertices);

    public float MaximumDiscardedWeight => Segments.Count == 0
        ? 0
        : Segments.Max(segment => segment.MaximumDiscardedWeight);

    public int VerticesAboveTenPercentDiscarded => Segments.Sum(
        segment => segment.VerticesAboveTenPercentDiscarded);
}

internal sealed record RmvMeshSegmentImportSummary(
    string ModelName,
    int AffectedVertices,
    float MaximumDiscardedWeight,
    int VerticesAboveTenPercentDiscarded,
    bool RebuiltNormals,
    bool RebuiltTangents,
    bool DefaultedTextureCoordinates,
    bool IgnoredVertexColors,
    bool IgnoredMorphTargets);
