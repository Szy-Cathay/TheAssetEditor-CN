using System.Reflection;
using System.Text;
using AssetEditor.Services;
using Editors.Audio.ContextMenu;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.External;

namespace AssetEditorTests
{
    [TestClass]
    public class CAVp8ExportCommandTests
    {
        [TestMethod]
        public void CommandsAndMenu_AreAvailableOnlyForCaVp8Files()
        {
            var serviceProvider = new DependencyInjectionConfig(false).Build(true);
            try
            {
                using var scope = serviceProvider.CreateScope();
                var services = scope.ServiceProvider;
                services.GetRequiredService<LocalizationManager>()
                    .LoadLanguage();
                var ivfCommand =
                    services.GetRequiredService<IExportCAVp8AsIvfCommand>();
                var webMCommand =
                    services.GetRequiredService<IExportCAVp8AsWebMCommand>();
                var owner = new PackFileContainer("test.pack");
                var movieNode = CreateFileNode(owner, "movie.ca_vp8");
                var uppercaseMovieNode =
                    CreateFileNode(owner, "MOVIE.CA_VP8");
                var textureNode = CreateFileNode(owner, "texture.dds");

                Assert.IsTrue(ivfCommand.IsEnabled(movieNode));
                Assert.IsTrue(ivfCommand.IsEnabled(uppercaseMovieNode));
                Assert.IsFalse(ivfCommand.IsEnabled(textureNode));
                Assert.IsTrue(webMCommand.IsEnabled(movieNode));
                Assert.IsFalse(webMCommand.IsEnabled(textureNode));
                Assert.AreEqual(
                    "导出为 IVF",
                    ivfCommand.GetDisplayName(movieNode));
                Assert.AreEqual(
                    "导出为 WebM（自动匹配音频）",
                    webMCommand.GetDisplayName(movieNode));

                var menuBuilder = services.GetServices<IContextMenuBuilder>()
                    .Single(builder =>
                        builder.Type == ContextMenuType.MainApplication);
                var movieMenu = menuBuilder.Build(movieNode);
                var textureMenu = menuBuilder.Build(textureNode);

                Assert.IsTrue(ContainsMenuItem(
                    movieMenu,
                    ivfCommand.GetDisplayName(movieNode)));
                Assert.IsTrue(ContainsMenuItem(
                    movieMenu,
                    webMCommand.GetDisplayName(movieNode)));
                Assert.IsFalse(ContainsMenuItem(
                    textureMenu,
                    ivfCommand.GetDisplayName(movieNode)));
                Assert.IsFalse(ContainsMenuItem(
                    textureMenu,
                    webMCommand.GetDisplayName(movieNode)));
            }
            finally
            {
                (serviceProvider as IDisposable)?.Dispose();
            }
        }

        [TestMethod]
        public void IvfCommand_CancelledSelection_DoesNotConvertOrWrite()
        {
            var converted = false;
            var writes = new List<string>();
            var command = CreateIvfCommand(
                () => null,
                _ =>
                {
                    converted = true;
                    return [];
                },
                (path, _) => writes.Add(path),
                _ => { },
                _ => { });

            command.Execute(CreateFileNode(
                new PackFileContainer("test.pack"),
                "movie.ca_vp8"));

            Assert.IsFalse(converted);
            Assert.AreEqual(0, writes.Count);
        }

        [TestMethod]
        public void WebMCommand_CancelledSelection_DoesNotStartBackgroundWork()
        {
            var backgroundRuns = 0;
            var command = CreateWebMCommandWithOverwrite(
                () => null,
                () => Assert.Fail("Cancelled export loaded audio."),
                _ => throw new InvalidOperationException(),
                (_, _) => throw new InvalidOperationException(),
                (_, _, _) => Assert.Fail("Cancelled export wrote a file."),
                _ => throw new InvalidOperationException(),
                _ => throw new InvalidOperationException(),
                _ => Assert.Fail("Cancelled export reported success."),
                _ => Assert.Fail("Cancelled export reported an error."),
                action =>
                {
                    backgroundRuns++;
                    action();
                    return Task.CompletedTask;
                });

            command.Execute(CreateFileNode(
                new PackFileContainer("test.pack"),
                "movie.ca_vp8"));

            Assert.AreEqual(0, backgroundRuns);
        }

