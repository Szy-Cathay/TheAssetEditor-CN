namespace GameWorld.Core.WpfWindow.FactionColourSettings
{
    public interface IFactionColourSettingsDialogService
    {
        void ShowDialog();
    }

    public sealed class FactionColourSettingsDialogService :
        IFactionColourSettingsDialogService
    {
        private readonly Services.FactionColourSettingsService
            _settingsService;

        public FactionColourSettingsDialogService(
            Services.FactionColourSettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public void ShowDialog()
        {
            var viewModel = new FactionColourSettingsViewModel(
                _settingsService);
            var window = new FactionColourSettingsWindow(viewModel);
            if (window.ShowDialog() == true)
                viewModel.Save();
            else
                viewModel.Cancel();
        }
    }
}
