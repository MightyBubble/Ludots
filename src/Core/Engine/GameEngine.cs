using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Config;
using Arch.Core;
using Ludots.Core.Map;
using Ludots.Core.Map.Hex;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.Camera;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.GAS.Bindings;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Schedulers; // Added for JobScheduler
using Ludots.Core.Systems;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Physics;
using Ludots.Core.Gameplay.GAS; // Added for GameplayEventBus
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Selection;
using Ludots.Core.Input.Systems;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Gameplay.GAS.Presentation;
// Indicators directory removed — unified into Performers
using Ludots.Core.Presentation.Performers;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Spatial;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Components;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.AOI;
using Ludots.Core.Engine.Navigation2D;
using Ludots.Core.Diagnostics;
using Ludots.Core.Map.Board;
using Ludots.Core.Gameplay.Camera.FollowTargets;
using Ludots.Core.Registry;

namespace Ludots.Core.Engine
{
    public enum SystemGroup
    {
        // Phase 0: Schema更新（运行时注册：属性/Graph等）
        // 说明：为保证确定性，运行时schema变更通过队列提交，在每帧开始统一生效
        SchemaUpdate,
        
        // Phase 1: 输入与状态收集
        InputCollection,

        // Phase 1.5: 移动后同步与空间更新（物理/导航输出落地后的 SSOT 更新）
        PostMovement,
        
        // Phase 2: 能力激活
        AbilityActivation,
        
        // Phase 3: Effect处理（含响应链）
        EffectProcessing,
        
        // Phase 4: 属性计算
        AttributeCalculation,
        
        // Phase 5: 延迟触发器收集
        DeferredTriggerCollection,
        
        // Phase 6: 清理
        Cleanup,
        
        // Phase 7: 事件分发
        EventDispatch,
        
        // Phase 7.1: 表现层标记清理
        // 目的：清理 EffectiveChangedBitset 等仅服务于 UI/表现层的脏标记位
        ClearPresentationFlags,
    }

    public class GameEngine : IDisposable // Implement IDisposable
    {
        private const int PrimitiveDrawBufferCapacity = 8192;
        private const int VisualSnapshotBufferCapacity = 131072;

        private bool _isRunning;
        private EffectTemplateLoader _effectTemplateLoader;
        private GraphProgramLoader _graphProgramLoader;
        private ICooperativeSimulation _cooperativeSimulation;
        private bool _simulationBudgetFused;

        public int SimulationBudgetMsPerFrame { get; set; } = 4;
        public int SimulationMaxSlicesPerLogicFrame { get; set; } = 120;
        
        // Time Control
        public IPacemaker Pacemaker { get; set; } = new RealtimePacemaker();

        // Infrastructure
        public IVirtualFileSystem VFS { get; private set; }
        public FunctionRegistry FunctionRegistry { get; private set; }
        public TriggerManager TriggerManager { get; private set; }
        public ModLoader ModLoader { get; private set; }
        public IMapManager MapManager { get; private set; }
        public ConfigPipeline ConfigPipeline { get; private set; }
        public MapLoader MapLoader { get; private set; }
        public SystemFactoryRegistry SystemFactoryRegistry { get; private set; }
        public TriggerDecoratorRegistry TriggerDecoratorRegistry { get; private set; }
        
        // Game State
        public World World { get; private set; }
        public WorldMap WorldMap { get; private set; }
        public VertexMap VertexMap { get; private set; }
        public PhysicsWorld PhysicsWorld { get; private set; }
        public GameSession GameSession { get; private set; }
        public WorldSizeSpec WorldSizeSpec { get; private set; }
        public ISpatialCoordinateConverter SpatialCoords { get; private set; }
        public ISpatialQueryService SpatialQueries { get; private set; }

        // Board infrastructure
        public MapSessionManager MapSessions { get; private set; }
        public BoardIdRegistry BoardIdRegistry { get; private set; }

        private ChunkedGridSpatialPartitionWorld _spatialPartition;
        public HexGridAOI HexGridAOI { get; private set; }
        private static readonly QueryDescription _mapEntitySuspendQuery = new QueryDescription().WithAll<MapEntity>();
        
        // GAS
        public GameplayEventBus EventBus { get; private set; } // Added EventBus

        public Dictionary<string, object> GlobalContext { get; } = new Dictionary<string, object>();

        public void SetService<T>(ServiceKey<T> key, T value)
            => GlobalContext[key.Name] = value;

        public T GetService<T>(ServiceKey<T> key)
            => GlobalContext.TryGetValue(key.Name, out var v) && v is T t ? t : default;

        public GameSynchronizationContext SyncContext { get; private set; }

        // Systems - 按Phase分组
        private Dictionary<SystemGroup, List<ISystem<float>>> _systemGroups = new Dictionary<SystemGroup, List<ISystem<float>>>();
        private List<ISystem<float>> _presentationSystems = new List<ISystem<float>>();
        private ISystem<float> _inputRuntimeSystem;
        private Ludots.Core.Presentation.Rendering.PrimitiveDrawBuffer _primitiveDrawBuffer;
        private Ludots.Core.Presentation.Rendering.PrimitiveDrawBuffer _visualSnapshotBuffer;
        private GasPresentationEventBuffer _gasPresentationEvents;
        private Ludots.Core.Presentation.Rendering.GroundOverlayBuffer _groundOverlayBuffer;
        private Ludots.Core.Presentation.Hud.WorldHudBatchBuffer _worldHudBuffer;
        private Physics2DController _physics2DController;
        private Ludots.Core.Gameplay.GAS.GasController _gasController;

        // Spatial systems — kept for hot-swap on map load
        private WorldToGridSyncSystem _worldToGridSyncSystem;
        private SpatialPartitionUpdateSystem _spatialPartitionUpdateSystem;

        // Multithreading
        private JobScheduler _jobScheduler;

        /// <summary>
        /// The final merged GameConfig from all sources (Core + Mods).
        /// Available after InitializeWithConfigPipeline is called.
        /// </summary>
        public GameConfig MergedConfig { get; private set; }

        public void RegisterSystem(ISystem<float> system, SystemGroup group)
        {
            if (!_systemGroups.ContainsKey(group))
            {
                _systemGroups[group] = new List<ISystem<float>>();
            }
            _systemGroups[group].Add(system);
            
            system.Initialize();
        }

        public void RegisterPresentationSystem(ISystem<float> system)
        {
            _presentationSystems.Add(system);
            system.Initialize();
        }

        public ScriptContext CreateContext()
        {
            var ctx = new ScriptContext();
            ctx.Set(CoreServiceKeys.World, World);
            ctx.Set(CoreServiceKeys.WorldMap, WorldMap);
            ctx.Set(CoreServiceKeys.VertexMap, VertexMap);
            ctx.Set(CoreServiceKeys.GameSession, GameSession);
            ctx.Set(CoreServiceKeys.Engine, this);
            ctx.Set(CoreServiceKeys.WorldSizeSpec, WorldSizeSpec);
            ctx.Set(CoreServiceKeys.SpatialCoordinateConverter, SpatialCoords);
            ctx.Set(CoreServiceKeys.SpatialQueryService, SpatialQueries);

            foreach (var kvp in GlobalContext)
            {
                ctx.Set(kvp.Key, kvp.Value);
            }

            return ctx;
        }

        /// <summary>
        /// New initialization method using ConfigPipeline to merge game.json from all sources.
        /// This is the recommended initialization path.
        /// </summary>
        public RegistrationConflictReport ConflictReport { get; private set; }
        public Ludots.Core.Config.ConfigConflictReport ConfigConflictReport { get; private set; }
        public Ludots.Core.Config.ConfigCatalog ConfigCatalog { get; private set; }
        public Ludots.Core.Gameplay.AI.Config.AiCompiledRuntime AiRuntime { get; private set; }

