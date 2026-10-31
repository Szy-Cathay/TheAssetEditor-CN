#nullable enable
using System.Diagnostics;
using System.Reflection;
using GameWorld.Core.Vfx;
using Moq;
using Shared.ByteParsing;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxResourceReadCaptureTests
{
    [TestCase(false, CompressionFormat.None)]
    [TestCase(true, CompressionFormat.None)]
    [TestCase(false, CompressionFormat.Lzma1)]
    [TestCase(true, CompressionFormat.Lzma1)]
    [TestCase(false, CompressionFormat.Lz4)]
    [TestCase(true, CompressionFormat.Lz4)]
    [TestCase(false, CompressionFormat.Zstd)]
    [TestCase(true, CompressionFormat.Zstd)]
    public void PackedReaderFreezesThePathAndMetadataAndUsesTheOriginalDecryptAndDecompress(bool encrypted, CompressionFormat format)
    {
        var payload = Enumerable.Range(0, 512).Select(index => (byte)(index * 19)).ToArray();
        var encoded = format == CompressionFormat.None ? payload : FileCompression.Compress(payload, format);
        if (encrypted) encoded = FileEncryption.Encrypt(encoded);
        var path = Path.GetTempFileName(); var other = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[13].Concat(encoded).ToArray());
            File.WriteAllBytes(other, [255]);
            var parent = new PackedFileSourceParent { FilePath = path };
            var source = new PackedFileSource(parent, 13, encoded.Length, encrypted, format != CompressionFormat.None, format, (uint)payload.Length);
            var read = VfxPreviewDocumentStore.CaptureDataSourceRead(source);
            parent.FilePath = other;
            source.Parent = new() { FilePath = other };
            source.IsCompressed = !source.IsCompressed; source.CompressionFormat = CompressionFormat.None; source.UncompressedSize = 1;
            var actual = Task.Run(read).GetAwaiter().GetResult();
            Assert.That(actual, Is.EqualTo(payload));
            Assert.That(typeof(PackedFileSourceParent).GetField("_sharedStream", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(parent),
                Is.Null, "The captured worker must open its own stream instead of touching the parent's live shared stream.");
        }
        finally { File.Delete(path); File.Delete(other); }
    }

    [Test]
    public void FileSystemCaptureDefersIoButFreezesItsPathAndPropagatesIoFailures()
    {
        var path = Path.GetTempFileName(); var other = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]); File.WriteAllBytes(other, [9]);
            var source = new FileSystemSource(path);
            var read = VfxPreviewDocumentStore.CaptureDataSourceRead(source);
            typeof(FileSystemSource).GetField("_filepath", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(source, other);
            File.WriteAllBytes(path, [4, 5]);
            Assert.That(Task.Run(read).GetAwaiter().GetResult(), Is.EqualTo(new byte[] { 4, 5 }),
                "The owner captures the path; I/O itself must occur later on the worker.");
            File.Delete(path);
            Assert.Throws<FileNotFoundException>(() => Task.Run(read).GetAwaiter().GetResult());
        }
        finally { File.Delete(path); File.Delete(other); }
    }

    [Test]
    public void MemoryAndUnknownSourcesStayDetachedFromLaterPackFileReplacement()
    {
        var owner = Environment.CurrentManagedThreadId;
        var recording = new RecordingSource([1, 2], owner);
        var file = new PackFile("unknown.bin", recording);
        var read = new VfxPreviewDocumentStore().CaptureResourceRead(file);
        Assert.That(recording.Reads, Is.EqualTo(1));
        file.DataSource = new MemorySource([9]);
        Assert.That(Task.Run(read).GetAwaiter().GetResult(), Is.EqualTo(new byte[] { 1, 2 }));
        Assert.That(recording.Reads, Is.EqualTo(1), "An unknown source's ReadData is invoked exactly once on the owner.");
        var memory = VfxPreviewDocumentStore.CaptureDataSourceRead(new MemorySource([3, 4]));
        Assert.That(Task.Run(memory).GetAwaiter().GetResult(), Is.EqualTo(new byte[] { 3, 4 }));
    }

    [Test]
    public void LiveSnapshotsTakePriorityAndInvalidLiveInputFailsDuringOwnerCapture()
    {
        var store = new VfxPreviewDocumentStore(); var source = new RecordingSource([1], Environment.CurrentManagedThreadId);
        var file = new PackFile("live.xml", source); var editor = new object();
        store.Publish(editor, file, new([2, 3], "vfx/live.xml", _ => null));
        var read = store.CaptureResourceRead(file);
        store.Publish(editor, file, new([4], "vfx/live.xml", _ => null));
        Assert.That(Task.Run(read).GetAwaiter().GetResult(), Is.EqualTo(new byte[] { 2, 3 }));
        Assert.That(source.Reads, Is.Zero);
        store.Publish(editor, file, null);
        Assert.Throws<InvalidDataException>(() => store.CaptureResourceRead(file));
        Assert.That(source.Reads, Is.Zero, "Invalid live values cannot silently fall back to saved bytes.");
    }

    [Test]
    public void DocumentSourceLocatesOnTheOwnerRegistersDependenciesAndPreservesPackPriority()
    {
        var ownerThread = Environment.CurrentManagedThreadId;
        var root = PackFile.CreateFromBytes("root.xml", VfxDocument.CreateComposition().Write());
        var preferred = PackFile.CreateFromBytes("texture.dds", [1]);
        var editableCopy = PackFile.CreateFromBytes("texture.dds", [9]);
        var owner = new PackFileContainer("owner"); var editable = new PackFileContainer("editable");
        var files = new Mock<IPackFileService>(); var lookups = new List<PackFileContainer>();
        files.Setup(value => value.GetFullPath(root, It.IsAny<PackFileContainer?>())).Returns("vfx/root.xml");
        files.Setup(value => value.GetPackFileContainer(root)).Returns(owner);
        files.Setup(value => value.GetEditablePack()).Returns(editable);
        files.Setup(value => value.GetAllPackfileContainers()).Returns([owner, editable]);
        files.Setup(value => value.FindFile(It.IsAny<string>(), It.IsAny<PackFileContainer>()))
            .Returns((string _, PackFileContainer container) =>
            {
                Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(ownerThread)); lookups.Add(container);
                return container == owner ? preferred : editableCopy;
            });
        var store = new VfxPreviewDocumentStore();
        using var source = new VfxPreviewDocumentSource(files.Object, root, store);
        var snapshot = source.Read()!;
        var read = snapshot.CaptureResourceRead!("vfx/texture.dds")!;
        Assert.That(source.DependsOn(preferred), Is.True);
        Assert.That(source.DependsOn(editableCopy), Is.False);
        Assert.That(lookups, Is.EqualTo(new[] { owner }));
        Assert.That(Task.Run(read).GetAwaiter().GetResult(), Is.EqualTo(new byte[] { 1 }));
        Assert.That(lookups, Has.Count.EqualTo(1), "No Pack lookup or dependency registration may run in the captured worker reader.");
        var changed = 0; source.Changed += () => changed++;
        store.Publish(new object(), preferred, new([5], "vfx/texture.dds", _ => null));
        Assert.That(changed, Is.EqualTo(1));
        Assert.That(Task.Run(snapshot.CaptureResourceRead!("vfx/texture.dds")!).GetAwaiter().GetResult(), Is.EqualTo(new byte[] { 5 }));
    }

    [Test]
    public void ConversionCapturesOnOwnerReadsWithinTheWorkerPermitAndReadsEachDependencyOnce()
    {
        var owner = Environment.CurrentManagedThreadId;
        var source = VfxLibraryVisibilityTests.Source("");
        var composition = VfxDocument.CreateComposition();
        composition.AddReference("child", VfxDocument.Read(source)).Apply();
        composition.AddReference("child", VfxDocument.Read(source)).Apply();
        var captures = new Dictionary<string, int>(); var reads = new Dictionary<string, int>();
        var snapshot = new VfxPreviewSnapshot(composition.Write(), "vfx/capture-DIAG.xml", _ => throw new AssertionException("Legacy resolver must not be used."))
        {
            CaptureResourceRead = path =>
            {
                Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(owner));
                captures[path] = captures.GetValueOrDefault(path) + 1;
                return () =>
                {
                    Assert.That(Environment.CurrentManagedThreadId, Is.Not.EqualTo(owner));
                    Assert.That(Workers.CurrentCount, Is.LessThan(2), "I/O must hold a bounded CPU worker permit.");
                    reads[path] = reads.GetValueOrDefault(path) + 1;
                    return path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? source : [1, 2, 3];
                };
            },
        };
        using var helper = new VfxInteractiveConversion(snapshot, false);
        var result = Take(helper);
        Assert.That(result.Conversion.Emitters, Has.Count.EqualTo(2));
        Assert.That(captures.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(captures.Values, Is.All.EqualTo(1)); Assert.That(reads.Values, Is.All.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingAndFailingCapturedReadsKeepTheirOriginalFailureDiagnostics(bool throwing)
    {
        var source = VfxLibraryVisibilityTests.Source("");
        byte[]? Failure(string _) => throwing ? throw new InvalidDataException("DIAG captured read failed") : null;
        var expected = TotalWarVfxConverter.Convert(new(source, "vfx/failure-DIAG.xml", Failure), false);
        var snapshot = new VfxPreviewSnapshot(source, "vfx/failure-DIAG.xml", _ => throw new AssertionException("Legacy resolver used."))
            { CaptureResourceRead = _ => throwing ? () => throw new InvalidDataException("DIAG captured read failed") : null };
        using var helper = new VfxInteractiveConversion(snapshot, false);
        var actual = Take(helper);
        Assert.That(actual.Conversion.Diagnostics, Is.EqualTo(expected.Diagnostics));
        Assert.That(actual.Conversion.Emitters.Count, Is.EqualTo(expected.Emitters.Count));
    }

    [Test]
    public void DisposeCancelsAnInFlightCapturedReadWithoutPublishingOrLosingWorkerPermits()
    {
        var source = VfxLibraryVisibilityTests.Source("");
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var snapshot = new VfxPreviewSnapshot(source, "vfx/cancel-capture-DIAG.xml", _ => throw new AssertionException("Legacy resolver used."))
        {
            CaptureResourceRead = _ => () =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test-owned read was not released.");
                return [1];
            },
        };
        using var helper = new VfxInteractiveConversion(snapshot, false);
        try
        {
            var deadline = Stopwatch.GetTimestamp();
            while (!entered.IsSet && Stopwatch.GetElapsedTime(deadline).TotalSeconds < 5) { helper.TryTake(out _); Thread.Sleep(1); }
            Assert.That(entered.IsSet, Is.True); helper.Dispose();
        }
        finally { release.Set(); }
        var task = (Task)typeof(VfxInteractiveConversion).GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(helper)!;
        Assert.Catch<OperationCanceledException>(() => task.GetAwaiter().GetResult());
        Assert.That(helper.TryTake(out _), Is.False);
        Assert.That(Workers.CurrentCount, Is.EqualTo(2));
    }

    private static SemaphoreSlim Workers => (SemaphoreSlim)typeof(VfxInteractiveConversion)
        .GetField("Workers", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    private static VfxPreparedConversion Take(VfxInteractiveConversion helper)
    {
        var deadline = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(deadline).TotalSeconds < 10)
        {
            if (helper.TryTake(out var result)) return result!;
            Thread.Sleep(1);
        }
        throw new TimeoutException("Captured resource request did not finish.");
    }

    private sealed class RecordingSource(byte[] bytes, int owner) : IDataSource
    {
        internal int Reads;
        public long Size => bytes.Length;
        public byte[] ReadData() { Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(owner)); Reads++; return bytes; }
        public byte[] PeekData(int size) => throw new NotSupportedException();
        public ByteChunk ReadDataAsChunk() => throw new NotSupportedException();
        public CompressionFormat CompressionFormat => CompressionFormat.None;
    }
}
