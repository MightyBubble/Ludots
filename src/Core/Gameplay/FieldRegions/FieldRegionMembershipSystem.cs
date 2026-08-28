using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Fields;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.FieldRegions
{
    /// <summary>
    /// Differential membership maintenance for discrete-id field layers. Tracked entities
    /// (MapEntity + WorldPositionCm + FieldTrackedCm) are observed every tick, but only
    /// cell changes do work: ownership transitions move the entity between per-region
    /// rosters, fire FieldRegionExited/FieldRegionEntered on the map's trigger line, and
    /// project changed rosters into the EntityCollectionStore. Destroyed entities are
    /// removed from rosters silently (death is not a crossing).
    /// </summary>
    public sealed class FieldRegionMembershipSystem : BaseSystem<World, float>
    {
        private readonly Func<MapSessionManager?> _sessions;
        private readonly EntityCollectionStore _collections;
        private readonly TriggerManager _triggerManager;
        private readonly Func<ScriptContext> _contextFactory;
        private readonly CommandBuffer _commandBuffer = new();
        private static readonly List<string> EmptyTags = new();

        private readonly QueryDescription _adoptionQuery = new QueryDescription()
            .WithAll<MapEntity, WorldPositionCm, FieldTrackedCm>()
            .WithNone<RegionMembershipCm, SuspendedTag, PresentationDestroyPending>();
        private readonly QueryDescription _trackedQuery = new QueryDescription()
            .WithAll<MapEntity, WorldPositionCm, FieldTrackedCm, RegionMembershipCm>()
            .WithNone<SuspendedTag, PresentationDestroyPending>();

        private readonly Dictionary<MapId, Dictionary<long, HashSet<Entity>>> _rostersByMap = new();
        private readonly Dictionary<MapId, HashSet<long>> _dirtyRostersByMap = new();

        public FieldRegionMembershipSystem(
            World world,
            Func<MapSessionManager?> sessions,
            EntityCollectionStore collections,
            TriggerManager triggerManager,
            Func<ScriptContext> contextFactory)
            : base(world)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _collections = collections ?? throw new ArgumentNullException(nameof(collections));
            _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            World.SubscribeEntityDestroyed(OnEntityDestroyed);
        }

        public override void Update(in float dt)
        {
            AdoptNewTrackedEntities();
            MaintainMembership();
            FlushDirtyRosters();
        }

        public bool TryGetRosterMemberCount(MapId mapId, FieldLayerId layerId, int regionId, out int count)
        {
            count = 0;
            if (!_rostersByMap.TryGetValue(mapId, out Dictionary<long, HashSet<Entity>>? rosters))
            {
                return false;
            }

            if (!rosters.TryGetValue(RegionEntityIndex.Pack(layerId, regionId), out HashSet<Entity>? members))
            {
                return false;
            }

            count = members.Count;
            return true;
        }

        private void AdoptNewTrackedEntities()
        {
            foreach (ref var chunk in World.Query(in _adoptionQuery))
            {
                ref var entityFirst = ref chunk.Entity(0);
                foreach (var index in chunk)
                {
                    var entity = Unsafe.Add(ref entityFirst, index);
                    _commandBuffer.Add(entity, new RegionMembershipCm { Initialized = 0 });
                }
            }

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        private void MaintainMembership()
        {
            foreach (ref var chunk in World.Query(in _trackedQuery))
            {
                ref var entityFirst = ref chunk.Entity(0);
                var mapEntities = chunk.GetSpan<MapEntity>();
                var positions = chunk.GetSpan<WorldPositionCm>();
                var tracked = chunk.GetSpan<FieldTrackedCm>();
                var memberships = chunk.GetSpan<RegionMembershipCm>();

                foreach (var index in chunk)
                {
                    var entity = Unsafe.Add(ref entityFirst, index);
                    MapId mapId = mapEntities[index].MapId;
                    MapSession? session = _sessions()?.GetSession(mapId);
                    if (session?.Fields == null)
                    {
                        continue;
                    }

                    FieldLayerId layerId = tracked[index].LayerId;
                    if (session.Fields.TryGet(layerId, out FieldLayerData layerData) &&
                        layerData is DiscreteIdFieldLayerData layer)
                    {
                        UpdateMembership(entity, mapId, session, layer, ref positions[index], ref memberships[index]);
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Entity {entity.Id} is field-tracked on layer id {layerId.Value} which is not a discrete-id layer of map '{mapId.Value}'.");
                    }
                }
            }
        }

        private void UpdateMembership(
            Entity entity,
            MapId mapId,
            MapSession session,
            DiscreteIdFieldLayerData layer,
            ref WorldPositionCm position,
            ref RegionMembershipCm membership)
        {
            var cell = layer.Field.WorldToCell(position.Value.ToWorldCmInt2());
            long chunkStamp = layer.Field.GetChangeStamp(cell);
            if (membership.Initialized != 0 && membership.LayerId == layer.LayerId.Value &&
                membership.LastCellX == cell.X && membership.LastCellY == cell.Y &&
                membership.LastChunkStamp == chunkStamp)
            {
                return;
            }

            membership.LastChunkStamp = chunkStamp;
            int newRegionId = layer.Field.Get(cell);
            membership.LayerId = layer.LayerId.Value;
            membership.LastCellX = cell.X;
            membership.LastCellY = cell.Y;

            if (membership.Initialized == 0)
            {
                membership.Initialized = 1;
                membership.RegionId = newRegionId;
                AddToRoster(mapId, layer.LayerId, newRegionId, entity);
                FireRegionEvent(session, GameEvents.FieldRegionEntered, entity, layer, newRegionId);
                return;
            }

            if (newRegionId == membership.RegionId)
            {
                return;
            }

            int oldRegionId = membership.RegionId;
            membership.RegionId = newRegionId;
            RemoveFromRoster(mapId, layer.LayerId, oldRegionId, entity);
            if (newRegionId != 0)
            {
                AddToRoster(mapId, layer.LayerId, newRegionId, entity);
            }

            FireRegionEvent(session, GameEvents.FieldRegionExited, entity, layer, oldRegionId);
            if (newRegionId != 0)
            {
                FireRegionEvent(session, GameEvents.FieldRegionEntered, entity, layer, newRegionId);
            }
        }

        private void AddToRoster(MapId mapId, FieldLayerId layerId, int regionId, Entity entity)
        {
            if (regionId == 0)
            {
                return;
            }

            Roster(mapId, layerId, regionId).Add(entity);
            MarkDirty(mapId, layerId, regionId);
        }

        private void RemoveFromRoster(MapId mapId, FieldLayerId layerId, int regionId, Entity entity)
        {
            if (regionId == 0)
            {
                return;
            }

            if (Roster(mapId, layerId, regionId).Remove(entity))
            {
                MarkDirty(mapId, layerId, regionId);
            }
        }

        private HashSet<Entity> Roster(MapId mapId, FieldLayerId layerId, int regionId)
        {
            if (!_rostersByMap.TryGetValue(mapId, out Dictionary<long, HashSet<Entity>>? rosters))
            {
                rosters = new Dictionary<long, HashSet<Entity>>();
                _rostersByMap.Add(mapId, rosters);
            }

            long key = RegionEntityIndex.Pack(layerId, regionId);
            if (!rosters.TryGetValue(key, out HashSet<Entity>? members))
            {
                members = new HashSet<Entity>();
                rosters.Add(key, members);
            }

            return members;
        }

        private void MarkDirty(MapId mapId, FieldLayerId layerId, int regionId)
        {
            if (!_dirtyRostersByMap.TryGetValue(mapId, out HashSet<long>? dirty))
            {
                dirty = new HashSet<long>();
                _dirtyRostersByMap.Add(mapId, dirty);
            }

            dirty.Add(RegionEntityIndex.Pack(layerId, regionId));
        }

        private void FlushDirtyRosters()
        {
            foreach (KeyValuePair<MapId, HashSet<long>> mapDirty in _dirtyRostersByMap)
            {
                MapSession? session = _sessions()?.GetSession(mapDirty.Key);
                if (session?.Fields == null)
                {
                    continue;
                }

                foreach (long rosterKey in mapDirty.Value)
                {
                    FlushRoster(session, rosterKey);
                }

                mapDirty.Value.Clear();
            }
        }

        private void FlushRoster(MapSession session, long rosterKey)
        {
            int layerIdValue = (int)(rosterKey >> 32);
            int regionId = (int)(rosterKey & 0xFFFFFFFF);
            if (!session.Fields!.TryGet(new FieldLayerId(layerIdValue), out FieldLayerData layerData) ||
                layerData is not DiscreteIdFieldLayerData layer)
            {
                return;
            }

            if (session.RegionIndex == null ||
                !session.RegionIndex.TryResolve(layer.LayerId, regionId, out Entity regionEntity))
            {
                return;
            }

            HashSet<Entity> members = Roster(session.MapId, layer.LayerId, regionId);
            var entities = new Entity[members.Count];
            members.CopyTo(entities);
            var descriptor = EntityCollectionDescriptor.Create(
                $"collection.field.{layer.LayerKey}.members",
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.Display,
                regionEntity,
                title: layer.Regions.GetName(regionId),
                summary: layer.LayerKey);
            _collections.Replace(regionEntity, descriptor, entities);
        }

        private void OnEntityDestroyed(in Entity entity)
        {
            foreach (KeyValuePair<MapId, Dictionary<long, HashSet<Entity>>> mapRosters in _rostersByMap)
            {
                foreach (KeyValuePair<long, HashSet<Entity>> roster in mapRosters.Value)
                {
                    if (roster.Value.Remove(entity))
                    {
                        MarkDirty(mapRosters.Key, new FieldLayerId((int)(roster.Key >> 32)), (int)(roster.Key & 0xFFFFFFFF));
                    }
                }
            }
        }

        private void FireRegionEvent(
            MapSession session, EventKey eventKey, Entity entity, DiscreteIdFieldLayerData layer, int regionId)
        {
            ScriptContext context = _contextFactory();
            context.Set(CoreServiceKeys.MapId, session.MapId);
            context.Set(CoreServiceKeys.MapSession, session);
            context.Set(CoreServiceKeys.MapTags, session.MapConfig?.Tags ?? EmptyTags);
            context.Set(MapTriggerEventPayloadKeys.SourceEntity, entity);
            context.Set(MapTriggerEventPayloadKeys.RegionId, layer.Regions.GetName(regionId));
            context.Set(MapTriggerEventPayloadKeys.FieldLayer, layer.LayerKey);
            _triggerManager.FireMapEvent(session.MapId, eventKey, context);
        }
    }
}
