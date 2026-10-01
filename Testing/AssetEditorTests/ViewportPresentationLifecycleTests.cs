using System.Reflection;
using System.Windows;
using System.Windows.Media;
using GameWorld.Core.WpfWindow;
using SharpDX.Direct3D11;

namespace AssetEditorTests;

[TestClass]
[DoNotParallelize]
public class ViewportPresentationLifecycleTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ClosingViewportKeepsSharedContextAndOtherViewportRendering(bool openSecondFirst)
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            var first = CreateViewport();
            PresentationProbe? second = null;
            try
            {
                var context = ((Device)first.GraphicsDevice.Handle).ImmediateContext;
                if (openSecondFirst) second = CreateViewport();
                first.Dispose();
                Assert.IsFalse(context.IsDisposed, "关闭预览不能释放由其他预览共享的绘图设备上下文。");
                second ??= CreateViewport();
                var renderingArgs = Activator.CreateInstance(typeof(RenderingEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
                    null, [TimeSpan.FromSeconds(1)], null);
                typeof(D3D11Host).GetMethod("OnRendering", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(second, [second, renderingArgs]);
                Assert.IsFalse(context.IsDisposed);
                var target = (Microsoft.Xna.Framework.Graphics.RenderTarget2D)typeof(D3D11Host)
                    .GetField("_sharedRenderTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(second)!;
                var pixels = new Microsoft.Xna.Framework.Color[target.Width * target.Height];
                target.GetData(pixels);
                // Bgr32 readback stores blue first and has no alpha channel.
                var expected = Microsoft.Xna.Framework.Color.CornflowerBlue;
                Assert.IsTrue(pixels.All(pixel => pixel.R == expected.B && pixel.G == expected.G && pixel.B == expected.R), "关闭其他预览后仍必须能绘制并显示正确的图像。");
            }
            finally { second?.Dispose(); first.Dispose(); }
        });
    }

    private static PresentationProbe CreateViewport()
    {
        var viewport = new PresentationProbe { Width = 32, Height = 32 };
        viewport.Measure(new Size(32, 32));
        viewport.Arrange(new Rect(0, 0, 32, 32));
        viewport.ForceEnsureCreated();
        return viewport;
    }

    private sealed class PresentationProbe : D3D11Host
    {
        protected override void Initialize() { }
        protected override void Render(Microsoft.Xna.Framework.GameTime time) => GraphicsDevice.Clear(Microsoft.Xna.Framework.Color.CornflowerBlue);
        protected override void Dispose(bool disposing) { }
    }
}