        public void InitializeWithConfigPipeline(List<string> modPaths, string assetsRoot)
        {
            // Early log bootstrap with console backend — will be upgraded after config merge
            if (Diagnostics.Log.Backend is NullLogBackend)
                Diagnostics.Log.Initialize(new ConsoleLogBackend());
            Diagnostics.Log.Info(in LogChannels.Engine, "Initializing with ConfigPipeline...");

            // Setup Async Context
            SyncContext = new GameSynchronizationContext();
            System.Threading.SynchronizationContext.SetSynchronizationContext(SyncContext);

            // Setup conflict report for mod registration tracing
            ConflictReport = new RegistrationConflictReport();
            Ludots.Core.Config.ComponentRegistry.SetConflictReport(ConflictReport);

            // 1. Setup Infrastructure (VFS, ModLoader)
            VFS = new VirtualFileSystem();
            VFS.Mount("Core", assetsRoot); // Mount Core Assets

            FunctionRegistry = new FunctionRegistry();
            FunctionRegistry.SetConflictReport(ConflictReport);
            TriggerManager = new TriggerManager();
            SystemFactoryRegistry = new SystemFactoryRegistry();
            TriggerDecoratorRegistry = new TriggerDecoratorRegistry();
            ModLoader = new ModLoader(VFS, FunctionRegistry, TriggerManager, SystemFactoryRegistry, TriggerDecoratorRegistry);
            MapManager = new MapManager(VFS, TriggerManager, ModLoader);
            ModLoader.MapManager = MapManager;
            SetService(CoreServiceKeys.SystemFactoryRegistry, SystemFactoryRegistry);
            SetService(CoreServiceKeys.TriggerDecoratorRegistry, TriggerDecoratorRegistry);

            // 2. Load Mods first (so ConfigPipeline can access their game.json)
            if (modPaths != null && modPaths.Count > 0)
            {
                ModLoader.LoadMods(modPaths);
            }
            
            // 3. Create ConfigPipeline and merge all game.json files
            ConfigPipeline = new ConfigPipeline((VirtualFileSystem)VFS, ModLoader);
            ((MapManager)MapManager).SetConfigPipeline(ConfigPipeline);
            MergedConfig = ConfigPipeline.MergeGameConfig();

            ConfigCatalog = Ludots.Core.Config.ConfigCatalogLoader.Load(ConfigPipeline);
            ConfigConflictReport = new Ludots.Core.Config.ConfigConflictReport();
            RebuildAiRuntime();

            // Apply log config from merged game.json
            LogConfigApplier.Apply(MergedConfig.Logging);

            Diagnostics.Log.Info(in LogChannels.Engine, $"Merged GameConfig: StartupMapId={MergedConfig.StartupMapId}, DefaultCoreMod={MergedConfig.DefaultCoreMod}");
            Diagnostics.Log.Info(in LogChannels.Engine, $"Constants loaded: OrderTypeIds={MergedConfig.Constants.OrderTypeIds.Count}, ResponseChainOrderTypeIds={MergedConfig.Constants.ResponseChainOrderTypeIds.Count}");
            
            // Store merged config in GlobalContext for access throughout the engine
            SetService(CoreServiceKeys.GameConfig, MergedConfig);
            SetService(CoreServiceKeys.ConfigCatalog, ConfigCatalog);
            SetService(CoreServiceKeys.ConfigConflictReport, ConfigConflictReport);
            SetService(CoreServiceKeys.AiRuntime, AiRuntime);

            // 4. Setup ECS & Session using merged config values
            InitializeWorld(MergedConfig.WorldWidthInTiles, MergedConfig.WorldHeightInTiles);
            WorldMap = new WorldMap(MergedConfig.WorldWidthInTiles, MergedConfig.WorldHeightInTiles);
            GameSession = new GameSession();
            SetService(CoreServiceKeys.GameSession, GameSession);
            int gridCellSizeCm = MergedConfig.GridCellSizeCm;
            int worldWidthCm = WorldMap.TotalWidth * gridCellSizeCm;
            int worldHeightCm = WorldMap.TotalHeight * gridCellSizeCm;
            WorldSizeSpec = new WorldSizeSpec(
                new WorldAabbCm(-worldWidthCm / 2, -worldHeightCm / 2, worldWidthCm, worldHeightCm),
                gridCellSizeCm: gridCellSizeCm);
            SpatialCoords = new SpatialCoordinateConverter(WorldSizeSpec);
            _spatialPartition = new ChunkedGridSpatialPartitionWorld(chunkSizeCells: 64);
            SpatialQueries = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(_spatialPartition, WorldSizeSpec));
            WireUpPositionProvider();
            SetService(CoreServiceKeys.WorldSizeSpec, WorldSizeSpec);
            SetService(CoreServiceKeys.SpatialCoordinateConverter, SpatialCoords);
            SetService(CoreServiceKeys.SpatialQueryService, SpatialQueries);

            // 4b. Create HexGridAOI as ILoadedChunks SSOT
            HexGridAOI = new HexGridAOI();
            SetService(CoreServiceKeys.LoadedChunks, (ILoadedChunks)HexGridAOI);

            // 5. Setup Data Loaders
            MapLoader = new MapLoader(World, WorldMap, ConfigPipeline);

            // 6. Initialize Core Systems with merged config
            InitializeCoreSystems(MergedConfig);

            TriggerManager.RegisterTrigger(new Ludots.Core.Config.ReloadConfigTrigger(this));

            SimulationBudgetMsPerFrame = MergedConfig.SimulationBudgetMsPerFrame;
            SimulationMaxSlicesPerLogicFrame = MergedConfig.SimulationMaxSlicesPerLogicFrame;
            
            // 7. Post-Mod Load Initialization
            MapLoader.LoadTemplates();

            // 8. Print registration conflict summary
            ConflictReport?.PrintSummary();
        }

        public void RebuildAiRuntime()
        {
            if (ConfigPipeline == null)
            {
                AiRuntime = default;
                return;
            }

            var atoms = new Ludots.Core.Gameplay.AI.WorldState.AtomRegistry(capacity: 256);
            var loader = new Ludots.Core.Gameplay.AI.Config.AiConfigLoader(ConfigPipeline, atoms);
            var catalog = ConfigCatalog ?? Ludots.Core.Gameplay.AI.Config.AiConfigCatalog.CreateDefault();
            AiRuntime = loader.LoadAndCompile(catalog, ConfigConflictReport);
        }

        public void ReloadConfigs(string? group = null, string? relativePath = null)
        {
            if (ConfigPipeline == null) return;

            ConfigCatalog = Ludots.Core.Config.ConfigCatalogLoader.Load(ConfigPipeline);
            ConfigConflictReport = new Ludots.Core.Config.ConfigConflictReport();

            bool reloadAi = string.IsNullOrWhiteSpace(group)
                         || string.Equals(group, "AI", StringComparison.OrdinalIgnoreCase)
                         || (!string.IsNullOrWhiteSpace(relativePath) && relativePath.StartsWith("AI/", StringComparison.OrdinalIgnoreCase));

            if (reloadAi) RebuildAiRuntime();

            SetService(CoreServiceKeys.ConfigCatalog, ConfigCatalog);
            SetService(CoreServiceKeys.ConfigConflictReport, ConfigConflictReport);
            SetService(CoreServiceKeys.AiRuntime, AiRuntime);
        }

        private void InitializeWorld(int widthInTiles, int heightInTiles)
        {
            World = World.Create();
            PhysicsWorld = new PhysicsWorld(widthInChunks: widthInTiles, heightInChunks: heightInTiles);
            EventBus = new GameplayEventBus(); // Initialize EventBus
            
            // Initialize JobScheduler if not already set (Static per AppDomain usually, but we manage it here)
            if (World.SharedJobScheduler == null)
            {
                Diagnostics.Log.Info(in LogChannels.Engine, "Initializing JobScheduler...");
                _jobScheduler = new JobScheduler(new JobScheduler.Config
                {
                    ThreadPrefixName = "LudotsWorker",
                    ThreadCount = 0, // Auto
                    MaxExpectedConcurrentJobs = 64,
                    StrictAllocationMode = false
                });
                World.SharedJobScheduler = _jobScheduler;
            }
        }

        private void WireUpPositionProvider()
        {
            var w = World;
            ((SpatialQueryService)SpatialQueries).SetPositionProvider(entity =>
            {
                if (!w.IsAlive(entity) || !w.Has<WorldPositionCm>(entity))
                {
                    // Spatial backend may momentarily contain stale entities during structural transitions.
                    // Return a far-away sentinel position so fine-shape filtering excludes them safely.
                    return new WorldCmInt2(1_000_000_000, 1_000_000_000);
                }
                ref var pos = ref w.Get<WorldPositionCm>(entity);
                return pos.Value.ToWorldCmInt2();
            });
        }

