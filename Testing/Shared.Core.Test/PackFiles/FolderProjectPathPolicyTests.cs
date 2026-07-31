using System.Diagnostics;
using Shared.Core.PackFiles.Utility;

namespace Test.Shared.Core.PackFiles;

public class FolderProjectPathPolicyTests
{
    [TestCase(@"db\units_tables\data.bin", @"db\units_tables\data.bin")]
    [TestCase(@"db/units_tables/data.bin", @"db\units_tables\data.bin")]
    [TestCase(@"./db/units_tables/data.bin", @"db\units_tables\data.bin")]
    public void NormalizeRelativePath_ValidPackPath_ReturnsCanonicalPath(
        string input,
        string expected)
    {
        Assert.That(
            FolderProjectPathPolicy.NormalizeRelativePath(input),
            Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase(@"..\outside.txt")]
    [TestCase(@"db\..\outside.txt")]
    [TestCase(@"C:\outside.txt")]
    [TestCase(@"\\server\share\outside.txt")]
    [TestCase(@"\rooted.txt")]
    [TestCase(@"db\file.txt:stream")]
    [TestCase(@"db\CON")]
    [TestCase(@"db\aux.txt")]
    [TestCase(@"db\name. ")]
    public void NormalizeRelativePath_UnsafePath_Throws(string input)
    {
        Assert.That(
            () => FolderProjectPathPolicy.NormalizeRelativePath(input),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ResolveFilePath_PathEscapesProject_ThrowsWithoutTouchingOutsideFile()
    {
        using var project = new TemporaryDirectory();
        var outsidePath = Path.Combine(
            Path.GetDirectoryName(project.Path)!,
            "outside.txt");

        Assert.That(
            () => FolderProjectPathPolicy.ResolveFilePath(
                project.Path,
                @"..\outside.txt"),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(File.Exists(outsidePath), Is.False);
    }

    [Test]
    public void ResolveFilePath_OutputInsideProject_ReturnsCanonicalPath()
    {
        using var project = new TemporaryDirectory();

        var result = FolderProjectPathPolicy.ResolveFilePath(
            project.Path,
            @"db\units_tables\data.bin");

        Assert.That(
            result,
            Is.EqualTo(
                Path.Combine(
                    project.Path,
                    "db",
                    "units_tables",
                    "data.bin")));
    }

    [Test]
    public void ResolveFilePath_DriveRootProject_Throws()
    {
        var driveRoot = Path.GetPathRoot(Path.GetTempPath())!;

        var exception = Assert.Throws<InvalidDataException>(
            () => FolderProjectPathPolicy.ResolveFilePath(
                driveRoot,
                "file.bin"));

        Assert.That(
            exception!.Message,
            Does.Contain("drive root").IgnoreCase);
    }

    [Test]
    public void EnsureOutputOutsideProject_NestedOutput_Throws()
    {
        using var project = new TemporaryDirectory();
        var output = Path.Combine(project.Path, "output.pack");

        Assert.That(
            () => FolderProjectPathPolicy.EnsureOutputOutsideProject(
                project.Path,
                output),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void EnsureOutputOutsideProject_OutputThroughJunction_Throws()
    {
        using var project = new TemporaryDirectory();
        using var redirectRoot = new TemporaryDirectory();
        var junctionPath = Path.Combine(
            redirectRoot.Path,
            "project-link");
        CreateDirectoryJunction(junctionPath, project.Path);

        try
        {
            var output = Path.Combine(junctionPath, "output.pack");

            Assert.That(
                () => FolderProjectPathPolicy.EnsureOutputOutsideProject(
                    project.Path,
                    output),
                Throws.TypeOf<InvalidDataException>());
        }
        finally
        {
            if (Directory.Exists(junctionPath))
                Directory.Delete(junctionPath);
        }
    }

    [TestCase("aeproject.cn.json", true)]
    [TestCase("aeproject.json", true)]
    [TestCase(@"db\aeproject.cn.json", false)]
    [TestCase(@"aeproject.json\nested.bin", false)]
    public void IsControlFile_OnlyMatchesProjectRootFiles(
        string path,
        bool expected)
    {
        Assert.That(
            FolderProjectPathPolicy.IsControlFile(path),
            Is.EqualTo(expected));
    }

    [TestCase(@".git", true)]
    [TestCase(@".git\config", true)]
    [TestCase(@"nested\.git\objects\item", true)]
    [TestCase(@".gitignore", true)]
    [TestCase(@"nested\.gitignore", true)]
    [TestCase(@".gitattributes", true)]
    [TestCase(@"nested\.gitattributes", true)]
    [TestCase(@".gitignore\nested.bin", false)]
    [TestCase(@".gitattributes\nested.bin", false)]
    [TestCase(@"file.0123456789abcdef0123456789abcdef.tmp", true)]
    [TestCase(@"file.0123456789ABCDEF0123456789ABCDEF.rename", true)]
    [TestCase(@"ordinary.tmp", false)]
    [TestCase(@"asset.abcdefghijklmnopqrstuvwxyz123456.tmp", false)]
    [TestCase(@"file.0123456789abcdef0123456789abcdeg.tmp", false)]
    [TestCase(@"file.0123456789abcdef0123456789abcdef.tmp.bin", false)]
    [TestCase(@"db\aeproject.json", false)]
    public void IsMetadataPath_MatchesOnlyReservedMetadata(
        string path,
        bool expected)
    {
        Assert.That(
            FolderProjectPathPolicy.IsMetadataPath(path),
            Is.EqualTo(expected));
    }

    [TestCase(@".git\config")]
    [TestCase(@"nested\.git")]
    [TestCase(@".gitignore")]
    [TestCase(@"nested\.gitattributes")]
    [TestCase(@"db\file.0123456789abcdef0123456789abcdef.tmp")]
    [TestCase(
        @"packfile_corruction_detection\packfile_corruction_detection_2.txt")]
    [TestCase("aeproject.cn.json")]
    public void EnsureResourcePath_ReservedPath_Throws(string path)
    {
        Assert.That(
            () => FolderProjectPathPolicy.EnsureResourcePath(path),
            Throws.TypeOf<InvalidDataException>());
    }

    [TestCase(".gitignore")]
    [TestCase(".gitattributes")]
    public void EnsureResourceDirectoryPath_FileOnlyMetadataName_IsAllowed(
        string path)
    {
        Assert.That(
            FolderProjectPathPolicy.EnsureResourceDirectoryPath(path),
            Is.EqualTo(path));
    }

    [TestCase(@".git\objects")]
    [TestCase(@"nested\.git")]
    [TestCase(@"folder.0123456789abcdef0123456789abcdef.rename")]
    [TestCase(@"!!!packfile_corruction_detection")]
    [TestCase(@"packfile_corruction_detection")]
    [TestCase(@"zzzz_packfile_corruction_detection")]
    public void EnsureResourceDirectoryPath_DirectoryMetadata_Throws(
        string path)
    {
        Assert.That(
            () => FolderProjectPathPolicy
                .EnsureResourceDirectoryPath(path),
            Throws.TypeOf<InvalidDataException>());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"ae-folder-project-{Guid.NewGuid():N}");

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }

    private static void CreateDirectoryJunction(
        string junctionPath,
        string targetPath)
    {
        using var process = Process.Start(
            new ProcessStartInfo(
                "cmd.exe",
                $"/d /c mklink /J \"{junctionPath}\" \"{targetPath}\"")
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            })!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.That(
            process.ExitCode,
            Is.Zero,
            $"Unable to create a test junction. {output} {error}");
    }
}
