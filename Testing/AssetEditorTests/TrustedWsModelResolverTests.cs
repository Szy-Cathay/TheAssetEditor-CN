using System.Text;
using Editors.AnimationVisualEditors.AnimationWorkbench;
using Moq;
using NUnit.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;

using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class TrustedWsModelResolverTests
{
    [OneTimeSetUp]
    public void InitializeLocalization() =>
        new LocalizationManager().LoadLanguage();

    [Test]
    public async Task Resolve_UsesEffectiveSourcesAndKeepsStaticAttachment()
    {
        var project = CreateContainer(
            "project",
            TrustedAnimationModelSourceRole.FolderProject);
        var reference = CreateContainer(
            "reference.pack",
            TrustedAnimationModelSourceRole.ReferencePack);
        var ca = CreateContainer(
            "data.pack",
            TrustedAnimationModelSourceRole.CaPack);
        var root = Add(project, @"models\character.wsmodel",
            CreateWsModel(
                [@"models\body.wsmodel", @"models\weapon.wsmodel"],
                []));
        Add(project, @"models\body.wsmodel",
            CreateWsModel(
                [@"models\body.rigid_model_v2"],
                [(0, 0, @"materials\body.xml")]));
        Add(reference, @"models\weapon.wsmodel",
            CreateWsModel(
                [@"models\weapon.rigid_model_v2"],
                [(0, 0, @"materials\weapon.xml")]));
        var effectiveBody = Add(project,
            @"models\body.rigid_model_v2",
            PackFile.CreateFromBytes("body.rigid_model_v2", [1]));
        var shadowedBody = Add(ca,
            @"models\body.rigid_model_v2",
            PackFile.CreateFromBytes("body.rigid_model_v2", [2]));
        var weapon = Add(ca,
            @"models\weapon.rigid_model_v2",
            PackFile.CreateFromBytes("weapon.rigid_model_v2", [3]));
        Add(reference, @"materials\body.xml",
            CreateMaterial(@"textures\body.dds"));
        Add(ca, @"materials\weapon.xml",
            CreateMaterial(@"textures\weapon.dds"));
        var effectiveBodyTexture = Add(project,
            @"textures\body.dds",
            PackFile.CreateFromBytes("body.dds", [4]));
        Add(ca, @"textures\body.dds",
            PackFile.CreateFromBytes("body.dds", [5]));
        Add(ca, @"textures\weapon.dds",
            PackFile.CreateFromBytes("weapon.dds", [6]));
        Add(ca, @"animations\skeletons\humanoid.anim",
            CreateSkeleton("humanoid"));
        var inspector = new Mock<ITrustedRigidModelInspector>();
        inspector.Setup(item => item.Inspect(effectiveBody))
            .Returns(CreateInspection("humanoid"));
        inspector.Setup(item => item.Inspect(shadowedBody))
            .Returns(CreateInspection("wrong"));
        inspector.Setup(item => item.Inspect(weapon))
            .Returns(CreateInspection(string.Empty));
        var resolver = new TrustedWsModelResolver(
            CreateService([ca, reference, project]).Object,
            inspector.Object);

        var result = await resolver.ResolveAsync(
            root,
            CancellationToken.None);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(result.IsSuccess, Is.True,
                result.Diagnostic);
            NUnitAssert.That(result.Resolution, Is.Not.Null);
            NUnitAssert.That(result.Resolution!.SkeletonGeometry,
                Is.SameAs(effectiveBody));
            NUnitAssert.That(result.Resolution.GeometryCount,
                Is.EqualTo(2));
            NUnitAssert.That(result.Resolution.StaticAttachmentCount,
                Is.EqualTo(1));
            NUnitAssert.That(result.Resolution.Dependencies.Any(item =>
                item.Kind == TrustedModelDependencyKind.Texture &&
                ReferenceEquals(item.File, effectiveBodyTexture)), Is.True);
            NUnitAssert.That(result.Resolution.Dependencies.Any(item =>
                ReferenceEquals(item.File, shadowedBody)), Is.False);
        });
    }

    [Test]
    public async Task Resolve_ParsesCompositeResourcesOffCallerThread()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        var inspectionThread = callerThread;
        var project = CreateContainer(
            "project",
            TrustedAnimationModelSourceRole.FolderProject);
        var root = Add(project, @"models\body.wsmodel",
            CreateWsModel(
                [@"models\body.rigid_model_v2"],
                [(0, 0, @"materials\body.xml")]));
        var geometry = Add(project, @"models\body.rigid_model_v2",
            PackFile.CreateFromBytes("body.rigid_model_v2", [1]));
        Add(project, @"materials\body.xml", CreateMaterial());
        Add(project, @"animations\skeletons\humanoid.anim",
            CreateSkeleton("humanoid"));
        var inspector = new Mock<ITrustedRigidModelInspector>();
        inspector.Setup(item => item.Inspect(geometry))
            .Callback(() => inspectionThread =
                Environment.CurrentManagedThreadId)
            .Returns(CreateInspection("humanoid"));
        var resolver = new TrustedWsModelResolver(
            CreateService([project]).Object,
            inspector.Object);

        var result = await resolver.ResolveAsync(
            root,
            CancellationToken.None);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(result.IsSuccess, Is.True,
                result.Diagnostic);
            NUnitAssert.That(inspectionThread,
                Is.Not.EqualTo(callerThread));
        });
    }

    [Test]
    public async Task Resolve_MissingGeometryNamesParentAndRequestedPath()
    {
        var project = CreateContainer(
            "project",
            TrustedAnimationModelSourceRole.FolderProject);
        var root = Add(project, @"models\missing.wsmodel",
            CreateWsModel([@"models\missing.rigid_model_v2"], []));
        var resolver = new TrustedWsModelResolver(
            CreateService([project]).Object,
            Mock.Of<ITrustedRigidModelInspector>());

        var result = await resolver.ResolveAsync(
            root,
            CancellationToken.None);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(result.IsSuccess, Is.False);
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"models\missing.wsmodel"));
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"models\missing.rigid_model_v2"));
            NUnitAssert.That(result.Diagnostic, Does.Contain("project"));
        });
    }

    [Test]
    public async Task Resolve_MissingMaterialAndTextureNeverReturnPartialGraph()
    {
        foreach (var missingTexture in new[] { false, true })
        {
            var project = CreateContainer(
                "project",
                TrustedAnimationModelSourceRole.FolderProject);
            var materialPath = @"materials\body.xml";
            var root = Add(project, @"models\body.wsmodel",
                CreateWsModel(
                    [@"models\body.rigid_model_v2"],
                    [(0, 0, materialPath)]));
            var geometry = Add(project,
                @"models\body.rigid_model_v2",
                PackFile.CreateFromBytes("body.rigid_model_v2", [1]));
            if (missingTexture)
            {
                Add(project, materialPath,
                    CreateMaterial(@"textures\missing.dds"));
            }
            Add(project, @"animations\skeletons\humanoid.anim",
                CreateSkeleton("humanoid"));
            var inspector = new Mock<ITrustedRigidModelInspector>();
            inspector.Setup(item => item.Inspect(geometry))
                .Returns(CreateInspection("humanoid"));
            var resolver = new TrustedWsModelResolver(
                CreateService([project]).Object,
                inspector.Object);

            var result = await resolver.ResolveAsync(
                root,
                CancellationToken.None);

            NUnitAssert.That(result.IsSuccess, Is.False);
            NUnitAssert.That(result.Resolution, Is.Null);
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(missingTexture
                    ? @"textures\missing.dds"
                    : materialPath));
        }
    }

    [Test]
    public async Task Resolve_MissingSkeletonNeverReturnsGeometry()
    {
        var project = CreateContainer(
            "project",
            TrustedAnimationModelSourceRole.FolderProject);
        var root = Add(project, @"models\body.wsmodel",
            CreateWsModel(
                [@"models\body.rigid_model_v2"],
                [(0, 0, @"materials\body.xml")]));
        var geometry = Add(project,
            @"models\body.rigid_model_v2",
            PackFile.CreateFromBytes("body.rigid_model_v2", [1]));
        Add(project, @"materials\body.xml", CreateMaterial());
        var inspector = new Mock<ITrustedRigidModelInspector>();
        inspector.Setup(item => item.Inspect(geometry))
            .Returns(CreateInspection("humanoid"));
        var resolver = new TrustedWsModelResolver(
            CreateService([project]).Object,
            inspector.Object);

        var result = await resolver.ResolveAsync(
            root,
            CancellationToken.None);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(result.IsSuccess, Is.False);
            NUnitAssert.That(result.Resolution, Is.Null);
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"animations\skeletons\humanoid.anim"));
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"models\body.rigid_model_v2"));
        });
    }

    [Test]
    public async Task Resolve_ValidatesTexturePathsFromUnknownSlots()
    {
        var project = CreateContainer(
            "project",
            TrustedAnimationModelSourceRole.FolderProject);
        var root = Add(project, @"models\body.wsmodel",
            CreateWsModel(
                [@"models\body.rigid_model_v2"],
                [(0, 0, @"materials\body.xml")]));
        var geometry = Add(project,
            @"models\body.rigid_model_v2",
            PackFile.CreateFromBytes("body.rigid_model_v2", [1]));
        Add(project, @"materials\body.xml",
            PackFile.CreateFromBytes(
                "body.xml",
                Encoding.UTF8.GetBytes(
                    "<material><name>weighted_standard_4</name>" +
                    "<textures><texture><slot>future_slot</slot>" +
                    "<source>textures\\future.dds</source>" +
                    "</texture></textures></material>")));
        Add(project, @"animations\skeletons\humanoid.anim",
            CreateSkeleton("humanoid"));
        var inspector = new Mock<ITrustedRigidModelInspector>();
        inspector.Setup(item => item.Inspect(geometry))
            .Returns(CreateInspection("humanoid"));
        var resolver = new TrustedWsModelResolver(
            CreateService([project]).Object,
            inspector.Object);

        var result = await resolver.ResolveAsync(
            root,
            CancellationToken.None);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(result.IsSuccess, Is.False);
            NUnitAssert.That(result.Resolution, Is.Null);
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"textures\future.dds"));
        });
    }

    [Test]
    public async Task Resolve_ReportsCompleteWsModelCycle()
    {
        var project = CreateContainer(
            "project",
            TrustedAnimationModelSourceRole.FolderProject);
        var first = Add(project, @"models\first.wsmodel",
            CreateWsModel([@"models\second.wsmodel"], []));
        Add(project, @"models\second.wsmodel",
            CreateWsModel([@"models\first.wsmodel"], []));
        var resolver = new TrustedWsModelResolver(
            CreateService([project]).Object,
            Mock.Of<ITrustedRigidModelInspector>());

        var result = await resolver.ResolveAsync(
            first,
            CancellationToken.None);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(result.IsSuccess, Is.False);
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"models\first.wsmodel"));
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"models\second.wsmodel"));
        });
    }

    [Test]
    public async Task Resolve_RejectsMultipleSkinnedSkeletonIdentities()
    {
        var project = CreateContainer(
            "project",
            TrustedAnimationModelSourceRole.FolderProject);
        var root = Add(project, @"models\conflict.wsmodel",
            CreateWsModel(
                [@"models\body.wsmodel", @"models\mount.wsmodel"],
                []));
        Add(project, @"models\body.wsmodel",
            CreateWsModel(
                [@"models\body.rigid_model_v2"],
                [(0, 0, @"materials\body.xml")]));
        Add(project, @"models\mount.wsmodel",
            CreateWsModel(
                [@"models\mount.rigid_model_v2"],
                [(0, 0, @"materials\mount.xml")]));
        var body = Add(project, @"models\body.rigid_model_v2",
            PackFile.CreateFromBytes("body.rigid_model_v2", [1]));
        var mount = Add(project, @"models\mount.rigid_model_v2",
            PackFile.CreateFromBytes("mount.rigid_model_v2", [2]));
        Add(project, @"materials\body.xml", CreateMaterial());
        Add(project, @"materials\mount.xml", CreateMaterial());
        var inspector = new Mock<ITrustedRigidModelInspector>();
        inspector.Setup(item => item.Inspect(body))
            .Returns(CreateInspection("humanoid"));
        inspector.Setup(item => item.Inspect(mount))
            .Returns(CreateInspection("horse"));
        var resolver = new TrustedWsModelResolver(
            CreateService([project]).Object,
            inspector.Object);

        var result = await resolver.ResolveAsync(
            root,
            CancellationToken.None);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(result.IsSuccess, Is.False);
            NUnitAssert.That(result.Diagnostic, Does.Contain("humanoid"));
            NUnitAssert.That(result.Diagnostic, Does.Contain("horse"));
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"models\body.rigid_model_v2"));
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"models\mount.rigid_model_v2"));
        });
    }

    [Test]
    public async Task ResolveVariantMesh_UsesEffectiveNestedGraphAndStaticParts()
    {
        var project = CreateContainer(
            "project",
            TrustedAnimationModelSourceRole.FolderProject);
        var reference = CreateContainer(
            "reference.pack",
            TrustedAnimationModelSourceRole.ReferencePack);
        var ca = CreateContainer(
            "data.pack",
            TrustedAnimationModelSourceRole.CaPack);
        var root = Add(reference, @"variants\root.variantmeshdefinition",
            CreateVariantMesh(
                @"models\body.wsmodel",
                [(@"models\weapon.rigid_model_v2", "root")],
                [@"variants\gear.variantmeshdefinition"]));
        Add(reference, @"models\body.wsmodel",
            CreateWsModel(
                [@"models\body.rigid_model_v2"],
                [(0, 0, @"materials\body.xml")]));
        var body = Add(ca, @"models\body.rigid_model_v2",
            PackFile.CreateFromBytes("body.rigid_model_v2", [1]));
        var weapon = Add(ca, @"models\weapon.rigid_model_v2",
            PackFile.CreateFromBytes("weapon.rigid_model_v2", [2]));
        var effectiveGear = Add(project,
            @"variants\gear.variantmeshdefinition",
            CreateVariantMesh(@"models\cape.rigid_model_v2", [], []));
        var shadowedGear = Add(ca,
            @"variants\gear.variantmeshdefinition",
            CreateVariantMesh(@"models\wrong.rigid_model_v2", [], []));
        var cape = Add(reference, @"models\cape.rigid_model_v2",
            PackFile.CreateFromBytes("cape.rigid_model_v2", [3]));
        Add(reference, @"materials\body.xml", CreateMaterial());
        Add(ca, @"animations\skeletons\humanoid.anim",
            CreateSkeleton("humanoid"));
        var inspector = new Mock<ITrustedRigidModelInspector>();
        inspector.Setup(item => item.Inspect(body))
            .Returns(CreateInspection("humanoid"));
        inspector.Setup(item => item.Inspect(weapon))
            .Returns(CreateInspection(string.Empty));
        inspector.Setup(item => item.Inspect(cape))
            .Returns(CreateInspection(string.Empty));
        var resolver = new TrustedWsModelResolver(
            CreateService([ca, reference, project]).Object,
            inspector.Object);

        var result = await resolver.ResolveAsync(
            root,
            CancellationToken.None);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(result.IsSuccess, Is.True,
                result.Diagnostic);
            NUnitAssert.That(result.Resolution?.GeometryCount,
                Is.EqualTo(3));
            NUnitAssert.That(result.Resolution?.StaticAttachmentCount,
                Is.EqualTo(2));
            NUnitAssert.That(result.Resolution?.Dependencies.Any(item =>
                ReferenceEquals(item.File, effectiveGear)), Is.True);
            NUnitAssert.That(result.Resolution?.Dependencies.Any(item =>
                ReferenceEquals(item.File, shadowedGear)), Is.False);
        });
    }

    [Test]
    public async Task ResolveVariantMesh_MissingChildNamesParentAndSource()
    {
        var project = CreateContainer(
            "project",
            TrustedAnimationModelSourceRole.FolderProject);
        var root = Add(project, @"variants\root.variantmeshdefinition",
            CreateVariantMesh(
                string.Empty,
                [],
                [@"variants\missing.variantmeshdefinition"]));
        var resolver = new TrustedWsModelResolver(
            CreateService([project]).Object,
            Mock.Of<ITrustedRigidModelInspector>());

        var result = await resolver.ResolveAsync(
            root,
            CancellationToken.None);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(result.IsSuccess, Is.False);
            NUnitAssert.That(result.Resolution, Is.Null);
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"variants\root.variantmeshdefinition"));
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"variants\missing.variantmeshdefinition"));
            NUnitAssert.That(result.Diagnostic, Does.Contain("project"));
        });
    }

    [Test]
    public async Task ResolveVariantMesh_MissingAttachmentPointBlocksGraph()
    {
        var project = CreateContainer(
            "project",
            TrustedAnimationModelSourceRole.FolderProject);
        var root = Add(project, @"variants\root.variantmeshdefinition",
            CreateVariantMesh(
                @"models\body.rigid_model_v2",
                [(@"models\weapon.rigid_model_v2", "missing_socket")],
                []));
        var body = Add(project, @"models\body.rigid_model_v2",
            PackFile.CreateFromBytes("body.rigid_model_v2", [1]));
        var weapon = Add(project, @"models\weapon.rigid_model_v2",
            PackFile.CreateFromBytes("weapon.rigid_model_v2", [2]));
        Add(project, @"animations\skeletons\humanoid.anim",
            CreateSkeleton("humanoid"));
        var inspector = new Mock<ITrustedRigidModelInspector>();
        inspector.Setup(item => item.Inspect(body))
            .Returns(CreateInspection("humanoid"));
        inspector.Setup(item => item.Inspect(weapon))
            .Returns(CreateInspection(string.Empty));
        var resolver = new TrustedWsModelResolver(
            CreateService([project]).Object,
            inspector.Object);

        var result = await resolver.ResolveAsync(
            root,
            CancellationToken.None);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(result.IsSuccess, Is.False);
            NUnitAssert.That(result.Resolution, Is.Null);
            NUnitAssert.That(result.Diagnostic,
                Does.Contain("missing_socket"));
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"variants\root.variantmeshdefinition#SLOT"));
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"animations\skeletons\humanoid.anim"));
        });
    }

    [Test]
    public async Task ResolveVariantMesh_ReportsCompleteReferenceCycle()
    {
        var project = CreateContainer(
            "project",
            TrustedAnimationModelSourceRole.FolderProject);
        var root = Add(project, @"variants\first.variantmeshdefinition",
            CreateVariantMesh(
                string.Empty,
                [],
                [@"variants\second.variantmeshdefinition"]));
        Add(project, @"variants\second.variantmeshdefinition",
            CreateVariantMesh(
                string.Empty,
                [],
                [@"variants\first.variantmeshdefinition"]));
        var resolver = new TrustedWsModelResolver(
            CreateService([project]).Object,
            Mock.Of<ITrustedRigidModelInspector>());

        var result = await resolver.ResolveAsync(
            root,
            CancellationToken.None);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(result.IsSuccess, Is.False);
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"variants\first.variantmeshdefinition"));
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"variants\second.variantmeshdefinition"));
        });
    }

    [Test]
    public async Task ResolveVariantMesh_RejectsSkeletonConflict()
    {
        var project = CreateContainer(
            "project",
            TrustedAnimationModelSourceRole.FolderProject);
        var root = Add(project, @"variants\conflict.variantmeshdefinition",
            CreateVariantMesh(
                @"models\body.rigid_model_v2",
                [(@"models\mount.rigid_model_v2", "root")],
                []));
        var body = Add(project, @"models\body.rigid_model_v2",
            PackFile.CreateFromBytes("body.rigid_model_v2", [1]));
        var mount = Add(project, @"models\mount.rigid_model_v2",
            PackFile.CreateFromBytes("mount.rigid_model_v2", [2]));
        var inspector = new Mock<ITrustedRigidModelInspector>();
        inspector.Setup(item => item.Inspect(body))
            .Returns(CreateInspection("humanoid"));
        inspector.Setup(item => item.Inspect(mount))
            .Returns(CreateInspection("horse"));
        var resolver = new TrustedWsModelResolver(
            CreateService([project]).Object,
            inspector.Object);

        var result = await resolver.ResolveAsync(
            root,
            CancellationToken.None);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(result.IsSuccess, Is.False);
            NUnitAssert.That(result.Diagnostic, Does.Contain("humanoid"));
            NUnitAssert.That(result.Diagnostic, Does.Contain("horse"));
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"models\body.rigid_model_v2"));
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"models\mount.rigid_model_v2"));
        });
    }

    [Test]
    public async Task ResolveVariantMesh_ValidatesEmbeddedMaterialTextures()
    {
        var project = CreateContainer(
            "project",
            TrustedAnimationModelSourceRole.FolderProject);
        var root = Add(project, @"variants\body.variantmeshdefinition",
            CreateVariantMesh(
                @"models\body.rigid_model_v2",
                [],
                []));
        var body = Add(project, @"models\body.rigid_model_v2",
            PackFile.CreateFromBytes("body.rigid_model_v2", [1]));
        Add(project, @"animations\skeletons\humanoid.anim",
            CreateSkeleton("humanoid"));
        var inspector = new Mock<ITrustedRigidModelInspector>();
        inspector.Setup(item => item.Inspect(body))
            .Returns(new TrustedRigidModelInspection(
                "humanoid",
                [new TrustedRigidModelMeshSlot(0, 0)],
                [@"textures\missing.dds"]));
        var resolver = new TrustedWsModelResolver(
            CreateService([project]).Object,
            inspector.Object);

        var result = await resolver.ResolveAsync(
            root,
            CancellationToken.None);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(result.IsSuccess, Is.False);
            NUnitAssert.That(result.Resolution, Is.Null);
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"textures\missing.dds"));
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"models\body.rigid_model_v2"));
        });
    }

    [Test]
    public async Task ResolveVariantMesh_BlankSkeletonDoesNotHideSkinnedMesh()
    {
        var project = CreateContainer(
            "project",
            TrustedAnimationModelSourceRole.FolderProject);
        var root = Add(project, @"variants\body.variantmeshdefinition",
            CreateVariantMesh(
                @"models\body.rigid_model_v2",
                [],
                []));
        var body = Add(project, @"models\body.rigid_model_v2",
            PackFile.CreateFromBytes("body.rigid_model_v2", [1]));
        var inspector = new Mock<ITrustedRigidModelInspector>();
        inspector.Setup(item => item.Inspect(body))
            .Returns(new TrustedRigidModelInspection(
                string.Empty,
                [new TrustedRigidModelMeshSlot(0, 0)],
                [],
                true));
        var resolver = new TrustedWsModelResolver(
            CreateService([project]).Object,
            inspector.Object);

        var result = await resolver.ResolveAsync(
            root,
            CancellationToken.None);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(result.IsSuccess, Is.False);
            NUnitAssert.That(result.Resolution, Is.Null);
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"models\body.rigid_model_v2"));
            NUnitAssert.That(result.Diagnostic,
                Does.Contain("顶点类型与骨架声明不一致"));
        });
    }

    [Test]
    public async Task ResolveVariantMesh_MissingWsModelMaterialBlocksGraph()
    {
        var project = CreateContainer(
            "project",
            TrustedAnimationModelSourceRole.FolderProject);
        var root = Add(project, @"variants\body.variantmeshdefinition",
            CreateVariantMesh(@"models\body.wsmodel", [], []));
        Add(project, @"models\body.wsmodel",
            CreateWsModel(
                [@"models\body.rigid_model_v2"],
                [(0, 0, @"materials\missing.xml")]));
        var geometry = Add(project, @"models\body.rigid_model_v2",
            PackFile.CreateFromBytes("body.rigid_model_v2", [1]));
        var inspector = new Mock<ITrustedRigidModelInspector>();
        inspector.Setup(item => item.Inspect(geometry))
            .Returns(CreateInspection("humanoid"));
        var resolver = new TrustedWsModelResolver(
            CreateService([project]).Object,
            inspector.Object);

        var result = await resolver.ResolveAsync(
            root,
            CancellationToken.None);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(result.IsSuccess, Is.False);
            NUnitAssert.That(result.Resolution, Is.Null);
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"materials\missing.xml"));
            NUnitAssert.That(result.Diagnostic,
                Does.Contain(@"models\body.wsmodel"));
        });
    }

    private static TrustedRigidModelInspection CreateInspection(
        string skeletonName) => new(
            skeletonName,
            [new TrustedRigidModelMeshSlot(0, 0)]);

    private static PackFile CreateWsModel(
        IReadOnlyList<string> geometries,
        IReadOnlyList<(int Lod, int Part, string Path)> materials)
    {
        var geometryXml = string.Concat(geometries.Select(path =>
            $"<geometry>{path}</geometry>"));
        var materialXml = string.Concat(materials.Select(item =>
            $"<material lod_index=\"{item.Lod}\" " +
            $"part_index=\"{item.Part}\">{item.Path}</material>"));
        var xml = $"<model>{geometryXml}<materials>" +
                  $"{materialXml}</materials></model>";
        return PackFile.CreateFromBytes(
            "model.wsmodel",
            Encoding.UTF8.GetBytes(xml));
    }

    private static PackFile CreateVariantMesh(
        string model,
        IReadOnlyList<(string Model, string AttachPoint)> inlineChildren,
        IReadOnlyList<string> references)
    {
        var modelAttribute = string.IsNullOrWhiteSpace(model)
            ? string.Empty
            : $" model=\"{model}\"";
        var childXml = string.Concat(inlineChildren.Select((item, index) =>
            $"<SLOT name=\"inline_{index}\" " +
            $"attach_point=\"{item.AttachPoint}\">" +
            $"<VARIANT_MESH model=\"{item.Model}\" /></SLOT>"));
        var referenceXml = string.Concat(references.Select((path, index) =>
            $"<SLOT name=\"reference_{index}\" attach_point=\"root\">" +
            $"<VARIANT_MESH_REFERENCE definition=\"{path}\" /></SLOT>"));
        var slotXml = childXml + referenceXml;
        var xml = $"<VARIANT_MESH{modelAttribute}>{slotXml}" +
                  "</VARIANT_MESH>";
        return PackFile.CreateFromBytes(
            "model.variantmeshdefinition",
            Encoding.UTF8.GetBytes(xml));
    }

    private static PackFile CreateMaterial(params string[] textures)
    {
        var textureXml = string.Concat(textures.Select(path =>
            "<texture><slot>s_base_colour</slot>" +
            $"<source>{path}</source></texture>"));
        var xml = "<material><name>weighted_standard_4</name>" +
                  $"<textures>{textureXml}</textures></material>";
        return PackFile.CreateFromBytes(
            "material.xml",
            Encoding.UTF8.GetBytes(xml));
    }

    private static PackFile CreateSkeleton(string skeletonName)
    {
        var frame = new AnimationFile.Frame();
        frame.Transforms.Add(new RmvVector3());
        frame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
        var part = new AnimationFile.AnimationPart();
        part.TranslationMappings.Add(
            new AnimationFile.AnimationBoneMapping(0));
        part.RotationMappings.Add(
            new AnimationFile.AnimationBoneMapping(0));
        part.DynamicFrames.Add(frame);
        var file = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                Version = 7,
                SkeletonName = skeletonName,
                FrameRate = 20,
                AnimationTotalPlayTimeInSec = 0.05f,
            },
            Bones =
            [
                new AnimationFile.BoneInfo
                {
                    Id = 0,
                    Name = "root",
                    ParentId = -1,
                },
            ],
            AnimationParts = [part],
        };
        return PackFile.CreateFromBytes(
            $"{skeletonName}.anim",
            AnimationFile.ConvertToBytes(file));
    }

    private static PackFileContainer CreateContainer(
        string name,
        TrustedAnimationModelSourceRole role)
    {
        var container = new PackFileContainer(name)
        {
            SystemFilePath = $@"C:\packs\{name}",
        };
        if (role == TrustedAnimationModelSourceRole.FolderProject)
            container.Role = PackFileContainerRole.ProjectWorkspace;
        else if (role == TrustedAnimationModelSourceRole.ReferencePack)
            container.Role = PackFileContainerRole.Reference;
        else
            container.IsCaPackFile = true;
        return container;
    }

    private static PackFile Add(
        PackFileContainer container,
        string path,
        PackFile file)
    {
        container.FileList[path.ToLowerInvariant()] = file;
        return file;
    }

    private static Mock<IPackFileService> CreateService(
        List<PackFileContainer> containers)
    {
        var owners = containers
            .SelectMany(container => container.FileList.Select(entry =>
                (entry.Value, entry.Key, container)))
            .GroupBy(item => item.Value)
            .ToDictionary(group => group.Key, group => group.First());
        var service = new Mock<IPackFileService>();
        service.Setup(candidate => candidate.GetAllPackfileContainers())
            .Returns(containers);
        service.Setup(candidate => candidate.GetFileEntriesSnapshot(
                It.IsAny<PackFileContainer>()))
            .Returns((PackFileContainer container) =>
                container.FileList.ToArray());
        service.Setup(candidate => candidate.GetPackFileContainer(
                It.IsAny<PackFile>()))
            .Returns((PackFile file) => owners[file].container);
        service.Setup(candidate => candidate.GetFullPath(
                It.IsAny<PackFile>(),
                null))
            .Returns((PackFile file, PackFileContainer? _) =>
                owners[file].Key);
        return service;
    }
}
