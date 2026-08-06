using System.Text.Json;
using Shared.Core.Settings;

namespace Test.Shared.Core.Settings;

public class ViewportRenderSettingsTests
{
    [Test]
    public void LegacySettings_ReceiveStableViewportDefaults()
    {
        var applicationSettings = JsonSerializer.Deserialize<ApplicationSettings>(
            """
            {
              "Theme": 0
            }
            """);

        var viewportSettings = ViewportRenderSettings.From(applicationSettings!);

        Assert.Multiple(() =>
        {
            Assert.That(viewportSettings.BackgroundColour,
                Is.EqualTo(BackgroundColour.DarkGrey));
            Assert.That(viewportSettings.CustomBackgroundColour,
                Is.EqualTo("50,50,50"));
            Assert.That(viewportSettings.SimulateGameBackfaces, Is.False);
            Assert.That(viewportSettings.ShowGrid, Is.True);
            Assert.That(viewportSettings.GridColour,
                Is.EqualTo("0,0,0"));
            Assert.That(viewportSettings.LightIntensity,
                Is.EqualTo(1.0f));
            Assert.That(viewportSettings.EnvironmentLightRotationY,
                Is.EqualTo(20.0f));
            Assert.That(viewportSettings.DirectLightRotationX,
                Is.EqualTo(0.0f));
            Assert.That(viewportSettings.DirectLightRotationY,
                Is.EqualTo(0.0f));
            Assert.That(viewportSettings.FactionColoursEnabled, Is.True);
            Assert.That(viewportSettings.FactionColour0,
                Is.EqualTo("255,0,0"));
            Assert.That(viewportSettings.FactionColour1,
                Is.EqualTo("100,169,226"));
            Assert.That(viewportSettings.FactionColour2,
                Is.EqualTo("255,255,255"));
        });
    }

    [Test]
    public void ViewportSettingsSerialization_PreservesFactionPreview()
    {
        var settings = new ApplicationSettings
        {
            ViewportFactionColoursEnabled = false,
            ViewportFactionColour0 = "1,2,3",
            ViewportFactionColour1 = "4,5,6",
            ViewportFactionColour2 = "7,8,9"
        };

        var json = JsonSerializer.Serialize(settings);
        var loaded = JsonSerializer.Deserialize<ApplicationSettings>(json)!;
        var viewportSettings = ViewportRenderSettings.From(loaded);

        Assert.Multiple(() =>
        {
            Assert.That(viewportSettings.FactionColoursEnabled, Is.False);
            Assert.That(viewportSettings.FactionColour0,
                Is.EqualTo("1,2,3"));
            Assert.That(viewportSettings.FactionColour1,
                Is.EqualTo("4,5,6"));
            Assert.That(viewportSettings.FactionColour2,
                Is.EqualTo("7,8,9"));
        });
    }
}
