using Editors.AnimationMeta.SuperView.Visualisation;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Events;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta;

public partial class EffectVfxPreviewTests
{
    [TestCase(6, false)]
    [TestCase(6, true)]
    [TestCase(8, false)]
    [TestCase(8, true)]
    [TestCase(9, false)]
    [TestCase(9, true)]
    [TestCase(10, false)]
    [TestCase(10, true)]
    public void IntermediateEffectDiagnostics_UseTheSharedTerrainTimeAndTransformRoutes(int version, bool tracking)
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var payload = EffectIntermediateVersionTests.DiagnosticPayload(version, tracking, out _);
        var source = EffectIntermediateVersionTests.DecodeDiagnostic(parser, version, payload);
        var effect = (IEffectMeta)source;
        effect.VfxName = "step_grass";
        effect.NodeIndex = -1;
        var serializedBefore = parser.Serialize(source, out _);
        var fixture = new TerrainResourceFixture();
        var pack = fixture.AddPack("diagnostic");
        fixture.SetTables(pack, "step", TerrainKeys(1, "ground"), ["ground"]);
        fixture.AddVfx(pack, "ground", "ground");
        var builder = fixture.CreateBuilder(out var factory);
        builder.EffectPreviewContext = new() { TerrainType = 1 };
        using var scene = new SceneManager(null!, null!, Mock.Of<IEventHub>());
        var root = scene.RootNode.AddObject(new GroupNode("actor") { ModelMatrix = Matrix.CreateRotationY(.3f) * Matrix.CreateTranslation(4, 2, -3) });
        var result = builder.Build(null, new ParsedMetadataFile { Version = 2, Attributes = [source] }, null,
            root, null!, new AnimationPlayer(), null);
        try
        {
            var instance = result.Instances.Single();
            instance.Update(1.25f);
            var marker = root.Children.Single();
            Assert.That(marker.Children, Has.Count.EqualTo(1), "The new decoded versions must use the DB terrain route instead of looking for the authored base XML.");
            var particles = (VfxPreviewSceneNode)marker.Children.Single();
            Assert.That(particles.PrepareFrame(marker.ModelMatrix), Is.True);
            var expected = EffectAttachmentTransform.Create(new Quaternion(effect.Orientation), effect.Position,
                -2.5f, false, Matrix.Identity, root.ModelMatrix);
            var preview = factory.Created.Single();
            AssertParameterMatrix(preview.Attachment, expected);
            AssertParameterMatrix(preview.History!(0), expected);
            Assert.Multiple(() =>
            {
                Assert.That(preview.Time, Is.EqualTo(1));
                Assert.That(preview.Snapshot!.SourcePath, Is.EqualTo(@"vfx\ground.xml"));
                Assert.That(source.Data, Is.SameAs(payload));
                Assert.That(parser.Serialize(source, out _), Is.EqualTo(serializedBefore));
            });
        }
        finally { foreach (var instance in result.Instances) instance.CleanUp(); }
    }
}
