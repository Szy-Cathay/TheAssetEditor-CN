using System.Text;
using LibGit2Sharp;

namespace Shared.Core.PackFiles.Utility;

internal static class FolderProjectGitRepository
{
    private const string BinaryAttributeRule = "* -text";
    private static readonly string s_atomicGuidPattern =
        string.Concat(
            Enumerable.Repeat("[0-9a-f]", 32));
    private static readonly string[] s_ignoreRules =
    [
        $"*.{s_atomicGuidPattern}.tmp",
        $"*.{s_atomicGuidPattern}.rename",
    ];

    public static bool IsRepository(string projectRoot)
    {
        ValidateProjectRoot(projectRoot);
        return IsRepositoryRoot(Path.GetFullPath(projectRoot));
    }

    public static bool IsValidBranchName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        try
        {
            FolderProjectPathPolicy.NormalizeRelativePath(name);
            return Reference.IsValidName($"refs/heads/{name}");
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                  InvalidDataException or
                  NotSupportedException)
        {
            return false;
        }
    }

    public static void Initialize(string projectRoot)
    {
        ValidateProjectRoot(projectRoot);
        var root = Path.GetFullPath(projectRoot);
        var attributesPath = Path.Combine(root, ".gitattributes");
        var ignorePath = Path.Combine(root, ".gitignore");
        EnsurePolicyFileAvailable(attributesPath);
        EnsurePolicyFileAvailable(ignorePath);
        var markerPath = Path.Combine(root, ".git");
        if (File.Exists(markerPath))
        {
            throw new InvalidOperationException(
                "Linked Git worktrees and submodules are not supported.");
        }

        if (Directory.Exists(markerPath) &&
            !IsRepositoryRoot(root))
        {
            throw new InvalidOperationException(
                "The existing .git directory is not a valid local repository.");
        }

        var repositoryExisted = Directory.Exists(markerPath);
        var attributesSnapshot = PolicyFileSnapshot.Capture(attributesPath);
        var ignoreSnapshot = PolicyFileSnapshot.Capture(ignorePath);
        PolicyFileSnapshot? fallbackAttributesSnapshot = null;
        PolicyFileSnapshot? fallbackIgnoreSnapshot = null;
        try
        {
            var attributesEncoding =
                DetectEncoding(attributesSnapshot.Bytes);
            var ignoreEncoding =
                DetectEncoding(ignoreSnapshot.Bytes);
            var updatedAttributes = BuildUpdatedBytes(
                attributesSnapshot.Bytes,
                [BinaryAttributeRule],
                prepend: true);
            var updatedIgnore = BuildUpdatedBytes(
                ignoreSnapshot.Bytes,
                s_ignoreRules,
                prepend: false);

            if (!repositoryExisted)
                Repository.Init(root);

            if (RequiresRepositoryFallback(attributesEncoding))
            {
                fallbackAttributesSnapshot =
                    PolicyFileSnapshot.Capture(
                        Path.Combine(
                            markerPath,
                            "info",
                            "attributes"));
            }
            if (RequiresRepositoryFallback(ignoreEncoding))
            {
                fallbackIgnoreSnapshot =
                    PolicyFileSnapshot.Capture(
                        Path.Combine(
                            markerPath,
                            "info",
                            "exclude"));
            }

            WriteUpdatedPolicy(
                attributesSnapshot,
                updatedAttributes);
            WriteUpdatedPolicy(
                ignoreSnapshot,
                updatedIgnore);
            if (fallbackAttributesSnapshot != null)
            {
                WriteUpdatedPolicy(
                    fallbackAttributesSnapshot,
                    BuildUpdatedAsciiFallbackBytes(
                        fallbackAttributesSnapshot.Bytes,
                        [BinaryAttributeRule]));
            }
            if (fallbackIgnoreSnapshot != null)
            {
                WriteUpdatedPolicy(
                    fallbackIgnoreSnapshot,
                    BuildUpdatedAsciiFallbackBytes(
                        fallbackIgnoreSnapshot.Bytes,
                        s_ignoreRules));
            }
        }
        catch (Exception failure)
        {
            var rollbackFailures = new List<Exception>();
            if (fallbackAttributesSnapshot != null)
            {
                TryRollback(
                    () => fallbackAttributesSnapshot.Restore(),
                    rollbackFailures);
            }
            if (fallbackIgnoreSnapshot != null)
            {
                TryRollback(
                    () => fallbackIgnoreSnapshot.Restore(),
                    rollbackFailures);
            }
            TryRollback(
                () => attributesSnapshot.Restore(),
                rollbackFailures);
            TryRollback(
                () => ignoreSnapshot.Restore(),
                rollbackFailures);
            if (!repositoryExisted)
            {
                TryRollback(
                    () => DeleteRepositoryMetadata(markerPath),
                    rollbackFailures);
            }

            if (rollbackFailures.Count != 0)
            {
                throw new AggregateException(
                    "Git initialization failed and rollback was incomplete.",
                    [failure, .. rollbackFailures]);
            }

            throw;
        }
    }

    private static void EnsurePolicyFileAvailable(string path)
    {
        if (Directory.Exists(path))
        {
            throw new InvalidOperationException(
                $"A resource directory conflicts with '{Path.GetFileName(path)}'.");
        }
    }

    private static bool IsRepositoryRoot(string root)
    {
        var markerPath = Path.Combine(root, ".git");
        if (!Directory.Exists(markerPath))
            return false;
        if ((File.GetAttributes(markerPath) &
             FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        try
        {
            using var repository = new Repository(root);
            var workingDirectory =
                repository.Info.WorkingDirectory;
            return !repository.Info.IsBare &&
                   !string.IsNullOrWhiteSpace(workingDirectory) &&
                   Path.TrimEndingDirectorySeparator(
                       Path.GetFullPath(workingDirectory))
                       .Equals(
                           Path.TrimEndingDirectorySeparator(root),
                           StringComparison.OrdinalIgnoreCase);
        }
        catch (RepositoryNotFoundException)
        {
            return false;
        }
    }

    private static void ValidateProjectRoot(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) ||
            !Directory.Exists(projectRoot))
        {
            throw new DirectoryNotFoundException(projectRoot);
        }

        FolderProjectPathPolicy.ResolveFilePath(
            projectRoot,
            ".folder-project-git-root-check");
    }

    private static byte[] BuildUpdatedBytes(
        byte[] existing,
        IReadOnlyList<string> rules,
        bool prepend)
    {
        var encoding = DetectEncoding(existing);
        var missingRules = rules
            .Where(
                rule => !ContainsRule(
                    existing,
                    encoding,
                    rule))
            .ToList();
        if (missingRules.Count == 0)
            return existing;

        const string newLine = "\r\n";
        var newRules = Encode(
            string.Join(newLine, missingRules) + newLine,
            encoding);
        if (existing.Length == 0)
            return newRules;

        if (prepend)
        {
            var preambleLength = encoding?.PreambleLength ?? 0;
            return
            [
                .. existing.AsSpan(0, preambleLength),
                .. newRules,
                .. existing.AsSpan(preambleLength),
            ];
        }

        var separator = EndsWithNewLine(existing, encoding)
            ? []
            : Encode(newLine, encoding);
        return
        [
            .. existing,
            .. separator,
            .. newRules,
        ];
    }

    private static PolicyEncoding? DetectEncoding(
        ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(
                new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
        {
            return new PolicyEncoding(
                new UTF32Encoding(
                    bigEndian: true,
                    byteOrderMark: false),
                4);
        }
        if (bytes.StartsWith(
                new byte[] { 0xFF, 0xFE, 0x00, 0x00 }))
        {
            return new PolicyEncoding(
                new UTF32Encoding(
                    bigEndian: false,
                    byteOrderMark: false),
                4);
        }
        if (bytes.StartsWith(
                new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            return new PolicyEncoding(
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false),
                3);
        }
        if (bytes.StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            return new PolicyEncoding(
                new UnicodeEncoding(
                    bigEndian: true,
                    byteOrderMark: false),
                2);
        }
        if (bytes.StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            return new PolicyEncoding(
                new UnicodeEncoding(
                    bigEndian: false,
                    byteOrderMark: false),
                2);
        }

        return null;
    }

    private static byte[] BuildUpdatedAsciiFallbackBytes(
        byte[] existing,
        IReadOnlyList<string> rules)
    {
        var missingRules = rules
            .Where(
                rule => !ContainsRule(
                    existing,
                    encoding: null,
                    rule))
            .ToList();
        if (missingRules.Count == 0)
            return existing;

        var newRules = Encoding.ASCII.GetBytes(
            string.Join("\r\n", missingRules) + "\r\n");
        return
        [
            .. newRules,
            .. existing,
        ];
    }

    private static bool RequiresRepositoryFallback(
        PolicyEncoding? encoding)
    {
        return encoding != null &&
               encoding.Encoding.CodePage != Encoding.UTF8.CodePage;
    }

    private static bool ContainsRule(
        byte[] bytes,
        PolicyEncoding? encoding,
        string rule)
    {
        if (encoding != null)
        {
            var text = encoding.Encoding.GetString(
                bytes.AsSpan(encoding.PreambleLength));
            return text
                .Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
                .Any(
                    line => line.Trim().Equals(
                        rule,
                        StringComparison.Ordinal));
        }

        var ruleBytes = Encoding.ASCII.GetBytes(rule);
        var start = 0;
        while (start <= bytes.Length)
        {
            var end = start;
            while (end < bytes.Length &&
                   bytes[end] is not (byte)'\r' and not (byte)'\n')
            {
                end++;
            }

            var trimmedStart = start;
            var trimmedEnd = end;
            while (trimmedStart < trimmedEnd &&
                   bytes[trimmedStart] is (byte)' ' or (byte)'\t')
            {
                trimmedStart++;
            }
            while (trimmedEnd > trimmedStart &&
                   bytes[trimmedEnd - 1] is (byte)' ' or (byte)'\t')
            {
                trimmedEnd--;
            }

            if (bytes.AsSpan(
                    trimmedStart,
                    trimmedEnd - trimmedStart)
                .SequenceEqual(ruleBytes))
            {
                return true;
            }

            while (end < bytes.Length &&
                   bytes[end] is (byte)'\r' or (byte)'\n')
            {
                end++;
            }
            if (end == start)
                break;
            start = end;
        }

        return false;
    }

    private static byte[] Encode(
        string value,
        PolicyEncoding? encoding)
    {
        return (encoding?.Encoding ?? Encoding.UTF8)
            .GetBytes(value);
    }

    private static bool EndsWithNewLine(
        byte[] bytes,
        PolicyEncoding? encoding)
    {
        if (bytes.Length == (encoding?.PreambleLength ?? 0))
            return true;
        if (encoding == null)
        {
            return bytes[^1] is (byte)'\r' or (byte)'\n';
        }

        var text = encoding.Encoding.GetString(
            bytes.AsSpan(encoding.PreambleLength));
        return text.EndsWith('\r') || text.EndsWith('\n');
    }

    private static void WriteUpdatedPolicy(
        PolicyFileSnapshot snapshot,
        byte[] updated)
    {
        if (snapshot.Bytes.AsSpan().SequenceEqual(updated))
            return;

        WriteAllBytesAtomic(snapshot.Path, updated);
        if (snapshot.Exists)
            File.SetAttributes(snapshot.Path, snapshot.Attributes);
    }

    private static void WriteAllBytesAtomic(
        string path,
        byte[] contents)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tempPath, contents);
            File.Move(tempPath, path, true);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static void TryRollback(
        Action rollback,
        ICollection<Exception> failures)
    {
        try
        {
            rollback();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static void DeleteRepositoryMetadata(string markerPath)
    {
        if (!Directory.Exists(markerPath))
            return;

        foreach (var file in Directory.EnumerateFiles(
                     markerPath,
                     "*",
                     SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        foreach (var directory in Directory.EnumerateDirectories(
                     markerPath,
                     "*",
                     SearchOption.AllDirectories)
                 .OrderByDescending(path => path.Length))
        {
            File.SetAttributes(directory, FileAttributes.Normal);
        }
        File.SetAttributes(markerPath, FileAttributes.Normal);
        Directory.Delete(markerPath, true);
    }

    private sealed record PolicyEncoding(
        Encoding Encoding,
        int PreambleLength);

    private sealed record PolicyFileSnapshot(
        string Path,
        bool Exists,
        byte[] Bytes,
        FileAttributes Attributes)
    {
        public static PolicyFileSnapshot Capture(string path)
        {
            if (!File.Exists(path))
            {
                return new PolicyFileSnapshot(
                    path,
                    false,
                    [],
                    FileAttributes.Normal);
            }

            return new PolicyFileSnapshot(
                path,
                true,
                File.ReadAllBytes(path),
                File.GetAttributes(path));
        }

        public void Restore()
        {
            if (!Exists)
            {
                if (File.Exists(Path))
                {
                    File.SetAttributes(Path, FileAttributes.Normal);
                    File.Delete(Path);
                }
                return;
            }

            if (File.Exists(Path))
                File.SetAttributes(Path, FileAttributes.Normal);
            File.WriteAllBytes(Path, Bytes);
            File.SetAttributes(Path, Attributes);
        }
    }
}
