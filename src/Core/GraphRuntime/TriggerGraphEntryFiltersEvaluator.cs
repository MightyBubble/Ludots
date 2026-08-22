using System;
using Ludots.Core.Scripting;

namespace Ludots.Core.GraphRuntime
{
    /// <summary>
    /// Entry-side filter evaluation for mounted TriggerGraph graphs. A declared filter that
    /// is missing from the event payload never matches (fail closed, no throw — the entry
    /// simply does not fire for that event).
    /// Tag filtering fails closed while declared: no current map trigger event carries a
    /// tag payload, so a declared tag filter can never match until tag-bearing events exist.
    /// </summary>
    public static class TriggerGraphEntryFiltersEvaluator
    {
        public static bool Matches(ScriptContext context, in TriggerGraphEntryFilters filters)
        {
            if (filters.IsEmpty)
            {
                return true;
            }

            if (context == null)
            {
                return false;
            }

            if (filters.Region != null)
            {
                if (!TryGetPayloadString(context, MapTriggerEventPayloadKeys.RegionId, out string regionId) ||
                    !string.Equals(regionId, filters.Region, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            if (filters.Tag != null)
            {
                return false;
            }

            if (filters.Action != null)
            {
                if (!TryGetPayloadString(context, MapTriggerEventPayloadKeys.InputAction, out string actionId) ||
                    !string.Equals(actionId, filters.Action, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            if (filters.Team.HasValue)
            {
                if (!TryGetPayloadInt(context, MapTriggerEventPayloadKeys.SourceTeamId, out int teamId) ||
                    teamId != filters.Team.Value)
                {
                    return false;
                }
            }

            if (filters.Threshold.HasValue || filters.Direction.HasValue)
            {
                if (!filters.Threshold.HasValue || !filters.Direction.HasValue)
                {
                    return false;
                }

                if (!TryGetPayloadInt(context, MapTriggerEventPayloadKeys.Count, out int count))
                {
                    return false;
                }

                return filters.Direction.Value switch
                {
                    TriggerGraphEntryFilterDirection.CrossAbove => count >= filters.Threshold.Value,
                    TriggerGraphEntryFilterDirection.CrossBelow => count <= filters.Threshold.Value,
                    _ => false,
                };
            }

            return true;
        }

        private static bool TryGetPayloadString(ScriptContext context, string key, out string value)
        {
            value = context.Contains(key) ? context.Get<object>(key) as string : null;
            return value != null;
        }

        private static bool TryGetPayloadInt(ScriptContext context, string key, out int value)
        {
            value = 0;
            if (!context.Contains(key) || context.Get<object>(key) is not int boxed)
            {
                return false;
            }

            value = boxed;
            return true;
        }
    }
}
