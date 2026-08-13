using Shared.Core.PackFiles.Models;

namespace Editors.Ipc
{
    public interface IIpcRequestHandler
    {
        Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken cancellationToken);
    }

    public interface IExternalPackFileLookup
    {
        PackFile FindByPath(string path);
    }

    public interface IIpcUserNotifier
    {
        Task ShowExternalOpenFailedAsync(string normalizedPath, CancellationToken cancellationToken);
    }

}
