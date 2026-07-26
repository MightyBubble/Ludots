using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Ludots.Core.Association;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Config;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.EntityQueries;
using Ludots.Core.Map;
using Ludots.Core.Map.Hex;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.AI.Systems;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Exchange;
using Ludots.Core.Gameplay.Narrative;
using Ludots.Core.Gameplay.Quests;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.GAS.Bindings;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Spawning.Systems;
using Ludots.Core.Gameplay.Lifecycle;
using Schedulers; // Added for JobScheduler
using Ludots.Core.Systems;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Physics;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Knowledge;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.Core.UI.CommandDeck;
using Ludots.Core.UI.ProductionOverview;
using Ludots.Core.Input.Attributes;
using Ludots.Core.Input.Systems;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Diagnostics;
using Ludots.Core.Presentation.ChunkDebug;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Instancing;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Presentation.Surfaces;
using Ludots.Core.Vision.Config;
// Indicators directory removed — unified into Performers
using Ludots.Core.Presentation.Performers;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Spatial;
using Ludots.Core.StructureCollision;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Components;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Navigation.AOI;
using Ludots.Core.Diagnostics;
using Ludots.Core.Map.Board;
using Ludots.Core.Gameplay.Camera.FollowTargets;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphSemantics.GAS;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Registry;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Hosting;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Gameplay.Progression.Config;
using Ludots.Core.Gameplay.Progression.Systems;
using Ludots.Core.Persistence;
using Ludots.Core.Vision;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;

namespace Ludots.Core.Engine
{
    public enum SystemGroup
    {
        // Phase 0: Schema更新（运行时注册：属性/Graph等）
        // 说明：为保证确定性，运行时schema变更通过队列提交，在每帧开始统一生效
        SchemaUpdate,

        // Phase 0.5: 本地输入意图。复制客户端只执行这一组，不运行权威仿真。
        LocalInput,

        // Phase 1: 输入与状态收集
        InputCollection,

        // Phase 1.5: 移动后同步与空间更新（物理/导航输出落地后的 SSOT 更新）
        PostMovement,

        // Phase 2: 能力激活
        AbilityActivation,

        // Phase 3: Effect处理（含响应链）
        EffectProcessing,

        // Phase 3.5: 运行时实体创建后的能力绑定
        // 目的：让 RuntimeEntitySpawnSystem 创建的 ECS-authored 实体由各 capability runtime 统一发现、验证并绑定
        RuntimeEntityBinding,

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

    public partial class GameEngine : IDisposable // Implement IDisposable
    {
        private const int PathStoreMaxPaths = 512;
        private const int PathStoreMaxPointsPerPath = 256;
        private const string SkipDefaultCameraOnLoadTag = "camera.skip_default_on_load";

        private bool _isRunning;
        private bool _gameStartCompleted;
        private bool _networkRuntimeActivationAttempted;
        private bool _networkRuntimeActivating;
        private bool _networkRuntimeReady;
        private bool _networkStartupMapLoadObserved;
        private Exception? _networkStartupFailure;
        private EffectTemplateLoader _effectTemplateLoader;
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
        public LogicTerrainField LogicTerrain { get; private set; }
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
        public GameplayEventBus EventBus { get; private set; }

        private readonly TypedServiceScope _engineServices = new("engine");

        public Dictionary<string, object> GlobalContext => _engineServices.LegacyStore;

        public void SetService<T>(ServiceKey<T> key, T value)
            => _engineServices.Set(key, value);

        public T GetService<T>(ServiceKey<T> key)
            => _engineServices.GetOrDefault(key);

        public bool TryGetService<T>(ServiceKey<T> key, out T value)
            => _engineServices.TryGet(key, out value);

        public bool RemoveService<T>(ServiceKey<T> key)
            => _engineServices.Remove(key);

        public void RegisterPresentationAdapterCapabilities(PresentationAdapterCapabilities capabilities)
        {
            if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));

            TryGetService(CoreServiceKeys.PresentationTargetGeneration, out PresentationTargetGeneration targetGeneration);
            PresentationVisualCapabilityValidator.ValidateTargetLifecycle(capabilities, targetGeneration);
            SetService(CoreServiceKeys.PresentationAdapterCapabilities, capabilities);
        }

        public GameSynchronizationContext SyncContext { get; private set; }

        // Systems - 按Phase分组
        private Dictionary<SystemGroup, List<ISystem<float>>> _systemGroups = new Dictionary<SystemGroup, List<ISystem<float>>>();
        private List<ISystem<float>> _presentationSystems = new List<ISystem<float>>();
        private ISystem<float> _inputRuntimeSystem;
        private CameraRuntimeSystem _cameraRuntimeSystem;
        private Ludots.Core.Presentation.Rendering.PrimitiveDrawBuffer _primitiveDrawBuffer;
        private Ludots.Core.Presentation.Rendering.PrimitiveDrawBuffer _visualSnapshotBuffer;
        private Ludots.Core.Presentation.Rendering.PresentationVisualProxyBuffer _visualProxyBuffer;
        private Ludots.Core.Presentation.Rendering.SkinnedVisualBatchBuffer _skinnedVisualBatchBuffer;
        private Ludots.Core.Presentation.Requests.PresentationRequestBuffer _presentationRequestBuffer;
        private Ludots.Core.Presentation.Rendering.GlobalFieldVisualBuffer _globalFieldVisualBuffer;
        private Ludots.Core.Presentation.Requests.SoundRequestBuffer _soundRequestBuffer;
        private Ludots.Core.Presentation.Instancing.InstancedBatchRequestBuffer _instancedBatchRequestBuffer;
        private Ludots.Core.Presentation.Instancing.InstancedBatchOperationBuffer _instancedBatchOperationBuffer;
        private GasPresentationEventBuffer _gasPresentationEvents;
        private GasGraphRuntimeApi _gasGraphRuntimeApi;
        private Ludots.Core.Presentation.Rendering.GroundOverlayBuffer _groundOverlayBuffer;
        private Ludots.Core.Presentation.Rendering.RoadSplineBuffer _roadSplineBuffer;
        private Ludots.Core.Presentation.Hud.WorldHudBatchBuffer _worldHudBuffer;
        private Physics2DController _physics2DController;
        private Ludots.Core.Gameplay.GAS.GasController _gasController;
        private TimeFlowService _timeFlow;

        // Spatial systems — kept for hot-swap on map load
        private WorldToGridSyncSystem _worldToGridSyncSystem;
        private SpatialPartitionUpdateSystem _spatialPartitionUpdateSystem;
        private readonly MassNavigationRuntime _massNavigationRuntime = new();

        // Multithreading
        private JobScheduler _jobScheduler;

        /// <summary>
        /// The final merged GameConfig from all sources (Core + Mods).
        /// Available after InitializeWithConfigPipeline is called.
        /// </summary>
        public GameConfig MergedConfig { get; private set; }

        public void RegisterSystem(ISystem<float> system, SystemGroup group)
        {
            if (group == SystemGroup.LocalInput && system is ITimeSlicedSystem)
            {
                throw new InvalidOperationException(
                    $"LocalInput system '{system.GetType().Name}' cannot be time-sliced because replicated clients execute local intent exactly once per visual frame.");
            }

            if (!_systemGroups.ContainsKey(group))
            {
                _systemGroups[group] = new List<ISystem<float>>();
            }
            _systemGroups[group].Add(system);

            system.Initialize();
        }

        public bool UnregisterSystem(ISystem<float> system, SystemGroup group)
        {
            if (system == null)
            {
                throw new ArgumentNullException(nameof(system));
            }

            if (!_systemGroups.TryGetValue(group, out var systems))
            {
                return false;
            }

            for (int i = 0; i < systems.Count; i++)
            {
                if (!ReferenceEquals(systems[i], system))
                {
                    continue;
                }

                systems.RemoveAt(i);
                system.Dispose();
                return true;
            }

            return false;
        }

        public void InsertSystemBeforeRequired<TAnchor>(ISystem<float> system, SystemGroup group)
            where TAnchor : class
        {
            if (!_systemGroups.TryGetValue(group, out var systems))
            {
                throw new InvalidOperationException(
                    $"Cannot insert system before required anchor '{typeof(TAnchor).Name}' because group '{group}' has not been registered.");
            }

            for (int i = 0; i < systems.Count; i++)
            {
                if (systems[i] is TAnchor)
                {
                    systems.Insert(i, system);
                    system.Initialize();
                    return;
                }
            }

            throw new InvalidOperationException(
                $"Cannot insert system before required anchor '{typeof(TAnchor).Name}' in group '{group}' because the anchor is missing.");
        }

        public void RegisterPresentationSystem(ISystem<float> system)
        {
            _presentationSystems.Add(system);
            system.Initialize();
        }

        public bool UnregisterPresentationSystem(ISystem<float> system)
        {
            if (system == null)
            {
                throw new ArgumentNullException(nameof(system));
            }

            for (int i = 0; i < _presentationSystems.Count; i++)
            {
                if (!ReferenceEquals(_presentationSystems[i], system))
                {
                    continue;
                }

                _presentationSystems.RemoveAt(i);
                system.Dispose();
                return true;
            }

            return false;
        }

        public void InsertPresentationSystemBefore<TAnchor>(ISystem<float> system)
            where TAnchor : class
        {
            for (int i = 0; i < _presentationSystems.Count; i++)
            {
                if (_presentationSystems[i] is TAnchor)
                {
                    _presentationSystems.Insert(i, system);
                    system.Initialize();
                    return;
                }
            }

            throw new InvalidOperationException(
                $"Cannot insert presentation system before required anchor '{typeof(TAnchor).Name}' because the anchor is missing.");
        }

        public ScriptContext CreateContext()
        {
            var ctx = new ScriptContext();
            ctx.Set(CoreServiceKeys.World, World);
            ctx.Set(CoreServiceKeys.WorldMap, WorldMap);
            ctx.Set(CoreServiceKeys.VertexMap, VertexMap);
            ctx.Set(CoreServiceKeys.LogicTerrain, LogicTerrain);
            ctx.Set(CoreServiceKeys.GameSession, GameSession);
            ctx.Set(CoreServiceKeys.Engine, this);
            ctx.Set(CoreServiceKeys.WorldSizeSpec, WorldSizeSpec);
            ctx.Set(CoreServiceKeys.SpatialCoordinateConverter, SpatialCoords);
            ctx.Set(CoreServiceKeys.SpatialQueryService, SpatialQueries);
            ctx.MergeFrom(_engineServices);

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
            InitializeWithConfigPipelineInternal(modPaths, null, assetsRoot);
        }

        public void InitializeWithConfigPipeline(ResolvedModLoadPlan modPlan, string assetsRoot)
        {
            if (modPlan == null)
            {
                throw new ArgumentNullException(nameof(modPlan));
            }

            InitializeWithConfigPipelineInternal(null, modPlan, assetsRoot);
        }

        private void InitializeWithConfigPipelineInternal(List<string>? modPaths, ResolvedModLoadPlan? modPlan, string assetsRoot)
        {
            // Early log bootstrap with console backend — will be upgraded after config merge
            if (Diagnostics.Log.Backend is NullLogBackend)
                Diagnostics.Log.Initialize(new ConsoleLogBackend());
            Diagnostics.Log.Info(in LogChannels.Engine, "Initializing with ConfigPipeline...");

            // Setup Async Context
            var previousSyncContext = System.Threading.SynchronizationContext.Current;
            SyncContext = new GameSynchronizationContext();
            System.Threading.SynchronizationContext.SetSynchronizationContext(SyncContext);

            void RestoreInitializationFailureState()
            {
                try
                {
                    ModLoader?.UnloadAll();
                }
                catch (Exception unloadEx)
                {
                    Diagnostics.Log.Error(in LogChannels.Engine, $"Failed to unload partially initialized mod state: {unloadEx}");
                }

                System.Threading.SynchronizationContext.SetSynchronizationContext(previousSyncContext);
                SyncContext = previousSyncContext as GameSynchronizationContext;
            }

            // Setup conflict report for mod registration tracing
            ConflictReport = new RegistrationConflictReport();
            Ludots.Core.Config.ComponentRegistry.SetConflictReport(ConflictReport);
            SetService(CoreServiceKeys.Engine, this);
            SetService(CoreServiceKeys.RegistrationConflictReport, ConflictReport);
            if (modPlan != null)
            {
                SetService(CoreServiceKeys.ModLoadPlan, modPlan);
            }
            else
            {
                RemoveService(CoreServiceKeys.ModLoadPlan);
            }

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
            OrderBlackboardKeyRegistry.ResetToBuiltins();

            try
            {
            // 2. Load Mods first (so ConfigPipeline can access their game.json)
            if (modPlan != null && modPlan.OrderedMods.Count > 0)
            {
                if (!string.IsNullOrWhiteSpace(modPlan.PlanFingerprint))
                {
                    Diagnostics.Log.Info(
                        in LogChannels.Engine,
                        $"Applying launcher-resolved mod plan: fingerprint={modPlan.PlanFingerprint}, schema={modPlan.SchemaVersion?.ToString() ?? "explicit"}, mods={modPlan.OrderedMods.Count}");
                }
                else
                {
                    Diagnostics.Log.Info(in LogChannels.Engine, $"Applying explicit mod plan: mods={modPlan.OrderedMods.Count}");
                }

                ModLoader.LoadResolvedPlan(modPlan.OrderedMods);
            }
            else if (modPaths != null && modPaths.Count > 0)
            {
                Diagnostics.Log.Info(in LogChannels.Engine, $"Resolving mod dependencies from explicit mod paths: mods={modPaths.Count}");
                ModLoader.LoadMods(modPaths);
            }

            // 3. Create ConfigPipeline and merge all game.json files
            ConfigPipeline = new ConfigPipeline((VirtualFileSystem)VFS, ModLoader);
            ((MapManager)MapManager).SetConfigPipeline(ConfigPipeline);
            MergedConfig = ConfigPipeline.MergeGameConfig();
            (MergedConfig.Presentation
                ?? throw new InvalidOperationException("game.json presentation must be explicitly configured.")).Validate();

            ConfigCatalog = Ludots.Core.Config.ConfigCatalogLoader.Load(ConfigPipeline);
            ConfigConflictReport = new Ludots.Core.Config.ConfigConflictReport();
            LoadAgentProfiles();
            AiRuntime = default;
            Ludots.Core.Config.ComponentRegistry.SetUtilityAiAuthoringCatalog(null);

            // Apply log config from merged game.json
            LogConfigApplier.Apply(MergedConfig.Logging);

            Diagnostics.Log.Info(in LogChannels.Engine, $"Merged GameConfig: StartupMapId={MergedConfig.StartupMapId}, DefaultCoreMod={MergedConfig.DefaultCoreMod}");
            Diagnostics.Log.Info(in LogChannels.Engine, $"Constants loaded: OrderTypeIds={MergedConfig.Constants.OrderTypeIds.Count}, ResponseChainOrderTypeIds={MergedConfig.Constants.ResponseChainOrderTypeIds.Count}");

            // Store merged config in GlobalContext for access throughout the engine
            SetService(CoreServiceKeys.GameConfig, MergedConfig);
            SetService(CoreServiceKeys.ConfigCatalog, ConfigCatalog);
            SetService(CoreServiceKeys.ConfigConflictReport, ConfigConflictReport);
            }
            catch
            {
                RestoreInitializationFailureState();
                throw;
            }

            try
            {
                // 4. Setup ECS & Session using merged config values
                InitializeWorld(MergedConfig.WorldWidthInMacroTiles, MergedConfig.WorldHeightInMacroTiles);
                SetService(CoreServiceKeys.World, World);
                WorldMap = new WorldMap(MergedConfig.WorldWidthInMacroTiles, MergedConfig.WorldHeightInMacroTiles);
                SetService(CoreServiceKeys.WorldMap, WorldMap);
                GameSession = new GameSession();
                SetService(CoreServiceKeys.GameSession, GameSession);
                int gridCellSizeCm = MergedConfig.GridCellSizeCm;
                WorldSizeSpec = new WorldExtentSpec(
                    MergedConfig.WorldWidthInMacroTiles,
                    MergedConfig.WorldHeightInMacroTiles,
                    gridCellSizeCm).ToWorldSizeSpec();
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
                MapLoader.LoadTemplates(ConfigCatalog, ConfigConflictReport);
                SetService(CoreServiceKeys.EntityTemplateKeyRegistry, MapLoader.EntityTemplateKeys);

                // 6. Initialize Core Systems with merged config
                InitializeCoreSystems(MergedConfig);

                TriggerManager.RegisterTrigger(new Ludots.Core.Config.ReloadConfigTrigger(this));

                SimulationBudgetMsPerFrame = MergedConfig.SimulationBudgetMsPerFrame;
                SimulationMaxSlicesPerLogicFrame = MergedConfig.SimulationMaxSlicesPerLogicFrame;

                // 7. Print registration conflict summary
                ConflictReport?.PrintSummary();
            }
            catch
            {
                RestoreInitializationFailureState();
                throw;
            }
        }

        public void RebuildAiRuntime()
        {
            if (ConfigPipeline == null)
            {
                AiRuntime = default;
                Ludots.Core.Config.ComponentRegistry.SetUtilityAiAuthoringCatalog(null);
                return;
            }

            var atoms = new Ludots.Core.Gameplay.AI.WorldState.AtomRegistry(capacity: 256);
            Ludots.Core.Gameplay.AI.Config.AiConfigValidationContext? validation = null;
            if (TryGetService(CoreServiceKeys.OrderTypeRegistry, out OrderTypeRegistry orderTypes) && orderTypes != null)
            {
                validation = new Ludots.Core.Gameplay.AI.Config.AiConfigValidationContext(
                    orderTypes,
                    GetService(CoreServiceKeys.AbilityDefinitionRegistry),
                    GetService(CoreServiceKeys.GraphProgramRegistry));
            }

            var loader = new Ludots.Core.Gameplay.AI.Config.AiConfigLoader(ConfigPipeline, atoms, validation);
            var catalog = ConfigCatalog ?? Ludots.Core.Gameplay.AI.Config.AiConfigCatalog.CreateDefault();
            AiRuntime = loader.LoadAndCompile(catalog, ConfigConflictReport);
            Ludots.Core.Config.ComponentRegistry.SetUtilityAiAuthoringCatalog(AiRuntime.UtilityRuntime.Authoring);
        }

        private void LoadAgentProfiles()
        {
            if (ConfigPipeline == null)
            {
                RemoveService(CoreServiceKeys.AgentProfiles);
                return;
            }

            AgentProfileRegistry agentProfiles = new AgentProfileConfigLoader(ConfigPipeline)
                .Load(ConfigCatalog, ConfigConflictReport);
            SetService(CoreServiceKeys.AgentProfiles, agentProfiles);
        }

        public void ReloadConfigs(string? group = null, string? relativePath = null)
        {
            if (ConfigPipeline == null) return;

            ConfigCatalog = Ludots.Core.Config.ConfigCatalogLoader.Load(ConfigPipeline);
            ConfigConflictReport = new Ludots.Core.Config.ConfigConflictReport();
            LoadAgentProfiles();

            bool reloadAi = string.IsNullOrWhiteSpace(group)
                         || string.Equals(group, "AI", StringComparison.OrdinalIgnoreCase)
                         || (!string.IsNullOrWhiteSpace(relativePath) && relativePath.StartsWith("AI/", StringComparison.OrdinalIgnoreCase));

            if (reloadAi) RebuildAiRuntime();

            bool reloadNarrative = string.IsNullOrWhiteSpace(group)
                                 || string.Equals(group, "Narrative", StringComparison.OrdinalIgnoreCase)
                                 || (!string.IsNullOrWhiteSpace(relativePath) && relativePath.StartsWith("Narrative/", StringComparison.OrdinalIgnoreCase));
            bool reloadQuests = string.IsNullOrWhiteSpace(group)
                             || string.Equals(group, "Quests", StringComparison.OrdinalIgnoreCase)
                             || (!string.IsNullOrWhiteSpace(relativePath) && relativePath.StartsWith("Quests/", StringComparison.OrdinalIgnoreCase));

            if (reloadQuests &&
                GetService(CoreServiceKeys.QuestDefinitionRegistry) is QuestDefinitionRegistry questDefinitions &&
                GetService(CoreServiceKeys.QuestRuntimeService) is QuestRuntimeService questRuntime)
            {
                new QuestConfigLoader(ConfigPipeline, questDefinitions).Load(ConfigCatalog, ConfigConflictReport);
                questRuntime.ResetState();
            }

            if (reloadNarrative && GetService(CoreServiceKeys.NarrativeDefinitions) is NarrativeDefinitionRegistry narrativeDefinitions)
            {
                new NarrativeConfigLoader(ConfigPipeline, narrativeDefinitions).Load(ConfigCatalog, ConfigConflictReport);
                if (GetService(CoreServiceKeys.NarrativeDirector) is NarrativeDirector narrativeDirector)
                {
                    narrativeDirector.ResetNarrativeState();
                }
            }

            SetService(CoreServiceKeys.ConfigCatalog, ConfigCatalog);
            SetService(CoreServiceKeys.ConfigConflictReport, ConfigConflictReport);
            SetService(CoreServiceKeys.AiRuntime, AiRuntime);
        }

