using Serilog;
using Shared.Core.ErrorHandling;

namespace Editors.Ipc
{
    public class IpcRequestHandler : IIpcRequestHandler
    {
        private readonly ILogger _logger = Logging.Create<IpcRequestHandler>();
        private readonly IExternalPackOpenWorkflow _externalPackWorkflow;
        private readonly IExternalPackFileLookup _packFileLookup;
        private readonly IExternalFileOpenExecutor _fileOpenExecutor;
        private readonly IIpcUserNotifier _userNotifier;

        public IpcRequestHandler(IExternalPackOpenWorkflow externalPackWorkflow, IExternalPackFileLookup packFileLookup, IExternalFileOpenExecutor fileOpenExecutor, IIpcUserNotifier userNotifier)
        {
            _externalPackWorkflow = externalPackWorkflow;
            _packFileLookup = packFileLookup;
            _fileOpenExecutor = fileOpenExecutor;
            _userNotifier = userNotifier;
        }

        public async Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var action = request.Action?.Trim();
            if (string.IsNullOrWhiteSpace(action))
                return IpcResponse.Failure("Unsupported action");

            if (string.Equals(action, "open", StringComparison.OrdinalIgnoreCase) == false)
                return IpcResponse.Failure("Unsupported action");

            var packPathOnDisk = GetPackPathOnDisk(request);
            if (string.IsNullOrWhiteSpace(packPathOnDisk) == false)
            {
                var openResult = await _externalPackWorkflow.OpenAsync(
                    packPathOnDisk,
                    cancellationToken);
                if (!openResult.CanOpenRequestedFile)
                {
                    return IpcResponse.Failure(
                        openResult.Error ?? GetExternalPackError(
                            openResult.Status));
                }
            }

            var normalizedPath = PackPathResolver.ResolvePackPath(request.Path);
            if (string.IsNullOrWhiteSpace(normalizedPath))
                return IpcResponse.Failure("Path is empty");

            var packFile = _packFileLookup.FindByPath(normalizedPath);
            if (packFile == null)
            {
                _logger.Here().Information($"External open failed. File not found: {normalizedPath}");
                await _userNotifier.ShowExternalOpenFailedAsync(normalizedPath, cancellationToken);
                return IpcResponse.Failure("File not found", normalizedPath);
            }

            var bringToFront = request.BringToFront != false;
            var openInExistingKitbashTab = request.OpenInExistingKitbashTab == true;
            await _fileOpenExecutor.OpenAsync(packFile, bringToFront, openInExistingKitbashTab, cancellationToken);

            return IpcResponse.Success();
        }

        private static string GetPackPathOnDisk(IpcRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.PackPathOnDisk) == false)
                return request.PackPathOnDisk;

            return string.Empty;
        }

        private static string GetExternalPackError(
            ExternalPackOpenStatus status)
        {
            return status switch
            {
                ExternalPackOpenStatus.Cancelled =>
                    "External Pack open canceled",
                ExternalPackOpenStatus.RoleConflict =>
                    "External Pack role conflict",
                _ => "External Pack open failed",
            };
        }
    }
}
