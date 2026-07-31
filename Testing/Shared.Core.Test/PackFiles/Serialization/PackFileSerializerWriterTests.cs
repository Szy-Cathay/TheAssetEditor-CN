using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Serialization;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;

namespace Test.Shared.Core.PackFiles.Serialization
{
    [TestFixture]
    internal class PackFileSerializerWriterTests
    {
        private static readonly string[] s_corruptionDetectionContents =
        [
            "This file is here to validate that the packfile has not been corrupted while saving. This is check 1",
            "This file is here to validate that the packfile has not been corrupted while saving. This is check 2",
            "This file is here to validate that the packfile has not been corrupted while saving. This is check 3",
        ];

        [TestCase(GameTypeEnum.Warhammer3, PackFileVersion.PFH4, "folder//filex.txt", CompressionFormat.Zstd, CompressionFormat.None, true)]
        [TestCase(GameTypeEnum.Warhammer3, PackFileVersion.PFH5, "folder//filex.txt", CompressionFormat.Zstd, CompressionFormat.Zstd, false)]
        [TestCase(GameTypeEnum.Warhammer3, PackFileVersion.PFH5, "folder//filex.txt", CompressionFormat.Lzma1, CompressionFormat.Zstd, true)]
        [TestCase(GameTypeEnum.Warhammer3, PackFileVersion.PFH4, "folder//filex", CompressionFormat.None, CompressionFormat.None, false)]
        [TestCase(GameTypeEnum.Warhammer3, PackFileVersion.PFH4, "folder//filex", CompressionFormat.Lz4, CompressionFormat.None, true)]
        [TestCase(GameTypeEnum.Rome2, PackFileVersion.PFH4, "folder//filex.txt", CompressionFormat.Lz4, CompressionFormat.None, true)]
        [TestCase(GameTypeEnum.Rome2, PackFileVersion.PFH4, "folder//filex.txt", CompressionFormat.None, CompressionFormat.None, false)]
        // Rome 2 cases
        public void DetermineFileCompression(
            GameTypeEnum game,
            PackFileVersion outputPackFileVersion, 
            string fileName, 
            CompressionFormat currentFileCompression, 
            CompressionFormat expected_Compression, 
            bool expected_deserializeBeforeWrite)
        {
            var gameInfo = GameInformationDatabase.GetGameById(game);

            var res = PackFileSerializerWriter.DetermineFileCompression(outputPackFileVersion, gameInfo, fileName, currentFileCompression);
            Assert.That(res.DecompressBeforeSaving, Is.EqualTo(expected_deserializeBeforeWrite));
            Assert.That(res.IntendedCompressionFormat, Is.EqualTo(expected_Compression));
        }

