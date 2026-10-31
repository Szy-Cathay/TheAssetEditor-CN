using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using GameWorld.Core.WpfWindow;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharpDX.Direct3D11;
using Color = Microsoft.Xna.Framework.Color;

namespace AssetEditorTests;

[TestClass]
[DoNotParallelize]
public class ViewportDrawBackpressureTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestMethod]
    public void PendingGpuPreservesEveryUpdateAndElapsedTickWithoutQueuingInvisibleDraws()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateViewport(true);
            Render(viewport, 1); Settle(viewport, 1);
            Render(viewport, 2);
            var presenter = Field(viewport, "_framePresenter")!;
            var pending = (int)presenter.GetType().GetField("_pending", Private)!.GetValue(presenter)!;
            var buffer = ((Array)presenter.GetType().GetField("_buffers", Private)!.GetValue(presenter)!).GetValue(pending)!;
            var completion = buffer.GetType().GetField("Completion", Private)!;
            var actual = (Query)completion.GetValue(buffer)!;
            using var unfinished = new Query((Device)viewport.GraphicsDevice.Handle, new QueryDescription { Type = QueryType.Event });
            completion.SetValue(buffer, unfinished);
            try
            {
                for (var index = 1; index <= 60; index++) Render(viewport, 2 + index / 60d);
                Assert.AreEqual(62, viewport.UpdateCalls, "GPU 背压不能跳过输入、动画或粒子的 Update。");
                Assert.AreEqual(TimeSpan.FromSeconds(3), viewport.Elapsed, "所有原始时间增量必须保留。");
                Assert.AreEqual(3, viewport.DrawCalls, "两个待完成帧的有界容量满后不能继续排 GPU Draw。");
                Assert.AreEqual(3L, Submitted(viewport));
                Assert.AreEqual(1L, Presented(viewport));
                Assert.AreEqual(2, presenter.GetType().GetProperty("PendingFrameCount", Private)!.GetValue(presenter));
                AssertPixels(viewport, 1);
            }
            finally { completion.SetValue(buffer, actual); }
            Settle(viewport, 2);
            AssertPixels(viewport, 2);
            Render(viewport, 3.25); Settle(viewport, 4);
            Assert.AreEqual(63, viewport.UpdateCalls);
            Assert.AreEqual(4, viewport.DrawCalls);
            Assert.AreEqual(TimeSpan.FromSeconds(3.25), viewport.Elapsed);
            AssertPixels(viewport, 63);
        });
    }

    [TestMethod]
    public void BusyCompositorSlotPreservesUpdatesWithoutQueuingUnsubmittableDraws()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateViewport(true);
            Render(viewport, 1); Settle(viewport, 1);
            Render(viewport, 2); Settle(viewport, 2);
            var presenter = Field(viewport, "_framePresenter")!;
            var buffers = (Array)presenter.GetType().GetField("_buffers", Private)!.GetValue(presenter)!;
            var freeImages = buffers.Cast<object>().Select(buffer => (D3DImage)buffer.GetType().GetField("Image", Private)!.GetValue(buffer)!)
                .Where(image => !ReferenceEquals(image, viewport.Source)).ToArray();
            var canWrite = freeImages.Select(image => (ManualResetEvent)typeof(D3DImage).GetField("_canWriteEvent", Private)!.GetValue(image)!).ToArray();
            foreach (var available in canWrite) available.Reset();
            try
            {
                for (var index = 1; index <= 60; index++) Render(viewport, 2 + index / 60d);
                Assert.AreEqual(62, viewport.UpdateCalls);
                Assert.AreEqual(2, viewport.DrawCalls);
                Assert.AreEqual(TimeSpan.FromSeconds(3), viewport.Elapsed);
                Assert.AreEqual(2L, Submitted(viewport));
                Assert.AreEqual(2L, Presented(viewport));
                foreach (var image in freeImages) Assert.AreEqual(0u, typeof(D3DImage).GetField("_lockCount", Private)!.GetValue(image));
                AssertPixels(viewport, 2);
            }
            finally { foreach (var available in canWrite) available.Set(); }
            Render(viewport, 3.25); Settle(viewport, 3);
            Assert.AreEqual(63, viewport.UpdateCalls);
            Assert.AreEqual(3, viewport.DrawCalls);
            AssertPixels(viewport, 63);
        });
    }

    [TestMethod]
    public void ResizeWaitsForBothQueuedFramesWithoutDroppingUpdatesOrAllocatingGenerations()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateViewport(true);
            Render(viewport, 1); Settle(viewport, 1); Render(viewport, 2);
            var presenter = Field(viewport, "_framePresenter")!;
            var cached = Field(viewport, "_cachedRenderTarget");
            var buffer = Buffers(presenter)[(int)presenter.GetType().GetField("_pending", Private)!.GetValue(presenter)!];
            var completion = buffer.GetType().GetField("Completion", Private)!;
            var actual = (Query)completion.GetValue(buffer)!;
            using var unfinished = new Query((Device)viewport.GraphicsDevice.Handle, new QueryDescription { Type = QueryType.Event });
            completion.SetValue(buffer, unfinished);
            try
            {
                Render(viewport, 2.1);
                viewport.Width = 64; viewport.Height = 48;
                viewport.Measure(new Size(64, 48)); viewport.Arrange(new Rect(0, 0, 64, 48));
                for (var frame = 1; frame <= 30; frame++)
                {
                    viewport.RecreateGraphicsDevice(new PresentationParameters { BackBufferWidth = 64, BackBufferHeight = 48, MultiSampleCount = 4 });
                    Render(viewport, 2.1 + frame / 60d);
                    Assert.AreSame(presenter, Field(viewport, "_framePresenter"));
                    Assert.AreSame(cached, Field(viewport, "_cachedRenderTarget"));
                }
                Assert.AreEqual(33, viewport.UpdateCalls);
                Assert.AreEqual(3, viewport.DrawCalls);
                Assert.AreEqual(0, ((System.Collections.ICollection)Field(viewport, "_retiredPresenters")!).Count);
            }
            finally { completion.SetValue(buffer, actual); }
            WaitComplete(presenter);
            Render(viewport, 3); Settle(viewport, 4);
            Assert.AreEqual(34, viewport.UpdateCalls);
            Assert.AreEqual(4, viewport.DrawCalls);
            var target = (RenderTarget2D)Field(viewport, "_sharedRenderTarget")!;
            Assert.AreEqual(64, target.Width); Assert.AreEqual(48, target.Height);
            Assert.AreEqual(4, ((RenderTarget2D)Field(viewport, "_cachedRenderTarget")!).MultiSampleCount);
            AssertPixels(viewport, 34);
            Render(viewport, 3.1);
            Assert.IsTrue((bool)presenter.GetType().GetField("_disposed", Private)!.GetValue(presenter)!);
        });
    }

    [TestMethod]
    public void ClosingTwoQueuedFramesReleasesEveryImageLockAndKeepsTheSharedDevice()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            var viewport = CreateViewport(true);
            try
            {
                Render(viewport, 1);
                var presenter = Field(viewport, "_framePresenter")!;
                var buffers = Buffers(presenter);
                var first = buffers[(int)presenter.GetType().GetField("_pending", Private)!.GetValue(presenter)!];
                var completion = first.GetType().GetField("Completion", Private)!;
                var actual = (Query)completion.GetValue(first)!;
                using var unfinished = new Query((Device)viewport.GraphicsDevice.Handle, new QueryDescription { Type = QueryType.Event });
                completion.SetValue(first, unfinished);
                try { Render(viewport, 2); Assert.AreEqual(2, presenter.GetType().GetProperty("PendingFrameCount", Private)!.GetValue(presenter)); }
                finally { completion.SetValue(first, actual); }
                var context = ((Device)viewport.GraphicsDevice.Handle).ImmediateContext;
                viewport.Dispose();
                Assert.IsFalse(context.IsDisposed);
                foreach (var buffer in buffers)
                {
                    var image = (D3DImage)buffer.GetType().GetField("Image", Private)!.GetValue(buffer)!;
                    Assert.IsFalse((bool)buffer.GetType().GetField("WriteLocked", Private)!.GetValue(buffer)!);
                    Assert.AreEqual(0u, typeof(D3DImage).GetField("_lockCount", Private)!.GetValue(image));
                }
            }
            finally { viewport.Dispose(); }
        });
    }

    [TestMethod]
    public void FailedGpuCopyReleasesTheReservedSlotBeforeTheNextFrame()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateViewport(true);
            Render(viewport, 1); Settle(viewport, 1);
            var presenter = Field(viewport, "_framePresenter")!;
            var sprites = (SpriteBatch)Field(viewport, "_spriteBatch")!;
            sprites.Begin();
            try { Assert.ThrowsException<TargetInvocationException>(() => Render(viewport, 2)); }
            finally { sprites.End(); }
            Assert.AreEqual(0, presenter.GetType().GetProperty("PendingFrameCount", Private)!.GetValue(presenter));
            foreach (var buffer in Buffers(presenter))
            {
                var image = (D3DImage)buffer.GetType().GetField("Image", Private)!.GetValue(buffer)!;
                Assert.AreEqual(0u, typeof(D3DImage).GetField("_lockCount", Private)!.GetValue(image));
            }
            AssertPixels(viewport, 1);
            Render(viewport, 3); Settle(viewport, 3);
            AssertPixels(viewport, 3);
        });
    }

    [TestMethod]
    public void OrdinarySynchronousViewportStillUpdatesAndDrawsEveryCallback()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateViewport(false);
            for (var frame = 1; frame <= 5; frame++) Render(viewport, frame);
            Assert.AreEqual(5, viewport.UpdateCalls);
            Assert.AreEqual(5, viewport.DrawCalls);
            Assert.AreEqual(TimeSpan.FromSeconds(5), viewport.Elapsed);
            Assert.IsNull(Field(viewport, "_framePresenter"));
            AssertPixels(viewport, 5);
        });
    }

    private static Probe CreateViewport(bool asynchronous)
    {
        var viewport = new Probe { Width = 32, Height = 32, UseAsynchronousPresentation = asynchronous };
        viewport.Measure(new Size(32, 32)); viewport.Arrange(new Rect(0, 0, 32, 32)); viewport.ForceEnsureCreated();
        if (asynchronous) viewport.RecreateGraphicsDevice(new PresentationParameters { BackBufferWidth = 32, BackBufferHeight = 32 });
        return viewport;
    }
    private sealed class Probe() : WpfGame(null!, null!, null!)
    {
        internal int UpdateCalls, DrawCalls;
        internal TimeSpan Elapsed;
        protected override void Initialize() { }
        protected override void Update(GameTime time) { UpdateCalls++; Elapsed += time.ElapsedGameTime; }
        protected override void Draw(GameTime time) { DrawCalls++; GraphicsDevice.Clear(new Color(UpdateCalls, 40, 80)); }
    }
    private static object? Field(D3D11Host host, string name) => typeof(D3D11Host).GetField(name, Private)!.GetValue(host);
    private static object[] Buffers(object presenter) => ((Array)presenter.GetType().GetField("_buffers", Private)!.GetValue(presenter)!).Cast<object>().ToArray();
    private static void WaitComplete(object presenter)
    {
        var started = Stopwatch.GetTimestamp();
        while (!(bool)presenter.GetType().GetProperty("IsComplete", Private)!.GetValue(presenter)!)
        {
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(10)) Assert.Fail("两个有界 GPU 帧未完成。");
            Thread.Yield();
        }
    }
    private static long Submitted(D3D11Host host) => Sequence(host, "LastSubmittedSequence");
    private static long Presented(D3D11Host host) => Sequence(host, "PresentedSequence");
    private static long Sequence(D3D11Host host, string name)
    {
        var presenter = Field(host, "_framePresenter")!;
        return (long)presenter.GetType().GetProperty(name, Private)!.GetValue(presenter)!;
    }
    private static void Render(D3D11Host host, double seconds)
    {
        var args = Activator.CreateInstance(typeof(RenderingEventArgs), Private, null, [TimeSpan.FromSeconds(seconds)], null);
        typeof(D3D11Host).GetMethod("OnRendering", Private)!.Invoke(host, [host, args]);
    }
    private static void Settle(D3D11Host host, long sequence)
    {
        var method = typeof(D3D11Host).GetMethod("TryCompleteAsynchronousPresentation", Private)!;
        var timing = typeof(D3D11Host).Assembly.GetType("GameWorld.Core.WpfWindow.D3D11FrameTiming")!;
        var started = Stopwatch.GetTimestamp();
        while (Presented(host) != sequence)
        {
            method.Invoke(host, [null, Activator.CreateInstance(timing)]);
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(10)) Assert.Fail("完整 GPU 帧未就绪。");
            Thread.Yield();
        }
    }
    private static void AssertPixels(D3D11Host host, int updates)
    {
        var target = (RenderTarget2D)Field(host, "_sharedRenderTarget")!;
        var pixels = new Color[target.Width * target.Height]; target.GetData(pixels);
        Assert.IsTrue(pixels.All(pixel => pixel.R == 80 && pixel.G == 40 && pixel.B == updates), "必须显示最新完整模拟阶段，不能显示未完成或旧覆盖层。");
    }
}