        [TestMethod]
        public void IvfCommand_ConversionFailure_DoesNotWriteAndReportsError()
        {
            LocalizationManager.Instance.LoadLanguage();
            var writes = new List<string>();
            var errors = new List<string>();
            var command = CreateIvfCommand(
                () => Path.GetTempPath(),
                _ => throw new InvalidDataException("broken CAMV"),
                (path, _) => writes.Add(path),
                _ => { },
                errors.Add);

            command.Execute(CreateFileNode(
                new PackFileContainer("test.pack"),
                "movie.ca_vp8"));

            Assert.AreEqual(0, writes.Count);
            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual(
                "导出失败。请确认影片资源完整，并检查目标文件夹是否可写。",
                errors[0]);
        }

        [TestMethod]
        public void AtomicFileWriter_ReplacesDestinationAndCleansTemporaryFile()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "AssetEditorCAVp8Tests",
                Guid.NewGuid().ToString("N"));
            var destination = Path.Combine(directory, "movie.ivf");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(destination, [9]);

            try
            {
                AtomicFileWriter.WriteAllBytes(destination, [1, 2, 3]);

                CollectionAssert.AreEqual(
                    new byte[] { 1, 2, 3 },
                    File.ReadAllBytes(destination));
                Assert.AreEqual(
                    0,
                    Directory.GetFiles(directory, "*.tmp").Length);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void AtomicFileWriter_MoveFailure_CleansTemporaryFile()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "AssetEditorCAVp8Tests",
                Guid.NewGuid().ToString("N"));
            var destination = Path.Combine(directory, "movie.ivf");
            Directory.CreateDirectory(destination);

            try
            {
                Assert.ThrowsException<UnauthorizedAccessException>(
                    () => AtomicFileWriter.WriteAllBytes(
                        destination,
                        [1, 2, 3]));

                Assert.IsTrue(Directory.Exists(destination));
                Assert.AreEqual(
                    0,
                    Directory.GetFiles(directory, "*.tmp").Length);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void AtomicFileWriter_OverwriteDisabled_PreservesDestination()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "AssetEditorCAVp8Tests",
                Guid.NewGuid().ToString("N"));
            var destination = Path.Combine(directory, "movie.ivf");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(destination, [9]);
            var method = typeof(AtomicFileWriter).GetMethod(
                nameof(AtomicFileWriter.WriteAllBytes),
                [typeof(string), typeof(byte[]), typeof(bool)]);

