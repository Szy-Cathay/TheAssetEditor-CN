using Editors.VfxEditor;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.Services;

namespace AssetEditorTests;

[TestClass]
public class VfxLateBoundsCameraTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LateBoundsRespectProjectionNavigationAndFrameTheUntouchedDefault(bool changedProjection)
    {
        using var camera = new ArcBallCamera(Mock.Of<IDeviceResolver>(), Mock.Of<IKeyboardComponent>(), Mock.Of<IMouseComponent>());
        var preview = new Mock<IVfxPreview>();
        BoundingBox? bounds = null;
        var complete = false;
        preview.SetupGet(value => value.EmitterCount).Returns(1);
        preview.SetupGet(value => value.TotalEmitterCount).Returns(1);
        preview.SetupGet(value => value.CycleDuration).Returns(8);
        preview.SetupGet(value => value.Diagnostics).Returns(Array.Empty<VfxPreviewDiagnostic>());
        preview.SetupGet(value => value.PreviewBounds).Returns(() => bounds);
        preview.Setup(value => value.PrepareInteractiveFrame(It.IsAny<Matrix>(), It.IsAny<Func<float, Matrix>>()))
            .Callback(() => { if (complete) bounds = new(new(10, 0, 10), new(20, 8, 20)); });
        var factory = new Mock<IVfxPreviewFactory>();
        factory.Setup(value => value.Create(It.IsAny<GraphicsDevice>())).Returns(preview.Object);
        using var component = new VfxPreviewComponent(factory.Object, Mock.Of<IDeviceResolver>(), null!);
        var core = new View3DCoreComponentSet(null!, null!, null!, null!, camera, null!, null!, null!, null!, null!, null!);
        using var scene = new VfxScenePreview(Mock.Of<IWpfGame>(), Mock.Of<IComponentInserter>(), core, component);
        scene.Start();
        component.Reload(new([], "DIAG-late-camera.xml", _ => null));
        component.Update(new(TimeSpan.FromSeconds(.1), TimeSpan.FromSeconds(.1)));
        if (changedProjection) camera.CurrentProjectionType = ProjectionType.Orthographic;
        complete = true;
        component.Update(new(TimeSpan.FromSeconds(.2), TimeSpan.FromSeconds(.1)));
        Assert.AreEqual(MathHelper.ToRadians(26), camera.Yaw);
        Assert.AreEqual(MathHelper.ToRadians(-24), camera.Pitch);
        if (changedProjection)
        {
            Assert.AreEqual(ProjectionType.Orthographic, camera.CurrentProjectionType);
            Assert.AreEqual(Vector3.Zero, camera.LookAt, "A projection-only navigation action must prevent the delayed automatic framing.");
            Assert.AreEqual(10, camera.Zoom);
        }
        else Assert.AreEqual(new Vector3(15, 4, 15), camera.LookAt, "The untouched default view still frames the completed range.");
    }
}
