using Editors.Ipc;

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
    }
}
