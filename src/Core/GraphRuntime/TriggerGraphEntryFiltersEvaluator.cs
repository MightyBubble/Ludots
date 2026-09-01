using System;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace Ludots.Core.GraphRuntime
{
    /// <summary>
    /// Entry-side filter evaluation for mounted TriggerGraph graphs. A declared filter that
    /// is missing from the event payload never matches (fail closed, no throw — the entry
    /// simply does not fire for that event). Tag filters match the TagId payload against the
    /// mount-time-resolved TagId (Gas.Event.* bridge events carry the payload; an unresolved
    /// tag name never matches). InstanceId filters reverse-resolve the event's SourceEntity
    /// through the firing map's MapLoadEntityIndex and require an exact match.
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
                if (!filters.TagId.HasValue ||
                    !TryGetPayloadInt(context, MapTriggerEventPayloadKeys.TagId, out int tagId) ||
                    tagId != filters.TagId.Value)
                {
                    return false;
                }
            }

            if (filters.InstanceId != null)
            {
                if (!context.Contains(MapTriggerEventPayloadKeys.SourceEntity) ||
                    context.Get<object>(MapTriggerEventPayloadKeys.SourceEntity) is not Arch.Core.Entity sourceEntity ||
                    !context.TryGet(CoreServiceKeys.MapSession, out MapSession? session) ||
                    session?.EntityIndex is not { } index ||
                    !index.TryGetInstanceId(sourceEntity, out string sourceInstanceId) ||
                    !string.Equals(sourceInstanceId, filters.InstanceId, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            if (filters.Action != null)
            {
                if (!TryGetPayloadString(context, MapTriggerEventPayloadKeys.Action, out string actionId) ||
                    !string.Equals(actionId, filters.Action, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            if (filters.VarName != null)
            {
                if (!TryGetPayloadString(context, MapTriggerEventPayloadKeys.VarName, out string varName) ||
                    !string.Equals(varName, filters.VarName, StringComparison.Ordinal))
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
