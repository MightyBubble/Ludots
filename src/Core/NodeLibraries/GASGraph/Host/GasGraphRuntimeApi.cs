using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
using Ludots.Core.Map;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Spatial;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Progression.Components;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Placement;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.GraphQuery;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    public sealed class GasGraphRuntimeProductionServices
    {
        public GasGraphRuntimeProductionServices(
            World world,
            ISpatialQueryService spatialQueries,
            ISpatialCoordinateConverter coords,
            GameplayEventBus eventBus,
            EffectRequestQueue effectRequests,
            TagOps tagOps,
            RelationshipRuntime relationshipRuntime,
            RelationshipTypeRegistry typeRegistry,
            RelationshipMetricRegistry metricRegistry,
            RelationshipFlagRegistry flagRegistry,
            RelationshipReasonRegistry reasonRegistry,
            TargetDispatchPresetRegistry targetDispatchPresets,
            EntityCollectionStore entityCollections,
            EntitySetQueryRuntime entityQueries,
            ControlDomainQuery controlDomains,
            KnowledgeProjectionResolver knowledgeProjections,
            IClock clock,
            InventoryRuntimeService inventoryRuntime,
            ItemDefinitionRegistry itemDefinitions,
            GraphLookupTableRegistry? lookupTables = null)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            SpatialQueries = spatialQueries ?? throw new ArgumentNullException(nameof(spatialQueries));
            Coords = coords ?? throw new ArgumentNullException(nameof(coords));
            EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            EffectRequests = effectRequests ?? throw new ArgumentNullException(nameof(effectRequests));
            TagOps = tagOps ?? throw new ArgumentNullException(nameof(tagOps));
            RelationshipRuntime = relationshipRuntime ?? throw new ArgumentNullException(nameof(relationshipRuntime));
            TypeRegistry = typeRegistry ?? throw new ArgumentNullException(nameof(typeRegistry));
            MetricRegistry = metricRegistry ?? throw new ArgumentNullException(nameof(metricRegistry));
            FlagRegistry = flagRegistry ?? throw new ArgumentNullException(nameof(flagRegistry));
            ReasonRegistry = reasonRegistry ?? throw new ArgumentNullException(nameof(reasonRegistry));
            TargetDispatchPresets = targetDispatchPresets ?? throw new ArgumentNullException(nameof(targetDispatchPresets));
            EntityCollections = entityCollections ?? throw new ArgumentNullException(nameof(entityCollections));
            EntityQueries = entityQueries ?? throw new ArgumentNullException(nameof(entityQueries));
            ControlDomains = controlDomains ?? throw new ArgumentNullException(nameof(controlDomains));
            KnowledgeProjections = knowledgeProjections ?? throw new ArgumentNullException(nameof(knowledgeProjections));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            InventoryRuntime = inventoryRuntime ?? throw new ArgumentNullException(nameof(inventoryRuntime));
            ItemDefinitions = itemDefinitions ?? throw new ArgumentNullException(nameof(itemDefinitions));
            LookupTables = lookupTables;
        }

        public World World { get; }
        public ISpatialQueryService SpatialQueries { get; }
        public ISpatialCoordinateConverter Coords { get; }
        public GameplayEventBus EventBus { get; }
        public EffectRequestQueue EffectRequests { get; }
        public TagOps TagOps { get; }
        public RelationshipRuntime RelationshipRuntime { get; }
        public RelationshipTypeRegistry TypeRegistry { get; }
        public RelationshipMetricRegistry MetricRegistry { get; }
        public RelationshipFlagRegistry FlagRegistry { get; }
        public RelationshipReasonRegistry ReasonRegistry { get; }
        public TargetDispatchPresetRegistry TargetDispatchPresets { get; }
        public EntityCollectionStore EntityCollections { get; }
        public GraphLookupTableRegistry? LookupTables { get; }

        public EntitySetQueryRuntime EntityQueries { get; }
        public ControlDomainQuery ControlDomains { get; }
        public KnowledgeProjectionResolver KnowledgeProjections { get; }
        public IClock Clock { get; }
        public InventoryRuntimeService InventoryRuntime { get; }
        public ItemDefinitionRegistry ItemDefinitions { get; }
    }

    public sealed class GasGraphRuntimeApi : IDerivedAttributeGraphRuntimeApi
    {
        public const string MissingBlackboardError = "GAS.GRAPH.ERR.MissingBlackboard";
        private static readonly QueryDescription TaskInstanceQuery = new QueryDescription().WithAll<TaskInstanceCm>();

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
        private readonly GraphLookupTableRegistry? _lookupTables;
        private readonly InventoryRuntimeService? _inventory;
        private readonly ItemDefinitionRegistry? _itemDefinitions;
        private Gameplay.Rng.RngPickService? _rngPickService;
        private Ludots.Core.UI.PanelActivation.PanelActivationApi? _panelActivationApi;
        private Ludots.Core.UI.PanelHosting.PanelHost? _panelHost;
        private GraphPresentationTextSink? _presentationTextSink;
        private LoadedGraphRuntime? _loadedGraphRuntime;
        private Func<MapId, Gameplay.MapTriggers.MapVariableStore?>? _mapVariableStoreResolver;
        private Func<MapId, Ludots.Core.Systems.MapLoadEntityIndex?>? _placedInstanceIndexResolver;
        private Func<MapId, IReadOnlySet<string>?>? _regionCatalogResolver;
        private Ludots.Core.Scripting.TriggerManager? _triggerManager;
        private Ludots.Core.GraphRuntime.GraphCallbackService? _graphCallbacks;
        private Gameplay.Spawning.RuntimeEntitySpawnQueue? _runtimeEntitySpawnQueue;
        private Gameplay.Spawning.EntityTemplateKeyRegistry? _entityTemplateKeys;

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
        private Entity _derivedAttributeWriteOwner;
        private AttributeBuffer _derivedAttributeWriteBuffer;
        private bool _derivedAttributeWritesActive;
        private EffectPhaseSideEffectTransaction? _effectSideEffects;

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

            return CreateProduction(new GasGraphRuntimeProductionServices(
                world,
                spatialQueries ?? throw new InvalidOperationException("Production GasGraphRuntimeApi requires SpatialQueryService."),
                coords ?? throw new InvalidOperationException("Production GasGraphRuntimeApi requires SpatialCoordinateConverter."),
                eventBus ?? throw new InvalidOperationException("Production GasGraphRuntimeApi requires GameplayEventBus."),
                effectRequests ?? throw new InvalidOperationException("Production GasGraphRuntimeApi requires EffectRequestQueue."),
                RequireService(services, CoreServiceKeys.TagOps),
                RequireService(services, CoreServiceKeys.RelationshipRuntime),
                RequireService(services, CoreServiceKeys.RelationshipTypeRegistry),
                RequireService(services, CoreServiceKeys.RelationshipMetricRegistry),
                RequireService(services, CoreServiceKeys.RelationshipFlagRegistry),
                RequireService(services, CoreServiceKeys.RelationshipReasonRegistry),
                RequireService(services, CoreServiceKeys.TargetDispatchPresetRegistry),
                RequireService(services, CoreServiceKeys.EntityCollectionStore),
                RequireService(services, CoreServiceKeys.EntitySetQueryRuntime),
                RequireService(services, CoreServiceKeys.ControlDomainQuery),
                RequireService(services, CoreServiceKeys.KnowledgeProjectionResolver),
                RequireService(services, CoreServiceKeys.Clock),
                RequireService(services, CoreServiceKeys.InventoryRuntimeService),
                RequireService(services, CoreServiceKeys.ItemDefinitionRegistry)));
        }

        public static GasGraphRuntimeApi CreateProduction(GasGraphRuntimeProductionServices services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            var api = new GasGraphRuntimeApi(
                services.World,
                services.SpatialQueries,
                services.Coords,
                services.EventBus,
                services.EffectRequests,
                services.TagOps,
                services.RelationshipRuntime,
                services.TypeRegistry,
                services.MetricRegistry,
                services.FlagRegistry,
                services.ReasonRegistry,
                services.TargetDispatchPresets,
                services.EntityCollections,
                services.EntityQueries,
                lookupTables: services.LookupTables,
                inventory: services.InventoryRuntime,
                itemDefinitions: services.ItemDefinitions);
            api.BindTopologyServices(
                services.ControlDomains,
                services.KnowledgeProjections,
                services.Clock);
            return api;
        }

        private static T RequireService<T>(IReadOnlyDictionary<string, object> services, ServiceKey<T> key)
        {
            if (!services.TryGetValue(key.Name, out object? value) || value is not T typed)
            {
                throw new InvalidOperationException($"Production GasGraphRuntimeApi requires engine-owned service `{key.Name}`.");
            }

            return typed;
        }

        public Ludots.Core.UI.PanelActivation.PanelActivationApi? PanelActivationApi => _panelActivationApi;

        public void BindPanelActivation(Ludots.Core.UI.PanelActivation.PanelActivationApi api)
        {
            _panelActivationApi = api ?? throw new ArgumentNullException(nameof(api));
        }

        public Ludots.Core.UI.PanelHosting.PanelHost? PanelHost => _panelHost;

        public void BindPanelHost(Ludots.Core.UI.PanelHosting.PanelHost host)
        {
            _panelHost = host ?? throw new ArgumentNullException(nameof(host));
        }

        /// <summary>
        /// Resolves a map id to its live <see cref="Gameplay.MapTriggers.MapVariableStore"/>.
        /// The engine binds this lazily because map sessions are created after the graph API.
        /// </summary>
        public void BindMapVariableStoreResolver(Func<MapId, Gameplay.MapTriggers.MapVariableStore?> resolver)
        {
            _mapVariableStoreResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        /// <summary>
        /// Resolves a map id to its live placed-instance index (#1108). Bound by the engine
        /// next to the variable-store resolver: LoadPlacedEntity reads the same session.
        /// </summary>
        public void BindPlacedInstanceIndexResolver(Func<MapId, Ludots.Core.Systems.MapLoadEntityIndex?> resolver)
        {
            _placedInstanceIndexResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        /// <summary>
        /// Resolves a map id to its authored Regions id set (#1108 LoadPlacedRegion).
        /// Bound next to the placed-instance index; never writes into EntityIndex.
        /// </summary>
        public void BindRegionCatalogResolver(Func<MapId, System.Collections.Generic.IReadOnlySet<string>?> resolver)
        {
            _regionCatalogResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        /// <summary>
        /// Binds the engine TriggerManager so graph programs can fire map-scoped trigger
        /// events via <see cref="FireEventKey"/>.
        /// </summary>
        public void BindTriggerManager(Ludots.Core.Scripting.TriggerManager triggerManager)
        {
            _triggerManager = triggerManager ?? throw new ArgumentNullException(nameof(triggerManager));
        }

        /// <summary>
        /// Binds #1126 AwaitCallback registration/completion service.
        /// </summary>
        public void BindGraphCallbackService(Ludots.Core.GraphRuntime.GraphCallbackService callbacks)
        {
            _graphCallbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
        }

        /// <summary>
        /// Binds the runtime entity spawn queue and template key registry so graph
        /// programs can enqueue template spawns via <see cref="SpawnTemplate"/>.
        /// </summary>
        public void BindRuntimeEntitySpawn(
            Gameplay.Spawning.RuntimeEntitySpawnQueue queue,
            Gameplay.Spawning.EntityTemplateKeyRegistry templateKeys)
        {
            _runtimeEntitySpawnQueue = queue ?? throw new ArgumentNullException(nameof(queue));
            _entityTemplateKeys = templateKeys ?? throw new ArgumentNullException(nameof(templateKeys));
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
            EntitySetQueryRuntime? entityQueries = null,
            GraphLookupTableRegistry? lookupTables = null,
            InventoryRuntimeService? inventory = null,
            ItemDefinitionRegistry? itemDefinitions = null)
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
            _lookupTables = lookupTables;
            _inventory = inventory;
            _itemDefinitions = itemDefinitions;
            _ = typeRegistry;
            _ = metricRegistry;
            _ = flagRegistry;
            _ = reasonRegistry;
        }

        private TagOps RequireTagOps()
        {
            return _tagOps ?? throw new InvalidOperationException("GAS.GRAPH.ERR.MissingTagOps");
        }


        public void BindRngPickService(Gameplay.Rng.RngPickService rngPickService)
        {
            _rngPickService = rngPickService ?? throw new ArgumentNullException(nameof(rngPickService));
        }

        public int WeightedPick(int distributionKeyId, int modulationPermille)
        {
            var picks = _rngPickService
                ?? throw new InvalidOperationException("GAS.GRAPH.ERR.RngPickUnavailable");
            return picks.PickByKeyId(distributionKeyId, Math.Clamp(modulationPermille, -1000, 1000) / 1000f);
        }
        public int ResolveTableRow(int tableId, int key)
        {
            var tables = _lookupTables
                ?? throw new InvalidOperationException("GAS.GRAPH.ERR.LookupTableUnavailable");
            return tables.ResolveRow(tableId, key);
        }

        public int TableReadInt(int fieldId, int rowHandle)
        {
            var tables = _lookupTables
                ?? throw new InvalidOperationException("GAS.GRAPH.ERR.LookupTableUnavailable");
            return tables.ReadInt(rowHandle, fieldId);
        }

        public float TableReadFloat(int fieldId, int rowHandle)
        {
            var tables = _lookupTables
                ?? throw new InvalidOperationException("GAS.GRAPH.ERR.LookupTableUnavailable");
            return tables.ReadFloat(rowHandle, fieldId);
        }

        public void ShowPanel(int panelTypeId)
        {
            RequirePanelActivationApi().ShowPanel(ResolvePanelTypeName(panelTypeId));
        }

        public void HidePanel(int panelTypeId)
        {
            RequirePanelActivationApi().HidePanel(ResolvePanelTypeName(panelTypeId));
        }

        public void CreatePanel(int templateKeyId, int anchorKeyId, Entity scope)
        {
            CreatePanel(templateKeyId, anchorKeyId, scope, UI.PanelHosting.PanelSkinIds.Unspecified, 100f);
        }

        public void CreatePanel(int templateKeyId, int anchorKeyId, Entity scope, byte skinId, float zOrder)
        {
            RequirePanelHost().Instantiate(
                ResolvePanelTypeName(templateKeyId),
                ResolvePanelTypeName(anchorKeyId),
                scope,
                UI.PanelHosting.PanelSkinIds.ToName(skinId),
                (int)zOrder);
        }

        public void DestroyPanel(int templateKeyId, Entity scope)
        {
            RequirePanelHost().DisposeMatching(ResolvePanelTypeName(templateKeyId), scope);
        }

        public void PushPresentationText(GraphPresentationTextSurface surface, ReadOnlySpan<char> text)
        {
            GraphPresentationTextSink sink = _presentationTextSink
                ?? throw new InvalidOperationException(GraphPresentationTextSink.UnavailableError);
            sink.Push(surface, text);
        }

        public GraphPresentationTextSink? PresentationTextSink => _presentationTextSink;

        public void BindPresentationTextSink(GraphPresentationTextSink sink)
        {
            _presentationTextSink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        private string ResolvePanelTypeName(int panelTypeId)
        {
            string? name = Gameplay.GAS.Registry.ConfigKeyRegistry.GetName(panelTypeId);
            return name ?? throw new InvalidOperationException(
                $"Panel op references unregistered config key id {panelTypeId}.");
        }

        public int ReadMapVarInt(int varKeyId, MapId mapId)
            => ResolveMapVariableStore(mapId).ReadInt(ResolveMapVariableName(varKeyId));

        public float ReadMapVarFloat(int varKeyId, MapId mapId)
            => ResolveMapVariableStore(mapId).ReadFloat(ResolveMapVariableName(varKeyId));

        public void WriteMapVarInt(int varKeyId, MapId mapId, int value)
            => ResolveMapVariableStore(mapId).WriteInt(ResolveMapVariableName(varKeyId), value);

        public void WriteMapVarFloat(int varKeyId, MapId mapId, float value)
            => ResolveMapVariableStore(mapId).WriteFloat(ResolveMapVariableName(varKeyId), value);

        public bool TryGetPlacedEntity(int instanceKeyId, MapId mapId, out Entity entity)
        {
            var resolver = _placedInstanceIndexResolver
                ?? throw new InvalidOperationException("GAS.GRAPH.ERR.PlacedIndexUnavailable");
            Ludots.Core.Systems.MapLoadEntityIndex index = resolver(mapId)
                ?? throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.PlacedIndexUnavailable: map '{mapId.Value}' has no live placed-instance index.");
            string instanceId = Gameplay.GAS.Registry.ConfigKeyRegistry.GetName(instanceKeyId)
                ?? throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.PlacedInstanceNameUnknown: placed-instance op references unregistered config key id {instanceKeyId}.");
            return index.TryGet(instanceId, out entity);
        }

        public bool TryHasPlacedRegion(int regionKeyId, MapId mapId)
        {
            var resolver = _regionCatalogResolver
                ?? throw new InvalidOperationException("GAS.GRAPH.ERR.RegionCatalogUnavailable");
            IReadOnlySet<string> catalog = resolver(mapId)
                ?? throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.RegionCatalogUnavailable: map '{mapId.Value}' has no live region catalog.");
            string regionId = Gameplay.GAS.Registry.ConfigKeyRegistry.GetName(regionKeyId)
                ?? throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.PlacedRegionNameUnknown: LoadPlacedRegion references unregistered config key id {regionKeyId}.");
            return catalog.Contains(regionId);
        }

        /// <summary>
        /// Fires a config-key-named trigger event from a graph program in the scope entity's map.
        /// </summary>
        public void FireEventKey(Entity scope, int eventKeyId)
        {
            RejectDerivedAttributeSideEffect(nameof(FireEventKey));
            RejectNonTransactionalEffectSideEffect(nameof(FireEventKey));
            var triggerManager = _triggerManager
                ?? throw new InvalidOperationException("GAS.GRAPH.ERR.TriggerBridgeUnavailable");

            string? name = Gameplay.GAS.Registry.ConfigKeyRegistry.GetName(eventKeyId);
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.EventKeyNameUnknown: FireEventKey references unregistered config key id {eventKeyId}.");
            }

            MapId mapId = ResolveRequiredMapId(scope);
            var context = new ScriptContext();
            context.Set(ContextKeys.MapId, mapId);
            context.Set(MapTriggerEventPayloadKeys.SourceEntity, scope);
            triggerManager.FireMapEvent(mapId, new EventKey(name), context);
        }

        /// <summary>
        /// Structured map-event dispatch (#1115): assembles a ScriptContext from the StoreArg*
        /// staging table per the event schema and fires it map-scoped. Fire-time
        /// ValidateFirePayload backstops missing required params, type mismatches, and
        /// undeclared MapTrigger.* keys.
        /// </summary>
        public void FireMapEventPayload(int eventKeyId, MapId mapId, Entity selfSource, GraphEntryPayloadTable? stagedArgs)
        {
            RejectDerivedAttributeSideEffect(nameof(FireMapEventPayload));
            var triggerManager = _triggerManager
                ?? throw new InvalidOperationException("GAS.GRAPH.ERR.TriggerBridgeUnavailable");

            EventSchema schema = RequireDispatchEventSchema(triggerManager, eventKeyId, out string name);

            if (string.IsNullOrEmpty(mapId.Value))
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.DispatchMapEventNoMapScope: DispatchMapEvent '{name}' requires a map scope.");
            }

            ScriptContext context = BuildDispatchContext(schema, mapId, stagedArgs);

            if (selfSource != Entity.Null && selfSource != default &&
                schema.DeclaresPayloadKey(MapTriggerEventPayloadKeys.SourceEntity))
            {
                context.Set(MapTriggerEventPayloadKeys.SourceEntity, selfSource);
            }

            triggerManager.FireMapEvent(mapId, new EventKey(name), context);
        }

        /// <summary>
        /// Global-scope dispatch (#1123): same schema-driven context assembly, then
        /// TriggerManager.FireGlobalEvent — only the global subscription table sees it,
        /// regardless of how many maps or map triggers are live. The origin map (mount
        /// scope or caster anchor) rides MapTrigger.SourceMapId as transport metadata.
        /// </summary>
        public void FireGlobalEventPayload(int eventKeyId, MapId originMapId, GraphEntryPayloadTable? stagedArgs)
        {
            RejectDerivedAttributeSideEffect(nameof(FireGlobalEventPayload));
            var triggerManager = _triggerManager
                ?? throw new InvalidOperationException("GAS.GRAPH.ERR.TriggerBridgeUnavailable");

            EventSchema schema = RequireDispatchEventSchema(triggerManager, eventKeyId, out string name);

            if (schema.Scope != EventScope.Global)
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.DispatchEventScopeMismatch: DispatchMapEvent '{name}' global dispatch requires a " +
                    $"Global-scope schema (declared '{schema.Scope}'); the compiler should have rejected this graph.");
            }

            ScriptContext context = BuildDispatchContext(schema, originMapId, stagedArgs);

            triggerManager.FireGlobalEvent(new EventKey(name), context);
        }

        public void BeginAwaitCallback(string callbackType, MapId mapId, Entity scope, int resultBoolRegister)
        {
            RejectDerivedAttributeSideEffect(nameof(BeginAwaitCallback));
            GraphCallbackService callbacks = _graphCallbacks
                ?? throw new InvalidOperationException("GAS.GRAPH.ERR.GraphCallbackUnavailable");
            callbacks.BeginAwait(callbackType, mapId, scope, resultBoolRegister);
        }

        private static EventSchema RequireDispatchEventSchema(TriggerManager triggerManager, int eventKeyId, out string name)
        {
            name = Gameplay.GAS.Registry.ConfigKeyRegistry.GetName(eventKeyId)
                ?? throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.EventKeyNameUnknown: DispatchMapEvent references unregistered config key id {eventKeyId}.");

            EventSchemaRegistry? schemas = triggerManager.EventSchemas
                ?? throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.EventSchemaUnavailable: DispatchMapEvent '{name}' requires the engine EventSchemaRegistry.");

            if (!schemas.TryGet(name, out EventSchema schema))
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.EventSchemaUnknown: DispatchMapEvent event '{name}' has no registered schema.");
            }

            return schema;
        }

        private static ScriptContext BuildDispatchContext(EventSchema schema, MapId mapId, GraphEntryPayloadTable? stagedArgs)
        {
            var context = new ScriptContext();
            if (!string.IsNullOrEmpty(mapId.Value))
            {
                context.Set(ContextKeys.MapId, mapId);
                context.Set(MapTriggerEventPayloadKeys.SourceMapId, mapId);
            }

            for (int i = 0; i < schema.Params.Count; i++)
            {
                EventParamSchema param = schema.Params[i];
                if (stagedArgs == null)
                {
                    continue;
                }

                switch (param.Type)
                {
                    case EventParamType.Entity:
                        if (stagedArgs.TryGetEntity(param.PayloadKey, out Entity entityValue))
                        {
                            context.Set(param.PayloadKey, entityValue);
                        }

                        break;
                    case EventParamType.Int:
                        if (stagedArgs.TryGetInt(param.PayloadKey, out int intValue))
                        {
                            context.Set(param.PayloadKey, intValue);
                        }

                        break;
                    case EventParamType.Float:
                        if (stagedArgs.TryGetFloat(param.PayloadKey, out float floatValue))
                        {
                            context.Set(param.PayloadKey, floatValue);
                        }

                        break;
                }
            }

            return context;
        }

        /// <summary>
        /// Sets an entity's world position. Fail-closed on dead or unmapped targets.
        /// </summary>
        public void SetWorldPosition(Entity target, int xCm, int yCm)
        {
            RejectDerivedAttributeSideEffect(nameof(SetWorldPosition));
            if (_world == null || !_world.IsAlive(target))
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.SetWorldPositionTargetDead: target entity {target} is not alive.");
            }

            var position = new Ludots.Core.Components.WorldPositionCm
            {
                Value = Mathematics.FixedPoint.Fix64Vec2.FromInt(xCm, yCm),
            };

            if (_world.TryGet(target, out Ludots.Core.Components.WorldPositionCm existing))
            {
                _world.Set(target, position);
            }
            else
            {
                _world.Add(target, position);
            }
        }

        /// <summary>
        /// Enqueues a template entity spawn on the runtime spawn queue. Fail-closed on
        /// unknown template symbols, unmapped spawn anchors, and queue capacity.
        /// </summary>
        public void SpawnTemplate(int templateKeyId, Entity source, float xCm, float yCm, bool hasPosition)
        {
            RejectDerivedAttributeSideEffect(nameof(SpawnTemplate));
            var queue = _runtimeEntitySpawnQueue
                ?? throw new InvalidOperationException("GAS.GRAPH.ERR.SpawnQueueUnavailable");
            if (_entityTemplateKeys == null)
            {
                throw new InvalidOperationException("GAS.GRAPH.ERR.TemplateKeysUnavailable");
            }

            string templateName = _entityTemplateKeys.GetName(templateKeyId);
            if (string.IsNullOrWhiteSpace(templateName))
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.TemplateUnknown: SpawnTemplate references unregistered template key id {templateKeyId}.");
            }

            MapId mapId = ResolveMapId(source);
            if (string.IsNullOrEmpty(mapId.Value))
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.SpawnSourceUnmapped: SpawnTemplate source must anchor a map (template '{templateName}').");
            }

            var request = new Gameplay.Spawning.RuntimeEntitySpawnRequest
            {
                Kind = Gameplay.Spawning.RuntimeEntitySpawnKind.Template,
                Source = source,
                TemplateId = templateName,
                MapId = mapId,
                WorldPositionCm = Mathematics.FixedPoint.Fix64Vec2.FromInt((int)xCm, (int)yCm),
                HasWorldPosition = hasPosition ? (byte)1 : (byte)0,
            };

            if (!queue.TryEnqueue(in request))
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.SpawnQueueFull: SpawnTemplate '{templateName}' dropped because the runtime spawn queue is at capacity.");
            }
        }

        private MapId ResolveMapId(Entity entity)
        {
            if (_world != null &&
                _world.IsAlive(entity) &&
                _world.TryGet<Ludots.Core.Components.MapEntity>(entity, out var mapEntity))
            {
                return mapEntity.MapId;
            }

            return new MapId(string.Empty);
        }

        private MapId ResolveRequiredMapId(Entity entity)
        {
            if (entity == Entity.Null || entity == default || !_world.IsAlive(entity))
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.FireEventKeyScopeInvalid: scope entity {entity} is null or not alive.");
            }

            if (!_world.TryGet<Ludots.Core.Components.MapEntity>(entity, out var mapEntity))
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.FireEventKeyScopeInvalid: scope entity {entity} has no MapEntity.");
            }

            if (string.IsNullOrWhiteSpace(mapEntity.MapId.Value))
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.FireEventKeyScopeInvalid: scope entity {entity} has an empty map id.");
            }

            return mapEntity.MapId;
        }

        private Gameplay.MapTriggers.MapVariableStore ResolveMapVariableStore(MapId mapId)
        {
            var resolver = _mapVariableStoreResolver
                ?? throw new InvalidOperationException("GAS.GRAPH.ERR.MapVariableStoreUnavailable");
            return resolver(mapId)
                ?? throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.MapVariableStoreUnavailable: map '{mapId.Value}' has no live variable store.");
        }

        private string ResolveMapVariableName(int varKeyId)
        {
            string? name = Gameplay.GAS.Registry.ConfigKeyRegistry.GetName(varKeyId);
            return name ?? throw new InvalidOperationException(
                $"GAS.GRAPH.ERR.MapVariableNameUnknown: map variable op references unregistered config key id {varKeyId}.");
        }

        private Ludots.Core.UI.PanelActivation.PanelActivationApi RequirePanelActivationApi()
        {
            return _panelActivationApi ?? throw new InvalidOperationException("GAS.GRAPH.ERR.PanelActivationUnavailable");
        }

        private Ludots.Core.UI.PanelHosting.PanelHost RequirePanelHost()
        {
            return _panelHost ?? throw new InvalidOperationException("GAS.GRAPH.ERR.PanelHostUnavailable");
        }

        public void BeginDerivedAttributeWrites(Entity entity, in AttributeBuffer attributes)
        {
            if (_derivedAttributeWritesActive)
            {
                throw new InvalidOperationException("GAS.GRAPH.ERR.DerivedAttributeWriteScopeAlreadyActive");
            }
            if (!_world.IsAlive(entity) || !_world.Has<AttributeBuffer>(entity))
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.InvalidDerivedAttributeWriteOwner: entity={entity.Id}.");
            }

            _derivedAttributeWriteOwner = entity;
            _derivedAttributeWriteBuffer = attributes;
            _derivedAttributeWritesActive = true;
        }

        public void EndDerivedAttributeWrites(Entity entity, ref AttributeBuffer attributes, bool commit)
        {
            if (!_derivedAttributeWritesActive || _derivedAttributeWriteOwner != entity)
            {
                throw new InvalidOperationException(
                    $"GAS.GRAPH.ERR.DerivedAttributeWriteScopeMismatch: entity={entity.Id}.");
            }

            try
            {
                if (commit)
                {
                    attributes = _derivedAttributeWriteBuffer;
                }
            }
            finally
            {
                _derivedAttributeWritesActive = false;
                _derivedAttributeWriteOwner = default;
                _derivedAttributeWriteBuffer = default;
            }
        }

        private RelationshipRuntime RequireRelationshipRuntime()
        {
            return _relationshipRuntime ?? throw new InvalidOperationException("GAS.GRAPH.ERR.MissingRelationshipRuntime");
        }

        private void RejectDerivedAttributeSideEffect(string operation)
        {
            if (_derivedAttributeWritesActive)
            {
                throw new InvalidOperationException(
                    $"{IDerivedAttributeGraphRuntimeApi.SideEffectForbiddenError}: operation={operation}.");
            }
        }

        private void RejectNonTransactionalEffectSideEffect(string operation)
        {
            if (_effectSideEffects?.IsActive == true)
            {
                throw new InvalidOperationException(
                    $"{EffectPhaseSideEffectTransaction.UnsupportedSideEffectError}: operation={operation}.");
            }
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

        public void BeginEffectSideEffectTransaction(EffectPhaseSideEffectTransaction transaction)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (_effectSideEffects != null)
            {
                throw new InvalidOperationException(EffectPhaseSideEffectTransaction.ScopeAlreadyActiveError);
            }
            if (!transaction.IsActive)
            {
                throw new InvalidOperationException(EffectPhaseSideEffectTransaction.ScopeNotActiveError);
            }

            _effectSideEffects = transaction;
        }

        public void EndEffectSideEffectTransaction(EffectPhaseSideEffectTransaction transaction)
        {
            if (!ReferenceEquals(_effectSideEffects, transaction))
            {
                throw new InvalidOperationException("GAS.EFFECT_TRANSACTION.ERR.ScopeMismatch");
            }

            _effectSideEffects = null;
        }

        internal bool HasActiveEffectSideEffectTransaction => _effectSideEffects?.IsActive == true;
        internal bool HasGameplayEventBus => _eventBus != null;

        public void BindLoadedGraphRuntime(LoadedGraphRuntime? runtime)
        {
            _loadedGraphRuntime = runtime;
            PreflightGraphProjectionCandidateScratch(runtime);
        }

        private void PreflightGraphProjectionCandidateScratch(LoadedGraphRuntime? runtime)
        {
            if (runtime == null || !runtime.HasLoadedGraph)
            {
                _graphProjectionCandidateScratch = Array.Empty<int>();
                return;
            }

            int nodeCount = runtime.CurrentGraph.NodeCount;
            if (nodeCount <= 0)
            {
                _graphProjectionCandidateScratch = Array.Empty<int>();
                return;
            }

            if (_graphProjectionCandidateScratch.Length == nodeCount)
            {
                return;
            }

            // Allocate once when the loaded graph is bound — never grow on the snap hot path.
            _graphProjectionCandidateScratch = new int[nodeCount];
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
            RejectDerivedAttributeSideEffect(nameof(BeginLifecycleTransaction));
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
            RejectDerivedAttributeSideEffect(nameof(InvokeBuiltin));
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
                    builtinHandlerId,
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
            if (_effectSideEffects?.TryHasTag(entity, tagId, out bool stagedHasTag) == true)
            {
                return stagedHasTag;
            }
            if (!_world.IsAlive(entity) || !_world.Has<GameplayTagContainer>(entity)) return false;
            ref var tags = ref _world.Get<GameplayTagContainer>(entity);
            return RequireTagOps().HasTag(ref tags, tagId, TagSense.Effective);
        }

        public bool TryGetAttributeCurrent(Entity entity, int attributeId, out float value)
        {
            if (_effectSideEffects?.TryGetAttributeCurrent(entity, attributeId, out value) == true)
            {
                return true;
            }

            if (_derivedAttributeWritesActive && entity == _derivedAttributeWriteOwner)
            {
                value = _derivedAttributeWriteBuffer.GetCurrent(attributeId);
                return true;
            }

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

        public int CollectActiveEffects(Entity owner, Span<Entity> buffer)
        {
            if (!_world.IsAlive(owner) || !_world.Has<ActiveEffectContainer>(owner))
            {
                return 0;
            }

            ref ActiveEffectContainer container = ref _world.Get<ActiveEffectContainer>(owner);
            int written = 0;
            for (int i = 0; i < container.Count && written < buffer.Length; i++)
            {
                Entity effectEntity = container.GetEntity(i);
                if (!_world.IsAlive(effectEntity))
                {
                    continue;
                }

                buffer[written++] = effectEntity;
            }

            return written;
        }

        public int CollectEffectTemplateIds(Span<int> buffer)
        {
            RegistryMapping[] mappings = EffectTemplateIdRegistry.SnapshotMappings();
            if (mappings.Length == 0 || buffer.IsEmpty)
            {
                return 0;
            }

            Array.Sort(mappings, static (a, b) => a.Id.CompareTo(b.Id));
            int written = 0;
            for (int i = 0; i < mappings.Length && written < buffer.Length; i++)
            {
                if (mappings[i].Id <= 0)
                {
                    continue;
                }

                buffer[written++] = mappings[i].Id;
            }

            return written;
        }

        public int CollectAbilitySlots(Entity owner, Span<int> buffer)
        {
            if (!_world.IsAlive(owner) || buffer.IsEmpty)
            {
                return 0;
            }

            int written = 0;
            for (int slot = 0; slot < AbilityStateBuffer.CAPACITY && written < buffer.Length; slot++)
            {
                if (AbilitySlotResolver.TryResolve(_world, owner, slot, out _))
                {
                    buffer[written++] = slot;
                }
            }

            return written;
        }

        public int CollectInventoryItems(Entity owner, Span<Entity> buffer)
        {
            if (!_world.IsAlive(owner) || buffer.IsEmpty)
            {
                return 0;
            }

            if (_inventory == null)
            {
                throw new InvalidOperationException("GAS.GRAPH.ERR.MissingInventoryRuntime");
            }

            return _inventory.CollectOwnedItemInstances(owner, buffer);
        }

        public int CollectItemDefinitionIds(Span<int> buffer)
        {
            if (_itemDefinitions == null)
            {
                throw new InvalidOperationException("GAS.GRAPH.ERR.MissingItemDefinitionRegistry");
            }

            return _itemDefinitions.CopyRegisteredIds(buffer);
        }

        public int CollectPresentTags(Entity owner, Span<int> buffer)
        {
            if (!_world.IsAlive(owner) || buffer.IsEmpty)
            {
                return 0;
            }

            if (_world.Has<TagCountContainer>(owner))
            {
                ref TagCountContainer counts = ref _world.Get<TagCountContainer>(owner);
                return counts.CopyTagIds(buffer);
            }

            if (!_world.Has<GameplayTagContainer>(owner))
            {
                return 0;
            }

            ref GameplayTagContainer tags = ref _world.Get<GameplayTagContainer>(owner);
            RegistryMapping[] mappings = TagRegistry.SnapshotMappings();
            Array.Sort(mappings, static (left, right) => left.Id.CompareTo(right.Id));
            int written = 0;
            for (int i = 0; i < mappings.Length && written < buffer.Length; i++)
            {
                int tagId = mappings[i].Id;
                if (tagId > 0 && RequireTagOps().HasTag(ref tags, tagId, TagSense.Present))
                {
                    buffer[written++] = tagId;
                }
            }

            return written;
        }

        public int CollectActiveTasks(Entity owner, Span<Entity> buffer)
        {
            if (!_world.IsAlive(owner) || buffer.IsEmpty)
            {
                return 0;
            }

            int written = 0;
            foreach (ref var chunk in _world.Query(in TaskInstanceQuery))
            {
                ref Entity first = ref chunk.Entity(0);
                Span<TaskInstanceCm> tasks = chunk.GetSpan<TaskInstanceCm>();
                foreach (int index in chunk)
                {
                    if (written >= buffer.Length)
                    {
                        return written;
                    }

                    if (tasks[index].ScopeHost == owner &&
                        tasks[index].State == TaskInstanceState.Active)
                    {
                        buffer[written++] = Unsafe.Add(ref first, index);
                    }
                }
            }

            return written;
        }

        public int CollectProgressionNodes(Entity owner, Span<int> buffer)
        {
            if (!_world.IsAlive(owner) ||
                !_world.Has<ProgressionStateBuffer>(owner) ||
                buffer.IsEmpty)
            {
                return 0;
            }

            ref ProgressionStateBuffer state = ref _world.Get<ProgressionStateBuffer>(owner);
            return state.CopyProgressionIds(buffer);
        }

        public int CollectAbilityHolders(int abilityId, ReadOnlySpan<Entity> candidates, Span<Entity> buffer)
        {
            if (abilityId <= 0 || buffer.IsEmpty)
            {
                return 0;
            }

            int written = 0;
            for (int i = 0; i < candidates.Length && written < buffer.Length; i++)
            {
                Entity candidate = candidates[i];
                if (candidate == Entity.Null || !_world.IsAlive(candidate))
                {
                    continue;
                }

                if (AbilitySlotResolver.TryFindAbility(_world, candidate, abilityId, out _))
                {
                    buffer[written++] = candidate;
                }
            }

            return written;
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
            if (_effectSideEffects != null)
            {
                int write = 0;
                for (int i = 0; i < count; i++)
                {
                    Entity entity = entities[i];
                    if (HasTag(entity, tagId))
                    {
                        entities[write++] = entity;
                    }
                }
                return write;
            }

            return RequireEntityQueries().FilterTagAny(entities, count, tagId);
        }

        public int FilterTagNone(Span<Entity> entities, int count, int tagId)
        {
            if (_effectSideEffects != null)
            {
                int write = 0;
                for (int i = 0; i < count; i++)
                {
                    Entity entity = entities[i];
                    if (!HasTag(entity, tagId))
                    {
                        entities[write++] = entity;
                    }
                }
                return write;
            }

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

        public bool TryMinEntityByWorldDistanceCm(ReadOnlySpan<Entity> entities, WorldCmInt2 centerCm, out Entity entity, out long distanceSquaredCm)
        {
            return RequireEntityQueries().TryMinEntityByWorldDistanceCm(entities, centerCm, out entity, out distanceSquaredCm);
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
        public void EnsureRelationshipLink(Entity source, Entity target, int typeId)
        {
            RejectDerivedAttributeSideEffect(nameof(EnsureRelationshipLink));
            RejectNonTransactionalEffectSideEffect(nameof(EnsureRelationshipLink));
            RequireRelationshipRuntime().EnsureLink(source, target, typeId);
        }
        public void RemoveRelationshipLink(Entity source, Entity target, int typeId)
        {
            RejectDerivedAttributeSideEffect(nameof(RemoveRelationshipLink));
            RejectNonTransactionalEffectSideEffect(nameof(RemoveRelationshipLink));
            RequireRelationshipRuntime().RemoveLink(source, target, typeId);
        }
        public short SetRelationshipMetric(Entity source, Entity target, int metricId, int value, int reasonId, int typeId)
        {
            RejectDerivedAttributeSideEffect(nameof(SetRelationshipMetric));
            RejectNonTransactionalEffectSideEffect(nameof(SetRelationshipMetric));
            return RequireRelationshipRuntime().SetMetric(source, target, typeId, metricId, value, reasonId);
        }
        public short AddRelationshipMetric(Entity source, Entity target, int metricId, int delta, int reasonId, int typeId)
        {
            RejectDerivedAttributeSideEffect(nameof(AddRelationshipMetric));
            RejectNonTransactionalEffectSideEffect(nameof(AddRelationshipMetric));
            return RequireRelationshipRuntime().AddMetric(source, target, typeId, metricId, delta, reasonId);
        }
        public short GetRelationshipMetric(Entity source, Entity target, int metricId, int typeId)
            => RequireRelationshipRuntime().GetMetric(source, target, typeId, metricId);
        public bool HasRelationshipFlag(Entity source, Entity target, int flagId, int typeId)
            => RequireRelationshipRuntime().HasFlag(source, target, typeId, flagId);
        public void SetRelationshipFlag(Entity source, Entity target, int flagId, bool enabled, int reasonId, int typeId)
        {
            RejectDerivedAttributeSideEffect(nameof(SetRelationshipFlag));
            RejectNonTransactionalEffectSideEffect(nameof(SetRelationshipFlag));
            RequireRelationshipRuntime().SetFlag(source, target, typeId, flagId, enabled, reasonId);
        }
        public RelationshipQueryResult CollectOutgoing(Entity source, Span<Entity> buffer, int typeId = RelationshipTypeRegistry.AnyTypeId)
        {
            int count = RequireRelationshipRuntime().CollectOutgoing(source, typeId, buffer, out int dropped);
            return new RelationshipQueryResult(count, dropped);
        }
        public RelationshipQueryResult CollectIncoming(Entity target, Span<Entity> buffer, int typeId = RelationshipTypeRegistry.AnyTypeId)
        {
            int count = RequireRelationshipRuntime().CollectIncoming(target, typeId, buffer, out int dropped);
            return new RelationshipQueryResult(count, dropped);
        }
        public RelationshipQueryResult CollectMutual(Entity first, Entity second, Span<Entity> buffer, int typeId = RelationshipTypeRegistry.AnyTypeId)
        {
            int count = RequireRelationshipRuntime().CollectMutual(first, second, typeId, buffer, out int dropped);
            return new RelationshipQueryResult(count, dropped);
        }
        public RelationshipQueryResult CollectBetweenPair(Entity source, Entity target, Span<Entity> buffer, int typeId = RelationshipTypeRegistry.AnyTypeId)
        {
            int count = RequireRelationshipRuntime().CollectBetweenPair(source, target, typeId, buffer, out int dropped);
            return new RelationshipQueryResult(count, dropped);
        }
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
            RejectDerivedAttributeSideEffect(nameof(ApplyEffectTemplate));
            if (_effectSideEffects == null && _effectRequests == null)
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

            if (_effectSideEffects != null)
            {
                _effectSideEffects.StageEffectRequest(in req);
                return;
            }

            _effectRequests!.Publish(req);
        }

        public void FanOutDispatchEffect(Entity source, Entity target, Entity targetContext, ReadOnlySpan<Entity> targets, int templateId, int payloadPresetId)
        {
            RejectDerivedAttributeSideEffect(nameof(FanOutDispatchEffect));
            if (_effectSideEffects == null && _effectRequests == null)
            {
                throw new InvalidOperationException("GAS.GRAPH.ERR.MissingEffectRequestQueue");
            }

            if (templateId <= 0)
            {
                return;
            }

            TargetResolverContextMapping mapping = RequireTargetDispatchPresets().Get(payloadPresetId);
            if (_effectSideEffects != null)
            {
                int rootId = ResolveChildEffectRootId();
                for (int i = 0; i < targets.Length; i++)
                {
                    var command = new FanOutCommand
                    {
                        RootId = rootId,
                        OriginalSource = source,
                        OriginalTarget = target,
                        OriginalTargetContext = targetContext,
                        PayloadEffectTemplateId = templateId,
                        ContextMapping = mapping,
                        ResolvedEntity = targets[i],
                    };
                    _effectSideEffects.StageFanOutCommand(in command);
                }
                return;
            }

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
            RejectDerivedAttributeSideEffect(nameof(RemoveEffectTemplate));
            if (_effectSideEffects != null)
            {
                _effectSideEffects.StageEffectCancellation(target, templateId);
                return;
            }
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
            RejectDerivedAttributeSideEffect(nameof(ModifyAttributeAdd));
            if (_effectSideEffects != null)
            {
                _effectSideEffects.StageAttributeAdd(target, attributeId, delta);
                return;
            }
            AttributeMutationOps.AddCurrent(_world, target, attributeId, delta, RequireTagOps());
        }

        public void ModifyAttributeSet(Entity caster, Entity target, int attributeId, float value)
        {
            if (_effectSideEffects != null)
            {
                _effectSideEffects.StageAttributeSet(target, attributeId, value);
                return;
            }

            if (_derivedAttributeWritesActive)
            {
                if (caster != _derivedAttributeWriteOwner || target != _derivedAttributeWriteOwner)
                {
                    throw new InvalidOperationException(
                        $"GAS.GRAPH.ERR.DerivedAttributeWriteTargetMismatch: owner={_derivedAttributeWriteOwner.Id}, caster={caster.Id}, target={target.Id}.");
                }

                _derivedAttributeWriteBuffer.SetCurrent(attributeId, value);
                return;
            }

            AttributeMutationOps.SetCurrent(_world, target, attributeId, value, RequireTagOps());
        }

        public void SendEvent(Entity caster, Entity target, int eventTagId, float magnitude)
        {
            RejectDerivedAttributeSideEffect(nameof(SendEvent));
            if (_eventBus == null)
            {
                throw new System.InvalidOperationException("GAS.GRAPH.ERR.MissingGameplayEventBus");
            }
            var gameplayEvent = new GameplayEvent
            {
                TagId = eventTagId,
                Source = caster,
                Target = target,
                Magnitude = magnitude
            };
            if (_effectSideEffects != null)
            {
                _effectSideEffects.StageGameplayEvent(_eventBus, in gameplayEvent);
                return;
            }
            _eventBus.Publish(gameplayEvent);
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
            if (_effectSideEffects?.TryReadBlackboardFloat(entity, keyId, out value) == true) return true;
            value = 0f;
            if (!_world.IsAlive(entity) || !_world.Has<BlackboardFloatBuffer>(entity)) return false;
            ref var bb = ref _world.Get<BlackboardFloatBuffer>(entity);
            return bb.TryGet(keyId, out value);
        }

        public bool TryReadBlackboardInt(Entity entity, int keyId, out int value)
        {
            if (_effectSideEffects?.TryReadBlackboardInt(entity, keyId, out value) == true) return true;
            value = 0;
            if (!_world.IsAlive(entity) || !_world.Has<BlackboardIntBuffer>(entity)) return false;
            ref var bb = ref _world.Get<BlackboardIntBuffer>(entity);
            return bb.TryGet(keyId, out value);
        }

        public bool TryReadBlackboardEntity(Entity entity, int keyId, out Entity value)
        {
            if (_effectSideEffects?.TryReadBlackboardEntity(entity, keyId, out value) == true) return true;
            value = default;
            if (!_world.IsAlive(entity) || !_world.Has<BlackboardEntityBuffer>(entity)) return false;
            ref var bb = ref _world.Get<BlackboardEntityBuffer>(entity);
            return bb.TryGet(keyId, out value);
        }

        public void WriteBlackboardFloat(Entity entity, int keyId, float value)
        {
            RejectDerivedAttributeSideEffect(nameof(WriteBlackboardFloat));
            if (_effectSideEffects != null)
            {
                _effectSideEffects.StageBlackboardFloat(entity, keyId, value);
                return;
            }
            RequireBlackboard<BlackboardFloatBuffer>(entity);
            ref var bb = ref _world.Get<BlackboardFloatBuffer>(entity);
            bb.Set(keyId, value);
        }

        public void WriteBlackboardInt(Entity entity, int keyId, int value)
        {
            RejectDerivedAttributeSideEffect(nameof(WriteBlackboardInt));
            if (_effectSideEffects != null)
            {
                _effectSideEffects.StageBlackboardInt(entity, keyId, value);
                return;
            }
            RequireBlackboard<BlackboardIntBuffer>(entity);
            ref var bb = ref _world.Get<BlackboardIntBuffer>(entity);
            bb.Set(keyId, value);
        }

        public void WriteBlackboardEntity(Entity entity, int keyId, Entity value)
        {
            RejectDerivedAttributeSideEffect(nameof(WriteBlackboardEntity));
            if (_effectSideEffects != null)
            {
                _effectSideEffects.StageBlackboardEntity(entity, keyId, value);
                return;
            }
            RequireBlackboard<BlackboardEntityBuffer>(entity);
            ref var bb = ref _world.Get<BlackboardEntityBuffer>(entity);
            bb.Set(keyId, value);
        }

        private void RequireBlackboard<T>(Entity entity)
        {
            if (!_world.IsAlive(entity) || !_world.Has<T>(entity))
            {
                throw new InvalidOperationException(
                    $"{MissingBlackboardError}: entity={entity.Id}, component={typeof(T).Name}.");
            }
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

            int nodeCount = runtime.CurrentGraph.NodeCount;
            if (_graphProjectionCandidateScratch.Length < nodeCount)
            {
                throw new InvalidOperationException(
                    $"Graph edge snap candidate scratch capacity {_graphProjectionCandidateScratch.Length} is below loaded graph node count {nodeCount}. BindLoadedGraphRuntime must preflight capacity before execution.");
            }

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
    }
}
