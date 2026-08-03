using System.Text;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;

namespace Shared.Core.PackFiles.Serialization
{
    record FileCompressionInfo(CompressionFormat IntendedCompressionFormat, bool DecompressBeforeSaving);

    class PackFileWriteInformation(PackFile pf, string fullFileName, long sizePosition, FileCompressionInfo compressionInfo)
    {
        public PackFile PackFile { get; set; } = pf;
        public string FullFileName { get; set; } = fullFileName;
        public long SizePosition { get; set; } = sizePosition;
        public FileCompressionInfo CompressionInfo { get; set; } = compressionInfo;
    }

    internal sealed record PendingDataSourceUpdate(
        PackFile PackFile,
        PackedFileSource DataSource);

    internal sealed class PackFileSerializationResult(IReadOnlyList<PendingDataSourceUpdate> updates)
    {
        internal IReadOnlyList<PendingDataSourceUpdate> Updates { get; } = updates;

        internal void Commit()
        {
            foreach (var update in Updates)
                update.PackFile.DataSource = update.DataSource;
        }
    }

    static class PackFileSerializerWriter
    {
        private static readonly IReadOnlyList<(string Path, string Content)>
            s_corruptionDetectionFiles =
            [
                (
                    @"!!!packfile_corruction_detection\packfile_corruction_detection_1.txt",
                    "This file is here to validate that the packfile has not been corrupted while saving. This is check 1"),
                (
                    @"packfile_corruction_detection\packfile_corruction_detection_2.txt",
                    "This file is here to validate that the packfile has not been corrupted while saving. This is check 2"),
                (
                    @"zzzz_packfile_corruction_detection\packfile_corruction_detection_3.txt",
                    "This file is here to validate that the packfile has not been corrupted while saving. This is check 3"),
            ];

        internal static PackFileSerializationResult SaveToByteArray(
            string outputFileName,
            PackFileContainer container,
            BinaryWriter writer,
            GameInformation currentGameInformation,
            bool enableCompression = true,
            bool enableCorruptionDetection = false)
        {
            if (container.Header.HasEncryptedData || container.Header.HasEncryptedIndex)
                throw new InvalidOperationException("Saving encrypted packs is not supported.");

            var sortedFiles = container.FileList
                .Where(
                    file =>
                        (container is not FolderProjectContainer ||
                         !FolderProjectPathPolicy.IsExcludedPath(
                             file.Key)) &&
                        (!enableCorruptionDetection ||
                         !IsCorruptionDetectionFile(file.Key)))
                .OrderBy(file => file.Key, StringComparer.Ordinal)
                .ToList();
            if (enableCorruptionDetection)
                AddCorruptionDetectionFiles(sortedFiles);

            var headerSpecificBytes = ComputeFileHeaderSpecificByte(container);
            var fileNamesOffset = ComputeFileNameOffset(headerSpecificBytes, sortedFiles);

            // Update and write header
            container.Header.FileCount = (uint)sortedFiles.Count;
            WriteHeader(container.Header, (uint)fileNamesOffset, writer);

            // Write the core of the file
            var fileMetaDataTable = BuildMetaDataTable(sortedFiles, container, currentGameInformation, enableCompression);
            SerializeFileTable(fileMetaDataTable, container, writer);
            var outputParent = new PackedFileSourceParent { FilePath = outputFileName };
            var pendingUpdates = new List<PendingDataSourceUpdate>(fileMetaDataTable.Count);
            SerializeFileBlob(fileMetaDataTable, container, writer, outputParent, pendingUpdates);
            if (enableCorruptionDetection)
            {
                ValidateCorruptionDetectionFiles(
                    outputFileName,
                    writer);
            }
            return new PackFileSerializationResult(pendingUpdates);
        }

        private static bool IsCorruptionDetectionFile(
            string relativePath)
        {
            var normalizedPath = relativePath.Replace('/', '\\');
            var fileName = Path.GetFileName(normalizedPath);
            return s_corruptionDetectionFiles.Any(
                detectionFile =>
                    string.Equals(
                        normalizedPath,
                        detectionFile.Path,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        fileName,
                        Path.GetFileName(detectionFile.Path),
                        StringComparison.OrdinalIgnoreCase));
        }

        private static void AddCorruptionDetectionFiles(
            List<KeyValuePair<string, PackFile>> files)
        {
            foreach (var detectionFile in s_corruptionDetectionFiles)
            {
                var data = Encoding.UTF8.GetBytes(
                    detectionFile.Content);
                files.Add(
                    new KeyValuePair<string, PackFile>(
                        detectionFile.Path,
                        new PackFile(
                            Path.GetFileName(detectionFile.Path),
                            new MemorySource(data))));
            }

            files.Sort(
                (left, right) =>
                    StringComparer.Ordinal.Compare(
                        left.Key,
                        right.Key));
        }