        private void InitializeCoreSystems(GameConfig config)
        {
            Diagnostics.Log.Info(in LogChannels.Engine, "Initializing Core GAS Systems...");
            // Instantiate GAS Systems
            var engineClockConfigLoader = new EngineClockConfigLoader(ConfigPipeline);
            var engineClockConfig = engineClockConfigLoader.Load();
            Time.FixedDeltaTime = 1f / engineClockConfig.FixedHz;

            var extensionAttributeRegistry = new ExtensionAttributeRegistry();
            var attributeSchemaUpdateQueue = new AttributeSchemaUpdateQueue();
            var schemaUpdateSystem = new AttributeSchemaUpdateSystem(World, extensionAttributeRegistry, attributeSchemaUpdateQueue);
            var gasBudget = new GasBudget();
            TeamManager.DefaultRelationship = TeamRelationship.Hostile;
            var tagOps = new TagOps(new TagRuleRegistry(), gasBudget);
            var effectTemplateRegistry = new EffectTemplateRegistry();
            effectTemplateRegistry.SetConflictReport(ConflictReport);
            var gasConditions = new GasConditionRegistry();
            _effectTemplateLoader = new EffectTemplateLoader(ConfigPipeline, effectTemplateRegistry, gasConditions);
            var gasClockConfigLoader = new GasClockConfigLoader(ConfigPipeline);
            var gasClockConfig = gasClockConfigLoader.Load();
            var physics2dClockConfigLoader = new Physics2DClockConfigLoader(ConfigPipeline);
            var physics2dClockConfig = physics2dClockConfigLoader.Load();
            var navigation2dClockConfigLoader = new Navigation2DClockConfigLoader(ConfigPipeline);
            var navigation2dClockConfig = navigation2dClockConfigLoader.Load();
            var graphProgramRegistry = new GraphProgramRegistry();
            var graphSymbolResolver = new GasGraphSymbolResolver();
            var graphConfigLoader = new GraphProgramConfigLoader(ConfigPipeline, graphProgramRegistry, graphSymbolResolver);
            var graphPackages = graphConfigLoader.LoadIdsAndCompile();
            var presetTypes = new PresetTypeRegistry();
            var presetTypeLoader = new PresetTypeLoader(ConfigPipeline, presetTypes);
            presetTypeLoader.Load(ConfigCatalog, ConfigConflictReport);
            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            var effectRequestQueue = new EffectRequestQueue();
            var clock = new DiscreteClock();
            var gasClocks = new GasClocks(clock);
            var abilityDefinitions = new AbilityDefinitionRegistry();
            var abilityFormSets = new AbilityFormSetRegistry();
            var contextGroups = new ContextGroupRegistry();
            abilityDefinitions.SetConflictReport(ConflictReport);
            EffectParamKeys.Initialize();
            AbilityFormSetIdRegistry.Clear();
            ContextGroupIdRegistry.Clear();
            _effectTemplateLoader.Load();
            new AbilityExecLoader(ConfigPipeline, abilityDefinitions).Load(ConfigCatalog, ConfigConflictReport);
            new AbilityFormSetConfigLoader(ConfigPipeline, abilityFormSets).Load(ConfigCatalog, ConfigConflictReport);
            graphConfigLoader.PatchAndRegister(graphPackages);
            new ContextGroupConfigLoader(ConfigPipeline, contextGroups).Load(ConfigCatalog, ConfigConflictReport);
            var gasGraphApi = new GasGraphRuntimeApi(World, SpatialQueries, SpatialCoords, EventBus, effectRequestQueue, tagOps);
            var phaseExecutor = new EffectPhaseExecutor(graphProgramRegistry, presetTypes, builtinHandlers, GasGraphOpHandlerTable.Instance, effectTemplateRegistry, eventBus: EventBus, budget: gasBudget);
            var tagRules = new TagRuleSetLoader(ConfigPipeline).Load();
            for (int i = 0; i < tagRules.Count; i++)
            {
                tagOps.RegisterTagRuleSet(tagRules[i].TagId, tagRules[i].RuleSet);
            }
            var inputRequestQueue = new InputRequestQueue();
            var abilityInputRequestQueue = new InputRequestQueue();
            var inputResponseBuffer = new InputResponseBuffer();
            var selectionRequestQueue = new SelectionRequestQueue();
            var selectionResponseBuffer = new SelectionResponseBuffer();
            var selectionSetKeyRegistry = new StringIntRegistry(capacity: 32, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var selectionConfig = config.Selection ?? new SelectionRuntimeConfig();
            var selectionRuntime = new SelectionRuntime(World, selectionConfig, selectionSetKeyRegistry);
            var runtimeEntitySpawnQueue = new RuntimeEntitySpawnQueue();
            var orderQueue = new OrderQueue();
            var chainOrderQueue = new OrderQueue();
            var orderRequestQueue = new OrderRequestQueue();
            var responseChainTelemetry = new ResponseChainTelemetryBuffer();
            
            var orderTypeIds = config.Constants.OrderTypeIds;
            var responseChainOrderTypeIds = config.Constants.ResponseChainOrderTypeIds;

            var deferredTriggerQueue = new DeferredTriggerQueue();
            var deferredTriggerCollectionSystem = new DeferredTriggerCollectionSystem(World, deferredTriggerQueue, tagOps);
            var deferredTriggerProcessSystem = new DeferredTriggerProcessSystem(World, deferredTriggerQueue, EventBus);
            var clearPresentationFlagsSystem = new ClearPresentationFlagsSystem(World);
            var gasPresentationEvents = new GasPresentationEventBuffer();
            var presentationEventStream = new PresentationEventStream();
            var presentationBridgeSystem = new PresentationBridgeSystem(World, EventBus, presentationEventStream, GameSession, gasPresentationEvents);
            var presentationCommandBuffer = new PresentationCommandBuffer();
            var presentationPrefabs = new PrefabRegistry();
            var meshAssets = new MeshAssetRegistry();
            var visualTemplates = new VisualTemplateRegistry();
            var animatorControllers = new AnimatorControllerRegistry();
            var presentationStableIds = new PresentationStableIdAllocator();
            var primitiveDrawBuffer = new PrimitiveDrawBuffer(PrimitiveDrawBufferCapacity);
            var visualSnapshotBuffer = new PrimitiveDrawBuffer(VisualSnapshotBufferCapacity);
            var transientMarkerBuffer = new TransientMarkerBuffer();
            var groundOverlayBuffer = new GroundOverlayBuffer();
            var worldHudBuffer = new WorldHudBatchBuffer();
            var performerDefinitions = new PerformerDefinitionRegistry();
            var performerInstances = new PerformerInstanceBuffer();
            var performerGraphApi = new GasGraphRuntimeApi(World, spatialQueries: null, coords: null, eventBus: null);
            new MeshAssetConfigLoader(ConfigPipeline, meshAssets, presentationPrefabs).Load();
            new VisualTemplateConfigLoader(ConfigPipeline, visualTemplates, meshAssets, animatorControllers).Load();
            var presentationTextCatalog = new PresentationTextCatalogLoader(ConfigPipeline).Load(ConfigCatalog, ConfigConflictReport);
            var presentationTextLocaleSelection = new PresentationTextLocaleSelection(presentationTextCatalog);
            BuiltinPerformerDefinitions.Register(performerDefinitions, meshAssets, presentationTextCatalog.GetTokenId);
            var performerRuleSystem = new PerformerRuleSystem(World, presentationEventStream, presentationCommandBuffer, performerDefinitions, graphProgramRegistry, performerGraphApi, GlobalContext);
            var performerRuntimeSystem = new PerformerRuntimeSystem(World, presentationPrefabs, presentationCommandBuffer, primitiveDrawBuffer, transientMarkerBuffer, performerInstances, presentationStableIds);
            var performerEmitSystem = new PerformerEmitSystem(World, performerInstances, performerDefinitions, groundOverlayBuffer, primitiveDrawBuffer, worldHudBuffer, graphProgramRegistry, performerGraphApi, GlobalContext,
                entityColorResolver: (world, entity) => Ludots.Core.Presentation.Utils.TeamColorResolver.Resolve(world, entity));
            new PerformerDefinitionConfigLoader(
                ConfigPipeline,
                performerDefinitions,
                Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register,
                meshAssets.GetId,
                presentationTextCatalog.GetTokenId,
                visualTemplates.GetId).Load();
            var presentationAuthoring = new PresentationAuthoringContext(visualTemplates, performerDefinitions, animatorControllers, presentationStableIds);
            MapLoader.PresentationAuthoringContext = presentationAuthoring;

            System.Diagnostics.Debug.Assert(
                meshAssets.TryGetDescriptor(meshAssets.GetId(WellKnownMeshKeys.Cube), out var _cubeDbg) && _cubeDbg.Type == MeshAssetType.Primitive,
                "MeshAssetRegistry: 'cube' descriptor missing or invalid after config load");
            System.Diagnostics.Debug.Assert(
                meshAssets.TryGetDescriptor(meshAssets.GetId(WellKnownMeshKeys.Sphere), out var _sphereDbg) && _sphereDbg.Type == MeshAssetType.Primitive,
                "MeshAssetRegistry: 'sphere' descriptor missing or invalid after config load");

            var worldHudStrings = new WorldHudStringTable(presentationTextCatalog, presentationTextLocaleSelection);
            new AttributeConstraintsLoader(ConfigPipeline).Load();

            var abilitySystem = new AbilitySystem(World, effectRequestQueue, abilityDefinitions, tagOps, graphProgramRegistry, gasGraphApi);
            var reactionSystem = new ReactionSystem(World, abilitySystem, EventBus);
            var attributeSinks = new AttributeSinkRegistry();
            GasAttributeSinks.RegisterBuiltins(attributeSinks);
            var attributeBindings = new AttributeBindingRegistry();
            new AttributeBindingLoader(ConfigPipeline, attributeSinks, attributeBindings).Load();
            var bindingSystem = new AttributeBindingSystem(World, attributeSinks, attributeBindings);
            var aggSystem = new AttributeAggregatorSystem(World);
            var sessionSystem = new GameSessionSystem(GameSession);
            var authoritativeInput = new FrozenInputActionReader();
            var authoritativeInputAccumulator = new AuthoritativeInputAccumulator();
            _inputRuntimeSystem = new InputRuntimeSystem(GlobalContext, authoritativeInputAccumulator);
            _inputRuntimeSystem.Initialize();
            var clockStepPolicy = new GasClockStepPolicy(gasClockConfig.StepEveryFixedTicks, gasClockConfig.Mode);
            var clockSystem = new GasClockSystem(clock, clockStepPolicy);
            var physics2dTickPolicy = new Physics2DTickPolicy(physics2dClockConfig.PhysicsHz, physics2dClockConfig.MaxStepsPerFixedTick);
            var navigation2dTickPolicy = new Navigation2DTickPolicy(navigation2dClockConfig.NavigationHz, navigation2dClockConfig.MaxStepsPerFixedTick);
            _physics2DController = new Physics2DController(World, physics2dTickPolicy, physics2dClockConfig.PhysicsHz, CreateContext, TriggerManager.FireEvent);
            var simulationLoopController = new SimulationLoopController(this);
            _gasController = new Ludots.Core.Gameplay.GAS.GasController(World, clockStepPolicy, simulationLoopController, CreateContext, TriggerManager.FireEvent);
            var timedTagSystem = new TimedTagExpirationSystem(World, clock, tagOps);
            
            // Get order tags from config — fail-fast if missing (SSOT: game.json + OrderStateTags.cs)
            if (!orderTypeIds.ContainsKey("castAbility") ||
                !orderTypeIds.ContainsKey("moveTo") ||
                !orderTypeIds.ContainsKey("attackTarget") ||
                !orderTypeIds.ContainsKey("stop"))
            {
                throw new InvalidOperationException(
                    "game.json constants.orderTypeIds must define all required keys: castAbility, moveTo, attackTarget, stop. " +
                    "These are the single source of truth for order type ids.");
            }
            int cfgCastAbility = orderTypeIds["castAbility"];
            int cfgMoveTo = orderTypeIds["moveTo"];
            int cfgAttackTarget = orderTypeIds["attackTarget"];
            int cfgStop = orderTypeIds["stop"];
            
            // respondChainOrderTagId = -1 (invalid sentinel): chain orders are routed directly
            // to chainOrderQueue by ResponseChain*Systems, not through the dispatch system.
            // Using -1 prevents accidental match with default OrderTagId == 0.
            var orderRuleRegistry = new OrderRuleRegistry();
            
            // ── OrderBuffer pipeline ──
            var orderTypeRegistry = new OrderTypeRegistry();
            new OrderTypeConfigLoader(ConfigPipeline).Load(orderTypeRegistry, orderRuleRegistry, ConfigCatalog, ConfigConflictReport);
            
            // Register chain order types (response chain) into OrderTypeRegistry
            int cfgChainPass = responseChainOrderTypeIds.GetValueOrDefault("chainPass", 1);
            int cfgChainNegate = responseChainOrderTypeIds.GetValueOrDefault("chainNegate", 2);
            int cfgChainActivateEffect = responseChainOrderTypeIds.GetValueOrDefault("chainActivateEffect", 3);
            if (!orderTypeRegistry.IsRegistered(cfgCastAbility) ||
                !orderTypeRegistry.IsRegistered(cfgMoveTo) ||
                !orderTypeRegistry.IsRegistered(cfgAttackTarget) ||
                !orderTypeRegistry.IsRegistered(cfgStop) ||
                !orderTypeRegistry.IsRegistered(cfgChainPass) ||
                !orderTypeRegistry.IsRegistered(cfgChainNegate) ||
                !orderTypeRegistry.IsRegistered(cfgChainActivateEffect))
            {
                throw new InvalidOperationException(
                    "GAS/order_types.json must define castAbility, moveTo, attackTarget, stop, chainPass, chainNegate, and chainActivateEffect order types. " +
                    "Order runtime is configured from merged config and does not provide code defaults.");
            }
            int stepRateHz = engineClockConfig.FixedHz / Math.Max(1, gasClockConfig.StepEveryFixedTicks);
            var orderBufferSystem = new OrderBufferSystem(
                World, clock, orderTypeRegistry, orderRuleRegistry,
                orderQueue, stepRateHz,
                graphProgramRegistry, gasGraphApi);
            var abilityExecSystem = new AbilityExecSystem(World, clock, abilityInputRequestQueue, inputResponseBuffer, selectionRequestQueue, selectionResponseBuffer, effectRequestQueue, abilityDefinitions, EventBus, cfgCastAbility, gasPresentationEvents, phaseExecutor: phaseExecutor, graphPrograms: graphProgramRegistry, graphApi: gasGraphApi, tagOps: tagOps, orderTypeRegistry: orderTypeRegistry);
            var stopOrderSystem = new StopOrderSystem(World, orderTypeRegistry, cfgStop);
            var moveToOrderSystem = new MoveToWorldCmOrderSystem(World, orderTypeRegistry, cfgMoveTo);

            // Register systems in Phase order according to GAS design document
            // Phase 0: SchemaUpdate
            SetService(CoreServiceKeys.ExtensionAttributeRegistry, extensionAttributeRegistry);
            SetService(CoreServiceKeys.AttributeSchemaUpdateQueue, attributeSchemaUpdateQueue);
            SetService(CoreServiceKeys.GasBudget, gasBudget);
            SetService(CoreServiceKeys.EffectTemplateRegistry, effectTemplateRegistry);
            SetService(CoreServiceKeys.GraphProgramRegistry, graphProgramRegistry);
            SetService(CoreServiceKeys.EffectRequestQueue, effectRequestQueue);
            SetService(CoreServiceKeys.Clock, (IClock)clock);
            SetService(CoreServiceKeys.GasClockStepPolicy, clockStepPolicy);
            SetService(CoreServiceKeys.GasClocks, gasClocks);
            SetService(CoreServiceKeys.Physics2DTickPolicy, physics2dTickPolicy);
            SetService(CoreServiceKeys.Navigation2DTickPolicy, navigation2dTickPolicy);
            SetService(CoreServiceKeys.Physics2DController, _physics2DController);
            SetService(CoreServiceKeys.SimulationLoopController, simulationLoopController);
            SetService(CoreServiceKeys.GasController, _gasController);
            SetService(CoreServiceKeys.GasConditionRegistry, gasConditions);
            SetService(CoreServiceKeys.TagOps, tagOps);
            SetService(CoreServiceKeys.AbilityDefinitionRegistry, abilityDefinitions);
            SetService(CoreServiceKeys.AbilityFormSetRegistry, abilityFormSets);
            SetService(CoreServiceKeys.ContextGroupRegistry, contextGroups);
            SetService(CoreServiceKeys.InputRequestQueue, inputRequestQueue);
            SetService(CoreServiceKeys.AbilityInputRequestQueue, abilityInputRequestQueue);
            SetService(CoreServiceKeys.InputResponseBuffer, inputResponseBuffer);
            SetService(CoreServiceKeys.SelectionRequestQueue, selectionRequestQueue);
            SetService(CoreServiceKeys.SelectionResponseBuffer, selectionResponseBuffer);
            SetService(CoreServiceKeys.SelectionRuntime, selectionRuntime);
            SetService(CoreServiceKeys.SelectionConfig, selectionConfig);
            SetService(CoreServiceKeys.SelectionSetKeyRegistry, selectionSetKeyRegistry);
            SetService(CoreServiceKeys.RuntimeEntitySpawnQueue, runtimeEntitySpawnQueue);
            SetService(CoreServiceKeys.OrderQueue, orderQueue);
            SetService(CoreServiceKeys.OrderTypeRegistry, orderTypeRegistry);
            SetService(CoreServiceKeys.OrderRuleRegistry, orderRuleRegistry);
            SetService(CoreServiceKeys.OrderBufferSystem, orderBufferSystem);
            SetService(CoreServiceKeys.OrderRequestQueue, orderRequestQueue);
            SetService(CoreServiceKeys.ResponseChainTelemetryBuffer, responseChainTelemetry);
            SetService(CoreServiceKeys.ChainOrderQueue, chainOrderQueue);
            SetService(CoreServiceKeys.AttributeSinkRegistry, attributeSinks);
            SetService(CoreServiceKeys.AttributeBindingRegistry, attributeBindings);
            SetService(CoreServiceKeys.AuthoritativeInput, authoritativeInput);
            SetService(CoreServiceKeys.PresentationEventStream, presentationEventStream);
            SetService(CoreServiceKeys.PresentationCommandBuffer, presentationCommandBuffer);
            SetService(CoreServiceKeys.PresentationPrefabRegistry, presentationPrefabs);
            SetService(CoreServiceKeys.PresentationMeshAssetRegistry, meshAssets);
            SetService(CoreServiceKeys.PresentationVisualTemplateRegistry, visualTemplates);
            SetService(CoreServiceKeys.AnimatorControllerRegistry, animatorControllers);
            SetService(CoreServiceKeys.PresentationStableIdAllocator, presentationStableIds);
            _primitiveDrawBuffer = primitiveDrawBuffer;
            _visualSnapshotBuffer = visualSnapshotBuffer;
            _gasPresentationEvents = gasPresentationEvents;
            _groundOverlayBuffer = groundOverlayBuffer;
            _worldHudBuffer = worldHudBuffer;
            SetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer, primitiveDrawBuffer);
            SetService(CoreServiceKeys.PresentationVisualSnapshotBuffer, visualSnapshotBuffer);
            SetService(CoreServiceKeys.PresentationWorldHudBuffer, worldHudBuffer);
            SetService(CoreServiceKeys.PresentationWorldHudStrings, worldHudStrings);
            SetService(CoreServiceKeys.PresentationTextCatalog, presentationTextCatalog);
            SetService(CoreServiceKeys.PresentationTextLocaleSelection, presentationTextLocaleSelection);
            var screenHudBuffer = new ScreenHudBatchBuffer();
            SetService(CoreServiceKeys.PresentationScreenHudBuffer, screenHudBuffer);
            SetService(CoreServiceKeys.ScreenOverlayBuffer, new ScreenOverlayBuffer());
            SetService(CoreServiceKeys.RenderDebugState, new RenderDebugState());
            SetService(CoreServiceKeys.PresentationTimingDiagnostics, new PresentationTimingDiagnostics());
            SetService(CoreServiceKeys.TransientMarkerBuffer, transientMarkerBuffer);
            SetService(CoreServiceKeys.GasPresentationEventBuffer, gasPresentationEvents);
            SetService(CoreServiceKeys.GroundOverlayBuffer, groundOverlayBuffer);
            SetService(CoreServiceKeys.PerformerDefinitionRegistry, performerDefinitions);
            SetService(CoreServiceKeys.PerformerInstanceBuffer, performerInstances);
            var virtualCameraRegistry = new VirtualCameraRegistry();
            new VirtualCameraDefinitionLoader(ConfigPipeline, virtualCameraRegistry).Load(ConfigCatalog, ConfigConflictReport);
            SetService(CoreServiceKeys.VirtualCameraRegistry, virtualCameraRegistry);
            GameSession.Camera.SetVirtualCameraRegistry(virtualCameraRegistry);
            var cameraRuntimeSystem = new CameraRuntimeSystem(World, GameSession.Camera, GlobalContext, virtualCameraRegistry);
            RegisterSystem(new GasBudgetResetSystem(gasBudget), SystemGroup.SchemaUpdate);
            RegisterSystem(schemaUpdateSystem, SystemGroup.SchemaUpdate);
            
            // Phase 0.5: 保存上一帧位置（插值前置条件，必须在所有移动系统之前）
            RegisterSystem(new SavePreviousWorldPositionSystem(World), SystemGroup.SchemaUpdate);
            
            // Phase 1: InputCollection
            RegisterSystem(sessionSystem, SystemGroup.InputCollection); // Session handles input gathering
            RegisterSystem(new AuthoritativeInputSnapshotSystem(authoritativeInput, authoritativeInputAccumulator), SystemGroup.InputCollection);
            RegisterSystem(new LocalPlayerEntityResolverSystem(World, GlobalContext), SystemGroup.InputCollection);
            RegisterSystem(cameraRuntimeSystem, SystemGroup.InputCollection);
            RegisterSystem(clockSystem, SystemGroup.InputCollection);
            RegisterSystem(timedTagSystem, SystemGroup.InputCollection);
            RegisterSystem(new AbilityFormRoutingSystem(World, abilityFormSets, tagOps), SystemGroup.InputCollection);
            _worldToGridSyncSystem = new WorldToGridSyncSystem(World, SpatialCoords);
            _spatialPartitionUpdateSystem = new SpatialPartitionUpdateSystem(World, _spatialPartition, WorldSizeSpec);
            RegisterSystem(_worldToGridSyncSystem, SystemGroup.PostMovement);
            RegisterSystem(_spatialPartitionUpdateSystem, SystemGroup.PostMovement);

            if (config.Navigation2D.Enabled)
            {
                var navigation2dRuntime = new Navigation2DRuntime(config.Navigation2D, gridCellSizeCm: SpatialCoords.GridCellSizeCm, loadedChunks: null);
                SetService(CoreServiceKeys.Navigation2DRuntime, navigation2dRuntime);

                const string nav2dSystemTypeName = "Ludots.Core.Physics2D.Systems.Navigation2DSimulationSystem2D";
                const string physics2dAssemblyName = "Ludots.Physics2D";
                var nav2dSystemType = Type.GetType($"{nav2dSystemTypeName}, {physics2dAssemblyName}", throwOnError: false);
                if (nav2dSystemType == null)
                {
                    AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(physics2dAssemblyName));
                    nav2dSystemType = Type.GetType($"{nav2dSystemTypeName}, {physics2dAssemblyName}", throwOnError: false);
                }

                if (nav2dSystemType == null)
                {
                    throw new InvalidOperationException("Navigation2D.Enabled=true requires Ludots.Physics2D and Navigation2DSimulationSystem2D to be loadable.");
                }
                else
                {
                    var nav2dSystemObj = Activator.CreateInstance(nav2dSystemType, World, navigation2dRuntime, clock, navigation2dTickPolicy);
                    if (nav2dSystemObj is ISystem<float> nav2dSystem)
                    {
                        RegisterSystem(nav2dSystem, SystemGroup.InputCollection);
                    }
                }
            }
            
