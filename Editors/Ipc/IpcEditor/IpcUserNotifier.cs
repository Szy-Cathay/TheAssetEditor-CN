using System.Windows;
using Shared.Core.Services;

namespace Editors.Ipc
{
    public class IpcUserNotifier : IIpcUserNotifier
    {
        private readonly IStandardDialogs _standardDialogs;
        private readonly LocalizationManager _localizationManager;

        public IpcUserNotifier(
            IStandardDialogs standardDialogs,
            LocalizationManager localizationManager)
        {
            _standardDialogs = standardDialogs;
            _localizationManager = localizationManager;
        }

        public async Task ShowExternalOpenFailedAsync(string normalizedPath, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            var message = _localizationManager.GetFormat(
                "ExternalPack.Open.ResourceNotFound",
                normalizedPath);
            var title = _localizationManager.Get(
                "ExternalPack.Open.Title");
            var app = Application.Current;
            if (app?.Dispatcher == null)
            {
                _standardDialogs.ShowDialogBox(message, title);
                return;
            }

            if (app.Dispatcher.CheckAccess())
            {
                _standardDialogs.ShowDialogBox(message, title);
                return;
            }

            await app.Dispatcher.InvokeAsync(() =>
                _standardDialogs.ShowDialogBox(message, title));
        }
    }
}
