using System.Runtime.CompilerServices;
using System.Text;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.RenderItems;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta;

public partial class EffectVfxPreviewTests
{
    [TestCase(16, false)]
    [TestCase(24, false)]
    [TestCase(16, true)]
    [TestCase(24, true)]
    public void ManyEffects_KeepEverySourceRuntimeAndDrawWhenOneMiddleEntryChanges(int count, bool sameResource)
    {
        var files = ManyCpuFiles();
        var entries = Enumerable.Range(0, count).Select(index => new Effect_v11
        {
            Name = "EFFECT", Version = 11, VfxName = sameResource || index % 2 == 0 ? "first" : "second",
            StartTime = index * .01f, EndTime = 3, Scale = index % 3 + 1, Position = new(index * 2, index, 0)
        }).ToArray();
        var factory = new CapturingFactory();
        var builder = new MetaDataBuilder(null!, null!, null!, files.Object, null!, factory, Mock.Of<IDeviceResolver>(), new());
        using var scene = new SceneManager(null!, null!, Mock.Of<IEventHub>());
        var root = scene.RootNode.AddObject(new GroupNode("many-effects"));
        var result = builder.Build(new() { Version = 10, Attributes = entries.Take(count / 2).Cast<ParsedMetadataAttribute>().ToList() },
            new() { Version = 10, Attributes = entries.Skip(count / 2).Cast<ParsedMetadataAttribute>().ToList() }, entries[^1], root, null!, new(), null);
        var instances = result.Instances.Cast<EffectMetaInstance>().ToArray();
        try
        {
            Assert.That(instances, Has.Length.EqualTo(count));
            var renderer = (RenderEngineComponent)RuntimeHelpers.GetUninitializedObject(typeof(RenderEngineComponent));
            var queue = new Dictionary<RenderBuckedId, List<IRenderItem>> { [RenderBuckedId.Normal] = [] };
            typeof(RenderEngineComponent).GetField("_renderItems", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(renderer, queue);
            for (var index = 0; index < count; index++)
            {
                Assert.That(instances[index].Source, Is.SameAs(entries[index]));
                instances[index].Update(1.2f);
                var marker = root.Children[index];
                ((VfxPreviewSceneNode)marker.Children.Single()).Render(renderer, marker.ModelMatrix);
            }
            Assert.That(queue[RenderBuckedId.Normal], Has.Count.EqualTo(count));
            Assert.That(queue[RenderBuckedId.Normal].Distinct(ReferenceEqualityComparer.Instance).Count(), Is.EqualTo(count));
            foreach (var draw in queue[RenderBuckedId.Normal])
                draw.Draw(null!, new(Matrix.Identity, Matrix.Identity, Vector3.Zero, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []), RenderingTechnique.Normal);
            Assert.That(factory.Created, Has.Count.EqualTo(count));
            for (var index = 0; index < count; index++)
            {
                Assert.That(factory.Created[index].Draws, Is.EqualTo(1));
                Assert.That(factory.Created[index].Time, Is.EqualTo(1.2f - entries[index].StartTime).Within(.00001f));
                Assert.That(factory.Created[index].Attachment.Translation, Is.EqualTo(entries[index].Position));
            }
            var middle = count / 2;
            var untouched = root.Children.Where((_, index) => index != middle).ToArray();
            entries[middle].Position += new Vector3(0, 5, 0);
            var replacement = builder.BuildPreview(entries[middle], MetaDataDocumentOwner.Animation, true, root, null!, new AnimationPlayer());
            Assert.That(replacement.Instance, Is.TypeOf<EffectMetaInstance>());
            instances[middle].CleanUp(); instances[middle] = (EffectMetaInstance)replacement.Instance!;
            instances[middle].Update(1.2f);
            var newMarker = root.Children.Single(marker => !untouched.Contains(marker));
            Assert.That(((VfxPreviewSceneNode)newMarker.Children.Single()).PrepareFrame(newMarker.ModelMatrix), Is.True);
            Assert.That(root.Children, Has.Count.EqualTo(count));
            foreach (var node in untouched) Assert.That(root.Children, Does.Contain(node));
            for (var index = 0; index < count; index++)
            {
                Assert.That(factory.Created[index].Disposed, Is.EqualTo(index == middle));
                Assert.That(factory.Created[index].Reloads, Is.EqualTo(1));
            }
        }
        finally { foreach (var instance in instances) instance.CleanUp(); }
        Assert.That(root.Children, Is.Empty);
        Assert.That(factory.Created.All(preview => preview.Disposed), Is.True);
    }

    [Test]
    public void ManyEffects_OneMissingResourceKeepsTheOtherFifteenIndependentInstances()
    {
        var entries = Enumerable.Range(0, 16).Select(index => new Effect_v11
            { Name = "EFFECT", Version = 11, VfxName = index == 7 ? "missing" : "first", EndTime = 3 }).ToArray();
        var root = new GroupNode("one-missing-effect");
        var builder = new MetaDataBuilder(null!, null!, null!, ManyCpuFiles().Object, null!, new CapturingFactory(), Mock.Of<IDeviceResolver>(), new());
        var result = builder.Build(null, new() { Version = 10, Attributes = entries.Cast<ParsedMetadataAttribute>().ToList() }, entries[7], root, null!, new(), null);
        try
        {
            Assert.That(result.Instances, Has.Count.EqualTo(16));
            Assert.That(result.Instances.OfType<EffectMetaInstance>().Count(), Is.EqualTo(15));
            Assert.That(result.Diagnostics.Any(item => ReferenceEquals(item.Source, entries[7]) && item.ReasonKey == "SuperView.Diagnostics.MissingEffect"), Is.True);
            foreach (var effect in result.Instances.OfType<EffectMetaInstance>())
            {
                effect.Update(1);
                var node = (VfxPreviewSceneNode)typeof(EffectMetaInstance).GetField("_particles",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(effect)!;
                Assert.That(node.PrepareFrame(node.Parent.ModelMatrix), Is.True);
            }
        }
        finally { foreach (var instance in result.Instances) instance.CleanUp(); }
        Assert.That(root.Children, Is.Empty);
    }

    private static Mock<IPackFileService> ManyCpuFiles()
    {
        // CPU-only identity/lifetime fixture; these bytes are never used for GPU acceptance.
        var resources = new[] { "first", "second" }.ToDictionary(name => @"vfx\" + name + ".xml",
            name => PackFile.CreateFromBytes(name + ".xml", Encoding.UTF8.GetBytes("<vfx revision=\"2\"><emitters/></vfx>")), StringComparer.OrdinalIgnoreCase);
        var files = new Mock<IPackFileService>();
        files.Setup(value => value.FindFile(It.IsAny<string>(), null)).Returns((string path, PackFileContainer? _) => resources.GetValueOrDefault(path.Replace('/', '\\')));
        files.Setup(value => value.GetFullPath(It.IsAny<PackFile>(), It.IsAny<PackFileContainer?>()))
            .Returns((PackFile file, PackFileContainer? _) => resources.Single(entry => ReferenceEquals(entry.Value, file)).Key);
        files.Setup(value => value.GetAllPackfileContainers()).Returns([]);
        return files;
    }
}
