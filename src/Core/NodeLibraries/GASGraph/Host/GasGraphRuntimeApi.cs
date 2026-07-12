using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.EntityQueries;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Knowledge;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Placement;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.GraphQuery;
using Ludots.Core.Navigation.GraphWorld;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    public sealed class GasGraphRuntimeApi : IGraphRuntimeApi
    {
        private readonly World _world;
        private readonly ISpatialQueryService? _spatialQueries;
        private readonly ISpatialCoordinateConverter? _coords;
        private readonly GameplayEventBus? _eventBus;
        private readonly EffectRequestQueue? _effectRequests;
        private readonly TagOps? _tagOps;
        private readonly RelationshipRuntime? _relationshipRuntime;
        private readonly TargetDispatchPresetRegistry? _targetDispatchPresets;
        private readonly EntityCollectionStore? _entityCollections;
        private readonly EntitySetQueryRuntime? _entityQueries;
        private LoadedGraphRuntime? _loadedGraphRuntime;

        // ── Topology predicate services (RFC-0065 PROV-4b), bound post-construction ──
        private ControlDomainQuery? _controlDomains;
        private KnowledgeProjectionResolver? _knowledgeProjections;
        private IClock? _clock;
        private int[] _graphProjectionCandidateScratch = Array.Empty<int>();

        // ── Config context: set before each graph execution, cleared after ──
        private EffectConfigParams _currentConfigParams;
        private bool _hasConfigContext;

        // ── Builtin invocation context for lifecycle graph composition ──
        private BuiltinHandlerRegistry? _builtinHandlers;
        private EffectTemplateRegistry? _effectTemplates;
        private BuiltinHandlerExecutionContext? _builtinRuntime;
        private int _currentEffectTemplateId;
        private EffectContext _currentEffectContext;
        private bool _hasEffectContext;

        public static GasGraphRuntimeApi CreateProduction(
            World world,
            ISpatialQueryService? spatialQueries,
            ISpatialCoordinateConverter? coords,
            GameplayEventBus? eventBus,
            EffectRequestQueue? effectRequests,
            IReadOnlyDictionary<string, object> services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            var api = new GasGraphRuntimeApi(
                world,
                spatialQueries,
                coords,
                eventBus,
                effectRequests,
                RequireService(services, CoreServiceKeys.TagOps),
                RequireService(services, CoreServiceKeys.RelationshipRuntime),
                RequireService(services, CoreServiceKeys.RelationshipTypeRegistry),
                RequireService(services, CoreServiceKeys.RelationshipMetricRegistry),
                RequireService(services, CoreServiceKeys.RelationshipFlagRegistry),
                RequireService(services, CoreServiceKeys.RelationshipReasonRegistry),
                RequireService(services, CoreServiceKeys.TargetDispatchPresetRegistry),
                RequireService(services, CoreServiceKeys.EntityCollectionStore),
                RequireService(services, CoreServiceKeys.EntitySetQueryRuntime));
            api.BindTopologyServices(
                OptionalService(services, CoreServiceKeys.ControlDomainQuery),
                OptionalService(services, CoreServiceKeys.KnowledgeProjectionResolver),
                OptionalService(services, CoreServiceKeys.Clock));
            return api;
        }

        private static T? OptionalService<T>(IReadOnlyDictionary<string, object> services, ServiceKey<T> key)
            where T : class
        {
            return services.TryGetValue(key.Name, out object? value) && value is T typed ? typed : null;
        }

        private static T RequireService<T>(IReadOnlyDictionary<string, object> services, ServiceKey<T> key)
        {
            if (!services.TryGetValue(key.Name, out object? value) || value is not T typed)
            {
                throw new InvalidOperationException($"Production GasGraphRuntimeApi requires engine-owned service `{key.Name}`.");
            }

            return typed;
        }

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
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _spatialQueries = spatialQueries;
            _coords = coords;
            _eventBus = eventBus;
            _effectRequests = effectRequests;
            _tagOps = tagOps;
            _targetDispatchPresets = targetDispatchPresets;
            _relationshipRuntime = relationshipRuntime;
            _entityCollections = entityCollections;
            _entityQueries = entityQueries;
            _ = typeRegistry;
            _ = metricRegistry;
            _ = flagRegistry;
            _ = reasonRegistry;
        }

        private TagOps RequireTagOps()
        {
            return _tagOps ?? throw new InvalidOperationException("GAS.GRAPH.ERR.MissingTagOps");
        }

        private RelationshipRuntime RequireRelationshipRuntime()
        {
            return _relationshipRuntime ?? throw new InvalidOperationException("GAS.GRAPH.ERR.MissingRelationshipRuntime");
        }

        private TargetDispatchPresetRegistry RequireTargetDispatchPresets()
        {
            return _targetDispatchPresets ?? throw new InvalidOperationException("GAS.GRAPH.ERR.MissingTargetDispatchPresetRegistry");
        }

        private EntitySetQueryRuntime RequireEntityQueries()
        {
            return _entityQueries ?? throw new InvalidOperationException("GAS.GRAPH.ERR.MissingEntitySetQueryRuntime");
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

        public void BindLoadedGraphRuntime(LoadedGraphRuntime? runtime)
        {
            _loadedGraphRuntime = runtime;
        }

        /// <summary>
        /// Binds the topology predicate services consumed by the ControlDomain*/Knowledge* graph ops
        /// (RFC-0065 PROV-4b). The clock supplies the Step-domain tick for knowledge projection expiry.
        /// </summary>
        public void BindTopologyServices(
            ControlDomainQuery? controlDomains,
            KnowledgeProjectionResolver? knowledgeProjections,
            IClock? clock)
        {
            _controlDomains = controlDomains;
            _knowledgeProjections = knowledgeProjections;
            _clock = clock;
        }

        public void BeginBuiltinInvocation(
            BuiltinHandlerRegistry builtinHandlers,
            EffectTemplateRegistry effectTemplates,
            BuiltinHandlerExecutionContext? builtinRuntime,
            int effectTemplateId,
            in EffectContext effectContext,
            in EffectConfigParams mergedParams)
        {
            _builtinHandlers = builtinHandlers ?? throw new ArgumentNullException(nameof(builtinHandlers));
            _effectTemplates = effectTemplates ?? throw new ArgumentNullException(nameof(effectTemplates));
            _builtinRuntime = builtinRuntime;
            _currentEffectTemplateId = effectTemplateId;
            _currentEffectContext = effectContext;
            _hasEffectContext = true;
            SetConfigContext(in mergedParams);
        }

        public void EndBuiltinInvocation()
        {
            if (_builtinRuntime?.LifecycleTransaction != null)
            {
                _builtinRuntime.LifecycleTransaction = null;
            }

            _builtinHandlers = null;
            _effectTemplates = null;
            _builtinRuntime = null;
            _currentEffectTemplateId = 0;
            _currentEffectContext = default;
            _hasEffectContext = false;
            ClearConfigContext();
        }

        public void BeginLifecycleTransaction()
        {
            var runtime = RequireBuiltinRuntime();
            var services = runtime.LifecycleServices
                ?? throw new InvalidOperationException("BeginLifecycleTransaction requires LifecycleServices on BuiltinHandlerExecutionContext.");

            if (runtime.LifecycleTransaction != null)
            {
                throw new InvalidOperationException("BeginLifecycleTransaction cannot nest an active lifecycle transaction.");
            }

            if (!_hasEffectContext)
            {
                throw new InvalidOperationException("BeginLifecycleTransaction requires an active effect context.");
            }

            Entity source = _currentEffectContext.Source;
            if (!_world.IsAlive(source))
            {
                throw new LifecycleExecutionException("Entity lifecycle transaction failed because the source entity is no longer alive.");
            }

            if (_world.Has<PresentationDestroyPending>(source))
            {
                throw new LifecycleExecutionException("Entity lifecycle transaction failed because the source entity is already pending destroy.");
            }

            if (!_hasConfigContext ||
                !_currentConfigParams.TryGetInt(EffectParamKeys.TargetEntityTemplateKeyId, out int templateKeyId) ||
                templateKeyId <= 0)
            {
                throw new InvalidOperationException(
                    "BeginLifecycleTransaction requires config param '_ep.targetEntityTemplate' with type EntityTemplate.");
            }

            string targetTemplateId = services.TemplateKeys.GetName(templateKeyId);
            if (string.IsNullOrWhiteSpace(targetTemplateId))
            {
                throw new InvalidOperationException(
                    $"BeginLifecycleTransaction could not resolve entity template key id '{templateKeyId}'.");
            }

            if (!EffectTargetPointResolver.TryResolve(
                    _world,
                    in _currentEffectContext,
                    in _currentConfigParams,
                    EffectTargetPointResolveOptions.DeployAtTargetPoint,
                    out var placementCm))
            {
                throw new LifecycleExecutionException(
                    "DeployConsumeSource failed because target point could not be resolved.");
            }

            var state = new LifecycleTransactionState
            {
                Source = source,
                TargetTemplateId = targetTemplateId,
                PlacementCm = placementCm,
                Snapshot = LifecycleSnapshot.Capture(_world, source),
            };
            RuntimeEntityLifecycleTransactionExecutor.ConfigureDeployConsumeSourceFromConfig(
                state,
                in _currentConfigParams);
            runtime.LifecycleTransaction = state;
        }

        public void InvokeBuiltin(int builtinHandlerId)
        {
            var runtime = RequireBuiltinRuntime();
            var registry = RequireBuiltinHandlers();
            var templates = RequireEffectTemplates();

            if (!_hasEffectContext)
            {
                throw new InvalidOperationException("InvokeBuiltin requires an active effect context.");
            }

            if (!templates.TryGetRef(_currentEffectTemplateId, out int tplIdx))
            {
                throw new InvalidOperationException(
                    $"InvokeBuiltin requires effect template id '{_currentEffectTemplateId}', but it is not registered.");
            }

            ref readonly var tplData = ref templates.GetRef(tplIdx);
            var context = _currentEffectContext;
            var mergedParams = _hasConfigContext ? _currentConfigParams : tplData.ConfigParams;

            try
            {
                registry.Invoke(
                    (BuiltinHandlerId)builtinHandlerId,
                    _world,
                    default,
                    ref context,
                    in mergedParams,
                    in tplData,
                    runtime);
            }
            catch
            {
                RollbackLifecycleTransaction(runtime);
                throw;
            }
        }

        private BuiltinHandlerExecutionContext RequireBuiltinRuntime()
        {
            return _builtinRuntime
                ?? throw new InvalidOperationException("Graph builtin invocation requires BuiltinHandlerExecutionContext.");
        }

        private BuiltinHandlerRegistry RequireBuiltinHandlers()
        {
            return _builtinHandlers
                ?? throw new InvalidOperationException("Graph builtin invocation requires BuiltinHandlerRegistry.");
        }

        private EffectTemplateRegistry RequireEffectTemplates()
        {
            return _effectTemplates
                ?? throw new InvalidOperationException("Graph builtin invocation requires EffectTemplateRegistry.");
        }

        private void RollbackLifecycleTransaction(BuiltinHandlerExecutionContext runtime)
        {
            LifecycleTransactionState? state = runtime.LifecycleTransaction;
            if (state == null || !state.HasMaterializedTarget)
            {
                return;
            }

            EntityLifecycleAtomicOps.RollbackMaterializedTarget(_world, state.Target);
            state.HasMaterializedTarget = false;
            state.Target = Entity.Null;
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
            return RequireTagOps().HasTag(ref tags, tagId, TagSense.Effective);
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

        public SpatialQueryResult QueryRadius(IntVector2 centerCm, float radiusCm, Span<Entity> buffer)
        {
            if (_spatialQueries == null)
            {
                throw new System.InvalidOperationException("GAS.GRAPH.ERR.MissingSpatialQueryService");
            }
            var worldCenter = new WorldCmInt2(centerCm.X, centerCm.Y);
            int roundedRadiusCm = radiusCm >= 0f
                ? (int)(radiusCm + 0.5f)
                : -(int)(-radiusCm + 0.5f);
            return _spatialQueries.QueryRadius(worldCenter, roundedRadiusCm, buffer);
        }

        public SpatialQueryResult QueryCone(IntVector2 originCm, int directionDeg, int halfAngleDeg, float rangeCm, Span<Entity> buffer)
        {
            if (_spatialQueries == null) throw new System.InvalidOperationException("GAS.GRAPH.ERR.MissingSpatialQueryService");
            var worldOrigin = new WorldCmInt2(originCm.X, originCm.Y);
            int rCm = (int)(rangeCm + 0.5f);
            return _spatialQueries.QueryCone(worldOrigin, directionDeg, halfAngleDeg, rCm, buffer);
        }

        public SpatialQueryResult QueryRectangle(IntVector2 centerCm, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer)
        {
            if (_spatialQueries == null) throw new System.InvalidOperationException("GAS.GRAPH.ERR.MissingSpatialQueryService");
            var worldCenter = new WorldCmInt2(centerCm.X, centerCm.Y);
            return _spatialQueries.QueryRectangle(worldCenter, halfWidthCm, halfHeightCm, rotationDeg, buffer);
        }

        public SpatialQueryResult QueryLine(IntVector2 originCm, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer)
        {
            if (_spatialQueries == null) throw new System.InvalidOperationException("GAS.GRAPH.ERR.MissingSpatialQueryService");
            var worldOrigin = new WorldCmInt2(originCm.X, originCm.Y);
            return _spatialQueries.QueryLine(worldOrigin, directionDeg, lengthCm, halfWidthCm, buffer);
        }

        public int CollectMapEntities(Span<Entity> buffer)
        {
            return RequireEntityQueries().CollectMapEntities(buffer);
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

            return RequireEntityQueries().CopyCollection(_entityCollections, owner, collectionKeyId, buffer);
        }

        public int FilterTeam(Span<Entity> entities, int count, int teamId)
        {
            return RequireEntityQueries().FilterTeam(entities, count, teamId);
        }

        public int FilterTeamRelationship(Span<Entity> entities, int count, Entity reference, RelationshipFilter filter)
        {
            return RequireEntityQueries().FilterTeamRelationship(entities, count, reference, filter);
        }

        public int FilterTemplate(Span<Entity> entities, int count, int templateKeyId)
        {
            return RequireEntityQueries().FilterTemplate(entities, count, templateKeyId);
        }

        public int FilterAttributeRange(Span<Entity> entities, int count, int attributeId, float minInclusive, float maxInclusive)
        {
            return RequireEntityQueries().FilterAttributeRange(entities, count, attributeId, minInclusive, maxInclusive);
        }

        public int FilterTagAny(Span<Entity> entities, int count, int tagId)
        {
            return RequireEntityQueries().FilterTagAny(entities, count, tagId);
        }

        public int FilterTagNone(Span<Entity> entities, int count, int tagId)
        {
            return RequireEntityQueries().FilterTagNone(entities, count, tagId);
        }

        public int FilterLayer(Span<Entity> entities, int count, uint requiredMask)
        {
            return RequireEntityQueries().FilterLayer(entities, count, requiredMask);
        }

        public int FilterNotEntity(Span<Entity> entities, int count, Entity exclude)
        {
            return RequireEntityQueries().FilterNotEntity(entities, count, exclude);
        }

        public int SortStableDedup(Span<Entity> entities, int count)
        {
            return RequireEntityQueries().SortStableDedup(entities, count);
        }

        public int Limit(Span<Entity> entities, int count, int limit)
        {
            return RequireEntityQueries().Limit(entities, count, limit);
        }

        public void SortByAttribute(Span<Entity> entities, int count, int attributeId, bool descending)
        {
            RequireEntityQueries().SortByAttribute(entities, count, attributeId, descending);
        }

        public float SumAttribute(ReadOnlySpan<Entity> entities, int attributeId)
        {
            return RequireEntityQueries().SumAttribute(entities, attributeId);
        }

        public float AverageAttribute(ReadOnlySpan<Entity> entities, int attributeId)
        {
            return RequireEntityQueries().AverageAttribute(entities, attributeId);
        }

        public float MaxAttribute(ReadOnlySpan<Entity> entities, int attributeId)
        {
            return RequireEntityQueries().MaxAttribute(entities, attributeId);
        }

        public float MinAttribute(ReadOnlySpan<Entity> entities, int attributeId)
        {
            return RequireEntityQueries().MinAttribute(entities, attributeId);
        }

        public bool TryMaxEntityByAttribute(ReadOnlySpan<Entity> entities, int attributeId, out Entity entity, out float value)
        {
            return RequireEntityQueries().TryMaxEntityByAttribute(entities, attributeId, out entity, out value);
        }

        public bool TryMinEntityByAttribute(ReadOnlySpan<Entity> entities, int attributeId, out Entity entity, out float value)
        {
            return RequireEntityQueries().TryMinEntityByAttribute(entities, attributeId, out entity, out value);
        }

        public bool TryMinEntityByDistance(ReadOnlySpan<Entity> entities, IntVector2 center, out Entity entity, out long distanceSquared)
        {
            return RequireEntityQueries().TryMinEntityByDistance(entities, center, out entity, out distanceSquared);
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
        public void EnsureRelationshipLink(Entity source, Entity target, int typeId) => RequireRelationshipRuntime().EnsureLink(source, target, typeId);
        public void RemoveRelationshipLink(Entity source, Entity target, int typeId) => RequireRelationshipRuntime().RemoveLink(source, target, typeId);
        public short SetRelationshipMetric(Entity source, Entity target, int metricId, int value, int reasonId, int typeId)
            => RequireRelationshipRuntime().SetMetric(source, target, typeId, metricId, value, reasonId);
        public short AddRelationshipMetric(Entity source, Entity target, int metricId, int delta, int reasonId, int typeId)
            => RequireRelationshipRuntime().AddMetric(source, target, typeId, metricId, delta, reasonId);
        public short GetRelationshipMetric(Entity source, Entity target, int metricId, int typeId)
            => RequireRelationshipRuntime().GetMetric(source, target, typeId, metricId);
        public bool HasRelationshipFlag(Entity source, Entity target, int flagId, int typeId)
            => RequireRelationshipRuntime().HasFlag(source, target, typeId, flagId);
        public void SetRelationshipFlag(Entity source, Entity target, int flagId, bool enabled, int reasonId, int typeId)
            => RequireRelationshipRuntime().SetFlag(source, target, typeId, flagId, enabled, reasonId);
        public int CollectOutgoing(Entity source, Span<Entity> buffer, int typeId = RelationshipTypeRegistry.AnyTypeId)
            => RequireRelationshipRuntime().CollectOutgoing(source, typeId, buffer);
        public int CollectIncoming(Entity target, Span<Entity> buffer, int typeId = RelationshipTypeRegistry.AnyTypeId)
            => RequireRelationshipRuntime().CollectIncoming(target, typeId, buffer);
        public int CollectMutual(Entity first, Entity second, Span<Entity> buffer, int typeId = RelationshipTypeRegistry.AnyTypeId)
            => RequireRelationshipRuntime().CollectMutual(first, second, typeId, buffer);
        public int CollectBetweenPair(Entity source, Entity target, Span<Entity> buffer, int typeId = RelationshipTypeRegistry.AnyTypeId)
            => RequireRelationshipRuntime().CollectBetweenPair(source, target, typeId, buffer);
        public int FilterRelationshipMetricRange(Span<Entity> entities, int count, Entity source, int typeId, int metricId, short minInclusive, short maxInclusive)
            => RequireEntityQueries().FilterRelationshipMetricRange(entities, count, source, typeId, metricId, minInclusive, maxInclusive);
        public int FilterRelationshipFlag(Span<Entity> entities, int count, Entity source, int typeId, int flagId, bool expected)
            => RequireEntityQueries().FilterRelationshipFlag(entities, count, source, typeId, flagId, expected);
        public void SortByRelationshipMetric(Span<Entity> entities, int count, Entity source, int typeId, int metricId, bool descending)
            => RequireEntityQueries().SortByRelationshipMetric(entities, count, source, typeId, metricId, descending);
        public int SumRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId)
            => RequireEntityQueries().SumRelationshipMetric(entities, source, typeId, metricId);
        public int AverageRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId)
            => RequireEntityQueries().AverageRelationshipMetric(entities, source, typeId, metricId);
        public int MaxRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId)
            => RequireEntityQueries().MaxRelationshipMetric(entities, source, typeId, metricId);
        public int MinRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId)
            => RequireEntityQueries().MinRelationshipMetric(entities, source, typeId, metricId);
        public bool TryMaxEntityByRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId, out Entity entity, out int value)
            => RequireEntityQueries().TryMaxEntityByRelationshipMetric(entities, source, typeId, metricId, out entity, out value);
        public bool TryMinEntityByRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId, out Entity entity, out int value)
            => RequireEntityQueries().TryMinEntityByRelationshipMetric(entities, source, typeId, metricId, out entity, out value);

        // ── Topology predicates (RFC-0065 PROV-4b / DEC-5) ──

        public bool HasRelationshipLink(Entity source, Entity target, int typeId)
            => RequireRelationshipRuntime().HasLink(source, target, typeId);

        public Entity ResolveControlDomain(Entity target)
            => RequireControlDomains().TryResolveControlDomain(target, out Entity domainRep) ? domainRep : Entity.Null;

        public bool IsControllableBy(Entity controllerRep, Entity target)
            => RequireControlDomains().IsControllableBy(controllerRep, target);

        public bool HasKnowledgeProjection(Entity viewer, Entity target)
            => RequireKnowledgeProjections().CanKnowEntity(viewer, target, CurrentStepTick());

        private ControlDomainQuery RequireControlDomains()
        {
            return _controlDomains ?? throw new InvalidOperationException("GAS.GRAPH.ERR.MissingControlDomainQuery");
        }

        private KnowledgeProjectionResolver RequireKnowledgeProjections()
        {
            return _knowledgeProjections ?? throw new InvalidOperationException("GAS.GRAPH.ERR.MissingKnowledgeProjectionResolver");
        }

        private int CurrentStepTick()
        {
            return _clock?.Now(ClockDomainId.Step) ?? 0;
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

            // Convert EffectArgs to CallerParams
            var req = new Ludots.Core.Gameplay.GAS.EffectRequest
            {
                RootId = ResolveChildEffectRootId(),
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

            TargetResolverContextMapping mapping = RequireTargetDispatchPresets().Get(payloadPresetId);
            TargetResolverFanOutHelper.PublishResolvedTargets(
                rootId: ResolveChildEffectRootId(),
                source,
                target,
                targetContext,
                targets,
                templateId,
                in mapping,
                _effectRequests);
        }

        private int ResolveChildEffectRootId()
        {
            if (!_hasEffectContext)
            {
                return 0;
            }

            if (_currentEffectContext.RootId <= 0)
            {
                throw new InvalidOperationException("GAS.GRAPH.ERR.MissingParentEffectRoot");
            }

            return _currentEffectContext.RootId;
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
                if (gameplayEffect.AggregatesModifiers && !_world.Has<AttributeAggregateDirty>(target))
                {
                    _world.Add(target, new AttributeAggregateDirty());
                }
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

        public SpatialQueryResult QueryHexRange(IntVector2 centerCm, int hexRadius, Span<Entity> buffer)
        {
            if (_spatialQueries == null) throw new InvalidOperationException("GAS.GRAPH.ERR.MissingSpatialQueryService");
            if (_coords == null) throw new InvalidOperationException("GAS.GRAPH.ERR.MissingSpatialCoordinateConverter");
            var hexCenter = _coords.WorldToHex(new WorldCmInt2(centerCm.X, centerCm.Y));
            return _spatialQueries.QueryHexRange(hexCenter, hexRadius, buffer);
        }

        public SpatialQueryResult QueryHexRing(IntVector2 centerCm, int hexRadius, Span<Entity> buffer)
        {
            if (_spatialQueries == null) throw new InvalidOperationException("GAS.GRAPH.ERR.MissingSpatialQueryService");
            if (_coords == null) throw new InvalidOperationException("GAS.GRAPH.ERR.MissingSpatialCoordinateConverter");
            var hexCenter = _coords.WorldToHex(new WorldCmInt2(centerCm.X, centerCm.Y));
            return _spatialQueries.QueryHexRing(hexCenter, hexRadius, buffer);
        }

        public SpatialQueryResult QueryHexNeighbors(IntVector2 centerCm, Span<Entity> buffer)
        {
            // Neighbors = Ring(1)
            return QueryHexRing(centerCm, 1, buffer);
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

        public bool TrySnapTargetToNearestInCollection(
            Entity owner,
            int collectionKeyId,
            ref IntVector2 targetPosCm,
            float maxDistanceCm,
            out Entity snappedEntity)
        {
            snappedEntity = Entity.Null;
            if (_entityCollections == null)
            {
                return false;
            }

            Fix64Vec2 pointCm = Fix64Vec2.FromInt(targetPosCm.X, targetPosCm.Y);
            bool found = PlacementValidation.TrySnapToNearestInCollection(
                _world,
                _entityCollections,
                owner,
                collectionKeyId,
                in pointCm,
                Fix64.FromFloat(maxDistanceCm),
                out Fix64Vec2 snappedCm,
                out snappedEntity);
            if (found)
            {
                var rounded = snappedCm.RoundToInt();
                targetPosCm = new IntVector2(rounded.x, rounded.y);
            }

            return found;
        }

        public bool TrySnapTargetToNearestGraphEdge(
            ref IntVector2 targetPosCm,
            float searchRadiusCm,
            out GraphEdgeProjection projection)
        {
            projection = default;
            LoadedGraphRuntime? runtime = _loadedGraphRuntime;
            if (runtime == null || !runtime.HasLoadedGraph || searchRadiusCm <= 0f)
            {
                return false;
            }

            EnsureGraphProjectionCandidateCapacity(runtime.CurrentGraph.NodeCount);
            Fix64Vec2 pointCm = Fix64Vec2.FromInt(targetPosCm.X, targetPosCm.Y);
            bool found = PlacementValidation.TrySnapToNearestGraphEdge(
                runtime.CurrentGraph,
                runtime.CurrentSpatialIndex,
                ref pointCm,
                Fix64.FromFloat(searchRadiusCm),
                _graphProjectionCandidateScratch,
                out projection);
            if (found)
            {
                var rounded = pointCm.RoundToInt();
                targetPosCm = new IntVector2(rounded.x, rounded.y);
            }

            return found;
        }

        private void EnsureGraphProjectionCandidateCapacity(int required)
        {
            if (_graphProjectionCandidateScratch.Length >= required)
            {
                return;
            }

            int next = _graphProjectionCandidateScratch.Length == 0 ? 64 : _graphProjectionCandidateScratch.Length * 2;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _graphProjectionCandidateScratch, next);
        }
    }
}
