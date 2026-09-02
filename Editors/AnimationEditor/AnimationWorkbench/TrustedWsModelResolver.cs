using System.IO;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.WsModel;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public enum TrustedModelDependencyKind
{
    WsModel,
    Geometry,
    Material,
    Texture,
    Skeleton,
}

public sealed record TrustedModelDependency(
    TrustedModelDependencyKind Kind,
    PackFile File,
    string Path,
    string Source,
    string ParentPath,
    string ParentSource);

public sealed record TrustedRigidModelMeshSlot(
    int LodIndex,
    int PartIndex);

public sealed record TrustedRigidModelInspection(
    string SkeletonName,
    IReadOnlyList<TrustedRigidModelMeshSlot> MeshSlots);

public interface ITrustedRigidModelInspector
{
    TrustedRigidModelInspection Inspect(PackFile file);
}

public sealed class TrustedRigidModelInspector :
    ITrustedRigidModelInspector
{
    public TrustedRigidModelInspection Inspect(PackFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var model = ModelFactory.Create().Load(
            file.DataSource.ReadData());
        if (!string.Equals(
                model.Header.FileType,
                "RMV2",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Invalid RMV2 header.");
        }

        var slots = new List<TrustedRigidModelMeshSlot>();
        for (var lodIndex = 0;
             lodIndex < model.ModelList.Length;
             lodIndex++)
        {
            for (var partIndex = 0;
                 partIndex < model.ModelList[lodIndex].Length;
                 partIndex++)
            {
                slots.Add(new TrustedRigidModelMeshSlot(
                    lodIndex,
                    partIndex));
            }
        }

        return new TrustedRigidModelInspection(
            model.Header.SkeletonName.Trim(),
            slots);
    }
}

public sealed record TrustedWsModelResolution(
    PackFile RootModel,
    PackFile SkeletonGeometry,
    PackFile Skeleton,
    IReadOnlyList<TrustedModelDependency> Dependencies,
    int GeometryCount,
    int StaticAttachmentCount);

public sealed record TrustedWsModelResolutionResult(
    bool IsSuccess,
    TrustedWsModelResolution? Resolution,
    string Diagnostic)
{
    public static TrustedWsModelResolutionResult Success(
        TrustedWsModelResolution resolution) =>
        new(true, resolution, string.Empty);

    public static TrustedWsModelResolutionResult Failure(
        string diagnostic) => new(false, null, diagnostic);
}

public interface ITrustedWsModelResolver
{
    Task<TrustedWsModelResolutionResult> ResolveAsync(
        PackFile root,
        CancellationToken cancellationToken);
}

public sealed class TrustedWsModelResolver(
    IPackFileService packFileService,
    ITrustedRigidModelInspector rigidModelInspector) :
    ITrustedWsModelResolver
{
    public Task<TrustedWsModelResolutionResult> ResolveAsync(
        PackFile root,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        return Task.Run(
            () => Resolve(root, cancellationToken),
            cancellationToken);
    }

    private TrustedWsModelResolutionResult Resolve(
        PackFile root,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rootResource = CreateRootResource(root);
            if (!rootResource.Path.EndsWith(
                    ".wsmodel",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Failure(
                    "AnimationWorkbench.TrustedPreview.WsModelRootRequired",
                    rootResource.Path,
                    rootResource.Source);
            }

            var context = new ResolutionContext();
            ResolveWsModel(
                rootResource,
                context,
                cancellationToken);

            if (context.SkinnedGeometries.Count == 0)
            {
                return Failure(
                    "AnimationWorkbench.TrustedPreview.WsModelSkinnedGeometryMissing",
                    rootResource.Path,
                    rootResource.Source);
            }

            var skeletonNames = context.SkinnedGeometries
                .Select(item => item.Inspection.SkeletonName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (skeletonNames.Length != 1)
            {
                var conflicts = string.Join(
                    "; ",
                    context.SkinnedGeometries.Select(item =>
                        $"{item.Resource.Path}={item.Inspection.SkeletonName}"));
                return Failure(
                    "AnimationWorkbench.TrustedPreview.WsModelSkeletonConflict",
                    rootResource.Path,
                    rootResource.Source,
                    conflicts);
            }

            var skeletonName = skeletonNames[0];
            var skeletonPath = TrustedAnimationEffectiveResources.NormalizePath(
                $"animations\\skeletons\\{skeletonName}.anim");
            var skeletonResource = FindRequired(
                skeletonPath,
                context.SkinnedGeometries[0].Resource,
                TrustedModelDependencyKind.Skeleton);
            TrustedAnimationSkeletonIdentity skeletonIdentity;
            try
            {
                skeletonIdentity = TrustedAnimationSkeletonIdentity.Create(
                    skeletonResource.File,
                    skeletonResource.Path,
                    skeletonResource.Source);
            }
            catch (Exception exception)
            {
                return Failure(
                    "AnimationWorkbench.TrustedPreview.WsModelReferenceUnreadable",
                    skeletonResource.Path,
                    skeletonResource.Source,
                    context.SkinnedGeometries[0].Resource.Path,
                    context.SkinnedGeometries[0].Resource.Source,
                    exception.Message);
            }

            if (!string.Equals(
                    skeletonIdentity.Name,
                    skeletonName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Failure(
                    "AnimationWorkbench.TrustedPreview.WsModelSkeletonIdentityMismatch",
                    skeletonResource.Path,
                    skeletonResource.Source,
                    skeletonName,
                    skeletonIdentity.Name);
            }

            context.Dependencies.Add(CreateDependency(
                TrustedModelDependencyKind.Skeleton,
                skeletonResource,
                context.SkinnedGeometries[0].Resource));
            return TrustedWsModelResolutionResult.Success(
                new TrustedWsModelResolution(
                    root,
                    context.SkinnedGeometries[0].Resource.File,
                    skeletonResource.File,
                    context.Dependencies.ToArray(),
                    context.GeometryCount,
                    context.StaticAttachmentCount));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TrustedWsModelResolutionException exception)
        {
            return TrustedWsModelResolutionResult.Failure(
                exception.Message);
        }
        catch (Exception exception)
        {
            return Failure(
                "AnimationWorkbench.TrustedPreview.WsModelUnexpectedFailure",
                exception.Message);
        }
    }

    private void ResolveWsModel(
        Resource resource,
        ResolutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cycleStart = context.ActiveWsModels.FindIndex(path =>
            string.Equals(
                path,
                resource.Path,
                StringComparison.OrdinalIgnoreCase));
        if (cycleStart >= 0)
        {
            var cycle = context.ActiveWsModels
                .Skip(cycleStart)
                .Append(resource.Path);
            throw ResolutionFailure(
                "AnimationWorkbench.TrustedPreview.WsModelCycle",
                string.Join(" -> ", cycle));
        }

        context.ActiveWsModels.Add(resource.Path);
        try
        {
            WsModelFile model;
            try
            {
                model = new WsModelFile(resource.File);
            }
            catch (Exception exception)
            {
                throw ResolutionFailure(
                    "AnimationWorkbench.TrustedPreview.WsModelReferenceUnreadable",
                    resource.Path,
                    resource.Source,
                    resource.ParentPath,
                    resource.ParentSource,
                    exception.Message);
            }

            context.Dependencies.Add(new TrustedModelDependency(
                TrustedModelDependencyKind.WsModel,
                resource.File,
                resource.Path,
                resource.Source,
                resource.ParentPath,
                resource.ParentSource));
            var geometryPaths = GetGeometryPaths(model);
            if (geometryPaths.Count == 0)
            {
                throw ResolutionFailure(
                    "AnimationWorkbench.TrustedPreview.WsModelGeometryMissing",
                    resource.Path,
                    resource.Source);
            }

            if (geometryPaths.Count != 1 &&
                model.MaterialList.Count > 0)
            {
                throw ResolutionFailure(
                    "AnimationWorkbench.TrustedPreview.WsModelNestedMaterialsAmbiguous",
                    resource.Path,
                    resource.Source);
            }

            foreach (var geometryPath in geometryPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var geometryResource = FindRequired(
                    geometryPath,
                    resource,
                    geometryPath.EndsWith(
                        ".wsmodel",
                        StringComparison.OrdinalIgnoreCase)
                            ? TrustedModelDependencyKind.WsModel
                            : TrustedModelDependencyKind.Geometry);
                if (geometryPath.EndsWith(
                        ".wsmodel",
                        StringComparison.OrdinalIgnoreCase))
                {
                    ResolveWsModel(
                        geometryResource,
                        context,
                        cancellationToken);
                    continue;
                }

                if (!geometryPath.EndsWith(
                        ".rigid_model_v2",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw ResolutionFailure(
                        "AnimationWorkbench.TrustedPreview.WsModelGeometryTypeUnsupported",
                        geometryPath,
                        resource.Path,
                        resource.Source);
                }

                ResolveRigidModel(
                    geometryResource,
                    resource,
                    model,
                    context,
                    cancellationToken);
            }
        }
        finally
        {
            context.ActiveWsModels.RemoveAt(
                context.ActiveWsModels.Count - 1);
        }
    }

    private void ResolveRigidModel(
        Resource geometry,
        Resource wsModel,
        WsModelFile wsModelFile,
        ResolutionContext context,
        CancellationToken cancellationToken)
    {
        TrustedRigidModelInspection inspection;
        try
        {
            inspection = rigidModelInspector.Inspect(geometry.File);
        }
        catch (Exception exception)
        {
            throw ResolutionFailure(
                "AnimationWorkbench.TrustedPreview.WsModelReferenceUnreadable",
                geometry.Path,
                geometry.Source,
                wsModel.Path,
                wsModel.Source,
                exception.Message);
        }

        if (inspection.MeshSlots.Count == 0)
        {
            throw ResolutionFailure(
                "AnimationWorkbench.TrustedPreview.WsModelGeometryEmpty",
                geometry.Path,
                geometry.Source,
                wsModel.Path,
                wsModel.Source);
        }

        ValidateMaterials(
            wsModelFile,
            inspection,
            wsModel,
            context,
            cancellationToken);
        context.Dependencies.Add(CreateDependency(
            TrustedModelDependencyKind.Geometry,
            geometry,
            wsModel));
        context.GeometryCount++;
        if (string.IsNullOrWhiteSpace(inspection.SkeletonName))
        {
            context.StaticAttachmentCount++;
        }
        else
        {
            context.SkinnedGeometries.Add((geometry, inspection));
        }
    }

    private void ValidateMaterials(
        WsModelFile wsModelFile,
        TrustedRigidModelInspection inspection,
        Resource wsModel,
        ResolutionContext context,
        CancellationToken cancellationToken)
    {
        var entries = wsModelFile.MaterialList;
        var duplicate = entries.GroupBy(entry =>
                (entry.LodIndex, entry.PartIndex))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
        {
            throw ResolutionFailure(
                "AnimationWorkbench.TrustedPreview.WsModelMaterialDuplicate",
                wsModel.Path,
                wsModel.Source,
                duplicate.Key.LodIndex,
                duplicate.Key.PartIndex);
        }

        var unexpected = entries.FirstOrDefault(entry =>
            !inspection.MeshSlots.Any(slot =>
                slot.LodIndex == entry.LodIndex &&
                slot.PartIndex == entry.PartIndex));
        if (unexpected != null)
        {
            throw ResolutionFailure(
                "AnimationWorkbench.TrustedPreview.WsModelMaterialSlotUnexpected",
                wsModel.Path,
                wsModel.Source,
                unexpected.LodIndex,
                unexpected.PartIndex);
        }

        foreach (var slot in inspection.MeshSlots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries.FirstOrDefault(item =>
                item.LodIndex == slot.LodIndex &&
                item.PartIndex == slot.PartIndex);
            if (entry == null ||
                string.IsNullOrWhiteSpace(entry.MaterialPath))
            {
                throw ResolutionFailure(
                    "AnimationWorkbench.TrustedPreview.WsModelMaterialSlotMissing",
                    wsModel.Path,
                    wsModel.Source,
                    slot.LodIndex,
                    slot.PartIndex);
            }

            var material = FindRequired(
                entry.MaterialPath,
                wsModel,
                TrustedModelDependencyKind.Material);
            WsModelMaterialFile materialFile;
            try
            {
                materialFile = new WsModelMaterialFile(material.File);
            }
            catch (Exception exception)
            {
                throw ResolutionFailure(
                    "AnimationWorkbench.TrustedPreview.WsModelReferenceUnreadable",
                    material.Path,
                    material.Source,
                    wsModel.Path,
                    wsModel.Source,
                    exception.Message);
            }

            context.Dependencies.Add(CreateDependency(
                TrustedModelDependencyKind.Material,
                material,
                wsModel));
            foreach (var texturePath in materialFile.ReferencedTexturePaths
                         .Where(path => !string.IsNullOrWhiteSpace(path))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var texture = FindRequired(
                    texturePath,
                    material,
                    TrustedModelDependencyKind.Texture);
                context.Dependencies.Add(CreateDependency(
                    TrustedModelDependencyKind.Texture,
                    texture,
                    material));
            }
        }
    }

    private Resource FindRequired(
        string path,
        Resource parent,
        TrustedModelDependencyKind kind)
    {
        var normalizedPath = TrustedAnimationEffectiveResources.NormalizePath(
            path);
        var found = TrustedAnimationEffectiveResources.Find(
            packFileService,
            normalizedPath);
        if (found == null)
        {
            throw ResolutionFailure(
                "AnimationWorkbench.TrustedPreview.WsModelReferenceMissing",
                parent.Path,
                parent.Source,
                GetDependencyKindText(kind),
                normalizedPath);
        }

        return new Resource(
            found.File,
            found.Path,
            found.Container.Name,
            parent.Path,
            parent.Source);
    }

    private Resource CreateRootResource(PackFile root)
    {
        var owner = packFileService.GetPackFileContainer(root);
        var path = TrustedAnimationEffectiveResources.NormalizePath(
            packFileService.GetFullPath(root));
        return new Resource(
            root,
            path,
            owner?.Name ?? Localize(
                "AnimationWorkbench.TrustedPreview.SourceUnknown"),
            path,
            owner?.Name ?? Localize(
                "AnimationWorkbench.TrustedPreview.SourceUnknown"));
    }

    private static IReadOnlyList<string> GetGeometryPaths(
        WsModelFile model) => model.GeometryPaths.Count > 0
            ? model.GeometryPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(TrustedAnimationEffectiveResources.NormalizePath)
                .ToArray()
            : string.IsNullOrWhiteSpace(model.GeometryPath)
                ? []
                : [TrustedAnimationEffectiveResources.NormalizePath(
                    model.GeometryPath)];

    private static TrustedModelDependency CreateDependency(
        TrustedModelDependencyKind kind,
        Resource resource,
        Resource parent) => new(
            kind,
            resource.File,
            resource.Path,
            resource.Source,
            parent.Path,
            parent.Source);

    private static string GetDependencyKindText(
        TrustedModelDependencyKind kind) => Localize(kind switch
        {
            TrustedModelDependencyKind.WsModel =>
                "AnimationWorkbench.TrustedPreview.DependencyWsModel",
            TrustedModelDependencyKind.Geometry =>
                "AnimationWorkbench.TrustedPreview.DependencyGeometry",
            TrustedModelDependencyKind.Material =>
                "AnimationWorkbench.TrustedPreview.DependencyMaterial",
            TrustedModelDependencyKind.Texture =>
                "AnimationWorkbench.TrustedPreview.DependencyTexture",
            TrustedModelDependencyKind.Skeleton =>
                "AnimationWorkbench.TrustedPreview.DependencySkeleton",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        });

    private static TrustedWsModelResolutionResult Failure(
        string key,
        params object[] arguments) =>
        TrustedWsModelResolutionResult.Failure(
            string.Format(Localize(key), arguments));

    private static TrustedWsModelResolutionException ResolutionFailure(
        string key,
        params object[] arguments) => new(
            string.Format(Localize(key), arguments));

    private static string Localize(string key) =>
        LocalizationManager.Instance.Get(key);

    private sealed record Resource(
        PackFile File,
        string Path,
        string Source,
        string ParentPath,
        string ParentSource);

    private sealed class ResolutionContext
    {
        public List<string> ActiveWsModels { get; } = [];

        public List<TrustedModelDependency> Dependencies { get; } = [];

        public List<(Resource Resource,
            TrustedRigidModelInspection Inspection)> SkinnedGeometries
            { get; } = [];

        public int GeometryCount { get; set; }

        public int StaticAttachmentCount { get; set; }
    }

    private sealed class TrustedWsModelResolutionException(
        string message) : Exception(message);
}
