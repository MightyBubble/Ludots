using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.EntityQueries;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
using Ludots.Core.Gameplay.Relationships;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    public sealed class GasGraphRuntimeApi : IGraphRuntimeApi
    {
        private readonly World _world;
        private readonly ISpatialQueryService? _spatialQueries;
        private readonly ISpatialCoordinateConverter? _coords;
        private readonly GameplayEventBus? _eventBus;
        private readonly EffectRequestQueue? _effectRequests;
        private readonly TagOps _tagOps;
        private readonly RelationshipRuntime _relationshipRuntime;
        private readonly RelationshipTypeRegistry _relationshipTypeRegistry;
        private readonly RelationshipMetricRegistry _relationshipMetricRegistry;
        private readonly RelationshipFlagRegistry _relationshipFlagRegistry;
        private readonly RelationshipReasonRegistry _relationshipReasonRegistry;
        private readonly TargetDispatchPresetRegistry _targetDispatchPresets;
        private readonly EntityCollectionStore? _entityCollections;
        private readonly EntitySetQueryRuntime _entityQueries;

        // ── Config context: set before each graph execution, cleared after ──
        private EffectConfigParams _currentConfigParams;
        private bool _hasConfigContext;

        public GasGraphRuntimeApi(
            World world,
            ISpatialQueryService? spatialQueries = null,
            ISpatialCoordinateConverter? coords = null,
            GameplayEventBus? eventBus = null,
            EffectRequestQueue? effectRequests = null,
            TagOps? tagOps = null,
            RelationshipRuntime? relationshipRuntime = null,
            RelationshipTypeRegistry? typeRegistry = null,
            RelationshipMetricRegistry? metricRegistry = null,
            RelationshipFlagRegistry? flagRegistry = null,
            RelationshipReasonRegistry? reasonRegistry = null,
            TargetDispatchPresetRegistry? targetDispatchPresets = null,
            EntityCollectionStore? entityCollections = null,
            EntitySetQueryRuntime? entityQueries = null)
        {
            _world = world;
            _spatialQueries = spatialQueries;
            _coords = coords;
            _eventBus = eventBus;
            _effectRequests = effectRequests;
            _tagOps = tagOps ?? new TagOps();
            _relationshipTypeRegistry = typeRegistry ?? new RelationshipTypeRegistry();
            _relationshipMetricRegistry = metricRegistry ?? new RelationshipMetricRegistry();
            _relationshipFlagRegistry = flagRegistry ?? new RelationshipFlagRegistry();
            _relationshipReasonRegistry = reasonRegistry ?? new RelationshipReasonRegistry();
            _targetDispatchPresets = targetDispatchPresets ?? new TargetDispatchPresetRegistry();
            _relationshipRuntime = relationshipRuntime ?? new RelationshipRuntime(
                world,
                _relationshipTypeRegistry,
                _relationshipMetricRegistry,
                _relationshipFlagRegistry,
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer());
            _entityCollections = entityCollections;
            _entityQueries = entityQueries ?? new EntitySetQueryRuntime(world, _tagOps, _relationshipRuntime);
        }

        /// <summary>
        /// Set the config params context for the current graph execution.
        /// Call this before executing a graph that may use LoadConfig* ops.
        /// </summary>
        public void SetConfigContext(in EffectConfigParams configParams)
        {
            _currentConfigParams = configParams;
            _hasConfigContext = true;
        }

        /// <summary>
        /// Clear the config context after graph execution completes.
        /// </summary>
        public void ClearConfigContext()
        {
            _currentConfigParams = default;
            _hasConfigContext = false;
        }

        public bool TryGetGridPos(Entity entity, out IntVector2 gridPos)
        {
            if (_world.IsAlive(entity) && _world.Has<Position>(entity))
            {
                gridPos = _world.Get<Position>(entity).GridPos;
                return true;
            }

            gridPos = default;
            return false;
        }

        public bool HasTag(Entity entity, int tagId)
        {
            if (!_world.IsAlive(entity) || !_world.Has<GameplayTagContainer>(entity)) return false;
            ref var tags = ref _world.Get<GameplayTagContainer>(entity);
            return _tagOps.HasTag(ref tags, tagId, TagSense.Effective);
        }

        public bool TryGetAttributeCurrent(Entity entity, int attributeId, out float value)
        {
            if (_world.IsAlive(entity) && _world.Has<AttributeBuffer>(entity))
            {
                value = _world.Get<AttributeBuffer>(entity).GetCurrent(attributeId);
                return true;
            }

            value = 0f;
            return false;
        }

        public int QueryRadius(IntVector2 center, float radius, Span<Entity> buffer)
        {
            if (_spatialQueries == null)
            {
                throw new System.InvalidOperationException("GAS.GRAPH.ERR.MissingSpatialQueryService");
            }
            if (_coords == null)
            {
                throw new System.InvalidOperationException("GAS.GRAPH.ERR.MissingSpatialCoordinateConverter");
            }
            WorldCmInt2 worldCenter = _coords.GridToWorld(center);
            int radiusCm = radius >= 0f
                ? (int)(radius * _coords!.GridCellSizeCm + 0.5f)
                : -(int)(-radius * _coords!.GridCellSizeCm + 0.5f);
            return _spatialQueries.QueryRadius(worldCenter, radiusCm, buffer).Count;
        }

        public int QueryCone(IntVector2 origin, int directionDeg, int halfAngleDeg, float rangeCm, Span<Entity> buffer)
        {
            if (_spatialQueries == null) throw new System.InvalidOperationException("GAS.GRAPH.ERR.MissingSpatialQueryService");
            if (_coords == null) throw new System.InvalidOperationException("GAS.GRAPH.ERR.MissingSpatialCoordinateConverter");
            WorldCmInt2 worldOrigin = _coords.GridToWorld(origin);
            int rCm = (int)(rangeCm * _coords.GridCellSizeCm + 0.5f);
            return _spatialQueries.QueryCone(worldOrigin, directionDeg, halfAngleDeg, rCm, buffer).Count;
        }

        public int QueryRectangle(IntVector2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer)
        {
            if (_spatialQueries == null) throw new System.InvalidOperationException("GAS.GRAPH.ERR.MissingSpatialQueryService");
            if (_coords == null) throw new System.InvalidOperationException("GAS.GRAPH.ERR.MissingSpatialCoordinateConverter");
            WorldCmInt2 worldCenter = _coords.GridToWorld(center);
            return _spatialQueries.QueryRectangle(worldCenter, halfWidthCm, halfHeightCm, rotationDeg, buffer).Count;
        }

        public int QueryLine(IntVector2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer)
        {
            if (_spatialQueries == null) throw new System.InvalidOperationException("GAS.GRAPH.ERR.MissingSpatialQueryService");
            if (_coords == null) throw new System.InvalidOperationException("GAS.GRAPH.ERR.MissingSpatialCoordinateConverter");
            WorldCmInt2 worldOrigin = _coords.GridToWorld(origin);
            return _spatialQueries.QueryLine(worldOrigin, directionDeg, lengthCm, halfWidthCm, buffer).Count;
        }

        public int CollectMapEntities(Span<Entity> buffer)
        {
            return _entityQueries.CollectMapEntities(buffer);
        }

        public int CopyEntityCollection(Entity owner, int collectionKeyId, Span<Entity> buffer)
        {
            if (_entityCollections == null)
            {
                throw new InvalidOperationException("GAS.GRAPH.ERR.MissingEntityCollectionStore");
            }

            if (collectionKeyId <= 0)
            {
                throw new InvalidOperationException($"Graph references unknown entity collection key id {collectionKeyId}.");
            }

            return _entityQueries.CopyCollection(_entityCollections, owner, collectionKeyId, buffer);
        }

        public int FilterTeam(Span<Entity> entities, int count, int teamId)
        {
            return _entityQueries.FilterTeam(entities, count, teamId);
        }

        public int FilterTeamRelationship(Span<Entity> entities, int count, Entity reference, RelationshipFilter filter)
        {
            return _entityQueries.FilterTeamRelationship(entities, count, reference, filter);
        }

        public int FilterTemplate(Span<Entity> entities, int count, int templateKeyId)
        {
            return _entityQueries.FilterTemplate(entities, count, templateKeyId);
        }

        public int FilterAttributeRange(Span<Entity> entities, int count, int attributeId, float minInclusive, float maxInclusive)
        {
            return _entityQueries.FilterAttributeRange(entities, count, attributeId, minInclusive, maxInclusive);
        }

        public int FilterTagAny(Span<Entity> entities, int count, int tagId)
        {
            return _entityQueries.FilterTagAny(entities, count, tagId);
        }

        public int FilterTagNone(Span<Entity> entities, int count, int tagId)
        {
            return _entityQueries.FilterTagNone(entities, count, tagId);
        }

        public int FilterLayer(Span<Entity> entities, int count, uint requiredMask)
        {
            return _entityQueries.FilterLayer(entities, count, requiredMask);
        }

        public int FilterNotEntity(Span<Entity> entities, int count, Entity exclude)
        {
            return _entityQueries.FilterNotEntity(entities, count, exclude);
        }

        public int SortStableDedup(Span<Entity> entities, int count)
        {
            return _entityQueries.SortStableDedup(entities, count);
        }

        public int Limit(Span<Entity> entities, int count, int limit)
        {
            return _entityQueries.Limit(entities, count, limit);
        }

        public void SortByAttribute(Span<Entity> entities, int count, int attributeId, bool descending)
        {
            _entityQueries.SortByAttribute(entities, count, attributeId, descending);
        }

        public float SumAttribute(ReadOnlySpan<Entity> entities, int attributeId)
        {
            return _entityQueries.SumAttribute(entities, attributeId);
        }

        public float AverageAttribute(ReadOnlySpan<Entity> entities, int attributeId)
        {
            return _entityQueries.AverageAttribute(entities, attributeId);
        }

        public float MaxAttribute(ReadOnlySpan<Entity> entities, int attributeId)
        {
            return _entityQueries.MaxAttribute(entities, attributeId);
        }

        public float MinAttribute(ReadOnlySpan<Entity> entities, int attributeId)
        {
            return _entityQueries.MinAttribute(entities, attributeId);
        }

        public bool TryMaxEntityByAttribute(ReadOnlySpan<Entity> entities, int attributeId, out Entity entity, out float value)
        {
            return _entityQueries.TryMaxEntityByAttribute(entities, attributeId, out entity, out value);
        }

        public bool TryMinEntityByAttribute(ReadOnlySpan<Entity> entities, int attributeId, out Entity entity, out float value)
        {
            return _entityQueries.TryMinEntityByAttribute(entities, attributeId, out entity, out value);
        }

        public bool TryMinEntityByDistance(ReadOnlySpan<Entity> entities, IntVector2 center, out Entity entity, out long distanceSquared)
        {
            return _entityQueries.TryMinEntityByDistance(entities, center, out entity, out distanceSquared);
        }

        public int GetTeamId(Entity entity)
        {
            if (_world.IsAlive(entity) && _world.Has<Team>(entity))
                return _world.Get<Team>(entity).Id;
            return 0;
        }

        public uint GetEntityLayerCategory(Entity entity)
        {
            if (_world.IsAlive(entity) && _world.Has<EntityLayer>(entity))
                return _world.Get<EntityLayer>(entity).Value.Category;
            return 0;
        }

        public int GetRelationship(int teamA, int teamB)
        {
            return (int)TeamManager.GetRelationship(teamA, teamB);
        }
        public void EnsureRelationshipLink(Entity source, Entity target, int typeId) => _relationshipRuntime.EnsureLink(source, target, typeId);
        public void RemoveRelationshipLink(Entity source, Entity target, int typeId) => _relationshipRuntime.RemoveLink(source, target, typeId);
        public short SetRelationshipMetric(Entity source, Entity target, int metricId, int value, int reasonId, int typeId)
            => _relationshipRuntime.SetMetric(source, target, typeId, metricId, value, reasonId);
        public short AddRelationshipMetric(Entity source, Entity target, int metricId, int delta, int reasonId, int typeId)
            => _relationshipRuntime.AddMetric(source, target, typeId, metricId, delta, reasonId);
        public short GetRelationshipMetric(Entity source, Entity target, int metricId, int typeId)
            => _relationshipRuntime.GetMetric(source, target, typeId, metricId);
        public bool HasRelationshipFlag(Entity source, Entity target, int flagId, int typeId)
            => _relationshipRuntime.HasFlag(source, target, typeId, flagId);
        public void SetRelationshipFlag(Entity source, Entity target, int flagId, bool enabled, int reasonId, int typeId)
            => _relationshipRuntime.SetFlag(source, target, typeId, flagId, enabled, reasonId);
        public int CollectOutgoing(Entity source, Span<Entity> buffer, int typeId = RelationshipTypeRegistry.AnyTypeId)
            => _relationshipRuntime.CollectOutgoing(source, typeId, buffer);
        public int CollectIncoming(Entity target, Span<Entity> buffer, int typeId = RelationshipTypeRegistry.AnyTypeId)
            => _relationshipRuntime.CollectIncoming(target, typeId, buffer);
        public int CollectMutual(Entity first, Entity second, Span<Entity> buffer, int typeId = RelationshipTypeRegistry.AnyTypeId)
            => _relationshipRuntime.CollectMutual(first, second, typeId, buffer);
        public int CollectBetweenPair(Entity source, Entity target, Span<Entity> buffer, int typeId = RelationshipTypeRegistry.AnyTypeId)
            => _relationshipRuntime.CollectBetweenPair(source, target, typeId, buffer);
        public int FilterRelationshipMetricRange(Span<Entity> entities, int count, Entity source, int typeId, int metricId, short minInclusive, short maxInclusive)
            => _entityQueries.FilterRelationshipMetricRange(entities, count, source, typeId, metricId, minInclusive, maxInclusive);
        public int FilterRelationshipFlag(Span<Entity> entities, int count, Entity source, int typeId, int flagId, bool expected)
            => _entityQueries.FilterRelationshipFlag(entities, count, source, typeId, flagId, expected);
        public void SortByRelationshipMetric(Span<Entity> entities, int count, Entity source, int typeId, int metricId, bool descending)
            => _entityQueries.SortByRelationshipMetric(entities, count, source, typeId, metricId, descending);
        public int SumRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId)
            => _entityQueries.SumRelationshipMetric(entities, source, typeId, metricId);
        public int AverageRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId)
            => _entityQueries.AverageRelationshipMetric(entities, source, typeId, metricId);
        public int MaxRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId)
            => _entityQueries.MaxRelationshipMetric(entities, source, typeId, metricId);
        public int MinRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId)
            => _entityQueries.MinRelationshipMetric(entities, source, typeId, metricId);
        public bool TryMaxEntityByRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId, out Entity entity, out int value)
            => _entityQueries.TryMaxEntityByRelationshipMetric(entities, source, typeId, metricId, out entity, out value);
        public bool TryMinEntityByRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId, out Entity entity, out int value)
            => _entityQueries.TryMinEntityByRelationshipMetric(entities, source, typeId, metricId, out entity, out value);

        public void ApplyEffectTemplate(Entity caster, Entity target, int templateId)
        {
            var none = EffectArgs.None;
            ApplyEffectTemplate(caster, target, templateId, in none);
        }

        public void ApplyEffectTemplate(Entity caster, Entity target, int templateId, in EffectArgs args)
        {
            if (_effectRequests == null)
            {
                throw new System.InvalidOperationException("GAS.GRAPH.ERR.MissingEffectRequestQueue");
            }

            // Convert EffectArgs to CallerParams
            var req = new Ludots.Core.Gameplay.GAS.EffectRequest
            {
                Source = caster,
                Target = target,
                TargetContext = default,
                TemplateId = templateId,
            };

            if (args.FloatCount > 0)
            {
                req.HasCallerParams = true;
                // F0/F1 mapped to positional keys used by graph programs.
                req.CallerParams.TryAddFloat(
                    Ludots.Core.Gameplay.GAS.EffectParamKeys.ForceXAttribute, args.F0);
                if (args.FloatCount > 1)
                {
                    req.CallerParams.TryAddFloat(
                        Ludots.Core.Gameplay.GAS.EffectParamKeys.ForceYAttribute, args.F1);
                }
            }

            _effectRequests.Publish(req);
        }

        public void FanOutDispatchEffect(Entity source, Entity target, Entity targetContext, ReadOnlySpan<Entity> targets, int templateId, int payloadPresetId)
        {
            if (_effectRequests == null)
            {
                throw new InvalidOperationException("GAS.GRAPH.ERR.MissingEffectRequestQueue");
            }

            if (templateId <= 0)
            {
                return;
            }

            TargetResolverContextMapping mapping = _targetDispatchPresets.Get(payloadPresetId);
            TargetResolverFanOutHelper.PublishResolvedTargets(
                rootId: 0,
                source,
                target,
                targetContext,
                targets,
                templateId,
                in mapping,
                _effectRequests);
        }

        public void RemoveEffectTemplate(Entity target, int templateId)
        {
            if (!_world.IsAlive(target) || templateId <= 0 || !_world.Has<ActiveEffectContainer>(target))
            {
                return;
            }

            ref var container = ref _world.Get<ActiveEffectContainer>(target);
            for (int i = 0; i < container.Count; i++)
            {
                Entity effectEntity = container.GetEntity(i);
                if (!_world.IsAlive(effectEntity) ||
                    !_world.Has<EffectTemplateRef>(effectEntity) ||
                    !_world.Has<GameplayEffect>(effectEntity))
                {
                    continue;
                }

                if (_world.Get<EffectTemplateRef>(effectEntity).TemplateId != templateId)
                {
                    continue;
                }

                ref var gameplayEffect = ref _world.Get<GameplayEffect>(effectEntity);
                gameplayEffect.CancelRequested = true;
            }
        }

        public void ModifyAttributeAdd(Entity caster, Entity target, int attributeId, float delta)
        {
            AttributeMutationOps.AddCurrent(_world, target, attributeId, delta);
        }

        public void SendEvent(Entity caster, Entity target, int eventTagId, float magnitude)
        {
            if (_eventBus == null)
            {
                throw new System.InvalidOperationException("GAS.GRAPH.ERR.MissingGameplayEventBus");
            }
            _eventBus.Publish(new GameplayEvent
            {
                TagId = eventTagId,
                Source = caster,
                Target = target,
                Magnitude = magnitude
            });
        }

        // ── Hex spatial queries ──

        public int QueryHexRange(IntVector2 center, int hexRadius, Span<Entity> buffer)
        {
            if (_spatialQueries == null) throw new InvalidOperationException("GAS.GRAPH.ERR.MissingSpatialQueryService");
            if (_coords == null) throw new InvalidOperationException("GAS.GRAPH.ERR.MissingSpatialCoordinateConverter");
            var hexCenter = _coords.WorldToHex(_coords.GridToWorld(center));
            return _spatialQueries.QueryHexRange(hexCenter, hexRadius, buffer).Count;
        }

        public int QueryHexRing(IntVector2 center, int hexRadius, Span<Entity> buffer)
        {
            if (_spatialQueries == null) throw new InvalidOperationException("GAS.GRAPH.ERR.MissingSpatialQueryService");
            if (_coords == null) throw new InvalidOperationException("GAS.GRAPH.ERR.MissingSpatialCoordinateConverter");
            var hexCenter = _coords.WorldToHex(_coords.GridToWorld(center));
            return _spatialQueries.QueryHexRing(hexCenter, hexRadius, buffer).Count;
        }

        public int QueryHexNeighbors(IntVector2 center, Span<Entity> buffer)
        {
            // Neighbors = Ring(1)
            return QueryHexRing(center, 1, buffer);
        }

        // ── Blackboard immediate read/write ──

        public bool TryReadBlackboardFloat(Entity entity, int keyId, out float value)
        {
            value = 0f;
            if (!_world.IsAlive(entity) || !_world.Has<BlackboardFloatBuffer>(entity)) return false;
            ref var bb = ref _world.Get<BlackboardFloatBuffer>(entity);
            return bb.TryGet(keyId, out value);
        }

        public bool TryReadBlackboardInt(Entity entity, int keyId, out int value)
        {
            value = 0;
            if (!_world.IsAlive(entity) || !_world.Has<BlackboardIntBuffer>(entity)) return false;
            ref var bb = ref _world.Get<BlackboardIntBuffer>(entity);
            return bb.TryGet(keyId, out value);
        }

        public bool TryReadBlackboardEntity(Entity entity, int keyId, out Entity value)
        {
            value = default;
            if (!_world.IsAlive(entity) || !_world.Has<BlackboardEntityBuffer>(entity)) return false;
            ref var bb = ref _world.Get<BlackboardEntityBuffer>(entity);
            return bb.TryGet(keyId, out value);
        }

        public void WriteBlackboardFloat(Entity entity, int keyId, float value)
        {
            if (!_world.IsAlive(entity)) return;
            if (!_world.Has<BlackboardFloatBuffer>(entity)) return; // Component must be pre-added at entity template creation
            ref var bb = ref _world.Get<BlackboardFloatBuffer>(entity);
            bb.Set(keyId, value);
        }

        public void WriteBlackboardInt(Entity entity, int keyId, int value)
        {
            if (!_world.IsAlive(entity)) return;
            if (!_world.Has<BlackboardIntBuffer>(entity)) return; // Component must be pre-added at entity template creation
            ref var bb = ref _world.Get<BlackboardIntBuffer>(entity);
            bb.Set(keyId, value);
        }

        public void WriteBlackboardEntity(Entity entity, int keyId, Entity value)
        {
            if (!_world.IsAlive(entity)) return;
            if (!_world.Has<BlackboardEntityBuffer>(entity)) return; // Component must be pre-added at entity template creation
            ref var bb = ref _world.Get<BlackboardEntityBuffer>(entity);
            bb.Set(keyId, value);
        }

        // ── Config parameter reading ──

        public bool TryLoadConfigFloat(int keyId, out float value)
        {
            value = 0f;
            if (!_hasConfigContext) return false;
            return _currentConfigParams.TryGetFloat(keyId, out value);
        }

        public bool TryLoadConfigInt(int keyId, out int value)
        {
            value = 0;
            if (!_hasConfigContext) return false;
            return _currentConfigParams.TryGetInt(keyId, out value);
        }
    }
}
