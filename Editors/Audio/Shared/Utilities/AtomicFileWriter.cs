using System;
using System.IO;

namespace Editors.Audio.Shared.Utilities
{
    public static class AtomicFileWriter
    {
        public static void WriteAllBytes(
            string destinationPath,
            byte[] data)
        {
            WriteAllBytes(destinationPath, data, overwrite: true);
        }

        public static void WriteAllBytes(
            string destinationPath,
            byte[] data,
            bool overwrite)
        {
            var fullDestinationPath = Path.GetFullPath(destinationPath);
            var directory = Path.GetDirectoryName(fullDestinationPath);
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException(
                    "The destination path has no parent directory.",
                    nameof(destinationPath));

            Directory.CreateDirectory(directory);
            var tempPath = Path.Combine(
                directory,
                $".{Path.GetFileName(destinationPath)}." +
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
                    stream.Write(data);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(
                    tempPath,
                    fullDestinationPath,
                    overwrite);
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
}
