using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Post-placement semantic pass for region volume entities: validates emission
    /// contracts (event vocabulary, payload-vs-schema, required params, Int→Float
    /// widening) and derives the session's <c>RegionVolumeKeys</c> catalog keyed by
    /// VolumeKey. Runs at map load between <c>LoadEntitiesAndIndex</c> and
    /// <c>InstantiateMapTriggers</c> on both LoadMap and PushMap paths; runtime-spawned
    /// volumes skip it and rely on fire-time <c>ValidateFirePayload</c> as backstop.
    /// </summary>
    public static class RegionVolumeBakePass
    {
        private static readonly QueryDescription VolumeQuery = new QueryDescription()
            .WithAll<MapEntity, RegionVolumeCm>();

        private static readonly QueryDescription StandaloneEmissionQuery = new QueryDescription()
            .WithAll<MapEntity, RegionVolumeEmissionCm>()
            .WithNone<RegionVolumeCm>();

        public static IReadOnlySet<string> Bake(
            World world,
            MapSession session,
            CustomEventNameRegistry customEvents,
            EventSchemaRegistry schemas)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (customEvents == null) throw new ArgumentNullException(nameof(customEvents));
            if (schemas == null) throw new ArgumentNullException(nameof(schemas));

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (ref var chunk in world.Query(in VolumeQuery))
            {
                ref var entityFirst = ref chunk.Entity(0);
                var mapEntities = chunk.GetSpan<MapEntity>();
                var volumes = chunk.GetSpan<RegionVolumeCm>();

                foreach (var index in chunk)
                {
                    if (mapEntities[index].MapId != session.MapId)
                    {
                        continue;
                    }

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    RegionVolumeCm volume = volumes[index];
                    if (!world.Has<WorldPositionCm>(entity))
                    {
                        throw new InvalidOperationException(
                            $"Map '{session.MapId.Value}' region volume '{volume.VolumeKey}' has no WorldPositionCm; the volume anchor is the entity position (set the placement 'position' or a WorldPositionCm component).");
                    }

                    if (!keys.Add(volume.VolumeKey))
                    {
                        throw new InvalidOperationException(
                            $"Map '{session.MapId.Value}' has duplicate region volume key '{volume.VolumeKey}'.");
                    }

                    if (world.TryGet(entity, out RegionVolumeEmissionCm emission))
                    {
                        emission = ValidateAndCanonicalizeEmission(emission, volume.VolumeKey, session.MapId.Value, customEvents, schemas);
                        world.Set(entity, emission);
                    }
                }
            }

            ValidateStandaloneEmissions(world, session, customEvents, schemas);
            return keys;
        }

        /// <summary>
        /// Load-time semantic validation for emission contracts on entities that are
        /// not region volumes — contact sensor emitters (#1469). Runtime-spawned
        /// emitters skip this and rely on fire-time ValidateFirePayload.
        /// </summary>
        private static void ValidateStandaloneEmissions(
            World world,
            MapSession session,
            CustomEventNameRegistry customEvents,
            EventSchemaRegistry schemas)
        {
            foreach (ref var chunk in world.Query(in StandaloneEmissionQuery))
            {
                var mapEntities = chunk.GetSpan<MapEntity>();
                ref var entityFirst = ref chunk.Entity(0);
                foreach (var index in chunk)
                {
                    if (mapEntities[index].MapId != session.MapId)
                    {
                        continue;
                    }

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    RegionVolumeEmissionCm emission = world.Get<RegionVolumeEmissionCm>(entity);
                    world.Set(entity, ValidateAndCanonicalizeEmission(
                        emission, $"entity {entity.Id}", session.MapId.Value, customEvents, schemas));
                }
            }
        }

        /// <summary>
        /// Shared semantic validation for emission contracts (#1468): used by the
        /// volume bake pass and by the field region emission loader alike.
        /// </summary>
        internal static RegionVolumeEmissionCm ValidateAndCanonicalizeEmission(
            RegionVolumeEmissionCm emission,
            string volumeKey,
            string mapId,
            CustomEventNameRegistry customEvents,
            EventSchemaRegistry schemas)
        {
            EventKey enterKey = ResolveEmissionEvent(emission.EnterEvent, volumeKey, mapId, customEvents, schemas, "enter");
            EventKey exitKey = ResolveEmissionEvent(emission.ExitEvent, volumeKey, mapId, customEvents, schemas, "exit");

            bool enterCustom = IsCustom(enterKey, customEvents);
            bool exitCustom = IsCustom(exitKey, customEvents);
            if (emission.Payload != null && !enterCustom && !exitCustom)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' region volume '{volumeKey}' emission payload requires at least one custom event; engine region events carry the fixed crossing payload.");
            }

            if (emission.Payload != null)
            {
                if (enterCustom)
                {
                    ValidatePayloadAgainstSchema(emission.Payload, enterKey, schemas, volumeKey, mapId);
                }

                if (exitCustom)
                {
                    ValidatePayloadAgainstSchema(emission.Payload, exitKey, schemas, volumeKey, mapId);
                }

                emission.Payload = CanonicalizePayload(emission.Payload, enterCustom ? enterKey : null, exitCustom ? exitKey : null, schemas, volumeKey, mapId);
            }

            emission.EnterEvent = enterKey;
            emission.ExitEvent = exitKey;
            return emission;
        }

        private static EventKey ResolveEmissionEvent(
            EventKey eventKey,
            string volumeKey,
            string mapId,
            CustomEventNameRegistry customEvents,
            EventSchemaRegistry schemas,
            string side)
        {
            if (eventKey.Value == GameEvents.RegionEntered.Value || eventKey.Value == GameEvents.RegionExited.Value)
            {
                return eventKey;
            }

            string eventName = eventKey.Value;
            if (!customEvents.IsDeclaredCustom(eventName))
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' region volume '{volumeKey}' emission {side} event '{eventName}' is neither '{GameEvents.RegionEntered.Value}'/'{GameEvents.RegionExited.Value}' nor a declared custom event; vocabulary: {customEvents.DescribeVocabulary()}.");
            }

            if (!schemas.TryGet(eventName, out EventSchema schema))
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' region volume '{volumeKey}' emission {side} event '{eventName}' has no schema.");
            }

            if (schema.Scope != EventScope.Map)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' region volume '{volumeKey}' emission {side} event '{eventName}' has scope {schema.Scope}; region volumes fire map-scoped events only.");
            }

            return eventKey;
        }

        private static bool IsCustom(EventKey eventKey, CustomEventNameRegistry customEvents)
        {
            return customEvents.IsDeclaredCustom(eventKey.Value);
        }

        private static void ValidatePayloadAgainstSchema(
            RegionVolumePayloadEntry[] payload,
            EventKey eventKey,
            EventSchemaRegistry schemas,
            string volumeKey,
            string mapId)
        {
            schemas.TryGet(eventKey.Value, out EventSchema schema);
            var provided = new Dictionary<string, RegionVolumePayloadEntry>(StringComparer.Ordinal);
            for (int i = 0; i < payload.Length; i++)
            {
                RegionVolumePayloadEntry entry = payload[i];
                if (EventSchemaRegistry.IsReservedPayloadKey(entry.Key))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' region volume '{volumeKey}' emission payload key '{entry.Key}' is reserved; the crossing entity rides MapTrigger.SourceEntity automatically and volume identity belongs in authored keys.");
                }

                if (!schema.DeclaresPayloadKey(entry.Key))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' region volume '{volumeKey}' emission payload key '{entry.Key}' is not declared by event '{eventKey.Value}'.");
                }

                provided[entry.Key] = entry;
            }

            for (int i = 0; i < schema.Params.Count; i++)
            {
                EventParamSchema param = schema.Params[i];
                if (provided.TryGetValue(param.PayloadKey, out RegionVolumePayloadEntry entry))
                {
                    if (!ParamTypeMatches(entry.Type, param.Type))
                    {
                        throw new InvalidOperationException(
                            $"Map '{mapId}' region volume '{volumeKey}' emission payload key '{param.PayloadKey}' carries {entry.Type} but event '{eventKey.Value}' declares {param.Type}.");
                    }
                }
                else if (!param.Optional)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' region volume '{volumeKey}' emission payload is missing required param key '{param.PayloadKey}' of event '{eventKey.Value}'.");
                }
            }
        }

        /// <summary>
        /// Widens authored int literals to float entries when every referencing event
        /// schema declares the key as float; a key referenced as int by one event and
        /// float by the other is ambiguous and rejected.
        /// </summary>
        private static RegionVolumePayloadEntry[] CanonicalizePayload(
            RegionVolumePayloadEntry[] payload,
            EventKey? enterKey,
            EventKey? exitKey,
            EventSchemaRegistry schemas,
            string volumeKey,
            string mapId)
        {
            var result = new RegionVolumePayloadEntry[payload.Length];
            for (int i = 0; i < payload.Length; i++)
            {
                RegionVolumePayloadEntry entry = payload[i];
                if (entry.Type == RegionVolumePayloadValueType.Int)
                {
                    bool referencedFloat = false;
                    bool referencedInt = false;
                    CollectParamType(enterKey, entry.Key, schemas, ref referencedInt, ref referencedFloat);
                    CollectParamType(exitKey, entry.Key, schemas, ref referencedInt, ref referencedFloat);
                    if (referencedInt && referencedFloat)
                    {
                        throw new InvalidOperationException(
                            $"Map '{mapId}' region volume '{volumeKey}' emission payload key '{entry.Key}' is declared int by one emitted event and float by the other; author an explicit float.");
                    }

                    if (referencedFloat)
                    {
                        entry.Type = RegionVolumePayloadValueType.Float;
                        entry.FloatValue = entry.IntValue;
                    }
                }

                result[i] = entry;
            }

            return result;
        }

        private static void CollectParamType(
            EventKey? eventKey,
            string payloadKey,
            EventSchemaRegistry schemas,
            ref bool referencedInt,
            ref bool referencedFloat)
        {
            if (eventKey == null || !schemas.TryGet(eventKey.Value.Value, out EventSchema schema))
            {
                return;
            }

            for (int i = 0; i < schema.Params.Count; i++)
            {
                if (string.Equals(schema.Params[i].PayloadKey, payloadKey, StringComparison.Ordinal))
                {
                    if (schema.Params[i].Type == EventParamType.Float)
                    {
                        referencedFloat = true;
                    }
                    else if (schema.Params[i].Type == EventParamType.Int)
                    {
                        referencedInt = true;
                    }
                }
            }
        }

        private static bool ParamTypeMatches(RegionVolumePayloadValueType authored, EventParamType declared)
        {
            return (authored, declared) switch
            {
                (RegionVolumePayloadValueType.Int, EventParamType.Int) => true,
                (RegionVolumePayloadValueType.Int, EventParamType.Float) => true,
                (RegionVolumePayloadValueType.Float, EventParamType.Float) => true,
                (RegionVolumePayloadValueType.String, EventParamType.String) => true,
                _ => false,
            };
        }
    }
}
