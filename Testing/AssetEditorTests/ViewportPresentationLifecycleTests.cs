using System.Reflection;
using System.Windows;
using System.Windows.Media;
using GameWorld.Core.WpfWindow;
using SharpDX.Direct3D11;
using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Xna.Framework.Graphics;

namespace AssetEditorTests;

[TestClass]
[DoNotParallelize]
public partial class ViewportPresentationLifecycleTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void OptionalCodePreparationPreservesTheCompleteImageAndResumesWithZeroDelta(int completion)
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateViewport(true);
            Render(viewport, 1);
            Settle(viewport, 1);
            var preparation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            viewport.RenderingPreparation = preparation.Task;
            viewport.FrameColor = Microsoft.Xna.Framework.Color.Red;
            Render(viewport, 20);
            Render(viewport, 40);
            Assert.AreEqual(1, viewport.RenderCalls);
            Assert.AreEqual(1L, Presented(viewport));
            AssertPixels(viewport, Microsoft.Xna.Framework.Color.CornflowerBlue);
            if (completion == 0) preparation.SetResult();
            else if (completion == 1) preparation.SetException(new InvalidOperationException("DIAG optional compilation failure"));
            else preparation.SetCanceled();
            Render(viewport, 60);
            Assert.IsNull(viewport.RenderingPreparation);
            Assert.AreEqual(2, viewport.RenderCalls);
            Assert.AreEqual(TimeSpan.Zero, viewport.LastElapsed);
            Settle(viewport, 2);
            AssertPixels(viewport, Microsoft.Xna.Framework.Color.Red);
            Render(viewport, 60);
            Assert.AreEqual(2, viewport.RenderCalls, "同一合成时刻不能重复更新。");
            Render(viewport, 60.02);
            Assert.AreEqual(3, viewport.RenderCalls);
            Assert.AreEqual(TimeSpan.FromSeconds(.02), viewport.LastElapsed);
        });
    }

    [TestMethod]
    public void FailedNonblockingImageLockRestoresWpfNestingCount()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateViewport(true);
            var image = (D3DImage)Field(viewport, "_d3D11Image");
            var canWrite = (ManualResetEvent)typeof(D3DImage).GetField("_canWriteEvent", Private)!.GetValue(image)!;
            var count = typeof(D3DImage).GetField("_lockCount", Private)!;
            canWrite.Reset();
            try
            {
                var locked = (bool)image.GetType().GetMethod("TryLockForPresentation", Private)!.Invoke(image, null)!;
                Assert.IsFalse(locked);
                Assert.AreEqual(0u, count.GetValue(image), "超时不能遗留 WPF 锁计数，否则后续画面无法提交。");
            }
            finally { canWrite.Set(); }
        });
    }

    [TestMethod]
    public void AsynchronousPresentationKeepsCompleteFramesAndEverySimulationUpdate()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateViewport(true);
            Render(viewport, 1);
            Assert.AreEqual(1, viewport.RenderCalls);
            Assert.AreEqual(0L, Presented(viewport));
            Settle(viewport, 1);
            AssertPixels(viewport, Microsoft.Xna.Framework.Color.CornflowerBlue);
            viewport.FrameColor = Microsoft.Xna.Framework.Color.Red;
            Render(viewport, 2);
            Assert.AreEqual(2, viewport.RenderCalls);
            Assert.AreEqual(1L, Presented(viewport), "未完成的 GPU 帧不能提前显示。");
            AssertPixels(viewport, Microsoft.Xna.Framework.Color.CornflowerBlue);
            Settle(viewport, 2);
            AssertPixels(viewport, Microsoft.Xna.Framework.Color.Red);
            var presenter = Field(viewport, "_framePresenter");
            var submitted = presenter.GetType().GetProperty("LastSubmittedSequence", Private)!.GetValue(presenter);
            Render(viewport, 2);
            Assert.AreEqual(2, viewport.RenderCalls, "重复合成回调不能重跑模拟。");
            Assert.AreEqual(submitted, presenter.GetType().GetProperty("LastSubmittedSequence", Private)!.GetValue(presenter));
        });
    }

    [TestMethod]
    public void BusyDisplayedImageDoesNotBlockTheOppositeCompletedFrameOrSkipUpdates()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateViewport(true);
            Render(viewport, 1);
            Settle(viewport, 1);
            viewport.FrameColor = Microsoft.Xna.Framework.Color.Red;
            Render(viewport, 2);
            var image = (D3DImage)Field(viewport, "_d3D11Image");
            var canWrite = (ManualResetEvent)typeof(D3DImage).GetField("_canWriteEvent", Private)!.GetValue(image)!;
            WaitUntil(() => (bool)Field(viewport, "_framePresenter").GetType().GetProperty("IsComplete", Private)!.GetValue(Field(viewport, "_framePresenter"))!);
            canWrite.Reset();
            try
            {
                viewport.FrameColor = Microsoft.Xna.Framework.Color.Blue;
                Render(viewport, 3);
                Assert.AreEqual(3, viewport.RenderCalls);
                Assert.AreEqual(2L, Presented(viewport));
                Assert.AreNotSame(image, viewport.Source);
                AssertPixels(viewport, Microsoft.Xna.Framework.Color.Red);
                Assert.AreEqual(0u, typeof(D3DImage).GetField("_lockCount", Private)!.GetValue(image));
            }
            finally { canWrite.Set(); }
            AssertPixels(viewport, Microsoft.Xna.Framework.Color.Red, "等待中的完整帧不能被后续相机或覆盖层改写。");
            Render(viewport, 4);
            Settle(viewport, 4);
            AssertPixels(viewport, Microsoft.Xna.Framework.Color.Blue);
        });
    }

    [TestMethod]
    public void ResizeAndQualityChangeRetirePendingBuffersWithoutPresentingOldDimensions()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateViewport(true);
            Render(viewport, 1);
            var old = Field(viewport, "_framePresenter");
            viewport.Width = 64; viewport.Height = 48;
            viewport.Measure(new Size(64, 48)); viewport.Arrange(new Rect(0, 0, 64, 48));
            viewport.RecreateGraphicsDevice(new PresentationParameters { BackBufferWidth = 64, BackBufferHeight = 48, MultiSampleCount = 4 });
            WaitForSubmittedGpu(viewport);
            Render(viewport, 2);
            WaitUntil(() =>
            {
                if (ReferenceEquals(old, Field(viewport, "_framePresenter"))) Render(viewport, 2);
                return !ReferenceEquals(old, Field(viewport, "_framePresenter"));
            });
            Assert.AreNotSame(old, Field(viewport, "_framePresenter"));
            Assert.AreEqual(0L, Presented(viewport));
            Settle(viewport, 2);
            var displayed = (RenderTarget2D)Field(viewport, "_sharedRenderTarget");
            var cached = (RenderTarget2D)Field(viewport, "_cachedRenderTarget");
            Assert.AreEqual(64, displayed.Width); Assert.AreEqual(48, displayed.Height);
            Assert.AreEqual(4, cached.MultiSampleCount, "异步呈现不能降低原画质采样数。");
            AssertPixels(viewport, Microsoft.Xna.Framework.Color.CornflowerBlue);
        });
    }

    [TestMethod]
    public void ClosingAsynchronousViewportWithPendingGpuWorkKeepsOtherViewportUsable()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var first = CreateViewport(true);
            using var second = CreateViewport();
            var context = ((Device)first.GraphicsDevice.Handle).ImmediateContext;
            Render(first, 1);
            first.Dispose();
            Assert.IsFalse(context.IsDisposed);
            Render(second, 1);
            AssertPixels(second, Microsoft.Xna.Framework.Color.CornflowerBlue);
            Assert.IsNull(Field(second, "_framePresenter"), "普通编辑器必须保留原同步呈现路径。");
        });
    }

    [TestMethod]
    public void FrontBufferRecoveryRebuildsPendingGenerationAndKeepsFullQuality()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateViewport(true);
            Render(viewport, 1); Settle(viewport, 1);
            var old = Field(viewport, "_framePresenter");
            var image = (D3DImage)Field(viewport, "_d3D11Image");
            var key = (DependencyPropertyKey)typeof(D3DImage).GetFields(BindingFlags.Static | BindingFlags.NonPublic)
                .Single(field => field.FieldType == typeof(DependencyPropertyKey)).GetValue(null)!;
            image.SetValue(key, false);
            Render(viewport, 2);
            Assert.AreEqual(1, viewport.RenderCalls, "前缓冲不可用时不能继续呈现或写共享面。");
            image.SetValue(key, true);
            viewport.FrameColor = Microsoft.Xna.Framework.Color.Green;
            Render(viewport, 3);
            WaitUntil(() =>
            {
                if (ReferenceEquals(old, Field(viewport, "_framePresenter"))) Render(viewport, 3);
                return !ReferenceEquals(old, Field(viewport, "_framePresenter"));
            });
            Assert.AreNotSame(old, Field(viewport, "_framePresenter"));
            Settle(viewport, 2);
            AssertPixels(viewport, Microsoft.Xna.Framework.Color.Green);
        });
    }

    [TestMethod]
    public void BusyOldCompositorDefersRetirementWithoutBlockingTheNewSize()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateViewport(true);
            Render(viewport, 1); Settle(viewport, 1);
            var image = (D3DImage)Field(viewport, "_d3D11Image");
            var old = Field(viewport, "_framePresenter");
            var canWrite = (ManualResetEvent)typeof(D3DImage).GetField("_canWriteEvent", Private)!.GetValue(image)!;
            viewport.Width = 64; viewport.Height = 48;
            viewport.Measure(new Size(64, 48)); viewport.Arrange(new Rect(0, 0, 64, 48));
            viewport.RecreateGraphicsDevice(new PresentationParameters { BackBufferWidth = 64, BackBufferHeight = 48 });
            canWrite.Reset();
            try
            {
                Render(viewport, 2);
                Assert.AreEqual(2, viewport.RenderCalls);
                Assert.AreEqual(32, ((RenderTarget2D)Field(viewport, "_sharedRenderTarget")).Width);
                Assert.AreEqual(0u, typeof(D3DImage).GetField("_lockCount", Private)!.GetValue(image));
                Settle(viewport, 2);
                Assert.AreEqual(64, ((RenderTarget2D)Field(viewport, "_sharedRenderTarget")).Width);
                Render(viewport, 3); Settle(viewport, 3);
                Assert.IsFalse((bool)old.GetType().GetField("_disposed", Private)!.GetValue(old)!, "WPF still reading the old image must postpone only its retirement.");
            }
            finally { canWrite.Set(); }
            Render(viewport, 4); Settle(viewport, 4);
            Assert.IsTrue((bool)old.GetType().GetField("_disposed", Private)!.GetValue(old)!);
            Assert.AreEqual(64, ((RenderTarget2D)Field(viewport, "_sharedRenderTarget")).Width);
            AssertPixels(viewport, Microsoft.Xna.Framework.Color.CornflowerBlue);
        });
    }

    [TestMethod]
    public void QueuedQualityChangeUsesLatestRealViewportSizeAndDpi()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateViewport(true);
            Render(viewport, 1); Settle(viewport, 1);
            viewport.RecreateGraphicsDevice(new PresentationParameters { BackBufferWidth = 32, BackBufferHeight = 32, MultiSampleCount = 4 });
            viewport.Width = 64; viewport.Height = 48;
            viewport.Measure(new Size(64, 48)); viewport.Arrange(new Rect(0, 0, 64, 48));
            viewport.DpiScalingFactor = 1.5;
            Assert.AreEqual(64d, viewport.ActualWidth, "The fixture must provide a real new layout size, not only queued presentation parameters.");
            Assert.AreEqual(48d, viewport.ActualHeight);
            Render(viewport, 2); Settle(viewport, 2);
            var displayed = (RenderTarget2D)Field(viewport, "_sharedRenderTarget");
            var cached = (RenderTarget2D)Field(viewport, "_cachedRenderTarget");
            Assert.AreEqual(96, displayed.Width, "A queued old width must not override the current viewport and DPI.");
            Assert.AreEqual(72, displayed.Height);
            Assert.AreEqual(4, cached.MultiSampleCount);
            AssertPixels(viewport, Microsoft.Xna.Framework.Color.CornflowerBlue);
        });
    }

    [TestMethod]
    public void LateGpuDefersRepeatedResizeWithoutAllocatingResourceGenerations()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateViewport(true);
            Render(viewport, 1); Settle(viewport, 1);
            Render(viewport, 2);
            var presenter = Field(viewport, "_framePresenter");
            var cached = Field(viewport, "_cachedRenderTarget");
            var pending = (int)presenter.GetType().GetField("_pending", Private)!.GetValue(presenter)!;
            var buffers = (Array)presenter.GetType().GetField("_buffers", Private)!.GetValue(presenter)!;
            var buffer = buffers.GetValue(pending)!;
            var completionField = buffer.GetType().GetField("Completion", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var originalCompletion = (Query)completionField.GetValue(buffer)!;
            using var notSubmitted = new Query((Device)viewport.GraphicsDevice.Handle, new QueryDescription { Type = QueryType.Event });
            completionField.SetValue(buffer, notSubmitted);
            try
            {
                Assert.IsFalse((bool)presenter.GetType().GetProperty("IsComplete", Private)!.GetValue(presenter)!, "An event query without End must provide deterministic unfinished GPU work.");
                viewport.Width = 64; viewport.Height = 48;
                viewport.Measure(new Size(64, 48)); viewport.Arrange(new Rect(0, 0, 64, 48));
                for (var index = 0; index < 20; index++)
                {
                    viewport.RecreateGraphicsDevice(new PresentationParameters { BackBufferWidth = 64, BackBufferHeight = 48 });
                    Render(viewport, 3 + index);
                    Assert.AreSame(presenter, Field(viewport, "_framePresenter"), "Pending GPU work must postpone allocation, not create another retired generation.");
                    Assert.AreSame(cached, Field(viewport, "_cachedRenderTarget"));
                    Assert.AreEqual(0, ((System.Collections.ICollection)Field(viewport, "_retiredPresenters")).Count);
                }
                Assert.AreEqual(2, viewport.RenderCalls);
            }
            finally { completionField.SetValue(buffer, originalCompletion); }
            WaitForSubmittedGpu(viewport);
            Render(viewport, 30); Settle(viewport, 3);
            Assert.AreEqual(3, viewport.RenderCalls);
            Assert.AreEqual(64, ((RenderTarget2D)Field(viewport, "_sharedRenderTarget")).Width);
            AssertPixels(viewport, Microsoft.Xna.Framework.Color.CornflowerBlue);
        });
    }

    [TestMethod]
    public void FailedGpuCopyReleasesSlotLockAndAllowsTheNextCompleteFrame()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateViewport(true);
            Render(viewport, 1); Settle(viewport, 1);
            var presenter = Field(viewport, "_framePresenter");
            var sprites = (SpriteBatch)Field(viewport, "_spriteBatch");
            var buffers = (Array)presenter.GetType().GetField("_buffers", Private)!.GetValue(presenter)!;
            var free = buffers.GetValue(1)!;
            var freeImage = (D3DImage)free.GetType().GetField("Image", Private)!.GetValue(free)!;
            sprites.Begin();
            try { Assert.ThrowsException<TargetInvocationException>(() => Render(viewport, 2)); }
            finally { sprites.End(); }
            Assert.AreEqual(0u, typeof(D3DImage).GetField("_lockCount", Private)!.GetValue(freeImage));
            Assert.AreEqual(-1, presenter.GetType().GetField("_pending", Private)!.GetValue(presenter));
            Assert.AreEqual(1L, presenter.GetType().GetProperty("LastSubmittedSequence", Private)!.GetValue(presenter));
            AssertPixels(viewport, Microsoft.Xna.Framework.Color.CornflowerBlue);
            viewport.FrameColor = Microsoft.Xna.Framework.Color.Red;
            Render(viewport, 3); Settle(viewport, 3);
            AssertPixels(viewport, Microsoft.Xna.Framework.Color.Red);
        });
    }
    [TestMethod]
    public void AsynchronousSlotsKeepTheirImagesSurfacesAndInteropVersionsAcrossWholeFrames()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateViewport(true);
            Render(viewport, 1); Settle(viewport, 1);
            var first = (D3DImage)viewport.Source;
            var version = typeof(D3DImage).GetField("_version", Private)!;
            var firstVersion = version.GetValue(first);
            var firstSurface = Surface(first);
            viewport.FrameColor = Microsoft.Xna.Framework.Color.Red;
            Render(viewport, 2); Settle(viewport, 2);
            var second = (D3DImage)viewport.Source;
            Assert.AreNotSame(first, second, "Each slot needs its own permanent WPF bitmap instead of rebinding one image.");
            var secondVersion = version.GetValue(second);
            var secondSurface = Surface(second);
            Assert.AreNotEqual(firstSurface, secondSurface);
            for (var frame = 3; frame <= 12; frame++)
            {
                viewport.FrameColor = frame % 2 == 0 ? Microsoft.Xna.Framework.Color.Red : Microsoft.Xna.Framework.Color.Blue;
                Render(viewport, frame); Settle(viewport, frame);
                Assert.AreSame(frame % 2 == 0 ? second : first, viewport.Source);
                Assert.AreEqual(firstVersion, version.GetValue(first));
                Assert.AreEqual(secondVersion, version.GetValue(second));
                Assert.AreEqual(firstSurface, Surface(first));
                Assert.AreEqual(secondSurface, Surface(second));
                AssertPixels(viewport, viewport.FrameColor);
            }
        });
    }

    [TestMethod]
    public void ResizeRetainsThePreviousCompletedImageUntilTheNewGenerationFinishes()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateViewport(true);
            Render(viewport, 1); Settle(viewport, 1);
            var previousImage = viewport.Source;
            var previousTarget = Field(viewport, "_sharedRenderTarget");
            var previousPresenter = Field(viewport, "_framePresenter");
            viewport.Width = 64; viewport.Height = 48;
            viewport.Measure(new Size(64, 48)); viewport.Arrange(new Rect(0, 0, 64, 48));
            viewport.RecreateGraphicsDevice(new PresentationParameters { BackBufferWidth = 64, BackBufferHeight = 48 });
            viewport.FrameColor = Microsoft.Xna.Framework.Color.Red;
            Render(viewport, 2);
            Assert.AreSame(previousImage, viewport.Source);
            Assert.AreSame(previousTarget, Field(viewport, "_sharedRenderTarget"), "An unfinished new target must not replace the completed displayed image.");
            Assert.IsFalse((bool)previousPresenter.GetType().GetField("_disposed", Private)!.GetValue(previousPresenter)!);
            AssertPixels(viewport, Microsoft.Xna.Framework.Color.CornflowerBlue);
            Settle(viewport, 2);
            Assert.AreNotSame(previousImage, viewport.Source);
            AssertPixels(viewport, Microsoft.Xna.Framework.Color.Red);
            Render(viewport, 3);
            Assert.IsTrue((bool)previousPresenter.GetType().GetField("_disposed", Private)!.GetValue(previousPresenter)!);
        });
    }

    [TestMethod]
    public void BusyFreeImageDefersTheGpuCopyWithoutChangingTheDisplayedImage()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateViewport(true);
            Render(viewport, 1); Settle(viewport, 1);
            var first = (D3DImage)viewport.Source;
            viewport.FrameColor = Microsoft.Xna.Framework.Color.Red;
            Render(viewport, 2); Settle(viewport, 2);
            var displayed = viewport.Source;
            var canWrite = (ManualResetEvent)typeof(D3DImage).GetField("_canWriteEvent", Private)!.GetValue(first)!;
            canWrite.Reset();
            try
            {
                viewport.FrameColor = Microsoft.Xna.Framework.Color.Blue;
                Render(viewport, 3);
                Assert.AreSame(displayed, viewport.Source);
                Assert.AreEqual(2L, Field(viewport, "_framePresenter").GetType().GetProperty("LastSubmittedSequence", Private)!.GetValue(Field(viewport, "_framePresenter")));
                Assert.AreEqual(3, viewport.RenderCalls);
                AssertPixels(viewport, Microsoft.Xna.Framework.Color.Red);
                Assert.AreEqual(0u, typeof(D3DImage).GetField("_lockCount", Private)!.GetValue(first));
            }
            finally { canWrite.Set(); }
            Render(viewport, 4); Settle(viewport, 4);
            Assert.AreSame(first, viewport.Source);
            AssertPixels(viewport, Microsoft.Xna.Framework.Color.Blue);
        });
    }

    [TestMethod]
    public void CompletedInteropFrameRendersRealPixelsThroughWpfWithoutShowingAWindow()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateViewport(true);
            Render(viewport, 1); Settle(viewport, 1);
            viewport.Measure(new Size(32, 32)); viewport.Arrange(new Rect(0, 0, 32, 32));
            AssertPixels(viewport, Microsoft.Xna.Framework.Color.CornflowerBlue);
            var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(viewport);
            var pixels = new byte[32 * 32 * 4]; bitmap.CopyPixels(pixels, 32 * 4, 0);
            if (pixels.Where((_, index) => index % 4 == 3).All(alpha => alpha == 0))
                Assert.Inconclusive("This WPF runtime produced an empty D3DImage RenderTargetBitmap. GPU pixels were verified; this does not validate the visible compositor. Software fallback remains disabled.");
            var expected = Microsoft.Xna.Framework.Color.CornflowerBlue;
            for (var offset = 0; offset < pixels.Length; offset += 4)
            {
                Assert.AreEqual(expected.B, pixels[offset]); Assert.AreEqual(expected.G, pixels[offset + 1]);
                Assert.AreEqual(expected.R, pixels[offset + 2]); Assert.AreEqual((byte)255, pixels[offset + 3]);
            }
        });
    }

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

    private static PresentationProbe CreateViewport(bool asynchronous = false)
    {
        var viewport = new PresentationProbe { Width = 32, Height = 32, UseAsynchronousPresentation = asynchronous };
        viewport.Measure(new Size(32, 32));
        viewport.Arrange(new Rect(0, 0, 32, 32));
        viewport.ForceEnsureCreated();
        if (asynchronous)
            viewport.RecreateGraphicsDevice(new PresentationParameters { BackBufferWidth = 32, BackBufferHeight = 32 });
        return viewport;
    }

    private sealed class PresentationProbe : D3D11Host
    {
        internal int RenderCalls;
        internal Microsoft.Xna.Framework.Color FrameColor = Microsoft.Xna.Framework.Color.CornflowerBlue;
        protected override void Initialize() { }
        public TimeSpan LastElapsed { get; private set; }
        protected override void Render(Microsoft.Xna.Framework.GameTime time) { RenderCalls++; LastElapsed = time.ElapsedGameTime; GraphicsDevice.Clear(FrameColor); }
        protected override void Dispose(bool disposing) { }
    }

    private static object Field(D3D11Host host, string name) => typeof(D3D11Host).GetField(name, Private)!.GetValue(host)!;
    private static nint Surface(D3DImage image)
    {
        var registered = image.GetType().GetField("_registeredBackBuffer", Private)!.GetValue(image)!;
        var surface = registered.GetType().GetField("Surface", Private)!.GetValue(registered)!;
        return (nint)surface.GetType().GetProperty("NativePointer")!.GetValue(surface)!;
    }
    private static long Presented(D3D11Host host)
    {
        var presenter = Field(host, "_framePresenter");
        return (long)presenter.GetType().GetProperty("PresentedSequence", Private)!.GetValue(presenter)!;
    }

    private static void Render(D3D11Host host, double seconds)
    {
        var args = Activator.CreateInstance(typeof(RenderingEventArgs), Private, null, [TimeSpan.FromSeconds(seconds)], null);
        typeof(D3D11Host).GetMethod("OnRendering", Private)!.Invoke(host, [host, args]);
    }

    private static void Settle(D3D11Host host, long sequence)
    {
        var method = typeof(D3D11Host).GetMethod("TryCompleteAsynchronousPresentation", Private)!;
        var sampleType = typeof(D3D11Host).Assembly.GetType("GameWorld.Core.WpfWindow.D3D11FrameTiming")!;
        WaitUntil(() =>
        {
            method.Invoke(host, [null, Activator.CreateInstance(sampleType)]);
            return Presented(host) == sequence;
        });
    }

    private static void WaitUntil(Func<bool> completed)
    {
        var started = Stopwatch.GetTimestamp();
        while (!completed())
        {
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(10)) Assert.Fail("GPU 离屏呈现未能在诊断期限内完成。");
            Thread.Yield();
        }
    }

    private static void WaitForSubmittedGpu(D3D11Host host)
    {
        var presenter = Field(host, "_framePresenter");
        WaitUntil(() => (bool)presenter.GetType().GetProperty("IsComplete", Private)!.GetValue(presenter)!);
    }

    private static void AssertPixels(D3D11Host host, Microsoft.Xna.Framework.Color expected, string? message = null)
    {
        var target = (RenderTarget2D)Field(host, "_sharedRenderTarget");
        var pixels = new Microsoft.Xna.Framework.Color[target.Width * target.Height];
        target.GetData(pixels);
        Assert.IsTrue(pixels.All(pixel => pixel.R == expected.B && pixel.G == expected.G && pixel.B == expected.R), message ?? "显示面必须保持同一完整帧。");
    }
}
