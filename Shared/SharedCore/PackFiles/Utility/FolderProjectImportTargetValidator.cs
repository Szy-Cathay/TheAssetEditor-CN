using System.IO;

namespace Shared.Core.PackFiles.Utility;

public static class FolderProjectImportTargetValidator
{
    public static string ValidateEmptyTarget(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            throw new InvalidDataException("The project folder is empty.");

        var targetRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectRoot));
        if (!Directory.Exists(targetRoot))
            throw new DirectoryNotFoundException(targetRoot);

        FolderProjectPathPolicy.ResolveFilePath(
            targetRoot,
            ".folder-project-import-check");
        if (Directory.EnumerateFileSystemEntries(targetRoot).Any())
        {
            throw new InvalidOperationException(
                "The import target folder must be empty.");
        }

        return targetRoot;
    }

    public static bool IsEmptyTarget(string projectRoot)
    {
        return IsEmptyTarget(() => ValidateEmptyTarget(projectRoot));
    }

    internal static bool IsEmptyTarget(Func<string> validateTarget)
    {
        try
        {
            validateTarget();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
