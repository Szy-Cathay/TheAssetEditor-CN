using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.Presentation.View;
using Shared.Core.Events;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.AnimationMeta.Parsing;
using System.Windows;

namespace Editors.AnimationMeta.MetaEditor.Commands
{
    internal class NewEntryCommand : IUiCommand
    {
        private readonly MetaDataFileParser _metaDataFileParser;
        private readonly IMetaDataDatabase _metaDataDatabase;
        private readonly ApplicationSettingsService _settingsService;

        public NewEntryCommand(
            MetaDataFileParser metaDataFileParser,
            IMetaDataDatabase metaDataDatabase,
            ApplicationSettingsService settingsService)
        {
            _metaDataFileParser = metaDataFileParser;
            _metaDataDatabase = metaDataDatabase;
            _settingsService = settingsService;
        }

        public void Execute(MetaDataEditorViewModel controller)
        {
            var parsedFile = controller.ParsedFile;
            if (parsedFile == null)
                return;

            var dialog = new NewMetaDataEntryWindow
            {
                Owner = Application.Current.MainWindow
            };
            var allDefs = AnimationMetaTagCatalog.FilterForGame(
                _settingsService.CurrentSettings.CurrentGame,
                _metaDataDatabase.GetSupportedTypes(),
                _metaDataDatabase.GetDefinition);

            var model = new NewTagWindowViewModel();
            model.SetItems(allDefs.Select(definitionName =>
            {
                var separatorIndex = definitionName.LastIndexOf('_');
                var tagName = separatorIndex > 0
                    ? definitionName[..separatorIndex]
                    : definitionName;
                var description = MetaDataEditorViewModel
                    .LocalizeTagDescription(tagName);

                var categoryKey = AnimationMetaTagCatalog.GetCategoryKey(
                    definitionName);
                return new NewTagWindowItem(
                    definitionName,
                    description,
                    LocalizationManager.Instance.Get(categoryKey),
                    AnimationMetaTagCatalog.GetCategoryOrder(categoryKey));
            }).OrderBy(item => item.CategoryOrder)
                .ThenBy(item => item.Name, StringComparer.Ordinal));
            dialog.DataContext = model;

            var result = dialog.ShowDialog();
            if (result == true &&
                model.SelectedItem is NewTagWindowItem selectedItem)
            {
                var newEntry = _metaDataFileParser.CreateDefault(
                    selectedItem.Name);
                parsedFile.Attributes.Add(newEntry);
                controller.UpdateView();
                controller.SelectedTag = controller.Tags.LastOrDefault();
            }

            dialog.DataContext = null;
        }
    }
}
