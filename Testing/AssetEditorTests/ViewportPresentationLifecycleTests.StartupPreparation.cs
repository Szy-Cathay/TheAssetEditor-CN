using System.Windows;
using System.Windows.Controls;
using GameWorld.Core.WpfWindow;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AssetEditorTests;

public partial class ViewportPresentationLifecycleTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PendingCodePreparationBuildsKnownFirstTargetsWithoutUpdatingDrawingOrPublishing(bool arrangedSize)
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateStartupViewport(arrangedSize);
            var preparation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            viewport.RenderingPreparation = preparation.Task;
            var originalSource = viewport.Source;
            var originalTarget = Field(viewport, "_cachedRenderTarget");
            Render(viewport, 20);
            var target = (RenderTarget2D)Field(viewport, "_cachedRenderTarget");
            var presenter = Field(viewport, "_framePresenter");
            Assert.AreNotSame(originalTarget, target);
            Assert.AreEqual(64, target.Width); Assert.AreEqual(48, target.Height);
            Assert.AreEqual(0, viewport.Updates); Assert.AreEqual(0, viewport.Draws);
            Assert.AreSame(originalSource, viewport.Source);
            Assert.AreEqual(0L, Field(viewport, "_renderSequence"));
            Assert.AreEqual(0L, presenter.GetType().GetProperty("LastSubmittedSequence", Private)!.GetValue(presenter));
            Assert.AreEqual(0, presenter.GetType().GetProperty("PendingFrameCount", Private)!.GetValue(presenter));
            Assert.AreEqual(3, ((Array)presenter.GetType().GetField("_buffers", Private)!.GetValue(presenter)!).Length);
            Assert.AreEqual(false, Field(viewport, "_resetBackBuffer"));
            Render(viewport, 40);
            Assert.AreSame(target, Field(viewport, "_cachedRenderTarget"), "Repeated preparation callbacks must not allocate generations.");
            Assert.AreSame(originalSource, viewport.Source);
            Assert.AreEqual(0, viewport.Updates); Assert.AreEqual(0, viewport.Draws);
            preparation.SetResult(); Render(viewport, 60);
            Assert.AreSame(target, Field(viewport, "_cachedRenderTarget"), "Ready must use the prepared generation without rebuilding.");
            Assert.AreEqual(1, viewport.Updates); Assert.AreEqual(1, viewport.Draws);
            Assert.AreEqual(TimeSpan.Zero, viewport.LastElapsed);
            Settle(viewport, 1); AssertPixels(viewport, Color.CornflowerBlue);
            Render(viewport, 60.02);
            Assert.AreEqual(TimeSpan.FromSeconds(.02), viewport.LastElapsed);
        });
    }

    [TestMethod]
    public void PrebuiltStartupTargetsRetainTheNormalLatestSizeDpiAndQualityRebuild()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateStartupViewport(true);
            // DIAG: Image needs a nonempty natural size before any game frame has been published.
            var placeholder = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96,
                System.Windows.Media.PixelFormats.Bgr32, null, new byte[4], 4);
            placeholder.Freeze();
            viewport.Source = placeholder;
            var preparation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            viewport.RenderingPreparation = preparation.Task;
            var originalSource = viewport.Source;
            Render(viewport, 20);
            var prepared = Field(viewport, "_cachedRenderTarget");
            viewport.RecreateGraphicsDevice(new PresentationParameters { BackBufferWidth = 20, BackBufferHeight = 20, MultiSampleCount = 4 });
            var layoutRoot = new Grid { Width = 80, Height = 60 };
            layoutRoot.Children.Add(viewport);
            viewport.Width = 80; viewport.Height = 60;
            viewport.DpiScalingFactor = 1.5;
            layoutRoot.Measure(new Size(80, 60)); layoutRoot.Arrange(new Rect(0, 0, 80, 60));
            Assert.AreEqual(80d, viewport.ActualWidth, $"Complete the real WPF layout: root {layoutRoot.ActualWidth}x{layoutRoot.ActualHeight}, desired {layoutRoot.DesiredSize}, child {viewport.Width}x{viewport.Height}, visibility {viewport.Visibility}, valid {viewport.IsMeasureValid}/{viewport.IsArrangeValid}.");
            Assert.AreEqual(60d, viewport.ActualHeight);
            Render(viewport, 40);
            Assert.AreEqual(80d, viewport.ActualWidth);
            Assert.AreEqual(60d, viewport.ActualHeight);
            Assert.AreSame(prepared, Field(viewport, "_cachedRenderTarget"), "Later invalidations must retain the original normal rebuild route.");
            Assert.AreSame(originalSource, viewport.Source);
            Assert.AreEqual(true, Field(viewport, "_resetBackBuffer"));
            preparation.SetResult(); Render(viewport, 60);
            var latest = (RenderTarget2D)Field(viewport, "_cachedRenderTarget");
            Assert.AreNotSame(prepared, latest);
            Assert.AreEqual(120, latest.Width); Assert.AreEqual(90, latest.Height); Assert.AreEqual(4, latest.MultiSampleCount);
            Assert.AreEqual(TimeSpan.Zero, viewport.LastElapsed);
            Assert.AreSame(originalSource, viewport.Source, "A rebuilt generation cannot replace the complete image before GPU readiness.");
            Settle(viewport, 1); AssertPixels(viewport, Color.CornflowerBlue);
        });
    }

    [TestMethod]
    public void ClosingDuringStartupPreparationReleasesAllPreparedAndRetiredTargets()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            var viewport = CreateStartupViewport(false);
            try
            {
                viewport.RenderingPreparation = new TaskCompletionSource().Task;
                var original = (RenderTarget2D)Field(viewport, "_cachedRenderTarget");
                Render(viewport, 20);
                var prepared = (RenderTarget2D)Field(viewport, "_cachedRenderTarget");
                var presenter = Field(viewport, "_framePresenter");
                var buffers = (Array)presenter.GetType().GetField("_buffers", Private)!.GetValue(presenter)!;
                var targets = buffers.Cast<object>().Select(buffer => (RenderTarget2D)buffer.GetType().GetField("Target", Private)!.GetValue(buffer)!).ToArray();
                viewport.Dispose();
                Assert.IsTrue(original.IsDisposed); Assert.IsTrue(prepared.IsDisposed);
                Assert.IsTrue(targets.All(target => target.IsDisposed));
                Assert.IsNull(viewport.Source);
            }
            finally { viewport.Dispose(); }
        });
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CompletedPreparationOrAnAlreadyRunningSceneDoesNotPrebuildTargets(bool alreadyRunning)
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateStartupViewport(true);
            if (alreadyRunning) { Render(viewport, 1); Settle(viewport, 1); }
            viewport.RecreateGraphicsDevice(new PresentationParameters { BackBufferWidth = 64, BackBufferHeight = 48 });
            viewport.RenderingPreparation = alreadyRunning ? new TaskCompletionSource().Task : Task.CompletedTask;
            var original = Field(viewport, "_cachedRenderTarget");
            Render(viewport, 20);
            Assert.AreEqual(false, Field(viewport, "_preparedStartupBackBuffer"));
            if (alreadyRunning) Assert.AreSame(original, Field(viewport, "_cachedRenderTarget"));
            else Assert.AreNotSame(original, Field(viewport, "_cachedRenderTarget"), "An already ready task must use the normal immediate first frame.");
        });
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HiddenOrUnknownSizePreparationDoesNotCreateStartupTargets(bool hidden)
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var viewport = CreateStartupViewport(false);
            viewport.SuspendWhenHidden = hidden;
            if (!hidden) typeof(D3D11Host).GetField("_pendingPresentationParameters", Private)!.SetValue(viewport, null);
            viewport.RenderingPreparation = new TaskCompletionSource().Task;
            var originalTarget = Field(viewport, "_cachedRenderTarget");
            var originalSource = viewport.Source;
            Render(viewport, 20);
            Assert.AreSame(originalTarget, Field(viewport, "_cachedRenderTarget"));
            Assert.AreSame(originalSource, viewport.Source);
            Assert.AreEqual(false, Field(viewport, "_preparedStartupBackBuffer"));
            Assert.AreEqual(true, Field(viewport, "_resetBackBuffer"));
            Assert.AreEqual(0, viewport.Updates); Assert.AreEqual(0, viewport.Draws);
        });
    }

    private static StartupPreparationProbe CreateStartupViewport(bool arrange)
    {
        var viewport = new StartupPreparationProbe { UseAsynchronousPresentation = true };
        if (arrange)
        {
            viewport.Width = 64; viewport.Height = 48;
            viewport.Measure(new Size(64, 48)); viewport.Arrange(new Rect(0, 0, 64, 48));
        }
        viewport.ForceEnsureCreated();
        viewport.RecreateGraphicsDevice(new PresentationParameters { BackBufferWidth = 64, BackBufferHeight = 48 });
        return viewport;
    }

    private sealed class StartupPreparationProbe : D3D11Host
    {
        internal int Updates, Draws;
        internal TimeSpan LastElapsed;
        protected override void Initialize() { }
        protected override bool SupportsFrameUpdatesWithoutDrawing => true;
        protected override void UpdateFrameWithoutDrawing(GameTime time) { Updates++; LastElapsed = time.ElapsedGameTime; }
        protected override void Render(GameTime time) { Updates++; Draws++; LastElapsed = time.ElapsedGameTime; GraphicsDevice.Clear(Color.CornflowerBlue); }
        protected override void Dispose(bool disposing) { }
    }
}
