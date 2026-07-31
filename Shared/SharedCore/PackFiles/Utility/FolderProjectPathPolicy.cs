namespace Shared.Core.PackFiles.Utility;

public static class FolderProjectPathPolicy
{
    private static readonly HashSet<string> s_reservedNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "COM1",
            "COM2",
            "COM3",
            "COM4",
            "COM5",
            "COM6",
            "COM7",
            "COM8",
            "COM9",
            "LPT1",
            "LPT2",
            "LPT3",
            "LPT4",
            "LPT5",
            "LPT6",
            "LPT7",
            "LPT8",
            "LPT9",
        };
    private static readonly HashSet<string>
        s_corruptionDetectionDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "!!!packfile_corruction_detection",
            "packfile_corruction_detection",
            "zzzz_packfile_corruction_detection",
        };
    internal static IReadOnlyList<string> CorruptionDetectionFilePaths
    {
        get;
    } =
    [
        @"!!!packfile_corruction_detection\packfile_corruction_detection_1.txt",
        @"packfile_corruction_detection\packfile_corruction_detection_2.txt",
        @"zzzz_packfile_corruction_detection\packfile_corruction_detection_3.txt",
    ];

    public static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("Folder-project paths cannot be empty.");

        var normalized = path.Replace('/', '\\');
        while (normalized.StartsWith(@".\", StringComparison.Ordinal))
            normalized = normalized[2..];

        if (Path.IsPathRooted(normalized) ||
            normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Folder-project paths must be relative.");
        }

        var segments = normalized.Split(
            '\\',
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new InvalidDataException("Folder-project paths cannot be empty.");

        foreach (var segment in segments)
            ValidateSegment(segment);

        return string.Join('\\', segments);
    }

    public static string ResolveFilePath(string projectRoot, string relativePath)
    {
        var root = NormalizeRoot(projectRoot);
        EnsureNoReparsePoints(root);

        var normalized = NormalizeRelativePath(relativePath);
        var fullPath = Path.GetFullPath(Path.Combine(root, normalized));
        if (!IsWithinRoot(root, fullPath))
            throw new InvalidDataException("The path escapes the folder project.");

        EnsureNoReparsePoints(root, fullPath);
        return fullPath;
    }

    public static void EnsureOutputOutsideProject(
        string projectRoot,
        string outputPath)
    {
        var root = NormalizeRoot(projectRoot);
        EnsureNoReparsePoints(root);
        var output = Path.GetFullPath(outputPath);
        EnsureNoReparsePointsInAbsolutePath(output);
        if (IsWithinRoot(root, output))
        {
            throw new InvalidDataException(
                "The generated pack must be outside the folder project.");
        }
    }

    public static bool IsControlFile(string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (normalized.Contains(Path.DirectorySeparatorChar))
            return false;

        return normalized.Equals(
                   "aeproject.cn.json",
                   StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(
                   "aeproject.json",
                   StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(
                   "project_ignore.json",
                   StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(
                   ".asseteditor-project",
                   StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMetadataPath(string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var fileName = Path.GetFileName(normalized);
        if (IsMetadataDirectoryPath(normalized))
            return true;

        return fileName.Equals(
                   ".gitignore",
                   StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals(
                   ".gitattributes",
                   StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMetadataDirectoryPath(
        string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var segments = normalized.Split('\\');
        return segments.Any(
                   segment => segment.Equals(
                       ".git",
                       StringComparison.OrdinalIgnoreCase)) ||
               s_corruptionDetectionDirectories.Contains(segments[0]) ||
               IsAtomicResidue(segments[^1]);
    }

    public static bool IsCorruptionDetectionPath(
        string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var rootDirectory = normalized.Split('\\', 2)[0];
        return s_corruptionDetectionDirectories.Contains(rootDirectory);
    }

    public static bool IsExcludedPath(string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        return IsControlFile(normalized) ||
               IsMetadataPath(normalized);
    }

    public static string EnsureResourcePath(string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (IsExcludedPath(normalized))
        {
            throw new InvalidDataException(
                "The path is reserved for folder-project metadata.");
        }

        return normalized;
    }

    public static string EnsureResourceDirectoryPath(
        string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (IsMetadataDirectoryPath(normalized))
        {
            throw new InvalidDataException(
                "The path is reserved for folder-project metadata.");
        }

        return normalized;
    }

    public static bool TryNormalizeResourcePath(
        string? relativePath,
        out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        try
        {
            normalized = EnsureResourcePath(relativePath);
            return true;
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                  InvalidDataException or
                  NotSupportedException)
        {
            return false;
        }
    }

    public static bool TryNormalizeResourceDirectoryPath(
        string? relativePath,
        out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        try
        {
            normalized = EnsureResourceDirectoryPath(relativePath);
            return true;
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                  InvalidDataException or
                  NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsAtomicResidue(string fileName)
    {
        const int guidLength = 32;
        string suffix;
        if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            suffix = ".tmp";
        else if (fileName.EndsWith(
                     ".rename",
                     StringComparison.OrdinalIgnoreCase))
        {
            suffix = ".rename";
        }
        else
            return false;

        var guidStart = fileName.Length - suffix.Length - guidLength;
        if (guidStart < 1 || fileName[guidStart - 1] != '.')
            return false;

        return fileName
            .AsSpan(guidStart, guidLength)
            .IndexOfAnyExcept(
                "0123456789abcdefABCDEF") == -1;
    }

    private static string NormalizeRoot(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            throw new InvalidDataException("The folder-project root is empty.");

        var root = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);
        var pathRoot = Path.GetPathRoot(root);
        if (!string.IsNullOrWhiteSpace(pathRoot) &&
            string.Equals(
                root.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                pathRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "A drive root cannot be used as a folder project.");
        }
        return Path.TrimEndingDirectorySeparator(root);
    }

    private static void ValidateSegment(string segment)
    {
        if (segment is "." or "..")
            throw new InvalidDataException("Relative traversal is not allowed.");

        if (segment.EndsWith(' ') || segment.EndsWith('.'))
        {
            throw new InvalidDataException(
                "Path segments cannot end with a space or period.");
        }

        if (segment.IndexOf(':') >= 0 ||
            segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException(
                "The path contains invalid characters.");
        }

        var deviceName = Path.GetFileNameWithoutExtension(segment);
        if (s_reservedNames.Contains(deviceName))
            throw new InvalidDataException("Reserved device names are not allowed.");
    }

    private static bool IsWithinRoot(string root, string path)
    {
        if (string.Equals(root, path, StringComparison.OrdinalIgnoreCase))
            return true;

        var rootPrefix =
            Path.TrimEndingDirectorySeparator(root) +
            Path.DirectorySeparatorChar;
        return path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureNoReparsePoints(
        string root,
        string? target = null)
    {
        var current = root;
        ThrowIfReparsePoint(current);

        if (target == null)
            return;

        var relative = Path.GetRelativePath(root, target);
        foreach (var segment in relative.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
                break;
            ThrowIfReparsePoint(current);
        }
    }

    private static void EnsureNoReparsePointsInAbsolutePath(
        string path)
    {
        var pathRoot = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(pathRoot))
            throw new InvalidDataException("The output path has no root.");

        var current = pathRoot;
        ThrowIfReparsePoint(current);
        var relative = Path.GetRelativePath(pathRoot, path);
        foreach (var segment in relative.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
                break;
            ThrowIfReparsePoint(current);
        }
    }

    private static void ThrowIfReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Symbolic links and directory junctions are not supported.");
        }
    }
}