            // Phase 2: AbilityActivation
            RegisterSystem(orderBufferSystem, SystemGroup.AbilityActivation);
            RegisterSystem(stopOrderSystem, SystemGroup.AbilityActivation);
            RegisterSystem(reactionSystem, SystemGroup.AbilityActivation);
            RegisterSystem(abilitySystem, SystemGroup.AbilityActivation);
            RegisterSystem(abilityExecSystem, SystemGroup.AbilityActivation);
            RegisterSystem(moveToOrderSystem, SystemGroup.AbilityActivation);
            
            // Phase 3: EffectProcessing (含响应链)
            var responseChainOrderTypes = new ResponseChainOrderTypes
            {
                ChainPass = cfgChainPass,
                ChainNegate = cfgChainNegate,
                ChainActivateEffect = cfgChainActivateEffect
            };
            RegisterSystem(new EffectProcessingLoopSystem(World, effectRequestQueue, clock, gasConditions, gasBudget, effectTemplateRegistry, inputRequestQueue, chainOrderQueue, responseChainTelemetry, orderRequestQueue, responseChainOrderTypes, gasPresentationEvents, SpatialQueries, runtimeEntitySpawnQueue, phaseExecutor: phaseExecutor, graphApi: gasGraphApi, tagOps: tagOps), SystemGroup.EffectProcessing);
            RegisterSystem(new ProjectileRuntimeSystem(World, clock, effectRequestQueue), SystemGroup.EffectProcessing);
            RegisterSystem(new RuntimeEntitySpawnSystem(World, runtimeEntitySpawnQueue, MapLoader.TemplateRegistry, presentationAuthoring, effectRequestQueue), SystemGroup.EffectProcessing);
            RegisterSystem(new DisplacementRuntimeSystem(World), SystemGroup.EffectProcessing);
            
