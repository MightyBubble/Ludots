using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Load-time check for <see cref="GameEvents.ControlSchemeChanged"/> payload filters.
    /// A filter on <see cref="MapTriggerEventPayloadKeys.OldSchemeId"/> or
    /// <see cref="MapTriggerEventPayloadKeys.NewSchemeId"/> is a scheme id: the compiled
    /// int is a ConfigKey, and that name must already be an installed control scheme.
    /// Entries that do not filter a scheme id are not a mapping row.
    /// </summary>
    public static class ControlSchemeChangedMapping
    {
        public static void Validate(GraphProgramRegistry programs, ControlSchemeRuntime schemes)
        {
            if (programs == null)
            {
                throw new ArgumentNullException(nameof(programs));
            }

            if (schemes == null)
            {
                throw new ArgumentNullException(nameof(schemes));
            }

            foreach (int graphId in programs.GetRegisteredGraphIds())
            {
                if (!programs.TryGetRegistration(graphId, out GraphProgramRegistration registration))
                {
                    continue;
                }

                IReadOnlyList<TriggerGraphEntry> entries = registration.TriggerGraphEntries;
                for (int i = 0; i < entries.Count; i++)
                {
                    TriggerGraphEntry entry = entries[i];
                    if (!string.Equals(entry.EventName, GameEvents.ControlSchemeChanged.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    IReadOnlyList<TriggerGraphEntryPayloadFilter>? payload = entry.Filters.Payload;
                    if (payload == null)
                    {
                        continue;
                    }

                    for (int p = 0; p < payload.Count; p++)
                    {
                        TriggerGraphEntryPayloadFilter filter = payload[p];
                        if (!string.Equals(filter.Key, MapTriggerEventPayloadKeys.OldSchemeId, StringComparison.Ordinal) &&
                            !string.Equals(filter.Key, MapTriggerEventPayloadKeys.NewSchemeId, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        RequireInstalledScheme(graphId, entry.Label, filter, schemes);
                    }
                }
            }
        }

        private static void RequireInstalledScheme(
            int graphId,
            string label,
            TriggerGraphEntryPayloadFilter filter,
            ControlSchemeRuntime schemes)
        {
            string graphName = GraphIdRegistry.GetName(graphId);
            if (string.IsNullOrEmpty(graphName))
            {
                graphName = "#" + graphId.ToString();
            }

            if (!filter.IntValue.HasValue)
            {
                throw new InvalidOperationException(
                    $"CONTROL_SCHEME.ERR.ChangedMappingUnknownScheme: graph '{graphName}' entry '{label}' " +
                    $"filters payload '{filter.Key}' must be an installed control scheme id.");
            }

            string schemeName = ConfigKeyRegistry.GetName(filter.IntValue.Value);
            string shown = string.IsNullOrEmpty(schemeName) ? "#" + filter.IntValue.Value.ToString() : schemeName;
            if (string.IsNullOrEmpty(schemeName) ||
                !schemes.SchemeIdRegistry.TryGetId(schemeName, out int schemeId) ||
                !schemes.IsInstalled(schemeId))
            {
                throw new InvalidOperationException(
                    $"CONTROL_SCHEME.ERR.ChangedMappingUnknownScheme: graph '{graphName}' entry '{label}' " +
                    $"filters payload '{filter.Key}' value '{shown}' is not an installed control scheme.");
            }
        }
    }
}
