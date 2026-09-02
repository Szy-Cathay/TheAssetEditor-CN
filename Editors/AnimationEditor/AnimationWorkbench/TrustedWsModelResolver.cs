using System.IO;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.Vmd;
using Shared.GameFormats.WsModel;
using static Shared.GameFormats.Vmd.VariantMeshDefinition;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public enum TrustedModelDependencyKind
{
    VariantMeshDefinition,
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
    IReadOnlyList<TrustedRigidModelMeshSlot> MeshSlots,
    IReadOnlyList<string> EmbeddedTexturePaths,
    bool HasSkinnedVertices)
{
    public TrustedRigidModelInspection(
        string skeletonName,
        IReadOnlyList<TrustedRigidModelMeshSlot> meshSlots)
        : this(
            skeletonName,
            meshSlots,
            [],
            !string.IsNullOrWhiteSpace(skeletonName))
    {
    }

    public TrustedRigidModelInspection(
        string skeletonName,
        IReadOnlyList<TrustedRigidModelMeshSlot> meshSlots,
        IReadOnlyList<string> embeddedTexturePaths)
        : this(
            skeletonName,
            meshSlots,
            embeddedTexturePaths,
            !string.IsNullOrWhiteSpace(skeletonName))
    {
    }
}

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
            slots,
            model.ModelList
                .SelectMany(lod => lod)
                .SelectMany(mesh => mesh.Material.GetAllTextures())
                .Select(texture => texture.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            model.ModelList
                .SelectMany(lod => lod)
                .Any(mesh => mesh.Material.BinaryVertexFormat is
                    VertexFormat.Weighted or VertexFormat.Cinematic));
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
            var isWsModel = rootResource.Path.EndsWith(
                ".wsmodel",
                StringComparison.OrdinalIgnoreCase);
            var isVariantMesh = rootResource.Path.EndsWith(
                ".variantmeshdefinition",
                StringComparison.OrdinalIgnoreCase);
            if (!isWsModel && !isVariantMesh)
            {
                return Failure(
                    "AnimationWorkbench.TrustedPreview.WsModelRootRequired",
                    rootResource.Path,
                    rootResource.Source);
            }

            var context = new ResolutionContext();
            if (isWsModel)
            {
                ResolveWsModel(
                    rootResource,
                    context,
                    cancellationToken);
            }
            else
            {
                ResolveVariantMeshDefinition(
                    rootResource,
                    context,
                    cancellationToken);
            }

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

            var missingAttachment = context.Attachments.FirstOrDefault(item =>
                !skeletonIdentity.Bones.Any(bone =>
                    string.Equals(
                        bone.Name,
                        item.AttachmentPoint,
                        StringComparison.OrdinalIgnoreCase)));
            if (missingAttachment.Parent != null)
            {
                return Failure(
                    "AnimationWorkbench.TrustedPreview.VariantMeshAttachmentMissing",
                    missingAttachment.AttachmentPoint,
                    missingAttachment.Parent.Path,
                    missingAttachment.Parent.Source,
                    skeletonResource.Path,
                    skeletonResource.Source);
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
        var cycleStart = context.ActiveModels.FindIndex(path =>
            string.Equals(
                path,
                resource.Path,
                StringComparison.OrdinalIgnoreCase));
        if (cycleStart >= 0)
        {
            var cycle = context.ActiveModels
                .Skip(cycleStart)
                .Append(resource.Path);
            throw ResolutionFailure(
                "AnimationWorkbench.TrustedPreview.WsModelCycle",
                string.Join(" -> ", cycle));
        }

        context.ActiveModels.Add(resource.Path);
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
            context.ActiveModels.RemoveAt(
                context.ActiveModels.Count - 1);
        }
    }

    private void ResolveVariantMeshDefinition(
        Resource resource,
        ResolutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cycleStart = context.ActiveModels.FindIndex(path =>
            string.Equals(
                path,
                resource.Path,
                StringComparison.OrdinalIgnoreCase));
        if (cycleStart >= 0)
        {
            var cycle = context.ActiveModels
                .Skip(cycleStart)
                .Append(resource.Path);
            throw ResolutionFailure(
                "AnimationWorkbench.TrustedPreview.WsModelCycle",
                string.Join(" -> ", cycle));
        }

        context.ActiveModels.Add(resource.Path);
        try
        {
            VariantMesh model;
            try
            {
                model = VariantMeshDefinitionLoader.Load(
                    resource.File,
                    strict: true);
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
                TrustedModelDependencyKind.VariantMeshDefinition,
                resource.File,
                resource.Path,
                resource.Source,
                resource.ParentPath,
                resource.ParentSource));
            ResolveVariantMesh(
                model,
                resource,
                context,
                cancellationToken);
        }
        finally
        {
            context.ActiveModels.RemoveAt(
                context.ActiveModels.Count - 1);
        }
    }

    private void ResolveVariantMesh(
        VariantMesh model,
        Resource node,
        ResolutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hasContent = false;
        if (!string.IsNullOrWhiteSpace(model.ModelReference))
        {
            ResolveModelReference(
                model.ModelReference,
                node,
                context,
                cancellationToken);
            hasContent = true;
        }

        foreach (var decalPath in new[]
                 {
                     model.DecalDiffuse,
                     model.DecalNormal,
                 }.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var texture = FindRequired(
                decalPath,
                node,
                TrustedModelDependencyKind.Texture);
            context.Dependencies.Add(CreateDependency(
                TrustedModelDependencyKind.Texture,
                texture,
                node));
        }

        var slots = model.ChildSlots ?? [];
        for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var slot = slots[slotIndex];
            var slotName = string.IsNullOrWhiteSpace(slot.Name)
                ? slotIndex.ToString()
                : slot.Name;
            var slotPath = $"{node.Path}#SLOT[{slotName}]";
            var slotResource = new Resource(
                node.File,
                slotPath,
                node.Source,
                node.Path,
                node.Source);
            var inlineChildren = slot.ChildMeshes ?? [];
            var childReferences = slot.ChildReferences ?? [];
            if ((inlineChildren.Count > 0 || childReferences.Count > 0) &&
                !string.IsNullOrWhiteSpace(slot.AttachmentPoint))
            {
                context.Attachments.Add((
                    slot.AttachmentPoint.Trim(),
                    slotResource));
            }
            for (var childIndex = 0;
                 childIndex < inlineChildren.Count;
                 childIndex++)
            {
                var childResource = slotResource with
                {
                    Path = $"{slotPath}/VARIANT_MESH[{childIndex}]",
                    ParentPath = slotPath,
                };
                ResolveVariantMesh(
                    inlineChildren[childIndex],
                    childResource,
                    context,
                    cancellationToken);
                hasContent = true;
            }

            foreach (var childReference in childReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(childReference.Reference))
                {
                    throw ResolutionFailure(
                        "AnimationWorkbench.TrustedPreview.VariantMeshReferenceEmpty",
                        slotResource.Path,
                        slotResource.Source);
                }
                var reference = FindRequired(
                    childReference.Reference,
                    slotResource,
                    TrustedModelDependencyKind.VariantMeshDefinition);
                if (!reference.Path.EndsWith(
                        ".variantmeshdefinition",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw ResolutionFailure(
                        "AnimationWorkbench.TrustedPreview.WsModelGeometryTypeUnsupported",
                        reference.Path,
                        slotResource.Path,
                        slotResource.Source);
                }
                ResolveVariantMeshDefinition(
                    reference,
                    context,
                    cancellationToken);
                hasContent = true;
            }
        }

        if (!hasContent)
        {
            throw ResolutionFailure(
                "AnimationWorkbench.TrustedPreview.VariantMeshNodeEmpty",
                node.Path,
                node.Source);
        }
    }

    private void ResolveModelReference(
        string path,
        Resource parent,
        ResolutionContext context,
        CancellationToken cancellationToken)
    {
        var normalizedPath = TrustedAnimationEffectiveResources.NormalizePath(
            path);
        var kind = normalizedPath.EndsWith(
            ".wsmodel",
            StringComparison.OrdinalIgnoreCase)
                ? TrustedModelDependencyKind.WsModel
                : normalizedPath.EndsWith(
                    ".variantmeshdefinition",
                    StringComparison.OrdinalIgnoreCase)
                    ? TrustedModelDependencyKind.VariantMeshDefinition
                    : TrustedModelDependencyKind.Geometry;
        var resource = FindRequired(normalizedPath, parent, kind);
        if (normalizedPath.EndsWith(
                ".wsmodel",
                StringComparison.OrdinalIgnoreCase))
        {
            ResolveWsModel(resource, context, cancellationToken);
        }
        else if (normalizedPath.EndsWith(
                     ".variantmeshdefinition",
                     StringComparison.OrdinalIgnoreCase))
        {
            ResolveVariantMeshDefinition(
                resource,
                context,
                cancellationToken);
        }
        else if (normalizedPath.EndsWith(
                     ".rigid_model_v2",
                     StringComparison.OrdinalIgnoreCase))
        {
            ResolveEmbeddedRigidModel(
                resource,
                parent,
                context,
                cancellationToken);
        }
        else
        {
            throw ResolutionFailure(
                "AnimationWorkbench.TrustedPreview.WsModelGeometryTypeUnsupported",
                normalizedPath,
                parent.Path,
                parent.Source);
        }
    }

    private void ResolveEmbeddedRigidModel(
        Resource geometry,
        Resource parent,
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
                parent.Path,
                parent.Source,
                exception.Message);
        }

        if (inspection.MeshSlots.Count == 0)
        {
            throw ResolutionFailure(
                "AnimationWorkbench.TrustedPreview.WsModelGeometryEmpty",
                geometry.Path,
                geometry.Source,
                parent.Path,
                parent.Source);
        }

        foreach (var texturePath in inspection.EmbeddedTexturePaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var texture = FindRequired(
                texturePath,
                geometry,
                TrustedModelDependencyKind.Texture);
            context.Dependencies.Add(CreateDependency(
                TrustedModelDependencyKind.Texture,
                texture,
                geometry));
        }

        context.Dependencies.Add(CreateDependency(
            TrustedModelDependencyKind.Geometry,
            geometry,
            parent));
        AddGeometry(geometry, inspection, context);
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
        AddGeometry(geometry, inspection, context);
    }

    private static void AddGeometry(
        Resource geometry,
        TrustedRigidModelInspection inspection,
        ResolutionContext context)
    {
        context.GeometryCount++;
        var hasDeclaredSkeleton =
            !string.IsNullOrWhiteSpace(inspection.SkeletonName);
        if (inspection.HasSkinnedVertices != hasDeclaredSkeleton)
        {
            throw ResolutionFailure(
                "AnimationWorkbench.TrustedPreview.GeometrySkinningIdentityInvalid",
                geometry.Path,
                geometry.Source);
        }
        if (!inspection.HasSkinnedVertices)
            context.StaticAttachmentCount++;
        else
            context.SkinnedGeometries.Add((geometry, inspection));
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
            TrustedModelDependencyKind.VariantMeshDefinition =>
                "AnimationWorkbench.TrustedPreview.DependencyVariantMesh",
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
        public List<string> ActiveModels { get; } = [];

        public List<TrustedModelDependency> Dependencies { get; } = [];

        public List<(Resource Resource,
            TrustedRigidModelInspection Inspection)> SkinnedGeometries
            { get; } = [];

        public List<(string AttachmentPoint, Resource Parent)> Attachments
            { get; } = [];

        public int GeometryCount { get; set; }

        public int StaticAttachmentCount { get; set; }
    }

    private sealed class TrustedWsModelResolutionException(
        string message) : Exception(message);
}