            // Phase 4: AttributeCalculation
            RegisterSystem(aggSystem, SystemGroup.AttributeCalculation);
            RegisterSystem(bindingSystem, SystemGroup.AttributeCalculation);
            
            // Phase 5: DeferredTriggerCollection
            SetService(CoreServiceKeys.DeferredTriggerQueue, deferredTriggerQueue);
            RegisterSystem(deferredTriggerCollectionSystem, SystemGroup.DeferredTriggerCollection);
            RegisterSystem(deferredTriggerProcessSystem, SystemGroup.DeferredTriggerCollection);
            
            // Phase 6: Cleanup
            RegisterSystem(new GameplayEventDispatchSystem(EventBus, gasBudget), SystemGroup.EventDispatch);
            RegisterSystem(new GasBudgetReportSystem(gasBudget), SystemGroup.EventDispatch);
            
            // Phase 7.1: ClearPresentationFlags
            RegisterSystem(presentationBridgeSystem, SystemGroup.ClearPresentationFlags);
            RegisterSystem(clearPresentationFlagsSystem, SystemGroup.ClearPresentationFlags);
            _cooperativeSimulation = new PhaseOrderedCooperativeSimulation(_systemGroups, OnFixedStepCompleted);

            var responseChainUiState = new ResponseChainUiState();
            SetService(CoreServiceKeys.ResponseChainUiState, responseChainUiState);
            
            // PresentationFrameSetupSystem MUST be the first presentation system
            // It calculates InterpolationAlpha for all visual sync systems
            var presentationFrameSetup = new PresentationFrameSetupSystem(World, Pacemaker);
            RegisterPresentationSystem(presentationFrameSetup);
            SetService(CoreServiceKeys.PresentationFrameSetup, presentationFrameSetup);
            
            // WorldToVisualSyncSystem: 插值 WorldPositionCm → VisualTransform（必须在 PresentationFrameSetup 之后）
            RegisterPresentationSystem(new WorldToVisualSyncSystem(World));
            // TerrainHeightSyncSystem: 采样地形高度写入 VisualTransform.Y，使实体贴附地表
            RegisterPresentationSystem(new TerrainHeightSyncSystem(World, GlobalContext));
            RegisterPresentationSystem(new EntityVisualEmitSystem(World, primitiveDrawBuffer, visualSnapshotBuffer));
            RegisterPresentationSystem(new PresentationStartupPerformerSystem(World, presentationCommandBuffer));
            
