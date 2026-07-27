using System.Diagnostics;
using AssetEditorUpdater;
using UpdaterProgram = AssetEditorUpdater.AssetEditorUpdater;

namespace AssetEditorUpdaterTests;

public class UpdaterPayloadCopierTests
{
    [Test]
    public void CopyAndVerify_CopiesNestedPayloadAndReturnsUppercaseSha256Manifest()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var sourceDirectory = CreatePayloadSource(root);
            var destinationDirectory = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;

            var manifest = UpdaterPayloadCopier.CopyAndVerify(sourceDirectory, destinationDirectory);

            Assert.Multiple(() =>
            {
                Assert.That(
                    File.ReadAllText(Path.Combine(destinationDirectory, "AssetEditor.CN.Updater.exe")),
                    Is.EqualTo("updater"));
                Assert.That(
                    File.ReadAllBytes(Path.Combine(destinationDirectory, "runtimes", "win-x64", "native.dll")),
                    Is.EqualTo(new byte[] { 0, 1, 2, 255 }));
                Assert.That(
                    manifest,
                    Has.Count.EqualTo(2));
                Assert.That(
                    manifest["AssetEditor.CN.Updater.exe"],
                    Is.EqualTo("196C23750E800642E7C008DC0416536449F295069C8161C23283C86334F8D251"));
                Assert.That(
                    manifest[Path.Combine("runtimes", "win-x64", "native.dll")],
                    Is.EqualTo("3D1F57C984978EF98A18378C8166C1CB8EDE02C03EEB6AEE7E2F121DFEEE3E56"));
                Assert.That(
                    manifest.ContainsKey(Path.Combine("RUNTIMES", "WIN-X64", "NATIVE.DLL")),
                    Is.True);
                Assert.That(
                    manifest.Values.All(hash =>
                        hash.Length == 64
                        && hash.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F')),
                    Is.True);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Verify_ModifiedDestinationFileIsRejected()
    {
        var copied = CreateCopiedPayload();
        try
        {
            File.WriteAllText(
                Path.Combine(copied.DestinationDirectory, "AssetEditor.CN.Updater.exe"),
                "tampered");

            Assert.Throws<InvalidDataException>(() =>
                UpdaterPayloadCopier.Verify(
                    copied.SourceDirectory,
                    copied.DestinationDirectory,
                    copied.Manifest));
        }
        finally
        {
            Directory.Delete(copied.Root, true);
        }
    }

    [Test]
    public void Verify_MissingDestinationFileIsRejected()
    {
        var copied = CreateCopiedPayload();
        try
        {
            File.Delete(Path.Combine(copied.DestinationDirectory, "runtimes", "win-x64", "native.dll"));

            Assert.Throws<InvalidDataException>(() =>
                UpdaterPayloadCopier.Verify(
                    copied.SourceDirectory,
                    copied.DestinationDirectory,
                    copied.Manifest));
        }
        finally
        {
            Directory.Delete(copied.Root, true);
        }
    }

    [Test]
    public void Verify_AddedDestinationFileIsRejected()
    {
        var copied = CreateCopiedPayload();
        try
        {
            File.WriteAllText(Path.Combine(copied.DestinationDirectory, "added.dll"), "added");

            Assert.Throws<InvalidDataException>(() =>
                UpdaterPayloadCopier.Verify(
                    copied.SourceDirectory,
                    copied.DestinationDirectory,
                    copied.Manifest));
        }
        finally
        {
            Directory.Delete(copied.Root, true);
        }
    }

    [Test]
    public void Verify_AddedEmptyDestinationDirectoryIsRejected()
    {
        var copied = CreateCopiedPayload();
        try
        {
            Directory.CreateDirectory(Path.Combine(copied.DestinationDirectory, "added-directory"));

            Assert.Throws<InvalidDataException>(() =>
                UpdaterPayloadCopier.Verify(
                    copied.SourceDirectory,
                    copied.DestinationDirectory,
                    copied.Manifest));
        }
        finally
        {
            Directory.Delete(copied.Root, true);
        }
    }

    [Test]
    public void CopyAndVerify_NonEmptyDestinationIsRejectedWithoutChangingIt()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var sourceDirectory = CreatePayloadSource(root);
            var destinationDirectory = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;
            var sentinelPath = Path.Combine(destinationDirectory, "keep.txt");
            File.WriteAllText(sentinelPath, "preserve");

            Assert.Throws<InvalidOperationException>(() =>
                UpdaterPayloadCopier.CopyAndVerify(sourceDirectory, destinationDirectory));

            Assert.That(File.ReadAllText(sentinelPath), Is.EqualTo("preserve"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestCase("same")]
    [TestCase("destination-inside-source")]
    [TestCase("source-inside-destination")]
    public void CopyAndVerify_OverlappingRootsAreRejectedBeforeCopy(string layout)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            string sourceDirectory;
            string destinationDirectory;
            switch (layout)
            {
                case "same":
                    sourceDirectory = Directory.CreateDirectory(Path.Combine(root, "payload")).FullName;
                    destinationDirectory = sourceDirectory;
                    break;
                case "destination-inside-source":
                    sourceDirectory = Directory.CreateDirectory(Path.Combine(root, "payload")).FullName;
                    destinationDirectory = Directory.CreateDirectory(
                        Path.Combine(sourceDirectory, "destination")).FullName;
                    break;
                case "source-inside-destination":
                    destinationDirectory = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;
                    sourceDirectory = Directory.CreateDirectory(
                        Path.Combine(destinationDirectory, "payload")).FullName;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(layout));
            }

            File.WriteAllText(Path.Combine(sourceDirectory, "payload.bin"), "payload");

            Assert.Throws<InvalidOperationException>(() =>
                UpdaterPayloadCopier.CopyAndVerify(sourceDirectory, destinationDirectory));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void CopyAndVerify_SourceDirectoryReparsePointIsRejected()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var targetDirectory = CreatePayloadSource(root);
            var sourceDirectory = Path.Combine(root, "source-link");
            TryCreateDirectorySymbolicLinkOrIgnore(sourceDirectory, targetDirectory);
            var destinationDirectory = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;

            Assert.Throws<InvalidDataException>(() =>
                UpdaterPayloadCopier.CopyAndVerify(sourceDirectory, destinationDirectory));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void CopyAndVerify_SourceFileReparsePointIsRejected()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var sourceDirectory = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
            var targetPath = Path.Combine(root, "target.dll");
            File.WriteAllText(targetPath, "target");
            TryCreateFileSymbolicLinkOrIgnore(Path.Combine(sourceDirectory, "linked.dll"), targetPath);
            var destinationDirectory = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;

            Assert.Throws<InvalidDataException>(() =>
                UpdaterPayloadCopier.CopyAndVerify(sourceDirectory, destinationDirectory));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void Verify_EnumerationMutationFailsClosed()
    {
        var copied = CreateCopiedPayload();
        try
        {
            var mutated = false;

            Assert.Throws<InvalidDataException>(() =>
                UpdaterPayloadCopier.Verify(
                    copied.SourceDirectory,
                    copied.DestinationDirectory,
                    copied.Manifest,
                    enumeratedDirectory =>
                    {
                        if (mutated || !PathsEqual(enumeratedDirectory, copied.DestinationDirectory))
                            return;

                        mutated = true;
                        File.WriteAllText(
                            Path.Combine(copied.DestinationDirectory, "raced-into-tree.dll"),
                            "race");
                    }));

            Assert.That(mutated, Is.True);
        }
        finally
        {
            Directory.Delete(copied.Root, true);
        }
    }

    [Test]
    public void RelaunchFromUpdateDirectory_RevalidatesAfterCopyVerificationAndImmediatelyBeforeLaunch()
    {
        var events = new List<string>();
        var workspace = new UpdaterWorkspace(
            Path.Combine(Path.GetTempPath(), "transaction"),
            Path.Combine(Path.GetTempPath(), "transaction", "Update"),
            true);
        var validationCount = 0;

        UpdaterProgram.RelaunchFromUpdateDirectory(
            Path.Combine(Path.GetTempPath(), "source"),
            workspace,
            Path.Combine(Path.GetTempPath(), "installation"),
            (_, actualWorkspace, _) =>
            {
                Assert.That(actualWorkspace, Is.SameAs(workspace));
                events.Add("copy-and-verify");
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            },
            (isProtected, updateDirectory) =>
            {
                validationCount++;
                Assert.Multiple(() =>
                {
                    Assert.That(isProtected, Is.True);
                    Assert.That(updateDirectory, Is.EqualTo(workspace.UpdateDirectory));
                    if (validationCount == 1)
                        Assert.That(events, Is.Empty);
                    else
                        Assert.That(events, Is.EqualTo(new[] { "validate-1", "copy-and-verify" }));
                });
                events.Add($"validate-{validationCount}");
                return workspace;
            },
            startInfo =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(
                        events,
                        Is.EqualTo(new[] { "validate-1", "copy-and-verify", "validate-2" }));
                    Assert.That(
                        startInfo.FileName,
                        Is.EqualTo(Path.Combine(workspace.UpdateDirectory, "AssetEditor.CN.Updater.exe")));
                });
                events.Add("start");
            });

        Assert.That(
            events,
            Is.EqualTo(new[] { "validate-1", "copy-and-verify", "validate-2", "start" }));
    }

