using System.Diagnostics;
using System.Text;
using LibGit2Sharp;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;

namespace Test.Shared.Core.PackFiles;

public class FolderProjectGitFoundationTests
{
    [Test]
    public void Initialize_ChineseSpacePath_IsIdempotentAndReopens()
    {
        using var project = new TemporaryDirectory("中文 工程");
        FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" }).Dispose();
        File.WriteAllText(
            Path.Combine(project.Path, ".gitignore"),
            "# 用户规则\r\ncustom.bin\r\n");
        File.WriteAllText(
            Path.Combine(project.Path, ".gitattributes"),
            "*.txt text\r\n");

        Assert.That(
            FolderProjectGitRepository.IsRepository(project.Path),
            Is.False);

        FolderProjectGitRepository.Initialize(project.Path);
        FolderProjectGitRepository.Initialize(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                FolderProjectGitRepository.IsRepository(project.Path),
                Is.True);
            Assert.That(
                File.ReadAllText(Path.Combine(project.Path, ".gitignore")),
                Does.Contain("# 用户规则"));
            Assert.That(
                File.ReadAllText(Path.Combine(project.Path, ".gitignore")),
                Does.Contain(
                    "*.[0-9a-f][0-9a-f][0-9a-f][0-9a-f]" +
                    "[0-9a-f][0-9a-f][0-9a-f][0-9a-f]" +
                    "[0-9a-f][0-9a-f][0-9a-f][0-9a-f]" +
                    "[0-9a-f][0-9a-f][0-9a-f][0-9a-f]" +
                    "[0-9a-f][0-9a-f][0-9a-f][0-9a-f]" +
                    "[0-9a-f][0-9a-f][0-9a-f][0-9a-f]" +
                    "[0-9a-f][0-9a-f][0-9a-f][0-9a-f]" +
                    "[0-9a-f][0-9a-f][0-9a-f][0-9a-f].tmp"));
            Assert.That(
                File.ReadAllText(Path.Combine(project.Path, ".gitignore")),
                Does.Contain(
                    "*.[0-9a-f][0-9a-f][0-9a-f][0-9a-f]" +
                    "[0-9a-f][0-9a-f][0-9a-f][0-9a-f]" +
                    "[0-9a-f][0-9a-f][0-9a-f][0-9a-f]" +
                    "[0-9a-f][0-9a-f][0-9a-f][0-9a-f]" +
                    "[0-9a-f][0-9a-f][0-9a-f][0-9a-f]" +
                    "[0-9a-f][0-9a-f][0-9a-f][0-9a-f]" +
                    "[0-9a-f][0-9a-f][0-9a-f][0-9a-f]" +
                    "[0-9a-f][0-9a-f][0-9a-f][0-9a-f].rename"));
            Assert.That(
                File.ReadAllText(Path.Combine(project.Path, ".gitattributes")),
                Does.Contain("*.txt text"));
            Assert.That(
                File.ReadAllText(Path.Combine(project.Path, ".gitattributes")),
                Does.Contain("* -text"));
            Assert.That(
                File.ReadAllText(Path.Combine(project.Path, ".gitattributes"))
                    .IndexOf("* -text", StringComparison.Ordinal),
                Is.LessThan(
                    File.ReadAllText(
                            Path.Combine(project.Path, ".gitattributes"))
                        .IndexOf("*.txt text", StringComparison.Ordinal)));
        });

        using var reopened = new Repository(project.Path);
        Assert.That(reopened.Info.IsBare, Is.False);
    }

    [Test]
    public void Initialize_PolicyFilesAndSettingsRemainTrackable()
    {
        using var project = new TemporaryDirectory("trackable");
        FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" }).Dispose();

        FolderProjectGitRepository.Initialize(project.Path);

        using var repository = new Repository(project.Path);
        File.WriteAllText(
            Path.Combine(
                project.Path,
                "asset.abcdefghijklmnopqrstuvwxyz123456.tmp"),
            "resource");
        File.WriteAllText(
            Path.Combine(
                project.Path,
                "asset.0123456789abcdef0123456789abcdef.tmp"),
            "residue");
        Commands.Stage(
            repository,
            [
                FolderProjectSettings.CnFileName,
                ".gitignore",
                ".gitattributes",
            ]);
        Assert.That(
            repository.RetrieveStatus()
                .Where(entry => entry.State != FileStatus.Ignored)
                .Select(entry => entry.FilePath),
            Does.Contain(FolderProjectSettings.CnFileName));
        Assert.That(
            repository.Index[".gitignore"],
            Is.Not.Null);
        Assert.That(
            repository.Index[".gitattributes"],
            Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(
                repository.RetrieveStatus(
                    "asset.abcdefghijklmnopqrstuvwxyz123456.tmp"),
                Is.Not.EqualTo(FileStatus.Ignored));
            Assert.That(
                repository.RetrieveStatus(
                    "asset.0123456789abcdef0123456789abcdef.tmp"),
                Is.EqualTo(FileStatus.Ignored));
        });
    }

    [Test]
    public void Initialize_NestedInsideParentRepository_CreatesIndependentRepository()
    {
        using var parent = new TemporaryDirectory("parent");
        Repository.Init(parent.Path);
        var projectPath = Path.Combine(parent.Path, "nested", "中文 工程");
        Directory.CreateDirectory(projectPath);
        FolderProjectContainer.Create(
            projectPath,
            new FolderProjectSettings { Name = "工程" }).Dispose();

        Assert.That(
            FolderProjectGitRepository.IsRepository(projectPath),
            Is.False);

        FolderProjectGitRepository.Initialize(projectPath);

        Assert.Multiple(() =>
        {
            Assert.That(
                Directory.Exists(Path.Combine(projectPath, ".git")),
                Is.True);
            Assert.That(
                FolderProjectGitRepository.IsRepository(projectPath),
                Is.True);
            Assert.That(
                File.Exists(Path.Combine(parent.Path, ".gitattributes")),
                Is.False);
            Assert.That(
                File.Exists(Path.Combine(parent.Path, ".gitignore")),
                Is.False);
        });
    }

    [Test]
    public void Initialize_GitFile_RejectsWithoutOverwritingIt()
    {
        using var project = new TemporaryDirectory("git-file");
        FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" }).Dispose();
        var markerPath = Path.Combine(project.Path, ".git");
        const string markerContents = "gitdir: unsupported";
        File.WriteAllText(markerPath, markerContents);

        Assert.That(
            FolderProjectGitRepository.IsRepository(project.Path),
            Is.False);
        Assert.That(
            () => FolderProjectGitRepository.Initialize(project.Path),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(
            File.ReadAllText(markerPath),
            Is.EqualTo(markerContents));
    }

    [TestCase(".gitignore")]
    [TestCase(".gitattributes")]
    public void Initialize_PolicyDirectoryCollision_RejectsBeforeCreatingRepository(
        string directoryName)
    {
        using var project = new TemporaryDirectory("policy-directory");
        FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" }).Dispose();
        var resourcePath = Path.Combine(
            project.Path,
            directoryName,
            "item.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(resourcePath)!);
        File.WriteAllBytes(resourcePath, [1, 2, 3]);

        Assert.That(
            () => FolderProjectGitRepository.Initialize(project.Path),
            Throws.TypeOf<InvalidOperationException>());
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(resourcePath), Is.EqualTo(
                new byte[] { 1, 2, 3 }));
            Assert.That(
                Directory.Exists(Path.Combine(project.Path, ".git")),
                Is.False);
        });
    }

    [Test]
    public void Initialize_GitJunction_RejectsExternalRepositoryMetadata()
    {
        using var external = new TemporaryDirectory("external-repository");
        Repository.Init(external.Path);
        using var project = new TemporaryDirectory("git-junction");
        FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" }).Dispose();
        var junctionPath = Path.Combine(project.Path, ".git");
        CreateDirectoryJunction(
            junctionPath,
            Path.Combine(external.Path, ".git"));

        try
        {
            Assert.That(
                FolderProjectGitRepository.IsRepository(project.Path),
                Is.False);
            Assert.That(
                () => FolderProjectGitRepository.Initialize(project.Path),
                Throws.TypeOf<InvalidOperationException>());
        }
        finally
        {
            if (Directory.Exists(junctionPath))
                Directory.Delete(junctionPath);
        }
    }

    [Test]
    public void Initialize_NewRepositoryPolicyFailure_RollsBackAllChanges()
    {
        using var project = new TemporaryDirectory("new-transaction");
        FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" }).Dispose();
        var attributesPath = Path.Combine(project.Path, ".gitattributes");
        var ignorePath = Path.Combine(project.Path, ".gitignore");
        File.WriteAllBytes(attributesPath, "# attributes\r\n"u8.ToArray());
        File.WriteAllBytes(ignorePath, "# ignore\r\n"u8.ToArray());
        File.SetAttributes(
            ignorePath,
            File.GetAttributes(ignorePath) | FileAttributes.ReadOnly);
        var attributesBytes = File.ReadAllBytes(attributesPath);
        var ignoreBytes = File.ReadAllBytes(ignorePath);
        var attributesAttributes = File.GetAttributes(attributesPath);
        var ignoreAttributes = File.GetAttributes(ignorePath);

        Assert.That(
            () => FolderProjectGitRepository.Initialize(project.Path),
            Throws.Exception);

        Assert.Multiple(() =>
        {
            Assert.That(
                Directory.Exists(Path.Combine(project.Path, ".git")),
                Is.False);
            Assert.That(
                File.ReadAllBytes(attributesPath),
                Is.EqualTo(attributesBytes));
            Assert.That(
                File.ReadAllBytes(ignorePath),
                Is.EqualTo(ignoreBytes));
            Assert.That(
                File.GetAttributes(attributesPath),
                Is.EqualTo(attributesAttributes));
            Assert.That(
                File.GetAttributes(ignorePath),
                Is.EqualTo(ignoreAttributes));
        });
    }

    [Test]
    public void Initialize_NewRepositoryFailure_RemovesNewPolicyFile()
    {
        using var project = new TemporaryDirectory("new-policy-rollback");
        var attributesPath = Path.Combine(project.Path, ".gitattributes");
        var ignorePath = Path.Combine(project.Path, ".gitignore");
        File.WriteAllBytes(ignorePath, "# ignore\r\n"u8.ToArray());
        File.SetAttributes(
            ignorePath,
            File.GetAttributes(ignorePath) | FileAttributes.ReadOnly);
        var ignoreBytes = File.ReadAllBytes(ignorePath);
        var ignoreAttributes = File.GetAttributes(ignorePath);

        Assert.That(
            () => FolderProjectGitRepository.Initialize(project.Path),
            Throws.Exception);

        Assert.Multiple(() =>
        {
            Assert.That(
                Directory.Exists(Path.Combine(project.Path, ".git")),
                Is.False);
            Assert.That(File.Exists(attributesPath), Is.False);
            Assert.That(
                File.ReadAllBytes(ignorePath),
                Is.EqualTo(ignoreBytes));
            Assert.That(
                File.GetAttributes(ignorePath),
                Is.EqualTo(ignoreAttributes));
        });
    }

    [Test]
    public void Initialize_ExistingRepositoryPolicyFailure_RollsBackBothFiles()
    {
        using var project = new TemporaryDirectory("existing-transaction");
        Repository.Init(project.Path);
        var attributesPath = Path.Combine(project.Path, ".gitattributes");
        var ignorePath = Path.Combine(project.Path, ".gitignore");
        File.WriteAllBytes(attributesPath, "# attributes\r\n"u8.ToArray());
        File.WriteAllBytes(ignorePath, "# ignore\r\n"u8.ToArray());
        File.SetAttributes(
            ignorePath,
            File.GetAttributes(ignorePath) | FileAttributes.ReadOnly);
        var attributesBytes = File.ReadAllBytes(attributesPath);
        var ignoreBytes = File.ReadAllBytes(ignorePath);
        var attributesAttributes = File.GetAttributes(attributesPath);
        var ignoreAttributes = File.GetAttributes(ignorePath);

        Assert.That(
            () => FolderProjectGitRepository.Initialize(project.Path),
            Throws.Exception);

        Assert.Multiple(() =>
        {
            Assert.That(
                FolderProjectGitRepository.IsRepository(project.Path),
                Is.True);
            Assert.That(
                File.ReadAllBytes(attributesPath),
                Is.EqualTo(attributesBytes));
            Assert.That(
                File.ReadAllBytes(ignorePath),
                Is.EqualTo(ignoreBytes));
            Assert.That(
                File.GetAttributes(attributesPath),
                Is.EqualTo(attributesAttributes));
            Assert.That(
                File.GetAttributes(ignorePath),
                Is.EqualTo(ignoreAttributes));
        });
    }

    [Test]
    public void Initialize_FallbackFailure_RollsBackRootAndInfoPolicies()
    {
        using var project = new TemporaryDirectory("fallback-transaction");
        Repository.Init(project.Path);
        var attributesPath = Path.Combine(project.Path, ".gitattributes");
        var ignorePath = Path.Combine(project.Path, ".gitignore");
        var fallbackAttributesPath = Path.Combine(
            project.Path,
            ".git",
            "info",
            "attributes");
        var fallbackIgnorePath = Path.Combine(
            project.Path,
            ".git",
            "info",
            "exclude");
        File.WriteAllBytes(
            attributesPath,
            Encoding.Unicode.GetPreamble()
                .Concat(Encoding.Unicode.GetBytes("# attributes\r\n"))
                .ToArray());
        File.WriteAllBytes(
            ignorePath,
            Encoding.Unicode.GetPreamble()
                .Concat(Encoding.Unicode.GetBytes("# ignore\r\n"))
                .ToArray());
        File.WriteAllBytes(
            fallbackAttributesPath,
            "# local attributes\r\n"u8.ToArray());
        File.WriteAllBytes(
            fallbackIgnorePath,
            "# local ignores\r\n"u8.ToArray());
        File.SetAttributes(
            fallbackIgnorePath,
            File.GetAttributes(fallbackIgnorePath) |
            FileAttributes.ReadOnly);
        var paths = new[]
        {
            attributesPath,
            ignorePath,
            fallbackAttributesPath,
            fallbackIgnorePath,
        };
        var bytes = paths.ToDictionary(
            path => path,
            File.ReadAllBytes);
        var attributes = paths.ToDictionary(
            path => path,
            File.GetAttributes);

        Assert.That(
            () => FolderProjectGitRepository.Initialize(project.Path),
            Throws.Exception);

        Assert.Multiple(() =>
        {
            Assert.That(
                FolderProjectGitRepository.IsRepository(project.Path),
                Is.True);
            foreach (var path in paths)
            {
                Assert.That(
                    File.ReadAllBytes(path),
                    Is.EqualTo(bytes[path]),
                    path);
                Assert.That(
                    File.GetAttributes(path),
                    Is.EqualTo(attributes[path]),
                    path);
            }
        });
    }

    [Test]
    public void Initialize_Utf8BomPolicy_PreservesBomPayloadAndIsIdempotent()
    {
        using var project = new TemporaryDirectory("utf8-bom");
        var path = Path.Combine(project.Path, ".gitattributes");
        var payload = Encoding.UTF8.GetBytes("# 用户规则\r\n*.txt text\r\n");
        File.WriteAllBytes(
            path,
            Encoding.UTF8.GetPreamble().Concat(payload).ToArray());

        FolderProjectGitRepository.Initialize(project.Path);
        var firstUpdate = File.ReadAllBytes(path);
        FolderProjectGitRepository.Initialize(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                firstUpdate.Take(Encoding.UTF8.GetPreamble().Length),
                Is.EqualTo(Encoding.UTF8.GetPreamble()));
            Assert.That(ContainsSequence(firstUpdate, payload), Is.True);
            Assert.That(
                Encoding.UTF8.GetString(
                    firstUpdate[Encoding.UTF8.GetPreamble().Length..]),
                Does.StartWith("* -text"));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(firstUpdate));
        });
    }

    [Test]
    public void Initialize_Utf16Policy_PreservesBomPayloadAndIsIdempotent()
    {
        using var project = new TemporaryDirectory("utf16");
        var path = Path.Combine(project.Path, ".gitignore");
        var payload = Encoding.Unicode.GetBytes("# 用户规则\r\ncustom.bin\r\n");
        var original = Encoding.Unicode.GetPreamble()
            .Concat(payload)
            .ToArray();
        File.WriteAllBytes(path, original);

        FolderProjectGitRepository.Initialize(project.Path);
        var firstUpdate = File.ReadAllBytes(path);
        var fallbackPath = Path.Combine(
            project.Path,
            ".git",
            "info",
            "exclude");
        var firstFallbackUpdate = File.ReadAllBytes(fallbackPath);
        FolderProjectGitRepository.Initialize(project.Path);
        var residuePath =
            "asset.0123456789abcdef0123456789abcdef.tmp";
        File.WriteAllBytes(
            Path.Combine(project.Path, residuePath),
            [1]);
        using var repository = new Repository(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                firstUpdate.Take(Encoding.Unicode.GetPreamble().Length),
                Is.EqualTo(Encoding.Unicode.GetPreamble()));
            Assert.That(
                firstUpdate.Take(original.Length),
                Is.EqualTo(original));
            Assert.That(ContainsSequence(firstUpdate, payload), Is.True);
            Assert.That(
                Encoding.Unicode.GetString(
                    firstUpdate[Encoding.Unicode.GetPreamble().Length..]),
                Does.Contain(".rename"));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(firstUpdate));
            Assert.That(
                File.ReadAllBytes(fallbackPath),
                Is.EqualTo(firstFallbackUpdate));
            Assert.That(
                Encoding.ASCII.GetString(firstFallbackUpdate),
                Does.StartWith("*."));
            Assert.That(
                repository.RetrieveStatus(residuePath),
                Is.EqualTo(FileStatus.Ignored));
        });
    }

    [Test]
    public void Initialize_Utf32Policy_PreservesBomPayloadAndFallbackIsEffective()
    {
        using var project = new TemporaryDirectory("utf32");
        var path = Path.Combine(project.Path, ".gitignore");
        var encoding = new UTF32Encoding(
            bigEndian: false,
            byteOrderMark: true);
        var payload = encoding.GetBytes(
            "# 用户规则\r\ncustom.bin\r\n");
        var original = encoding.GetPreamble()
            .Concat(payload)
            .ToArray();
        File.WriteAllBytes(path, original);

        FolderProjectGitRepository.Initialize(project.Path);
        var firstUpdate = File.ReadAllBytes(path);
        var fallbackPath = Path.Combine(
            project.Path,
            ".git",
            "info",
            "exclude");
        var firstFallbackUpdate = File.ReadAllBytes(fallbackPath);
        FolderProjectGitRepository.Initialize(project.Path);
        var residuePath =
            "asset.0123456789abcdef0123456789abcdef.tmp";
        File.WriteAllBytes(
            Path.Combine(project.Path, residuePath),
            [1]);
        using var repository = new Repository(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                firstUpdate.Take(original.Length),
                Is.EqualTo(original));
            Assert.That(ContainsSequence(firstUpdate, payload), Is.True);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(firstUpdate));
            Assert.That(
                File.ReadAllBytes(fallbackPath),
                Is.EqualTo(firstFallbackUpdate));
            Assert.That(
                Encoding.ASCII.GetString(firstFallbackUpdate),
                Does.StartWith("*."));
            Assert.That(
                repository.RetrieveStatus(residuePath),
                Is.EqualTo(FileStatus.Ignored));
        });
    }

    [Test]
    public void Initialize_UppercaseResidue_IsIgnored()
    {
        using var project = new TemporaryDirectory("uppercase-residue");
        FolderProjectGitRepository.Initialize(project.Path);
        var residuePath =
            "asset.0123456789ABCDEF0123456789ABCDEF.tmp";
        File.WriteAllBytes(
            Path.Combine(project.Path, residuePath),
            [1]);
        using var repository = new Repository(project.Path);

        Assert.That(
            repository.RetrieveStatus(residuePath),
            Is.EqualTo(FileStatus.Ignored));
    }

    [Test]
    public void Initialize_Utf16Attributes_FallbackPreservesBinaryBytes()
    {
        using var project = new TemporaryDirectory("utf16-attributes");
        var path = Path.Combine(project.Path, ".gitattributes");
        var payload = Encoding.Unicode.GetBytes("# 用户规则\r\n");
        File.WriteAllBytes(
            path,
            Encoding.Unicode.GetPreamble().Concat(payload).ToArray());

        FolderProjectGitRepository.Initialize(project.Path);
        var firstUpdate = File.ReadAllBytes(path);
        var fallbackPath = Path.Combine(
            project.Path,
            ".git",
            "info",
            "attributes");
        var firstFallbackUpdate = File.ReadAllBytes(fallbackPath);
        FolderProjectGitRepository.Initialize(project.Path);

        var fileName = "binary.txt";
        var original = "first\r\nsecond\r\n"u8.ToArray();
        File.WriteAllBytes(
            Path.Combine(project.Path, fileName),
            original);
        byte[] committedBytes;
        using (var repository = new Repository(project.Path))
        {
            repository.Config.Set("core.autocrlf", true);
            Commands.Stage(repository, fileName);
            var signature = new Signature(
                "AE Test",
                "ae-test@example.invalid",
                DateTimeOffset.Now);
            var commit = repository.Commit(
                "binary",
                signature,
                signature);
            var blob = (Blob)commit.Tree[fileName].Target;
            using var stream = blob.GetContentStream();
            using var output = new MemoryStream();
            stream.CopyTo(output);
            committedBytes = output.ToArray();
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                firstUpdate.Take(Encoding.Unicode.GetPreamble().Length),
                Is.EqualTo(Encoding.Unicode.GetPreamble()));
            Assert.That(ContainsSequence(firstUpdate, payload), Is.True);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(firstUpdate));
            Assert.That(
                File.ReadAllBytes(fallbackPath),
                Is.EqualTo(firstFallbackUpdate));
            Assert.That(
                Encoding.ASCII.GetString(firstFallbackUpdate),
                Does.StartWith("* -text"));
            Assert.That(committedBytes, Is.EqualTo(original));
        });
    }

    [Test]
    public void Initialize_NonUtf8Policy_PreservesOriginalBytesAndIsIdempotent()
    {
        using var project = new TemporaryDirectory("ansi");
        var path = Path.Combine(project.Path, ".gitignore");
        var original = new byte[]
        {
            (byte)'#', (byte)' ', 0xD3, 0xC3, 0xBB, 0xA7,
            (byte)'\r', (byte)'\n',
            (byte)'c', (byte)'u', (byte)'s', (byte)'t', (byte)'o',
            (byte)'m', (byte)'.', (byte)'b', (byte)'i', (byte)'n',
            (byte)'\r', (byte)'\n',
        };
        File.WriteAllBytes(path, original);

        FolderProjectGitRepository.Initialize(project.Path);
        var firstUpdate = File.ReadAllBytes(path);
        FolderProjectGitRepository.Initialize(project.Path);
        var residuePath =
            "asset.0123456789abcdef0123456789abcdef.tmp";
        File.WriteAllBytes(
            Path.Combine(project.Path, residuePath),
            [1]);
        using var repository = new Repository(project.Path);

        Assert.Multiple(() =>
        {
            Assert.That(
                firstUpdate.Take(original.Length),
                Is.EqualTo(original));
            Assert.That(
                Encoding.ASCII.GetString(firstUpdate),
                Does.Contain(".tmp"));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(firstUpdate));
            Assert.That(
                repository.RetrieveStatus(residuePath),
                Is.EqualTo(FileStatus.Ignored));
        });
    }

    [Test]
    public void GitCommitAndCheckout_PreserveBinaryBytesExactly()
    {
        using var project = new TemporaryDirectory("binary 中文");
        FolderProjectContainer.Create(
            project.Path,
            new FolderProjectSettings { Name = "工程" }).Dispose();
        var binaryPath = Path.Combine(project.Path, "db", "item.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(binaryPath)!);
        var original = new byte[]
        {
            0, 13, 10, 26, 255, 10, 13, 0, 128, 42,
        };
        File.WriteAllBytes(binaryPath, original);
        FolderProjectGitRepository.Initialize(project.Path);

        string commitId;
        using (var repository = new Repository(project.Path))
        {
            Commands.Stage(repository, "*");
            var signature = new Signature(
                "AE Test",
                "ae-test@example.invalid",
                DateTimeOffset.Now);
            commitId = repository.Commit(
                "initial",
                signature,
                signature).Sha;
            File.WriteAllBytes(binaryPath, [9, 9, 9]);
            Commands.Checkout(
                repository,
                commitId,
                new CheckoutOptions
                {
                    CheckoutModifiers = CheckoutModifiers.Force,
                });
        }

        Assert.That(File.ReadAllBytes(binaryPath), Is.EqualTo(original));
    }

    private static bool ContainsSequence(
        IReadOnlyList<byte> bytes,
        IReadOnlyList<byte> sequence)
    {
        if (sequence.Count == 0)
            return true;

        for (var index = 0;
             index <= bytes.Count - sequence.Count;
             index++)
        {
            var matches = true;
            for (var offset = 0; offset < sequence.Count; offset++)
            {
                if (bytes[index + offset] == sequence[offset])
                    continue;

                matches = false;
                break;
            }

            if (matches)
                return true;
        }

        return false;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; }

        public TemporaryDirectory(string suffix)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"ae-folder-git-{suffix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                foreach (var file in Directory.EnumerateFiles(
                             Path,
                             "*",
                             SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(Path, true);
            }
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