            try
            {
                Assert.IsNotNull(
                    method,
                    "Atomic writes must support refusing an unapproved overwrite.");
                var exception = Assert.ThrowsException<TargetInvocationException>(
                    () => method.Invoke(null, [destination, new byte[] { 1 }, false]));

                Assert.IsInstanceOfType(
                    exception.InnerException,
                    typeof(IOException));
                CollectionAssert.AreEqual(
                    new byte[] { 9 },
                    File.ReadAllBytes(destination));
                Assert.AreEqual(
                    0,
                    Directory.GetFiles(directory, "*.tmp").Length);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void MoviePath_MapsToActionEventAndRejectsOutsideMovies()
        {
            Assert.AreEqual(
                "Play_Movie_warhammer3_chd_dlc23_astragoth_intro",
                Wh3ActionEventInformation.GetMovieActionEventName(
                    @"movies\warhammer3\chd\dlc23_astragoth_intro.ca_vp8"));
            Assert.ThrowsException<ArgumentException>(
                () => Wh3ActionEventInformation.GetMovieActionEventName(
                    @"audio\not_a_movie.ca_vp8"));
        }

        [TestMethod]
        public void MovieAudioResolver_MissingActionEvent_ReturnsNoAudio()
        {
            var audioRepository = new Mock<IAudioRepository>();
            audioRepository
                .Setup(repository => repository.GetHircs(It.IsAny<uint>()))
                .Returns([]);
            var resolver = new MovieAudioResolver(
                audioRepository.Object,
                Mock.Of<IPackFileService>());

            var wem = resolver.ResolveMovieWem(
                @"movies\mainmenu.ca_vp8");

            Assert.IsNull(wem);
        }

        [TestMethod]
        public void WebMExporter_MissingAudio_WritesVideoOnlyWebM()
        {
            var movie = PackFile.CreateFromBytes(
                "movie.ca_vp8",
                CreateCaVp8Bytes());

            var bytes = CAVp8Exporter.ExportToWebM(movie, null!);
            var text = Encoding.ASCII.GetString(bytes);

            StringAssert.Contains(text, "V_VP8");
            Assert.IsFalse(text.Contains("A_VORBIS"));
        }

        [TestMethod]
        public void WebMCommand_MissingAudio_ReportsVideoOnlySuccess()
        {
            new LocalizationManager().LoadLanguage();
            var writes = new List<string>();
            var successes = new List<string>();
            var errors = new List<string>();
            using var completed = new ManualResetEventSlim();
            var outputDirectory = Path.GetTempPath();
            var outputPath = Path.Combine(outputDirectory, "movie.webm");
            var command = CreateWebMCommand(
                () => outputDirectory,
                () => { },
                _ => null!,
                (_, wem) =>
                {
                    Assert.IsNull(wem);
                    return [1, 2, 3];
                },
                (path, _) => writes.Add(path),
                message =>
                {
                    successes.Add(message);
                    completed.Set();
                },
                message =>
                {
                    errors.Add(message);
                    completed.Set();
                });

            command.Execute(CreateFileNode(
                new PackFileContainer("test.pack"),
                "movie.ca_vp8"));
            Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));

            CollectionAssert.AreEqual(
                new[] { outputPath },
                writes);
            CollectionAssert.AreEqual(
                new[]
                {
                    $"导出完成（未找到关联音频，已导出无声 WebM）：{outputPath}"
                },
                successes);
            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        public void WebMCommand_Execute_ReturnsWhileAudioLoads()
        {
            new LocalizationManager().LoadLanguage();
            using var loadStarted = new ManualResetEventSlim();
            using var releaseLoad = new ManualResetEventSlim();
            var command = CreateWebMCommand(
                () => Path.GetTempPath(),
                () =>
                {
                    loadStarted.Set();
                    releaseLoad.Wait(TimeSpan.FromSeconds(10));
                },
                _ => null!,
                (_, _) => [],
                (_, _) => { },
                _ => { },
                _ => { });
            var node = CreateFileNode(
                new PackFileContainer("test.pack"),
                "movie.ca_vp8");
            var caller = Task.Run(() => command.Execute(node));

            try
            {
                Assert.IsTrue(
                    loadStarted.Wait(TimeSpan.FromSeconds(5)),
                    "Audio loading did not start.");
                Assert.IsTrue(
                    caller.Wait(TimeSpan.FromMilliseconds(250)),
                    "The context-menu command blocked its caller.");
            }
            finally
            {
                releaseLoad.Set();
                caller.Wait(TimeSpan.FromSeconds(5));
            }
        }

        [TestMethod]
        public void WebMCommand_ProductionConstructor_DoesNotCaptureAudioScope()
        {
            var constructor = typeof(ExportCAVp8AsWebMCommand)
                .GetConstructors()
                .Single();
            var parameterTypes = constructor
                .GetParameters()
                .Select(parameter => parameter.ParameterType)
                .ToList();

            CollectionAssert.DoesNotContain(
                parameterTypes,
                typeof(IAudioRepository));
            CollectionAssert.DoesNotContain(
                parameterTypes,
                typeof(IMovieAudioResolver));
            CollectionAssert.Contains(
                parameterTypes,
                typeof(IServiceScopeFactory));
        }

