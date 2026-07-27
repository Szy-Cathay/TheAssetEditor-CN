using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Shared.Core.ErrorHandling;

namespace Editors.Ipc
{
    internal sealed record AssetEditorIpcServerOptions(
        TimeSpan RetryDelay,
        TimeSpan ReadTimeout,
        TimeSpan WriteTimeout,
        int MaxRequestChars)
    {
        internal static AssetEditorIpcServerOptions Default { get; } = new(
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5),
            64 * 1024);
    }

    public class AssetEditorIpcServer : IDisposable
    {
        public const string PipeName = "AssetEditor.CN.Ipc";

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly ILogger _logger = Logging.Create<AssetEditorIpcServer>();
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly AssetEditorIpcServerOptions _options;
        private readonly Func<NamedPipeServerStream> _pipeFactory;
        private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
        private readonly object _syncLock = new();

        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _serverTask;
        private NamedPipeServerStream? _activePipe;
        private FailureKey? _lastFailureKey;
        private int _matchingFailureCount;
        private bool _disposed;

        internal static PipeOptions ProductionPipeOptions { get; } =
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

        public AssetEditorIpcServer(IServiceScopeFactory scopeFactory)
            : this(
                scopeFactory,
                AssetEditorIpcServerOptions.Default,
                () => CreateProductionPipe(PipeName),
                static (delay, cancellationToken) =>
                    Task.Delay(delay, cancellationToken))
        {
        }

        internal AssetEditorIpcServer(
            IServiceScopeFactory scopeFactory,
            AssetEditorIpcServerOptions options,
            Func<NamedPipeServerStream> pipeFactory,
            Func<TimeSpan, CancellationToken, Task> delayAsync)
        {
            ArgumentNullException.ThrowIfNull(scopeFactory);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(pipeFactory);
            ArgumentNullException.ThrowIfNull(delayAsync);
            if (options.RetryDelay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "The IPC retry delay cannot be negative.");
            }

            if (options.ReadTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "The IPC read timeout must be positive.");
            }

            if (options.WriteTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "The IPC write timeout must be positive.");
            }

            if (options.MaxRequestChars <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "The IPC request character limit must be positive.");
            }

            _scopeFactory = scopeFactory;
            _options = options;
            _pipeFactory = pipeFactory;
            _delayAsync = delayAsync;
        }

        internal static NamedPipeServerStream CreateProductionPipe(
            string pipeName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
            return new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                ProductionPipeOptions);
        }

        internal static async Task<string?> ReadBoundedLineAsync(
            TextReader reader,
            int maxChars,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(reader);
            if (maxChars <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxChars));

            var buffer = new char[1024];
            var line = new StringBuilder();
            var pendingCarriageReturn = false;

            void Append(char character)
            {
                line.Append(character);
                if (line.Length > maxChars)
                {
                    throw new InvalidDataException(
                        $"IPC request exceeds the {maxChars}-character limit.");
                }
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var charsRead = await reader.ReadAsync(
                    buffer.AsMemory(),
                    cancellationToken);
                if (charsRead == 0)
                {
                    if (pendingCarriageReturn)
                        Append('\r');
                    return line.Length == 0 ? null : line.ToString();
                }

                for (var index = 0; index < charsRead; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var character = buffer[index];
                    if (character == '\n')
                        return line.ToString();

                    if (pendingCarriageReturn)
                    {
                        Append('\r');
                        pendingCarriageReturn = false;
                    }

                    if (character == '\r')
                        pendingCarriageReturn = true;
                    else
                        Append(character);
                }
            }
        }

        public void Start()
        {
            lock (_syncLock)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(AssetEditorIpcServer));

                if (_serverTask != null)
                    return;

                var cancellationTokenSource =
                    new CancellationTokenSource();
                _cancellationTokenSource = cancellationTokenSource;
                _serverTask = Task.Run(
                    () => RunServerLoopAsync(
                        cancellationTokenSource.Token));
            }
        }

        private async Task RunServerLoopAsync(CancellationToken cancellationToken)
        {
            _logger.Here().Information($"Starting IPC named pipe server on {PipeName}");

            while (cancellationToken.IsCancellationRequested == false)
            {
                NamedPipeServerStream? pipe = null;
                Exception? iterationFailure = null;
                var failurePhase = FailurePhase.Create;
                try
                {
                    pipe = _pipeFactory()
                        ?? throw new InvalidOperationException(
                            "The IPC pipe factory returned null.");
                    SetActivePipe(pipe);

                    failurePhase = FailurePhase.AcceptOrRead;
                    await pipe.WaitForConnectionAsync(cancellationToken);

                    string? line;
                    using (var readCancellationTokenSource =
                           CancellationTokenSource.CreateLinkedTokenSource(
                               cancellationToken))
                    {
                        readCancellationTokenSource.CancelAfter(
                            _options.ReadTimeout);
                        using var reader = new StreamReader(
                            pipe,
                            new UTF8Encoding(false),
                            detectEncodingFromByteOrderMarks: false,
                            bufferSize: 1024,
                            leaveOpen: true);
                        line = await ReadBoundedLineAsync(
                            reader,
                            _options.MaxRequestChars,
                            readCancellationTokenSource.Token);
                    }

                    failurePhase = FailurePhase.Handler;
                    var requestResult = await ProcessRequestAsync(
                        line,
                        cancellationToken);

                    failurePhase = FailurePhase.Write;
                    using (var writeCancellationTokenSource =
                           CancellationTokenSource.CreateLinkedTokenSource(
                               cancellationToken))
                    {
                        writeCancellationTokenSource.CancelAfter(
                            _options.WriteTimeout);
                        await WriteResponseAsync(
                            pipe,
                            requestResult.Response,
                            writeCancellationTokenSource.Token);
                    }

                    if (requestResult.IsValidRequest)
                        ResetFailureHistory();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    iterationFailure = ex;
                }
                finally
                {
                    ClearActivePipe(pipe);
                    try
                    {
                        pipe?.Dispose();
                    }
                    catch (Exception disposeException)
                    {
                        iterationFailure ??= disposeException;
                    }
                }

                if (iterationFailure == null)
                    continue;

                RecordIterationFailure(
                    failurePhase,
                    iterationFailure,
                    cancellationToken);
                try
                {
                    await _delayAsync(
                        _options.RetryDelay,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.Here().Information("IPC named pipe server stopped");
        }

        private async Task<RequestResult> ProcessRequestAsync(
            string? line,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return new RequestResult(
                    IpcResponse.Failure("Empty request"),
                    false);
            }

            IpcRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<IpcRequest>(line, SerializerOptions);
            }
            catch (JsonException)
            {
                return new RequestResult(
                    IpcResponse.Failure("Invalid JSON"),
                    false);
            }

            if (request == null)
            {
                return new RequestResult(
                    IpcResponse.Failure("Invalid JSON"),
                    false);
            }

            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<IIpcRequestHandler>();

            try
            {
                return new RequestResult(
                    await handler.HandleAsync(
                        request,
                        cancellationToken),
                    true);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                return new RequestResult(
                    IpcResponse.Failure("Canceled"),
                    true);
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "IPC request handling failed");
                return new RequestResult(
                    IpcResponse.Failure("Internal server error"),
                    true);
            }
        }

        private static async Task WriteResponseAsync(
            NamedPipeServerStream pipe,
            IpcResponse response,
            CancellationToken cancellationToken)
        {
            StreamWriter? writer = null;
            var explicitFlushCompleted = false;
            try
            {
                writer = new StreamWriter(
                    pipe,
                    new UTF8Encoding(false),
                    bufferSize: 1024,
                    leaveOpen: true)
                {
                    AutoFlush = false
                };

                var json = JsonSerializer.Serialize(
                    response,
                    SerializerOptions);
                await writer.WriteLineAsync(
                    json.AsMemory(),
                    cancellationToken);
                await writer.FlushAsync(cancellationToken);
                explicitFlushCompleted = true;
            }
            catch
            {
                try
                {
                    pipe.Dispose();
                }
                catch
                {
                }

                throw;
            }
            finally
            {
                if (writer != null)
                {
                    try
                    {
                        writer.Dispose();
                    }
                    catch when (!explicitFlushCompleted)
                    {
                    }
                }
            }
        }

        private void RecordIterationFailure(
            FailurePhase phase,
            Exception exception,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            var key = new FailureKey(
                phase,
                exception.GetType().FullName
                ?? exception.GetType().Name,
                exception.HResult);
            if (_lastFailureKey == key)
            {
                _matchingFailureCount++;
            }
            else
            {
                _lastFailureKey = key;
                _matchingFailureCount = 1;
            }

            if (_matchingFailureCount != 1
                && _matchingFailureCount % 10 != 0)
            {
                return;
            }

            _logger.Here().Error(
                exception,
                "IPC server iteration failed after {FailureCount} consecutive matching failures in {FailurePhase}",
                _matchingFailureCount,
                phase);
        }

        private void ResetFailureHistory()
        {
            _lastFailureKey = null;
            _matchingFailureCount = 0;
        }

        private void SetActivePipe(NamedPipeServerStream pipe)
        {
            lock (_syncLock)
            {
                _activePipe = pipe;
            }
        }

        private void ClearActivePipe(NamedPipeServerStream? pipe)
        {
            lock (_syncLock)
            {
                if (ReferenceEquals(_activePipe, pipe))
                    _activePipe = null;
            }
        }

        public void Dispose()
        {
            CancellationTokenSource? cancellationTokenSource;
            Task? serverTask;
            NamedPipeServerStream? activePipe;

            lock (_syncLock)
            {
                if (_disposed)
                    return;

                _disposed = true;
                cancellationTokenSource = _cancellationTokenSource;
                serverTask = _serverTask;
                activePipe = _activePipe;

                _cancellationTokenSource = null;
                _serverTask = null;
                _activePipe = null;
            }

            try
            {
                cancellationTokenSource?.Cancel();
            }
            catch
            {
            }

            try
            {
                activePipe?.Dispose();
            }
            catch
            {
            }

            var serverTaskCompleted = serverTask == null;
            if (serverTask != null)
            {
                try
                {
                    serverTaskCompleted = serverTask.Wait(
                        TimeSpan.FromSeconds(2));
                }
                catch
                {
                    serverTaskCompleted = serverTask.IsCompleted;
                }
            }

            if (cancellationTokenSource == null)
                return;

            if (serverTaskCompleted)
            {
                cancellationTokenSource.Dispose();
                return;
            }

            _logger.Here().Warning(
                "IPC server did not stop within the bounded shutdown wait");
            _ = serverTask!.ContinueWith(
                _ => cancellationTokenSource.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private readonly record struct RequestResult(
            IpcResponse Response,
            bool IsValidRequest);

        private readonly record struct FailureKey(
            FailurePhase Phase,
            string ExceptionType,
            int HResult);

        private enum FailurePhase
        {
            Create,
            AcceptOrRead,
            Handler,
            Write
        }
    }
}