        private void InitializeWorld(int widthInMacroTiles, int heightInMacroTiles)
        {
            World = World.Create();
            PhysicsWorld = new PhysicsWorld(widthInChunks: widthInMacroTiles, heightInChunks: heightInMacroTiles);
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

        private static void RegisterBuiltInEntityCollectionKeys(StringIntRegistry registry)
        {
            registry.Register(EntityCollectionKeys.UiCommandAcquisition);
            registry.Register(EntityCollectionKeys.HoveredEntity);
            registry.Register(EntityCollectionKeys.AbilityAimHover);
            registry.Register(EntityCollectionKeys.AbilityAimAffected);
            registry.Register(EntityCollectionKeys.EntityInfoExplicit);
            registry.Register(EntityCollectionKeys.CommandSource);
            registry.Register(EntityCollectionKeys.UiCastRaw);
            registry.Register(EntityViewKeys.ControlPlaneCommand);
            registry.Register(EntityViewKeys.CommandDeckFiltered);
        }

        private void InitializeCoreSystems(GameConfig config)
        {
            Diagnostics.Log.Info(in LogChannels.Engine, "Initializing Core GAS Systems...");
            // Instantiate GAS Systems
            var engineClockConfigLoader = new EngineClockConfigLoader(ConfigPipeline);
            var engineClockConfig = engineClockConfigLoader.Load(ConfigCatalog, ConfigConflictReport);
            Time.FixedDeltaTime = 1f / engineClockConfig.FixedHz;
            _timeFlow = new TimeFlowService();
            Time.TimeScale = _timeFlow.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation) / 1000f;

            var extensionAttributeRegistry = new ExtensionAttributeRegistry();
            var attributeSchemaUpdateQueue = new AttributeSchemaUpdateQueue();
            var schemaUpdateSystem = new AttributeSchemaUpdateSystem(World, extensionAttributeRegistry, attributeSchemaUpdateQueue);
            var gasBudget = new GasBudget();
            var gasDiagnostics = new GasDiagnosticEventBuffer();
            TeamManager.DefaultRelationship = TeamRelationship.Hostile;
            var teamEntityLookup = new TeamEntityLookup();
            var playerEntityLookup = new PlayerEntityLookup();
            var relationshipTypeRegistry = new RelationshipTypeRegistry();
            var relationshipMetricRegistry = new RelationshipMetricRegistry();
            var relationshipFlagRegistry = new RelationshipFlagRegistry();
            var relationshipBandRegistry = new RelationshipBandRegistry();
            var relationshipReasonRegistry = new RelationshipReasonRegistry();
            var relationshipChangeBuffer = new RelationshipChangeBuffer();
            var relationshipRuntime = new RelationshipRuntime(World, relationshipTypeRegistry, relationshipMetricRegistry, relationshipFlagRegistry, relationshipBandRegistry, relationshipChangeBuffer, new RelationshipReverseIndex(World));
            var gasRuntimeCapacity = config.GasRuntimeCapacity
                ?? throw new InvalidOperationException("GameConfig.gasRuntimeCapacity is required.");
            gasRuntimeCapacity.Validate();
            var orderTerminalResults = new OrderTerminalResultBuffer(
                gasRuntimeCapacity.OrderTerminalResultCapacity);
            var orderTypeRegistry = new OrderTypeRegistry(orderTerminalResults);
            var orderRuleRegistry = new OrderRuleRegistry();
            var dirtyEntities = new DirtyEntityQueue(gasRuntimeCapacity.DeferredTriggerActiveEntityCapacity);
            var tagOps = new TagOps(dirtyEntities, new TagRuleRegistry(), gasBudget);
            var entityCollectionKeyRegistry = new StringIntRegistry(capacity: 64, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            RegisterBuiltInEntityCollectionKeys(entityCollectionKeyRegistry);
            var entityCollectionStore = new EntityCollectionStore(entityCollectionKeyRegistry, initialCollectionCapacity: 128, initialRowCapacity: 4096);
            var relationshipCatalog = new RelationshipCatalogPipelineLoader(ConfigPipeline).Load(ConfigCatalog, ConfigConflictReport);
            var relationshipCatalogRuntime = RelationshipCatalogInstaller.Install(
                relationshipCatalog,
                relationshipTypeRegistry,
                relationshipMetricRegistry,
                relationshipFlagRegistry,
                relationshipBandRegistry,
                relationshipReasonRegistry,
                entityCollectionStore);
            // Control-plane relationship types must ship in the default relationship catalog (RFC-0065 DEC-1/DEC-3); GetId fails fast when missing.
            // Control-plane reserved relationship types (RFC-0065 DEC-1): the engine binds these three
            // names as its contract with the catalog. Scenario-level types (e.g. alliance/diplomacy)
            // are pure data — profiles referencing them fail fast at their own load site instead.
            int ownsRelationshipTypeId = relationshipTypeRegistry.GetId("Owns");
            int controlsRelationshipTypeId = relationshipTypeRegistry.GetId("Controls");
            int memberOfRelationshipTypeId = relationshipTypeRegistry.GetId("MemberOf");
            var ownershipResolver = new OwnershipResolver(relationshipRuntime, ownsRelationshipTypeId);
            var controlDomainQuery = new ControlDomainQuery(
                World,
                relationshipRuntime,
                ownershipResolver,
                ownsRelationshipTypeId,
                controlsRelationshipTypeId);
            var domainStanceQuery = DomainStanceQuery.Create(
                relationshipRuntime,
                memberOfRelationshipTypeId,
                relationshipCatalog.Stance);
            var domainRoutedCollectionWriter = new DomainRoutedCollectionWriter(entityCollectionStore, controlDomainQuery);
            var controlPlaneView = new ControlPlaneView(entityCollectionStore, controlDomainQuery);
            // Infrastructure flag marking profile-granted edges (RFC-0065 CTRL-4b); registration is idempotent.
            int grantedRelationshipFlagId = relationshipFlagRegistry.Register(AssociationControlProfileRuntime.GrantedFlagName);
            var associationControlProfileCatalog = new AssociationControlProfilePipelineLoader(ConfigPipeline).Load(ConfigCatalog, ConfigConflictReport);
            var associationControlProfileRuntime = AssociationControlProfileRuntime.Create(
                World,
                relationshipRuntime,
                tagOps,
                relationshipTypeRegistry,
                associationControlProfileCatalog,
                grantedRelationshipFlagId);
            var relationshipProcessingSystem = new RelationshipProcessingSystem(this, relationshipChangeBuffer, tagOps, teamEntityLookup);
            var entitySetQueryRuntime = new EntitySetQueryRuntime(World, tagOps, relationshipRuntime);
            var effectTemplateRegistry = new EffectTemplateRegistry();
            effectTemplateRegistry.SetConflictReport(ConflictReport);
            var gasConditions = new GasConditionRegistry();
            var targetDispatchPresetRegistry = new TargetDispatchPresetRegistry();
            var progressionDefinitions = new ProgressionDefinitionRegistry();
            var progressionRequirements = new ProgressionRequirementRegistry();
            var progressionScopeKeys = new ScopeKeyRegistry();
            var targetDispatchPresetLoader = new TargetDispatchPresetLoader(ConfigPipeline, targetDispatchPresetRegistry);
            targetDispatchPresetLoader.Load(ConfigCatalog, ConfigConflictReport);
            var gasClockConfigLoader = new GasClockConfigLoader(ConfigPipeline);
            var gasClockConfig = gasClockConfigLoader.Load(ConfigCatalog, ConfigConflictReport);
            var physics2dClockConfigLoader = new Physics2DClockConfigLoader(ConfigPipeline);
            var physics2dClockConfig = physics2dClockConfigLoader.Load(ConfigCatalog, ConfigConflictReport);
            var physics2dSolverConfigLoader = new Physics2DSolverConfigLoader(ConfigPipeline);
            var physics2dSolverConfig = physics2dSolverConfigLoader.Load(ConfigCatalog, ConfigConflictReport);
            var componentAuthoringContext = new ComponentAuthoringContext();
            MapLoader.SetComponentAuthoringContext(componentAuthoringContext);
            new AttributeConstraintsLoader(ConfigPipeline).Load(ConfigCatalog, ConfigConflictReport);
            int timeScalePermilleAttributeId = AttributeRegistry.Register(TimeAttributeNames.ScalePermille);
            var graphProgramRegistry = new GraphProgramRegistry();
            var graphOutputSchemas = new GraphOutputSchemaRegistry();
            var graphOutputValueKeyRegistry = new StringIntRegistry(capacity: 64, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var scopeResolver = new ScopeResolver(World, progressionScopeKeys, entityCollectionStore, relationshipRuntime);
            var knowledgeProjectionStore = new KnowledgeProjectionStore(initialCapacity: 128);
            var knowledgeRelationCollectionProjector = new KnowledgeRelationCollectionProjector(
                relationshipRuntime,
                entityCollectionStore,
                relationshipCatalogRuntime,
                knowledgeProjectionStore);
            var knowledgeProjectionResolver = new KnowledgeProjectionResolver(
                knowledgeProjectionStore,
                knowledgeRelationCollectionProjector,
                scopeResolver);
            var visionFogLayerRegistry = new FogLayerRegistry();
            var visionFogFieldStore = new FogFieldStore();
            var visionFogSnapshotStore = new FogSnapshotStore(relationships: relationshipRuntime);
            var visionFogCellMap = new FogCellMap();
            new VisionFogLayerConfigLoader(ConfigPipeline, visionFogLayerRegistry)
                .Load(ConfigCatalog, ConfigConflictReport);
            var fogKnowledgeProjector = new FogKnowledgeProjector(knowledgeProjectionStore, visionFogCellMap);
            var visionResolver = new VisionResolver(
                visionFogLayerRegistry,
                visionFogFieldStore,
                elevation: visionFogCellMap,
                occlusion: visionFogCellMap,
                relationships: relationshipRuntime);
            var knowledgeAreaRevealRuntime = new KnowledgeAreaRevealRuntime(
                World,
                visionFogLayerRegistry,
                visionFogFieldStore,
                visionResolver,
                fogKnowledgeProjector);
            var graphSymbolResolver = new GasGraphSymbolResolver(
                relationshipTypeRegistry,
                relationshipMetricRegistry,
                relationshipFlagRegistry,
                relationshipReasonRegistry,
                targetDispatchPresetRegistry,
                MapLoader.EntityTemplateKeys);
            var graphConfigLoader = new GraphProgramConfigLoader(
                ConfigPipeline,
                graphProgramRegistry,
                graphSymbolResolver,
                graphOutputSchemas,
                graphOutputValueKeyRegistry,
                entityCollectionStore);
            var graphPackages = graphConfigLoader.LoadIdsAndCompile(ConfigCatalog, ConfigConflictReport);
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
            var itemShapes = new ItemShapeRegistry();
            var itemLayouts = new ItemLayoutRegistry();
            var itemDefinitions = new ItemDefinitionRegistry();
            var exchangeOperations = new ExchangeOperationRegistry();
            var exchangeScopedOperations = new ExchangeScopedOperationStore();
            abilityDefinitions.SetConflictReport(ConflictReport);
            EffectParamKeys.Initialize();
            AbilityFormSetIdRegistry.Clear();
            ContextGroupIdRegistry.Clear();
            var itemConfigLoader = new ItemConfigLoader(ConfigPipeline, itemShapes, itemLayouts, itemDefinitions);
            var exchangeLoader = new ExchangeConfigLoader(
                ConfigPipeline,
                exchangeOperations,
                itemDefinitions,
                relationshipTypeRegistry,
                relationshipMetricRegistry,
                relationshipFlagRegistry);
            exchangeLoader.LoadIds(ConfigCatalog, ConfigConflictReport);
            new ProgressionConfigLoader(
                ConfigPipeline,
                progressionDefinitions,
                progressionRequirements,
                progressionScopeKeys,
                entityCollectionStore,
                relationshipTypeRegistry).Load(ConfigCatalog, ConfigConflictReport);
            new OrderTypeConfigLoader(ConfigPipeline).Load(
                orderTypeRegistry,
                orderRuleRegistry,
                ConfigCatalog,
                ConfigConflictReport);
            _effectTemplateLoader = new EffectTemplateLoader(
                ConfigPipeline,
                effectTemplateRegistry,
                gasConditions,
                targetDispatchPresetRegistry,
                exchangeOperations,
                progressionScopeKeys,
                MapLoader.EntityTemplateKeys,
                relationshipTypes: relationshipTypeRegistry,
                fogLayers: visionFogLayerRegistry,
                orderTypes: orderTypeRegistry);
            _effectTemplateLoader.Load(ConfigCatalog, ConfigConflictReport);
            new AbilityExecLoader(ConfigPipeline, abilityDefinitions).Load(ConfigCatalog, ConfigConflictReport);
            new AbilityFormSetConfigLoader(ConfigPipeline, abilityFormSets).Load(ConfigCatalog, ConfigConflictReport);
            componentAuthoringContext.Set(ComponentAuthoringServiceKeys.AbilityDefinitionRegistry, abilityDefinitions);
            componentAuthoringContext.Set(ComponentAuthoringServiceKeys.AbilityFormSetRegistry, abilityFormSets);
            var tagRules = new TagRuleSetLoader(ConfigPipeline).Load(ConfigCatalog, ConfigConflictReport);
            for (int i = 0; i < tagRules.Count; i++)
            {
                tagOps.RegisterTagRuleSet(tagRules[i].TagId, tagRules[i].RuleSet);
            }
            graphConfigLoader.PatchAndRegister(graphPackages);
            new ContextGroupConfigLoader(ConfigPipeline, contextGroups).Load(ConfigCatalog, ConfigConflictReport);
            itemConfigLoader.Load(ConfigCatalog, ConfigConflictReport);
            exchangeLoader.Load(ConfigCatalog, ConfigConflictReport);
            var inventoryRuntime = new InventoryRuntimeService(World, itemShapes, itemLayouts, itemDefinitions, ownershipResolver);
            var exchangeRuntime = new ExchangeRuntime(
                World,
                exchangeOperations,
                exchangeScopedOperations,
                inventoryRuntime,
                effectRequestQueue,
                relationshipRuntime);
            var graphOutputValueStore = new GraphOutputValueStore(
                graphOutputValueKeyRegistry,
                gasRuntimeCapacity.GraphOutputValueCapacity);
            var gasGraphProductionServices = new GasGraphRuntimeProductionServices(
                World,
                SpatialQueries,
                SpatialCoords,
                EventBus,
                effectRequestQueue,
                tagOps,
                relationshipRuntime,
                relationshipTypeRegistry,
                relationshipMetricRegistry,
                relationshipFlagRegistry,
                relationshipReasonRegistry,
                targetDispatchPresetRegistry,
                entityCollectionStore,
                entitySetQueryRuntime,
                controlDomainQuery,
                knowledgeProjectionResolver,
                clock);
            var gasGraphApi = GasGraphRuntimeApi.CreateProduction(gasGraphProductionServices);
            _gasGraphRuntimeApi = gasGraphApi;
            var graphReturnWriter = new GraphReturnWriter(
                World,
                graphProgramRegistry,
                graphOutputSchemas,
                GasGraphOpHandlerTable.Instance,
                entityCollectionStore,
                graphOutputValueStore);
            var progressionEvaluator = new ProgressionRequirementEvaluator(
                World,
                progressionRequirements,
                progressionScopeKeys,
                graphProgramRegistry,
                gasGraphApi,
                tagOps,
                scopeResolver);
            var phaseExecutor = new EffectPhaseExecutor(
                graphProgramRegistry,
                presetTypes,
                builtinHandlers,
                GasGraphOpHandlerTable.Instance,
                effectTemplateRegistry,
                eventBus: EventBus,
                budget: gasBudget,
                graphProgramScratchCapacity: gasRuntimeCapacity.EffectPhaseGraphProgramScratchCapacity);
            var inputRequestQueue = new InputRequestQueue();
            var abilityInputRequestQueue = new InputRequestQueue();
            var inputResponseBuffer = new InputResponseBuffer();
            var commandSourceConfig = config.CommandSource
                ?? throw new InvalidOperationException("game.json commandSource must be explicitly configured.");
            var interactionActionBindings = new InteractionActionBindings();
            var interactionContextStack = new InteractionContextStack(entityCollectionKeyRegistry);
            interactionContextStack.Push(InteractionContextFrameDescriptor.Create(
                InteractionContextIds.Default,
                EntityCollectionKeys.CommandSource,
                EntityViewKeys.ControlPlaneCommand));
            interactionContextStack.AddTransitionListener(new InteractionContextInputContextBridge(
                interactionContextStack,
                () => GetService(CoreServiceKeys.InputHandler)));
            var filterProfileRegistry = new FilterProfileRegistry(interactionContextStack.FilterProfileIdRegistry, World, tagOps);
            // Association expansion is a control-plane provider injected into the filter registry (RFC-0065 DEC-8).
            filterProfileRegistry.RegisterExpander(
                FilterAssociationExpandKinds.Controls,
                controlDomainQuery.CollectControlled,
                () => controlDomainQuery.Revision);
            filterProfileRegistry.Install(new FilterProfileConfigLoader(ConfigPipeline).Load(ConfigCatalog, ConfigConflictReport));
            var contextBoundCollectionWriter = new ContextBoundCollectionWriter(
                interactionContextStack,
                filterProfileRegistry,
                domainRoutedCollectionWriter,
                entityCollectionStore);
            var presentationConfig = config.Presentation
                ?? throw new InvalidOperationException("game.json presentation must be explicitly configured.");
            var runtimeEntitySpawnQueue = new RuntimeEntitySpawnQueue(presentationConfig.RuntimeEntitySpawnQueueCapacity);
            var runtimeEntityLifecycleQueue = new RuntimeEntityLifecycleQueue(presentationConfig.RuntimeEntityLifecycleQueueCapacity);
            var runtimeEntityLifecycleReceiptQueue = new RuntimeEntityLifecycleReceiptQueue(presentationConfig.RuntimeEntityLifecycleReceiptQueueCapacity);
            var runtimeEntitySpawnReceiptQueue = new RuntimeEntitySpawnReceiptQueue(presentationConfig.RuntimeEntitySpawnReceiptQueueCapacity);
            var runtimeEntitySpawnReceiptChannels = new RuntimeEntitySpawnReceiptChannelRegistry();
            MapLoader.SetEffectRequestQueue(effectRequestQueue);
            NetworkRuntimeConfig? networkConfig = config.Networking;
            networkConfig?.Validate();
            NetworkCommandAdmissionResultBuffer? networkCommandAdmissionResults = networkConfig == null
                ? null
                : new NetworkCommandAdmissionResultBuffer(networkConfig.NetworkAdmissionResultCapacity);
            NetworkCommandIngress? networkCommandIngress = null;
            NetworkGameplayCommandGate? networkGameplayCommandGate = null;
            NetworkCommandSchemaRegistry? networkCommandSchemas = null;
            NetworkEntityTable? networkEntityTable = null;
            if (networkConfig != null)
            {
                networkEntityTable = new NetworkEntityTable(networkConfig.NetworkEntityCapacity);
            }
            var orderAdmissionResults = new OrderAdmissionResultBuffer(
                gasRuntimeCapacity.OrderAdmissionResultCapacity,
                gasRuntimeCapacity.OrderAdmissionRejectionCapacity);
            if (networkConfig != null &&
                (orderAdmissionResults.Capacity < networkConfig.EntityAdmissionResultCapacity ||
                 orderAdmissionResults.RejectionCapacity < networkConfig.EntityAdmissionResultCapacity))
            {
                throw new InvalidOperationException(
                    "GameConfig.gasRuntimeCapacity order admission result and rejection capacities must both be at least " +
                    $"networking.entityAdmissionResultCapacity ({networkConfig.EntityAdmissionResultCapacity}).");
            }
            var orderQueue = new OrderQueue(
                gasRuntimeCapacity.OrderQueueCapacity,
                orderAdmissionResults);
            var chainOrderQueue = new OrderQueue(
                gasRuntimeCapacity.ResponseChainOrderQueueCapacity,
                orderAdmissionResults);
            var orderRequestQueue = new OrderRequestQueue();
            var responseChainTelemetry = new ResponseChainTelemetryBuffer();

            var orderTypeIds = config.Constants.OrderTypeIds;
            var responseChainOrderTypeIds = config.Constants.ResponseChainOrderTypeIds;
            var deferredTriggerQueue = new DeferredTriggerQueue();
            var deferredTriggerCollectionSystem = new DeferredTriggerCollectionSystem(World, deferredTriggerQueue, tagOps, dirtyEntities);
            var deferredTriggerProcessSystem = new DeferredTriggerProcessSystem(World, deferredTriggerQueue, EventBus);
            var clearPresentationFlagsSystem = new ClearPresentationFlagsSystem(World);
            var gasPresentationEvents = new GasPresentationEventBuffer(presentationConfig.GasPresentationEventCapacity);
            var globalPresentationEvents = new GlobalPresentationEventBuffer();
            var presentationEventStream = new PresentationEventStream(presentationConfig.PresentationEventStreamCapacity);
            var presentationOwnerChanges = new PresentationOwnerChangeBuffer(presentationConfig.PresentationOwnerChangeCapacity);
            var gameplayPresentationProjectionSystem = new GameplayPresentationProjectionSystem(
                World,
                EventBus,
                presentationEventStream,
                GameSession,
                gasPresentationEvents,
                presentationOwnerChanges);
            var globalPresentationEventProjectionSystem = new GlobalPresentationEventProjectionSystem(World, globalPresentationEvents, presentationEventStream, GameSession);
            var performerCommandBuffer = new PerformerCommandBuffer(presentationConfig.PerformerCommandCapacity);
            var presentationPrefabs = new PrefabRegistry();
            var meshAssets = new MeshAssetRegistry();
            var materialAssets = new PresentationMaterialRegistry();
            var instancedBatchAssets = new InstancedBatchAssetRegistry();
            var instancedBatchRequests = new InstancedBatchRequestBuffer(presentationConfig.PresentationRequestCapacity);
            var instancedBatchOperations = new InstancedBatchOperationBuffer(presentationConfig.PresentationRequestCapacity);
            var instancedBatchSubmissionRuntime = new InstancedBatchSubmissionRuntime();
            var animatorControllers = new AnimatorControllerRegistry();
            var animationClips = new AnimationClipRegistry();
            var animationProfiles = new AnimationProfileRegistry();
            var presentationStableIds = new PresentationStableIdAllocator();
            var performerVisualStableIds = new PerformerVisualStableIdTable(
                presentationStableIds,
                presentationConfig.VisualSnapshotBufferCapacity);
            var primitiveDrawBuffer = new PrimitiveDrawBuffer(presentationConfig.PrimitiveDrawBufferCapacity);
            var presentationFrameReceiptBuffer = new PresentationFrameReceiptBuffer(presentationConfig.PrimitiveDrawBufferCapacity);
            var visualSnapshotBuffer = new PrimitiveDrawBuffer(presentationConfig.VisualSnapshotBufferCapacity);
            var visualProxyBuffer = new PresentationVisualProxyBuffer(presentationConfig.VisualProxyBufferCapacity);
            var skinnedVisualBatchBuffer = new SkinnedVisualBatchBuffer(presentationConfig.SkinnedVisualBatchCapacity);
            var stableDrawCache = new StableDrawCache(presentationConfig.VisualSnapshotBufferCapacity);
            var presentationTargetGeneration = new PresentationTargetGeneration();
            var presentationRequestBuffer = new PresentationRequestBuffer(presentationConfig.PresentationRequestCapacity);
            var globalFieldVisualBuffer = new GlobalFieldVisualBuffer(
                presentationConfig.GlobalFieldVisualRecordCapacity,
                presentationConfig.GlobalFieldVisualCellCapacity,
                presentationConfig.GlobalFieldVisualDirtyRectCapacity);
            var transientMarkerBuffer = new TransientMarkerBuffer();
            var groundOverlayBuffer = new GroundOverlayBuffer(presentationConfig.GroundOverlayCapacity);
            var roadSplineBuffer = new RoadSplineBuffer(presentationConfig.RoadSplineCapacity);
            var soundRequestBuffer = new SoundRequestBuffer();
            var worldHudBuffer = new WorldHudBatchBuffer(presentationConfig.WorldHudCapacity);
            var presentationTimingDiagnostics = new PresentationTimingDiagnostics();
            var performerDefinitions = new PerformerDefinitionRegistry();
            var performerRuntime = new PerformerEntityRuntime(World);
            var performerAnimatorStates = new PerformerAnimatorStateBuffer(presentationConfig.PerformerInstanceCapacity);
            performerRuntime.BindAnimatorStates(performerAnimatorStates);
            var surfacePayloads = new SurfaceSourcePayloadRegistry();
            var surfaceRuntime = new SurfaceSourceRuntimeRegistry();
            var presentationBehaviors = new PresentationBehaviorRegistry();
            int ResolveInstancedBatchGasEventKey(PresentationEventKind eventKind, string key)
            {
                return eventKind == PresentationEventKind.EffectApplied
                    ? EffectTemplateIdRegistry.GetId(key)
                    : AbilityIdRegistry.GetId(key);
            }

            int ResolveInstancedBatchPresentationEventKey(PresentationEventKind eventKind, string key)
            {
                return eventKind switch
                {
                    PresentationEventKind.EntitySpawned => MapLoader.EntityTemplateKeys.GetId(key),
                    PresentationEventKind.EntityDestroyed => MapLoader.EntityTemplateKeys.GetId(key),
                    PresentationEventKind.ProjectileSpawned => EffectTemplateIdRegistry.GetId(key),
                    PresentationEventKind.TagEffectiveChanged => TagRegistry.GetId(key),
                    PresentationEventKind.GameplayEvent => TagRegistry.GetId(key),
                    PresentationEventKind.EffectApplied => EffectTemplateIdRegistry.GetId(key),
                    PresentationEventKind.CastCommitted => AbilityIdRegistry.GetId(key),
                    PresentationEventKind.CastFailed => AbilityIdRegistry.GetId(key),
                    PresentationEventKind.GlobalDayNight => TagRegistry.GetId(key),
                    PresentationEventKind.GlobalRegionChanged => TagRegistry.GetId(key),
                    PresentationEventKind.GlobalWeather => TagRegistry.GetId(key),
                    PresentationEventKind.PerformerCreated => key == "*" ? -1 : 0,
                    PresentationEventKind.PerformerDestroyed => key == "*" ? -1 : 0,
                    _ => 0,
                };
            }

            new MeshAssetConfigLoader(ConfigPipeline, meshAssets, presentationPrefabs).Load(ConfigCatalog, ConfigConflictReport);
            new PresentationMaterialConfigLoader(ConfigPipeline, materialAssets).Load(ConfigCatalog, ConfigConflictReport);
            new InstancedBatchAssetConfigLoader(
                ConfigPipeline,
                instancedBatchAssets,
                meshAssets,
                materialAssets,
                Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId,
                ResolveInstancedBatchGasEventKey,
                ResolveInstancedBatchPresentationEventKey).Load(ConfigCatalog, ConfigConflictReport);
            new PresentationBehaviorConfigLoader(ConfigPipeline, presentationBehaviors, meshAssets).Load(ConfigCatalog, ConfigConflictReport);
            var presentationBehaviorResolver = new PresentationBehaviorResolver(presentationBehaviors, meshAssets);
            new AnimatorControllerConfigLoader(ConfigPipeline, animatorControllers).Load(ConfigCatalog, ConfigConflictReport);
            new AnimationClipConfigLoader(ConfigPipeline, animationClips).Load(ConfigCatalog, ConfigConflictReport);
            new AnimationProfileConfigLoader(ConfigPipeline, animationProfiles, animatorControllers, animationClips).Load(ConfigCatalog, ConfigConflictReport);
            var presentationTextCatalog = new PresentationTextCatalogLoader(ConfigPipeline).Load(ConfigCatalog, ConfigConflictReport);
            var presentationTextLocaleSelection = new PresentationTextLocaleSelection(presentationTextCatalog);
            if (presentationTextCatalog.DefaultLocaleId > 0)
            {
                AbilityPresentationTextValidator.ValidateRegisteredAbilities(
                    abilityDefinitions,
                    presentationTextCatalog,
                    presentationTextCatalog.GetLocaleKey(presentationTextCatalog.DefaultLocaleId),
                    requireTokensOnAllPresentations: false);
            }
            else
            {
                AbilityPresentationTextValidator.RejectTokenizedPresentationWithoutLocaleCatalog(abilityDefinitions);
            }
            var performerRuleSystem = new PerformerRuleSystem(World, presentationEventStream, performerCommandBuffer, performerDefinitions, performerRuntime, graphProgramRegistry, gasGraphApi, GlobalContext);
            var presentationEntityLifecycleSystem = new PresentationEntityLifecycleSystem(
                World,
                presentationEventStream,
                performerRuntime,
                performerDefinitions,
                presentationStableIds);
            var presentationEntityFinalizeDestroySystem = new PresentationEntityFinalizeDestroySystem(World);
            var performerRuntimeSystem = new PerformerRuntimeSystem(
                World,
                performerCommandBuffer,
                presentationEventStream,
                transientMarkerBuffer,
                presentationRequestBuffer,
                performerRuntime,
                presentationStableIds,
                performerDefinitions,
                performerAnimatorStates,
                stableDrawCache,
                performerVisualStableIds);
            var performerBehaviorSystem = new PerformerBehaviorSystem(
                World,
                performerRuntime,
                performerDefinitions,
                presentationEventStream,
                presentationOwnerChanges,
                soundRequestBuffer,
                () => GetService(CoreServiceKeys.VisualHeightmap),
                boneTransformProvider: null,
                timingDiagnostics: presentationTimingDiagnostics);
            var animatorRuntimeSystem = new AnimatorRuntimeSystem(
                World,
                animatorControllers,
                performerRuntime,
                performerDefinitions,
                performerAnimatorStates,
                presentationTimingDiagnostics);
            var performerEmitSystem = new PerformerEmitSystem(World, performerRuntime, performerDefinitions, presentationRequestBuffer, GlobalContext,
                performerAnimatorStates,
                soundRequestBuffer,
                presentationTimingDiagnostics,
                stableDrawCache,
                skinnedVisualBatchBuffer,
                worldHudBuffer,
                performerVisualStableIds);
            var surfaceSourceFlushSystem = new SurfaceSourceFlushSystem(World, presentationRequestBuffer, surfacePayloads, surfaceRuntime);
            var surfaceSourceLifecycleSystem = new SurfaceSourceLifecycleSystem(World, surfaceRuntime, performerCommandBuffer);
            var chunkSurfaceBakeSystem = new ChunkSurfaceBakeSystem(World, surfaceRuntime, meshAssets, materialAssets, performerDefinitions, performerCommandBuffer, performerRuntime);
            var presentationRequestFlushSystem = new PresentationRequestFlushSystem(
                World,
                presentationRequestBuffer,
                presentationPrefabs,
                meshAssets,
                stableDrawCache,
                primitiveDrawBuffer,
                groundOverlayBuffer,
                worldHudBuffer,
                roadSplineBuffer,
                visualSnapshotBuffer,
                visualProxyBuffer,
                skinnedVisualBatchBuffer,
                presentationTimingDiagnostics,
                presentationTargetGeneration);
            new PerformerDefinitionConfigLoader(
                ConfigPipeline,
                performerDefinitions,
                Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId,
                meshAssets.GetId,
                presentationTextCatalog.GetTokenId,
                MapLoader.EntityTemplateKeys.GetId,
                EffectTemplateIdRegistry.GetId,
                materialAssets.GetId,
                animatorControllers.GetId,
                animationProfiles.GetId,
                (kind, key) => kind switch
                {
                    AssetKind.Mesh => meshAssets.GetId(key),
                    AssetKind.SkinnedMesh => meshAssets.GetId(key),
                    AssetKind.Decal => meshAssets.GetId(key),
                    AssetKind.VFX => meshAssets.GetId(key),
                    AssetKind.Spline => meshAssets.GetId(key),
                    AssetKind.Surface => meshAssets.GetId(key),
                    AssetKind.Sound => meshAssets.GetId(key),
                    AssetKind.WorldText => presentationTextCatalog.GetTokenId(key),
                    AssetKind.GroundOverlay => ResolveGroundOverlayShapeId(key),
                    _ => 0,
                },
                instancedBatchAssets.GetId,
                entityCollectionKeyRegistry.Register).Load(ConfigCatalog, ConfigConflictReport);
            performerDefinitions.RebuildCompiledViews();
            MapLoader.SetPresentationRuntime(
                presentationStableIds,
                performerRuntime,
                performerDefinitions,
                _spatialPartition,
                WorldSizeSpec);

            System.Diagnostics.Debug.Assert(
                meshAssets.TryGetDescriptor(meshAssets.GetId(WellKnownMeshKeys.Cube), out var _cubeDbg) && _cubeDbg.Type == MeshAssetType.Primitive,
                "MeshAssetRegistry: 'cube' descriptor missing or invalid after config load");
            System.Diagnostics.Debug.Assert(
                meshAssets.TryGetDescriptor(meshAssets.GetId(WellKnownMeshKeys.Sphere), out var _sphereDbg) && _sphereDbg.Type == MeshAssetType.Primitive,
                "MeshAssetRegistry: 'sphere' descriptor missing or invalid after config load");

            var worldHudStrings = new WorldHudStringTable(presentationTextCatalog, presentationTextLocaleSelection);
            var minimapRuntime = new MinimapRuntime(presentationConfig.Minimap);
            var chunkDebugPanelRuntime = new ChunkDebugPanelRuntime();
            var minimapMarkerBuffer = new MinimapMarkerBuffer(presentationConfig.MinimapMarkerCapacity);
            var minimapScreenMarkerBuffer = new MinimapScreenMarkerBuffer(presentationConfig.MinimapMarkerCapacity);
            var inputFrameConsumers = new List<IInputFrameConsumer>
            {
                new MinimapInputConsumer(
                    minimapRuntime,
                    static (GameEngine engine, out Entity owner, out string collectionKey) =>
                    {
                        owner = Entity.Null;
                        collectionKey = string.Empty;
                        return engine.TryGetService(
                                   CoreServiceKeys.MinimapFocusCollectionProvider,
                                   out MinimapFocusCollectionProvider provider) &&
                               provider(engine, out owner, out collectionKey);
                    })
            };
            SetService(
                CoreServiceKeys.MinimapKnowledgeViewerProvider,
                static (GameEngine engine, out Entity viewer) =>
                    KnowledgeProjectionConsumer.TryResolveViewer(engine.World, engine.GlobalContext, Entity.Null, out viewer));

            var abilitySystem = new AbilitySystem(World, effectRequestQueue, abilityDefinitions, tagOps, graphProgramRegistry, gasGraphApi, progressionEvaluator);
            var reactionSystem = new ReactionSystem(World, abilitySystem, EventBus);
            var graphEdgeCostOverlay = new GraphEdgeCostOverlay();
            var cameraBehaviorInput = new CameraBehaviorInputState();
            var cameraImpulseRuntime = new CameraImpulseRuntime();
            World.Create(new AttributeBuffer(), new DirtyFlags(), new CameraBehaviorInputTarget());
            var attributeSinks = new AttributeSinkRegistry();
            GasAttributeSinks.RegisterBuiltins(attributeSinks, cameraBehaviorInput);
            GraphAttributeSinks.RegisterBuiltins(attributeSinks, graphEdgeCostOverlay);
            var attributeBindings = new AttributeBindingRegistry();
            new AttributeBindingLoader(ConfigPipeline, attributeSinks, attributeBindings).Load(ConfigCatalog, ConfigConflictReport);
            var bindingSystem = new AttributeBindingSystem(World, attributeSinks, attributeBindings);
            var aggSystem = new AttributeAggregatorSystem(World, graphProgramRegistry, gasGraphApi, tagOps);
            var sessionSystem = new GameSessionSystem(GameSession);
            var authoritativeInput = new FrozenInputActionReader();
            var authoritativeInputAccumulator = new AuthoritativeInputAccumulator();
            var authoritativePointerButtons = new AuthoritativePointerButtonSnapshot();
            var authoritativePointerButtonsAccumulator = new AuthoritativePointerButtonAccumulator();
            var authoritativeGroundPointerOverride = new AuthoritativeGroundPointerOverride();
            var inputConfigRoot = new InputConfigPipelineLoader(ConfigPipeline).Load();
            var inputActionAttributeBindings = new InputActionAttributeBindingRegistry();
            new InputActionAttributeBindingLoader(ConfigPipeline, inputActionAttributeBindings).Load(ConfigCatalog, ConfigConflictReport);
            _inputRuntimeSystem = new InputRuntimeSystem(GlobalContext, authoritativeInputAccumulator, authoritativePointerButtonsAccumulator);
            _inputRuntimeSystem.Initialize();
            var clockStepPolicy = new GasClockStepPolicy(gasClockConfig.StepEveryFixedTicks, gasClockConfig.Mode);
            var clockSystem = new GasClockSystem(clock, clockStepPolicy, CreateContext, TriggerManager.FireEvent);
            var entityLocalClockSystem = new EntityLocalClockSystem(World, clockStepPolicy, timeScalePermilleAttributeId);
            var physics2dTickPolicy = new Physics2DTickPolicy(physics2dClockConfig.PhysicsHz, physics2dClockConfig.MaxStepsPerFixedTick);
            var physics2dBroadphasePolicy = new Physics2DBroadphasePolicy(physics2dClockConfig.Broadphase);
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
            // respondChainOrderTagId = -1 (invalid sentinel): chain orders are routed directly
            // to chainOrderQueue by ResponseChain*Systems, not through the dispatch system.
            // Using -1 prevents accidental match with default OrderTagId == 0.
            // ── OrderBuffer pipeline ──
            if (networkConfig != null)
            {
                networkCommandSchemas = new NetworkCommandSchemaRegistry();
                for (int i = 0; i < networkConfig.CommandSchemas.Count; i++)
                {
                    NetworkCommandSchemaConfig configuredSchema = networkConfig.CommandSchemas[i];
                    int orderTypeId = RequireRegisteredOrderTypeId(orderTypeRegistry, configuredSchema.OrderTypeKey);
                    var schema = new NetworkCommandSchema(
                        orderTypeId,
                        configuredSchema.TargetKind,
                        configuredSchema.AllowArg0,
                        configuredSchema.AllowArg1,
                        configuredSchema.GetAllowedSubmitModeMask(),
                        configuredSchema.RequiredTargetPositionAccess);
                    networkCommandSchemas.Register(in schema);
                }

                networkCommandSchemas.Freeze();
                var ingressConfig = new NetworkCommandIngressConfig(
                    networkConfig.PlayerCapacity,
                    networkConfig.SimulationTickRateHz,
                    networkConfig.MaxCommandBatchesPerSecondPerPlayer,
                    networkConfig.CommandBurstBatchCapacity,
                    networkConfig.MaxActorsPerCommandBatch,
                    networkConfig.CommandSequenceHistoryCapacity,
                    networkConfig.MaxPastTargetTicks,
                    networkConfig.MaxFutureTargetTicks,
                    checked(networkConfig.PlayerCapacity * networkConfig.CommandBurstBatchCapacity));
                networkGameplayCommandGate = new NetworkGameplayCommandGate();
                networkCommandIngress = new NetworkCommandIngress(
                    in ingressConfig,
                    World,
                    networkEntityTable!,
                    controlDomainQuery,
                    knowledgeProjectionResolver,
                    orderTypeRegistry,
                    networkCommandSchemas,
                    networkGameplayCommandGate,
                    orderQueue,
                    networkCommandAdmissionResults!);
            }

            int cfgCastAbility = RequireConfiguredOrderTypeId(orderTypeIds, orderTypeRegistry, "castAbility", "constants.orderTypeIds");
            int cfgMoveTo = RequireConfiguredOrderTypeId(orderTypeIds, orderTypeRegistry, "moveTo", "constants.orderTypeIds");
            int cfgAttackTarget = RequireConfiguredOrderTypeId(orderTypeIds, orderTypeRegistry, "attackTarget", "constants.orderTypeIds");
            int cfgStop = RequireConfiguredOrderTypeId(orderTypeIds, orderTypeRegistry, "stop", "constants.orderTypeIds");
            int cfgChainPass = RequireConfiguredOrderTypeId(responseChainOrderTypeIds, orderTypeRegistry, "chainPass", "constants.responseChainOrderTypeIds");
            int cfgChainNegate = RequireConfiguredOrderTypeId(responseChainOrderTypeIds, orderTypeRegistry, "chainNegate", "constants.responseChainOrderTypeIds");
            int cfgChainActivateEffect = RequireConfiguredOrderTypeId(responseChainOrderTypeIds, orderTypeRegistry, "chainActivateEffect", "constants.responseChainOrderTypeIds");
            int cfgCastAbilityStart = RequireRegisteredOrderTypeId(orderTypeRegistry, "castAbility.Start");
            int cfgCastAbilityEnd = RequireRegisteredOrderTypeId(orderTypeRegistry, "castAbility.End");

            // Pointer command intent routing (RFC-0065 INT-1, DEC-14); installed after order types so
            // rule routes validate their orderTypeKey at load time. Target facts are knowledge-gated
            // through CanTargetCommand semantics (INT-2); the resolver is a hard dependency here.
            var commandIntentTargetGate = new KnowledgeCommandTargetGate(World, knowledgeProjectionResolver, clock);
            var commandIntentProfileIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var commandIntentProfileRegistry = new CommandIntentProfileRegistry(
                commandIntentProfileIds,
                World,
                tagOps,
                abilityDefinitions,
                controlDomainQuery,
                domainStanceQuery,
                orderTypeRegistry,
                commandIntentTargetGate.CanTarget);
            commandIntentProfileRegistry.Install(new CommandIntentProfileConfigLoader(ConfigPipeline).Load(ConfigCatalog, ConfigConflictReport));

            // Interaction context profiles + cast commit profiles (RFC-0065 CTX-6/CTX-7, DEC-13).
            var interactionContextProfileIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var interactionContextProfileRegistry = new InteractionContextProfileRegistry(interactionContextProfileIds);
            interactionContextProfileRegistry.Install(new InteractionContextProfileConfigLoader(ConfigPipeline).Load(ConfigCatalog, ConfigConflictReport));
            var castCommitProfileIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var castCommitActionIds = new StringIntRegistry(capacity: 32, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var castCommitProfileRegistry = new CastCommitProfileRegistry(castCommitProfileIds, castCommitActionIds, interactionContextProfileRegistry);
            castCommitProfileRegistry.Install(new CastCommitProfileConfigLoader(ConfigPipeline).Load(ConfigCatalog, ConfigConflictReport));

            // Client cast preference scope chain (RFC-0065 CTX-8, §5.6): player preference layers plus
            // the mod lock set; template/form-set keys resolve through the engine-wide id spaces.
            var clientCastPreferences = new ClientCastPreferenceStore(
                castCommitProfileRegistry,
                MapLoader.EntityTemplateKeys.Register,
                MapLoader.EntityTemplateKeys.GetName,
                RequireAbilityFormSetId,
                AbilityFormSetIdRegistry.GetName);
            clientCastPreferences.InstallLocks(new ClientCastPreferenceConfigLoader(ConfigPipeline).Load(ConfigCatalog, ConfigConflictReport));

            // Cast dispatch kernel (RFC-0065 DSP-1/2/4, DEC-9/DEC-11).
            var castDispatchProfileIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var castDispatchAdvanceEventIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var castDispatchProfileRegistry = new CastDispatchProfileRegistry(castDispatchProfileIds, castDispatchAdvanceEventIds);
            castDispatchProfileRegistry.Install(new CastDispatchProfileConfigLoader(ConfigPipeline).Load(ConfigCatalog, ConfigConflictReport));

            // Control scheme catalog + hot-switch runtime (RFC-0065 INT-5, DEC-15). The player input
            // handler is adapter-bound after engine init, so it is resolved per switch (null tolerated).
            var controlSchemeIds = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var controlSchemeRuntime = new ControlSchemeRuntime(
                controlSchemeIds,
                interactionContextStack,
                commandIntentProfileRegistry,
                castDispatchProfileRegistry,
                orderTypeRegistry,
                () => GetService(CoreServiceKeys.InputHandler),
                clientCastPreferences,
                inputConfig: inputConfigRoot);
            controlSchemeRuntime.Install(new ControlSchemeConfigLoader(ConfigPipeline).Load(ConfigCatalog, ConfigConflictReport));

            // Ability panel aggregation kernel (RFC-0065 PNL-1/2, DEC-10); installed after abilities.json
            // so catalog tag prefixes compile against the loaded definitions.
            var abilityAggregationProfileIds = new StringIntRegistry(capacity: 64, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var abilityAggregationProfileRegistry = new AbilityAggregationProfileRegistry(abilityAggregationProfileIds);
            abilityAggregationProfileRegistry.Install(new AbilityAggregationProfileConfigLoader(ConfigPipeline).Load(ConfigCatalog, ConfigConflictReport));

            // CommandDeck display/routing profiles (WPK-3): modes + route refs over existing panel/aggregation/dispatch kernels.
            var commandDeckProfileIds = new StringIntRegistry(capacity: 64, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var commandDeckProfileRegistry = new CommandDeckProfileRegistry(
                commandDeckProfileIds,
                abilityAggregationProfileRegistry,
                castDispatchProfileRegistry,
                filterProfileRegistry);
            commandDeckProfileRegistry.Install(new CommandDeckProfileConfigLoader(ConfigPipeline).Load(ConfigCatalog, ConfigConflictReport));
            var commandDeckRouteResolver = new CommandDeckRouteResolver(castDispatchProfileRegistry);

            // Production / Worker / Queue overview profiles (WPK-4): project command/status/queue + OrderBuffer + collection workers.
            // Command panel source existence is validated at project time (sources register from mods after Core boot).
            var productionOverviewProfileIds = new StringIntRegistry(capacity: 64, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var productionOverviewProfileRegistry = new ProductionOverviewProfileRegistry(
                productionOverviewProfileIds,
                commandPanelSources: null,
                orderTypes: orderTypeRegistry);
            productionOverviewProfileRegistry.Install(
                new ProductionOverviewProfileConfigLoader(ConfigPipeline).Load(ConfigCatalog, ConfigConflictReport));

            int stepRateHz = engineClockConfig.FixedHz / Math.Max(1, gasClockConfig.StepEveryFixedTicks);
            var orderBufferSystem = new OrderBufferSystem(
                World, clock, orderTypeRegistry, orderRuleRegistry,
                orderAdmissionResults,
                orderQueue, stepRateHz,
                graphProgramRegistry, gasGraphApi,
                closeEntityIntakeOnUpdate: false);
            var abilityExecSystem = new AbilityExecSystem(World, clock, abilityInputRequestQueue, inputResponseBuffer, effectRequestQueue, gasRuntimeCapacity.AbilityExecSnapshotCapacity, abilityDefinitions, EventBus, cfgCastAbility, cfgCastAbilityStart, gasPresentationEvents, phaseExecutor: phaseExecutor, graphPrograms: graphProgramRegistry, graphApi: gasGraphApi, tagOps: tagOps, orderTypeRegistry: orderTypeRegistry, progressionRequirements: progressionEvaluator, maxWorkUnitsPerSlice: gasRuntimeCapacity.AbilityExecMaxWorkUnitsPerSlice);
            var abilityEndOrderSystem = new AbilityEndOrderSystem(World, orderTypeRegistry, cfgCastAbilityEnd);
            var stopOrderSystem = new StopOrderSystem(World, orderTypeRegistry, cfgStop);
            var instantCompleteOrderSystem = new InstantCompleteOrderSystem(World, orderTypeRegistry);
            var moveToOrderSystem = new MoveToWorldCmOrderSystem(World, orderTypeRegistry, cfgMoveTo);
            var orderContinuationSystem = new OrderContinuationSystem(
                World,
                clock,
                orderTypeRegistry,
                orderRuleRegistry,
                orderAdmissionResults,
                stepRateHz);

            // Register systems in Phase order according to GAS design document
            // Phase 0: SchemaUpdate
            SetService(CoreServiceKeys.ComponentAuthoringContext, componentAuthoringContext);
            SetService(CoreServiceKeys.ExtensionAttributeRegistry, extensionAttributeRegistry);
            SetService(CoreServiceKeys.AttributeSchemaUpdateQueue, attributeSchemaUpdateQueue);
            SetService(CoreServiceKeys.GasBudget, gasBudget);
            SetService(CoreServiceKeys.GasDiagnosticEventBuffer, gasDiagnostics);
            SetService(CoreServiceKeys.EffectTemplateRegistry, effectTemplateRegistry);
            SetService(CoreServiceKeys.TargetDispatchPresetRegistry, targetDispatchPresetRegistry);
            SetService(CoreServiceKeys.GraphProgramRegistry, graphProgramRegistry);
            SetService(CoreServiceKeys.GasGraphRuntimeProductionServices, gasGraphProductionServices);
            SetService(CoreServiceKeys.GasGraphRuntimeApi, gasGraphApi);
            SetService(CoreServiceKeys.GraphOutputSchemaRegistry, graphOutputSchemas);
            SetService(CoreServiceKeys.GraphOutputValueKeyRegistry, graphOutputValueKeyRegistry);
            SetService(CoreServiceKeys.GraphOutputValueStore, graphOutputValueStore);
            SetService(CoreServiceKeys.GraphReturnWriter, graphReturnWriter);
            SetService(CoreServiceKeys.EffectRequestQueue, effectRequestQueue);
            SetService(CoreServiceKeys.OrderAdmissionResultBuffer, orderAdmissionResults);
            SetService(CoreServiceKeys.OrderTerminalResultBuffer, orderTerminalResults);
            SetService(CoreServiceKeys.TimeFlow, _timeFlow);
            SetService(CoreServiceKeys.Clock, (IClock)clock);
            SetService(CoreServiceKeys.GasClockStepPolicy, clockStepPolicy);
            SetService(CoreServiceKeys.GasClocks, gasClocks);
            SetService(CoreServiceKeys.Physics2DTickPolicy, physics2dTickPolicy);
            SetService(CoreServiceKeys.Physics2DSolverConfig, physics2dSolverConfig);
            SetService(CoreServiceKeys.Physics2DBroadphasePolicy, physics2dBroadphasePolicy);
            SetService(CoreServiceKeys.Physics2DController, _physics2DController);
            SetService(CoreServiceKeys.SimulationLoopController, simulationLoopController);
            SetService(CoreServiceKeys.GasController, _gasController);
            SetService(CoreServiceKeys.GasConditionRegistry, gasConditions);
            SetService(CoreServiceKeys.TagOps, tagOps);
            SetService(CoreServiceKeys.DirtyEntityQueue, dirtyEntities);
            SetService(CoreServiceKeys.AbilityDefinitionRegistry, abilityDefinitions);
            SetService(CoreServiceKeys.AbilityFormSetRegistry, abilityFormSets);
            SetService(CoreServiceKeys.ProgressionDefinitionRegistry, progressionDefinitions);
            SetService(CoreServiceKeys.ProgressionRequirementRegistry, progressionRequirements);
            SetService(CoreServiceKeys.ScopeKeyRegistry, progressionScopeKeys);
            SetService(CoreServiceKeys.ScopeResolver, scopeResolver);
            SetService(CoreServiceKeys.ProgressionRequirementEvaluator, progressionEvaluator);
            SetService(CoreServiceKeys.ContextGroupRegistry, contextGroups);
            SetService(CoreServiceKeys.InputRequestQueue, inputRequestQueue);
            SetService(CoreServiceKeys.AbilityInputRequestQueue, abilityInputRequestQueue);
            SetService(CoreServiceKeys.InputResponseBuffer, inputResponseBuffer);
            SetService(CoreServiceKeys.CommandSourceAcquisitionConfig, commandSourceConfig);
            SetService(CoreServiceKeys.EntityCollectionStore, entityCollectionStore);
            SetService(CoreServiceKeys.EntityCollectionKeyRegistry, entityCollectionKeyRegistry);
            SetService(CoreServiceKeys.DomainRoutedCollectionWriter, domainRoutedCollectionWriter);
            SetService(CoreServiceKeys.ControlPlaneView, controlPlaneView);
            SetService(CoreServiceKeys.KnowledgeProjectionStore, knowledgeProjectionStore);
            SetService(CoreServiceKeys.KnowledgeRelationCollectionProjector, knowledgeRelationCollectionProjector);
            SetService(CoreServiceKeys.KnowledgeProjectionResolver, knowledgeProjectionResolver);
            SetService(CoreServiceKeys.VisionFogLayerRegistry, visionFogLayerRegistry);
            SetService(CoreServiceKeys.VisionFogFieldStore, visionFogFieldStore);
            SetService(CoreServiceKeys.VisionFogSnapshotStore, visionFogSnapshotStore);
            SetService(CoreServiceKeys.VisionFogCellMap, visionFogCellMap);
            SetService(CoreServiceKeys.VisionResolver, visionResolver);
            SetService(CoreServiceKeys.FogKnowledgeProjector, fogKnowledgeProjector);
            SetService(CoreServiceKeys.KnowledgeAreaRevealRuntime, knowledgeAreaRevealRuntime);
            SetService(CoreServiceKeys.InteractionActionBindings, interactionActionBindings);
            SetService(CoreServiceKeys.InteractionContextStack, interactionContextStack);
            SetService(CoreServiceKeys.FilterProfileRegistry, filterProfileRegistry);
            SetService(CoreServiceKeys.CommandIntentProfileRegistry, commandIntentProfileRegistry);
            SetService(CoreServiceKeys.CastDispatchProfileRegistry, castDispatchProfileRegistry);
            SetService(CoreServiceKeys.InteractionContextProfileRegistry, interactionContextProfileRegistry);
            SetService(CoreServiceKeys.CastCommitProfileRegistry, castCommitProfileRegistry);
            SetService(CoreServiceKeys.ClientCastPreferenceStore, clientCastPreferences);
            SetService(CoreServiceKeys.ControlSchemeRuntime, controlSchemeRuntime);
            SetService(CoreServiceKeys.AbilityAggregationProfileRegistry, abilityAggregationProfileRegistry);
            SetService(CoreServiceKeys.CommandDeckProfileRegistry, commandDeckProfileRegistry);
            SetService(CoreServiceKeys.CommandDeckRouteResolver, commandDeckRouteResolver);
            SetService(CoreServiceKeys.ProductionOverviewProfileRegistry, productionOverviewProfileRegistry);
            SetService(CoreServiceKeys.ContextBoundCollectionWriter, contextBoundCollectionWriter);
            RemoveService(CoreServiceKeys.VisualHeightmap);
            RemoveService(CoreServiceKeys.StructureCollisionAsset);
            RemoveService(CoreServiceKeys.StructureCollisionRuntimeState);
            RemoveService(CoreServiceKeys.GroundSurfaceSampler);
            SetService(CoreServiceKeys.RuntimeEntitySpawnQueue, runtimeEntitySpawnQueue);
            SetService(CoreServiceKeys.RuntimeEntityLifecycleQueue, runtimeEntityLifecycleQueue);
            SetService(CoreServiceKeys.RuntimeEntityLifecycleReceiptQueue, runtimeEntityLifecycleReceiptQueue);
            SetService(CoreServiceKeys.RuntimeEntitySpawnReceiptQueue, runtimeEntitySpawnReceiptQueue);
            SetService(CoreServiceKeys.RuntimeEntitySpawnReceiptChannelRegistry, runtimeEntitySpawnReceiptChannels);
            SetService(CoreServiceKeys.OrderQueue, orderQueue);
            if (networkConfig != null)
            {
                SetService(CoreServiceKeys.NetworkRuntimeConfig, networkConfig);
                SetService(CoreServiceKeys.NetworkCommandIngress, networkCommandIngress!);
                SetService(CoreServiceKeys.NetworkGameplayCommandGate, networkGameplayCommandGate);
                SetService(CoreServiceKeys.NetworkCommandSchemaRegistry, networkCommandSchemas!);
                SetService(CoreServiceKeys.NetworkCommandAdmissionResults, networkCommandAdmissionResults!);
                SetService(CoreServiceKeys.EntityOrderAdmissionResults, orderAdmissionResults);
                SetService(CoreServiceKeys.NetworkEntityTable, networkEntityTable!);
            }
            SetService(CoreServiceKeys.OrderTypeRegistry, orderTypeRegistry);
            SetService(CoreServiceKeys.OrderRuleRegistry, orderRuleRegistry);
            RebuildAiRuntime();
            SetService(CoreServiceKeys.AiRuntime, AiRuntime);
            SetService(CoreServiceKeys.OrderBufferSystem, orderBufferSystem);
            SetService(CoreServiceKeys.OrderRequestQueue, orderRequestQueue);
            SetService(CoreServiceKeys.ResponseChainTelemetryBuffer, responseChainTelemetry);
            SetService(CoreServiceKeys.ChainOrderQueue, chainOrderQueue);
            SetService(CoreServiceKeys.AttributeSinkRegistry, attributeSinks);
            SetService(CoreServiceKeys.AttributeBindingRegistry, attributeBindings);
            SetService(CoreServiceKeys.InputActionAttributeBindingRegistry, inputActionAttributeBindings);
            SetService(CoreServiceKeys.CameraBehaviorInputState, cameraBehaviorInput);
            SetService(CoreServiceKeys.GraphEdgeCostOverlay, graphEdgeCostOverlay);
            SetService(CoreServiceKeys.ItemShapeRegistry, itemShapes);
            SetService(CoreServiceKeys.ItemLayoutRegistry, itemLayouts);
            SetService(CoreServiceKeys.ItemDefinitionRegistry, itemDefinitions);
            SetService(CoreServiceKeys.OwnershipResolver, ownershipResolver);
            SetService(CoreServiceKeys.InventoryRuntimeService, inventoryRuntime);
            SetService(CoreServiceKeys.ExchangeOperationRegistry, exchangeOperations);
            SetService(CoreServiceKeys.ExchangeScopedOperationStore, exchangeScopedOperations);
            SetService(CoreServiceKeys.ExchangeRuntime, exchangeRuntime);
            SetService(CoreServiceKeys.RelationshipTypeRegistry, relationshipTypeRegistry);
            SetService(CoreServiceKeys.RelationshipMetricRegistry, relationshipMetricRegistry);
            SetService(CoreServiceKeys.RelationshipFlagRegistry, relationshipFlagRegistry);
            SetService(CoreServiceKeys.RelationshipBandRegistry, relationshipBandRegistry);
            SetService(CoreServiceKeys.RelationshipReasonRegistry, relationshipReasonRegistry);
            SetService(CoreServiceKeys.RelationshipChangeBuffer, relationshipChangeBuffer);
            SetService(CoreServiceKeys.RelationshipRuntime, relationshipRuntime);
            SetService(CoreServiceKeys.RelationshipCatalogConfig, relationshipCatalog);
            SetService(CoreServiceKeys.RelationshipCatalogRuntime, relationshipCatalogRuntime);
            SetService(CoreServiceKeys.ControlDomainQuery, controlDomainQuery);
            SetService(CoreServiceKeys.DomainStanceQuery, domainStanceQuery);
            SetService(CoreServiceKeys.AssociationControlProfileRuntime, associationControlProfileRuntime);
            SetService(CoreServiceKeys.TeamEntityLookup, teamEntityLookup);
            SetService(CoreServiceKeys.PlayerEntityLookup, playerEntityLookup);
            SetService(CoreServiceKeys.EntitySetQueryRuntime, entitySetQueryRuntime);
            SetService(CoreServiceKeys.AuthoritativeInput, authoritativeInput);
            SetService(CoreServiceKeys.AuthoritativePointerButtons, authoritativePointerButtons);
            SetService(CoreServiceKeys.AuthoritativeGroundPointerOverride, authoritativeGroundPointerOverride);
            SetService(CoreServiceKeys.PresentationEventStream, presentationEventStream);
            SetService(CoreServiceKeys.PresentationOwnerChangeBuffer, presentationOwnerChanges);
            SetService(CoreServiceKeys.PerformerCommandBuffer, performerCommandBuffer);
            SetService(CoreServiceKeys.PresentationPrefabRegistry, presentationPrefabs);
            SetService(CoreServiceKeys.PresentationMeshAssetRegistry, meshAssets);
            SetService(CoreServiceKeys.PresentationMaterialRegistry, materialAssets);
            SetService(CoreServiceKeys.InstancedBatchAssetRegistry, instancedBatchAssets);
            SetService(CoreServiceKeys.InstancedBatchRequestBuffer, instancedBatchRequests);
            SetService(CoreServiceKeys.InstancedBatchOperationBuffer, instancedBatchOperations);
            SetService(CoreServiceKeys.PresentationBehaviorRegistry, presentationBehaviors);
            SetService(CoreServiceKeys.PresentationBehaviorResolver, presentationBehaviorResolver);
            SetService(CoreServiceKeys.AnimatorControllerRegistry, animatorControllers);
            SetService(CoreServiceKeys.AnimationClipRegistry, animationClips);
            SetService(CoreServiceKeys.AnimationProfileRegistry, animationProfiles);
            SetService(CoreServiceKeys.PresentationStableIdAllocator, presentationStableIds);
            SetService(CoreServiceKeys.PerformerVisualStableIdTable, performerVisualStableIds);
            SetService(CoreServiceKeys.PresentationStableDrawCache, stableDrawCache);
            SetService(CoreServiceKeys.PresentationTargetGeneration, presentationTargetGeneration);
            _primitiveDrawBuffer = primitiveDrawBuffer;
            _visualSnapshotBuffer = visualSnapshotBuffer;
            _visualProxyBuffer = visualProxyBuffer;
            _skinnedVisualBatchBuffer = skinnedVisualBatchBuffer;
            _presentationRequestBuffer = presentationRequestBuffer;
            _globalFieldVisualBuffer = globalFieldVisualBuffer;
            _soundRequestBuffer = soundRequestBuffer;
            _instancedBatchRequestBuffer = instancedBatchRequests;
            _instancedBatchOperationBuffer = instancedBatchOperations;
            _gasPresentationEvents = gasPresentationEvents;
            _groundOverlayBuffer = groundOverlayBuffer;
            _roadSplineBuffer = roadSplineBuffer;
            _worldHudBuffer = worldHudBuffer;
            SetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer, primitiveDrawBuffer);
            SetService(CoreServiceKeys.PresentationFrameReceiptBuffer, presentationFrameReceiptBuffer);
            SetService(CoreServiceKeys.PresentationVisualSnapshotBuffer, visualSnapshotBuffer);
            SetService(CoreServiceKeys.PresentationVisualProxyBuffer, visualProxyBuffer);
            SetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer, skinnedVisualBatchBuffer);
            SetService(CoreServiceKeys.PresentationRequestBuffer, presentationRequestBuffer);
            SetService(CoreServiceKeys.GlobalFieldVisualBuffer, globalFieldVisualBuffer);
            SetService(CoreServiceKeys.PresentationWorldHudBuffer, worldHudBuffer);
            SetService(CoreServiceKeys.PresentationWorldHudStrings, worldHudStrings);
            SetService(CoreServiceKeys.PresentationTextCatalog, presentationTextCatalog);
            SetService(CoreServiceKeys.PresentationTextLocaleSelection, presentationTextLocaleSelection);
            var screenHudBuffer = new ScreenHudBatchBuffer(presentationConfig.ScreenHudCapacity);
            SetService(CoreServiceKeys.PresentationScreenHudBuffer, screenHudBuffer);
            SetService(CoreServiceKeys.ScreenOverlayBuffer, new ScreenOverlayBuffer());
            SetService(CoreServiceKeys.MinimapRuntime, minimapRuntime);
            SetService(CoreServiceKeys.MinimapMarkerBuffer, minimapMarkerBuffer);
            SetService(CoreServiceKeys.MinimapScreenMarkerBuffer, minimapScreenMarkerBuffer);
            SetService(CoreServiceKeys.ChunkDebugPanelRuntime, chunkDebugPanelRuntime);
            SetService(CoreServiceKeys.InputFrameConsumers, inputFrameConsumers);
            SetService(CoreServiceKeys.RenderDebugState, new RenderDebugState());
            SetService(CoreServiceKeys.PresentationAudienceRevealHidden, false);
            SetService(CoreServiceKeys.PresentationTimingDiagnostics, presentationTimingDiagnostics);
            SetService(CoreServiceKeys.TransientMarkerBuffer, transientMarkerBuffer);
            SetService(CoreServiceKeys.GasPresentationEventBuffer, gasPresentationEvents);
            SetService(CoreServiceKeys.GlobalPresentationEventBuffer, globalPresentationEvents);
            SetService(CoreServiceKeys.GroundOverlayBuffer, groundOverlayBuffer);
            SetService(CoreServiceKeys.RoadSplineBuffer, roadSplineBuffer);
            SetService(CoreServiceKeys.SoundRequestBuffer, soundRequestBuffer);
            SetService(CoreServiceKeys.PerformerDefinitionRegistry, performerDefinitions);
            SetService(CoreServiceKeys.PerformerEntityRuntime, performerRuntime);
            SetService(CoreServiceKeys.PerformerAnimatorStateBuffer, performerAnimatorStates);
            SetService(CoreServiceKeys.SurfaceSourcePayloadRegistry, surfacePayloads);
            SetService(CoreServiceKeys.SurfaceSourceRuntimeRegistry, surfaceRuntime);
            var platformManagedCameraDrivers = new PlatformManagedCameraDriverRegistry();
            SetService(CoreServiceKeys.PlatformManagedCameraDriverRegistry, platformManagedCameraDrivers);
            GameSession.Camera.SetPlatformManagedCameraDriverRegistry(platformManagedCameraDrivers);
            var virtualCameraRegistry = new VirtualCameraRegistry();
            new VirtualCameraDefinitionLoader(ConfigPipeline, virtualCameraRegistry).Load(ConfigCatalog, ConfigConflictReport);
            SetService(CoreServiceKeys.VirtualCameraRegistry, virtualCameraRegistry);
            SetService(CoreServiceKeys.CameraImpulseRuntime, cameraImpulseRuntime);
            GameSession.Camera.SetVirtualCameraRegistry(virtualCameraRegistry);
            GameSession.Camera.SetImpulseRuntime(cameraImpulseRuntime);
            var questDefinitions = new QuestDefinitionRegistry();
            new QuestConfigLoader(ConfigPipeline, questDefinitions).Load(ConfigCatalog, ConfigConflictReport);
            var questRuntime = new QuestRuntimeService(World, questDefinitions);
            SetService(CoreServiceKeys.QuestDefinitionRegistry, questDefinitions);
            SetService(CoreServiceKeys.QuestRuntimeService, questRuntime);
            var narrativeDefinitions = new NarrativeDefinitionRegistry();
            new NarrativeConfigLoader(ConfigPipeline, narrativeDefinitions).Load(ConfigCatalog, ConfigConflictReport);
            var narrativeDirector = new NarrativeDirector(this, narrativeDefinitions, questRuntime);
            SetService(CoreServiceKeys.NarrativeDefinitions, narrativeDefinitions);
            SetService(CoreServiceKeys.NarrativeDirector, narrativeDirector);
            _cameraRuntimeSystem = new CameraRuntimeSystem(World, GameSession.Camera, GlobalContext, virtualCameraRegistry);
            RegisterSystem(new GasBudgetResetSystem(gasBudget, orderTerminalResults, orderAdmissionResults), SystemGroup.SchemaUpdate);
            RegisterSystem(schemaUpdateSystem, SystemGroup.SchemaUpdate);
            RegisterSystem(new AssociationControlProfileSystem(World, associationControlProfileRuntime), SystemGroup.SchemaUpdate);

            // Phase 0.5: 保存上一帧位置（插值前置条件，必须在所有移动系统之前）
            RegisterSystem(new SavePreviousWorldPositionSystem(World), SystemGroup.SchemaUpdate);

            // Phase 1: InputCollection
            RegisterSystem(sessionSystem, SystemGroup.InputCollection); // Session handles input gathering
            RegisterSystem(new AuthoritativeInputSnapshotSystem(authoritativeInput, authoritativeInputAccumulator), SystemGroup.LocalInput);
            RegisterSystem(new AuthoritativePointerButtonSnapshotSystem(authoritativePointerButtons, authoritativePointerButtonsAccumulator), SystemGroup.LocalInput);
            RegisterSystem(new LocalPlayerEntityResolverSystem(World, GlobalContext), SystemGroup.LocalInput);
            // WASD axis intent -> throttled move orders through the OrderQueue (RFC-0065 INT-6,
            // DEC-15); enablement and parameters come from the active control scheme's axisMove
            // declaration (single source of truth, hot-switch aware).
            RegisterSystem(
                new AxisMoveOrderSystem(World, GlobalContext, controlSchemeRuntime, orderQueue),
                SystemGroup.LocalInput);
            RegisterSystem(new InputActionAttributeBindingSystem(World, GlobalContext, inputActionAttributeBindings), SystemGroup.InputCollection);
            RegisterSystem(new NarrativeRuntimeSystem(narrativeDirector), SystemGroup.InputCollection);
            RegisterSystem(clockSystem, SystemGroup.InputCollection);
            RegisterSystem(entityLocalClockSystem, SystemGroup.InputCollection);
            RegisterSystem(timedTagSystem, SystemGroup.InputCollection);
            RegisterSystem(new ProgressionScopeBindingSystem(World, progressionEvaluator, progressionScopeKeys), SystemGroup.InputCollection);
            RegisterSystem(
                new InventoryEquipmentGrantSyncSystem(World, inventoryRuntime, effectRequestQueue, abilityDefinitions),
                SystemGroup.InputCollection);
            RegisterSystem(new AbilityFormRoutingSystem(World, abilityFormSets, tagOps), SystemGroup.InputCollection);
            RegisterSystem(new UtilityAiThinkScheduleSystem(World, clock, AiRuntime.UtilityRuntime), SystemGroup.InputCollection);
            _worldToGridSyncSystem = new WorldToGridSyncSystem(World, SpatialCoords);
            _spatialPartitionUpdateSystem = new SpatialPartitionUpdateSystem(World, _spatialPartition, WorldSizeSpec);
            SetService(
                CoreServiceKeys.SpatialPartitionMembership,
                (ISpatialPartitionMembership)_spatialPartitionUpdateSystem);
            RegisterSystem(_worldToGridSyncSystem, SystemGroup.PostMovement);
            RegisterSystem(_spatialPartitionUpdateSystem, SystemGroup.PostMovement);
            RegisterSystem(
                new VisionSystem(
                    World,
                    GameSession,
                    visionFogLayerRegistry,
                    visionFogFieldStore,
                    visionResolver,
                    fogKnowledgeProjector,
                    knowledgeProjectionStore,
                    new PlayerOwnedEntityObserverResolver(playerEntityLookup)),
                SystemGroup.PostMovement);
            RegisterSystem(
                new UtilityAiDecisionSystem(
                    World,
                    clock,
                    AiRuntime.UtilityRuntime,
                    SpatialQueries,
                    abilityDefinitions,
                    graphProgramRegistry,
                    gasGraphApi,
                    orderQueue),
                SystemGroup.PostMovement);
            RegisterPhysics2DSystems(
                clock,
                physics2dTickPolicy,
                physics2dSolverConfig,
                physics2dBroadphasePolicy,
                componentAuthoringContext);

            // Phase 2: AbilityActivation
            RegisterSystem(orderBufferSystem, SystemGroup.AbilityActivation);
            RegisterSystem(abilityEndOrderSystem, SystemGroup.AbilityActivation);
            RegisterSystem(stopOrderSystem, SystemGroup.AbilityActivation);
            RegisterSystem(instantCompleteOrderSystem, SystemGroup.AbilityActivation);
            RegisterSystem(reactionSystem, SystemGroup.AbilityActivation);
            RegisterSystem(abilitySystem, SystemGroup.AbilityActivation);
            RegisterSystem(abilityExecSystem, SystemGroup.AbilityActivation);
            // RFC-0065 CTX-6: exec lifecycle push/pop of interaction context frames.
            RegisterSystem(new AbilityExecInteractionContextSystem(World, interactionContextStack, interactionContextProfileRegistry, abilityDefinitions), SystemGroup.AbilityActivation);
            RegisterSystem(moveToOrderSystem, SystemGroup.AbilityActivation);
            RegisterSystem(relationshipProcessingSystem, SystemGroup.AbilityActivation);

            // Phase 3: EffectProcessing (含响应链)
            var responseChainOrderTypes = new ResponseChainOrderTypes
            {
                ChainPass = cfgChainPass,
                ChainNegate = cfgChainNegate,
                ChainActivateEffect = cfgChainActivateEffect
            };
            RegisterSystem(new DestroyWhenParentExecutionEndsSystem(World), SystemGroup.EffectProcessing);
            RegisterSystem(new ManifestationMotion2DSystem(World), SystemGroup.EffectProcessing);
            var entityLifecycleServices = new EntityLifecycleRuntimeServices(
                World,
                MapLoader.TemplateRegistry,
                MapLoader.EntityTemplateKeys,
                presentationStableIds,
                tagOps,
                performerRuntime,
                performerDefinitions,
                componentAuthoringContext);
            RegisterSystem(new EffectProcessingLoopSystem(World, effectRequestQueue, clock, gasConditions, gasRuntimeCapacity.EffectLifetimeSnapshotCapacity, gasRuntimeCapacity.EffectFanOutCommandCapacity, gasBudget, effectTemplateRegistry, inputRequestQueue, chainOrderQueue, responseChainTelemetry, orderRequestQueue, responseChainOrderTypes, gasPresentationEvents, SpatialQueries, runtimeEntitySpawnQueue, runtimeEntityLifecycleQueue, entityLifecycleServices, phaseExecutor: phaseExecutor, graphApi: gasGraphApi, tagOps: tagOps, exchangeRuntime: exchangeRuntime, progressionEvaluator: progressionEvaluator, orderTypeRegistry: orderTypeRegistry, orderRuleRegistry: orderRuleRegistry, stepRateHz: stepRateHz, relationshipRuntime: relationshipRuntime, knowledgeAreaRevealRuntime: knowledgeAreaRevealRuntime, maxWorkUnitsPerSlice: gasRuntimeCapacity.EffectProcessingMaxWorkUnitsPerSlice, orderIntake: orderQueue), SystemGroup.EffectProcessing);
            RegisterSystem(new ProjectileRuntimeSystem(
                World,
                effectRequestQueue,
                SpatialQueries,
                gasRuntimeCapacity.ProjectileCollisionCandidateCapacity,
                gasRuntimeCapacity.ProjectileRuntimeEntityCapacity), SystemGroup.EffectProcessing);
            RegisterSystem(
                new RuntimeEntitySpawnSystem(
                    World,
                    runtimeEntitySpawnQueue,
                MapLoader.TemplateRegistry,
                MapLoader.EntityTemplateKeys,
                presentationStableIds,
                effectRequestQueue,
                runtimeEntitySpawnReceiptQueue,
                performerRuntime,
                performerDefinitions,
                presentationEventStream,
                _spatialPartition,
                WorldSizeSpec,
                presentationTimingDiagnostics,
                componentAuthoringContext,
                ownership: ownershipResolver,
                playerLookup: playerEntityLookup,
                teamLookup: teamEntityLookup,
                relationships: relationshipRuntime,
                memberOfTypeId: memberOfRelationshipTypeId),
                SystemGroup.EffectProcessing);
            RegisterSystem(
                new RuntimeEntityLifecycleSystem(
                    World,
                    runtimeEntityLifecycleQueue,
                    effectRequestQueue,
                    runtimeEntityLifecycleReceiptQueue),
                SystemGroup.EffectProcessing);
            const string manifestationObstacleBridgeSystemTypeName = "Ludots.Core.Physics2D.Systems.ManifestationObstacleBridge2DSystem";
            var manifestationObstacleBridgeType = Type.GetType($"{manifestationObstacleBridgeSystemTypeName}, Ludots.Physics2D", throwOnError: false);
            if (manifestationObstacleBridgeType != null)
            {
                object shapeStorage = GetService(CoreServiceKeys.Physics2DShapeStorage)
                    ?? throw new InvalidOperationException("Manifestation obstacle bridge requires Physics2D shape storage to be registered.");
                if (Activator.CreateInstance(manifestationObstacleBridgeType, World, shapeStorage) is ISystem<float> manifestationObstacleBridgeSystem)
                {
                    RegisterSystem(manifestationObstacleBridgeSystem, SystemGroup.EffectProcessing);
                }
                else
                {
                    throw new InvalidOperationException($"Failed to create manifestation obstacle bridge system '{manifestationObstacleBridgeSystemTypeName}'.");
                }
            }
            const string runtimeNavMeshObstacleSystemTypeName = "Ludots.Core.Physics2D.Systems.RuntimeNavMeshObstacleDirtySystem";
            var runtimeNavMeshObstacleType = Type.GetType($"{runtimeNavMeshObstacleSystemTypeName}, Ludots.Physics2D", throwOnError: false);
            if (runtimeNavMeshObstacleType != null)
            {
                if (Activator.CreateInstance(runtimeNavMeshObstacleType, this) is ISystem<float> runtimeNavMeshObstacleSystem)
                {
                    RegisterSystem(runtimeNavMeshObstacleSystem, SystemGroup.EffectProcessing);
                }
                else
                {
                    throw new InvalidOperationException($"Failed to create runtime navmesh obstacle dirty system '{runtimeNavMeshObstacleSystemTypeName}'.");
                }
            }
            RegisterSystem(new DisplacementRuntimeSystem(World), SystemGroup.EffectProcessing);

            // Phase 4: AttributeCalculation
            RegisterSystem(aggSystem, SystemGroup.AttributeCalculation);
            RegisterSystem(bindingSystem, SystemGroup.AttributeCalculation);
            RegisterSystem(_cameraRuntimeSystem, SystemGroup.AttributeCalculation);

            // Phase 5: DeferredTriggerCollection
            SetService(CoreServiceKeys.DeferredTriggerQueue, deferredTriggerQueue);
            RegisterSystem(deferredTriggerCollectionSystem, SystemGroup.DeferredTriggerCollection);
            RegisterSystem(deferredTriggerProcessSystem, SystemGroup.DeferredTriggerCollection);

            // Phase 6: Cleanup
            RegisterSystem(orderContinuationSystem, SystemGroup.Cleanup);
            RegisterSystem(new UtilityAiCombatMemoryCleanupSystem(World, clock), SystemGroup.Cleanup);
            RegisterSystem(new GraphOutputValueCleanupSystem(World, graphOutputValueStore), SystemGroup.Cleanup);
            RegisterSystem(new OrderAdmissionEntityIntakeEndSystem(orderAdmissionResults), SystemGroup.Cleanup);

            RegisterSystem(new GameplayEventDispatchSystem(EventBus, gasBudget), SystemGroup.EventDispatch);
            RegisterSystem(new GasBudgetReportSystem(gasBudget, gasDiagnostics, orderAdmissionResults), SystemGroup.EventDispatch);

            // Phase 7.1: Project gameplay-side presentation facts into the presentation stream
            // and owner-change index consumed by performer owner bindings.
            // Changed-bit components must remain readable until presentation systems consume them,
            // so the actual clear runs at the tail of the presentation pipeline.
            RegisterSystem(gameplayPresentationProjectionSystem, SystemGroup.ClearPresentationFlags);
            RegisterSystem(new ProgressionScopeTagRevisionSystem(World), SystemGroup.ClearPresentationFlags);
            RegisterSystem(new OrderAdmissionGenerationEndSystem(orderAdmissionResults), SystemGroup.ClearPresentationFlags);
            _cooperativeSimulation = new PhaseOrderedCooperativeSimulation(
                _systemGroups,
                OnFixedStepCompleted,
                presentationTimingDiagnostics,
                OnFixedStepStarted);

            var responseChainUiState = new ResponseChainUiState();
            SetService(CoreServiceKeys.ResponseChainUiState, responseChainUiState);

            // PresentationFrameSetupSystem MUST be the first presentation system
            // It calculates InterpolationAlpha for all visual sync systems
            var presentationFrameSetup = new PresentationFrameSetupSystem(
                World,
                Pacemaker,
                ResolveExternalPresentationInterpolationSource);
            RegisterPresentationSystem(presentationFrameSetup);
            SetService(CoreServiceKeys.PresentationFrameSetup, presentationFrameSetup);

            RegisterPresentationSystem(new ProjectilePresentationBootstrapSystem(World, presentationStableIds));
            RegisterPresentationSystem(new PresentationStableIdBootstrapSystem(World, presentationStableIds));
            // WorldToVisualSyncSystem: 插值 WorldPositionCm → VisualTransform（必须在 PresentationFrameSetup 之后）
            RegisterPresentationSystem(new WorldToVisualSyncSystem(World));
            // TerrainHeightSyncSystem: 采样地形高度写入 VisualTransform.Y，使实体贴附地表
            RegisterPresentationSystem(new TerrainHeightSyncSystem(World, GlobalContext, presentationTimingDiagnostics));
            RegisterPresentationSystem(presentationEntityLifecycleSystem);
            RegisterPresentationSystem(new ResponseChainDirectorSystem(World, orderRequestQueue, responseChainTelemetry, responseChainUiState, transientMarkerBuffer, presentationPrefabs));
            RegisterPresentationSystem(new ResponseChainHumanOrderSourceSystem(GlobalContext, responseChainUiState, chainOrderQueue));
            RegisterPresentationSystem(new ResponseChainAiOrderSourceSystem(responseChainUiState, chainOrderQueue, cfgChainPass));
            RegisterPresentationSystem(new ResponseChainUiSyncSystem(GlobalContext, responseChainUiState, orderTypeRegistry));
            RegisterPresentationSystem(globalPresentationEventProjectionSystem);
            RegisterPresentationSystem(new EntityCollectionPresentationEventSystem(World, entityCollectionStore, presentationEventStream, GameSession));
            RegisterPresentationSystem(new InstancedBatchBehaviorSystem(
                World,
                performerDefinitions,
                performerRuntime,
                instancedBatchAssets,
                instancedBatchOperations,
                presentationEventStream,
                presentationOwnerChanges));
            // PerformerRuleSystem reads events and produces commands.
            RegisterPresentationSystem(performerRuleSystem);
            // PerformerRuntimeSystem consumes commands, manages instance lifecycle.
            RegisterPresentationSystem(performerRuntimeSystem);
            RegisterPresentationSystem(new InstancedBatchEmissionSystem(
                World,
                performerDefinitions,
                instancedBatchAssets,
                instancedBatchRequests,
                instancedBatchSubmissionRuntime,
                presentationEventStream));
            // Entity-anchored performers follow owner VisualTransform before behavior/animator/emit reads them.
            RegisterPresentationSystem(new PerformerEntityTransformSyncSystem(World, performerRuntime, performerDefinitions, presentationTimingDiagnostics));
            // PerformerBehaviorSystem drives blackboard-bound behavior before animator and emit read it.
            RegisterPresentationSystem(performerBehaviorSystem);
            RegisterPresentationSystem(animatorRuntimeSystem);
            RegisterPresentationSystem(new PerformerMinimapMarkerSystem(World, performerDefinitions, minimapMarkerBuffer, presentationTimingDiagnostics));
            // PerformerEmitSystem is the Wave 4 asset-binding emitter.
            RegisterPresentationSystem(performerEmitSystem);
            RegisterPresentationSystem(clearPresentationFlagsSystem);
            RegisterPresentationSystem(surfaceSourceFlushSystem);
            RegisterPresentationSystem(surfaceSourceLifecycleSystem);
            RegisterPresentationSystem(chunkSurfaceBakeSystem);
            RegisterPresentationSystem(presentationEntityFinalizeDestroySystem);
            RegisterPresentationSystem(presentationRequestFlushSystem);
            RegisterPresentationSystem(new MinimapPresentationSystem(this, minimapRuntime, minimapMarkerBuffer, minimapScreenMarkerBuffer, presentationTimingDiagnostics));
            RegisterPresentationSystem(new ChunkDebugPanelPresentationSystem(this, chunkDebugPanelRuntime));
        }

        private void OnFixedStepStarted(float fixedDt)
        {
            GameSession.BeginSimulationTick();
            if (GetService(CoreServiceKeys.NetworkProcessRole) == NetworkProcessRole.AuthoritativeServer)
            {
                GetRequiredNetworkRuntime().BeforeAuthoritativeTick(
                    checked((uint)GameSession.SimulationTicks.ExecutingTick));
            }
        }

        private void OnFixedStepCompleted(float fixedDt)
        {
            _physics2DController?.AfterPhysicsFixedTick();
            _gasController?.AfterFixedTick();
            GameSession.CommitFixedUpdate();
            if (GetService(CoreServiceKeys.NetworkProcessRole) == NetworkProcessRole.AuthoritativeServer)
            {
                GetRequiredNetworkRuntime().AfterAuthoritativeCommit(
                    checked((uint)GameSession.SimulationTicks.CommittedTick));
            }
        }

        private void ApplyBuiltInTimeFlowScales()
        {
            if (_timeFlow == null)
            {
                return;
            }

            Time.TimeScale = _timeFlow.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation) / 1000f;

            if (GetService(CoreServiceKeys.GasClockStepPolicy) is GasClockStepPolicy gasClockStepPolicy)
            {
                gasClockStepPolicy.SetScalePermille(_timeFlow.GetScalePermilleRelativeToParent(TimeFlowDomainIds.Gas));
            }

        }

        private void RegisterPhysics2DSystems(
            IClock clock,
            Physics2DTickPolicy physics2dTickPolicy,
            Physics2DSolverConfig physics2dSolverConfig,
            Physics2DBroadphasePolicy physics2dBroadphasePolicy,
            ComponentAuthoringContext componentAuthoringContext)
        {
            const string physics2dAssemblyName = "Ludots.Physics2D";
            const string shapeStorageTypeName = "Ludots.Core.Physics2D.ShapeDataStorage2D";
            const string physics2dSystemTypeName = "Ludots.Core.Physics2D.Ticking.Physics2DSimulationSystem";
            const string worldSyncSystemTypeName = "Ludots.Core.Physics2D.Systems.Physics2DToWorldPositionSyncSystem";

            ArgumentNullException.ThrowIfNull(physics2dSolverConfig);
            ArgumentNullException.ThrowIfNull(physics2dBroadphasePolicy);
            ArgumentNullException.ThrowIfNull(componentAuthoringContext);

            Type? physics2dSystemType = TryResolveOptionalAssemblyType(physics2dAssemblyName, physics2dSystemTypeName);
            Type? worldSyncSystemType = TryResolveOptionalAssemblyType(physics2dAssemblyName, worldSyncSystemTypeName);
            Type? shapeStorageType = TryResolveOptionalAssemblyType(physics2dAssemblyName, shapeStorageTypeName);
            if (physics2dSystemType == null && worldSyncSystemType == null && shapeStorageType == null)
            {
                return;
            }

            if (physics2dSystemType == null || worldSyncSystemType == null || shapeStorageType == null)
            {
                throw new InvalidOperationException(
                    $"Physics2D startup requires '{physics2dSystemTypeName}', '{worldSyncSystemTypeName}', and '{shapeStorageTypeName}' when '{physics2dAssemblyName}' is present.");
            }

            object shapeStorage = GetService(CoreServiceKeys.Physics2DShapeStorage)
                ?? Activator.CreateInstance(shapeStorageType)
                ?? throw new InvalidOperationException("Failed to create Physics2D ShapeDataStorage2D.");
            componentAuthoringContext.Set(ComponentAuthoringServiceKeys.Physics2DShapeStorage, shapeStorage);
            SetService(CoreServiceKeys.Physics2DShapeStorage, shapeStorage);

            object? physics2dSystemObj = Activator.CreateInstance(
                physics2dSystemType,
                World,
                clock,
                physics2dTickPolicy,
                physics2dSolverConfig,
                shapeStorage,
                physics2dBroadphasePolicy);
            if (physics2dSystemObj is not ISystem<float> physics2dSystem)
            {
                throw new InvalidOperationException($"Failed to create Physics2D simulation system '{physics2dSystemTypeName}'.");
            }

            object? worldSyncSystemObj = Activator.CreateInstance(worldSyncSystemType, World);
            if (worldSyncSystemObj is not ISystem<float> worldSyncSystem)
            {
                throw new InvalidOperationException($"Failed to create Physics2D world sync system '{worldSyncSystemTypeName}'.");
            }

            RegisterSystem(physics2dSystem, SystemGroup.InputCollection);
            RegisterSystem(worldSyncSystem, SystemGroup.PostMovement);
            GlobalContext["Ludots.Core.Physics2D.Ticking.Physics2DSimulationSystem"] = physics2dSystem;
            GlobalContext["Ludots.Core.Physics2D.Systems.Physics2DToWorldPositionSyncSystem"] = worldSyncSystem;
        }

        private static Type? TryResolveOptionalAssemblyType(string assemblyName, string typeName)
        {
            Type? type = Type.GetType($"{typeName}, {assemblyName}", throwOnError: false);
            if (type != null)
            {
                return type;
            }

            try
            {
                AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(assemblyName));
            }
            catch (FileNotFoundException)
            {
                return null;
            }

            return Type.GetType($"{typeName}, {assemblyName}", throwOnError: false);
        }

        private static int RequireConfiguredOrderTypeId(
            IReadOnlyDictionary<string, int> configuredIds,
            OrderTypeRegistry orderTypeRegistry,
            string orderTypeKey,
            string configPath)
        {
            if (!configuredIds.TryGetValue(orderTypeKey, out int configuredId) || configuredId <= 0)
            {
                throw new InvalidOperationException(
                    $"game.json {configPath} must explicitly define positive order type id for '{orderTypeKey}'.");
            }

            int registeredId = RequireRegisteredOrderTypeId(orderTypeRegistry, orderTypeKey);
            if (configuredId != registeredId)
            {
                throw new InvalidOperationException(
                    $"game.json {configPath}.{orderTypeKey} id {configuredId} does not match GAS/order_types.json orderTypeId {registeredId}.");
            }

            return configuredId;
        }

        private static int RequireRegisteredOrderTypeId(OrderTypeRegistry orderTypeRegistry, string orderTypeKey)
        {
            if (!orderTypeRegistry.TryGetId(orderTypeKey, out int orderTypeId) ||
                orderTypeId <= 0 ||
                !orderTypeRegistry.IsRegistered(orderTypeId))
            {
                throw new InvalidOperationException(
                    $"GAS/order_types.json must explicitly define order type '{orderTypeKey}'.");
            }

            return orderTypeId;
        }

        private static int RequireAbilityFormSetId(string formSetKey)
        {
            int formSetId = AbilityFormSetIdRegistry.GetId(formSetKey);
            if (formSetId <= 0)
            {
                throw new InvalidOperationException(
                    $"Cast preference references unknown ability form set '{formSetKey}'. Declare it in GAS/ability_form_sets.json.");
            }

            return formSetId;
        }

        public void LoadMap(string mapId) => LoadMap(MapLoadRequest.FromMapId(mapId));

        public void LoadMap(MapLoadRequest request)
        {
            bool isNetworkStartupMapLoad = IsNetworkStartupMapLoad(request.MapId);
            if (_networkStartupFailure != null && isNetworkStartupMapLoad)
            {
                ThrowNetworkStartupAlreadyFailed();
            }

            if (isNetworkStartupMapLoad)
            {
                _networkStartupMapLoadObserved = true;
            }

            try
            {
                LoadMapCore(request);
            }
            catch (Exception ex)
            {
                RecordNetworkStartupFailure(request.MapId, ex);
                throw;
            }
        }

        private void LoadMapCore(MapLoadRequest request)
        {
            string mapId = request.MapIdValue;
            Diagnostics.Log.Info(in LogChannels.Engine, $"Loading Map: {mapId}");
            var mid = request.MapId;

            EnsureMapSessionInfrastructure();

            if (MapSessions.GetSession(mid) != null)
            {
                UnloadMap(mapId);
            }

            var mapConfig = MapManager.LoadMap(mapId);

            if (mapConfig != null)
            {
                var previousFocused = MapSessions.FocusedSession;
                IVisualHeightmap? visualHeightmap = MapVisualHeightmapLoader.Load(VFS, ModLoader?.LoadedModIds, mapConfig);
                StructureCollisionAsset? structureCollision = MapStructureCollisionLoader.Load(VFS, ModLoader?.LoadedModIds, mapConfig);

                // Create new session with boards (additive — old sessions stay)
                var session = MapSessions.CreateSession(mid, mapConfig, null);
                session.LaunchContext = request.LaunchContext;
                session.VisualHeightmap = visualHeightmap;
                BindStructureCollisionSession(session, visualHeightmap, structureCollision);
                CreateBoardsForSession(session, mapConfig);
                if (previousFocused != null)
                {
                    CancelPendingMapResume(previousFocused.MapId, $"Map resume canceled because '{mid.Value}' became focused.", markFailed: true);
                    CancelPendingMapLoad(previousFocused.MapId, $"Map load canceled because '{mid.Value}' became focused.", markFailed: true);
                }
                MapSessions.PushFocused(mid);
                if (previousFocused != null)
                {
                    SetMapEntitiesSuspended(previousFocused.MapId, true);
                }
                _mapLoadStatuses[mid] = GetInitialMapLoadStatus();
                SetCurrentMapSession(session);
                bool pendingMapLoadStarted = TryStartPendingMapLoad(session, mapConfig, isPush: false, out var loadStatus);

                // Apply primary board spatial config to engine-level services
                var primaryBoard = session.PrimaryBoard;
                if (primaryBoard != null)
                {
                    ApplyBoardSpatialConfig(primaryBoard);
                    LoadBoardTerrainData(session, mapConfig);
                }

                LoadNavForMap(mapId, mapConfig);
                LoadPathingForSession(session);
                Diagnostics.Log.Info(in LogChannels.Engine, "Creating Entities from MapConfig...");
                var entityIndex = MapLoader.LoadEntitiesAndIndex(mapConfig);
                session.EntityIndex = entityIndex;
                SetSessionParticipants(
                    session,
                    ParticipantBindingResolver.Resolve(
                        session,
                        World,
                        entityIndex,
                        GetService(CoreServiceKeys.RelationshipRuntime),
                        GetService(CoreServiceKeys.RelationshipTypeRegistry),
                        GetService(CoreServiceKeys.OwnershipResolver),
                        GetService(CoreServiceKeys.RelationshipCatalogConfig)?.Stance));
                SetMapEntitiesSuspended(mid, true);

                // Instantiate map triggers + apply decorators
                var definition = ((MapManager)MapManager).GetDefinition(mid);
                var triggers = InstantiateMapTriggers(definition, mapConfig);
                ApplyTriggerDecorators(triggers);
                if (triggers.Count > 0)
                {
                    foreach (var t in triggers) session.AddTrigger(t);
                    TriggerManager.RegisterMapTriggers(mid, triggers);
                }

                if (pendingMapLoadStarted)
                {
                    Diagnostics.Log.Info(in LogChannels.Engine, $"MapLoaded deferred for '{mapId}'.");
                    return;
                }

                CompleteMapLoad(session, mapConfig, loadStatus);
            }
            else
            {
                Diagnostics.Log.Error(in LogChannels.Engine, $"Failed to load map {mapId}");
                ThrowIfNetworkStartupMapLoadFailed(mid, $"Map configuration '{mapId}' could not be loaded.");
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
            CancelPendingMapLoad(mid, $"Map '{mapId}' was unloaded before completion.", markFailed: false);
            CancelPendingMapResume(mid, $"Map '{mapId}' was unloaded before resume completion.", markFailed: false);

            var unloadCtx = CreateMapEventContext(session);
            CompleteLifecycleEvent(TriggerManager.FireMapEventAsync(mid, GameEvents.MapUnloaded, unloadCtx));
            _massNavigationRuntime.HandleMapUnloaded(this, mid);
            TriggerManager.UnregisterMapTriggers(mid, unloadCtx);
            RemoveRuntimeEntitySpawnRequestsForMap(mid);

            // Check if this map is at the top of the focus stack
            var focused = MapSessions.FocusedSession;
            bool wasFocused = focused != null && focused.MapId == mid;

            MapSessions.UnloadSession(mid, World);
            _mapLoadStatuses.Remove(mid);

            if (wasFocused && MapSessions.FocusedSession != null)
            {
                var restored = MapSessions.FocusedSession;
                _mapLoadStatuses[restored.MapId] = GetInitialMapLoadStatus();
                RestoreFocusedMapSession(restored);
                if (TryStartPendingMapResume(restored, session, out var resumeStatus))
                {
                    Diagnostics.Log.Info(in LogChannels.Engine, $"MapResumed deferred for '{restored.MapId.Value}'.");
                    return;
                }

                CompleteMapResume(restored, resumeStatus);
            }
            else if (wasFocused)
            {
                SetCurrentMapSession(null);
                ClearNavServices();
                ClearPathingServices();
            }
        }

        private void RemoveRuntimeEntitySpawnRequestsForMap(MapId mapId)
        {
            RuntimeEntitySpawnQueue? spawnQueue = GetService(CoreServiceKeys.RuntimeEntitySpawnQueue);
            spawnQueue?.RemoveForMap(mapId);
        }

        private static void BindStructureCollisionSession(
            MapSession session,
            IVisualHeightmap? visualHeightmap,
            StructureCollisionAsset? structureCollision)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            session.StructureCollisionAsset = structureCollision;
            session.StructureCollisionRuntimeState = structureCollision != null
                ? new StructureCollisionRuntimeState(structureCollision)
                : null;
            session.GroundSurfaceSampler = visualHeightmap != null || structureCollision != null
                ? new GroundSurfaceSampler(visualHeightmap, structureCollision, session.StructureCollisionRuntimeState)
                : null;
        }

        /// <summary>
        /// Push a nested inner map (三国志12 mode). Outer map is suspended, inner map becomes active.
        /// </summary>
        public void PushMap(string innerMapId, Dictionary<string, object> passthrough = null)
        {
            EnsureMapSessionInfrastructure();

            var inner = new MapId(innerMapId);
            var outerSession = MapSessions?.FocusedSession;

            var mapConfig = MapManager.LoadMap(innerMapId);
            if (mapConfig == null)
            {
                Diagnostics.Log.Error(in LogChannels.Engine, $"PushMap: Failed to load inner map '{innerMapId}'");
                return;
            }

            IVisualHeightmap? visualHeightmap = MapVisualHeightmapLoader.Load(VFS, ModLoader?.LoadedModIds, mapConfig);
            StructureCollisionAsset? structureCollision = MapStructureCollisionLoader.Load(VFS, ModLoader?.LoadedModIds, mapConfig);

            // Create inner session with parent context from outer
            MapContext parentCtx = outerSession?.Context;
            var session = MapSessions.CreateSession(inner, mapConfig, parentCtx);
            session.VisualHeightmap = visualHeightmap;
            BindStructureCollisionSession(session, visualHeightmap, structureCollision);

            // Pass through data to inner context
            if (passthrough != null)
            {
                foreach (var kvp in passthrough) session.Context.Set(kvp.Key, kvp.Value);
            }

            CreateBoardsForSession(session, mapConfig);
            if (outerSession != null)
            {
                CancelPendingMapResume(outerSession.MapId, $"Map resume canceled because '{inner.Value}' was pushed on top.", markFailed: true);
                CancelPendingMapLoad(outerSession.MapId, $"Map load canceled because '{inner.Value}' was pushed on top.", markFailed: true);
            }

            // Push focus — outer becomes Suspended
            MapSessions.PushFocused(inner);
            if (outerSession != null)
            {
                SetMapEntitiesSuspended(outerSession.MapId, true);
            }
            _mapLoadStatuses[inner] = GetInitialMapLoadStatus();
            SetCurrentMapSession(session);
            bool pendingMapLoadStarted = TryStartPendingMapLoad(session, mapConfig, isPush: true, out var loadStatus);

            var primaryBoard = session.PrimaryBoard;
            if (primaryBoard != null)
            {
                ApplyBoardSpatialConfig(primaryBoard);
                LoadBoardTerrainData(session, mapConfig);
                LoadNavForMap(innerMapId, mapConfig);
            }
            LoadPathingForSession(session);

            var entityIndex = MapLoader.LoadEntitiesAndIndex(mapConfig);
            session.EntityIndex = entityIndex;
            SetSessionParticipants(
                session,
                ParticipantBindingResolver.Resolve(
                    session,
                    World,
                    entityIndex,
                    GetService(CoreServiceKeys.RelationshipRuntime),
                    GetService(CoreServiceKeys.RelationshipTypeRegistry),
                    GetService(CoreServiceKeys.OwnershipResolver),
                    GetService(CoreServiceKeys.RelationshipCatalogConfig)?.Stance));
            SetMapEntitiesSuspended(inner, true);

            // Fire MapSuspended on outer (scoped)
            if (outerSession != null)
            {
                _massNavigationRuntime.HandleMapSuspended(this, outerSession.MapId);
                var suspendCtx = CreateMapEventContext(outerSession);
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

            if (pendingMapLoadStarted)
            {
                Diagnostics.Log.Info(in LogChannels.Engine, $"MapLoaded deferred for pushed map '{innerMapId}'.");
                return;
            }

            CompleteMapLoad(session, mapConfig, loadStatus);
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
                CancelPendingMapLoad(innerSession.MapId, $"Map '{innerSession.MapId.Value}' was popped before completion.", markFailed: false);

                var unloadCtx = CreateMapEventContext(innerSession);
                CompleteLifecycleEvent(TriggerManager.FireMapEventAsync(innerSession.MapId, GameEvents.MapUnloaded, unloadCtx));
                _massNavigationRuntime.HandleMapUnloaded(this, innerSession.MapId);
                TriggerManager.UnregisterMapTriggers(innerSession.MapId, unloadCtx);
                RemoveRuntimeEntitySpawnRequestsForMap(innerSession.MapId);
            }

            // Pop focus — restores previous session
            var poppedId = MapSessions.PopFocused();
            if (innerSession != null)
            {
                MapSessions.UnloadSession(poppedId, World);
                _mapLoadStatuses.Remove(poppedId);
            }

            var outerSession = MapSessions.FocusedSession;
            if (outerSession != null)
            {
                _mapLoadStatuses[outerSession.MapId] = GetInitialMapLoadStatus();
                RestoreFocusedMapSession(outerSession);
                if (TryStartPendingMapResume(outerSession, innerSession, out var resumeStatus))
                {
                    Diagnostics.Log.Info(in LogChannels.Engine, $"MapResumed deferred for '{outerSession.MapId.Value}'.");
                    return;
                }

                CompleteMapResume(outerSession, resumeStatus);
            }
            else
            {
                SetCurrentMapSession(null);
                ClearNavServices();
                ClearPathingServices();
            }
        }

        private void ApplyDefaultCamera(MapConfig mapConfig)
        {
            if (GetService(CoreServiceKeys.NetworkProcessRole) == NetworkProcessRole.AuthoritativeServer)
            {
                GameSession.Camera.ResetVirtualCameras();
                Diagnostics.Log.Info(
                    in LogChannels.Engine,
                    $"Skipped DefaultCamera for map '{mapConfig?.Id ?? "<unknown>"}' because the authoritative server has no local presentation camera.");
                return;
            }

            if (ShouldSkipDefaultCameraOnLoad(mapConfig))
            {
                Diagnostics.Log.Info(
                    in LogChannels.Engine,
                    $"Skipped DefaultCamera for map '{mapConfig?.Id ?? "<unknown>"}' due to tag '{SkipDefaultCameraOnLoadTag}'.");
                return;
            }

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
                followTarget: CameraFollowTargetFactory.Build(
                    World,
                    GlobalContext,
                    definition.FollowTargetKind,
                    ResolveDefaultCameraFollowCollectionOwner(definition.FollowTargetKind),
                    definition.FollowCollectionKey),
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

            EnsureCameraRuntimeConfigured();
            if (!GameSession.Camera.IsRuntimeConfigured)
            {
                throw new InvalidOperationException(
                    $"Map '{mapConfig.Id}' requires local camera runtime services, but CoreServiceKeys.ViewController is not bound.");
            }

            GameSession.Camera.SynchronizeActiveVirtualCameraBoundsAndHeight();

            var state = GameSession.Camera.State;
            Diagnostics.Log.Info(in LogChannels.Engine, $"Applied DefaultCamera: yaw={state.Yaw} pitch={state.Pitch} dist={state.DistanceCm}cm fov={state.FovYDeg}");
        }

        private Entity ResolveDefaultCameraFollowCollectionOwner(CameraFollowTargetKind followTargetKind)
        {
            if (!CameraFollowTargetFactory.RequiresEntityCollection(followTargetKind))
            {
                return Entity.Null;
            }

            Entity sessionLocal = CurrentMapSession?.LocalPlayerEntity ?? Entity.Null;
            if (sessionLocal != Entity.Null && World.IsAlive(sessionLocal))
            {
                return sessionLocal;
            }

            Entity serviceLocal = GetService(CoreServiceKeys.LocalPlayerEntity);
            return serviceLocal != Entity.Null && World.IsAlive(serviceLocal)
                ? serviceLocal
                : Entity.Null;
        }

        private static bool ShouldSkipDefaultCameraOnLoad(MapConfig mapConfig)
        {
            if (mapConfig?.Tags == null)
            {
                return false;
            }

            foreach (string tag in mapConfig.Tags)
            {
                if (string.Equals(tag, SkipDefaultCameraOnLoadTag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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
                RemoveService(CoreServiceKeys.HexMetrics);
                SetService(CoreServiceKeys.LoadedChunks, board.LoadedChunks);
                HexGridAOI = null;
                loadedChunks = board.LoadedChunks;
            }

            sharedSpatialQueries.SetLoadedChunks(loadedChunks);

            if (board is INodeGraphBoard nodeGraphBoard)
            {
                SetService(CoreServiceKeys.LoadedGraphRuntime, nodeGraphBoard.GraphRuntime);
                _gasGraphRuntimeApi?.BindLoadedGraphRuntime(nodeGraphBoard.GraphRuntime);
            }
            else
            {
                RemoveService(CoreServiceKeys.LoadedGraphRuntime);
                _gasGraphRuntimeApi?.BindLoadedGraphRuntime(null);
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
            LogicTerrain = null;
            RemoveService(CoreServiceKeys.VertexMap);
            RemoveService(CoreServiceKeys.LogicTerrain);

            foreach (var board in session.AllBoards)
            {
                if (board is ITerrainBoard terrainBoard)
                {
                    BoardConfig boardConfig = FindConfigForBoard(board.Name, mapConfig);
                    string dataFile = boardConfig?.DataFile;
                    if (!string.IsNullOrWhiteSpace(dataFile))
                    {
                        var vtxMap = LoadVertexMapFromFile(dataFile);
                        if (vtxMap != null)
                        {
                            terrainBoard.VertexMap = vtxMap;
                            terrainBoard.LogicTerrain = new VertexMapLogicTerrainField(vtxMap);
                            VertexMap = vtxMap;
                            LogicTerrain = terrainBoard.LogicTerrain;
                            SetService(CoreServiceKeys.VertexMap, vtxMap);
                            SetService(CoreServiceKeys.LogicTerrain, LogicTerrain);
                            Diagnostics.Log.Info(in LogChannels.Engine, $"Loaded VertexMap {vtxMap.WidthInChunks}x{vtxMap.HeightInChunks} for board '{board.Name}'");
                        }

                        continue;
                    }

                    if (board is GridBoard gridBoard && boardConfig != null)
                    {
                        int widthCells = checked(boardConfig.WidthInMacroTiles * SpatialScaleDefaults.MacroTileCells);
                        int heightCells = checked(boardConfig.HeightInMacroTiles * SpatialScaleDefaults.MacroTileCells);
                        gridBoard.LogicTerrain = new FlatGridLogicTerrainField(
                            widthCells,
                            heightCells,
                            boardConfig.GridCellSizeCm,
                            boardConfig.ChunkSizeCells);
                        if (LogicTerrain == null)
                        {
                            LogicTerrain = gridBoard.LogicTerrain;
                            SetService(CoreServiceKeys.LogicTerrain, LogicTerrain);
                        }

                        Diagnostics.Log.Info(
                            in LogChannels.Engine,
                            $"Created flat grid LogicTerrainField {widthCells}x{heightCells} cells for board '{board.Name}'");
                    }
                }
            }

            session.TerrainPresentation = ResolvedTerrainPresentation.Resolve(session);
        }

        private BoardConfig FindConfigForBoard(string boardName, MapConfig mapConfig)
        {
            if (mapConfig.Boards == null) return null;
            foreach (var b in mapConfig.Boards)
            {
                if (string.Equals(b.Name, boardName, StringComparison.OrdinalIgnoreCase))
                {
                    return b;
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

        private static int ResolveGroundOverlayShapeId(string key)
        {
            if (Enum.TryParse<Ludots.Core.Presentation.Rendering.GroundOverlayShape>(key, ignoreCase: false, out var shape))
            {
                return (int)shape;
            }

            throw new InvalidOperationException(
                $"Unknown GroundOverlay shape '{key}'. Expected Circle, Cone, Line, or Ring.");
        }

        private void LoadNavForMap(string mapId, MapConfig mapConfig)
        {
            ClearNavServices();

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

            if (LogicTerrain == null) throw new InvalidOperationException($"NavMesh enabled but LogicTerrainField is not loaded for map '{mapId}'.");

            var bakeConfig = LoadNavMeshBakeConfig();
            SetService(CoreServiceKeys.NavMeshBakeConfig, bakeConfig);

            var agentProfiles = GetService(CoreServiceKeys.AgentProfiles)
                ?? throw new InvalidOperationException("NavMesh bootstrap requires AgentProfiles.");
            var profileRegistry = new NavMeshProfileRegistry(bakeConfig, agentProfiles);
            SetService(CoreServiceKeys.NavMeshProfiles, profileRegistry);
            var areaCosts = BuildAreaCostTable(bakeConfig);
            if (bakeConfig.Layers == null || bakeConfig.Layers.Count == 0) throw new InvalidOperationException("NavMeshBakeConfig.layers is empty.");

            var stores = new Dictionary<NavQueryServiceKey, NavTileStore>(bakeConfig.Layers.Count * profileRegistry.Count);
            int widthChunks = LogicTerrain.WidthChunks;
            int heightChunks = LogicTerrain.HeightChunks;

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

            var navRegistry = new NavQueryServiceRegistry(stores);
            SetService(CoreServiceKeys.NavQueryServices, navRegistry);
            if (bakeConfig.ParsedMode == NavBakeMode.RuntimeIncremental)
            {
                var runtimeObstacles = new NavObstacleSet();
                SetService(CoreServiceKeys.RuntimeNavMeshObstacles, runtimeObstacles);
                var runtimeContext = new NavBakeContext
                {
                    MapId = mapId,
                    ModId = string.Empty,
                    SourceUri = $"Core:Maps/{mapId}.runtime-navmesh",
                    Terrain = LogicTerrain,
                    Obstacles = runtimeObstacles,
                    Config = bakeConfig,
                    AgentProfiles = agentProfiles,
                    Targets = new[] { new NavBakeTileCoord(0, 0) },
                    BuildConfig = new NavBuildConfig(
                        bakeConfig.RuntimeIncremental.HeightScaleMeters,
                        bakeConfig.RuntimeIncremental.MinWalkableUpDot,
                        bakeConfig.RuntimeIncremental.CliffHeightThreshold),
                    TileVersion = 1,
                    Mode = bakeConfig.ParsedMode,
                    Algorithm = bakeConfig.ParsedAlgorithm,
                    Execution = new NavBakeExecutionOptions { Parallel = false, MaxDegreeOfParallelism = 1 }
                };
                var runtimeQueue = new RuntimeIncrementalNavMeshRebuildQueue(
                    new NavBakeService(new CdtNavBakeAlgorithm()),
                    runtimeContext,
                    navRegistry,
                    profileRegistry);
                SetService(CoreServiceKeys.RuntimeNavMeshRebuildQueue, runtimeQueue);
            }
            if (MapSessions?.FocusedSession?.PrimaryBoard is INavigableBoard navigableBoard)
            {
                navigableBoard.NavServices = navRegistry;
            }
        }

        internal void LoadNavForMapForTests(string mapId, MapConfig mapConfig)
        {
            LoadNavForMap(mapId, mapConfig);
        }

        private void ClearNavServices()
        {
            RemoveService(CoreServiceKeys.NavMeshBakeConfig);
            RemoveService(CoreServiceKeys.NavMeshProfiles);
            RemoveService(CoreServiceKeys.NavQueryServices);
            RemoveService(CoreServiceKeys.RuntimeNavMeshObstacles);
            RemoveService(CoreServiceKeys.RuntimeNavMeshRebuildQueue);
        }

        private void LoadPathingForSession(MapSession session)
        {
            ClearPathingServices();

            if (session == null)
            {
                return;
            }

            var pathingConfig = new PathingConfigLoader(ConfigPipeline).Load(ConfigCatalog, ConfigConflictReport);
            var pathStore = new PathStore(PathStoreMaxPaths, PathStoreMaxPointsPerPath);
            LoadedGraphRuntime loadedGraphRuntime = null;
            IPathService nodeGraphService = null;

            if (session.PrimaryBoard is INodeGraphBoard nodeGraphBoard)
            {
                loadedGraphRuntime = nodeGraphBoard.GraphRuntime;
                nodeGraphService = new NodeGraphPathServiceAdapter(loadedGraphRuntime, pathStore);
            }

            IPathService pathService = nodeGraphService;

            var navRegistry = GetService(CoreServiceKeys.NavQueryServices);
            var navProfiles = GetService(CoreServiceKeys.NavMeshProfiles);
            var agentProfiles = GetService(CoreServiceKeys.AgentProfiles)
                ?? throw new InvalidOperationException("Pathing bootstrap requires AgentProfiles.");
            var graphEdgeCostOverlay = GetService(CoreServiceKeys.GraphEdgeCostOverlay);
            bool hasNavServices = navRegistry != null && navProfiles != null;
            bool requiresGraphPathing = RequiresGraphPathing(pathingConfig);
            bool requiresNavMeshPathing = RequiresNavMeshPathing(pathingConfig);
            bool requiresOnlyDirectPathing = RequiresOnlyDirectPathing(pathingConfig);

            if (loadedGraphRuntime == null && !hasNavServices && !requiresOnlyDirectPathing)
            {
                Diagnostics.Log.Info(
                    in LogChannels.Engine,
                    $"Pathing bootstrap skipped for map '{session.MapId.Value}': no node-graph board or navmesh query service is loaded.");
                return;
            }

            if (loadedGraphRuntime == null && requiresGraphPathing)
            {
                throw new InvalidOperationException(
                    $"Map '{session.MapId.Value}' pathing config selects graph-capable routing but the primary board is not a node graph.");
            }

            if (!hasNavServices && requiresNavMeshPathing)
            {
                throw new InvalidOperationException(
                    $"Map '{session.MapId.Value}' pathing config selects mesh-capable routing but navmesh query services are not loaded.");
            }

            if (hasNavServices)
            {
                IPathService navMeshService = CreateDefaultNavMeshPathService(pathingConfig, navRegistry, navProfiles, agentProfiles, pathStore);
                if (loadedGraphRuntime != null)
                {
                    IPathService autoPathService = new AutoPathService(loadedGraphRuntime, navRegistry, navProfiles, agentProfiles, pathStore, pathingConfig, graphEdgeCostOverlay);
                    pathService = new PathServiceRouter(nodeGraphService, navMeshService, autoPathService, pathStore);
                }
                else
                {
                    pathService = navMeshService;
                }
            }
            else if (loadedGraphRuntime != null)
            {
                pathService = new AutoPathService(loadedGraphRuntime, agentProfiles, pathStore, pathingConfig, graphEdgeCostOverlay);
            }
            else
            {
                pathService = new DirectPathService(pathStore);
            }

            SetService(CoreServiceKeys.PathingConfig, pathingConfig);
            SetService(CoreServiceKeys.PathStore, pathStore);
            SetService(CoreServiceKeys.PathService, pathService);

            Diagnostics.Log.Info(
                in LogChannels.Engine,
                $"Pathing bootstrap ready for map '{session.MapId.Value}': service={pathService.GetType().Name}, board='{session.PrimaryBoard?.Name ?? "<none>"}', loadedGraphChunks={loadedGraphRuntime?.LoadedChunkCount ?? 0}.");
        }

        private void ClearPathingServices()
        {
            RemoveService(CoreServiceKeys.PathingConfig);
            RemoveService(CoreServiceKeys.PathStore);
            RemoveService(CoreServiceKeys.PathService);
            RemoveService(CoreServiceKeys.LoadedGraphRuntime);
        }

        private static bool RequiresGraphPathing(PathingConfig pathingConfig)
        {
            if (pathingConfig?.AgentTypes == null)
            {
                return false;
            }

            for (int i = 0; i < pathingConfig.AgentTypes.Count; i++)
            {
                var agent = pathingConfig.AgentTypes[i];
                if (agent == null)
                {
                    continue;
                }

                if (agent.Selection?.Mode == PathSelectionMode.AutoCheapest ||
                    agent.Selection?.Mode == PathSelectionMode.PreferGraph)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RequiresNavMeshPathing(PathingConfig pathingConfig)
        {
            if (pathingConfig?.AgentTypes == null)
            {
                return false;
            }

            for (int i = 0; i < pathingConfig.AgentTypes.Count; i++)
            {
                var agent = pathingConfig.AgentTypes[i];
                if (agent == null)
                {
                    continue;
                }

                if (agent.Selection?.Mode == PathSelectionMode.AutoCheapest ||
                    agent.Selection?.Mode == PathSelectionMode.PreferMesh)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RequiresOnlyDirectPathing(PathingConfig pathingConfig)
        {
            if (pathingConfig?.AgentTypes == null || pathingConfig.AgentTypes.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < pathingConfig.AgentTypes.Count; i++)
            {
                var agent = pathingConfig.AgentTypes[i];
                if (agent == null || agent.Selection?.Mode != PathSelectionMode.Direct)
                {
                    return false;
                }
            }

            return true;
        }

        private static IPathService CreateDefaultNavMeshPathService(
            PathingConfig pathingConfig,
            NavQueryServiceRegistry navRegistry,
            NavMeshProfileRegistry navProfiles,
            AgentProfileRegistry agentProfiles,
            PathStore pathStore)
        {
            if (pathingConfig?.AgentTypes == null || pathingConfig.AgentTypes.Count == 0)
            {
                throw new InvalidOperationException("PathingConfig.agentTypes must define at least one agent type for navmesh path service bootstrap.");
            }

            var agent = pathingConfig.AgentTypes[0];
            if (agent == null || !navProfiles.TryGetIndex(agent.ProfileId, out int profileIndex))
            {
                throw new InvalidOperationException(
                    $"PathingConfig default agent profileId '{agent?.ProfileId ?? "<null>"}' is not registered in navmesh profiles.");
            }

            AgentProfileConfig agentProfile = agentProfiles.Require(agent.ProfileId, $"PathingConfig default agent '{agent.Id}'");
            var areaCosts = BuildPathNavAreaCosts(agent.NavMesh);
            if (!navRegistry.TryCreateQuery(agentProfile.Layer, profileIndex, areaCosts, out var query))
            {
                throw new InvalidOperationException(
                    $"PathingConfig default agent '{agent.Id}' cannot create navmesh query for layer {agentProfile.Layer}, profile '{agent.ProfileId}'.");
            }

            return new NavMeshPathServiceAdapter(query, pathStore);
        }

        private static NavAreaCostTable BuildPathNavAreaCosts(PathingNavMeshConfig cfg)
        {
            var arr = new Fix64[256];
            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = Fix64.OneValue;
            }

            if (cfg?.AreaCosts != null)
            {
                for (int i = 0; i < cfg.AreaCosts.Count; i++)
                {
                    var area = cfg.AreaCosts[i];
                    if (area == null)
                    {
                        continue;
                    }

                    if (area.AreaId < 0 || area.AreaId > 255)
                    {
                        throw new InvalidOperationException($"Invalid pathing areaId: {area.AreaId}");
                    }

                    if (area.Cost <= 0f || float.IsNaN(area.Cost))
                    {
                        throw new InvalidOperationException($"Invalid pathing cost for areaId={area.AreaId}");
                    }

                    arr[area.AreaId] = Fix64.FromFloat(area.Cost);
                }
            }

            return new NavAreaCostTable(arr);
        }

        private NavMeshBakeConfig LoadNavMeshBakeConfig()
        {
            var agentProfiles = GetService(CoreServiceKeys.AgentProfiles)
                ?? throw new InvalidOperationException("NavMeshBakeConfig requires AgentProfiles.");
            return new NavMeshBakeConfigLoader(ConfigPipeline, agentProfiles).Load(ConfigCatalog, ConfigConflictReport);
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

        public void LoadEntryMap(string mapId)
        {
            if (string.IsNullOrWhiteSpace(mapId))
            {
                throw new ArgumentException("Map id is required.", nameof(mapId));
            }

            if (!string.Equals(MergedConfig?.StartupMapId, mapId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"LoadEntryMap requires startup map '{MergedConfig?.StartupMapId}', got '{mapId}'.");
            }

            LoadStartupMap();
        }

        public void LoadStartupMap()
        {
            string mapId = MergedConfig?.StartupMapId;
            if (string.IsNullOrWhiteSpace(mapId))
            {
                throw new InvalidOperationException("StartupMapId is required.");
            }

            MapLaunchContext? launchContext =
                GetService(CoreServiceKeys.NetworkProcessRole) == NetworkProcessRole.Standalone &&
                MergedConfig!.StartupLocalPlayerId > 0
                ? MapLaunchContext.Create(MergedConfig.StartupLocalPlayerId)
                : null;
            LoadMap(new MapLoadRequest(new MapId(mapId), launchContext));
        }

        public void LoadMap(MapId mapId) => LoadMap(MapLoadRequest.FromMapId(mapId.Value));

        public void Start()
        {
            if (_networkStartupFailure != null)
            {
                ThrowNetworkStartupAlreadyFailed();
            }

            ValidateNetworkRuntimeBeforeStart();
            _isRunning = true;
            Time.TotalTime = 0;
            Time.FixedTotalTime = 0;
            _cooperativeSimulation?.Reset();
            if (Pacemaker is RealtimePacemaker realtime) realtime.Reset();

            var ctx = CreateContext();
            try
            {
                Diagnostics.Log.Info(in LogChannels.Engine, "Firing GameStart event...");
                CompleteLifecycleEvent(TriggerManager.FireEventAsync(GameEvents.GameStart, ctx));
                _gameStartCompleted = true;
                TryActivateNetworkRuntime();
            }
            catch
            {
                _isRunning = false;
                throw;
            }
        }

        public void Stop()
        {
            _isRunning = false;
        }

        public void ConfigureNetworkRuntime(NetworkProcessRole role, INetworkRuntimePort runtime)
        {
            if (role == NetworkProcessRole.Standalone)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(role),
                    "A network runtime requires an authoritative server or replicated client role.");
            }

            ArgumentNullException.ThrowIfNull(runtime);
            if (runtime.Role != role)
            {
                throw new ArgumentException(
                    $"Network runtime role {runtime.Role} does not match requested process role {role}.",
                    nameof(runtime));
            }

            if (MergedConfig?.Networking == null)
            {
                throw new InvalidOperationException(
                    "Cannot configure a network runtime without game.json networking configuration.");
            }

            if (TryGetService(CoreServiceKeys.NetworkRuntimePort, out INetworkRuntimePort existing) &&
                !ReferenceEquals(existing, runtime))
            {
                throw new InvalidOperationException(
                    "A different network runtime is already configured for this engine.");
            }

            SetService(CoreServiceKeys.NetworkProcessRole, role);
            SetService(CoreServiceKeys.NetworkRuntimePort, runtime);
        }

        private void TryActivateNetworkRuntime()
        {
            NetworkProcessRole role = GetService(CoreServiceKeys.NetworkProcessRole);
            if (role == NetworkProcessRole.Standalone || !_isRunning || !_gameStartCompleted || _networkRuntimeReady)
            {
                return;
            }

            if (_networkRuntimeActivating)
            {
                return;
            }

            if (_networkStartupFailure != null)
            {
                ThrowNetworkStartupAlreadyFailed();
            }

            string? startupMapIdValue = MergedConfig?.StartupMapId;
            if (string.IsNullOrWhiteSpace(startupMapIdValue))
            {
                return;
            }

            var startupMapId = new MapId(startupMapIdValue);
            MapSession? mapSession = MapSessions?.GetSession(startupMapId);
            if (mapSession == null)
            {
                if (_networkStartupMapLoadObserved)
                {
                    var exception = new InvalidOperationException(
                        $"Network startup map '{startupMapId.Value}' is no longer available before network runtime activation.");
                    RecordNetworkStartupFailure(startupMapId, exception);
                    throw exception;
                }

                return;
            }

            if (CurrentMapSession == null || CurrentMapSession.MapId != startupMapId)
            {
                var exception = new InvalidOperationException(
                    $"Network startup map '{startupMapId.Value}' lost focus before network runtime activation.");
                RecordNetworkStartupFailure(startupMapId, exception);
                throw exception;
            }

            MapLoadStatus startupMapLoadStatus = GetMapLoadStatus(mapSession.MapId);
            if (!startupMapLoadStatus.Succeeded)
            {
                if (startupMapLoadStatus.Failed)
                {
                    ThrowIfNetworkStartupMapLoadFailed(
                        mapSession.MapId,
                        startupMapLoadStatus.ErrorMessage);
                }

                return;
            }

            if (_networkRuntimeActivationAttempted)
            {
                throw new InvalidOperationException(
                    "Network runtime activation already failed; the process cannot retry frozen schema composition.");
            }

            _networkRuntimeActivationAttempted = true;
            _networkRuntimeActivating = true;
            try
            {
                GetRequiredNetworkRuntime().Activate();
                Diagnostics.Log.Info(in LogChannels.Engine, "Firing NetworkRuntimeReady event...");
                CompleteLifecycleEvent(
                    TriggerManager.FireEventAsync(
                        GameEvents.NetworkRuntimeReady,
                        CreateMapEventContext(mapSession)));
                _networkRuntimeReady = true;
            }
            catch (Exception ex)
            {
                RecordNetworkStartupFailure(mapSession.MapId, ex);
                throw;
            }
            finally
            {
                _networkRuntimeActivating = false;
            }
        }

        private bool IsNetworkStartupMapLoad(MapId mapId)
        {
            return !_networkRuntimeReady &&
                   GetService(CoreServiceKeys.NetworkProcessRole) != NetworkProcessRole.Standalone &&
                   string.Equals(
                       mapId.Value,
                       MergedConfig?.StartupMapId,
                       StringComparison.OrdinalIgnoreCase);
        }

        private void RecordNetworkStartupFailure(MapId mapId, Exception exception)
        {
            if (!IsNetworkStartupMapLoad(mapId))
            {
                return;
            }

            _networkStartupFailure ??= exception;
            _isRunning = false;
        }

        private void ThrowIfNetworkStartupMapLoadFailed(MapId mapId, string? errorMessage)
        {
            if (!IsNetworkStartupMapLoad(mapId))
            {
                return;
            }

            string detail = string.IsNullOrWhiteSpace(errorMessage)
                ? "The map load completion gate returned a failure without an error message."
                : errorMessage;
            var exception = new InvalidOperationException(
                $"Network startup map '{mapId.Value}' failed to load: {detail}");
            RecordNetworkStartupFailure(mapId, exception);
            throw exception;
        }

        private void ThrowNetworkStartupAlreadyFailed()
        {
            throw new InvalidOperationException(
                "Network startup previously failed and cannot be restarted on the same engine instance.",
                _networkStartupFailure);
        }

        private void ValidateNetworkRuntimeBeforeStart()
        {
            bool networkingConfigured = MergedConfig?.Networking != null;
            bool hasRuntime = TryGetService(
                CoreServiceKeys.NetworkRuntimePort,
                out INetworkRuntimePort runtime);
            NetworkProcessRole role = GetService(CoreServiceKeys.NetworkProcessRole);
            if (!networkingConfigured)
            {
                if (hasRuntime || role != NetworkProcessRole.Standalone)
                {
                    throw new InvalidOperationException(
                        "A network process role was configured without game.json networking configuration.");
                }

                return;
            }

            if (!hasRuntime || role == NetworkProcessRole.Standalone)
            {
                throw new InvalidOperationException(
                    "game.json networking is configured, but the host did not install an explicit authoritative server or replicated client runtime.");
            }

            if (runtime.Role != role)
            {
                throw new InvalidOperationException(
                    $"Configured network runtime role {runtime.Role} does not match engine process role {role}.");
            }
        }

        private INetworkRuntimePort GetRequiredNetworkRuntime()
        {
            if (!TryGetService(
                    CoreServiceKeys.NetworkRuntimePort,
                    out INetworkRuntimePort runtime))
            {
                throw new InvalidOperationException(
                    "The configured network process role has no runtime port.");
            }

            return runtime;
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
            if (TryGetService(CoreServiceKeys.NetworkRuntimePort, out INetworkRuntimePort networkRuntime))
            {
                networkRuntime.Dispose();
                RemoveService(CoreServiceKeys.NetworkRuntimePort);
            }

            if (_pendingMapLoads.Count > 0)
            {
                var pendingMapIds = new List<MapId>(_pendingMapLoads.Keys);
                for (int i = 0; i < pendingMapIds.Count; i++)
                {
                    CancelPendingMapLoad(pendingMapIds[i], $"Engine disposed before '{pendingMapIds[i].Value}' completed.", markFailed: false);
                }
            }

            if (_pendingMapResumes.Count > 0)
            {
                var pendingResumeMapIds = new List<MapId>(_pendingMapResumes.Keys);
                for (int i = 0; i < pendingResumeMapIds.Count; i++)
                {
                    CancelPendingMapResume(pendingResumeMapIds[i], $"Engine disposed before '{pendingResumeMapIds[i].Value}' resume completed.", markFailed: false);
                }
            }

            if (ModLoader != null)
            {
                Diagnostics.Log.Info(in LogChannels.Engine, "Unloading ModLoader contexts...");
                ModLoader.UnloadAll();
            }

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

            _engineServices?.LegacyStore.Clear();
            _gameStartCompleted = false;
            _networkRuntimeActivationAttempted = false;
            _networkRuntimeActivating = false;
            _networkRuntimeReady = false;
            _networkStartupMapLoadObserved = false;
            _networkStartupFailure = null;
            _systemGroups?.Clear();
            _presentationSystems?.Clear();
            _inputRuntimeSystem = null;
            _cameraRuntimeSystem = null;
            _primitiveDrawBuffer = null;
            _visualSnapshotBuffer = null;
            _visualProxyBuffer = null;
            _skinnedVisualBatchBuffer = null;
            _presentationRequestBuffer = null;
            _globalFieldVisualBuffer = null;
            _soundRequestBuffer = null;
            _instancedBatchRequestBuffer = null;
            _instancedBatchOperationBuffer = null;
            _gasPresentationEvents = null;
            _groundOverlayBuffer = null;
            _roadSplineBuffer = null;
            _worldHudBuffer = null;
            _physics2DController = null;
            _gasController = null;
            _timeFlow = null;
            _worldToGridSyncSystem = null;
            _spatialPartitionUpdateSystem = null;
            MergedConfig = null;
            ConfigPipeline = null;
            ConfigCatalog = null;
        }

        public void Tick(float platformDeltaTime)
        {
            if (!_isRunning) return;

            long tickStart = System.Diagnostics.Stopwatch.GetTimestamp();
            var presentationTiming = GetService(CoreServiceKeys.PresentationTimingDiagnostics);

            ApplyBuiltInTimeFlowScales();
            float dt = platformDeltaTime * Time.TimeScale;
            Time.DeltaTime = dt;
            Time.TotalTime += dt;

            GameTask.Update(dt);
            SyncContext.ProcessQueue();
            EnsureCameraRuntimeConfigured();
            _inputRuntimeSystem?.Update(dt);
            ProcessPendingMapLoads();

            NetworkProcessRole networkRole = GetService(CoreServiceKeys.NetworkProcessRole);
            INetworkRuntimePort? networkRuntime = null;
            if (networkRole != NetworkProcessRole.Standalone)
            {
                if (!_networkRuntimeReady)
                {
                    presentationTiming?.ObserveTotalTick(
                        (System.Diagnostics.Stopwatch.GetTimestamp() - tickStart) * 1000d /
                        System.Diagnostics.Stopwatch.Frequency);
                    return;
                }

                networkRuntime = GetRequiredNetworkRuntime();
                networkRuntime.PumpTransport();
            }

            // 1. Simulation Loop (GAS, Physics, AI) - Controlled by Pacemaker
            if (networkRole == NetworkProcessRole.ReplicatedClient)
            {
                networkRuntime!.PumpReplicatedClient(dt);
                UpdateReplicatedClientLocalInput(dt);
            }
            else if (!_simulationBudgetFused)
            {
                long simulationStart = System.Diagnostics.Stopwatch.GetTimestamp();
                int tickBefore = GameSession.CurrentTick;
                if (presentationTiming?.SystemBreakdownEnabled == true)
                {
                    presentationTiming.BeginSimulationSystemBreakdown();
                }

                Pacemaker.Update(dt, _cooperativeSimulation, SimulationBudgetMsPerFrame, SimulationMaxSlicesPerLogicFrame);
                presentationTiming?.ObserveSimulation((System.Diagnostics.Stopwatch.GetTimestamp() - simulationStart) * 1000d / System.Diagnostics.Stopwatch.Frequency);

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

            if (networkRole == NetworkProcessRole.AuthoritativeServer)
            {
                presentationTiming?.ObserveTotalTick(
                    (System.Diagnostics.Stopwatch.GetTimestamp() - tickStart) * 1000d /
                    System.Diagnostics.Stopwatch.Frequency);
                return;
            }

            // 2. Visual Loop (Rendering, UI, Animation) - standalone and replicated clients only.
            long presentationStart = System.Diagnostics.Stopwatch.GetTimestamp();
            Update(dt);
            presentationTiming?.ObservePresentation((System.Diagnostics.Stopwatch.GetTimestamp() - presentationStart) * 1000d / System.Diagnostics.Stopwatch.Frequency);
            presentationTiming?.ObserveTotalTick((System.Diagnostics.Stopwatch.GetTimestamp() - tickStart) * 1000d / System.Diagnostics.Stopwatch.Frequency);
        }

        private void UpdateReplicatedClientLocalInput(float dt)
        {
            if (!_systemGroups.TryGetValue(SystemGroup.LocalInput, out List<ISystem<float>>? systems))
            {
                throw new InvalidOperationException("Replicated client requires the LocalInput system group.");
            }

            for (int i = 0; i < systems.Count; i++)
            {
                systems[i].Update(dt);
            }

            _cameraRuntimeSystem?.Update(dt);
        }

        private IPresentationInterpolationSource? ResolveExternalPresentationInterpolationSource()
        {
            if (GetService(CoreServiceKeys.NetworkProcessRole) != NetworkProcessRole.ReplicatedClient)
            {
                return null;
            }

            INetworkRuntimePort runtime = GetRequiredNetworkRuntime();
            return runtime as IPresentationInterpolationSource
                ?? throw new InvalidOperationException(
                    "Replicated client network runtime must provide the presentation interpolation clock.");
        }

        private void Update(float dt)
        {
            _presentationRequestBuffer?.Clear();
            _soundRequestBuffer?.Clear();
            _instancedBatchRequestBuffer?.Clear();
            _instancedBatchOperationBuffer?.Clear();
            _groundOverlayBuffer?.ClearTransient();
            _roadSplineBuffer?.ClearTransient();
            _worldHudBuffer?.ClearTransient();
            var timingDiagnostics = GetService(CoreServiceKeys.PresentationTimingDiagnostics);
            bool captureSystemBreakdown = timingDiagnostics?.SystemBreakdownEnabled == true;
            if (captureSystemBreakdown)
            {
                timingDiagnostics!.BeginPresentationSystemBreakdown();
            }

            BeginPresentationCameraSnapshotScope();
            try
            {
                for (int i = 0; i < _presentationSystems.Count; i++)
                {
                    long systemStart = captureSystemBreakdown
                        ? System.Diagnostics.Stopwatch.GetTimestamp()
                        : 0L;
                    _presentationSystems[i].Update(dt);
                    if (captureSystemBreakdown)
                    {
                        double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - systemStart) * 1000d / System.Diagnostics.Stopwatch.Frequency;
                        timingDiagnostics!.ObservePresentationSystem(_presentationSystems[i].GetType().Name, elapsedMs);
                    }
                }
            }
            finally
            {
                EndPresentationCameraSnapshotScope();
            }

            if ((_instancedBatchRequestBuffer?.Count ?? 0) != 0 ||
                (_instancedBatchOperationBuffer?.Count ?? 0) != 0)
            {
                TryGetService(CoreServiceKeys.PresentationAdapterCapabilities, out PresentationAdapterCapabilities capabilities);
                InstancedBatchCapabilityValidator.Validate(
                    _instancedBatchRequestBuffer,
                    _instancedBatchOperationBuffer,
                    capabilities);
            }

        }

        private void BeginPresentationCameraSnapshotScope()
        {
            if (GetService(CoreServiceKeys.ScreenProjector) is Ludots.Core.Presentation.Camera.IPresentationCameraSnapshotScope projectorScope)
            {
                projectorScope.BeginPresentationFrame();
            }

            if (GetService(CoreServiceKeys.ScreenRayProvider) is Ludots.Core.Presentation.Camera.IPresentationCameraSnapshotScope rayScope)
            {
                rayScope.BeginPresentationFrame();
            }
        }

        private void EndPresentationCameraSnapshotScope()
        {
            if (GetService(CoreServiceKeys.ScreenProjector) is Ludots.Core.Presentation.Camera.IPresentationCameraSnapshotScope projectorScope)
            {
                projectorScope.EndPresentationFrame();
            }

            if (GetService(CoreServiceKeys.ScreenRayProvider) is Ludots.Core.Presentation.Camera.IPresentationCameraSnapshotScope rayScope)
            {
                rayScope.EndPresentationFrame();
            }
        }

        private void EnsureCameraRuntimeConfigured()
        {
            var behaviorInput = GetService(CoreServiceKeys.CameraBehaviorInputState);
            var viewport = GetService(CoreServiceKeys.ViewController);
            if (behaviorInput == null || viewport == null)
            {
                return;
            }

            if (!GameSession.Camera.IsRuntimeConfigured)
            {
                GameSession.Camera.ConfigureRuntime(
                    behaviorInput,
                    viewport,
                    () => WorldSizeSpec.Bounds,
                    () => GetService(CoreServiceKeys.VisualHeightmap));
            }
        }

    }
}
