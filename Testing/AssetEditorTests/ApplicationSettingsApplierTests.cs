using AssetEditor.Services.Settings;
using NUnit.Framework;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.Settings;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

public class ApplicationSettingsApplierTests
{
    [Test]
    public void CompleteSave_ClassifiesRestartAndPublishesFocusedChanges()
    {
        var settings = new ApplicationSettingsService(
            GameTypeEnum.Warhammer3);
        settings.CurrentSettings.GameDirectories.Add(
            new ApplicationSettings.GamePathPair
            {
                Game = GameTypeEnum.Warhammer3,
                Path = @"C:\old"
            });
        var eventHub = new TestEventHub();
        var viewportChanges = new List<ViewportRenderSettings>();
        var caWemChanges = new List<bool>();
        eventHub.Register<ViewportRenderSettingsChangedEvent>(
            this,
            value => viewportChanges.Add(value.Settings));
        eventHub.Register<ShowCaWemFilesChangedEvent>(
            this,
            value => caWemChanges.Add(value.ShowFiles));
        var applier = new ApplicationSettingsApplier(
            settings,
            eventHub);

        settings.CurrentSettings.GameDirectories[0].Path = @"C:\new";
        settings.CurrentSettings.ShowCAWemFiles = true;
        settings.CurrentSettings.OnlyLoadLod0ForReferenceMeshes = false;
        settings.CurrentSettings.RenderEngineBackgroundColour =
            BackgroundColour.Green;

        var result = applier.CompleteSave();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(result.RequiresApplicationRestart, Is.True);
            NUnitAssert.That(result.RequiresModelReload, Is.True);
            NUnitAssert.That(viewportChanges,
                Has.One.Items.Property(nameof(ViewportRenderSettings.BackgroundColour))
                    .EqualTo(BackgroundColour.Green));
            NUnitAssert.That(caWemChanges, Is.EqualTo(new[] { true }));
        });
    }

    [Test]
    public void CompleteSave_OtherGamePathDoesNotRequireCurrentGameRestart()
    {
        var settings = new ApplicationSettingsService(
            GameTypeEnum.Warhammer3);
        settings.CurrentSettings.GameDirectories.Add(
            new ApplicationSettings.GamePathPair
            {
                Game = GameTypeEnum.Rome2,
                Path = @"C:\old"
            });
        var applier = new ApplicationSettingsApplier(
            settings,
            new TestEventHub());

        settings.CurrentSettings.GameDirectories[0].Path = @"C:\new";

        var result = applier.CompleteSave();

        NUnitAssert.That(result.RequiresApplicationRestart, Is.False);
    }

    [Test]
    public void RestorePreview_ReappliesOriginalViewportSettings()
    {
        var settings = new ApplicationSettingsService();
        settings.CurrentSettings.ViewportGridColour = "10,20,30";
        var eventHub = new TestEventHub();
        var changes = new List<ViewportRenderSettings>();
        eventHub.Register<ViewportRenderSettingsChangedEvent>(
            this,
            value => changes.Add(value.Settings));
        var applier = new ApplicationSettingsApplier(
            settings,
            eventHub);

        applier.PreviewViewport(
            ViewportRenderSettings.From(settings.CurrentSettings) with
            {
                GridColour = "90,80,70"
            });
        applier.RestoreViewportPreview();

        NUnitAssert.That(
            changes.Select(value => value.GridColour),
            Is.EqualTo(new[] { "90,80,70", "10,20,30" }));
    }
}
