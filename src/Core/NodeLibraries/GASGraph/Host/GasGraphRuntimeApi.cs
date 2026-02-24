using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;

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

        // ── Config context: set before each graph execution, cleared after ──
        private EffectConfigParams _currentConfigParams;
        private bool _hasConfigContext;

        public GasGraphRuntimeApi(World world, ISpatialQueryService? spatialQueries, ISpatialCoordinateConverter? coords, GameplayEventBus? eventBus, EffectRequestQueue? effectRequests = null, TagOps? tagOps = null)
        {
            _world = world;
            _spatialQueries = spatialQueries;
            _coords = coords;
            _eventBus = eventBus;
            _effectRequests = effectRequests;
            _tagOps = tagOps ?? new TagOps();
        }

        public GasGraphRuntimeApi(World world, ISpatialQueryService? spatialQueries, GameplayEventBus? eventBus, EffectRequestQueue? effectRequests = null)
            : this(world, spatialQueries, coords: null, eventBus, effectRequests)
        {
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

            // Convert legacy EffectArgs to CallerParams
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
                // Legacy: F0/F1 mapped to positional keys for backward compat with existing graph programs.
                // New graph programs should use LoadConfig* + ApplyEffectDynamic instead.
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

        public void ModifyAttributeAdd(Entity caster, Entity target, int attributeId, float delta)
        {
            if (!_world.IsAlive(target) || !_world.Has<AttributeBuffer>(target)) return;
            ref var attr = ref _world.Get<AttributeBuffer>(target);
            float current = attr.GetCurrent(attributeId);
            attr.SetCurrent(attributeId, current + delta);
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
