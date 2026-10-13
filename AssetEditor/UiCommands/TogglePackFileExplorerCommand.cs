using AssetEditor.ViewModels;
using Shared.Core.Events;

namespace AssetEditor.UiCommands
{
    public class TogglePackFileExplorerCommand : IUiCommand
    {
        private readonly MainViewModel _mainViewModel;

        public TogglePackFileExplorerCommand(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
        }

        public void Execute()
        {
            _mainViewModel.IsPackFileExplorerVisible = !_mainViewModel.IsPackFileExplorerVisible;
        }
    }
}
