using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Navigation2D;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.AI.Config;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Bindings;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Map.Hex;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using Ludots.Core.UI;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Scripting
{
    /// <summary>
    /// Compile-time typed keys for Core engine services.
    /// Mod-specific keys should be defined inside each mod project, not here.
    /// </summary>
    public static class CoreServiceKeys
    {
        // --- ECS & Engine ---
        public static readonly ServiceKey<World> World = new("World");
        public static readonly ServiceKey<WorldMap> WorldMap = new("WorldMap");
        public static readonly ServiceKey<VertexMap> VertexMap = new("VertexMap");
        public static readonly ServiceKey<GameSession> GameSession = new("GameSession");
        public static readonly ServiceKey<GameEngine> Engine = new("Engine");
        public static readonly ServiceKey<GameConfig> GameConfig = new("GameConfig");
        public static readonly ServiceKey<SystemFactoryRegistry> SystemFactoryRegistry = new("SystemFactoryRegistry");
        public static readonly ServiceKey<TriggerDecoratorRegistry> TriggerDecoratorRegistry = new("TriggerDecoratorRegistry");

        // --- Map ---
        public static readonly ServiceKey<MapId> MapId = new("MapId");
        public static readonly ServiceKey<List<string>> MapTags = new("MapTags");
        public static readonly ServiceKey<MapFeatureFlags> MapFeatureFlags = new("MapFeatureFlags");
        public static readonly ServiceKey<MapSession> MapSession = new("MapSession");
        public static readonly ServiceKey<MapSessionManager> MapSessions = new("MapSessions");
        public static readonly ServiceKey<BoardIdRegistry> BoardIdRegistry = new("BoardIdRegistry");
        public static readonly ServiceKey<MapContext> MapContext = new("MapContext");

        // --- UI ---
        public static readonly ServiceKey<IUiSystem> UISystem = new("UISystem");
        public static readonly ServiceKey<object> UIRoot = new("UIRoot");
        public static readonly ServiceKey<bool> UiCaptured = new("UiCaptured");

        // --- Input ---
        public static readonly ServiceKey<PlayerInputHandler> InputHandler = new("InputHandler");
        public static readonly ServiceKey<IInputActionReader> AuthoritativeInput = new("AuthoritativeInput");
        public static readonly ServiceKey<IInputBackend> InputBackend = new("InputBackend");

        // --- Camera & View ---
        public static readonly ServiceKey<IViewController> ViewController = new("ViewController");
        public static readonly ServiceKey<IScreenProjector> ScreenProjector = new("ScreenProjector");
        public static readonly ServiceKey<IScreenRayProvider> ScreenRayProvider = new("ScreenRayProvider");
        public static readonly ServiceKey<CameraPoseRequest> CameraPoseRequest = new("CameraPoseRequest");
        public static readonly ServiceKey<VirtualCameraRequest> VirtualCameraRequest = new("VirtualCameraRequest");
        public static readonly ServiceKey<VirtualCameraRegistry> VirtualCameraRegistry = new("VirtualCameraRegistry");

        // --- GAS Core ---
        public static readonly ServiceKey<IClock> Clock = new("Clock");
        public static readonly ServiceKey<GasClockStepPolicy> GasClockStepPolicy = new("GasClockStepPolicy");
        public static readonly ServiceKey<GasClocks> GasClocks = new("GasClocks");
        public static readonly ServiceKey<GasBudget> GasBudget = new("GasBudget");
        public static readonly ServiceKey<GasController> GasController = new("GasController");
        public static readonly ServiceKey<GasConditionRegistry> GasConditionRegistry = new("GasConditionRegistry");
        public static readonly ServiceKey<TagOps> TagOps = new("TagOps");
        public static readonly ServiceKey<EffectTemplateRegistry> EffectTemplateRegistry = new("EffectTemplateRegistry");
        public static readonly ServiceKey<EffectRequestQueue> EffectRequestQueue = new("EffectRequestQueue");
        public static readonly ServiceKey<AbilityDefinitionRegistry> AbilityDefinitionRegistry = new("AbilityDefinitionRegistry");
        public static readonly ServiceKey<GraphProgramRegistry> GraphProgramRegistry = new("GraphProgramRegistry");
        public static readonly ServiceKey<ExtensionAttributeRegistry> ExtensionAttributeRegistry = new("ExtensionAttributeRegistry");
        public static readonly ServiceKey<AttributeSchemaUpdateQueue> AttributeSchemaUpdateQueue = new("AttributeSchemaUpdateQueue");
        public static readonly ServiceKey<DeferredTriggerQueue> DeferredTriggerQueue = new("DeferredTriggerQueue");
        public static readonly ServiceKey<AttributeSinkRegistry> AttributeSinkRegistry = new("AttributeSinkRegistry");
        public static readonly ServiceKey<AttributeBindingRegistry> AttributeBindingRegistry = new("AttributeBindingRegistry");

        // --- GAS Input / Selection / Orders ---
        public static readonly ServiceKey<InputRequestQueue> InputRequestQueue = new("InputRequestQueue");
        public static readonly ServiceKey<InputRequestQueue> AbilityInputRequestQueue = new("AbilityInputRequestQueue");
        public static readonly ServiceKey<InputResponseBuffer> InputResponseBuffer = new("InputResponseBuffer");
        public static readonly ServiceKey<SelectionRequestQueue> SelectionRequestQueue = new("SelectionRequestQueue");
        public static readonly ServiceKey<SelectionResponseBuffer> SelectionResponseBuffer = new("SelectionResponseBuffer");
        public static readonly ServiceKey<SelectionRuleRegistry> SelectionRuleRegistry = new("SelectionRuleRegistry");
        public static readonly ServiceKey<InteractionActionBindings> InteractionActionBindings = new("InteractionActionBindings");
        public static readonly ServiceKey<OrderQueue> OrderQueue = new("OrderQueue");
        public static readonly ServiceKey<OrderTypeRegistry> OrderTypeRegistry = new("OrderTypeRegistry");
        public static readonly ServiceKey<OrderRuleRegistry> OrderRuleRegistry = new("OrderRuleRegistry");
        public static readonly ServiceKey<OrderBufferSystem> OrderBufferSystem = new("OrderBufferSystem");
        public static readonly ServiceKey<OrderRequestQueue> OrderRequestQueue = new("OrderRequestQueue");
        public static readonly ServiceKey<ResponseChainTelemetryBuffer> ResponseChainTelemetryBuffer = new("ResponseChainTelemetryBuffer");
        public static readonly ServiceKey<OrderQueue> ChainOrderQueue = new("ChainOrderQueue");
        public static readonly ServiceKey<ResponseChainUiState> ResponseChainUiState = new("ResponseChainUiState");

        // --- Simulation ---
        public static readonly ServiceKey<SimulationLoopController> SimulationLoopController = new("SimulationLoopController");
        public static readonly ServiceKey<Physics2DTickPolicy> Physics2DTickPolicy = new("Physics2DTickPolicy");
        public static readonly ServiceKey<Physics2DController> Physics2DController = new("Physics2DController");
        public static readonly ServiceKey<Navigation2DTickPolicy> Navigation2DTickPolicy = new("Navigation2DTickPolicy");

        // --- Presentation ---
        public static readonly ServiceKey<PresentationEventStream> PresentationEventStream = new("PresentationEventStream");
        public static readonly ServiceKey<PresentationCommandBuffer> PresentationCommandBuffer = new("PresentationCommandBuffer");
        public static readonly ServiceKey<PrefabRegistry> PresentationPrefabRegistry = new("PresentationPrefabRegistry");
        public static readonly ServiceKey<MeshAssetRegistry> PresentationMeshAssetRegistry = new("PresentationMeshAssetRegistry");
        public static readonly ServiceKey<PrimitiveDrawBuffer> PresentationPrimitiveDrawBuffer = new("PresentationPrimitiveDrawBuffer");
        public static readonly ServiceKey<WorldHudBatchBuffer> PresentationWorldHudBuffer = new("PresentationWorldHudBuffer");
        public static readonly ServiceKey<WorldHudStringTable> PresentationWorldHudStrings = new("PresentationWorldHudStrings");
        public static readonly ServiceKey<ScreenHudBatchBuffer> PresentationScreenHudBuffer = new("PresentationScreenHudBuffer");
        public static readonly ServiceKey<ScreenOverlayBuffer> ScreenOverlayBuffer = new("ScreenOverlayBuffer");
        public static readonly ServiceKey<RenderDebugState> RenderDebugState = new("RenderDebugState");
        public static readonly ServiceKey<RenderCameraDebugState> RenderCameraDebugState = new("RenderCameraDebugState");
        public static readonly ServiceKey<CameraCullingDebugState> CameraCullingDebugState = new("CameraCullingDebugState");
        public static readonly ServiceKey<PresentationFrameSetupSystem> PresentationFrameSetup = new("PresentationFrameSetup");
        public static readonly ServiceKey<TransientMarkerBuffer> TransientMarkerBuffer = new("TransientMarkerBuffer");
        public static readonly ServiceKey<GasPresentationEventBuffer> GasPresentationEventBuffer = new("GasPresentationEventBuffer");
        public static readonly ServiceKey<GroundOverlayBuffer> GroundOverlayBuffer = new("GroundOverlayBuffer");
        public static readonly ServiceKey<DebugDrawCommandBuffer> DebugDrawCommandBuffer = new("DebugDrawCommandBuffer");

        // --- Performers ---
        public static readonly ServiceKey<PerformerDefinitionRegistry> PerformerDefinitionRegistry = new("PerformerDefinitionRegistry");
        public static readonly ServiceKey<PerformerInstanceBuffer> PerformerInstanceBuffer = new("PerformerInstanceBuffer");

        // --- Spatial ---
        public static readonly ServiceKey<WorldSizeSpec> WorldSizeSpec = new("WorldSizeSpec");
        public static readonly ServiceKey<ISpatialCoordinateConverter> SpatialCoordinateConverter = new("SpatialCoordinateConverter");
        public static readonly ServiceKey<ISpatialQueryService> SpatialQueryService = new("SpatialQueryService");
        public static readonly ServiceKey<HexMetrics> HexMetrics = new("HexMetrics");
        public static readonly ServiceKey<ILoadedChunks> LoadedChunks = new("LoadedChunks");

        // --- Navigation ---
        public static readonly ServiceKey<Navigation2DRuntime> Navigation2DRuntime = new("Navigation2DRuntime");
        public static readonly ServiceKey<NavMeshBakeConfig> NavMeshBakeConfig = new("NavMeshBakeConfig");
        public static readonly ServiceKey<NavMeshProfileRegistry> NavMeshProfiles = new("NavMeshProfiles");
        public static readonly ServiceKey<NavQueryServiceRegistry> NavQueryServices = new("NavQueryServices");

        // --- Entity Selection (presentation-layer) ---
        public static readonly ServiceKey<Entity> LocalPlayerEntity = new("LocalPlayerEntity");
        public static readonly ServiceKey<Entity> SelectedEntity = new("SelectedEntity");
        public static readonly ServiceKey<Entity> HoveredEntity = new("HoveredEntity");

        // --- Config & AI ---
        public static readonly ServiceKey<ConfigCatalog> ConfigCatalog = new("ConfigCatalog");
        public static readonly ServiceKey<ConfigConflictReport> ConfigConflictReport = new("ConfigConflictReport");
        public static readonly ServiceKey<RegistrationConflictReport> RegistrationConflictReport = new("RegistrationConflictReport");
        public static readonly ServiceKey<AiCompiledRuntime> AiRuntime = new("AiRuntime");

        // --- Diagnostics ---
        public static readonly ServiceKey<ILogBackend> LogBackend = new("LogBackend");
    }
}



