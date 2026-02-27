namespace Ludots.Core.Scripting
{
    public static class ContextKeys
    {
        public const string World = "World";
        public const string WorldMap = "WorldMap";
        public const string VertexMap = "VertexMap";
        public const string GameSession = "GameSession";
        public const string Engine = "Engine";
        public const string MapId = "MapId";
        public const string MapTags = "MapTags";
        public const string UISystem = "UISystem";
        public const string UIRoot = "UIRoot";
        public const string UiCaptured = "UiCaptured";
        public const string InputHandler = "InputHandler";
        public const string InputBackend = "InputBackend";
        public const string ScreenProjector = "ScreenProjector";
        public const string ScreenRayProvider = "ScreenRayProvider";
        public const string DebugDrawCommandBuffer = "DebugDrawCommandBuffer";
        public const string ExtensionAttributeRegistry = "ExtensionAttributeRegistry";
        public const string AttributeSchemaUpdateQueue = "AttributeSchemaUpdateQueue";
        public const string DeferredTriggerQueue = "DeferredTriggerQueue";
        public const string GasBudget = "GasBudget";
        public const string EffectTemplateRegistry = "EffectTemplateRegistry";
        public const string GraphProgramRegistry = "GraphProgramRegistry";
        public const string EffectRequestQueue = "EffectRequestQueue";
        public const string Clock = "Clock";
        public const string GasClockStepPolicy = "GasClockStepPolicy";
        public const string GasClocks = "GasClocks";
        public const string Physics2DTickPolicy = "Physics2DTickPolicy";
        public const string Physics2DController = "Physics2DController";
        public const string SimulationLoopController = "SimulationLoopController";
        public const string GasController = "GasController";
        public const string GasConditionRegistry = "GasConditionRegistry";
        public const string TagOps = "TagOps";
        public const string InputRequestQueue = "InputRequestQueue";
        public const string InputResponseBuffer = "InputResponseBuffer";
        public const string SelectionRequestQueue = "SelectionRequestQueue";
        public const string SelectionResponseBuffer = "SelectionResponseBuffer";
        public const string OrderQueue = "OrderQueue";
        public const string OrderTypeRegistry = "OrderTypeRegistry";
        public const string OrderBufferSystem = "OrderBufferSystem";
        public const string OrderRequestQueue = "OrderRequestQueue";
        public const string ResponseChainTelemetryBuffer = "ResponseChainTelemetryBuffer";
        public const string ChainOrderQueue = "ChainOrderQueue";
        public const string ResponseChainUiState = "ResponseChainUiState";
        public const string AbilityDefinitionRegistry = "AbilityDefinitionRegistry";
        public const string AttributeSinkRegistry = "AttributeSinkRegistry";
        public const string AttributeBindingRegistry = "AttributeBindingRegistry";
        public const string PresentationEventStream = "PresentationEventStream";
        public const string PresentationCommandBuffer = "PresentationCommandBuffer";
        public const string PresentationPrefabRegistry = "PresentationPrefabRegistry";
        public const string PresentationMeshAssetRegistry = "PresentationMeshAssetRegistry";
        public const string PresentationPrimitiveDrawBuffer = "PresentationPrimitiveDrawBuffer";
        public const string PresentationWorldHudBuffer = "PresentationWorldHudBuffer";
        public const string PresentationWorldHudStrings = "PresentationWorldHudStrings";
        public const string WorldSizeSpec = "WorldSizeSpec";
        public const string SpatialCoordinateConverter = "SpatialCoordinateConverter";
        public const string SpatialQueryService = "SpatialQueryService";
        public const string HexMetrics = "HexMetrics";
        public const string MapSession = "MapSession";
        public const string LoadedChunks = "LoadedChunks";
        public const string RegistrationConflictReport = "RegistrationConflictReport";
        public const string ConfigConflictReport = "ConfigConflictReport";
        public const string ConfigCatalog = "ConfigCatalog";
        public const string AiRuntime = "AiRuntime";
        public const string MapFeatureFlags = "MapFeatureFlags";
        public const string CameraControllerRequest = "CameraControllerRequest";
        public const string CameraControllerRegistry = "CameraControllerRegistry";
        public const string LocalPlayerEntity = "LocalPlayerEntity";
        public const string SelectedEntity = "SelectedEntity";
        public const string AbilityInputRequestQueue = "AbilityInputRequestQueue";
        public const string GameConfig = "GameConfig";
        public const string PresentationFrameSetup = "PresentationFrameSetup";
        public const string TransientMarkerBuffer = "TransientMarkerBuffer";
        // WorldHudConfig removed — unified into Performer entity-scoped definitions
        public const string GasPresentationEventBuffer = "GasPresentationEventBuffer";
        public const string GroundOverlayBuffer = "GroundOverlayBuffer";
        // IndicatorRequestBuffer removed — unified into Performer direct API
        public const string PerformerDefinitionRegistry = "PerformerDefinitionRegistry";
        public const string PerformerInstanceBuffer = "PerformerInstanceBuffer";
        public const string Navigation2DRuntime = "Navigation2DRuntime";
        public const string Navigation2DTickPolicy = "Navigation2DTickPolicy";
        public const string NavMeshBakeConfig = "NavMeshBakeConfig";
        public const string NavMeshProfiles = "NavMeshProfiles";
        public const string NavQueryServices = "NavQueryServices";

        public const string LogBackend = "LogBackend";

        public const string MapSessions = "MapSessions";
        public const string BoardIdRegistry = "BoardIdRegistry";
        public const string MapContext = "MapContext";

        public const string SystemFactoryRegistry = "SystemFactoryRegistry";
        public const string TriggerDecoratorRegistry = "TriggerDecoratorRegistry";

        public const string Navigation2DPlayground_AgentDeltaPerTeam = "Navigation2DPlayground_AgentDeltaPerTeam";
        public const string Navigation2DPlayground_ResetScenario = "Navigation2DPlayground_ResetScenario";
        public const string Navigation2DPlayground_AgentsPerTeam = "Navigation2DPlayground_AgentsPerTeam";
        public const string Navigation2DPlayground_LiveAgentsTotal = "Navigation2DPlayground_LiveAgentsTotal";
        public const string Navigation2DPlayground_FlowDebugLines = "Navigation2DPlayground_FlowDebugLines";
    }
}
