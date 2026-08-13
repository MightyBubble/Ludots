using System.IO;
using System.Text.Json;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.EntityQueries;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Lifecycle;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Knowledge;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.TagDisplay;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime;

internal sealed class GraphOpsNodeGalleryHost : IDisposable
{
    internal const string SquadCollectionKey = "squad.members";
    internal const string SnapCollectionKey = "showcase.graph_op.snap";
    internal const string TargetToResolvedPreset = "TargetToResolved";
    internal const string DefaultConfigEffectId = "Effect.GraphOps.Config";
    internal const uint AllyLayer = 0b0001;
    internal const uint EnemyLayer = 0b0010;

    private World? _world;
    private bool _ownsWorld;
    private SpatialPartitionUpdateSystem? _spatialPartition;
    private ConfigPipeline? _pipeline;
    private DataRegistry<EntityTemplate>? _templateRegistry;
    private EffectTemplateRegistry _effectTemplates = null!;
    private BuiltinHandlerRegistry _builtinHandlers = null!;
    private BuiltinHandlerExecutionContext _builtinRuntime = null!;
    private int _configEffectTemplateId;

    public World World => _world ?? throw new InvalidOperationException("Gallery host is not bootstrapped.");
    public GasGraphRuntimeApi Api { get; private set; } = null!;
    public MapLoadEntityIndex EntityIndex { get; private set; } = null!;
    public EntityTemplateKeyRegistry Templates { get; private set; } = null!;
    public bool OwnsSimulationWorld => _ownsWorld;
    public RelationshipRuntime Relationships { get; private set; } = null!;
    public RelationshipTypeRegistry RelationshipTypes { get; private set; } = null!;
    public RelationshipMetricRegistry RelationshipMetrics { get; private set; } = null!;
    public RelationshipFlagRegistry RelationshipFlags { get; private set; } = null!;
    public RelationshipReasonRegistry RelationshipReasons { get; private set; } = null!;
    public EntityCollectionStore Collections { get; private set; } = null!;
    public EffectRequestQueue EffectRequests { get; private set; } = null!;
    public TagOps TagOps { get; private set; } = null!;
    public TagDisplayTableRegistry TagDisplay { get; private set; } = null!;
    public TargetDispatchPresetRegistry DispatchPresets { get; private set; } = null!;
    public ISpatialQueryService SpatialQueries { get; private set; } = null!;
    public OwnershipResolver? Ownership { get; private set; }
    public KnowledgeProjectionStore Knowledge { get; private set; } = null!;
    public GameplayEventBus EventBus { get; private set; } = null!;
    public ISpatialCoordinateConverter Coords { get; private set; } = null!;
    public GraphOpsNodeGallerySymbolResolver Resolver { get; private set; } = null!;

