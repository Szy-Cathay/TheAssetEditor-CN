using System.Text;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using GameWorld.Core.Animation;
using GameWorld.Core.Rendering;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta;

public partial class EffectVfxPreviewTests
{
    [TestCase(11, true, true)]
    [TestCase(12, true, true)]
    [TestCase(12, false, true)]
    [TestCase(11, false, false)]
    [TestCase(11, true, false)]
    public void PersistentLifetime_PointEffectUsesActualDrainInsteadOfTheDisplayCycle(
        int version, bool infinite, bool persistent)
    {
        var source = (ParsedMetadataAttribute)(version == 11
            ? (IEffectMeta)new Effect_v11 { Scale = 1 }
            : new Effect_v12 { Scale = 1 });
        source.Name = "EFFECT";
        source.Version = version;
        var effect = (IEffectMeta)source;
        effect.VfxName = "persistent_lifetime_diagnostic";
        effect.NodeIndex = -1;
        var range = (DecodedMetaEntryBase)source;
        range.StartTime = range.EndTime = 0;
        var document = new ParsedMetadataFile { Version = 2, Attributes = [source] };
        var bytes = Encoding.UTF8.GetBytes("<vfx revision=\"2\"><emitters/></vfx>");
        var file = PackFile.CreateFromBytes("persistent_lifetime_diagnostic.xml", bytes);
        var files = new Mock<IPackFileService>();
        files.Setup(value => value.FindFile(@"vfx\persistent_lifetime_diagnostic.xml", null)).Returns(file);
        files.Setup(value => value.GetFullPath(file, It.IsAny<PackFileContainer?>()))
            .Returns(@"vfx\persistent_lifetime_diagnostic.xml");
        files.Setup(value => value.GetAllPackfileContainers()).Returns([]);
        var factory = new LifetimePreviewFactory(infinite);
        var builder = new MetaDataBuilder(null!, null!, null!, files.Object, null!, factory,
            Mock.Of<IDeviceResolver>(), new());
        var root = new GroupNode("persistent-lifetime-control");
        var result = builder.Build(persistent ? document : null, persistent ? null : document,
            null, root, null!, new AnimationPlayer(), null);
        try
        {
            var instance = (EffectMetaInstance)result.Instances.Single();
            var marker = root.Children.Single();
            var particles = (VfxPreviewSceneNode)marker.Children.Single();
            instance.Update(.125f);
            Assert.That(marker.IsVisible, Is.True);
            Assert.That(particles.PrepareFrame(marker.ModelMatrix), Is.True);
            Assert.That(factory.Preview.CycleDuration, Is.EqualTo(8),
                "This finite display cycle is independent of the runtime's emission lifetime.");
            foreach (var time in new[] { 9f, 30f })
            {
                instance.Update(time);
                if (marker.IsVisible) particles.PrepareFrame(marker.ModelMatrix);
                instance.Update(time);
                Assert.That(marker.IsVisible, Is.EqualTo(infinite),
                    "This control has no owner clip. Only actual finite runtime drain can end its tail; a display-cycle estimate cannot.");
                if (infinite)
                {
                    Assert.That(particles.PrepareFrame(marker.ModelMatrix), Is.True);
                    Assert.That(factory.Preview.Time, Is.EqualTo(time), "The host must not restart a persistent emitter at the display-cycle boundary.");
                    Assert.That(factory.Preview.ParticleCount, Is.EqualTo(1));
                }
            }
            if (persistent && infinite)
            {
                instance.IsEnabled = false;
                instance.Update(30);
                Assert.That(marker.IsVisible, Is.False);
                instance.IsEnabled = true;
                instance.Update(30);
                Assert.That(marker.IsVisible, Is.True);
            }
            Assert.That(instance.ShowForEntireAnimation, Is.False);
            Assert.That(range.StartTime, Is.Zero);
            Assert.That(range.EndTime, Is.Zero);
            Assert.That(file.DataSource.ReadData(), Is.EqualTo(bytes));
        }
        finally { foreach (var instance in result.Instances) instance.CleanUp(); }
        Assert.That(factory.Preview.Disposed, Is.True);
    }

    private sealed class LifetimePreviewFactory(bool infinite) : IVfxPreviewFactory
    {
        public LifetimePreview Preview { get; } = new(infinite);
        public IVfxPreview Create(GraphicsDevice device) => Preview;
    }

    // CPU-only lifecycle control: emission lifetime and the editor's display cycle are independent inputs.
    private sealed class LifetimePreview(bool infinite) : IVfxPreview
    {
        public bool HasInfiniteEmitters => infinite;
        public float CycleDuration => 8;
        public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics => [];
        public int EmitterCount => 1;
        public int TotalEmitterCount => 1;
        public int ParticleCount => infinite || Time <= CycleDuration ? 1 : 0;
        public bool IsDrained => ParticleCount == 0;
        public float Time { get; private set; }
        public bool Disposed { get; private set; }
        public void Reload(VfxPreviewSnapshot snapshot) { }
        public void Advance(float seconds, Matrix attachment) => Seek(Time + seconds, attachment);
        public void Seek(float seconds, Matrix attachment) => Time = seconds;
        public void Draw(CommonShaderParameters parameters) { }
        public void Dispose() => Disposed = true;
    }
}