        private static void ValidateCorruptionDetectionFiles(
            string outputFileName,
            BinaryWriter writer)
        {
            writer.Flush();
            var stream = writer.BaseStream;
            if (!stream.CanRead || !stream.CanSeek)
            {
                throw new InvalidOperationException(
                    "Packfile corruption detection requires a readable, seekable output stream.");
            }

            var originalPosition = stream.Position;
            try
            {
                stream.Position = 0;
                using var reader = new BinaryReader(
                    stream,
                    Encoding.UTF8,
                    true);
                var loadedPack = PackFileSerializerLoader.Load(
                    outputFileName,
                    stream.Length,
                    reader,
                    new CaPackDuplicateFileResolver());

                foreach (var detectionFile
                         in s_corruptionDetectionFiles)
                {
                    if (!loadedPack.FileList.TryGetValue(
                            detectionFile.Path.ToLowerInvariant(),
                            out var packFile))
                    {
                        throw new InvalidDataException(
                            $"Packfile corruption detection failed. Missing validation file '{detectionFile.Path}'.");
                    }

                    var data =
                        packFile.DataSource is PackedFileSource
                            packedFileSource
                            ? packedFileSource.ReadData(stream)
                            : packFile.DataSource.ReadData();
                    var actualContent =
                        Encoding.UTF8.GetString(data);
                    if (!string.Equals(
                            actualContent,
                            detectionFile.Content,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"Packfile corruption detection failed. Validation file '{detectionFile.Path}' had unexpected content.");
                    }
                }
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    "Packfile corruption detection failed while reading the generated pack.",
                    exception);
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        public static void WriteHeader(PFHeader header, uint fileContentSize, BinaryWriter writer)
        {
            var packFileTypeStr = PackFileVersionConverter.ToString(header.Version);        // 4
            foreach (var c in packFileTypeStr)
                writer.Write(c);

            writer.Write(header.ByteMask);                                                  // 8
            writer.Write(header.DependantFiles.Count);                                      // 12

            var pack_file_index_size = 0;
            foreach (var file in header.DependantFiles)
                pack_file_index_size += file.Length + 1;

            writer.Write(pack_file_index_size);                                             // 16
            writer.Write(header.FileCount);                                                 // 20
            writer.Write(fileContentSize);                                                  // 24

            switch (header.Version)
            {
                case PackFileVersion.PFH0:
                    break;// Nothing needed to do
                case PackFileVersion.PFH2:
                case PackFileVersion.PFH3: // 64 bit timestamp
                    writer.Write(0);
                    writer.Write(0);
                    break;
                case PackFileVersion.PFH4:
                case PackFileVersion.PFH5:
                    if (header.HasExtendedHeader)
                        throw new Exception("Not supported packfile type");

                    writer.Write(PFHeader.DefaultTimeStamp);
                    break;

                default:
                    throw new Exception("Not supported packfile type");
            }

            foreach (var file in header.DependantFiles)
            {
                var fileNameBytes = Encoding.UTF8.GetBytes(file);
                writer.Write(fileNameBytes);
                writer.Write((byte)0);
            }
        }

        static long ComputeFileHeaderSpecificByte(PackFileContainer container)
        {
            long headerSpecificBytes = 0;
            if (container.Header.HasIndexWithTimeStamp)
                headerSpecificBytes += 4;
            if (container.Header.Version == PackFileVersion.PFH5)
                headerSpecificBytes += 1;

            return headerSpecificBytes;
        }

        static long ComputeFileNameOffset(long headerSpecificBytePerFile, IList<KeyValuePair<string, PackFile>> sortedFileList)
        {
            long fileNamesOffset = 0;
            foreach (var file in sortedFileList)
            {
                var fileSize = 4;
                var zeroTerminator = 1;
                var fileNameBytes = Encoding.UTF8.GetByteCount(file.Key);
                fileNamesOffset += fileSize + headerSpecificBytePerFile + fileNameBytes + zeroTerminator;
            }

            return fileNamesOffset;
        }
         
        public static FileCompressionInfo DetermineFileCompression(PackFileVersion outputPackFileVersion, GameInformation currentGameInformation, string fullFileName, CompressionFormat currentFileCompressionFormat, bool enableCompression = true)
        {
            var doesGameSupportCompression = FileCompression.DoesGameSupportCompression(currentGameInformation);
            var compressIfPossible = enableCompression && doesGameSupportCompression && outputPackFileVersion == PackFileVersion.PFH5;

            var targetFileCompressionFormat = FileCompression.GetCompressionFormat(currentGameInformation, fullFileName);
            var isFileCompressed = currentFileCompressionFormat != CompressionFormat.None;

            if (isFileCompressed == false)
            {
                if (compressIfPossible)
                {
                    // Case 1 - Not a compressed file, going to a packfile/game with compression
                    return new FileCompressionInfo(targetFileCompressionFormat, true);
                }
                else
                {
                    // Case 2 - Not a compressed file, going to a packfile/game without compression
                    return new FileCompressionInfo(CompressionFormat.None, false);
                }
            }
            else
            {
                if (compressIfPossible)
                {
                    // Case 3 - A compressed file, going to a packfile/game with compression. Same target and source format
                    if (currentFileCompressionFormat == targetFileCompressionFormat)
                        return new FileCompressionInfo(targetFileCompressionFormat, false);

                    // Case 4 - A compressed file, going to a packfile/game with compression. Different target and source format
                    return new FileCompressionInfo(targetFileCompressionFormat, true);
                }
                else
                {
                    // Case 5 - A compressed file, going to a packfile/game without compression
                    return new FileCompressionInfo(CompressionFormat.None, true);
                }
            }
        }

        public static List<PackFileWriteInformation> BuildMetaDataTable(IList<KeyValuePair<string, PackFile>> sortedFiles, PackFileContainer container, GameInformation currentGameInformation, bool enableCompression = true)
        {
            var filesToWrite = new List<PackFileWriteInformation>();
            foreach (var file in sortedFiles)
            {
                var packFile = file.Value;
                var fileCompressionInfo = DetermineFileCompression(container.Header.Version, currentGameInformation, file.Key, packFile.DataSource.CompressionFormat, enableCompression);
                filesToWrite.Add(new PackFileWriteInformation(packFile, file.Key, 0, fileCompressionInfo));
            }

            return filesToWrite;
        }

        public static void SerializeFileTable(List<PackFileWriteInformation> fileMetaData, PackFileContainer container, BinaryWriter writer)
        {
            // Write file table
            // FileStartPosition
            // TimeStamp
            // CompressionFlag
            // FileName
            // ZeroTerminator for FileName
            foreach (var file in fileMetaData)
            {
                // File size placeholder (rewritten later)
                var sizePosition = writer.BaseStream.Position;
                writer.Write(0);
                file.SizePosition = sizePosition;   

                // Timestamp
                if (container.Header.HasIndexWithTimeStamp)
                    writer.Write(0);

                // Even if we do not compress - we alsways need to write the flag for PFH5
                if (container.Header.Version == PackFileVersion.PFH5)
                {
                    var shouldCompress = file.CompressionInfo.IntendedCompressionFormat != CompressionFormat.None;
                    writer.Write(shouldCompress);
                }

                // Filename
                var fileNameBytes = Encoding.UTF8.GetBytes(file.FullFileName);
                writer.Write(fileNameBytes);
                writer.Write((byte)0); // Zero terminator
            }
        }

        public static void SerializeFileBlob(
            List<PackFileWriteInformation> fileMetaDataTabel,
            PackFileContainer container,
            BinaryWriter writer,
            PackedFileSourceParent outputParent,
            List<PendingDataSourceUpdate> pendingUpdates)
        {
            foreach (var fileMetaData in fileMetaDataTabel)
            {
                var packFile = fileMetaData.PackFile;
                uint uncompressedSize = 0;
                var shouldCompress = fileMetaData.CompressionInfo.IntendedCompressionFormat != CompressionFormat.None;
                var offset = writer.BaseStream.Position;
                int dataLength;
                if (shouldCompress)
                {
                    var uncompressedData = packFile.DataSource.ReadData();
                    uncompressedSize = (uint)uncompressedData.Length;

                    // Compress the data into the right format
                    var compressedData = FileCompression.Compress(uncompressedData, fileMetaData.CompressionInfo.IntendedCompressionFormat);

                    // Validate new compression
                    var decompressedData = FileCompression.Decompress(compressedData, uncompressedData.Length, fileMetaData.CompressionInfo.IntendedCompressionFormat);
                    if (decompressedData.Length != uncompressedData.Length)
                        throw new InvalidDataException($"Decompressed bytes {decompressedData.Length:N0} does not match the expected uncompressed bytes {uncompressedData.Length:N0}.");

                    writer.Write(compressedData);
                    dataLength = compressedData.Length;
                }
                else if (!fileMetaData.CompressionInfo.DecompressBeforeSaving &&
                         packFile.DataSource is PackedFileSource packedFileSource)
                {
                    var data = packedFileSource.ReadDataWithoutDecompressing();
                    writer.Write(data);
                    dataLength = data.Length;
                }
                else if (!fileMetaData.CompressionInfo.DecompressBeforeSaving)
                {
                    packFile.DataSource.CopyTo(writer.BaseStream);
                    dataLength = checked((int)(
                        writer.BaseStream.Position - offset));
                }
                else
                {
                    var data = packFile.DataSource.ReadData();
                    writer.Write(data);
                    dataLength = data.Length;
                }

                // Patch the size from the position stored earlier
                var currentPosition = writer.BaseStream.Position;
                writer.BaseStream.Position = fileMetaData.SizePosition;
                writer.Write(dataLength);
                writer.BaseStream.Position = currentPosition;

                pendingUpdates.Add(new PendingDataSourceUpdate(
                    packFile,
                    new PackedFileSource(
                        outputParent,
                        offset,
                        dataLength,
                        false,     // We do not encrypt
                        shouldCompress,
                        fileMetaData.CompressionInfo.IntendedCompressionFormat,
                        uncompressedSize)));
            }
        }

        
    }
}
