using System;
using Ludots.Core.Engine;

namespace CapabilityStandardMassNavigationLargeWorld10kMod;

internal static class CapabilityStandardMassNavigationLargeWorld10kMapFocus
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
