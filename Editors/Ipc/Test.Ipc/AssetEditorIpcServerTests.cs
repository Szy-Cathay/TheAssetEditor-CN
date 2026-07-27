using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using Editors.Ipc;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Test.Ipc
{
    public class AssetEditorIpcServerTests
    {
        [Test]
        public void PipeName_UsesCnEditionIdentity()
        {
            Assert.That(AssetEditorIpcServer.PipeName, Is.EqualTo("AssetEditor.CN.Ipc"));
        }

        [Test]
        public void Options_DefaultsMatchPlan()
        {
            var options = AssetEditorIpcServerOptions.Default;

            Assert.Multiple(() =>
            {
                Assert.That(
                    options.RetryDelay,
                    Is.EqualTo(TimeSpan.FromMilliseconds(500)));
                Assert.That(
                    options.ReadTimeout,
                    Is.EqualTo(TimeSpan.FromSeconds(5)));
                Assert.That(
                    options.WriteTimeout,
                    Is.EqualTo(TimeSpan.FromSeconds(5)));
                Assert.That(options.MaxRequestChars, Is.EqualTo(64 * 1024));
            });
        }

        [TestCase("abc\n", "abc")]
        [TestCase("abc\r\n", "abc")]
        [TestCase("\n", "")]
        [TestCase("\r\n", "")]
        public async Task ReadBoundedLineAsync_ReturnsDelimitedLine(
            string input,
            string expected)
        {
            using var reader = new StringReader(input);

            var actual = await AssetEditorIpcServer.ReadBoundedLineAsync(
                reader,
                16,
                CancellationToken.None);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public async Task ReadBoundedLineAsync_ReturnsNullAtEmptyEof()
        {
            using var reader = new StringReader(string.Empty);

            var actual = await AssetEditorIpcServer.ReadBoundedLineAsync(
                reader,
                16,
                CancellationToken.None);

            Assert.That(actual, Is.Null);
        }

        [TestCase("abc", "abc")]
        [TestCase("abc\r", "abc\r")]
        [TestCase("\r", "\r")]
        public async Task ReadBoundedLineAsync_PreservesPartialLineAtEof(
            string input,
            string expected)
        {
            using var reader = new StringReader(input);

            var actual = await AssetEditorIpcServer.ReadBoundedLineAsync(
                reader,
                16,
                CancellationToken.None);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [TestCase("\n")]
        [TestCase("\r\n")]
        public async Task ReadBoundedLineAsync_AcceptsExactConfiguredLimit(
            string terminator)
        {
            var payload = new string('x', 64 * 1024);
            using var reader = new StringReader(payload + terminator);

            var actual = await AssetEditorIpcServer.ReadBoundedLineAsync(
                reader,
                64 * 1024,
                CancellationToken.None);

            Assert.That(actual, Is.EqualTo(payload));
        }

        [TestCase("\n")]
        [TestCase("")]
        public void ReadBoundedLineAsync_RejectsConfiguredLimitPlusOne(
            string terminator)
        {
            using var reader = new StringReader(
                new string('x', (64 * 1024) + 1) + terminator);

            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await AssetEditorIpcServer.ReadBoundedLineAsync(
                    reader,
                    64 * 1024,
                    CancellationToken.None));
        }

        [Test]
        public async Task ReadBoundedLineAsync_HandlesCrLfAcrossChunks()
        {
            using var reader = new ChunkedTextReader("abc\r", "\nignored");

            var actual = await AssetEditorIpcServer.ReadBoundedLineAsync(
                reader,
                16,
                CancellationToken.None);

            Assert.That(actual, Is.EqualTo("abc"));
        }

        [Test]
        public async Task ReadBoundedLineAsync_PreservesEmbeddedAndDoubledCr()
        {
            using var reader = new ChunkedTextReader(
                "a\r",
                "\rb\r",
                "c\n");

            var actual = await AssetEditorIpcServer.ReadBoundedLineAsync(
                reader,
                16,
                CancellationToken.None);

            Assert.That(actual, Is.EqualTo("a\r\rb\rc"));
        }

        [Test]
        public async Task ReadBoundedLineAsync_ReturnsOnlyFirstFrame()
        {
            using var reader = new StringReader("abc\nignored");

            var actual = await AssetEditorIpcServer.ReadBoundedLineAsync(
                reader,
                16,
                CancellationToken.None);

            Assert.That(actual, Is.EqualTo("abc"));
        }

        [Test]
        public void ReadBoundedLineAsync_ObservesPreCancellation()
        {
            using var reader = new StringReader("abc\n");
            using var cancellationTokenSource =
                new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await AssetEditorIpcServer.ReadBoundedLineAsync(
                    reader,
                    16,
                    cancellationTokenSource.Token));
        }

        [Test]
        public async Task ReadBoundedLineAsync_CancelsBlockedRead()
        {
            using var reader = new BlockingTextReader();
            using var cancellationTokenSource =
                new CancellationTokenSource();
            var readTask = AssetEditorIpcServer.ReadBoundedLineAsync(
                reader,
                16,
                cancellationTokenSource.Token);

            await reader.ReadStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(1));
            cancellationTokenSource.Cancel();

            var exception = Assert.CatchAsync(async () =>
                await readTask.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.That(
                exception,
                Is.InstanceOf<OperationCanceledException>());
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void ReadBoundedLineAsync_RejectsInvalidMax(int maxChars)
        {
            using var reader = new StringReader("abc\n");

            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await AssetEditorIpcServer.ReadBoundedLineAsync(
                    reader,
                    maxChars,
                    CancellationToken.None));
        }

        [Test]
        public void PublicConstructor_RemainsDiResolvable()
        {
            var services = new ServiceCollection();
            services.AddSingleton<AssetEditorIpcServer>();
            using var provider = services.BuildServiceProvider();

            var server = provider.GetRequiredService<AssetEditorIpcServer>();

            Assert.That(server, Is.Not.Null);
            server.Dispose();
        }

        [Test]
        public void Constructor_RejectsNullInjectedDependencies()
        {
            using var provider = new ServiceCollection()
                .BuildServiceProvider();
            var scopeFactory =
                provider.GetRequiredService<IServiceScopeFactory>();
            var options = CreateTestOptions();
            Func<NamedPipeServerStream> pipeFactory = () =>
                CreateTestPipe(CreateUniquePipeName());
            static Task DelayAsync(
                TimeSpan delay,
                CancellationToken cancellationToken) =>
                Task.Delay(delay, cancellationToken);

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(() =>
                    new AssetEditorIpcServer(null!));
                Assert.Throws<ArgumentNullException>(() =>
                    new AssetEditorIpcServer(
                        null!,
                        options,
                        pipeFactory,
                        DelayAsync));
                Assert.Throws<ArgumentNullException>(() =>
                    new AssetEditorIpcServer(
                        scopeFactory,
                        null!,
                        pipeFactory,
                        DelayAsync));
                Assert.Throws<ArgumentNullException>(() =>
                    new AssetEditorIpcServer(
                        scopeFactory,
                        options,
                        null!,
                        DelayAsync));
                Assert.Throws<ArgumentNullException>(() =>
                    new AssetEditorIpcServer(
                        scopeFactory,
                        options,
                        pipeFactory,
                        null!));
            });
        }

        [Test]
        public void Constructor_RejectsInvalidOptions()
        {
            using var provider = new ServiceCollection()
                .BuildServiceProvider();
            var scopeFactory =
                provider.GetRequiredService<IServiceScopeFactory>();
            Func<NamedPipeServerStream> pipeFactory = () =>
                CreateTestPipe(CreateUniquePipeName());
            static Task DelayAsync(
                TimeSpan delay,
                CancellationToken cancellationToken) =>
                Task.Delay(delay, cancellationToken);

            void Construct(AssetEditorIpcServerOptions options)
            {
                _ = new AssetEditorIpcServer(
                    scopeFactory,
                    options,
                    pipeFactory,
                    DelayAsync);
            }

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    Construct(CreateTestOptions() with
                    {
                        RetryDelay = TimeSpan.FromMilliseconds(-1)
                    }));
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    Construct(CreateTestOptions() with
                    {
                        ReadTimeout = TimeSpan.Zero
                    }));
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    Construct(CreateTestOptions() with
                    {
                        WriteTimeout = TimeSpan.Zero
                    }));
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    Construct(CreateTestOptions() with
                    {
                        MaxRequestChars = 0
                    }));
            });
        }

        [Test]
        public void ProductionPipeOptions_RequireAsyncAndCurrentUserOnly()
        {
            var options = AssetEditorIpcServer.ProductionPipeOptions;

            Assert.Multiple(() =>
            {
                Assert.That(
                    options.HasFlag(PipeOptions.Asynchronous),
                    Is.True);
                Assert.That(
                    options.HasFlag(PipeOptions.CurrentUserOnly),
                    Is.True);
            });
        }

        [Test]
        public async Task Start_IsIdempotent()
        {
            using var provider = new ServiceCollection()
                .BuildServiceProvider();
            var scopeFactory =
                provider.GetRequiredService<IServiceScopeFactory>();
            var delayEntered = CreateSignal();
            var factoryCount = 0;
            using var server = new AssetEditorIpcServer(
                scopeFactory,
                CreateTestOptions(),
                () =>
                {
                    Interlocked.Increment(ref factoryCount);
                    throw new IOException("controlled bind failure");
                },
                async (_, cancellationToken) =>
                {
                    delayEntered.TrySetResult();
                    await Task.Delay(
                        Timeout.InfiniteTimeSpan,
                        cancellationToken);
                });

            server.Start();
            server.Start();
            await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.That(factoryCount, Is.EqualTo(1));
        }

        [Test]
        public async Task BindConflict_WaitsForInjectedDelay()
        {
            using var provider = new ServiceCollection()
                .BuildServiceProvider();
            var scopeFactory =
                provider.GetRequiredService<IServiceScopeFactory>();
            var delayEntered = CreateSignal();
            using var retryGate = new SemaphoreSlim(0);
            var secondAttempt = CreateSignal();
            var factoryCount = 0;
            using var server = new AssetEditorIpcServer(
                scopeFactory,
                CreateTestOptions(),
                () =>
                {
                    if (Interlocked.Increment(ref factoryCount) == 2)
                        secondAttempt.TrySetResult();
                    throw new IOException("controlled bind failure");
                },
                async (_, cancellationToken) =>
                {
                    delayEntered.TrySetResult();
                    await retryGate.WaitAsync(cancellationToken);
                });

            server.Start();
            await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.That(factoryCount, Is.EqualTo(1));

            retryGate.Release();
            await secondAttempt.Task.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.That(factoryCount, Is.EqualTo(2));
        }

        [Test]
        public async Task Dispose_CancelsRetryDelay()
        {
            using var provider = new ServiceCollection()
                .BuildServiceProvider();
            var scopeFactory =
                provider.GetRequiredService<IServiceScopeFactory>();
            var delayEntered = CreateSignal();
            var server = new AssetEditorIpcServer(
                scopeFactory,
                CreateTestOptions(),
                () => throw new IOException("controlled bind failure"),
                async (_, cancellationToken) =>
                {
                    delayEntered.TrySetResult();
                    await Task.Delay(
                        Timeout.InfiniteTimeSpan,
                        cancellationToken);
                });

            server.Start();
            await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

            await Task.Run(server.Dispose)
                .WaitAsync(TimeSpan.FromSeconds(1));
        }

        [Test]
        public void DisposeBeforeStart_IsSafeAndStartThenThrows()
        {
            using var provider = new ServiceCollection()
                .BuildServiceProvider();
            var scopeFactory =
                provider.GetRequiredService<IServiceScopeFactory>();
            var factoryCount = 0;
            var server = new AssetEditorIpcServer(
                scopeFactory,
                CreateTestOptions(),
                () =>
                {
                    factoryCount++;
                    return CreateTestPipe(CreateUniquePipeName());
                },
                static (delay, cancellationToken) =>
                    Task.Delay(delay, cancellationToken));

            Assert.DoesNotThrow(server.Dispose);
            Assert.DoesNotThrow(server.Dispose);
            Assert.Throws<ObjectDisposedException>(server.Start);
            Assert.That(factoryCount, Is.Zero);
        }

        [Test]
        public async Task RealPipe_HandlesValidRequest()
        {
            var pipeName = CreateUniquePipeName();
            using var provider = CreateHandlerProvider(
                static (_, _) => Task.FromResult(
                    IpcResponse.Success()));
            using var server = CreateRealPipeServer(
                provider,
                pipeName);
            using var watchdog =
                new CancellationTokenSource(TimeSpan.FromSeconds(10));
            server.Start();

            using var client = CreateClient(pipeName);
            await client.ConnectAsync(watchdog.Token);
            var response = await SendRequestAndReadResponseAsync(
                client,
                ValidRequest,
                "\n",
                watchdog.Token);

            Assert.That(response, Is.EqualTo("{\"ok\":true}"));
        }

        [Test]
        public async Task RealPipe_AcceptsExactLimitCrLf()
        {
            var pipeName = CreateUniquePipeName();
            using var provider = CreateHandlerProvider(
                static (_, _) => Task.FromResult(
                    IpcResponse.Success()));
            using var server = CreateRealPipeServer(
                provider,
                pipeName);
            using var watchdog =
                new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var request = ValidRequest + new string(
                ' ',
                (64 * 1024) - ValidRequest.Length);
            server.Start();

            using var client = CreateClient(pipeName);
            await client.ConnectAsync(watchdog.Token);
            var response = await SendRequestAndReadResponseAsync(
                client,
                request,
                "\r\n",
                watchdog.Token);

            Assert.Multiple(() =>
            {
                Assert.That(request.Length, Is.EqualTo(64 * 1024));
                Assert.That(response, Is.EqualTo("{\"ok\":true}"));
            });
        }

        [Test]
        public async Task RealPipe_RecoversAfterSilentClient()
        {
            var pipeName = CreateUniquePipeName();
            var secondPipeCreated = CreateSignal();
            var factoryCount = 0;
            using var provider = CreateHandlerProvider(
                static (_, _) => Task.FromResult(
                    IpcResponse.Success()));
            using var server = CreateRealPipeServer(
                provider,
                pipeName,
                pipeFactory: () =>
                {
                    if (Interlocked.Increment(ref factoryCount) == 2)
                        secondPipeCreated.TrySetResult();
                    return CreateTestPipe(pipeName);
                });
            using var watchdog =
                new CancellationTokenSource(TimeSpan.FromSeconds(10));
            server.Start();

            using var silentClient = CreateClient(pipeName);
            await silentClient.ConnectAsync(watchdog.Token);
            using var recoveredClient = CreateClient(pipeName);
            var recoveredConnectTask =
                recoveredClient.ConnectAsync(watchdog.Token);

            await secondPipeCreated.Task.WaitAsync(watchdog.Token);
            await recoveredConnectTask;
            var response = await SendRequestAndReadResponseAsync(
                recoveredClient,
                ValidRequest,
                "\n",
                watchdog.Token);

            Assert.Multiple(() =>
            {
                Assert.That(factoryCount, Is.GreaterThanOrEqualTo(2));
                Assert.That(response, Is.EqualTo("{\"ok\":true}"));
            });
        }

        [Test]
        public async Task RealPipe_ClosesOversizedFrameAndRecovers()
        {
            var pipeName = CreateUniquePipeName();
            using var provider = CreateHandlerProvider(
                static (_, _) => Task.FromResult(
                    IpcResponse.Success()));
            using var server = CreateRealPipeServer(
                provider,
                pipeName);
            using var watchdog =
                new CancellationTokenSource(TimeSpan.FromSeconds(10));
            server.Start();

            using var oversizedClient = CreateClient(pipeName);
            await oversizedClient.ConnectAsync(watchdog.Token);
            var oversizedResponse = await SendRequestAndReadResponseAsync(
                oversizedClient,
                new string('x', (64 * 1024) + 1),
                "\n",
                watchdog.Token);

            using var recoveredClient = CreateClient(pipeName);
            await recoveredClient.ConnectAsync(watchdog.Token);
            var recoveredResponse = await SendRequestAndReadResponseAsync(
                recoveredClient,
                ValidRequest,
                "\n",
                watchdog.Token);

            Assert.Multiple(() =>
            {
                Assert.That(oversizedResponse, Is.Null);
                Assert.That(
                    recoveredResponse,
                    Is.EqualTo("{\"ok\":true}"));
            });
        }

        [Test]
        public async Task RealPipe_RecoversAfterWriteTimeout()
        {
            var pipeName = CreateUniquePipeName();
            var handlerCallCount = 0;
            using var provider = CreateHandlerProvider(
                (_, _) => Task.FromResult(
                    Interlocked.Increment(ref handlerCallCount) == 1
                        ? IpcResponse.Failure(
                            new string('x', 8 * 1024 * 1024))
                        : IpcResponse.Success()));
            using var server = CreateRealPipeServer(
                provider,
                pipeName,
                pipeFactory: () => CreateTestPipe(
                    pipeName,
                    inBufferSize: 4096,
                    outBufferSize: 4096));
            using var watchdog =
                new CancellationTokenSource(TimeSpan.FromSeconds(10));
            server.Start();

            using var blockedClient = CreateClient(pipeName);
            await blockedClient.ConnectAsync(watchdog.Token);
            await WriteRequestAsync(
                blockedClient,
                ValidRequest,
                "\n",
                watchdog.Token);
            using var recoveredClient = CreateClient(pipeName);
            var recoveredConnectTask =
                recoveredClient.ConnectAsync(watchdog.Token);

            await recoveredConnectTask;
            var response = await SendRequestAndReadResponseAsync(
                recoveredClient,
                ValidRequest,
                "\n",
                watchdog.Token);

            Assert.Multiple(() =>
            {
                Assert.That(handlerCallCount, Is.EqualTo(2));
                Assert.That(response, Is.EqualTo("{\"ok\":true}"));
            });
        }

        [Test]
        public async Task Dispose_CompletesDuringAccept()
        {
            var pipeName = CreateUniquePipeName();
            var factoryEntered = CreateSignal();
            using var provider = CreateHandlerProvider(
                static (_, _) => Task.FromResult(
                    IpcResponse.Success()));
            var server = CreateRealPipeServer(
                provider,
                pipeName,
                pipeFactory: () =>
                {
                    factoryEntered.TrySetResult();
                    return CreateTestPipe(pipeName);
                });
            server.Start();
            await factoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

            await Task.Run(server.Dispose)
                .WaitAsync(TimeSpan.FromSeconds(1));
        }

        [Test]
        public async Task Dispose_CompletesDuringRead()
        {
            var pipeName = CreateUniquePipeName();
            using var provider = CreateHandlerProvider(
                static (_, _) => Task.FromResult(
                    IpcResponse.Success()));
            var server = CreateRealPipeServer(
                provider,
                pipeName);
            using var watchdog =
                new CancellationTokenSource(TimeSpan.FromSeconds(10));
            server.Start();
            using var client = CreateClient(pipeName);
            await client.ConnectAsync(watchdog.Token);

            await Task.Run(server.Dispose)
                .WaitAsync(TimeSpan.FromSeconds(1));
        }

        [Test]
        public async Task Dispose_CompletesDuringWrite()
        {
            var pipeName = CreateUniquePipeName();
            using var provider = CreateHandlerProvider(
                static (_, _) => Task.FromResult(
                    IpcResponse.Failure(
                        new string('x', 8 * 1024 * 1024))));
            var server = CreateRealPipeServer(
                provider,
                pipeName,
                pipeFactory: () => CreateTestPipe(
                    pipeName,
                    inBufferSize: 4096,
                    outBufferSize: 4096));
            using var watchdog =
                new CancellationTokenSource(TimeSpan.FromSeconds(10));
            server.Start();
            using var client = CreateClient(pipeName);
            await client.ConnectAsync(watchdog.Token);
            await WriteRequestAsync(
                client,
                ValidRequest,
                "\n",
                watchdog.Token);
            var prefix = new byte[1];
            var bytesRead = await client.ReadAsync(
                prefix,
                watchdog.Token);
            Assert.That(bytesRead, Is.EqualTo(1));

            await Task.Run(server.Dispose)
                .WaitAsync(TimeSpan.FromSeconds(1));
        }

        [Test]
        public async Task ProductionPipe_AllowsSameUserSameElevationClient()
        {
            var pipeName = CreateUniquePipeName();
            using var provider = CreateHandlerProvider(
                static (_, _) => Task.FromResult(
                    IpcResponse.Success()));
            using var server = CreateRealPipeServer(
                provider,
                pipeName,
                pipeFactory: () =>
                    AssetEditorIpcServer.CreateProductionPipe(pipeName));
            using var watchdog =
                new CancellationTokenSource(TimeSpan.FromSeconds(10));
            server.Start();

            using var client = CreateClient(pipeName);
            await client.ConnectAsync(watchdog.Token);
            var response = await SendRequestAndReadResponseAsync(
                client,
                ValidRequest,
                "\n",
                watchdog.Token);

            Assert.That(response, Is.EqualTo("{\"ok\":true}"));
        }

        [Test]
        [NonParallelizable]
        public async Task RepeatedFailure_LogsAtOneTenTwenty()
        {
            var previousLogger = Log.Logger;
            var sink = new CollectingSink();
            using var testLogger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();
            Log.Logger = testLogger;
            try
            {
                using var provider = new ServiceCollection()
                    .BuildServiceProvider();
                var scopeFactory =
                    provider.GetRequiredService<IServiceScopeFactory>();
                using var retryGate = new SemaphoreSlim(0);
                var delaySignals = Enumerable.Range(0, 21)
                    .Select(_ => CreateSignal())
                    .ToArray();
                var delayCount = 0;
                using var server = new AssetEditorIpcServer(
                    scopeFactory,
                    CreateTestOptions(),
                    () => throw new IOException(
                        "controlled repeated bind failure"),
                    async (_, cancellationToken) =>
                    {
                        var current = Interlocked.Increment(
                            ref delayCount);
                        if (current < delaySignals.Length)
                            delaySignals[current].TrySetResult();
                        await retryGate.WaitAsync(cancellationToken);
                    });

                server.Start();
                for (var attempt = 1; attempt <= 20; attempt++)
                {
                    await delaySignals[attempt].Task.WaitAsync(
                        TimeSpan.FromSeconds(1));
                    if (attempt < 20)
                        retryGate.Release();
                }

                Assert.That(
                    GetFailureCounts(sink),
                    Is.EqualTo(new[] { 1, 10, 20 }));
            }
            finally
            {
                Log.Logger = previousLogger;
            }
        }

        [Test]
        [NonParallelizable]
        public async Task SuccessfulRequest_ResetsFailureLogCount()
        {
            var previousLogger = Log.Logger;
            var sink = new CollectingSink();
            using var testLogger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(sink)
                .CreateLogger();
            Log.Logger = testLogger;
            try
            {
                var pipeName = CreateUniquePipeName();
                var pipeCreated = CreateSignal();
                var delaySignals = new[]
                {
                    CreateSignal(),
                    CreateSignal(),
                    CreateSignal()
                };
                using var retryGate = new SemaphoreSlim(0);
                var factoryCount = 0;
                var delayCount = 0;
                using var provider = CreateHandlerProvider(
                    static (_, _) => Task.FromResult(
                        IpcResponse.Success()));
                using var server = new AssetEditorIpcServer(
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    CreateTestOptions(),
                    () =>
                    {
                        var current = Interlocked.Increment(
                            ref factoryCount);
                        if (current == 2)
                        {
                            pipeCreated.TrySetResult();
                            return CreateTestPipe(pipeName);
                        }

                        throw new IOException(
                            "controlled resettable bind failure");
                    },
                    async (_, cancellationToken) =>
                    {
                        var current = Interlocked.Increment(
                            ref delayCount);
                        if (current < delaySignals.Length)
                            delaySignals[current].TrySetResult();
                        await retryGate.WaitAsync(cancellationToken);
                    });
                using var watchdog =
                    new CancellationTokenSource(TimeSpan.FromSeconds(10));

                server.Start();
                await delaySignals[1].Task.WaitAsync(watchdog.Token);
                retryGate.Release();
                await pipeCreated.Task.WaitAsync(watchdog.Token);
                using var client = CreateClient(pipeName);
                await client.ConnectAsync(watchdog.Token);
                var response = await SendRequestAndReadResponseAsync(
                    client,
                    ValidRequest,
                    "\n",
                    watchdog.Token);
                await delaySignals[2].Task.WaitAsync(watchdog.Token);

                Assert.Multiple(() =>
                {
                    Assert.That(response, Is.EqualTo("{\"ok\":true}"));
                    Assert.That(
                        GetFailureCounts(sink),
                        Is.EqualTo(new[] { 1, 1 }));
                });
            }
            finally
            {
                Log.Logger = previousLogger;
            }
        }

        private sealed class ChunkedTextReader(
            params string[] chunks) : TextReader
        {
            private int chunkIndex;
            private int chunkOffset;

            public override ValueTask<int> ReadAsync(
                Memory<char> buffer,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (chunkIndex < chunks.Length)
                {
                    var chunk = chunks[chunkIndex];
                    if (chunkOffset == chunk.Length)
                    {
                        chunkIndex++;
                        chunkOffset = 0;
                        continue;
                    }

                    var length = Math.Min(
                        buffer.Length,
                        chunk.Length - chunkOffset);
                    chunk.AsMemory(chunkOffset, length).CopyTo(buffer);
                    chunkOffset += length;
                    return ValueTask.FromResult(length);
                }

                return ValueTask.FromResult(0);
            }
        }

        private sealed class BlockingTextReader : TextReader
        {
            internal TaskCompletionSource ReadStarted { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            public override async ValueTask<int> ReadAsync(
                Memory<char> buffer,
                CancellationToken cancellationToken = default)
            {
                ReadStarted.TrySetResult();
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
                return 0;
            }
        }

        private static AssetEditorIpcServerOptions CreateTestOptions()
        {
            return new AssetEditorIpcServerOptions(
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromMilliseconds(500),
                64 * 1024);
        }

        private static AssetEditorIpcServer CreateRealPipeServer(
            ServiceProvider provider,
            string pipeName,
            Func<NamedPipeServerStream>? pipeFactory = null)
        {
            return new AssetEditorIpcServer(
                provider.GetRequiredService<IServiceScopeFactory>(),
                CreateTestOptions(),
                pipeFactory ?? (() => CreateTestPipe(pipeName)),
                static (delay, cancellationToken) =>
                    Task.Delay(delay, cancellationToken));
        }

        private static NamedPipeServerStream CreateTestPipe(
            string pipeName,
            int inBufferSize = 0,
            int outBufferSize = 0)
        {
            return new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize,
                outBufferSize);
        }

        private static string CreateUniquePipeName()
        {
            return $"AssetEditor.CN.Ipc.Tests.{Guid.NewGuid():N}";
        }

        private static TaskCompletionSource CreateSignal()
        {
            return new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static ServiceProvider CreateHandlerProvider(
            Func<IpcRequest, CancellationToken, Task<IpcResponse>> handler)
        {
            var services = new ServiceCollection();
            services.AddScoped<IIpcRequestHandler>(
                _ => new DelegatingHandler(handler));
            return services.BuildServiceProvider();
        }

        private static NamedPipeClientStream CreateClient(string pipeName)
        {
            return new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
        }

        private static async Task<string?>
            SendRequestAndReadResponseAsync(
                NamedPipeClientStream client,
                string request,
                string terminator,
                CancellationToken cancellationToken)
        {
            await WriteRequestAsync(
                client,
                request,
                terminator,
                cancellationToken);
            using var reader = new StreamReader(
                client,
                new UTF8Encoding(false),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true);
            return await reader.ReadLineAsync(cancellationToken);
        }

        private static async Task WriteRequestAsync(
            NamedPipeClientStream client,
            string request,
            string terminator,
            CancellationToken cancellationToken)
        {
            using var writer = new StreamWriter(
                client,
                new UTF8Encoding(false),
                bufferSize: 1024,
                leaveOpen: true)
            {
                AutoFlush = false
            };
            await writer.WriteAsync(
                request.AsMemory(),
                cancellationToken);
            await writer.WriteAsync(
                terminator.AsMemory(),
                cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }

        private sealed class DelegatingHandler(
            Func<IpcRequest, CancellationToken, Task<IpcResponse>> handle)
            : IIpcRequestHandler
        {
            public Task<IpcResponse> HandleAsync(
                IpcRequest request,
                CancellationToken cancellationToken)
            {
                return handle(request, cancellationToken);
            }
        }

        private static int[] GetFailureCounts(CollectingSink sink)
        {
            return sink.Events
                .Where(logEvent =>
                    logEvent.MessageTemplate.Text.StartsWith(
                        "IPC server iteration failed after ",
                        StringComparison.Ordinal))
                .Select(logEvent =>
                    (int)((ScalarValue)logEvent.Properties[
                        "FailureCount"]).Value!)
                .ToArray();
        }

        private sealed class CollectingSink : ILogEventSink
        {
            private readonly ConcurrentQueue<LogEvent> events = new();

            internal IReadOnlyCollection<LogEvent> Events =>
                events.ToArray();

            public void Emit(LogEvent logEvent)
            {
                events.Enqueue(logEvent);
            }
        }

        private const string ValidRequest =
            "{\"action\":\"open\",\"path\":\"test.pack\"}";
    }
}
