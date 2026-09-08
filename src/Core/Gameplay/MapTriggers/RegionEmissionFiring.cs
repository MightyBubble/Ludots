using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Shared event outlet for region crossings (#1468): both the vector volume
    /// line (RegionVolumeTriggerSystem) and the field membership line
    /// (FieldRegionMembershipSystem) fire through this one implementation. The
    /// crossing entity always rides MapTrigger.SourceEntity (transport metadata);
    /// per-key facts (RegionId / FieldLayer) ride only on engine-default events
    /// whose schemas declare them — custom schemas cannot declare MapTrigger.* keys,
    /// so authored payload is the volume/region identity channel.
    /// </summary>
    public static class RegionEmissionFiring
    {
        private static readonly List<string> EmptyTags = new List<string>();

        public static void Fire(
            TriggerManager triggerManager,
            Func<ScriptContext> contextFactory,
            MapSession session,
            EventKey eventKey,
            string regionId,
            Entity crossingEntity,
            EventSchema? schema,
            RegionVolumePayloadEntry[]? payload,
            string? extraFactKey = null,
            string? extraFactValue = null)
        {
            ScriptContext context = contextFactory();
            context.Set(CoreServiceKeys.MapId, session.MapId);
            context.Set(CoreServiceKeys.MapSession, session);
            context.Set(CoreServiceKeys.MapTags, session.MapConfig?.Tags ?? EmptyTags);
            context.Set(MapTriggerEventPayloadKeys.SourceEntity, crossingEntity);
            if (schema == null || schema.DeclaresPayloadKey(MapTriggerEventPayloadKeys.RegionId))
            {
                context.Set(MapTriggerEventPayloadKeys.RegionId, regionId);
            }

            if (extraFactValue != null &&
                (schema == null || schema.DeclaresPayloadKey(extraFactKey!)))
            {
                context.Set(extraFactKey!, extraFactValue);
            }

            if (payload != null)
            {
                for (int i = 0; i < payload.Length; i++)
                {
                    RegionVolumePayloadEntry entry = payload[i];
                    switch (entry.Type)
                    {
                        case RegionVolumePayloadValueType.Int:
                            context.Set(entry.Key, entry.IntValue);
                            break;
                        case RegionVolumePayloadValueType.Float:
                            context.Set(entry.Key, entry.FloatValue);
                            break;
                        case RegionVolumePayloadValueType.String:
                            context.Set(entry.Key, entry.StringValue ?? string.Empty);
                            break;
                    }
                }
            }

            triggerManager.FireMapEvent(session.MapId, eventKey, context);
        }
    }
}
