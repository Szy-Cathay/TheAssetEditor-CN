using GameWorld.Core.Services;
using Moq;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.Settings;

namespace GameWorld.Core.Test.Rendering;

public class FactionColourSettingsServiceTests
{
    [Test]
    public void Save_PersistsSharedSettingsAndPublishesGlobalPreview()
    {
        var applicationSettings = new ApplicationSettingsService();
        var eventHub = new Mock<IGlobalEventHub>();
        ViewportRenderSettingsChangedEvent published = null!;
        eventHub
            .Setup(hub => hub.PublishGlobalEvent(
                It.IsAny<ViewportRenderSettingsChangedEvent>()))
            .Callback<ViewportRenderSettingsChangedEvent>(
                value => published = value);
        var service = new FactionColourSettingsService(
            applicationSettings,
            eventHub.Object);
        var factionSettings = new FactionColourSettings(
            false,
            "1,2,3",
            "4,5,6",
            "7,8,9");

        service.Save(factionSettings);

        Assert.Multiple(() =>
        {
            Assert.That(
                applicationSettings.CurrentSettings
                    .ViewportFactionColoursEnabled,
                Is.False);
            Assert.That(
                applicationSettings.CurrentSettings
                    .ViewportFactionColour0,
                Is.EqualTo("1,2,3"));
            Assert.That(
                applicationSettings.CurrentSettings
                    .ViewportFactionColour1,
                Is.EqualTo("4,5,6"));
            Assert.That(
                applicationSettings.CurrentSettings
                    .ViewportFactionColour2,
                Is.EqualTo("7,8,9"));
            Assert.That(published, Is.Not.Null);
            Assert.That(published!.Settings.FactionColoursEnabled,
                Is.False);
            Assert.That(published.Settings.FactionColour1,
                Is.EqualTo("4,5,6"));
        });
    }

    [Test]
    public void Preview_PublishesWithoutChangingPersistedSettings()
    {
        var applicationSettings = new ApplicationSettingsService();
        var eventHub = new Mock<IGlobalEventHub>();
        ViewportRenderSettingsChangedEvent published = null!;
        eventHub
            .Setup(hub => hub.PublishGlobalEvent(
                It.IsAny<ViewportRenderSettingsChangedEvent>()))
            .Callback<ViewportRenderSettingsChangedEvent>(
                value => published = value);
        var service = new FactionColourSettingsService(
            applicationSettings,
            eventHub.Object);

        service.Preview(new FactionColourSettings(
            false,
            "9,8,7",
            "6,5,4",
            "3,2,1"));

        Assert.Multiple(() =>
        {
            Assert.That(
                applicationSettings.CurrentSettings
                    .ViewportFactionColoursEnabled,
                Is.True);
            Assert.That(
                applicationSettings.CurrentSettings
                    .ViewportFactionColour0,
                Is.EqualTo(ApplicationSettings.DefaultFactionColour0));
            Assert.That(published, Is.Not.Null);
            Assert.That(published!.Settings.FactionColour0,
                Is.EqualTo("9,8,7"));
        });
    }
}
