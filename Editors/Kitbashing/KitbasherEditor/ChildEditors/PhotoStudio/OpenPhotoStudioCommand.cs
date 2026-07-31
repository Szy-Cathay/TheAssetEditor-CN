using System.Windows.Input;
using Editors.KitbasherEditor.Core.MenuBarViews;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.Ui.Common.MenuSystem;

namespace Editors.KitbasherEditor.ChildEditors.PhotoStudio
{
    internal class OpenPhotoStudioCommand :
        ITransientKitbasherUiCommand,
        IDisposable
    {
        private readonly IAbstractFormFactory<PhotoStudioWindow>
            _windowFactory;
        private PhotoStudioWindow? _windowInstance;

        public string ToolTip
        {
            get => LocalizationManager.Instance is { } localization
                ? localization.Get(
                    "KitbashTool.PhotoStudio.OpenTooltip")
                : "打开照片工作室";
            set { }
        }

        public ActionEnabledRule EnabledRule =>
            ActionEnabledRule.Always;

        public Hotkey? HotKey =>
            new(Key.P, ModifierKeys.Control);

        public OpenPhotoStudioCommand(
            IAbstractFormFactory<PhotoStudioWindow>
                windowFactory)
        {
            _windowFactory = windowFactory;
        }

        public void Execute()
        {
            if (_windowInstance == null)
            {
                _windowInstance = _windowFactory.Create();
                _windowInstance.Show();
                _windowInstance.Closed += OnWindowClosed;
                return;
            }

            _windowInstance.Activate();
        }

        private void OnWindowClosed(
            object? sender,
            EventArgs eventArgs)
        {
            if (_windowInstance != null)
                _windowInstance.Closed -= OnWindowClosed;

            _windowInstance = null;
        }

        public void Dispose()
        {
            _windowInstance?.Close();
            _windowInstance = null;
        }
    }
}
