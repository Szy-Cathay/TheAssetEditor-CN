using AssetEditorUpdater;

namespace AssetEditorUpdaterTests;

public class WindowsPathIdentityTests
{
    [Test]
    public void OpenExistingDirectory_NormalAndExtendedDosPathsHaveSameIdentity()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using var normal = WindowsPathIdentity.OpenExistingDirectory(root, nameof(root));
            using var extended = WindowsPathIdentity.OpenExistingDirectory(
                ToExtendedDosPath(root),
                "extendedRoot");

            Assert.Multiple(() =>
            {
                Assert.That(WindowsPathIdentity.IsSameDirectory(normal, extended), Is.True);
                Assert.That(normal.VolumeSerialNumber, Is.EqualTo(extended.VolumeSerialNumber));
                Assert.That(normal.FileId.ToArray(), Is.EqualTo(extended.FileId.ToArray()));
                Assert.That(normal.FinalVolumePath, Is.EqualTo(extended.FinalVolumePath).IgnoreCase);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void IsSameOrAncestor_UsesResolvedExtendedPathAndCompleteSegments()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var ancestorPath = Directory.CreateDirectory(Path.Combine(root, "payload")).FullName;
            var childPath = Directory.CreateDirectory(Path.Combine(ancestorPath, "child")).FullName;
            var prefixSiblingPath = Directory.CreateDirectory(Path.Combine(root, "payload-sibling")).FullName;

            using var ancestor = WindowsPathIdentity.OpenExistingDirectory(
                ancestorPath,
                nameof(ancestorPath));
            using var extendedChild = WindowsPathIdentity.OpenExistingDirectory(
                ToExtendedDosPath(childPath),
                nameof(childPath));
            using var prefixSibling = WindowsPathIdentity.OpenExistingDirectory(
                prefixSiblingPath,
                nameof(prefixSiblingPath));

            Assert.Multiple(() =>
            {
                Assert.That(
                    WindowsPathIdentity.IsSameOrAncestor(ancestor, extendedChild),
                    Is.True);
                Assert.That(
                    WindowsPathIdentity.IsSameOrAncestor(extendedChild, ancestor),
                    Is.False);
                Assert.That(
                    WindowsPathIdentity.IsSameOrAncestor(ancestor, prefixSibling),
                    Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void IsSameDirectory_DifferentDirectoriesHaveDifferentFileIds()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var firstPath = Directory.CreateDirectory(Path.Combine(root, "first")).FullName;
            var secondPath = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;

            using var first = WindowsPathIdentity.OpenExistingDirectory(firstPath, nameof(firstPath));
            using var second = WindowsPathIdentity.OpenExistingDirectory(secondPath, nameof(secondPath));

            Assert.That(WindowsPathIdentity.IsSameDirectory(first, second), Is.False);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void OpenExistingDirectory_ParentReparseComponentIsRejected()
    {
        var root = CreateTemporaryDirectory();
        var aliasPath = Path.Combine(root, "alias");
        try
        {
            var targetPath = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            Directory.CreateDirectory(Path.Combine(targetPath, "child"));
            TryCreateDirectorySymbolicLinkOrIgnore(aliasPath, targetPath);

            Assert.Throws<InvalidDataException>(() =>
                WindowsPathIdentity.OpenExistingDirectory(
                    Path.Combine(aliasPath, "child"),
                    "aliasedChild"));
        }
        finally
        {
            if (Directory.Exists(aliasPath))
                Directory.Delete(aliasPath);
            Directory.Delete(root, true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        return Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"WindowsPathIdentityTests-{Guid.NewGuid():N}")).FullName;
    }

    private static string ToExtendedDosPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + fullPath[2..]
            : @"\\?\" + fullPath;
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
}
