#nullable enable
using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxReloadCoalescingTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void ContinuousEditsFinishOneUsefulVersionAndThenTheNewestOwnedSnapshot()
    {
        // DIAG: disabled roots exercise the real asynchronous converter without creating a GPU device.
        using var preview = new EffekseerVfxPreview(null!);
        var first = Snapshot(1); var skipped = Snapshot(2); var latest = Snapshot(3);
        Assert.That(preview.BeginInteractiveReload(first), Is.False);
        var active = Read(preview, "_reloadConversion");
        preview.BeginInteractiveReload(skipped);
        preview.BeginInteractiveReload(latest);
        Assert.That(Read(preview, "_reloadConversion"), Is.SameAs(active), "Continuous input must not cancel every useful conversion.");
        Array.Fill(latest.GameBytes, (byte)0);
        var applied = new List<int>();
        var previous = 0;
        var deadline = Stopwatch.GetTimestamp();
        while (preview.HasPendingInteractiveReload)
        {
            preview.ContinueInteractiveReload(new(0, Matrix.Identity, InitializeDuration: true, GroundHeight: 0));
            if (preview.TotalEmitterCount != previous)
            {
                previous = preview.TotalEmitterCount;
                applied.Add(previous);
            }
            Assert.That(Stopwatch.GetElapsedTime(deadline), Is.LessThan(TimeSpan.FromSeconds(10)));
            Thread.Sleep(1);
        }
        Assert.That(applied, Is.EqualTo(new[] { 1, 3 }), "Publish coherent completed versions; discard the superseded queued edit.");
        Assert.That(preview.DisabledEmitterCount, Is.EqualTo(3));
        Assert.That(preview.Diagnostics.All(item => item.ReasonKey != "Vfx.Preview.InvalidData"), Is.True);
    }

    [TestCase("source")]
    [TestCase("quality")]
    [TestCase("selection")]
    public void NewScopeImmediatelyCancelsActiveAndQueuedOldVersions(string change)
    {
        using var preview = new EffekseerVfxPreview(null!);
        preview.BeginInteractiveReload(Snapshot(1));
        preview.BeginInteractiveReload(Snapshot(2));
        var active = Read(preview, "_reloadConversion");
        var next = Snapshot(3);
        next = change switch
        {
            "source" => next with { SourcePath = "vfx/other-DIAG.xml" },
            "quality" => next with { Quality = "1" },
            _ => next with { RootSelection = new(new[] { "layer0", "layer1", "layer2" }, []) },
        };
        preview.BeginInteractiveReload(next);
        Assert.That(Read(preview, "_reloadConversion"), Is.Not.SameAs(active));
        Assert.That(Read(preview, "_queuedReloadSnapshot"), Is.Null);
        Finish(preview);
        Assert.That(preview.TotalEmitterCount, Is.EqualTo(3));
    }

    [Test]
    public void ExplicitSynchronousReloadAndDisposeClearQueuedWork()
    {
        var preview = new EffekseerVfxPreview(null!);
        try
        {
            preview.BeginInteractiveReload(Snapshot(1));
            preview.BeginInteractiveReload(Snapshot(2));
            preview.Reload(Snapshot(3));
            Assert.That(preview.HasPendingInteractiveReload, Is.False);
            Assert.That(Read(preview, "_queuedReloadSnapshot"), Is.Null);
            Assert.That(preview.TotalEmitterCount, Is.EqualTo(3));
            preview.BeginInteractiveReload(Snapshot(1));
            preview.BeginInteractiveReload(Snapshot(2));
        }
        finally { preview.Dispose(); }
        Assert.That(Read(preview, "_queuedReloadSnapshot"), Is.Null);
        Assert.That(preview.HasPendingInteractiveReload, Is.False);
    }

    private static void Finish(EffekseerVfxPreview preview)
    {
        var started = Stopwatch.GetTimestamp();
        while (preview.HasPendingInteractiveReload)
        {
            preview.ContinueInteractiveReload(new(0, Matrix.Identity, InitializeDuration: true, GroundHeight: 0));
            Assert.That(Stopwatch.GetElapsedTime(started), Is.LessThan(TimeSpan.FromSeconds(10)));
            Thread.Sleep(1);
        }
    }

    private static object? Read(object value, string name) => value.GetType().GetField(name, Private)!.GetValue(value);

    private static VfxPreviewSnapshot Snapshot(int count)
    {
        var document = VfxDocument.CreateComposition();
        for (var index = 0; index < count; index++)
            document.Root.Element("vfx")!.Element("emitters")!.Add(new XElement("emitter",
                new XAttribute("id", "layer" + index), new XAttribute("type", "continuous_mesh"),
                new XElement("property", new XAttribute("name", "Enable"), new XAttribute("value", "false"))));
        return new(document.Write(), "vfx/coalescing-DIAG.xml", _ => null);
    }
}