        [Test]
        public void SaveToByteArray_WhenLaterSourceReadFails_DoesNotReplaceEarlierSources()
        {
            var gameInfo = GameInformationDatabase.GetGameById(GameTypeEnum.Rome2);
            var outputPath = Path.Combine(Path.GetTempPath(), $"pack-output-{Guid.NewGuid():N}.pack");
            var missingPath = Path.Combine(Path.GetTempPath(), $"missing-pack-{Guid.NewGuid():N}.pack");
            var firstSource = new MemorySource([1, 2, 3]);
            var missingSource = new PackedFileSource(
                new PackedFileSourceParent { FilePath = missingPath },
                0,
                4,
                false,
                false,
                CompressionFormat.None,
                0);
            var first = new PackFile("first.bin", firstSource);
            var second = new PackFile("second.bin", missingSource);
            var container = new PackFileContainer("test")
            {
                Header = new PFHeader(
                    PackFileVersionConverter.ToString(PackFileVersion.PFH4),
                    PackFileCAType.MOD),
            };
            container.FileList["a\\first.bin"] = first;
            container.FileList["z\\second.bin"] = second;

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            Assert.Throws<FileNotFoundException>(() =>
                PackFileSerializerWriter.SaveToByteArray(
                    outputPath, container, writer, gameInfo, false));
            Assert.That(first.DataSource, Is.SameAs(firstSource));
            Assert.That(second.DataSource, Is.SameAs(missingSource));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void SaveToByteArray_CorruptionDetectionEnabled_DetectsCorruptedOutput(
            int validationFileIndex)
        {
            var gameInfo = GameInformationDatabase.GetGameById(
                GameTypeEnum.Rome2);
            var container = new PackFileContainer("test")
            {
                Header = new PFHeader(
                    PackFileVersionConverter.ToString(
                        PackFileVersion.PFH4),
                    PackFileCAType.MOD),
            };
            container.FileList["keep.bin"] =
                PackFile.CreateFromBytes("keep.bin", [1, 2, 3]);

            using var stream = new CorruptOnRewindStream(
                s_corruptionDetectionContents[validationFileIndex]);
            using var writer = new BinaryWriter(stream);

            Assert.That(
                () => PackFileSerializerWriter.SaveToByteArray(
                    "corrupted-output.pack",
                    container,
                    writer,
                    gameInfo,
                    enableCompression: false,
                    enableCorruptionDetection: true),
                Throws.TypeOf<InvalidDataException>()
                    .With.Message.Contains("unexpected content"));
        }

        [Test]
        public void SaveToByteArray_DefaultOptions_PreserveMarkerNamedUserFile()
        {
            const string markerPath =
                @"packfile_corruction_detection\packfile_corruction_detection_2.txt";
            var gameInfo = GameInformationDatabase.GetGameById(
                GameTypeEnum.Rome2);
            var container = new PackFileContainer("test")
            {
                Header = new PFHeader(
                    PackFileVersionConverter.ToString(
                        PackFileVersion.PFH4),
                    PackFileCAType.MOD),
            };
            container.FileList["keep.bin"] =
                PackFile.CreateFromBytes("keep.bin", [1]);
            container.FileList[markerPath] =
                PackFile.CreateFromASCII(
                    Path.GetFileName(markerPath),
                    "user data");

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            PackFileSerializerWriter.SaveToByteArray(
                "ordinary.pack",
                container,
                writer,
                gameInfo,
                enableCompression: false);

            using var readStream = new MemoryStream(stream.ToArray());
            using var reader = new BinaryReader(readStream);
            var loaded = PackFileSerializerLoader.Load(
                "ordinary.pack",
                readStream.Length,
                reader,
                new CaPackDuplicateFileResolver());
            var marker =
                (PackedFileSource)loaded.FileList[markerPath]
                    .DataSource;

            Assert.Multiple(() =>
            {
                Assert.That(
                    loaded.FileList.Keys,
                    Is.EquivalentTo(
                        new[] { "keep.bin", markerPath }));
                Assert.That(
                    marker.ReadData(readStream),
                    Is.EqualTo(
                        System.Text.Encoding.ASCII.GetBytes(
                            "user data")));
            });
        }

        [Test]
        [TestCase(GameTypeEnum.Rome2, PackFileVersion.PFH4, false)]
        [TestCase(GameTypeEnum.Warhammer3, PackFileVersion.PFH4, false)]
        [TestCase(GameTypeEnum.Warhammer3, PackFileVersion.PFH5, true)]
        public void PackFileSerializerWriterTests_GameWithoutCompression(
            GameTypeEnum game,
            PackFileVersion outputPackFileVersion,
            bool expectFileCompression)
        {
            // Arrange
            var gameInfo = GameInformationDatabase.GetGameById(game);
            var expectedFileInfo = new List<(string FilePath, string FileName, int Length, char Content, bool IsCompressable)>
            {
                ("directory\\fileA.txt", "fileA.txt", 512, 'A', true),
                ("directory\\fileB.txt", "fileB.txt", 1024, 'B', true),
                ("directory\\fileC.txt", "fileC.txt", 2048, 'C', true),
                ("directory\\fileD", "fileD", 512, 'D', false),
                ("\"directory\\\\db\\\\TableTest\"", "TableTest", 128, 'E', false),
            };

            // Create packfile with the above files
            var outputContainerName = @"c:\fullpath\to\packfile.pack";
            var packFileHeader = PackFileVersionConverter.ToString(outputPackFileVersion);
            var container = new PackFileContainer("test")
            {
                Header = new PFHeader(packFileHeader, PackFileCAType.MOD),
                SystemFilePath = "test.pack"
            };

            foreach (var fileInfo in expectedFileInfo)
                container.FileList[fileInfo.FilePath] = PackFile.CreateFromASCII(fileInfo.FileName, new string(fileInfo.Content, fileInfo.Length));
            var originalSources = container.FileList.ToDictionary(
                file => file.Key,
                file => file.Value.DataSource);

            using var writeMs = new MemoryStream();
            using var writer = new BinaryWriter(writeMs);
            var result = PackFileSerializerWriter.SaveToByteArray(outputContainerName, container, writer, gameInfo);
            var data = writeMs.ToArray();

            foreach (var fileInfo in expectedFileInfo)
                Assert.That(container.FileList[fileInfo.FilePath].DataSource, Is.SameAs(originalSources[fileInfo.FilePath]));

            result.Commit();

            // Assert that the internal file references have been updated
            foreach (var fileInfo in expectedFileInfo)
            {
                var dataSourceInstance = container.FileList[fileInfo.FilePath].DataSource as PackedFileSource;
                Assert.That(dataSourceInstance, Is.Not.Null);
                Assert.That(dataSourceInstance.Parent.FilePath, Is.EqualTo(outputContainerName));
            }

            //  Load the file and assert
            using var readBackMs = new MemoryStream(data);
            var reader = new BinaryReader(readBackMs);
            var loadedPackFile = PackFileSerializerLoader.Load(outputContainerName, data.LongLength, reader, new CaPackDuplicateFileResolver());

            for (var i = 0; i < expectedFileInfo.Count; i++)
            {
                var expectedFileInfoInstance = expectedFileInfo[i];
                var packFile = loadedPackFile.FileList[expectedFileInfoInstance.FilePath.ToLower()];

                // Bypass the filesystem lookup and go directly to stream
                var packFileConentet = (packFile.DataSource as PackedFileSource).ReadData(readBackMs);
                var parentName = (packFile.DataSource as PackedFileSource).Parent.FilePath;

                // Assert that parent file has been updated correctly
                Assert.That(parentName.ToLower(), Is.EqualTo(outputContainerName.ToLower()));

                // Assert content is correct
                Assert.That(packFileConentet.Length, Is.EqualTo(expectedFileInfoInstance.Length));
                Assert.That(packFileConentet, Is.EqualTo(new string(expectedFileInfoInstance.Content, expectedFileInfoInstance.Length)));

                if (expectedFileInfoInstance.IsCompressable && expectFileCompression)
                {
                    Assert.That(packFile.DataSource.CompressionFormat, Is.Not.EqualTo(CompressionFormat.None));
                    Assert.That(packFile.DataSource.Size, Is.LessThan(expectedFileInfoInstance.Length));
                }
                else
                {
                    Assert.That(packFile.DataSource.CompressionFormat, Is.EqualTo(CompressionFormat.None));
                    Assert.That(packFile.DataSource.Size, Is.EqualTo(expectedFileInfoInstance.Length));
                }
            }

        }

        private sealed class CorruptOnRewindStream(
            string contentToCorrupt) : MemoryStream
        {
            private bool _corruptOnRewind = true;

            public override long Position
            {
                get => base.Position;
                set
                {
                    if (_corruptOnRewind &&
                        value == 0 &&
                        Length != 0)
                    {
                        var content = System.Text.Encoding.UTF8.GetBytes(
                            contentToCorrupt);
                        var data = GetBuffer().AsSpan(
                            0,
                            checked((int)Length));
                        var contentIndex = data.IndexOf(content);
                        if (contentIndex < 0)
                        {
                            throw new InvalidOperationException(
                                "The validation content was not serialized.");
                        }

                        data[contentIndex] ^= 0xFF;
                        _corruptOnRewind = false;
                    }

                    base.Position = value;
                }
            }
        }
    }
}