    public static GraphOpsNodeGalleryHost FromEngine(GameEngine engine, string assetsRoot, string mapId)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var host = new GraphOpsNodeGalleryHost();
        host.BindEngineServices(engine);
        host.EntityIndex = engine.CurrentMapSession?.EntityIndex
            ?? throw new InvalidOperationException(
                $"Node gallery map '{mapId}' is not loaded. EnsureWorld must run after MapLoaded.");
        host.FinishResolver();
        host.LoadSandboxDisplayTable(assetsRoot);
        return host;
    }

    public static GraphOpsNodeGalleryHost CreateHeadless(string assetsRoot, string mapId)
    {
        var host = new GraphOpsNodeGalleryHost();
        host.BootstrapHeadless(assetsRoot, mapId);
        return host;
    }

    public GraphOpsNodeDriverContext BindContext(
        string assetsRoot,
        GraphOpsNodeVignette vignette,
        GraphControlFlowCompileResult compiled,
        GraphKind kind,
        byte featuredDest,
        GraphShowcaseMetrics metrics,
        GraphOpsStageVisuals? stage)
    {
        Entity[] actors = BindMapActors(vignette, GraphOpsNodeIds.MapId(vignette.Op));
        ApplyVignetteState(vignette, actors);
        ApplyCollections(vignette, actors);
        ApplyLinks(vignette, actors);
        ResolveConfigEffect(vignette);

        var ctx = new GraphOpsNodeDriverContext
        {
            AssetsRoot = assetsRoot,
            Vignette = vignette,
            Compiled = compiled,
            Kind = kind,
            FeaturedDest = featuredDest,
            SimWorld = World,
            Api = Api,
            Metrics = metrics,
            Stage = stage,
            EffectRequests = EffectRequests,
            Relationships = Relationships,
            Collections = Collections,
            TagOps = TagOps,
            TagDisplay = TagDisplay,
            EventBus = EventBus,
            Ownership = Ownership,
            Knowledge = Knowledge,
            Coords = Coords,
            RelationshipTypes = RelationshipTypes,
            RelationshipMetrics = RelationshipMetrics,
            RelationshipFlags = RelationshipFlags,
            BuiltinHandlers = _builtinHandlers,
            EffectTemplates = _effectTemplates,
            BuiltinRuntime = _builtinRuntime,
            ConfigEffectTemplateId = _configEffectTemplateId,
            OwnsSimulationWorld = _ownsWorld
        };
        ctx.SimActors = actors;
        ctx.ActorHealth = new float[actors.Length];
        for (int i = 0; i < actors.Length; i++)
        {
            GraphOpsNodeActorBinding.WriteHealth(
                World,
                actors[i],
                vignette.Actors[i].Health,
                vignette.Actors[i].HealthMax,
                TagOps);
            ctx.ActorHealth[i] = GraphOpsNodeActorBinding.ReadHealth(World, actors[i]);
        }

        GraphOpsNodeActorBinding.BindRolesFromMap(ctx);
        ctx.Metrics.AgentCount = actors.Length;
        ctx.Metrics.Detail = vignette.Beat;
        return ctx;
    }

    public void Dispose()
    {
        if (_ownsWorld)
        {
            _world?.Dispose();
        }

        _world = null;
    }

    private void BindEngineServices(GameEngine engine)
    {
        _world = engine.World;
        _ownsWorld = false;
        SpatialQueries = engine.SpatialQueries
            ?? throw new InvalidOperationException("Node gallery requires engine SpatialQueries.");
        Coords = engine.SpatialCoords
            ?? throw new InvalidOperationException("Node gallery requires engine SpatialCoords.");
        if (SpatialQueries is not SpatialQueryService spatialQueries)
        {
            throw new InvalidOperationException(
                $"Node gallery requires engine SpatialQueries to be {nameof(SpatialQueryService)} so hex queries can bind the live coordinate converter.");
        }

        spatialQueries.SetCoordinateConverter(Coords);
        EventBus = engine.EventBus
            ?? throw new InvalidOperationException("Node gallery requires engine EventBus.");
        EffectRequests = RequireEngineService(engine, CoreServiceKeys.EffectRequestQueue);
        TagOps = RequireEngineService(engine, CoreServiceKeys.TagOps);
        Relationships = RequireEngineService(engine, CoreServiceKeys.RelationshipRuntime);
        RelationshipTypes = RequireEngineService(engine, CoreServiceKeys.RelationshipTypeRegistry);
        RelationshipMetrics = RequireEngineService(engine, CoreServiceKeys.RelationshipMetricRegistry);
        RelationshipFlags = RequireEngineService(engine, CoreServiceKeys.RelationshipFlagRegistry);
        RelationshipReasons = RequireEngineService(engine, CoreServiceKeys.RelationshipReasonRegistry);
        DispatchPresets = RequireEngineService(engine, CoreServiceKeys.TargetDispatchPresetRegistry);
        Collections = RequireEngineService(engine, CoreServiceKeys.EntityCollectionStore);
        Knowledge = RequireEngineService(engine, CoreServiceKeys.KnowledgeProjectionStore);
        Templates = RequireEngineService(engine, CoreServiceKeys.EntityTemplateKeyRegistry);
        _templateRegistry = engine.MapLoader.TemplateRegistry;
        TagDisplay = RequireEngineService(engine, CoreServiceKeys.TagDisplayTableRegistry);
        _effectTemplates = RequireEngineService(engine, CoreServiceKeys.EffectTemplateRegistry);
        Api = RequireEngineService(engine, CoreServiceKeys.GasGraphRuntimeApi);
        EnsureGalleryRelationshipCatalog();
        EnsureDispatchPreset();
        RegisterCollectionKeys();
        int ownsType = RelationshipTypes.Register("Owns");
        Ownership = new OwnershipResolver(Relationships, ownsType);
        BindLifecycleServices(RequireEngineService(engine, CoreServiceKeys.PresentationStableIdAllocator));
    }

    private void BootstrapHeadless(string assetsRoot, string mapId)
    {
        _world = World.Create();
        _ownsWorld = true;
        var vfs = new VirtualFileSystem();
        vfs.Mount("Core", assetsRoot);
        var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
        _pipeline = new ConfigPipeline(vfs, modLoader);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(_pipeline);
        var mapLoader = new MapLoader(World, new WorldMap(), _pipeline);
        mapLoader.LoadTemplates(catalog);
        Templates = mapLoader.EntityTemplateKeys;
        _templateRegistry = mapLoader.TemplateRegistry;

        MapConfig map = LoadMapConfig(assetsRoot, mapId);
        EntityIndex = mapLoader.LoadEntitiesAndIndex(map);

        var extent = new WorldExtentSpec(
            SpatialScaleDefaults.DefaultWorldWidthMacroTiles,
            SpatialScaleDefaults.DefaultWorldHeightMacroTiles,
            cellCm: 100);
        WorldSizeSpec spec = extent.ToWorldSizeSpec();
        var partition = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 64);
        var spatial = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, spec));
        var coords = new SpatialCoordinateConverter(spec);
        spatial.SetCoordinateConverter(coords);
        World world = World;
        spatial.SetPositionProvider(entity =>
        {
            if (!world.IsAlive(entity) || !world.Has<WorldPositionCm>(entity))
            {
                throw new InvalidOperationException(
                    $"Map entity {entity.Id} is missing WorldPositionCm; MapLoader must spawn people with positions.");
            }

            return world.Get<WorldPositionCm>(entity).Value.ToWorldCmInt2();
        });
        SpatialQueries = spatial;
        Coords = coords;
        _spatialPartition = new SpatialPartitionUpdateSystem(World, partition, spec);
        _spatialPartition.Update(0f);

        EffectRequests = new EffectRequestQueue();
        EventBus = new GameplayEventBus();
        TagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry(), new GasBudget());
        RelationshipTypes = new RelationshipTypeRegistry();
        RelationshipMetrics = new RelationshipMetricRegistry();
        RelationshipFlags = new RelationshipFlagRegistry();
        RelationshipReasons = new RelationshipReasonRegistry();
        EnsureGalleryRelationshipCatalog();
        Relationships = new RelationshipRuntime(
            World,
            RelationshipTypes,
            RelationshipMetrics,
            RelationshipFlags,
            new RelationshipBandRegistry(),
            new RelationshipChangeBuffer(),
            new RelationshipReverseIndex(World));
        var collectionKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
        Collections = new EntityCollectionStore(collectionKeys);
        RegisterCollectionKeys();
        DispatchPresets = new TargetDispatchPresetRegistry();
        EnsureDispatchPreset();
        TagDisplay = new TagDisplayTableRegistry();
        LoadSandboxDisplayTable(assetsRoot);
        var entityQueries = new EntitySetQueryRuntime(World, TagOps, Relationships);
        int ownsType = RelationshipTypes.Register("Owns");
        int controlsType = RelationshipTypes.Register("Controls");
        Ownership = new OwnershipResolver(Relationships, ownsType);
        var controlDomains = new ControlDomainQuery(World, Relationships, Ownership, ownsType, controlsType);
        Knowledge = new KnowledgeProjectionStore();
        var knowledge = new KnowledgeProjectionResolver(Knowledge);
        var clock = new DiscreteClock();
        EffectParamKeys.Initialize();
        _effectTemplates = new EffectTemplateRegistry();
        var effectLoader = new EffectTemplateLoader(
            _pipeline,
            _effectTemplates,
            targetDispatchPresets: DispatchPresets,
            entityTemplateKeys: Templates,
            relationshipTypes: RelationshipTypes);
        effectLoader.Load(catalog);
        BindLifecycleServices(new PresentationStableIdAllocator());
        Api = GasGraphRuntimeApi.CreateProduction(new GasGraphRuntimeProductionServices(
            World,
            SpatialQueries,
            Coords,
            EventBus,
            EffectRequests,
            TagOps,
            Relationships,
            RelationshipTypes,
            RelationshipMetrics,
            RelationshipFlags,
            RelationshipReasons,
            DispatchPresets,
            Collections,
            entityQueries,
            controlDomains,
            knowledge,
            clock,
            TagDisplay));
        FinishResolver();
        TeamManager.SetRelationship(1, 2, TeamRelationship.Hostile);
        TeamManager.SetRelationship(2, 1, TeamRelationship.Hostile);
    }

    private void FinishResolver()
    {
        Resolver = new GraphOpsNodeGallerySymbolResolver(
            Templates,
            RelationshipTypes,
            RelationshipMetrics,
            RelationshipFlags,
            RelationshipReasons,
            DispatchPresets,
            TagDisplay);
    }

    private Entity[] BindMapActors(GraphOpsNodeVignette vignette, string mapId)
    {
        var actors = new Entity[vignette.Actors.Length];
        for (int i = 0; i < vignette.Actors.Length; i++)
        {
            GraphOpsNodeActor actor = vignette.Actors[i];
            if (!EntityIndex.TryGet(actor.Id, out Entity entity))
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' is missing InstanceId '{actor.Id}' for gallery '{vignette.Op}'. Maps must be generated from vignette actors.");
            }

            actors[i] = entity;
        }

        return actors;
    }

    private void ApplyVignetteState(GraphOpsNodeVignette vignette, Entity[] actors)
    {
        int healthId = GraphOpsNodeActorBinding.HealthAttributeId();
        _ = healthId;
        for (int i = 0; i < actors.Length; i++)
        {
            Entity entity = actors[i];
            GraphOpsNodeActor actor = vignette.Actors[i];
            EnsureComponent(entity, new BlackboardFloatBuffer());
            EnsureComponent(entity, new BlackboardIntBuffer());
            EnsureComponent(entity, new BlackboardEntityBuffer());
            EnsureComponent(entity, new DirtyFlags());
            TagStateInstaller.EnsureInstalled(World, entity);
            if (string.Equals(actor.Role, "target", StringComparison.Ordinal) ||
                string.Equals(actor.Role, "caster", StringComparison.Ordinal))
            {
                EnsureComponent(entity, new ActiveEffectContainer());
            }

            if (string.Equals(actor.Role, "caster", StringComparison.Ordinal))
            {
                EnsureComponent(entity, new PlayerIdentity { PlayerId = 1 });
            }

            if (actor.Team != 0)
            {
                if (World.Has<Team>(entity))
                {
                    World.Get<Team>(entity).Id = actor.Team;
                }
                else
                {
                    World.Add(entity, new Team { Id = actor.Team });
                }
            }

            uint layer = actor.Team == 1 ? AllyLayer : EnemyLayer;
            if (string.Equals(actor.Role, "caster", StringComparison.Ordinal) ||
                string.Equals(actor.Role, "ally", StringComparison.Ordinal) ||
                string.Equals(actor.Role, "friend", StringComparison.Ordinal))
            {
                layer = AllyLayer;
            }

            if (World.Has<EntityLayer>(entity))
            {
                ref EntityLayer layerRef = ref World.Get<EntityLayer>(entity);
                layerRef = new EntityLayer(layer, uint.MaxValue);
            }
            else
            {
                World.Add(entity, new EntityLayer(layer, uint.MaxValue));
            }

            if (actor.Tags is { Length: > 0 })
            {
                if (!World.Has<GameplayTagContainer>(entity))
                {
                    World.Add(entity, new GameplayTagContainer());
                }

                ref GameplayTagContainer tags = ref World.Get<GameplayTagContainer>(entity);
                for (int t = 0; t < actor.Tags.Length; t++)
                {
                    int tagId = TagRegistry.Register(actor.Tags[t]);
                    tags.AddTag(tagId);
                }
            }
        }
    }

    private void ApplyCollections(GraphOpsNodeVignette vignette, Entity[] actors)
    {
        if (vignette.Collections.Length == 0)
        {
            return;
        }

        Entity caster = RequireCaster(vignette, actors);
        for (int i = 0; i < vignette.Collections.Length; i++)
        {
            GraphOpsNodeCollection collection = vignette.Collections[i];
            _ = Collections.KeyRegistry.Register(collection.Key);
            var members = new Entity[collection.Members.Length];
            for (int m = 0; m < collection.Members.Length; m++)
            {
                members[m] = RequireActor(vignette, actors, collection.Members[m]);
            }

            var descriptor = EntityCollectionDescriptor.Create(
                collection.Key,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.Display,
                contextEntity: caster,
                primaryEntity: caster,
                title: collection.Key,
                summary: collection.Key);
            Collections.Replace(caster, descriptor, members, caster);
        }
    }

    private void ApplyLinks(GraphOpsNodeVignette vignette, Entity[] actors)
    {
        for (int i = 0; i < vignette.Links.Length; i++)
        {
            GraphOpsNodeLink link = vignette.Links[i];
            Entity from = RequireActor(vignette, actors, link.From);
            Entity to = RequireActor(vignette, actors, link.To);
            int typeId = RelationshipTypes.Register(link.Type);
            Relationships.EnsureLink(from, to, typeId);
            if (!string.IsNullOrWhiteSpace(link.Metric))
            {
                int metricId = RelationshipMetrics.Register(link.Metric, -100, 100, 0);
                Relationships.SetMetric(from, to, typeId, metricId, link.MetricValue, reasonId: 0);
            }

            if (link.Flags == null)
            {
                continue;
            }

            for (int f = 0; f < link.Flags.Length; f++)
            {
                int flagId = RelationshipFlags.Register(link.Flags[f]);
                Relationships.SetFlag(from, to, typeId, flagId, true);
            }
        }
    }

    private void ResolveConfigEffect(GraphOpsNodeVignette vignette)
    {
        string effectId = string.IsNullOrWhiteSpace(vignette.ConfigEffectId)
            ? DefaultConfigEffectId
            : vignette.ConfigEffectId;
        int templateId = EffectTemplateIdRegistry.GetId(effectId);
        if (templateId <= 0 || !_effectTemplates.TryGet(templateId, out EffectTemplateData data))
        {
            throw new InvalidOperationException(
                $"Gallery effect '{effectId}' is not loaded. Headless hosts must run EffectTemplateLoader; playable hosts must use the engine registry.");
        }

        if (data.ConfigParams.Count <= 0)
        {
            throw new InvalidOperationException($"Effect '{effectId}' requires configParams.");
        }

        _configEffectTemplateId = templateId;
    }

    private void BindLifecycleServices(PresentationStableIdAllocator stableIds)
    {
        DataRegistry<EntityTemplate> templates = _templateRegistry
            ?? throw new InvalidOperationException("Gallery host requires MapLoader entity templates before lifecycle services.");
        _builtinHandlers = new BuiltinHandlerRegistry();
        BuiltinHandlers.RegisterAll(_builtinHandlers);
        var lifecycleServices = new EntityLifecycleRuntimeServices(
            World,
            templates,
            Templates,
            stableIds,
            TagOps);
        _builtinRuntime = new BuiltinHandlerExecutionContext
        {
            LifecycleServices = lifecycleServices,
            TagOps = TagOps,
            Relationships = Relationships,
            SpatialQueries = SpatialQueries
        };
    }

    private void LoadSandboxDisplayTable(string assetsRoot)
    {
        GraphOpsNodeGallerySymbolResolver.BindSandboxDisplayTable(TagDisplay, assetsRoot);
        if (_ownsWorld && !TagDisplay.IsFrozen)
        {
            TagDisplay.Freeze();
        }
    }

    private void EnsureGalleryRelationshipCatalog()
    {
        _ = RelationshipTypes.Register("SocialBond");
        _ = RelationshipTypes.Register("Owns");
        _ = RelationshipTypes.Register("Controls");
        _ = RelationshipTypes.Register("MemberOf");
        _ = RelationshipMetrics.Register("Loyalty", -100, 100, 0);
        _ = RelationshipFlags.Register("Trusted");
        _ = RelationshipFlags.Register("Estranged");
        _ = RelationshipReasons.Register("Scenario.Setup");
    }

    private void EnsureDispatchPreset()
    {
        if (DispatchPresets.TryGetId(TargetToResolvedPreset, out _))
        {
            return;
        }

        DispatchPresets.Register(
            TargetToResolvedPreset,
            new TargetResolverContextMapping
            {
                PayloadSource = ContextSlot.OriginalTarget,
                PayloadTarget = ContextSlot.ResolvedEntity,
                PayloadTargetContext = ContextSlot.OriginalSource
            });
    }

    private void RegisterCollectionKeys()
    {
        _ = Collections.KeyRegistry.Register(SquadCollectionKey);
        _ = Collections.KeyRegistry.Register(SnapCollectionKey);
    }

    private void EnsureComponent<T>(Entity entity, T component)
        where T : struct
    {
        if (!World.Has<T>(entity))
        {
            World.Add(entity, component);
        }
    }

    private static T RequireEngineService<T>(GameEngine engine, ServiceKey<T> key)
        where T : class
    {
        return engine.GetService(key)
            ?? throw new InvalidOperationException($"Node gallery requires engine service '{key.Name}'.");
    }

    private static Entity RequireCaster(GraphOpsNodeVignette vignette, Entity[] actors)
    {
        int index = GraphOpsNodeActorBinding.FindRole(vignette, "caster");
        if (index < 0)
        {
            throw new InvalidOperationException($"Gallery '{vignette.Op}' requires a caster actor.");
        }

        return actors[index];
    }

    private static Entity RequireActor(GraphOpsNodeVignette vignette, Entity[] actors, string id)
    {
        for (int i = 0; i < vignette.Actors.Length; i++)
        {
            if (string.Equals(vignette.Actors[i].Id, id, StringComparison.Ordinal))
            {
                return actors[i];
            }
        }

        throw new InvalidOperationException($"Gallery '{vignette.Op}' unknown actor '{id}'.");
    }

    private static MapConfig LoadMapConfig(string assetsRoot, string mapId)
    {
        string path = Path.Combine(assetsRoot, "Maps", mapId + ".json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Gallery map missing: {path}", path);
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        MapConfig? map = JsonSerializer.Deserialize<MapConfig>(File.ReadAllText(path), options);
        if (map == null || string.IsNullOrWhiteSpace(map.Id))
        {
            throw new InvalidOperationException($"Map '{path}' deserialized to an empty MapConfig.");
        }

        if (map.Entities == null || map.Entities.Count == 0)
        {
            throw new InvalidOperationException(
                $"Map '{map.Id}' has no Entities. Per-op galleries must spawn people through MapLoader.");
        }

        return map;
    }
}
