using System.IO;
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

    private const int SettlementTickLimit = 8;

    private World? _world;
    private bool _ownsWorld;
    private GameEngine? _ownedEngine;
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
        public TargetDispatchPresetRegistry DispatchPresets { get; private set; } = null!;
    public ISpatialQueryService SpatialQueries { get; private set; } = null!;
    public OwnershipResolver? Ownership { get; private set; }
    public KnowledgeProjectionStore Knowledge { get; private set; } = null!;
    public GameplayEventBus EventBus { get; private set; } = null!;
    public GraphCallbackService GraphCallbacks { get; private set; } = null!;
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
        host.FinishResolver(
            Path.Combine(assetsRoot, "GraphTables"),
            engine.GetService(CoreServiceKeys.RngPickService),
            engine.GetService(CoreServiceKeys.PresentationTextCatalog));
        GraphOpsNodeGallerySymbolResolver.RegisterAuthoredCompileSymbols(assetsRoot);
        return host;
    }

    public static GraphOpsNodeGalleryHost CreateHeadless(string assetsRoot, string mapId)
    {
        string repoRoot = GraphOpsHeadlessGameEngine.FindRepoRoot(assetsRoot);
        GameEngine engine = GraphOpsHeadlessGameEngine.SharedGallery(repoRoot);
        GraphOpsHeadlessGameEngine.LoadExclusiveMap(engine, mapId);
        var host = FromEngine(engine, assetsRoot, mapId);
        host._ownedEngine = engine;
        return host;
    }

    /// <summary>
    /// Advances the headless-owned engine until the registered production
    /// EffectProcessingLoopSystem (plus AttributeCalculation) closes its slice and drains
    /// EffectRequests. Engine ticks are cooperative (4ms budget per frame), so a single tick
    /// can leave the settlement transaction open — swapping maps then would orphan half-settled
    /// effects. No-op when the gallery runs inside an externally ticked engine: that engine's
    /// own loop settles the queue, and ticking it here would double-settle.
    /// </summary>
    public void SettleEffectRequests()
    {
        if (_ownedEngine == null)
        {
            return;
        }

        for (int tick = 0; EffectSettlementOpen(); tick++)
        {
            if (tick >= SettlementTickLimit)
            {
                throw new InvalidOperationException(
                    $"Gallery effect settlement did not close within {SettlementTickLimit} engine ticks "
                    + $"(pendingRequests={EffectRequests.Count}).");
            }

            _ownedEngine.Tick(Time.FixedDeltaTime);
        }
    }

    private bool EffectSettlementOpen()
    {
        if (EffectRequests.Count > 0)
        {
            return true;
        }

        var stateQuery = new QueryDescription().WithAll<GasRuntimeState>();
        bool loopInSlice = false;
        World.Query(in stateQuery, (Entity _, ref GasRuntimeState state) => loopInSlice |= state.EffectLoopInSlice);
        return loopInSlice;
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
            EventBus = EventBus,
            GraphCallbacks = GraphCallbacks,
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
        ctx.ActorHudLit = new bool[actors.Length];
        Array.Fill(ctx.ActorHudLit, true);
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
        GraphCallbacks = RequireEngineService(engine, CoreServiceKeys.GraphCallbackService);
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
        _effectTemplates = RequireEngineService(engine, CoreServiceKeys.EffectTemplateRegistry);
        Api = RequireEngineService(engine, CoreServiceKeys.GasGraphRuntimeApi);
        EnsureGalleryRelationshipCatalog();
        EnsureDispatchPreset();
        RegisterCollectionKeys();
        int ownsType = RelationshipTypes.Register("Owns");
        Ownership = new OwnershipResolver(Relationships, ownsType);
        BindLifecycleServices(RequireEngineService(engine, CoreServiceKeys.PresentationStableIdAllocator));
        EnsureHostileCasterAndEnemyTeams();
    }

    private static void EnsureHostileCasterAndEnemyTeams()
    {
        TeamManager.SetRelationship(1, 2, TeamRelationship.Hostile);
        TeamManager.SetRelationship(2, 1, TeamRelationship.Hostile);
    }

    private void FinishResolver(
        string? graphTablesDir,
        Ludots.Core.Gameplay.Rng.RngPickService? rngPicks = null,
        Ludots.Core.Presentation.Hud.PresentationTextCatalog? presentationTextCatalog = null)
    {
        Resolver = new GraphOpsNodeGallerySymbolResolver(
            Templates,
            RelationshipTypes,
            RelationshipMetrics,
            RelationshipFlags,
            RelationshipReasons,
            DispatchPresets,
            graphTablesDir == null ? null : GraphOpsNodeGallerySymbolResolver.LoadLookupTables(graphTablesDir),
            rngPicks,
            presentationTextCatalog);
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
}
