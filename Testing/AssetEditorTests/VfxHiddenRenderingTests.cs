using System.Reflection;
using System.Windows;
using System.Windows.Media;
using GameWorld.Core.WpfWindow;
using Microsoft.Xna.Framework;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class VfxHiddenRenderingTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    [Test]
    public void SharedHostDefaultsToLegacyHiddenRendering()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var host = new Host();
            var option = typeof(D3D11Host).GetProperty("SuspendWhenHidden");
            Assert.That(option?.GetValue(host) ?? false, Is.False);
            Set(host, "_isRendering", true);
            Assert.That(() => Tick(host, 1), Throws.TypeOf<NullReferenceException>(),
                "An uninitialized default host must retain the existing GPU-entry route.");
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OptedInHiddenTickStopsBeforeGpuAndKeepsPendingTargets(bool pendingResize)
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var host = new Host { Visibility = Visibility.Collapsed };
            Enable(host);
            Set(host, "_isRendering", true);
            Set(host, "_resetBackBuffer", pendingResize);
            Set(host, "_dpiChanged", pendingResize);
            var sharedDevice = host.GraphicsDevice;
            Assert.That(() => Tick(host, 2), Throws.Nothing);
            Assert.Multiple(() =>
            {
                Assert.That(Read(host, "_lastRenderingTime"), Is.EqualTo(TimeSpan.FromTicks(2)));
                Assert.That(Read(host, "_renderingSuspended"), Is.True);
                Assert.That(Read(host, "_resetBackBuffer"), Is.EqualTo(pendingResize));
                Assert.That(Read(host, "_dpiChanged"), Is.EqualTo(pendingResize));
                Assert.That(host.GraphicsDevice, Is.SameAs(sharedDevice),
                    "A hidden tick must preserve any device shared by earlier viewports.");
                Assert.That(Read(host, "GraphicsDeviceInitialized"), Is.False);
                foreach (var field in new[] { "_graphicsDevice", "_cachedRenderTarget", "_sharedRenderTarget", "_d3D11Image", "_presentationQuery", "_presentationContext", "_spriteBatch" })
                    Assert.That(Read(host, field), Is.Null, $"A hidden tick must not initialize {field}.");
            });
        });
    }

    [Test]
    public void HiddenClockThenVisibleGapPreparesZeroElapsedWithoutConsumingState()
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var host = new Host { Visibility = Visibility.Collapsed };
            Enable(host);
            Set(host, "_isRendering", true);
            Tick(host, 10);
            Tick(host, 20);
            var result = Prepare(host, TimeSpan.FromHours(1).Ticks, true);
            Assert.Multiple(() =>
            {
                Assert.That(result.Present, Is.True);
                Assert.That(result.Frame, Is.Not.Null);
                Assert.That(result.Frame!.ElapsedGameTime, Is.EqualTo(TimeSpan.Zero));
                Assert.That(result.Frame.TotalGameTime, Is.EqualTo(TimeSpan.FromHours(1)));
                Assert.That(Read(host, "_lastRenderingTime"), Is.EqualTo(TimeSpan.FromTicks(20)),
                    "The actual Render must succeed before committing its clock.");
                Assert.That(Read(host, "_renderingSuspended"), Is.True);
                Assert.That(Read(host, "_resetBackBuffer"), Is.False,
                    "A plain resume must not request a render-target recreation.");
            });
        });
    }

    [TestCase(1, 2, 1, false, 1)]
    [TestCase(1, 2, 10, false, -1)]
    [TestCase(1, 2, 10, true, -1)]
    [TestCase(2, 2, 1, false, -1)]
    [TestCase(2, 2, 1, true, 0)]
    public void DefaultClockKeepsLegacyDrawAndPresentationDecisions(long previous, long now, long interval, bool reset, long expectedElapsed)
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            using var host = new Host { Visibility = Visibility.Collapsed, TargetElapsedTime = TimeSpan.FromTicks(interval) };
            Set(host, "_lastRenderingTime", TimeSpan.FromTicks(previous));
            Set(host, "_resetBackBuffer", reset);
            var result = Prepare(host, now, false);
            Assert.That(result.Present, Is.True, "Default hosts keep their existing presentation route, even with no new draw.");
            Assert.That(result.Frame?.ElapsedGameTime.Ticks ?? -1, Is.EqualTo(expectedElapsed));
            Assert.That(Read(host, "_lastRenderingTime"), Is.EqualTo(TimeSpan.FromTicks(previous)));
            Assert.That(Read(host, "_resetBackBuffer"), Is.EqualTo(reset));
        });
    }

    private static void Enable(Host host)
        => typeof(D3D11Host).GetProperty("SuspendWhenHidden")?.SetValue(host, true);

    private static object? Read(Host host, string name) => typeof(D3D11Host).GetField(name, Private)?.GetValue(host);
    private static void Set(Host host, string name, object value) => typeof(D3D11Host).GetField(name, Private)!.SetValue(host, value);
    private static void Tick(Host host, long ticks)
    {
        var args = Activator.CreateInstance(typeof(RenderingEventArgs), Private | BindingFlags.Public, null, [TimeSpan.FromTicks(ticks)], null)!;
        try { typeof(D3D11Host).GetMethod("OnRendering", Private)!.Invoke(host, [host, args]); }
        catch (TargetInvocationException exception) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException!).Throw(); }
    }
    private static (bool Present, GameTime? Frame) Prepare(Host host, long ticks, bool visible)
    {
        var method = typeof(D3D11Host).GetMethod("PrepareRenderingTime", Private);
        Assert.That(method, Is.Not.Null, "Use the actual production timing decision, without creating a GPU device.");
        object?[] args = [TimeSpan.FromTicks(ticks), visible, null];
        return ((bool)method!.Invoke(host, args)!, (GameTime?)args[2]);
    }
    private sealed class Host : D3D11Host { protected override void Dispose(bool disposing) { } }
}
