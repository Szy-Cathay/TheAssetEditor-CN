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
        private readonly object _syncLock = new();

        private CancellationTokenSource _cancellationTokenSource;
        private Task _serverTask;
        private NamedPipeServerStream _activePipe;
        private bool _disposed;

        public AssetEditorIpcServer(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
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

                _cancellationTokenSource = new CancellationTokenSource();
                _serverTask = Task.Run(() => RunServerLoopAsync(_cancellationTokenSource.Token));
            }
        }

        private async Task RunServerLoopAsync(CancellationToken cancellationToken)
        {
            _logger.Here().Information($"Starting IPC named pipe server on {PipeName}");

            while (cancellationToken.IsCancellationRequested == false)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    SetActivePipe(pipe);

                    await pipe.WaitForConnectionAsync(cancellationToken);

                    var response = await ProcessRequestAsync(pipe, cancellationToken);
                    await WriteResponseAsync(pipe, response);
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
                    _logger.Here().Error(ex, "Unhandled exception in IPC server loop");
                }
                finally
                {
                    ClearActivePipe(pipe);
                    pipe?.Dispose();
                }
            }

            _logger.Here().Information("IPC named pipe server stopped");
        }

        private async Task<IpcResponse> ProcessRequestAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            var line = await reader.ReadLineAsync();

            if (string.IsNullOrWhiteSpace(line))
                return IpcResponse.Failure("Empty request");

            IpcRequest request;
            try
            {
                request = JsonSerializer.Deserialize<IpcRequest>(line, SerializerOptions);
            }
            catch (JsonException)
            {
                return IpcResponse.Failure("Invalid JSON");
            }

            if (request == null)
                return IpcResponse.Failure("Invalid JSON");

            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<IIpcRequestHandler>();

            try
            {
                return await handler.HandleAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return IpcResponse.Failure("Canceled");
            }
            catch (Exception ex)
            {
                _logger.Here().Error(ex, "IPC request handling failed");
                return IpcResponse.Failure("Internal server error");
            }
        }

        private static async Task WriteResponseAsync(NamedPipeServerStream pipe, IpcResponse response)
        {
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
            {
                AutoFlush = true
            };

            var json = JsonSerializer.Serialize(response, SerializerOptions);
            await writer.WriteLineAsync(json);
        }

        private void SetActivePipe(NamedPipeServerStream pipe)
        {
            lock (_syncLock)
            {
                _activePipe = pipe;
            }
        }

        private void ClearActivePipe(NamedPipeServerStream pipe)
        {
            lock (_syncLock)
            {
                if (ReferenceEquals(_activePipe, pipe))
                    _activePipe = null;
            }
        }

        public void Dispose()
        {
            CancellationTokenSource cancellationTokenSource;
            Task serverTask;
            NamedPipeServerStream activePipe;

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

            if (serverTask != null)
            {
                try
                {
                    _ = serverTask.Wait(TimeSpan.FromSeconds(2));
                }
                catch
                {
                }
            }

            cancellationTokenSource?.Dispose();
        }
    }
}
