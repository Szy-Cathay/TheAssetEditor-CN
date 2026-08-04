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
        });
    }

    [Test]
    public void ViewportSettingsSerialization_DoesNotContainFactionPreviewColours()
    {
        var json = JsonSerializer.Serialize(
            ViewportRenderSettings.From(new ApplicationSettings()));

        Assert.That(json, Does.Not.Contain("Faction"));
    }
}
