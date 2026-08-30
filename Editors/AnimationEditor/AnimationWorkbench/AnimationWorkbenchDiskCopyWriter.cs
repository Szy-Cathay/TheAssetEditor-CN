using System.IO;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

internal sealed class AnimationWorkbenchDestinationExistsException(
    string destinationPath) :
    IOException($"The destination already exists: {destinationPath}");

internal static class AnimationWorkbenchDiskCopyWriter
{
    public static void Write(
        string destinationPath,
        byte[] bytes)
    {
        var fullDestinationPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullDestinationPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException(
                "The destination path has no parent directory.",
                nameof(destinationPath));
        }

        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullDestinationPath)}." +
            $"{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(
                    tempPath,
                    fullDestinationPath,
                    overwrite: false);
            }
            catch (IOException) when (File.Exists(fullDestinationPath))
            {
                throw new AnimationWorkbenchDestinationExistsException(
                    fullDestinationPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Preserve the original write exception.
                }
            }
        }
    }
}