            RegisterPresentationSystem(new ResponseChainDirectorSystem(World, orderRequestQueue, responseChainTelemetry, responseChainUiState, presentationCommandBuffer, presentationPrefabs));
            RegisterPresentationSystem(new ResponseChainHumanOrderSourceSystem(GlobalContext, responseChainUiState, chainOrderQueue));
            RegisterPresentationSystem(new ResponseChainAiOrderSourceSystem(responseChainUiState, chainOrderQueue, cfgChainPass));
            RegisterPresentationSystem(new ResponseChainUiSyncSystem(GlobalContext, responseChainUiState, orderTypeRegistry));
            // PerformerRuleSystem reads events and produces commands.
            RegisterPresentationSystem(performerRuleSystem);
            // PerformerRuntimeSystem consumes commands, manages instance lifecycle.
            RegisterPresentationSystem(performerRuntimeSystem);
            // PerformerEmitSystem ticks instances, evaluates visibility/bindings, outputs to draw buffers.
            // Also handles entity-scoped definitions (replaces WorldHudCollectorSystem).
            RegisterPresentationSystem(performerEmitSystem);
        }

        private void OnFixedStepCompleted(float fixedDt)
        {
            _physics2DController?.AfterPhysicsFixedTick();
            _gasController?.AfterFixedTick();
        }

        public MapSession CurrentMapSession { get; private set; }

        public void LoadMap(string mapId)
        {
            Diagnostics.Log.Info(in LogChannels.Engine, $"Loading Map: {mapId}");
            var mid = new MapId(mapId);

            // Initialize MapSessionManager if first time
            if (MapSessions == null)
            {
                MapSessions = new MapSessionManager();
                BoardIdRegistry = new BoardIdRegistry();
                SetService(CoreServiceKeys.MapSessions, MapSessions);
                SetService(CoreServiceKeys.BoardIdRegistry, BoardIdRegistry);
            }

            // Unload first when same map already loaded, then reload
            if (MapSessions.GetSession(mid) != null)
            {
                UnloadMap(mapId);
            }

            var mapConfig = MapManager.LoadMap(mapId);

            if (mapConfig != null)
            {
                var previousFocused = MapSessions.FocusedSession;

                // Create new session with boards (additive — old sessions stay)
                var session = MapSessions.CreateSession(mid, mapConfig, null);
                CreateBoardsForSession(session, mapConfig);
                MapSessions.PushFocused(mid);   // old focused → Suspended
                if (previousFocused != null)
                {
                    SetMapEntitiesSuspended(previousFocused.MapId, true);
                }
                CurrentMapSession = session;
                SetService(CoreServiceKeys.MapSession, session);

                // Apply primary board spatial config to engine-level services
                var primaryBoard = session.PrimaryBoard;
                if (primaryBoard != null)
                {
                    ApplyBoardSpatialConfig(primaryBoard);
                    LoadBoardTerrainData(session, mapConfig);
                }

                LoadNavForMap(mapId, mapConfig);
                Diagnostics.Log.Info(in LogChannels.Engine, "Creating Entities from MapConfig...");
                MapLoader.LoadEntities(mapConfig);
                SetMapEntitiesSuspended(mid, false);

                // Instantiate map triggers + apply decorators
                var definition = ((MapManager)MapManager).GetDefinition(mid);
                var triggers = InstantiateMapTriggers(definition, mapConfig);
                ApplyTriggerDecorators(triggers);
                if (triggers.Count > 0)
                {
                    foreach (var t in triggers) session.AddTrigger(t);
                    TriggerManager.RegisterMapTriggers(mid, triggers);
                }

                var finalCtx = CreateContext();
                finalCtx.Set(CoreServiceKeys.MapId, mid);
                finalCtx.Set(CoreServiceKeys.MapTags, mapConfig.Tags);
                var featureFlags = MapFeatureFlags.FromTags(mapConfig.Tags);
                SetService(CoreServiceKeys.MapFeatureFlags, featureFlags);
                finalCtx.Set(CoreServiceKeys.MapFeatureFlags, featureFlags);

                foreach (var kvp in GlobalContext) finalCtx.Set(kvp.Key, kvp.Value);

                ApplyDefaultCamera(mapConfig);

                Diagnostics.Log.Info(in LogChannels.Engine, $"Firing MapLoaded event for {mapId}...");
                CompleteLifecycleEvent(TriggerManager.FireMapEventAsync(mid, GameEvents.MapLoaded, finalCtx));
            }
            else
            {
                Diagnostics.Log.Error(in LogChannels.Engine, $"Failed to load map {mapId}");
            }
        }

        /// <summary>
        /// Explicitly unload a map by ID. Fires MapUnloaded, unregisters triggers,
        /// cleans up session. If the map is at the top of the focus stack, pops it
        /// and fires MapResumed on the restored map.
        /// </summary>
        public void UnloadMap(string mapId)
        {
            var mid = new MapId(mapId);
            if (MapSessions == null) return;

            var session = MapSessions.GetSession(mid);
            if (session == null)
            {
                Diagnostics.Log.Warn(in LogChannels.Engine, $"UnloadMap: No session for '{mapId}'.");
                return;
            }

            // Fire MapUnloaded — scoped to this map's triggers
            var unloadCtx = CreateContext();
            unloadCtx.Set(CoreServiceKeys.MapId, mid);
            foreach (var kvp in GlobalContext) unloadCtx.Set(kvp.Key, kvp.Value);
            CompleteLifecycleEvent(TriggerManager.FireMapEventAsync(mid, GameEvents.MapUnloaded, unloadCtx));
            TriggerManager.UnregisterMapTriggers(mid, unloadCtx);

            // Check if this map is at the top of the focus stack
            var focused = MapSessions.FocusedSession;
            bool wasFocused = focused != null && focused.MapId == mid;

            MapSessions.UnloadSession(mid, World);

            if (wasFocused && MapSessions.FocusedSession != null)
            {
                // The stack auto-pops in UnloadSession; restore next focused
                var restored = MapSessions.FocusedSession;
                CurrentMapSession = restored;
                SetService(CoreServiceKeys.MapSession, restored);

                var primaryBoard = restored.PrimaryBoard;
                if (primaryBoard != null)
                {
                    ApplyBoardSpatialConfig(primaryBoard);
                    LoadBoardTerrainData(restored, restored.MapConfig);
                    LoadNavForMap(restored.MapId.Value, restored.MapConfig);
                }
                SetMapEntitiesSuspended(restored.MapId, false);

                var resumeCtx = CreateContext();
                resumeCtx.Set(CoreServiceKeys.MapId, restored.MapId);
                foreach (var kvp in GlobalContext) resumeCtx.Set(kvp.Key, kvp.Value);
                CompleteLifecycleEvent(TriggerManager.FireMapEventAsync(restored.MapId, GameEvents.MapResumed, resumeCtx));
            }
        }

        /// <summary>
        /// Push a nested inner map (三国志12 mode). Outer map is suspended, inner map becomes active.
        /// </summary>
        public void PushMap(string innerMapId, Dictionary<string, object> passthrough = null)
        {
            var inner = new MapId(innerMapId);
            var outerSession = MapSessions?.FocusedSession;

            var mapConfig = MapManager.LoadMap(innerMapId);
            if (mapConfig == null)
            {
                Diagnostics.Log.Error(in LogChannels.Engine, $"PushMap: Failed to load inner map '{innerMapId}'");
                return;
            }

            // Create inner session with parent context from outer
            MapContext parentCtx = outerSession?.Context;
            var session = MapSessions.CreateSession(inner, mapConfig, parentCtx);

            // Pass through data to inner context
            if (passthrough != null)
            {
                foreach (var kvp in passthrough) session.Context.Set(kvp.Key, kvp.Value);
            }

            CreateBoardsForSession(session, mapConfig);

            // Push focus — outer becomes Suspended
            MapSessions.PushFocused(inner);
            if (outerSession != null)
            {
                SetMapEntitiesSuspended(outerSession.MapId, true);
            }
            CurrentMapSession = session;
            SetService(CoreServiceKeys.MapSession, session);

            var primaryBoard = session.PrimaryBoard;
            if (primaryBoard != null)
            {
                ApplyBoardSpatialConfig(primaryBoard);
                LoadBoardTerrainData(session, mapConfig);
                LoadNavForMap(innerMapId, mapConfig);
            }

            MapLoader.LoadEntities(mapConfig);
            SetMapEntitiesSuspended(inner, false);

            // Fire MapSuspended on outer (scoped)
            if (outerSession != null)
            {
                var suspendCtx = CreateContext();
                suspendCtx.Set(CoreServiceKeys.MapId, outerSession.MapId);
                foreach (var kvp in GlobalContext) suspendCtx.Set(kvp.Key, kvp.Value);
                CompleteLifecycleEvent(TriggerManager.FireMapEventAsync(outerSession.MapId, GameEvents.MapSuspended, suspendCtx));
            }

            // Instantiate, decorate, and register inner map triggers
            var definition = ((MapManager)MapManager).GetDefinition(inner);
            var triggers = InstantiateMapTriggers(definition, mapConfig);
            ApplyTriggerDecorators(triggers);
            if (triggers.Count > 0)
            {
                foreach (var t in triggers) session.AddTrigger(t);
                TriggerManager.RegisterMapTriggers(inner, triggers);
            }

            var ctx = CreateContext();
            ctx.Set(CoreServiceKeys.MapId, inner);
            ctx.Set(CoreServiceKeys.MapTags, mapConfig.Tags);
            foreach (var kvp in GlobalContext) ctx.Set(kvp.Key, kvp.Value);
            CompleteLifecycleEvent(TriggerManager.FireMapEventAsync(inner, GameEvents.MapLoaded, ctx));
        }

        /// <summary>
        /// Pop the inner map, restoring the outer map to Active.
        /// </summary>
        public void PopMap()
        {
            if (MapSessions == null || MapSessions.All.Count <= 1)
            {
                Diagnostics.Log.Warn(in LogChannels.Engine, "PopMap: No inner map to pop.");
                return;
            }

            var innerSession = MapSessions.FocusedSession;
            if (innerSession != null)
            {
                // Fire MapUnloaded (scoped) + unregister triggers
                var unloadCtx = CreateContext();
                unloadCtx.Set(CoreServiceKeys.MapId, innerSession.MapId);
                foreach (var kvp in GlobalContext) unloadCtx.Set(kvp.Key, kvp.Value);
                CompleteLifecycleEvent(TriggerManager.FireMapEventAsync(innerSession.MapId, GameEvents.MapUnloaded, unloadCtx));
                TriggerManager.UnregisterMapTriggers(innerSession.MapId, unloadCtx);
            }

            // Pop focus — restores previous session
            var poppedId = MapSessions.PopFocused();
            if (innerSession != null)
            {
                MapSessions.UnloadSession(poppedId, World);
            }

            // Restore outer
            var outerSession = MapSessions.FocusedSession;
            if (outerSession != null)
            {
                CurrentMapSession = outerSession;
                SetService(CoreServiceKeys.MapSession, outerSession);

                var primaryBoard = outerSession.PrimaryBoard;
                if (primaryBoard != null)
                {
                    ApplyBoardSpatialConfig(primaryBoard);
                    LoadBoardTerrainData(outerSession, outerSession.MapConfig);
                    LoadNavForMap(outerSession.MapId.Value, outerSession.MapConfig);
                }
                SetMapEntitiesSuspended(outerSession.MapId, false);

                var resumeCtx = CreateContext();
                resumeCtx.Set(CoreServiceKeys.MapId, outerSession.MapId);
                foreach (var kvp in GlobalContext) resumeCtx.Set(kvp.Key, kvp.Value);
                CompleteLifecycleEvent(TriggerManager.FireMapEventAsync(outerSession.MapId, GameEvents.MapResumed, resumeCtx));
            }
        }

        private void ApplyDefaultCamera(MapConfig mapConfig)
        {
            var cam = mapConfig?.DefaultCamera;
            var registry = GetService(CoreServiceKeys.VirtualCameraRegistry)
                ?? throw new InvalidOperationException("VirtualCameraRegistry is required before loading maps.");

            string virtualCameraId = string.IsNullOrWhiteSpace(cam?.VirtualCameraId)
                ? "Default"
                : cam.VirtualCameraId;

            if (!registry.TryGet(virtualCameraId, out var definition) || definition == null)
            {
                if (!string.IsNullOrWhiteSpace(cam?.VirtualCameraId))
                {
                    throw new InvalidOperationException($"Map DefaultCamera.VirtualCameraId '{cam.VirtualCameraId}' is not registered.");
                }

                if (!registry.TryGet("Default", out definition) || definition == null)
                {
                    return;
                }

                virtualCameraId = definition.Id;
            }

            GameSession.Camera.ResetVirtualCameras();
            GameSession.Camera.ActivateVirtualCamera(
                virtualCameraId,
                blendDurationSeconds: 0f,
                followTarget: CameraFollowTargetFactory.Build(World, GlobalContext, definition.FollowTargetKind),
                snapToFollowTargetWhenAvailable: definition.SnapToFollowTargetWhenAvailable);

            if (cam != null)
            {
                GameSession.Camera.ApplyPose(new CameraPoseRequest
                {
                    VirtualCameraId = virtualCameraId,
                    TargetCm = (cam.TargetXCm.HasValue || cam.TargetYCm.HasValue)
                        ? new System.Numerics.Vector2(cam.TargetXCm ?? 0f, cam.TargetYCm ?? 0f)
                        : null,
                    Yaw = cam.Yaw,
                    Pitch = cam.Pitch,
                    DistanceCm = cam.DistanceCm,
                    FovYDeg = cam.FovYDeg
                });
            }

            var state = GameSession.Camera.State;
            Diagnostics.Log.Info(in LogChannels.Engine, $"Applied DefaultCamera: yaw={state.Yaw} pitch={state.Pitch} dist={state.DistanceCm}cm fov={state.FovYDeg}");
        }

        private void CreateBoardsForSession(MapSession session, MapConfig mapConfig)
        {
            if (mapConfig.Boards == null || mapConfig.Boards.Count == 0) return;

            foreach (var boardCfg in mapConfig.Boards)
            {
                var board = BoardFactory.Create(boardCfg, BoardIdRegistry);
                session.AddBoard(board);
                Diagnostics.Log.Info(in LogChannels.Engine, $"Created Board '{boardCfg.Name}' (type={boardCfg.SpatialType}) for map '{session.MapId}'");
            }
        }

        private void SetMapEntitiesSuspended(MapId mapId, bool suspended)
        {
            var entities = new List<Entity>();
            World.Query(in _mapEntitySuspendQuery, (Entity entity, ref MapEntity mapEntity) =>
            {
                if (mapEntity.MapId == mapId)
                {
                    entities.Add(entity);
                }
            });

            for (int i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                if (!World.IsAlive(entity)) continue;

                if (suspended)
                {
                    if (!World.Has<SuspendedTag>(entity))
                    {
                        World.Add(entity, new SuspendedTag());
                    }
                }
                else if (World.Has<SuspendedTag>(entity))
                {
                    World.Remove<SuspendedTag>(entity);
                }
            }
        }

        /// <summary>
        /// Apply a board's spatial config to engine-level spatial services.
        /// Replaces the old ApplyMapSpatialConfig(MapConfig).
        /// </summary>
        private void ApplyBoardSpatialConfig(IBoard board)
        {
            // Use the board's spatial services as engine-level defaults
            WorldSizeSpec = board.WorldSize;
            SpatialCoords = board.CoordinateConverter;
            _spatialPartition = board.SpatialPartition as ChunkedGridSpatialPartitionWorld
                ?? throw new InvalidOperationException(
                    $"Board '{board.Name}' exposed unsupported spatial partition '{board.SpatialPartition?.GetType().FullName}'.");

            if (SpatialQueries is not SpatialQueryService sharedSpatialQueries)
            {
                throw new InvalidOperationException(
                    $"Engine SpatialQueries must remain a stable {nameof(SpatialQueryService)} instance during board swaps.");
            }

            sharedSpatialQueries.SetBackend(new ChunkedGridSpatialPartitionBackend(_spatialPartition, WorldSizeSpec));
            sharedSpatialQueries.SetCoordinateConverter(SpatialCoords);
            SpatialQueries = sharedSpatialQueries;
            WireUpPositionProvider();

            ILoadedChunks? loadedChunks;
            // Wire up HexMetrics if this is a hex board
            if (board is HexGridBoard hexBoard)
            {
                sharedSpatialQueries.SetHexMetrics(hexBoard.HexMetrics);
                sharedSpatialQueries.SetLoadedChunks(hexBoard.HexGridAOI);
                SetService(CoreServiceKeys.HexMetrics, hexBoard.HexMetrics);
                SetService(CoreServiceKeys.LoadedChunks, (ILoadedChunks)hexBoard.HexGridAOI);
                HexGridAOI = hexBoard.HexGridAOI;
                loadedChunks = hexBoard.HexGridAOI;
            }
            else
            {
                GlobalContext.Remove(CoreServiceKeys.HexMetrics.Name);
                SetService(CoreServiceKeys.LoadedChunks, board.LoadedChunks);
                HexGridAOI = null;
                loadedChunks = board.LoadedChunks;
            }

            if (GlobalContext.TryGetValue(CoreServiceKeys.Navigation2DRuntime.Name, out var navigationObj) &&
                navigationObj is Navigation2DRuntime navigation2dRuntime)
            {
                navigation2dRuntime.BindLoadedChunks(loadedChunks);
            }

            // Update GlobalContext with rebuilt services
            SetService(CoreServiceKeys.WorldSizeSpec, WorldSizeSpec);
            SetService(CoreServiceKeys.SpatialCoordinateConverter, SpatialCoords);
            SetService(CoreServiceKeys.SpatialQueryService, SpatialQueries);

            // Hot-swap registered system references to prevent stale refs
            _worldToGridSyncSystem?.SetCoordinateConverter(SpatialCoords);
            if (_spatialPartition != null)
                _spatialPartitionUpdateSystem?.SetPartition(_spatialPartition, WorldSizeSpec);
        }

        private void LoadBoardTerrainData(MapSession session, MapConfig mapConfig)
        {
            VertexMap?.UnsubscribeFromLoadedChunks();
            VertexMap = null;

            foreach (var board in session.AllBoards)
            {
                if (board is ITerrainBoard terrainBoard)
                {
                    string dataFile = FindDataFileForBoard(board.Name, mapConfig);
                    if (!string.IsNullOrWhiteSpace(dataFile))
                    {
                        var vtxMap = LoadVertexMapFromFile(dataFile);
                        if (vtxMap != null)
                        {
                            terrainBoard.VertexMap = vtxMap;
                            VertexMap = vtxMap;
                            SetService(CoreServiceKeys.VertexMap, vtxMap);
                            Diagnostics.Log.Info(in LogChannels.Engine, $"Loaded VertexMap {vtxMap.WidthInChunks}x{vtxMap.HeightInChunks} for board '{board.Name}'");
                        }
                    }
                }
            }
        }

        private string FindDataFileForBoard(string boardName, MapConfig mapConfig)
        {
            if (mapConfig.Boards == null) return null;
            foreach (var b in mapConfig.Boards)
            {
                if (string.Equals(b.Name, boardName, StringComparison.OrdinalIgnoreCase))
                {
                    return b.DataFile;
                }
            }
            return null;
        }

        private VertexMap LoadVertexMapFromFile(string dataFile)
        {
            if (string.IsNullOrWhiteSpace(dataFile)) return null;

            if (dataFile.StartsWith("/") || dataFile.StartsWith("\\")) dataFile = dataFile.Substring(1);

            string rel = dataFile.Replace('\\', '/');
            var candidates = new List<string>(6) { rel };
            if (!rel.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add($"assets/{rel}");
            }
            if (!rel.Contains("Data/Maps", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add($"assets/Data/Maps/{rel}");
            }

            Stream TryOpen(string uri)
            {
                try { return VFS.GetStream(uri); }
                catch { return null; }
            }

            Stream stream = null;
            for (int i = 0; i < candidates.Count && stream == null; i++)
            {
                stream = TryOpen($"Core:{candidates[i]}");
            }

            if (stream == null)
            {
                foreach (var modId in ModLoader.LoadedModIds)
                {
                    for (int i = 0; i < candidates.Count && stream == null; i++)
                    {
                        stream = TryOpen($"{modId}:{candidates[i]}");
                    }
                    if (stream != null) break;
                }
            }

            if (stream == null) return null;

            try
            {
                return VertexMapBinary.Read(stream);
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Error(in LogChannels.Engine, $"Failed to load VertexMapBinary '{dataFile}': {ex.Message}");
                return null;
            }
            finally
            {
                stream.Dispose();
            }
        }

        private List<Trigger> InstantiateMapTriggers(MapDefinition definition, MapConfig mapConfig)
        {
            var triggers = new List<Trigger>();

            // From code-first MapDefinition.TriggerTypes
            if (definition?.TriggerTypes != null)
            {
                foreach (var triggerType in definition.TriggerTypes)
                {
                    try
                    {
                        var trigger = (Trigger)Activator.CreateInstance(triggerType);
                        triggers.Add(trigger);
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.Log.Error(in LogChannels.Engine, $"Failed to instantiate trigger type '{triggerType.Name}': {ex.Message}");
                    }
                }
            }

            // From JSON MapConfig.TriggerTypes (type names resolved via reflection)
            if (mapConfig?.TriggerTypes != null)
            {
                foreach (var typeName in mapConfig.TriggerTypes)
                {
                    var type = ResolveType(typeName);
                    if (type != null && typeof(Trigger).IsAssignableFrom(type))
                    {
                        try
                        {
                            var trigger = (Trigger)Activator.CreateInstance(type);
                            triggers.Add(trigger);
                        }
                        catch (Exception ex)
                        {
                            Diagnostics.Log.Error(in LogChannels.Engine, $"Failed to instantiate trigger '{typeName}': {ex.Message}");
                        }
                    }
                    else if (type == null)
                    {
                        Diagnostics.Log.Warn(in LogChannels.Engine, $"Could not resolve trigger type '{typeName}'");
                    }
                }
            }

            return triggers;
        }

        private void ApplyTriggerDecorators(List<Trigger> triggers)
        {
            if (TriggerDecoratorRegistry == null || triggers.Count == 0) return;

            for (int i = 0; i < triggers.Count; i++)
            {
                TriggerDecoratorRegistry.Apply(triggers[i]);
            }
        }

        private static Type ResolveType(string typeName)
        {
            // Try direct resolution first
            var type = Type.GetType(typeName);
            if (type != null) return type;

            // Search loaded assemblies
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(typeName);
                if (type != null) return type;
            }
            return null;
        }

        private void LoadNavForMap(string mapId, MapConfig mapConfig)
        {
            GlobalContext.Remove(CoreServiceKeys.NavMeshBakeConfig.Name);
            GlobalContext.Remove(CoreServiceKeys.NavMeshProfiles.Name);
            GlobalContext.Remove(CoreServiceKeys.NavQueryServices.Name);

            if (mapConfig?.Tags == null || mapConfig.Tags.Count == 0) return;
            bool navEnabled = false;
            for (int i = 0; i < mapConfig.Tags.Count; i++)
            {
                if (string.Equals(mapConfig.Tags[i], MapTags.FeatureNavMeshOn.Name, StringComparison.OrdinalIgnoreCase))
                {
                    navEnabled = true;
                    break;
                }
            }
            if (!navEnabled) return;

            if (VertexMap == null) throw new InvalidOperationException($"NavMesh enabled but VertexMap is not loaded for map '{mapId}'.");

            var bakeConfig = LoadNavMeshBakeConfig();
            SetService(CoreServiceKeys.NavMeshBakeConfig, bakeConfig);

            var profileRegistry = new NavMeshProfileRegistry(bakeConfig);
            SetService(CoreServiceKeys.NavMeshProfiles, profileRegistry);
            var areaCosts = BuildAreaCostTable(bakeConfig);
            if (bakeConfig.Layers == null || bakeConfig.Layers.Count == 0) throw new InvalidOperationException("NavMeshBakeConfig.layers is empty.");

            var stores = new Dictionary<NavQueryServiceKey, NavTileStore>(bakeConfig.Layers.Count * profileRegistry.Count);
            int widthChunks = VertexMap.WidthInChunks;
            int heightChunks = VertexMap.HeightInChunks;

            for (int li = 0; li < bakeConfig.Layers.Count; li++)
            {
                int layer = bakeConfig.Layers[li].Layer;
                for (int pi = 0; pi < profileRegistry.Count; pi++)
                {
                    int profileIndex = pi;
                    var uriCache = new Dictionary<NavTileId, string>(256);

                    string ResolveTileUri(NavTileId id)
                    {
                        if (id.Layer != layer) throw new InvalidOperationException($"NavTileId.Layer mismatch. Expected={layer}, actual={id.Layer}.");
                        if (uriCache.TryGetValue(id, out var cached)) return cached;
                        string profileId = profileRegistry.GetId(profileIndex);
                        string rel = NavAssetPaths.GetNavTileRelativePath(mapId, layer, profileId, id.ChunkX, id.ChunkY);
                        string uri = ResolveSingleExistingUri(rel);
                        uriCache[id] = uri;
                        return uri;
                    }

                    for (int cy = 0; cy < heightChunks; cy++)
                    {
                        for (int cx = 0; cx < widthChunks; cx++)
                        {
                            _ = ResolveTileUri(new NavTileId(cx, cy, layer));
                        }
                    }

                    var store = new NavTileStore(id => VFS.GetStream(ResolveTileUri(id)));
                    stores[new NavQueryServiceKey(layer, profileIndex)] = store;
                }
            }

            SetService(CoreServiceKeys.NavQueryServices, new NavQueryServiceRegistry(stores));
        }

        private NavMeshBakeConfig LoadNavMeshBakeConfig()
        {
            string rel = NavMeshConfigPaths.BakeConfigPath;
            string uri = ResolveSingleExistingUri(rel);
            using var stream = VFS.GetStream(uri);
            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            opts.Converters.Add(new JsonStringEnumConverter());
            var cfg = JsonSerializer.Deserialize<NavMeshBakeConfig>(stream, opts);
            if (cfg == null) throw new InvalidOperationException($"Failed to deserialize NavMeshBakeConfig from '{uri}'.");
            return cfg;
        }

        private string ResolveSingleExistingUri(string relPath)
        {
            if (string.IsNullOrWhiteSpace(relPath)) throw new ArgumentException("relPath is required.", nameof(relPath));
            string rel = relPath.Replace('\\', '/');

            if (TryResolveSingleExistingUri(rel, out var uri)) return uri;
            throw new FileNotFoundException($"Missing asset: {rel}");
        }

        private bool TryResolveSingleExistingUri(string rel, out string uri)
        {
            string foundCore = null;
            if (VFS.TryResolveFullPath($"Core:{rel}", out var fullCore) && File.Exists(fullCore))
            {
                foundCore = $"Core:{rel}";
            }

            string foundMod = null;
            int modCount = 0;
            for (int i = 0; i < ModLoader.LoadedModIds.Count; i++)
            {
                string modId = ModLoader.LoadedModIds[i];
                if (!VFS.TryResolveFullPath($"{modId}:{rel}", out var full)) continue;
                if (!File.Exists(full)) continue;
                modCount++;
                foundMod = $"{modId}:{rel}";
            }

            if (modCount > 1) throw new InvalidOperationException($"Asset conflict (multiple mods): {rel}");
            if (modCount == 1)
            {
                uri = foundMod;
                return true;
            }
            if (foundCore != null)
            {
                uri = foundCore;
                return true;
            }
            uri = null;
            return false;
        }

        private static NavAreaCostTable BuildAreaCostTable(NavMeshBakeConfig cfg)
        {
            var arr = new Fix64[256];
            for (int i = 0; i < arr.Length; i++) arr[i] = Fix64.OneValue;
            if (cfg?.Areas != null)
            {
                for (int i = 0; i < cfg.Areas.Count; i++)
                {
                    var a = cfg.Areas[i];
                    if (a == null) continue;
                    if (a.AreaId < 0 || a.AreaId > 255) throw new InvalidOperationException($"NavMeshBakeConfig.areas has invalid areaId: {a.AreaId}");
                    if (a.Cost <= 0) throw new InvalidOperationException($"NavMeshBakeConfig.areas has invalid cost for areaId={a.AreaId}");
                    arr[a.AreaId] = Fix64.FromFloat(a.Cost);
                }
            }
            return new NavAreaCostTable(arr);
        }

        public void LoadEntryMap(string mapId) => LoadMap(mapId);

        public void LoadMap(MapId mapId) => LoadMap(mapId.Value);

        public void Start()
        {
            _isRunning = true;
            Time.TotalTime = 0;
            Time.FixedTotalTime = 0;
            _cooperativeSimulation?.Reset();
            if (Pacemaker is RealtimePacemaker realtime) realtime.Reset();

            var ctx = new ScriptContext();
            ctx.Set(CoreServiceKeys.World, World);
            ctx.Set(CoreServiceKeys.WorldMap, WorldMap);
            ctx.Set(CoreServiceKeys.GameSession, GameSession);
            ctx.Set(CoreServiceKeys.Engine, this);
            ctx.Set(CoreServiceKeys.WorldSizeSpec, WorldSizeSpec);
            ctx.Set(CoreServiceKeys.SpatialCoordinateConverter, SpatialCoords);
            ctx.Set(CoreServiceKeys.SpatialQueryService, SpatialQueries);
            foreach (var kvp in GlobalContext) ctx.Set(kvp.Key, kvp.Value);

            Diagnostics.Log.Info(in LogChannels.Engine, "Firing GameStart event...");
            CompleteLifecycleEvent(TriggerManager.FireEventAsync(GameEvents.GameStart, ctx));
        }

        public void Stop()
        {
            _isRunning = false;
        }

        private void CompleteLifecycleEvent(System.Threading.Tasks.Task task)
        {
            if (task == null)
            {
                return;
            }

            while (!task.IsCompleted)
            {
                SyncContext?.ProcessQueue();
                System.Threading.Thread.Yield();
            }

            task.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Stop();
            if (_jobScheduler != null)
            {
                Diagnostics.Log.Info(in LogChannels.Engine, "Disposing JobScheduler...");
                _jobScheduler.Dispose();
                _jobScheduler = null;
                World.SharedJobScheduler = null;
            }
            
            if (World != null)
            {
                World.Destroy(World);
                World = null;
            }
        }

        public void Tick(float platformDeltaTime)
        {
            if (!_isRunning) return;

            float dt = platformDeltaTime * Time.TimeScale;
            Time.DeltaTime = dt;
            Time.TotalTime += dt;
            
            GameTask.Update(dt);
            SyncContext.ProcessQueue();
            EnsureCameraRuntimeConfigured();
            _inputRuntimeSystem?.Update(dt);

            // 1. Simulation Loop (GAS, Physics, AI) - Controlled by Pacemaker
            if (!_simulationBudgetFused)
            {
                int tickBefore = GameSession.CurrentTick;
                Pacemaker.Update(dt, _cooperativeSimulation, SimulationBudgetMsPerFrame, SimulationMaxSlicesPerLogicFrame);

                bool fused = (Pacemaker is RealtimePacemaker rt && rt.IsBudgetFused) ||
                             (Pacemaker is TurnBasedPacemaker tb && tb.IsBudgetFused);

                if (fused)
                {
                    _simulationBudgetFused = true;
                    Diagnostics.Log.Warn(in LogChannels.Engine, $"BudgetFuse: Simulation halted at LogicTick={tickBefore} (budgetMs={SimulationBudgetMsPerFrame}, sliceLimit={SimulationMaxSlicesPerLogicFrame})");

                    if (World != null)
                    {
                        World.Create(new SimulationBudgetFuseEvent
                        {
                            LogicTick = tickBefore,
                            BudgetMs = SimulationBudgetMsPerFrame,
                            SliceLimit = SimulationMaxSlicesPerLogicFrame,
                            Reason = 1
                        });
                    }

                    var ctx = CreateContext();
                    ctx.Set("LogicTick", tickBefore);
                    ctx.Set("BudgetMs", SimulationBudgetMsPerFrame);
                    ctx.Set("SliceLimit", SimulationMaxSlicesPerLogicFrame);
                    TriggerManager.FireEvent(GameEvents.SimulationBudgetFused, ctx);
                }
            }

            // 2. Visual Loop (Rendering, UI, Animation) - Always runs
            Update(dt); 
        }

        private void Update(float dt)
        {
            _primitiveDrawBuffer?.Clear();
            _visualSnapshotBuffer?.Clear();
            _groundOverlayBuffer?.Clear();
            _worldHudBuffer?.Clear();
            for (int i = 0; i < _presentationSystems.Count; i++)
            {
                _presentationSystems[i].Update(dt);
            }
            // Clear GAS presentation events AFTER all presentation systems have consumed them
            _gasPresentationEvents?.Clear();
        }

        private void EnsureCameraRuntimeConfigured()
        {
            var input = GetService(CoreServiceKeys.InputHandler);
            var viewport = GetService(CoreServiceKeys.ViewController);
            if (input == null || viewport == null)
            {
                return;
            }

            if (!GameSession.Camera.IsRuntimeConfigured)
            {
                GameSession.Camera.ConfigureRuntime(input, viewport, () => WorldSizeSpec.Bounds);
            }
        }
    }
}
