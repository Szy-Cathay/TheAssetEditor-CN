using Shared.Core.PackFiles.Models;

namespace Shared.Core.PackFiles.Utility;

public interface IFolderProjectFactory
{
    FolderProjectContainer Create(
        string projectRoot,
        FolderProjectSettings settings);

    FolderProjectContainer Open(string projectRoot);

    FolderProjectContainer ImportPack(
        PackFileContainer source,
        string projectRoot,
        FolderProjectSettings settings);

    FolderProjectContainer ImportPack(
        PackFileContainer source,
        string projectRoot,
        FolderProjectSettings settings,
        Action<FolderProjectImportProgress> reportProgress);

    FolderProjectContainer ImportPack(
        PackFileContainer source,
        string projectRoot,
        FolderProjectSettings settings,
        Action<FolderProjectImportProgress> reportProgress,
        CancellationToken cancellationToken);
}

public sealed record FolderProjectImportProgress(
    string RelativePath,
    int CurrentIndex,
    int Total,
    bool IsCompressed);

public sealed class FolderProjectFactory : IFolderProjectFactory
{
    public FolderProjectContainer Create(
        string projectRoot,
        FolderProjectSettings settings)
    {
        return FolderProjectContainer.Create(projectRoot, settings);
    }

    public FolderProjectContainer Open(string projectRoot)
    {
        return FolderProjectContainer.Open(
            projectRoot,
            mutateDisk: false);
    }

    public FolderProjectContainer ImportPack(
        PackFileContainer source,
        string projectRoot,
        FolderProjectSettings settings)
    {
        return ImportPack(source, projectRoot, settings, null);
    }

    public FolderProjectContainer ImportPack(
        PackFileContainer source,
        string projectRoot,
        FolderProjectSettings settings,
        Action<FolderProjectImportProgress>? reportProgress)
    {
        return ImportPack(
            source,
            projectRoot,
            settings,
            reportProgress,
            CancellationToken.None);
    }

    public FolderProjectContainer ImportPack(
        PackFileContainer source,
        string projectRoot,
        FolderProjectSettings settings,
        Action<FolderProjectImportProgress>? reportProgress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);

        if (source is FolderProjectContainer folderProject)
        {
            return folderProject.ExecuteSynchronized(
                () => ImportPackCore(
                    source,
                    projectRoot,
                    settings,
                    reportProgress,
                    cancellationToken));
        }

        return ImportPackCore(
            source,
            projectRoot,
            settings,
            reportProgress,
            cancellationToken);
    }

    private static FolderProjectContainer ImportPackCore(
        PackFileContainer source,
        string projectRoot,
        FolderProjectSettings settings,
        Action<FolderProjectImportProgress>? reportProgress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var targetRoot = FolderProjectImportTargetValidator
            .ValidateEmptyTarget(projectRoot);
        if (!string.IsNullOrWhiteSpace(settings.OutputPackPath))
        {
            FolderProjectPathPolicy.EnsureOutputOutsideProject(
                targetRoot,
                settings.OutputPackPath);
        }

        var entries = ValidateSourceEntries(source);
        if (source.FileList.Keys.Any(
                FolderProjectPathPolicy.IsCorruptionDetectionPath))
        {
            settings.EnablePackFileCorruptionDetection = true;
        }
        settings.PackFileVersion = source.Header.Version;
        settings.PackFileType = source.Header.PackFileType;
        settings.DependantFiles =
            source.Header.DependantFiles.ToList();

        var parentDirectory = Path.GetDirectoryName(targetRoot);
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            throw new InvalidDataException(
                "A drive root cannot be used as an import target.");
        }

        var stagingRoot = Path.Combine(
            parentDirectory,
            $".{Path.GetFileName(targetRoot)}.aeimport-{Guid.NewGuid():N}");
        var packedParents = source.FileList.Values
            .Select(file => file.DataSource)
            .OfType<PackedFileSource>()
            .Select(dataSource => dataSource.Parent)
            .Distinct()
            .ToList();

        Directory.CreateDirectory(stagingRoot);
        try
        {
            for (var index = 0; index < entries.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (relativePath, file) = entries[index];
                reportProgress?.Invoke(
                    new FolderProjectImportProgress(
                        relativePath.Replace('\\', '/'),
                        index + 1,
                        entries.Count,
                        file.DataSource is PackedFileSource
                        {
                            IsCompressed: true,
                        }));
                var outputPath = FolderProjectPathPolicy.ResolveFilePath(
                    stagingRoot,
                    relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllBytes(outputPath, file.DataSource.ReadData());
            }

            cancellationToken.ThrowIfCancellationRequested();
            settings.Save(stagingRoot);

            FolderProjectImportTargetValidator
                .ValidateEmptyTarget(targetRoot);
            Directory.Delete(targetRoot, false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(stagingRoot, targetRoot);
            }
            catch
            {
                Directory.CreateDirectory(targetRoot);
                throw;
            }

            return FolderProjectContainer.Open(targetRoot);
        }
        finally
        {
            foreach (var parent in packedParents)
                parent.CloseStream();

            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, true);
        }
    }

    private static List<(string RelativePath, PackFile File)>
        ValidateSourceEntries(PackFileContainer source)
    {
        var paths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var entries = new List<(string RelativePath, PackFile File)>();

        foreach (var (path, file) in source.FileList)
        {
            var normalized =
                FolderProjectPathPolicy.NormalizeRelativePath(path);
            if (FolderProjectPathPolicy.IsExcludedPath(normalized))
                continue;
            if (!paths.Add(normalized))
            {
                throw new InvalidDataException(
                    $"Duplicate project path: {normalized}");
            }

            entries.Add((normalized, file));
        }

        return entries;
    }

}
