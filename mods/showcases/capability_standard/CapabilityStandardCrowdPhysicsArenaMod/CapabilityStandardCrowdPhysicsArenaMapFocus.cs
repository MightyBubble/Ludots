using System;
using Ludots.Core.Engine;

namespace CapabilityStandardCrowdPhysicsArenaMod;

internal static class CapabilityStandardCrowdPhysicsArenaMapFocus
{
    public static bool IsStartupMapFocused(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        string? startupMapId = engine.MergedConfig?.StartupMapId;
        return !string.IsNullOrWhiteSpace(startupMapId) &&
               string.Equals(
                   engine.CurrentMapSession?.MapId.Value,
                   startupMapId,
                   StringComparison.Ordinal);
    }
}
