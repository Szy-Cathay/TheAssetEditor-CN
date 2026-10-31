using System.Text;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta;

public partial class EffectVfxPreviewTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void InteractiveEffectBuilder_DefersDuplicateInspectionOnlyForTheExplicitHost(bool interactive)
    {
        // DIAG: invalid XML detects eager inspection without drawing or interpreting game behavior.
        var files = new Mock<IPackFileService>();
        var file = PackFile.CreateFromBytes("parameter_fixture.xml", Encoding.UTF8.GetBytes("DIAG invalid XML"));
        files.Setup(value => value.FindFile(@"vfx\parameter_fixture.xml", null)).Returns(file);
        files.Setup(value => value.GetFullPath(file, It.IsAny<PackFileContainer?>())).Returns(@"vfx\parameter_fixture.xml");
        files.Setup(value => value.GetAllPackfileContainers()).Returns([]);
        var live = new VfxPreviewDocumentStore();
        var factory = new CapturingFactory();
        var builder = new MetaDataBuilder(null!, null!, null!, files.Object, null!, factory, Mock.Of<IDeviceResolver>(), live)
        { EnableInteractiveVfxPreview = interactive };
        var source = ParameterEffect(11);
        var root = new GroupNode("actor");
        var result = builder.Build(null, new() { Version = 2, Attributes = [source] }, null, root, null!, new(), null);
        try
        {
            var node = (VfxPreviewSceneNode)root.Children.Single().Children.Single();
            Assert.Multiple(() =>
            {
                Assert.That(node.EnableInteractiveReload, Is.EqualTo(interactive));
                Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.ReasonKey == "Vfx.Preview.InvalidData"), Is.EqualTo(!interactive));
                Assert.That(factory.Created, Is.Empty);
            });
        }
        finally { foreach (var instance in result.Instances) instance.CleanUp(); }

        live.Publish(new object(), file, null);
        result = builder.Build(null, new() { Version = 2, Attributes = [source] }, null, root, null!, new(), null);
        try
        {
            Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.ReasonKey == "Vfx.Preview.LiveDocumentInvalid"), Is.True,
                "Explicitly invalid unsaved values remain an immediate diagnostic, even for the interactive host.");
        }
        finally { foreach (var instance in result.Instances) instance.CleanUp(); }
    }

    [Test]
    public void EffectTransformRefresh_ReusesPreparedResourcesAndReseeksTheOriginalPhase()
    {
        using var control = new TailPlaybackControl(.25f, 3);
        control.Factory.ShareResources = true;
        var phase = new AnimationPlaybackTime(1.25f, 1.25, 0, 0, false, false);
        control.Update(phase);
        var original = control.Factory.Created.Single();
        var marker = control.Marker;
        var node = control.Node;
        var before = original.History!(0);
        control.Source.Position = new(4, 1, -3);
        control.Source.Scale = 2;
        Assert.That(control.Effect.TryRefreshTransform(), Is.True);
        control.Update(phase);
        var refreshed = control.Factory.Created.Last();
        Assert.Multiple(() =>
        {
            Assert.That(control.Factory.Created, Has.Count.EqualTo(2));
            Assert.That(control.Factory.Created.Sum(preview => preview.ReloadCount), Is.EqualTo(1),
                "Only the original load may convert/upload; transform refresh uses the existing playback fork.");
            Assert.That(original.Disposed, Is.True);
            Assert.That(refreshed.Disposed, Is.False);
            Assert.That(control.Marker, Is.SameAs(marker));
            Assert.That(control.Node, Is.SameAs(node));
            Assert.That(refreshed.Time, Is.EqualTo(1));
            Assert.That(refreshed.EmissionEndTimeSeconds, Is.EqualTo(2.75f));
            Assert.That(refreshed.History!(0), Is.Not.EqualTo(before));
            Assert.That(refreshed.History(0), Is.EqualTo(Matrix.CreateScale(2) * Matrix.CreateTranslation(control.Source.Position)));
        });
        control.Dispose();
        Assert.That(control.Effect.TryRefreshTransform(), Is.False, "A stale editor callback cannot restart a cleaned Effect.");
        Assert.That(control.Factory.Created, Has.Count.EqualTo(2));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(7)]
    [TestCase(11)]
    [TestCase(12)]
    public void EffectTransformRefresh_UpdatesCurrentAndCachedHistoricalAttachmentsWithoutReplacingTheNode(int version)
    {
        var source = ParameterEffect(version);
        var effect = (IEffectMeta)source;
        var builder = ParameterBuilder(out var factory);
        var root = new GroupNode("actor");
        var result = builder.Build(null, new() { Version = 2, Attributes = [source] }, null, root, null!, new(), null);
        try
        {
            var instance = (EffectMetaInstance)result.Instances.Single();
            instance.Update(1.25f);
            var marker = root.Children.Single();
            var particles = (VfxPreviewSceneNode)marker.Children.Single();
            var before = particles.LocalAttachmentAtTime!(0);
            effect.Position = new(7, -2, 4);
            effect.Orientation = Quaternion.CreateFromYawPitchRoll(.6f, -.2f, .3f).ToVector4();
            var scaleProperty = source.GetType().GetProperty("Scale");
            scaleProperty?.SetValue(source, -1.5f);
            var scale = scaleProperty == null ? 1 : -1.5f;

            Assert.That(instance.TryRefreshTransform(), Is.True);
            instance.Update(1.25f);
            var expected = Matrix.CreateScale(scale) * Matrix.CreateFromQuaternion(new Quaternion(effect.Orientation))
                * Matrix.CreateTranslation(effect.Position);
            Assert.Multiple(() =>
            {
                Assert.That(root.Children.Single(), Is.SameAs(marker));
                Assert.That(marker.Children.Single(), Is.SameAs(particles));
                Assert.That(particles.LocalAttachmentAtTime!(0), Is.Not.EqualTo(before), "A previously cached sample must not retain the old transform.");
                Assert.That(particles.LocalAttachmentAtTime(0), Is.EqualTo(expected));
                Assert.That(particles.ModelMatrix, Is.EqualTo(Matrix.CreateScale(scale)));
                Assert.That(instance.FocusPosition, Is.EqualTo(effect.Position));
                Assert.That(particles.TimeSeconds, Is.EqualTo(version == 1 ? 1.25f : 1));
                Assert.That(factory.Created, Is.Empty, "Transform editing before first draw must not initialize a GPU preview.");
            });
        }
        finally { foreach (var instance in result.Instances) instance.CleanUp(); }
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(7)]
    [TestCase(11)]
    [TestCase(12)]
    public void EffectTransformRefresh_RefusesEveryOtherSerializedFieldChange(int version)
    {
        var source = ParameterEffect(version);
        var builder = ParameterBuilder(out _);
        var root = new GroupNode("actor");
        var result = builder.Build(null, new() { Version = 2, Attributes = [source] }, null, root, null!, new(), null);
        try
        {
            var instance = (EffectMetaInstance)result.Instances.Single();
            var fields = source.GetType().GetProperties().Where(property =>
                property.Name is not ("Position" or "Orientation" or "Scale")
                && property.GetCustomAttributes(typeof(MetaDataTagAttribute), true).Length != 0);
            foreach (var field in fields)
            {
                var original = field.GetValue(source);
                object changed = original switch
                {
                    string text => text + "_changed",
                    int integer => integer + 1,
                    bool flag => !flag,
                    float single => single + .25f,
                    _ => throw new InvalidOperationException($"Unexpected Effect field {field.Name}: {field.PropertyType}"),
                };
                field.SetValue(source, changed);
                Assert.That(instance.TryRefreshTransform(), Is.False, field.Name + " must preserve the normal rebuild and diagnostic path.");
                field.SetValue(source, original);
            }
            var originalName = source.Name;
            source.Name += "_changed";
            Assert.That(instance.TryRefreshTransform(), Is.False);
            source.Name = originalName;
            Assert.That(instance.TryRefreshTransform(), Is.True);
        }
        finally { foreach (var instance in result.Instances) instance.CleanUp(); }
    }
}
