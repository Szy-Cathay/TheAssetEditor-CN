using Shared.Core.Settings;

namespace Shared.Core.Events.Global
{
    public sealed record ViewportRenderSettingsChangedEvent(
        ViewportRenderSettings Settings);
}