        [TestMethod]
        public void WebMCommand_AudioLookup_UsesChineseTemporaryScope()
        {
            var movie = PackFile.CreateFromBytes(
                "movie.ca_vp8",
                []);
            var audioRepository = new Mock<IAudioRepository>();
            var movieAudioResolver = new Mock<IMovieAudioResolver>();
            movieAudioResolver
                .Setup(resolver => resolver.ResolveMovieWem(
                    @"movies\movie.ca_vp8"))
                .Returns((PackFile?)null);
            var scopedServices = new Mock<IServiceProvider>();
            scopedServices
                .Setup(provider => provider.GetService(
                    typeof(IAudioRepository)))
                .Returns(audioRepository.Object);
            scopedServices
                .Setup(provider => provider.GetService(
                    typeof(IMovieAudioResolver)))
                .Returns(movieAudioResolver.Object);
            var scope = new Mock<IServiceScope>();
            scope
                .SetupGet(value => value.ServiceProvider)
                .Returns(scopedServices.Object);
            var scopeFactory = new Mock<IServiceScopeFactory>();
            scopeFactory
                .Setup(factory => factory.CreateScope())
                .Returns(scope.Object);
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(service => service.GetFullPath(movie, null))
                .Returns(@"movies\movie.ca_vp8");
            var method = typeof(ExportCAVp8AsWebMCommand).GetMethod(
                "ResolveMovieWemForExport",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.IsNotNull(
                method,
                "WebM audio lookup must use an isolated export scope.");
            var wem = method.Invoke(
                null,
                [scopeFactory.Object, packFileService.Object, movie]);

            Assert.IsNull(wem);
            audioRepository.Verify(
                repository => repository.Load(
                    It.Is<List<string>>(languages =>
                        languages.SequenceEqual(new[] { "chinese" })),
                    null,
                    default),
                Times.Once);
            scope.Verify(value => value.Dispose(), Times.Once);
        }

        [TestMethod]
        public void IvfCommand_ExistingDestinationDeclined_HasNoSideEffects()
        {
            var converted = false;
            var writes = 0;
            var confirmations = new List<string>();
            var outputDirectory = Path.GetTempPath();
            var outputPath = Path.Combine(outputDirectory, "movie.ivf");
            var command = CreateIvfCommandWithOverwrite(
                () => outputDirectory,
                _ =>
                {
                    converted = true;
                    return [];
                },
                (_, _, _) => writes++,
                _ => true,
                path =>
                {
                    confirmations.Add(path);
                    return false;
                },
                _ => Assert.Fail("Cancelled export reported success."),
                _ => Assert.Fail("Cancelled export reported an error."));

            command.Execute(CreateFileNode(
                new PackFileContainer("test.pack"),
                "movie.ca_vp8"));

            CollectionAssert.AreEqual(
                new[] { outputPath },
                confirmations);
            Assert.IsFalse(converted);
            Assert.AreEqual(0, writes);
        }

        [TestMethod]
        public void WebMCommand_ExistingDestinationDeclined_HasNoSideEffects()
        {
            var audioLoaded = false;
            var converted = false;
            var writes = 0;
            var backgroundRuns = 0;
            var outputDirectory = Path.GetTempPath();
            var outputPath = Path.Combine(outputDirectory, "movie.webm");
            var command = CreateWebMCommandWithOverwrite(
                () => outputDirectory,
                () => audioLoaded = true,
                _ => null,
                (_, _) =>
                {
                    converted = true;
                    return [];
                },
                (_, _, _) => writes++,
                _ => true,
                path =>
                {
                    Assert.AreEqual(outputPath, path);
                    return false;
                },
                _ => Assert.Fail("Cancelled export reported success."),
                _ => Assert.Fail("Cancelled export reported an error."),
                action =>
                {
                    backgroundRuns++;
                    action();
                    return Task.CompletedTask;
                });

            command.Execute(CreateFileNode(
                new PackFileContainer("test.pack"),
                "movie.ca_vp8"));

            Assert.IsFalse(audioLoaded);
            Assert.IsFalse(converted);
            Assert.AreEqual(0, writes);
            Assert.AreEqual(0, backgroundRuns);
        }

        [TestMethod]
        public void ExportCommands_ConfirmedOverwrite_IsPassedToWriter()
        {
            new LocalizationManager().LoadLanguage();
            var ivfOverwrite = false;
            var webMOverwrite = false;
            var outputDirectory = Path.GetTempPath();
            var node = CreateFileNode(
                new PackFileContainer("test.pack"),
                "movie.ca_vp8");
            var ivfCommand = CreateIvfCommandWithOverwrite(
                () => outputDirectory,
                _ => [],
                (_, _, overwrite) => ivfOverwrite = overwrite,
                _ => true,
                _ => true,
                _ => { },
                _ => { });
            var webMCommand = CreateWebMCommandWithOverwrite(
                () => outputDirectory,
                () => { },
                _ => null,
                (_, _) => [],
                (_, _, overwrite) => webMOverwrite = overwrite,
                _ => true,
                _ => true,
                _ => { },
                _ => { },
                action =>
                {
                    action();
                    return Task.CompletedTask;
                });

            ivfCommand.Execute(node);
            webMCommand.Execute(node);

            Assert.IsTrue(ivfOverwrite);
            Assert.IsTrue(webMOverwrite);
        }

        [TestMethod]
        public void ExportCommands_NewDestination_DisallowUnexpectedOverwrite()
        {
            new LocalizationManager().LoadLanguage();
            var ivfOverwrite = true;
            var webMOverwrite = true;
            var outputDirectory = Path.GetTempPath();
            var node = CreateFileNode(
                new PackFileContainer("test.pack"),
                "movie.ca_vp8");
            var ivfCommand = CreateIvfCommandWithOverwrite(
                () => outputDirectory,
                _ => [],
                (_, _, overwrite) => ivfOverwrite = overwrite,
                _ => false,
                _ => throw new InvalidOperationException(),
                _ => { },
                _ => { });
            var webMCommand = CreateWebMCommandWithOverwrite(
                () => outputDirectory,
                () => { },
                _ => null,
                (_, _) => [],
                (_, _, overwrite) => webMOverwrite = overwrite,
                _ => false,
                _ => throw new InvalidOperationException(),
                _ => { },
                _ => { },
                action =>
                {
                    action();
                    return Task.CompletedTask;
                });

            ivfCommand.Execute(node);
            webMCommand.Execute(node);

            Assert.IsFalse(ivfOverwrite);
            Assert.IsFalse(webMOverwrite);
        }

        private static ExportCAVp8AsIvfCommand CreateIvfCommand(
            Func<string?> selectOutputDirectory,
            Func<PackFile, byte[]> convert,
            Action<string, byte[]> writeAllBytes,
            Action<string> showSuccess,
            Action<string> showError)
        {
            var constructor =
                typeof(ExportCAVp8AsIvfCommand).GetConstructor(
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    [
                        typeof(Func<string>),
                        typeof(Func<PackFile, byte[]>),
                        typeof(Action<string, byte[]>),
                        typeof(Action<string>),
                        typeof(Action<string>)
                    ],
                    modifiers: null);

            Assert.IsNotNull(
                constructor,
                "IVF command must expose an internal injectable constructor.");
            return (ExportCAVp8AsIvfCommand)constructor.Invoke(
                [
                    selectOutputDirectory,
                    convert,
                    writeAllBytes,
                    showSuccess,
                    showError
                ]);
        }

        private static ExportCAVp8AsIvfCommand CreateIvfCommandWithOverwrite(
            Func<string?> selectOutputDirectory,
            Func<PackFile, byte[]> convert,
            Action<string, byte[], bool> writeAllBytes,
            Func<string, bool> fileExists,
            Func<string, bool> confirmOverwrite,
            Action<string> showSuccess,
            Action<string> showError)
        {
            var constructor =
                typeof(ExportCAVp8AsIvfCommand).GetConstructor(
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    [
                        typeof(Func<string>),
                        typeof(Func<PackFile, byte[]>),
                        typeof(Action<string, byte[], bool>),
                        typeof(Func<string, bool>),
                        typeof(Func<string, bool>),
                        typeof(Action<string>),
                        typeof(Action<string>)
                    ],
                    modifiers: null);

            Assert.IsNotNull(
                constructor,
                "IVF command must expose overwrite-safe test seams.");
            return (ExportCAVp8AsIvfCommand)constructor.Invoke(
                [
                    selectOutputDirectory,
                    convert,
                    writeAllBytes,
                    fileExists,
                    confirmOverwrite,
                    showSuccess,
                    showError
                ]);
        }

        private static ExportCAVp8AsWebMCommand CreateWebMCommand(
            Func<string?> selectOutputDirectory,
            Action loadAudio,
            Func<PackFile, PackFile> resolveWem,
            Func<PackFile, PackFile, byte[]> convert,
            Action<string, byte[]> writeAllBytes,
            Action<string> showSuccess,
            Action<string> showError)
        {
            var constructor =
                typeof(ExportCAVp8AsWebMCommand).GetConstructor(
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    [
                        typeof(Func<string>),
                        typeof(Action),
                        typeof(Func<PackFile, PackFile>),
                        typeof(Func<PackFile, PackFile, byte[]>),
                        typeof(Action<string, byte[]>),
                        typeof(Action<string>),
                        typeof(Action<string>)
                    ],
                    modifiers: null);

            Assert.IsNotNull(
                constructor,
                "WebM command must expose an internal injectable constructor.");
            return (ExportCAVp8AsWebMCommand)constructor.Invoke(
                [
                    selectOutputDirectory,
                    loadAudio,
                    resolveWem,
                    convert,
                    writeAllBytes,
                    showSuccess,
                    showError
                ]);
        }

        private static ExportCAVp8AsWebMCommand CreateWebMCommandWithOverwrite(
            Func<string?> selectOutputDirectory,
            Action loadAudio,
            Func<PackFile, PackFile?> resolveWem,
            Func<PackFile, PackFile?, byte[]> convert,
            Action<string, byte[], bool> writeAllBytes,
            Func<string, bool> fileExists,
            Func<string, bool> confirmOverwrite,
            Action<string> showSuccess,
            Action<string> showError,
            Func<Action, Task> runInBackground)
        {
            var constructor =
                typeof(ExportCAVp8AsWebMCommand).GetConstructor(
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    [
                        typeof(Func<string>),
                        typeof(Action),
                        typeof(Func<PackFile, PackFile>),
                        typeof(Func<PackFile, PackFile, byte[]>),
                        typeof(Action<string, byte[], bool>),
                        typeof(Func<string, bool>),
                        typeof(Func<string, bool>),
                        typeof(Action<string>),
                        typeof(Action<string>),
                        typeof(Func<Action, Task>)
                    ],
                    modifiers: null);

            Assert.IsNotNull(
                constructor,
                "WebM command must expose overwrite-safe test seams.");
            return (ExportCAVp8AsWebMCommand)constructor.Invoke(
                [
                    selectOutputDirectory,
                    loadAudio,
                    resolveWem,
                    convert,
                    writeAllBytes,
                    fileExists,
                    confirmOverwrite,
                    showSuccess,
                    showError,
                    runInBackground
                ]);
        }

        private static TreeNode CreateFileNode(
            PackFileContainer owner,
            string name)
        {
            var packFile = PackFile.CreateFromBytes(name, []);
            return new TreeNode(
                name,
                NodeType.File,
                owner,
                null,
                packFile);
        }

        private static byte[] CreateCaVp8Bytes()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write(Encoding.ASCII.GetBytes("CAMV"));
            writer.Write((ushort)0);
            writer.Write((ushort)32);
            writer.Write(Encoding.ASCII.GetBytes("VP80"));
            writer.Write((ushort)16);
            writer.Write((ushort)8);
            writer.Write(40f);
            writer.Write(1u);
            writer.Write(0u);
            writer.Write(43u);
            writer.Write(1u);
            writer.Write(3u);
            writer.Write(new byte[] { 1, 2, 3 });
            writer.Write(40u);
            writer.Write(3u);
            writer.Write(true);

            return stream.ToArray();
        }

        private static bool ContainsMenuItem(
            IEnumerable<ContextMenuItem2?> items,
            string displayName)
        {
            foreach (var item in items)
            {
                if (item == null)
                    continue;
                if (item.Name == displayName ||
                    ContainsMenuItem(item.ContextMenu, displayName))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