    private static string CreatePayloadSource(string root)
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
        var nativeDirectory = Directory.CreateDirectory(
            Path.Combine(sourceDirectory, "runtimes", "win-x64")).FullName;
        File.WriteAllText(Path.Combine(sourceDirectory, "AssetEditor.CN.Updater.exe"), "updater");
        File.WriteAllBytes(Path.Combine(nativeDirectory, "native.dll"), [0, 1, 2, 255]);
        return sourceDirectory;
    }

    private static CopiedPayload CreateCopiedPayload()
    {
        var root = CreateTemporaryDirectory();
        var sourceDirectory = CreatePayloadSource(root);
        var destinationDirectory = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;
        var manifest = UpdaterPayloadCopier.CopyAndVerify(sourceDirectory, destinationDirectory);
        return new CopiedPayload(root, sourceDirectory, destinationDirectory, manifest);
    }

    private static string CreateTemporaryDirectory()
    {
        return Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"UpdaterPayloadCopierTests-{Guid.NewGuid():N}")).FullName;
    }

    private static void TryCreateDirectorySymbolicLinkOrIgnore(string path, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(path, targetPath);
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Ignore("Creating directory symbolic links is not permitted on this machine.");
        }
        catch (PlatformNotSupportedException)
        {
            Assert.Ignore("Directory symbolic links are not supported on this machine.");
        }
    }

    private static void TryCreateFileSymbolicLinkOrIgnore(string path, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(path, targetPath);
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Ignore("Creating file symbolic links is not permitted on this machine.");
        }
        catch (PlatformNotSupportedException)
        {
            Assert.Ignore("File symbolic links are not supported on this machine.");
        }
    }

    private static bool PathsEqual(string firstPath, string secondPath)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(firstPath)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(secondPath)),
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CopiedPayload(
        string Root,
        string SourceDirectory,
        string DestinationDirectory,
        IReadOnlyDictionary<string, string> Manifest);
}
