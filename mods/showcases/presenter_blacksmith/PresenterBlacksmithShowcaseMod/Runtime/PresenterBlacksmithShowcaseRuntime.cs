using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Knowledge;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Platform.Abstractions;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using Ludots.UI;
using PresenterBlacksmithShowcaseMod.UI;
using Ludots.Platform.Abstractions;

namespace PresenterBlacksmithShowcaseMod.Runtime
{
    internal sealed class PresenterBlacksmithShowcaseRuntime : IBenchmarkSceneController
    {
        private const float RootSearchRadiusCm = 50f;
        private const int ScatterMinTotal = 1;
        private const int ScatterUiHardMaxTotal = 300_000;
        private const int BenchmarkSampleFrames = 60;
        private const int DetailedPanelCrowdThreshold = 2048;
        private const int PresenterCountPerBlacksmith = 9;
        private const int BenchmarkHudTextPresenterCountPerBlacksmith = 3;
        private const int BenchmarkHudTextPrimitiveCountPerBlacksmith = 1;
        private const int BenchmarkHudTextWorldHudCountPerBlacksmith = 2;
        private const int BenchmarkHudTextScreenHudCountPerBlacksmith = 2;
        private const int PrimitiveCountPerBlacksmith = 3;
        private const int WorldHudCountPerBlacksmith = 2;
        private const int ScreenHudCountPerBlacksmith = 2;
        private const int SplineRibbonCountPerBlacksmith = 1;
        private const int GroundOverlayCountPerBlacksmith = 1;
        private const int SkinnedCountPerBlacksmith = 1;
        private const int LiveKnowledgeConfidencePermille = 1000;
        private const string AutoScatterTotalEnvKey = "LUDOTS_BLACKSMITH_AUTO_SCATTER_TOTAL";
        private const string AutoMeshBenchmarkTotalEnvKey = "LUDOTS_BLACKSMITH_MESH_BENCHMARK_TOTAL";
        private const string AutoDynamicWorkerBenchmarkTotalEnvKey = "LUDOTS_BLACKSMITH_DYNAMIC_WORKER_BENCHMARK_TOTAL";
        private const string MetadataSectionKey = "presenterBlacksmith";
        private const string ScatterInitialTargetMetadataKey = "scatterInitialTarget";
        private const string ScatterSeedMetadataKey = "scatterSeed";
        private const string ScatterMinRadiusMetadataKey = "scatterMinRadiusCm";
        private const string ScatterMaxRadiusMetadataKey = "scatterMaxRadiusCm";
        private const string ScatterJitterMetadataKey = "scatterJitterCm";
        private const string MeshBenchmarkTotalMetadataKey = "meshBenchmarkTotal";
        private const string MeshBenchmarkScatterSeedMetadataKey = "meshBenchmarkScatterSeed";
        private const string MeshBenchmarkScatterMinRadiusMetadataKey = "meshBenchmarkScatterMinRadiusCm";
        private const string MeshBenchmarkScatterMaxRadiusMetadataKey = "meshBenchmarkScatterMaxRadiusCm";
        private const string MeshBenchmarkScatterJitterMetadataKey = "meshBenchmarkScatterJitterCm";
        private const string DynamicWorkerBenchmarkTotalMetadataKey = "dynamicWorkerBenchmarkTotal";
        private const string DynamicWorkerScatterSeedMetadataKey = "dynamicWorkerScatterSeed";
        private const string DynamicWorkerScatterJitterMetadataKey = "dynamicWorkerScatterJitterCm";
        private const string DynamicWorkerScatterPaddingMetadataKey = "dynamicWorkerScatterPaddingCm";
        private const string MinimapMarkerShowcaseTotalMetadataKey = "minimapMarkerShowcaseTotal";
        private const string MinimapMarkerScatterJitterMetadataKey = "minimapMarkerScatterJitterCm";
        private const string MinimapMarkerScatterPaddingMetadataKey = "minimapMarkerScatterPaddingCm";
        private const string MinimapMarkerVisibleClusterCountMetadataKey = "minimapMarkerVisibleClusterCount";
        private const string MinimapMarkerVisibleClusterCenterXMetadataKey = "minimapMarkerVisibleClusterCenterXCm";
        private const string MinimapMarkerVisibleClusterCenterYMetadataKey = "minimapMarkerVisibleClusterCenterYCm";
        private const string MinimapMarkerVisibleClusterRadiusMetadataKey = "minimapMarkerVisibleClusterRadiusCm";
        private const string MinimapMarkerScatterSeedMetadataKey = "minimapMarkerScatterSeed";
        private const string ForcePanelEnvKey = "LUDOTS_BLACKSMITH_FORCE_PANEL";
        private const string ForceBenchmarkUiEnvKey = "LUDOTS_BLACKSMITH_FORCE_BENCHMARK_UI";
        private const string AutoWorkingEnvKey = "LUDOTS_BLACKSMITH_AUTO_WORKING";
        private const float PanelRefreshIntervalSeconds = 0.25f;
        private const float LargeCrowdPanelRefreshIntervalSeconds = 1.5f;

        private readonly PresenterBlacksmithShowcasePanelController _panelController;
        private static readonly QueryDescription KnowledgeTargetQuery = new QueryDescription()
            .WithAll<Name, MapEntity, AttributeBuffer>()
            .WithNone<PresentationDestroyPending>();

        private GameEngine? _activeEngine;

        private int _workingTagId;
        private int _durabilityAttributeId;
        private int _durabilityIntactEffectId;
        private int _durabilityDamagedEffectId;
        private int _durabilityRuinedEffectId;
        private Entity _buildingEntity = Entity.Null;
        private bool _isWorking;
        private bool _isNight;
        private int _regionIndex;
        private bool _destroyed;
        private float _changeFlashTimer;
        private string _lastChangedField = string.Empty;
        private int _scatterRequestedTotal = 1;
        private int _scatterTargetTotal = ScatterMinTotal;
        private int _lastScatterSeed;
        private int _lastQueuedScatterExtras;
        private bool _autoScatterApplied;
        private bool _autoMeshBenchmarkApplied;
        private bool _autoDynamicWorkerBenchmarkApplied;
        private bool _autoMinimapMarkerShowcaseApplied;
        private bool _autoWorkingApplied;
        private float _panelRefreshCooldown;
        private bool _panelDirty = true;
        private PresenterBlacksmithShowcasePanelState _cachedPanelState = PresenterBlacksmithShowcasePanelState.Empty;
        private static readonly string[] RegionNames = { "NORTH", "SOUTH" };

        public bool IsActive => _activeEngine != null && PresenterBlacksmithShowcaseIds.IsShowcaseMap(_activeEngine.CurrentMapSession?.MapId.Value);

        public bool SupportsScatterControl => _activeEngine != null && SupportsBlacksmithScatter(_activeEngine);

        public bool IsCleanPerformanceScene => _activeEngine != null && ShouldUseCleanPerformanceScene(_activeEngine);

        public bool SuppressHostDiagnosticUi => _activeEngine != null &&
            PresenterBlacksmithShowcaseIds.IsShowcaseMap(_activeEngine.CurrentMapSession?.MapId.Value) &&
            !IsInteractiveMode(_activeEngine) &&
            !ReadStrictBoolEnv(ForceBenchmarkUiEnvKey);

        public bool SuppressHostDebugGuides => _activeEngine != null &&
            PresenterBlacksmithShowcaseIds.IsShowcaseMap(_activeEngine.CurrentMapSession?.MapId.Value) &&
            !PresenterBlacksmithShowcaseIds.IsMinimapMarkerLargeWorldShowcaseMap(_activeEngine.CurrentMapSession?.MapId.Value);

        public int ScatterMin => ScatterMinTotal;

        public int ScatterMax => _activeEngine != null ? ResolveScatterUiMax(_activeEngine) : ScatterUiHardMaxTotal;

        public int ScatterTarget => _scatterTargetTotal;

        public int ScatterAppliedTotal => _activeEngine != null ? CountTrackedBlacksmithEntities(_activeEngine, out _) : 0;

        public PresenterBlacksmithShowcaseRuntime()
        {
            _panelController = new PresenterBlacksmithShowcasePanelController(this);
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            if (!PresenterBlacksmithShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
            {
                Disable(engine);
                return Task.CompletedTask;
            }

            _workingTagId = TagRegistry.Register("working");
            _durabilityAttributeId = AttributeRegistry.Register("Durability");
            _durabilityIntactEffectId = EffectTemplateIdRegistry.GetId(PresenterBlacksmithShowcaseIds.EffectSetDurabilityIntact);
            _durabilityDamagedEffectId = EffectTemplateIdRegistry.GetId(PresenterBlacksmithShowcaseIds.EffectSetDurabilityDamaged);
            _durabilityRuinedEffectId = EffectTemplateIdRegistry.GetId(PresenterBlacksmithShowcaseIds.EffectSetDurabilityRuined);
            _activeEngine = engine;
            ResetControlState(engine);
            _buildingEntity = IsInteractiveMode(engine)
                ? FindRootBuildingEntity(engine)
                : Entity.Null;
            _autoScatterApplied =
                (IsScatterBenchmarkMode(engine) && CountMeshBenchmarkEntities(engine) > 0) ||
                (IsScatterHudBarBenchmarkMode(engine) && CountMeshHudBarBenchmarkEntities(engine) > 0) ||
                (IsScatterHudTextBenchmarkMode(engine) && CountMeshHudTextBenchmarkEntities(engine) > 0);
            _autoMeshBenchmarkApplied = IsMeshBenchmarkMode(engine) && CountMeshBenchmarkEntities(engine) > 0;
            _autoDynamicWorkerBenchmarkApplied = IsDynamicWorkerBenchmarkMode(engine) && CountDynamicWorkerEntities(engine) > 0;
            _autoMinimapMarkerShowcaseApplied = IsMinimapMarkerShowcaseMode(engine) && CountMinimapMarkerBallEntities(engine) > 0;
            TryApplyStartupBenchmarkLayout(engine);
            TryApplyAutoWorkingState(engine);
            EnsureShowcaseKnowledgeProjection(engine);
            MarkPanelDirty();
            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            if (context.GetEngine() is GameEngine engine)
            {
                Disable(engine);
            }

            _activeEngine = null;
            _buildingEntity = Entity.Null;
            _destroyed = false;
            _autoScatterApplied = false;
            _autoMeshBenchmarkApplied = false;
            _autoDynamicWorkerBenchmarkApplied = false;
            _autoMinimapMarkerShowcaseApplied = false;
            _autoWorkingApplied = false;
            _panelDirty = true;
            _panelRefreshCooldown = 0f;
            _cachedPanelState = PresenterBlacksmithShowcasePanelState.Empty;
            return Task.CompletedTask;
        }

        public void Update(GameEngine engine)
        {
            if (!PresenterBlacksmithShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
            {
                Disable(engine);
                return;
            }

            _activeEngine = engine;
            if (IsInteractiveMode(engine))
            {
                RefreshRootEntity(engine);
                TryApplyAutoWorkingState(engine);
            }

            _panelRefreshCooldown = MathF.Max(0f, _panelRefreshCooldown - (1f / 60f));
            if (_changeFlashTimer > 0f)
            {
                _changeFlashTimer = MathF.Max(0f, _changeFlashTimer - 0.016f);
                if (_changeFlashTimer <= 0f)
                {
                    MarkPanelDirty();
                }
            }

            RefreshPanel(engine);
        }

        internal void UpdateKnowledgeProjection(GameEngine engine)
        {
            if (!PresenterBlacksmithShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
            {
                return;
            }

            _activeEngine = engine;
            EnsureShowcaseKnowledgeProjection(engine);
        }

        internal void ToggleWorking()
        {
            GameEngine engine = RequireShowcaseEngine();
            if (_destroyed || _buildingEntity == Entity.Null || !engine.World.IsAlive(_buildingEntity))
            {
                return;
            }

            SetWorkingState(engine, !_isWorking, $"Working => {(!_isWorking ? "ON" : "OFF")}");
        }

        private void TryApplyAutoWorkingState(GameEngine engine)
        {
            if (_autoWorkingApplied || !ReadStrictBoolEnv(AutoWorkingEnvKey))
            {
                return;
            }

            if (_destroyed || _buildingEntity == Entity.Null || !engine.World.IsAlive(_buildingEntity))
            {
                return;
            }

            SetWorkingState(engine, enabled: true, "Auto working => ON");
            _autoWorkingApplied = true;
        }

        private void SetWorkingState(GameEngine engine, bool enabled, string flashLabel)
        {
            if (_isWorking == enabled)
            {
                _autoWorkingApplied = _autoWorkingApplied || enabled;
                return;
            }

            _isWorking = enabled;
            EnsureGameplayTagState(engine, _buildingEntity);
            TagOps tagOps = engine.GetService(CoreServiceKeys.TagOps)
                ?? throw new InvalidOperationException("TagOps service missing.");
            if (_isWorking)
            {
                tagOps.AddTag(engine.World, _buildingEntity, _workingTagId);
            }
            else
            {
                tagOps.RemoveTag(engine.World, _buildingEntity, _workingTagId);
            }

            Flash(flashLabel);
        }

        internal void ToggleDayNight()
        {
            GameEngine engine = RequireShowcaseEngine();
            if (_destroyed)
            {
                return;
            }

            _isNight = !_isNight;
            engine.GetService(CoreServiceKeys.GlobalPresentationEventBuffer)
                ?.AddDayNight(PresenterBlacksmithShowcaseIds.ParamDayNight, _isNight ? 1f : 0f);
            Flash($"Day/Night => {(_isNight ? "NIGHT" : "DAY")}");
        }

        internal void CycleRegion()
        {
            GameEngine engine = RequireShowcaseEngine();
            if (_destroyed)
            {
                return;
            }

            int previous = _regionIndex;
            _regionIndex = (_regionIndex + 1) % RegionNames.Length;
            engine.GetService(CoreServiceKeys.GlobalPresentationEventBuffer)
                ?.AddRegionChanged(_regionIndex, previous);
            Flash($"Region => {RegionNames[_regionIndex]}");
        }

        internal void SetDurabilityPreset(int preset)
        {
            GameEngine engine = RequireShowcaseEngine();
            if (_destroyed || _buildingEntity == Entity.Null || !engine.World.IsAlive(_buildingEntity))
            {
                return;
            }

            int effectTemplateId = preset switch
            {
                0 => _durabilityIntactEffectId,
                1 => _durabilityDamagedEffectId,
                _ => _durabilityRuinedEffectId,
            };

            if (effectTemplateId <= 0)
            {
                throw new InvalidOperationException("Blacksmith durability control effects are not registered.");
            }

            if (engine.GetService(CoreServiceKeys.EffectRequestQueue) is not EffectRequestQueue requests)
            {
                throw new InvalidOperationException("EffectRequestQueue service missing.");
            }

            requests.Publish(new EffectRequest
            {
                Source = _buildingEntity,
                Target = _buildingEntity,
                TargetContext = _buildingEntity,
                TemplateId = effectTemplateId,
            });

            Flash($"Durability request => {ResolveDurabilityLabel(preset)}");
        }

        internal void DestroyBuilding()
        {
            GameEngine engine = RequireShowcaseEngine();
            if (_destroyed || _buildingEntity == Entity.Null || !engine.World.IsAlive(_buildingEntity))
            {
                return;
            }

            PresentationEntityLifecycle.RequestDestroy(engine.World, _buildingEntity, "Blacksmith showcase root");
            _buildingEntity = Entity.Null;
            _destroyed = true;
            _isWorking = false;
            Flash("Root entity destroyed");
        }

        internal void RespawnBuilding()
        {
            QueueRootRespawn(RequireShowcaseEngine());
        }

        public void ApplyScatterLayout(int totalBuildings)
        {
            GameEngine engine = RequireShowcaseEngine();
            if (!SupportsBlacksmithScatter(engine))
            {
                Flash("Scatter unavailable on this benchmark map");
                return;
            }

            int clampedTotal = ClampScatterTotal(engine, totalBuildings);

            ClearScatterBuildings(engine);
            _scatterRequestedTotal = clampedTotal;
            _scatterTargetTotal = clampedTotal;
            _lastQueuedScatterExtras = 0;

            bool cleanHudTextScatter = UsesCleanHudTextScatter(engine);
            bool meshOnlyScatter = IsScatterBenchmarkMode(engine);
            bool meshHudBarScatter = IsScatterHudBarBenchmarkMode(engine);
            bool meshHudTextScatter = IsScatterHudTextBenchmarkMode(engine) || cleanHudTextScatter;
            if (_destroyed && !meshOnlyScatter && !meshHudBarScatter && !meshHudTextScatter)
            {
                QueueRootRespawn(engine);
                _scatterRequestedTotal = clampedTotal;
            }

            if (clampedTotal <= 1)
            {
                Flash("Scatter reset => root only");
                return;
            }

            if (engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) is not RuntimeEntitySpawnQueue spawnQueue)
            {
                Flash("Scatter unavailable: RuntimeEntitySpawnQueue missing");
                return;
            }

            BlacksmithScatterConfig scatter = ReadScatterConfig(engine);
            _lastScatterSeed = scatter.Seed;
            string templateId = ResolveScatterTemplateId(engine);
            int requestedEntityCount = cleanHudTextScatter || meshOnlyScatter || meshHudBarScatter || meshHudTextScatter
                ? clampedTotal
                : clampedTotal - 1;
            int queued = PresenterBlacksmithScatterPlanner.EnqueueTemplateScatter(
                spawnQueue,
                engine.CurrentMapSession?.MapId ?? default,
                templateId,
                requestedEntityCount,
                scatter.Seed,
                scatter.MinRadiusCm,
                scatter.MaxRadiusCm,
                scatter.JitterCm);
            _lastQueuedScatterExtras = queued;
            Flash($"Scatter total => {clampedTotal} (seed {scatter.Seed}, queued {queued})");
        }

        internal void AdjustScatterTarget(int delta)
        {
            GameEngine engine = RequireShowcaseEngine();
            _scatterTargetTotal = ClampScatterTotal(engine, _scatterTargetTotal + delta);
            Flash($"Scatter target => {_scatterTargetTotal}");
        }

        public void SetScatterTargetFromRatio(float ratio)
        {
            GameEngine engine = RequireShowcaseEngine();
            float clampedRatio = Math.Clamp(ratio, 0f, 1f);
            int scatterUiMax = ResolveScatterUiMax(engine);
            int value = ScatterMinTotal + (int)MathF.Round(clampedRatio * (scatterUiMax - ScatterMinTotal));
            _scatterTargetTotal = ClampScatterTotal(engine, value);
            Flash($"Scatter target => {_scatterTargetTotal}");
        }

        public void ApplyScatterTarget()
        {
            ApplyScatterLayout(_scatterTargetTotal);
        }

        private GameEngine RequireShowcaseEngine()
        {
            if (_activeEngine != null &&
                PresenterBlacksmithShowcaseIds.IsShowcaseMap(_activeEngine.CurrentMapSession?.MapId.Value))
            {
                return _activeEngine;
            }

            throw new InvalidOperationException("Blacksmith showcase actions require the showcase map to be active.");
        }

        private static bool IsInteractiveMode(GameEngine engine)
        {
            return PresenterBlacksmithShowcaseIds.IsInteractiveShowcaseMap(engine.CurrentMapSession?.MapId.Value);
        }

        private static bool IsScatterBenchmarkMode(GameEngine engine)
        {
            return PresenterBlacksmithShowcaseIds.IsScatterBenchmarkMap(engine.CurrentMapSession?.MapId.Value);
        }

        private static bool IsScatterHudBarBenchmarkMode(GameEngine engine)
        {
            return PresenterBlacksmithShowcaseIds.IsScatterHudBarBenchmarkMap(engine.CurrentMapSession?.MapId.Value);
        }

        private static bool IsScatterHudTextBenchmarkMode(GameEngine engine)
        {
            return PresenterBlacksmithShowcaseIds.IsScatterHudTextBenchmarkMap(engine.CurrentMapSession?.MapId.Value);
        }

        private static bool IsMeshBenchmarkMode(GameEngine engine)
        {
            return PresenterBlacksmithShowcaseIds.IsMeshBenchmarkMap(engine.CurrentMapSession?.MapId.Value);
        }

        private static bool IsDynamicWorkerBenchmarkMode(GameEngine engine)
        {
            return PresenterBlacksmithShowcaseIds.IsDynamicWorkerBenchmarkMap(engine.CurrentMapSession?.MapId.Value);
        }

        private static bool IsMinimapMarkerShowcaseMode(GameEngine engine)
        {
            return PresenterBlacksmithShowcaseIds.IsMinimapMarkerLargeWorldShowcaseMap(engine.CurrentMapSession?.MapId.Value);
        }

        private static bool IsBenchmarkMode(GameEngine engine)
        {
            return IsMeshBenchmarkMode(engine) ||
                   IsDynamicWorkerBenchmarkMode(engine) ||
                   IsMinimapMarkerShowcaseMode(engine) ||
                   IsScatterBenchmarkMode(engine) ||
                   IsScatterHudBarBenchmarkMode(engine) ||
                   IsScatterHudTextBenchmarkMode(engine);
        }

        private static bool UsesCleanHudTextScatter(GameEngine engine)
        {
            return IsScatterHudTextBenchmarkMode(engine);
        }

        private static bool ShouldUseCleanPerformanceScene(GameEngine engine)
        {
            return IsMeshBenchmarkMode(engine) ||
                   IsDynamicWorkerBenchmarkMode(engine) ||
                   IsScatterBenchmarkMode(engine) ||
                   IsScatterHudBarBenchmarkMode(engine) ||
                   IsScatterHudTextBenchmarkMode(engine) ||
                   (IsInteractiveMode(engine) && CountMeshHudTextBenchmarkEntities(engine) > 0);
        }

        private static string ResolveScatterTemplateId(GameEngine engine)
        {
            if (IsScatterBenchmarkMode(engine))
            {
                return PresenterBlacksmithShowcaseIds.MeshBenchmarkTemplateId;
            }

            if (IsScatterHudBarBenchmarkMode(engine))
            {
                return PresenterBlacksmithShowcaseIds.MeshHudBarBenchmarkTemplateId;
            }

            if (IsScatterHudTextBenchmarkMode(engine) || UsesCleanHudTextScatter(engine))
            {
                return PresenterBlacksmithShowcaseIds.MeshHudTextBenchmarkTemplateId;
            }

            return PresenterBlacksmithShowcaseIds.TemplateId;
        }

        private static bool SupportsBlacksmithScatter(GameEngine engine)
        {
            return IsInteractiveMode(engine) ||
                   IsScatterBenchmarkMode(engine) ||
                   IsScatterHudBarBenchmarkMode(engine) ||
                   IsScatterHudTextBenchmarkMode(engine);
        }

        private void RefreshRootEntity(GameEngine engine)
        {
            if (_buildingEntity != Entity.Null && engine.World.IsAlive(_buildingEntity))
            {
                return;
            }

            Entity previousBuilding = _buildingEntity;
            bool previousDestroyed = _destroyed;
            _buildingEntity = FindRootBuildingEntity(engine);
            _destroyed = _buildingEntity == Entity.Null;
            if (_buildingEntity != previousBuilding || _destroyed != previousDestroyed)
            {
                MarkPanelDirty();
            }
        }

        private void TryApplyStartupBenchmarkLayout(GameEngine engine)
        {
            if (IsMeshBenchmarkMode(engine))
            {
                TryApplyMeshBenchmark(engine);
                return;
            }

            if (IsDynamicWorkerBenchmarkMode(engine))
            {
                TryApplyDynamicWorkerBenchmark(engine);
                return;
            }

            if (IsMinimapMarkerShowcaseMode(engine))
            {
                TryApplyMinimapMarkerShowcase(engine);
                return;
            }

            if (IsInteractiveMode(engine))
            {
                TryApplyAutoScatter(engine);
                return;
            }

            if (IsScatterBenchmarkMode(engine))
            {
                TryApplyAutoScatter(engine);
                return;
            }

            if (IsScatterHudBarBenchmarkMode(engine))
            {
                TryApplyAutoScatter(engine);
                return;
            }

            if (IsScatterHudTextBenchmarkMode(engine))
            {
                TryApplyAutoScatter(engine);
            }
        }

        private void TryApplyMeshBenchmark(GameEngine engine)
        {
            if (_autoMeshBenchmarkApplied)
            {
                return;
            }

            if (CountMeshBenchmarkEntities(engine) > 0)
            {
                _autoMeshBenchmarkApplied = true;
                return;
            }

            if (engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) is not RuntimeEntitySpawnQueue spawnQueue)
            {
                Flash("Mesh benchmark unavailable: RuntimeEntitySpawnQueue missing");
                return;
            }

            int configured = ReadRequiredMapMetadataInt(engine, MetadataSectionKey, MeshBenchmarkTotalMetadataKey);
            int requested = ReadPositiveIntEnvOverride(AutoMeshBenchmarkTotalEnvKey, configured);
            int total = Math.Clamp(requested, ScatterMinTotal, ScatterUiHardMaxTotal);
            int enqueued = EnqueueMeshBenchmark(spawnQueue, engine, total);
            _scatterRequestedTotal = total;
            _scatterTargetTotal = total;
            _lastQueuedScatterExtras = enqueued;
            _autoMeshBenchmarkApplied = true;
            Flash($"Mesh benchmark total => {enqueued}");
        }

        private void TryApplyDynamicWorkerBenchmark(GameEngine engine)
        {
            if (_autoDynamicWorkerBenchmarkApplied)
            {
                return;
            }

            if (CountDynamicWorkerEntities(engine) > 0)
            {
                _autoDynamicWorkerBenchmarkApplied = true;
                return;
            }

            if (engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) is not RuntimeEntitySpawnQueue spawnQueue)
            {
                Flash("Dynamic worker benchmark unavailable: RuntimeEntitySpawnQueue missing");
                return;
            }

            int requested = ResolveDynamicWorkerBenchmarkTotal(engine);
            if (requested <= 0)
            {
                _autoDynamicWorkerBenchmarkApplied = true;
                return;
            }

            int total = Math.Clamp(requested, ScatterMinTotal, ScatterUiHardMaxTotal);
            int enqueued = EnqueueDynamicWorkerBenchmark(spawnQueue, engine, total);
            _scatterRequestedTotal = total;
            _scatterTargetTotal = total;
            _lastQueuedScatterExtras = enqueued;
            _autoDynamicWorkerBenchmarkApplied = true;
            Flash($"Dynamic worker benchmark total => {enqueued}");
        }

        private static int ResolveDynamicWorkerBenchmarkTotal(GameEngine engine)
        {
            int metadataTotal = ReadRequiredMapMetadataInt(
                engine,
                MetadataSectionKey,
                DynamicWorkerBenchmarkTotalMetadataKey);
            return ReadPositiveIntEnvOverride(AutoDynamicWorkerBenchmarkTotalEnvKey, metadataTotal);
        }

        private static int EnqueueMeshBenchmark(RuntimeEntitySpawnQueue queue, GameEngine engine, int total)
        {
            int count = Math.Max(0, total);
            if (count == 0)
            {
                return 0;
            }

            int seed = ReadRequiredMapMetadataInt(engine, MetadataSectionKey, MeshBenchmarkScatterSeedMetadataKey);
            float minRadiusCm = ReadRequiredPositiveMapMetadataFloat(engine, MetadataSectionKey, MeshBenchmarkScatterMinRadiusMetadataKey);
            float maxRadiusCm = ReadRequiredPositiveMapMetadataFloat(engine, MetadataSectionKey, MeshBenchmarkScatterMaxRadiusMetadataKey);
            float jitterCm = ReadRequiredNonNegativeMapMetadataFloat(engine, MetadataSectionKey, MeshBenchmarkScatterJitterMetadataKey);
            if (maxRadiusCm <= minRadiusCm)
            {
                throw new InvalidOperationException(
                    $"metadata.{MetadataSectionKey}.{MeshBenchmarkScatterMaxRadiusMetadataKey} must be greater than {MeshBenchmarkScatterMinRadiusMetadataKey}.");
            }

            return PresenterBlacksmithScatterPlanner.EnqueueTemplateScatter(
                queue,
                engine.CurrentMapSession?.MapId ?? default,
                PresenterBlacksmithShowcaseIds.MeshBenchmarkTemplateId,
                count,
                seed,
                minRadiusCm,
                maxRadiusCm,
                jitterCm);
        }

        private static int EnqueueDynamicWorkerBenchmark(RuntimeEntitySpawnQueue queue, GameEngine engine, int total)
        {
            int count = Math.Max(0, total);
            if (count == 0)
            {
                return 0;
            }

            int seed = ReadRequiredMapMetadataInt(engine, MetadataSectionKey, DynamicWorkerScatterSeedMetadataKey);
            IVisualHeightmapRenderSource heightmap = RequireVisualHeightmapRenderSource(engine);
            float jitterCm = ReadRequiredNonNegativeMapMetadataFloat(
                engine,
                MetadataSectionKey,
                DynamicWorkerScatterJitterMetadataKey);
            float paddingCm = ReadRequiredNonNegativeMapMetadataFloat(
                engine,
                MetadataSectionKey,
                DynamicWorkerScatterPaddingMetadataKey);

            float leftCm = heightmap.Bounds.Left + paddingCm + jitterCm;
            float rightCm = heightmap.Bounds.Right - paddingCm - jitterCm;
            float topCm = heightmap.Bounds.Top + paddingCm + jitterCm;
            float bottomCm = heightmap.Bounds.Bottom - paddingCm - jitterCm;
            if (leftCm >= rightCm || topCm >= bottomCm)
            {
                throw new InvalidOperationException(
                    $"Map '{engine.CurrentMapSession?.MapId.Value ?? "<none>"}' dynamic worker scatter padding leaves no valid VisualHeightmap spawn area.");
            }

            return PresenterBlacksmithScatterPlanner.EnqueueTemplateAreaScatter(
                queue,
                engine.CurrentMapSession?.MapId ?? default,
                PresenterBlacksmithShowcaseIds.DynamicWorkerTemplateId,
                count,
                seed,
                leftCm,
                rightCm,
                topCm,
                bottomCm,
                jitterCm);
        }

        private void TryApplyMinimapMarkerShowcase(GameEngine engine)
        {
            if (_autoMinimapMarkerShowcaseApplied)
            {
                return;
            }

            if (CountMinimapMarkerBallEntities(engine) > 0)
            {
                _autoMinimapMarkerShowcaseApplied = true;
                return;
            }

            if (engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) is not RuntimeEntitySpawnQueue spawnQueue)
            {
                Flash("Minimap marker showcase unavailable: RuntimeEntitySpawnQueue missing");
                return;
            }

            int requested = ReadRequiredMapMetadataInt(engine, MetadataSectionKey, MinimapMarkerShowcaseTotalMetadataKey);
            int total = Math.Clamp(requested, ScatterMinTotal, ScatterUiHardMaxTotal);
            int enqueued = EnqueueMinimapMarkerShowcase(spawnQueue, engine, total);
            _scatterRequestedTotal = total;
            _scatterTargetTotal = total;
            _lastQueuedScatterExtras = enqueued;
            _autoMinimapMarkerShowcaseApplied = true;
            Flash($"Minimap marker balls => {enqueued}");
        }

        private static int EnqueueMinimapMarkerShowcase(RuntimeEntitySpawnQueue queue, GameEngine engine, int total)
        {
            int count = Math.Max(0, total);
            if (count == 0)
            {
                return 0;
            }

            float jitterCm = ReadRequiredNonNegativeMapMetadataFloat(engine, MetadataSectionKey, MinimapMarkerScatterJitterMetadataKey);
            float paddingCm = ReadRequiredNonNegativeMapMetadataFloat(engine, MetadataSectionKey, MinimapMarkerScatterPaddingMetadataKey);

            var bounds = engine.WorldSizeSpec.Bounds;
            float leftCm = bounds.Left + paddingCm + jitterCm;
            float rightCm = bounds.Right - paddingCm - jitterCm;
            float topCm = bounds.Top + paddingCm + jitterCm;
            float bottomCm = bounds.Bottom - paddingCm - jitterCm;
            if (leftCm >= rightCm || topCm >= bottomCm)
            {
                throw new InvalidOperationException(
                    $"Map '{engine.CurrentMapSession?.MapId.Value ?? "<none>"}' minimap marker scatter padding leaves no valid world spawn area.");
            }

            int seed = ReadRequiredMapMetadataInt(engine, MetadataSectionKey, MinimapMarkerScatterSeedMetadataKey);
            int clusterCount = Math.Clamp(
                ReadRequiredMapMetadataInt(engine, MetadataSectionKey, MinimapMarkerVisibleClusterCountMetadataKey),
                0,
                count);
            int queued = 0;
            if (clusterCount > 0)
            {
                float clusterCenterXCm = ReadRequiredMapMetadataFloat(engine, MetadataSectionKey, MinimapMarkerVisibleClusterCenterXMetadataKey);
                float clusterCenterYCm = ReadRequiredMapMetadataFloat(engine, MetadataSectionKey, MinimapMarkerVisibleClusterCenterYMetadataKey);
                float clusterRadiusCm = ReadRequiredPositiveMapMetadataFloat(engine, MetadataSectionKey, MinimapMarkerVisibleClusterRadiusMetadataKey);

                queued += PresenterBlacksmithScatterPlanner.EnqueueTemplateClusterScatter(
                    queue,
                    engine.CurrentMapSession?.MapId ?? default,
                    PresenterBlacksmithShowcaseIds.MinimapMarkerBallTemplateId,
                    clusterCount,
                    seed,
                    clusterCenterXCm,
                    clusterCenterYCm,
                    clusterRadiusCm,
                    MathF.Min(jitterCm, clusterRadiusCm * 0.04f));
            }

            int remaining = count - queued;
            if (remaining <= 0)
            {
                return queued;
            }

            queued += PresenterBlacksmithScatterPlanner.EnqueueTemplateAreaScatter(
                queue,
                engine.CurrentMapSession?.MapId ?? default,
                PresenterBlacksmithShowcaseIds.MinimapMarkerBallTemplateId,
                remaining,
                unchecked(seed ^ 0x5bd1e995),
                leftCm,
                rightCm,
                topCm,
                bottomCm,
                jitterCm);
            return queued;
        }

        private static IVisualHeightmapRenderSource RequireVisualHeightmapRenderSource(GameEngine engine)
        {
            IVisualHeightmap? heightmap = engine.GetService(CoreServiceKeys.VisualHeightmap);
            if (heightmap is IVisualHeightmapRenderSource renderSource)
            {
                return renderSource;
            }

            string mapId = engine.CurrentMapSession?.MapId.Value ?? "<none>";
            throw new InvalidOperationException(
                $"Map '{mapId}' must provide a VisualHeightmap render source for the dynamic worker benchmark production path.");
        }

        private Entity FindRootBuildingEntity(GameEngine engine)
        {
            Entity found = Entity.Null;
            float bestDistanceSq = float.MaxValue;
            float maxDistanceSq = RootSearchRadiusCm * RootSearchRadiusCm;
            var query = new QueryDescription().WithAll<Name, WorldPositionCm>();
            engine.World.Query(in query, (Entity entity, ref Name name, ref WorldPositionCm position) =>
            {
                if (!string.Equals(name.Value, PresenterBlacksmithShowcaseIds.EntityName, StringComparison.Ordinal))
                {
                    return;
                }

                Vector2 pos = position.Value.ToVector2();
                float distanceSq = (pos.X * pos.X) + (pos.Y * pos.Y);
                if (distanceSq > maxDistanceSq || distanceSq >= bestDistanceSq)
                {
                    return;
                }

                found = entity;
                bestDistanceSq = distanceSq;
            });
            return found;
        }

        private void EnsureShowcaseKnowledgeProjection(GameEngine engine)
        {
            if (_durabilityAttributeId <= 0)
            {
                throw new InvalidOperationException("Blacksmith showcase Durability attribute must be registered before publishing HUD knowledge.");
            }

            if (engine.CurrentMapSession == null)
            {
                throw new InvalidOperationException("Blacksmith showcase requires an active map session before publishing HUD knowledge.");
            }

            if (!KnowledgeProjectionConsumer.HasResolver(engine.GlobalContext))
            {
                throw new InvalidOperationException("Blacksmith showcase requires KnowledgeProjectionResolver before publishing HUD knowledge.");
            }

            KnowledgeProjectionStore knowledge = engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
                ?? throw new InvalidOperationException("Blacksmith showcase requires KnowledgeProjectionStore before publishing HUD knowledge.");
            Entity viewer = RequireShowcaseSolePossessedRep(engine);
            var durabilityMask = KnowledgeIdMask256.Empty.WithId(_durabilityAttributeId);
            int observedTick = KnowledgeProjectionConsumer.ResolveCurrentTick(engine.GlobalContext);
            var mapId = engine.CurrentMapSession.MapId;
            engine.World.Query(in KnowledgeTargetQuery, (Entity target, ref Name name, ref MapEntity mapEntity, ref AttributeBuffer attributes) =>
            {
                if (mapEntity.MapId != mapId ||
                    !IsKnowledgeTargetName(name.Value) ||
                    !attributes.HasAttribute(_durabilityAttributeId))
                {
                    return;
                }

                UpsertDurabilityKnowledgeIfNeeded(
                    knowledge,
                    viewer,
                    target,
                    in durabilityMask,
                    observedTick);
            });
        }

        private const int ShowcaseLocalPlayerId = 1;
        private Entity _showcaseViewerEntity;

        private Entity RequireShowcaseSolePossessedRep(GameEngine engine)
        {
            if (ClientLocalSeatAccess.TryGetSolePossessedRep(engine, out Entity possessed) &&
                engine.World.IsAlive(possessed))
            {
                return possessed;
            }

            if (_showcaseViewerEntity != Entity.Null && engine.World.IsAlive(_showcaseViewerEntity))
            {
                ClientLocalSeatBindings.BindSoleSeat(engine, _showcaseViewerEntity, ShowcaseLocalPlayerId);
                return _showcaseViewerEntity;
            }

            if (engine.CurrentMapSession == null)
            {
                throw new InvalidOperationException(
                    "Blacksmith showcase requires an active map session before creating a local viewer.");
            }

            _showcaseViewerEntity = engine.World.Create(
                new Name { Value = "Blacksmith Showcase Viewer" },
                new Ludots.Core.Gameplay.Components.PlayerIdentity { PlayerId = ShowcaseLocalPlayerId },
                new Ludots.Core.Gameplay.Components.PlayerOwner { PlayerId = ShowcaseLocalPlayerId },
                new Ludots.Core.Components.MapEntity { MapId = engine.CurrentMapSession.MapId });
            ClientLocalSeatBindings.BindSoleSeat(engine, _showcaseViewerEntity, ShowcaseLocalPlayerId);
            return _showcaseViewerEntity;
        }

        private static void UpsertDurabilityKnowledgeIfNeeded(
            KnowledgeProjectionStore knowledge,
            Entity viewer,
            Entity target,
            in KnowledgeIdMask256 durabilityMask,
            int observedTick)
        {
            var attributeMask = durabilityMask;
            var relationshipMask = KnowledgeIdMask256.Empty;
            var tagMask = KnowledgeIdMask256.Empty;
            if (knowledge.TryGet(viewer, target, observedTick, out KnowledgeDisclosureRecord existing))
            {
                if (existing.Presence == KnowledgePresence.LiveVisible &&
                    existing.Position == KnowledgePositionAccess.Live &&
                    existing.AttributeMask.ContainsAll(in durabilityMask))
                {
                    return;
                }

                attributeMask = existing.AttributeMask.Union(in durabilityMask);
                relationshipMask = existing.RelationshipTypeMask;
                tagMask = existing.TagMask;
            }

            var record = new KnowledgeDisclosureRecord(
                KnowledgePresence.LiveVisible,
                KnowledgePositionAccess.Live,
                in attributeMask,
                in relationshipMask,
                in tagMask,
                viewer,
                observedTick,
                expiryTick: 0,
                confidencePermille: LiveKnowledgeConfidencePermille,
                revision: 0);
            knowledge.Upsert(viewer, target, in record);
        }

        private static bool IsKnowledgeTargetName(string name)
        {
            return string.Equals(name, PresenterBlacksmithShowcaseIds.EntityName, StringComparison.Ordinal) ||
                   string.Equals(name, PresenterBlacksmithShowcaseIds.MeshHudBarBenchmarkEntityName, StringComparison.Ordinal) ||
                   string.Equals(name, PresenterBlacksmithShowcaseIds.MeshHudTextBenchmarkEntityName, StringComparison.Ordinal) ||
                   string.Equals(name, PresenterBlacksmithShowcaseIds.MinimapMarkerBallEntityName, StringComparison.Ordinal);
        }

        private void QueueRootRespawn(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) is not RuntimeEntitySpawnQueue spawnQueue)
            {
                Flash("Respawn unavailable: RuntimeEntitySpawnQueue missing");
                return;
            }

            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = PresenterBlacksmithShowcaseIds.TemplateId,
                MapId = engine.CurrentMapSession?.MapId ?? default,
                WorldPositionCm = default,
                HasFacing = 1,
                FacingAngleRad = 0f,
            };

            if (!spawnQueue.TryEnqueue(in request))
            {
                Flash("Respawn unavailable: spawn queue full");
                return;
            }

            ResetControlState(engine);
            _destroyed = false;
            Flash("Root respawn queued");
        }

        private void ClearScatterBuildings(GameEngine engine)
        {
            var toDestroy = new List<Entity>();
            bool meshOnlyScatter = IsScatterBenchmarkMode(engine) ||
                                   IsScatterHudBarBenchmarkMode(engine) ||
                                   IsScatterHudTextBenchmarkMode(engine) ||
                                   UsesCleanHudTextScatter(engine);
            var query = new QueryDescription().WithAll<Name, WorldPositionCm>();
            float rootDistanceSq = RootSearchRadiusCm * RootSearchRadiusCm;
            engine.World.Query(in query, (Entity entity, ref Name name, ref WorldPositionCm position) =>
            {
                bool isBlacksmith = string.Equals(name.Value, PresenterBlacksmithShowcaseIds.EntityName, StringComparison.Ordinal);
                bool isMeshBenchmark = string.Equals(name.Value, PresenterBlacksmithShowcaseIds.MeshBenchmarkEntityName, StringComparison.Ordinal);
                bool isMeshHudBarBenchmark = string.Equals(name.Value, PresenterBlacksmithShowcaseIds.MeshHudBarBenchmarkEntityName, StringComparison.Ordinal);
                bool isMeshHudTextBenchmark = string.Equals(name.Value, PresenterBlacksmithShowcaseIds.MeshHudTextBenchmarkEntityName, StringComparison.Ordinal);
                if (!isBlacksmith && !isMeshBenchmark && !isMeshHudBarBenchmark && !isMeshHudTextBenchmark)
                {
                    return;
                }

                if (meshOnlyScatter)
                {
                    toDestroy.Add(entity);
                    return;
                }

                Vector2 pos = position.Value.ToVector2();
                float distanceSq = (pos.X * pos.X) + (pos.Y * pos.Y);
                if (distanceSq <= rootDistanceSq)
                {
                    return;
                }

                toDestroy.Add(entity);
            });

            for (int i = 0; i < toDestroy.Count; i++)
            {
                if (engine.World.IsAlive(toDestroy[i]))
                {
                    PresentationEntityLifecycle.RequestDestroy(engine.World, toDestroy[i], "Blacksmith showcase scatter");
                }
            }
        }

        private void RefreshPanel(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            bool forcePanel = ReadStrictBoolEnv(ForcePanelEnvKey);
            bool benchmarkPanelSuppressed = IsBenchmarkMode(engine) && !forcePanel;
            if (benchmarkPanelSuppressed)
            {
                _panelController.ClearIfOwned(root);
                return;
            }

            int totalBlacksmiths = CountTrackedBlacksmithEntities(engine, out _);
            bool largeCrowd = totalBlacksmiths > DetailedPanelCrowdThreshold;
            if (largeCrowd && !forcePanel)
            {
                _panelController.ClearIfOwned(root);
                return;
            }

            bool playerControlsOnly = IsInteractiveMode(engine) && !forcePanel;
            if (playerControlsOnly)
            {
                if (_panelDirty)
                {
                    _cachedPanelState = BuildPanelState(engine, root);
                    _panelDirty = false;
                }

                _panelController.MountOrSync(root, engine, _cachedPanelState);
                return;
            }

            float refreshInterval = largeCrowd
                ? LargeCrowdPanelRefreshIntervalSeconds
                : PanelRefreshIntervalSeconds;

            if (_panelDirty || _panelRefreshCooldown <= 0f)
            {
                _cachedPanelState = BuildPanelState(engine, root);
                _panelDirty = false;
                _panelRefreshCooldown = refreshInterval;
            }

            _panelController.MountOrSync(root, engine, _cachedPanelState);
        }

        private PresenterBlacksmithShowcasePanelState BuildPanelState(GameEngine engine, UIRoot root)
        {
            float viewportWidth = root.Width > 0f ? root.Width : 1280f;
            float viewportHeight = root.Height > 0f ? root.Height : 720f;
            float availableWidth = MathF.Max(220f, viewportWidth - 24f);
            float availableHeight = MathF.Max(220f, viewportHeight - 24f);
            string lastChange = _changeFlashTimer > 0f && !string.IsNullOrWhiteSpace(_lastChangedField)
                ? $"Last change: {_lastChangedField}"
                : string.Empty;
            bool playerControlsOnly = IsInteractiveMode(engine) && !ReadStrictBoolEnv(ForcePanelEnvKey);
            if (playerControlsOnly)
            {
                return new PresenterBlacksmithShowcasePanelState(
                    PanelLeft: 12f,
                    PanelTop: 12f,
                    PanelWidth: MathF.Min(280f, availableWidth),
                    PanelHeight: 0f,
                    ScrollHeight: 0f,
                    Title: "Blacksmith Shop",
                    Subtitle: "Watch the blacksmith work. Use the buttons to start or stop, switch day and night, or tear the shop down.",
                    ViewportLabel: $"{(int)viewportWidth} x {(int)viewportHeight}",
                    SceneSummary: string.Empty,
                    ScatterSummary: string.Empty,
                    LastChange: lastChange,
                    WorkingActive: _isWorking,
                    NightActive: _isNight,
                    RootDestroyed: _destroyed,
                    RegionIndex: _regionIndex,
                    DurabilityPreset: 0,
                    ScatterTarget: _scatterTargetTotal,
                    ScatterAppliedTotal: _scatterRequestedTotal,
                    ScatterMin: ScatterMinTotal,
                    ScatterMax: ResolveScatterUiMax(engine),
                    BenchmarkSummary: string.Empty,
                    CapacitySummary: string.Empty,
                    ChecklistLines: Array.Empty<string>(),
                    DiagnosticLines: Array.Empty<string>(),
                    PresenterLines: Array.Empty<string>(),
                    PlayerControlsOnly: true);
            }

            float panelWidth = viewportWidth < 960f
                ? availableWidth
                : MathF.Min(480f, MathF.Max(360f, viewportWidth * 0.31f));
            panelWidth = MathF.Min(panelWidth, availableWidth);
            float panelHeight = MathF.Min(780f, availableHeight);
            float scrollHeight = MathF.Max(180f, panelHeight - 118f);

            int totalBlacksmiths = CountTrackedBlacksmithEntities(engine, out int scatterExtras);
            bool useDetailedPanelScan = totalBlacksmiths <= DetailedPanelCrowdThreshold;
            CapacityMetrics capacityMetrics = CaptureCapacityMetrics(engine);
            int durabilityPreset = ResolveDurabilityPreset(engine);
            float durabilityRatio = ResolveDurabilityRatio(engine);
            float durabilityCurrent = ResolveDurabilityCurrent(engine);
            float durabilityBase = ResolveDurabilityBase(engine);
            PresenterMetrics presenterMetrics = CapturePresenterMetrics(engine);
            RenderMetrics renderMetrics = CaptureRenderMetrics(
                engine,
                useDetailedPanelScan,
                durabilityRatio,
                durabilityCurrent,
                durabilityBase);
            string regionLabel = RegionNames[Math.Clamp(_regionIndex, 0, RegionNames.Length - 1)];
            string dayNightLabel = _isNight ? "NIGHT" : "DAY";
            string rootLabel = _buildingEntity != Entity.Null && engine.World.IsAlive(_buildingEntity)
                ? (engine.World.TryGet(_buildingEntity, out Name name) ? $"{name.Value}#{_buildingEntity.Id}" : $"Entity#{_buildingEntity.Id}")
                : "(missing)";

            string sceneSummary = _destroyed
                ? "Root OFFLINE | click Respawn Root to rebuild the canonical showcase actor."
                : $"Root ONLINE | Working {(_isWorking ? "ON" : "OFF")} | Region {regionLabel} | Durability {durabilityCurrent:F0}/{durabilityBase:F0} ({durabilityRatio:F2}) | {dayNightLabel}";
            string scatterSummary = IsMeshBenchmarkMode(engine)
                ? $"Mesh benchmark total {totalBlacksmiths} (target {_scatterRequestedTotal}, queued {_lastQueuedScatterExtras})"
                : $"Scatter total {totalBlacksmiths} (target {_scatterRequestedTotal}, extras {scatterExtras}, queued {_lastQueuedScatterExtras}, seed {_lastScatterSeed})";
            string benchmarkSummary = BuildBenchmarkSummary(engine, totalBlacksmiths, renderMetrics);
            string capacitySummary = BuildCapacitySummary(capacityMetrics, totalBlacksmiths);

            string[] checklistLines =
            {
                presenterMetrics.RootCount == (_destroyed ? 0 : 1) &&
                presenterMetrics.WorkshopLeftCount == (_destroyed ? 0 : 1) &&
                presenterMetrics.WorkshopRightCount == (_destroyed ? 0 : 1) &&
                presenterMetrics.ChimneyCount == (_destroyed ? 0 : 1) &&
                presenterMetrics.RouteSplineCount == (_destroyed ? 0 : 1) &&
                presenterMetrics.DecalCount == (_destroyed ? 0 : 1) &&
                presenterMetrics.WorkerCount == (_destroyed ? 0 : 1) &&
                presenterMetrics.BarCount == (_destroyed ? 0 : 1) &&
                presenterMetrics.TextCount == (_destroyed ? 0 : 1)
                    ? "PASS base tree: root + left/right/chimney/spline/decal/worker/HUD tree is complete."
                    : "WAIT base tree: canonical blacksmith subtree is still settling.",
                (!_isWorking && renderMetrics.VisibleSmokeCount == 0 && renderMetrics.VisibleWorkerCount == 0) ||
                (_isWorking && renderMetrics.VisibleSmokeCount >= 1 && renderMetrics.VisibleWorkerCount >= 1)
                    ? $"PASS working gate: smoke/worker => {(_isWorking ? "visible" : "hidden")}."
                    : "WARN working gate: smoke or worker visibility does not match the working tag.",
                renderMetrics.WorldHudBarCount >= (_destroyed ? 0 : 1) && renderMetrics.WorldHudTextCount >= (_destroyed ? 0 : 1)
                    ? $"PASS durability HUD: bar {renderMetrics.WorldHudBarCount}, text {renderMetrics.WorldHudTextCount}, ratio {renderMetrics.PrimaryHudBarValue:F2}, text {renderMetrics.PrimaryHudTextCurrent:F0}/{renderMetrics.PrimaryHudTextBase:F0}."
                    : "WARN durability HUD: expected both world HUD bar and text.",
                !useDetailedPanelScan ||
                totalBlacksmiths <= 1 ||
                renderMetrics.WorldHudBarValueRange > 0.01f ||
                renderMetrics.WorldHudTextCurrentRange > 0.5f
                    ? $"PASS random drift: world HUD range bar {renderMetrics.WorldHudBarValueRange:F2}, text {renderMetrics.WorldHudTextCurrentRange:F1}."
                    : "WAIT random drift: durability effect has not fanned out across the crowd yet.",
                renderMetrics.SplineRibbonCount >= (_destroyed ? 0 : 1)
                    ? $"PASS spline route: {renderMetrics.SplineRibbonCount} spline request(s) visible."
                    : "WARN spline route: worker route spline missing.",
                renderMetrics.GroundOverlayCount >= (_destroyed ? 0 : 1)
                    ? $"PASS forge decal: {renderMetrics.GroundOverlayCount} ground overlay request(s) visible."
                    : "WARN forge decal: ground overlay missing.",
                $"INFO durability: current {durabilityCurrent:F0}, base {durabilityBase:F0}, ratio {durabilityRatio:F2}, state {ResolveDurabilityLabel(durabilityPreset)}.",
                $"INFO smoke attachment: smoke parented under chimney => {renderMetrics.SmokeAttachedToChimneyCount}/{renderMetrics.VisibleSmokeCount}.",
                _destroyed
                    ? "INFO lifecycle: root destroyed; Respawn Root restores the canonical actor."
                    : "PASS lifecycle: root actor is alive; Destroy Root exercises teardown."
            };

            string[] diagnosticLines =
            {
                $"Viewport: {(int)viewportWidth} x {(int)viewportHeight} | panel {(int)panelWidth} x {(int)panelHeight}",
                $"Root entity: {rootLabel}",
                $"Blacksmith entities: {totalBlacksmiths} total | extras {scatterExtras}",
                $"Presenters: owned {presenterMetrics.RootOwnedCount} | active buffer {presenterMetrics.BufferActiveCount}",
                $"Meshes: workshops {renderMetrics.VisibleWorkshopCount} | chimney {renderMetrics.VisibleChimneyCount} | smoke {renderMetrics.VisibleSmokeCount} | worker skinned {renderMetrics.VisibleWorkerCount}",
                $"Presentation: spline {renderMetrics.SplineRibbonCount} | decal {renderMetrics.GroundOverlayCount} | world HUD {renderMetrics.WorldHudBarCount}/{renderMetrics.WorldHudTextCount} | screen HUD {renderMetrics.ScreenHudBarCount}/{renderMetrics.ScreenHudTextCount}",
                $"HUD truth: bar {renderMetrics.PrimaryHudBarValue:F2} | text current {renderMetrics.PrimaryHudTextCurrent:F0} | text base {renderMetrics.PrimaryHudTextBase:F0} | crowd range {renderMetrics.WorldHudBarValueRange:F2}/{renderMetrics.WorldHudTextCurrentRange:F1}",
                $"Drops: evt {capacityMetrics.PresentationEventDrops} | worldHud {capacityMetrics.WorldHudDrops} | screenHud {capacityMetrics.ScreenHudDrops} | prim {capacityMetrics.PrimitiveDrops} | skinned {capacityMetrics.SkinnedDrops}",
                $"State: region {_regionIndex} ({regionLabel}) | durability {durabilityRatio:F2} | working {(_isWorking ? 1 : 0)} | night {(_isNight ? 1 : 0)}",
                useDetailedPanelScan
                    ? "Diagnostics mode: detailed"
                    : $"Diagnostics mode: throttled for crowd>{DetailedPanelCrowdThreshold:n0}; panel avoids full-world scans."
            };

            return new PresenterBlacksmithShowcasePanelState(
                PanelLeft: 12f,
                PanelTop: 12f,
                PanelWidth: panelWidth,
                PanelHeight: panelHeight,
                ScrollHeight: scrollHeight,
                Title: "Presenter Blacksmith Showcase",
                Subtitle: "Mouse-only UAT panel. Buttons drive formal events/effects; showcase runtime stays on production pipelines.",
                ViewportLabel: $"{(int)viewportWidth} x {(int)viewportHeight}",
                SceneSummary: sceneSummary,
                ScatterSummary: scatterSummary,
                LastChange: lastChange,
                WorkingActive: _isWorking,
                NightActive: _isNight,
                RootDestroyed: _destroyed,
                RegionIndex: _regionIndex,
                DurabilityPreset: durabilityPreset,
                ScatterTarget: _scatterTargetTotal,
                ScatterAppliedTotal: _scatterRequestedTotal,
                ScatterMin: ScatterMinTotal,
                ScatterMax: ResolveScatterUiMax(engine),
                BenchmarkSummary: benchmarkSummary,
                CapacitySummary: capacitySummary,
                ChecklistLines: checklistLines,
                DiagnosticLines: diagnosticLines,
                PresenterLines: presenterMetrics.Lines,
                PlayerControlsOnly: false);
        }

        private PresenterMetrics CapturePresenterMetrics(GameEngine engine)
        {
            PresenterEntityRuntime? presenters = engine.GetService(CoreServiceKeys.PresenterEntityRuntime);
            PresenterDefinitionRegistry? definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry);
            if (presenters == null || definitions == null)
            {
                return PresenterMetrics.Empty;
            }

            int rootId = definitions.GetId(PresenterBlacksmithShowcaseIds.RootDefinitionId);
            int workshopLeftId = definitions.GetId(PresenterBlacksmithShowcaseIds.WorkshopLeftDefinitionId);
            int workshopRightId = definitions.GetId(PresenterBlacksmithShowcaseIds.WorkshopRightDefinitionId);
            int chimneyId = definitions.GetId(PresenterBlacksmithShowcaseIds.ChimneyDefinitionId);
            int smokeId = definitions.GetId(PresenterBlacksmithShowcaseIds.SmokeDefinitionId);
            int routeSplineId = definitions.GetId(PresenterBlacksmithShowcaseIds.RouteSplineDefinitionId);
            int decalId = definitions.GetId(PresenterBlacksmithShowcaseIds.DecalDefinitionId);
            int workerId = definitions.GetId(PresenterBlacksmithShowcaseIds.WorkerDefinitionId);
            int barId = definitions.GetId(PresenterBlacksmithShowcaseIds.DurabilityBarDefinitionId);
            int textId = definitions.GetId(PresenterBlacksmithShowcaseIds.DurabilityTextDefinitionId);

            int rootCount = CountAlive(presenters.GetActiveByOwnerDefinition(rootId, _buildingEntity));
            int workshopLeftCount = CountAlive(presenters.GetActiveByOwnerDefinition(workshopLeftId, _buildingEntity));
            int workshopRightCount = CountAlive(presenters.GetActiveByOwnerDefinition(workshopRightId, _buildingEntity));
            int chimneyCount = CountAlive(presenters.GetActiveByOwnerDefinition(chimneyId, _buildingEntity));
            int smokeCount = CountAlive(presenters.GetActiveByOwnerDefinition(smokeId, _buildingEntity));
            int routeSplineCount = CountAlive(presenters.GetActiveByOwnerDefinition(routeSplineId, _buildingEntity));
            int decalCount = CountAlive(presenters.GetActiveByOwnerDefinition(decalId, _buildingEntity));
            int workerCount = CountAlive(presenters.GetActiveByOwnerDefinition(workerId, _buildingEntity));
            int barCount = CountAlive(presenters.GetActiveByOwnerDefinition(barId, _buildingEntity));
            int textCount = CountAlive(presenters.GetActiveByOwnerDefinition(textId, _buildingEntity));
            int rootOwnedCount = rootCount + workshopLeftCount + workshopRightCount + chimneyCount + smokeCount + routeSplineCount + decalCount + workerCount + barCount + textCount;
            var lines = new List<string>(12);
            AppendPresenterLines(engine, presenters, definitions, lines, rootId);
            AppendPresenterLines(engine, presenters, definitions, lines, workshopLeftId);
            AppendPresenterLines(engine, presenters, definitions, lines, workshopRightId);
            AppendPresenterLines(engine, presenters, definitions, lines, chimneyId);
            AppendPresenterLines(engine, presenters, definitions, lines, smokeId);
            AppendPresenterLines(engine, presenters, definitions, lines, routeSplineId);
            AppendPresenterLines(engine, presenters, definitions, lines, decalId);
            AppendPresenterLines(engine, presenters, definitions, lines, workerId);
            AppendPresenterLines(engine, presenters, definitions, lines, barId);
            AppendPresenterLines(engine, presenters, definitions, lines, textId);

            if (lines.Count == 0)
            {
                lines.Add("(no root-owned presenter instances)");
            }

            return new PresenterMetrics(
                presenters.ActiveCount,
                rootOwnedCount,
                rootCount,
                workshopLeftCount,
                workshopRightCount,
                chimneyCount,
                smokeCount,
                routeSplineCount,
                decalCount,
                workerCount,
                barCount,
                textCount,
                lines.ToArray());
        }

        private RenderMetrics CaptureRenderMetrics(
            GameEngine engine,
            bool useDetailedPanelScan,
            float rootDurabilityRatio,
            float rootDurabilityCurrent,
            float rootDurabilityBase)
        {
            PrimitiveDrawBuffer? primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
            SkinnedVisualBatchBuffer? skinned = engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer);
            WorldHudBatchBuffer? worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            ScreenHudBatchBuffer? screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer);
            GroundOverlayBuffer? overlays = engine.GetService(CoreServiceKeys.GroundOverlayBuffer);
            SplineRibbonBuffer? splines = engine.GetService(CoreServiceKeys.SplineRibbonBuffer);
            MeshAssetRegistry? meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry);
            PresenterEntityRuntime? presenters = engine.GetService(CoreServiceKeys.PresenterEntityRuntime);
            PresenterDefinitionRegistry? definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry);
            if (primitives == null || skinned == null || worldHud == null || screenHud == null || overlays == null || splines == null || meshes == null || presenters == null || definitions == null)
            {
                return RenderMetrics.Empty;
            }

            int visibleWorkshopCount = 0;
            int visibleChimneyCount = 0;
            int visibleSmokeCount = 0;
            int visibleWorkerCount = 0;
            if (useDetailedPanelScan)
            {
                int northWorkshop = meshes.GetId("blacksmith.building.north.intact");
                int southWorkshop = meshes.GetId("blacksmith.building.south.intact");
                int damagedWorkshop = meshes.GetId("blacksmith.building.damaged");
                int ruinedWorkshop = meshes.GetId("blacksmith.building.ruined");
                int chimneyAsset = meshes.GetId("blacksmith.furnace");
                int smokeAsset = meshes.GetId("blacksmith.smoke.billboard");
                int workerAsset = meshes.GetId("blacksmith.worker.knight");

                foreach (ref readonly PrimitiveDrawItem item in primitives.GetSpan())
                {
                    if (item.Visibility != VisualVisibility.Visible)
                    {
                        continue;
                    }

                    if (item.MeshAssetId == northWorkshop ||
                        item.MeshAssetId == southWorkshop ||
                        item.MeshAssetId == damagedWorkshop ||
                        item.MeshAssetId == ruinedWorkshop)
                    {
                        visibleWorkshopCount++;
                    }

                    if (item.MeshAssetId == chimneyAsset)
                    {
                        visibleChimneyCount++;
                    }

                    if (item.MeshAssetId == smokeAsset)
                    {
                        visibleSmokeCount++;
                    }
                }

                foreach (ref readonly SkinnedVisualBatchItem item in skinned.GetSpan())
                {
                    if (item.MeshAssetId == workerAsset)
                    {
                        visibleWorkerCount++;
                    }
                }
            }

            int barCount = 0;
            int textCount = 0;
            float minHudBarValue = float.MaxValue;
            float maxHudBarValue = float.MinValue;
            float minHudTextCurrent = float.MaxValue;
            float maxHudTextCurrent = float.MinValue;
            float primaryHudBarValue = rootDurabilityRatio;
            float primaryHudTextCurrent = rootDurabilityCurrent;
            float primaryHudTextBase = rootDurabilityBase;
            foreach (ref readonly WorldHudItem item in worldHud.GetSpan())
            {
                if (item.Kind == WorldHudItemKind.Bar)
                {
                    barCount++;
                    minHudBarValue = MathF.Min(minHudBarValue, item.Value0);
                    maxHudBarValue = MathF.Max(maxHudBarValue, item.Value0);
                }
                else if (item.Kind == WorldHudItemKind.Text)
                {
                    textCount++;
                    minHudTextCurrent = MathF.Min(minHudTextCurrent, item.Value0);
                    maxHudTextCurrent = MathF.Max(maxHudTextCurrent, item.Value0);
                }
            }

            if (barCount == 0)
            {
                minHudBarValue = 0f;
                maxHudBarValue = 0f;
            }

            if (textCount == 0)
            {
                minHudTextCurrent = 0f;
                maxHudTextCurrent = 0f;
            }

            int smokeAttachedToChimneyCount = 0;
            if (useDetailedPanelScan)
            {
                int smokeId = definitions.GetId(PresenterBlacksmithShowcaseIds.SmokeDefinitionId);
                int chimneyId = definitions.GetId(PresenterBlacksmithShowcaseIds.ChimneyDefinitionId);
                var smokeQuery = new QueryDescription().WithAll<PresenterState, PresenterParent>();
                engine.World.Query(in smokeQuery, (Entity entity, ref PresenterState inst, ref PresenterParent parentComp) =>
                {
                    if (inst.DefId != smokeId || parentComp.Parent == Entity.Null || !engine.World.IsAlive(parentComp.Parent))
                    {
                        return;
                    }

                    if (!engine.World.Has<PresenterState>(parentComp.Parent))
                    {
                        return;
                    }

                    if (engine.World.Get<PresenterState>(parentComp.Parent).DefId == chimneyId)
                    {
                        smokeAttachedToChimneyCount++;
                    }
                });
            }

            return new RenderMetrics(
                visibleWorkshopCount,
                visibleChimneyCount,
                visibleSmokeCount,
                visibleWorkerCount,
                splines.Count,
                overlays.Count,
                barCount,
                textCount,
                screenHud.BarCount,
                screenHud.TextCount,
                smokeAttachedToChimneyCount,
                maxHudBarValue - minHudBarValue,
                maxHudTextCurrent - minHudTextCurrent,
                primaryHudBarValue,
                primaryHudTextCurrent,
                primaryHudTextBase);
        }

        private CapacityMetrics CaptureCapacityMetrics(GameEngine engine)
        {
            PresentationRuntimeConfig runtimeConfig = engine.MergedConfig?.Presentation
                ?? throw new InvalidOperationException("game.json presentation must be explicitly configured.");
            var presenters = engine.GetService(CoreServiceKeys.PresenterEntityRuntime);
            var events = engine.GetService(CoreServiceKeys.PresentationEventStream);
            var worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer);
            var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
            var skinned = engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer);
            var splineRibbons = engine.GetService(CoreServiceKeys.SplineRibbonBuffer);
            var overlays = engine.GetService(CoreServiceKeys.GroundOverlayBuffer);
            var spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue);

            return new CapacityMetrics(
                PresenterCapacity: runtimeConfig.PresenterInstanceCapacity,
                PresenterActive: presenters?.ActiveCount ?? 0,
                PresentationEventCapacity: events?.Capacity ?? runtimeConfig.PresentationEventStreamCapacity,
                PresentationEventCount: events?.Count ?? 0,
                PresentationEventDrops: events?.DroppedSinceClear ?? 0,
                PrimitiveCapacity: primitives?.Capacity ?? runtimeConfig.PrimitiveDrawBufferCapacity,
                PrimitiveCount: primitives?.Count ?? 0,
                PrimitiveDrops: primitives?.DroppedSinceClear ?? 0,
                WorldHudCapacity: worldHud?.Capacity ?? runtimeConfig.WorldHudCapacity,
                WorldHudCount: worldHud?.Count ?? 0,
                WorldHudDrops: worldHud?.DroppedSinceClear ?? 0,
                ScreenHudCapacity: screenHud?.Capacity ?? runtimeConfig.ScreenHudCapacity,
                ScreenHudCount: screenHud?.Count ?? 0,
                ScreenHudDrops: screenHud?.DroppedSinceClear ?? 0,
                SkinnedCapacity: skinned?.Capacity ?? runtimeConfig.SkinnedVisualBatchCapacity,
                SkinnedCount: skinned?.Count ?? 0,
                SkinnedDrops: skinned?.DroppedSinceClear ?? 0,
                SplineRibbonCapacity: splineRibbons?.Capacity ?? runtimeConfig.SplineRibbonCapacity,
                SplineRibbonCount: splineRibbons?.Count ?? 0,
                GroundOverlayCapacity: overlays?.Capacity ?? runtimeConfig.GroundOverlayCapacity,
                GroundOverlayCount: overlays?.Count ?? 0,
                SpawnQueueCapacity: spawnQueue?.Capacity ?? runtimeConfig.RuntimeEntitySpawnQueueCapacity,
                SpawnQueueCount: spawnQueue?.Count ?? 0);
        }

        private int CountBlacksmithEntities(GameEngine engine, out int scatterExtras)
        {
            int total = 0;
            int extras = 0;
            float rootDistanceSq = RootSearchRadiusCm * RootSearchRadiusCm;
            var query = new QueryDescription().WithAll<Name, WorldPositionCm>();
            engine.World.Query(in query, (Entity _, ref Name name, ref WorldPositionCm position) =>
            {
                if (!string.Equals(name.Value, PresenterBlacksmithShowcaseIds.EntityName, StringComparison.Ordinal))
                {
                    return;
                }

                total++;
                Vector2 pos = position.Value.ToVector2();
                float distanceSq = (pos.X * pos.X) + (pos.Y * pos.Y);
                if (distanceSq > rootDistanceSq)
                {
                    extras++;
                }
            });

            scatterExtras = extras;
            return total;
        }

        private static int CountMeshBenchmarkEntities(GameEngine engine)
        {
            int total = 0;
            var query = new QueryDescription().WithAll<Name>();
            engine.World.Query(in query, (ref Name name) =>
            {
                if (string.Equals(name.Value, PresenterBlacksmithShowcaseIds.MeshBenchmarkEntityName, StringComparison.Ordinal))
                {
                    total++;
                }
            });

            return total;
        }

        private static int CountMeshHudBarBenchmarkEntities(GameEngine engine)
        {
            int total = 0;
            var query = new QueryDescription().WithAll<Name>();
            engine.World.Query(in query, (ref Name name) =>
            {
                if (string.Equals(name.Value, PresenterBlacksmithShowcaseIds.MeshHudBarBenchmarkEntityName, StringComparison.Ordinal))
                {
                    total++;
                }
            });

            return total;
        }

        private static int CountMeshHudTextBenchmarkEntities(GameEngine engine)
        {
            int total = 0;
            var query = new QueryDescription().WithAll<Name>();
            engine.World.Query(in query, (ref Name name) =>
            {
                if (string.Equals(name.Value, PresenterBlacksmithShowcaseIds.MeshHudTextBenchmarkEntityName, StringComparison.Ordinal))
                {
                    total++;
                }
            });

            return total;
        }

        private static int CountDynamicWorkerEntities(GameEngine engine)
        {
            int total = 0;
            var query = new QueryDescription().WithAll<Name>();
            engine.World.Query(in query, (ref Name name) =>
            {
                if (string.Equals(name.Value, PresenterBlacksmithShowcaseIds.DynamicWorkerEntityName, StringComparison.Ordinal))
                {
                    total++;
                }
            });

            return total;
        }

        private static int CountMinimapMarkerBallEntities(GameEngine engine)
        {
            int total = 0;
            var query = new QueryDescription().WithAll<Name>();
            engine.World.Query(in query, (ref Name name) =>
            {
                if (string.Equals(name.Value, PresenterBlacksmithShowcaseIds.MinimapMarkerBallEntityName, StringComparison.Ordinal))
                {
                    total++;
                }
            });

            return total;
        }

        private int CountTrackedBlacksmithEntities(GameEngine engine, out int scatterExtras)
        {
            if (IsDynamicWorkerBenchmarkMode(engine))
            {
                int total = CountDynamicWorkerEntities(engine);
                scatterExtras = total;
                return total;
            }

            if (IsMinimapMarkerShowcaseMode(engine))
            {
                int total = CountMinimapMarkerBallEntities(engine);
                scatterExtras = total;
                return total;
            }

            if (UsesCleanHudTextScatter(engine))
            {
                int total = CountMeshHudTextBenchmarkEntities(engine);
                if (total > 0)
                {
                    scatterExtras = total;
                    return total;
                }
            }

            if (IsMeshBenchmarkMode(engine))
            {
                int total = CountMeshBenchmarkEntities(engine);
                scatterExtras = total;
                return total;
            }

            if (IsScatterHudBarBenchmarkMode(engine))
            {
                int total = CountMeshHudBarBenchmarkEntities(engine);
                scatterExtras = total;
                return total;
            }

            if (IsScatterHudTextBenchmarkMode(engine))
            {
                int total = CountMeshHudTextBenchmarkEntities(engine);
                scatterExtras = total;
                return total;
            }

            return CountBlacksmithEntities(engine, out scatterExtras);
        }

        private int ResolveDurabilityPreset(GameEngine engine)
        {
            float ratio = ResolveDurabilityRatio(engine);
            if (ratio <= 0f)
            {
                return 2;
            }

            return ratio <= 0.5f ? 1 : 0;
        }

        private static string ResolveDurabilityLabel(int preset)
        {
            return preset switch
            {
                0 => "intact",
                1 => "damaged",
                _ => "ruined",
            };
        }

        private void Flash(string field)
        {
            _lastChangedField = field;
            _changeFlashTimer = 0.5f;
            MarkPanelDirty();
        }

        private void TryApplyAutoScatter(GameEngine engine)
        {
            if (_autoScatterApplied)
            {
                return;
            }

            if (!SupportsBlacksmithScatter(engine))
            {
                _autoScatterApplied = true;
                return;
            }

            int currentTotal = CountTrackedBlacksmithEntities(engine, out _);
            if (currentTotal > 1)
            {
                _scatterRequestedTotal = currentTotal;
                _scatterTargetTotal = currentTotal;
                _autoScatterApplied = true;
                return;
            }

            if (TryReadPositiveIntEnv(AutoScatterTotalEnvKey, out int requested) && requested > 1)
            {
                ApplyScatterLayout(requested);
            }
            else if (IsDedicatedScatterBenchmarkMode(engine) && _scatterTargetTotal > 1)
            {
                ApplyScatterLayout(_scatterTargetTotal);
            }

            _autoScatterApplied = true;
        }

        private static bool IsDedicatedScatterBenchmarkMode(GameEngine engine)
        {
            return IsScatterBenchmarkMode(engine) ||
                   IsScatterHudBarBenchmarkMode(engine) ||
                   IsScatterHudTextBenchmarkMode(engine);
        }

        private void ResetControlState(GameEngine engine)
        {
            _isWorking = false;
            _isNight = false;
            _regionIndex = 0;
            _destroyed = false;
            _scatterRequestedTotal = 1;
            _scatterTargetTotal = SupportsBlacksmithScatter(engine)
                ? ReadRequiredMapMetadataInt(engine, MetadataSectionKey, ScatterInitialTargetMetadataKey)
                : ScatterMinTotal;
            _autoMeshBenchmarkApplied = false;
            _autoDynamicWorkerBenchmarkApplied = false;
            _autoMinimapMarkerShowcaseApplied = false;
            _autoWorkingApplied = false;
            MarkPanelDirty();
        }

        private void EnsureGameplayTagState(GameEngine engine, Entity entity)
        {
            if (!engine.World.Has<GameplayTagContainer>(entity))
            {
                engine.World.Add(entity, default(GameplayTagContainer));
            }

            if (!engine.World.Has<TagCountContainer>(entity))
            {
                engine.World.Add(entity, default(TagCountContainer));
            }

            if (!engine.World.Has<DirtyFlags>(entity))
            {
                engine.World.Add(entity, default(DirtyFlags));
            }
        }

        private float ResolveDurabilityCurrent(GameEngine engine)
        {
            if (_buildingEntity == Entity.Null || !engine.World.IsAlive(_buildingEntity) || !engine.World.Has<AttributeBuffer>(_buildingEntity))
            {
                return 0f;
            }

            return engine.World.Get<AttributeBuffer>(_buildingEntity).GetCurrent(_durabilityAttributeId);
        }

        private float ResolveDurabilityBase(GameEngine engine)
        {
            if (_buildingEntity == Entity.Null || !engine.World.IsAlive(_buildingEntity) || !engine.World.Has<AttributeBuffer>(_buildingEntity))
            {
                return 0f;
            }

            return engine.World.Get<AttributeBuffer>(_buildingEntity).GetBase(_durabilityAttributeId);
        }

        private float ResolveDurabilityRatio(GameEngine engine)
        {
            float max = ResolveDurabilityBase(engine);
            if (max <= 0f)
            {
                return 0f;
            }

            return Math.Clamp(ResolveDurabilityCurrent(engine) / max, 0f, 1f);
        }

        private int ResolveScatterUiMax(GameEngine engine)
        {
            return ComputeScatterCapacityMax(CaptureCapacityMetrics(engine), UsesCleanHudTextScatter(engine));
        }

        private int ClampScatterTotal(GameEngine engine, int total)
        {
            return Math.Clamp(total, ScatterMinTotal, ResolveScatterUiMax(engine));
        }

        private static int CountAlive(IReadOnlyList<Entity> entities)
        {
            int count = 0;
            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i] != Entity.Null)
                {
                    count++;
                }
            }

            return count;
        }

        private void AppendPresenterLines(
            GameEngine engine,
            PresenterEntityRuntime presenters,
            PresenterDefinitionRegistry definitions,
            List<string> lines,
            int definitionId)
        {
            if (_buildingEntity == Entity.Null || lines.Count >= 12)
            {
                return;
            }

            IReadOnlyList<Entity> scoped = presenters.GetActiveByOwnerDefinition(definitionId, _buildingEntity);
            for (int i = 0; i < scoped.Count && lines.Count < 12; i++)
            {
                Entity entity = scoped[i];
                if (entity == Entity.Null || !engine.World.IsAlive(entity) || !engine.World.Has<PresenterState>(entity))
                {
                    continue;
                }

                PresenterState inst = engine.World.Get<PresenterState>(entity);
                int parentId = engine.World.Has<PresenterParent>(entity)
                    ? engine.World.Get<PresenterParent>(entity).Parent.Id
                    : 0;
                TransformSource source = engine.World.Has<PresenterTransformSource>(entity)
                    ? engine.World.Get<PresenterTransformSource>(entity).Value
                    : TransformSource.EntityTransform;
                float durabilityRatio = presenters.ResolveFloat(entity, PresenterBlacksmithShowcaseIds.ParamDurability, -1f);
                int workshopState = presenters.ResolveInt(entity, PresenterBlacksmithShowcaseIds.ParamWorkshopAssetState, -1);
                string definitionName = definitions.GetName(inst.DefId);
                lines.Add($"[{entity.Id}] {definitionName} parent={parentId} source={source} durability={durabilityRatio:F2} meshState={workshopState}");
            }
        }

        private string BuildBenchmarkSummary(GameEngine engine, int totalBlacksmiths, in RenderMetrics renderMetrics)
        {
            PresentationTimingDiagnostics? timings = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
            if (timings == null)
            {
                return "fps --.-";
            }

            float frameMs = timings.LastWallFrameMs > 0.001f
                ? timings.LastWallFrameMs
                : (timings.WallFrameMs > 0.001f ? timings.WallFrameMs : (timings.LastFrameMs > 0.001f ? timings.LastFrameMs : timings.FrameMs));
            float fps = frameMs > 0.001f ? 1000f / frameMs : 0f;
            return $"fps {fps:0.0}";
        }

        private string BuildCapacitySummary(in CapacityMetrics metrics, int totalBlacksmiths)
        {
            int scatterUiMax = ComputeScatterCapacityMax(metrics, _activeEngine != null && UsesCleanHudTextScatter(_activeEngine));

            return $"Capacity: presenters {metrics.PresenterActive}/{metrics.PresenterCapacity}, events {metrics.PresentationEventCount}/{metrics.PresentationEventCapacity}, primitives {metrics.PrimitiveCount}/{metrics.PrimitiveCapacity}, world HUD {metrics.WorldHudCount}/{metrics.WorldHudCapacity}, screen HUD {metrics.ScreenHudCount}/{metrics.ScreenHudCapacity}, skinned {metrics.SkinnedCount}/{metrics.SkinnedCapacity}, spline {metrics.SplineRibbonCount}/{metrics.SplineRibbonCapacity}, decal {metrics.GroundOverlayCount}/{metrics.GroundOverlayCapacity}, spawnQ {metrics.SpawnQueueCount}/{metrics.SpawnQueueCapacity} | UI max {scatterUiMax} | requested {totalBlacksmiths}";
        }

        private static int ComputeScatterCapacityMax(in CapacityMetrics metrics, bool cleanHudTextScatter)
        {
            int presenterPerBlacksmith = cleanHudTextScatter ? BenchmarkHudTextPresenterCountPerBlacksmith : PresenterCountPerBlacksmith;
            int primitivePerBlacksmith = cleanHudTextScatter ? BenchmarkHudTextPrimitiveCountPerBlacksmith : PrimitiveCountPerBlacksmith;
            int worldHudPerBlacksmith = cleanHudTextScatter ? BenchmarkHudTextWorldHudCountPerBlacksmith : WorldHudCountPerBlacksmith;
            int screenHudPerBlacksmith = cleanHudTextScatter ? BenchmarkHudTextScreenHudCountPerBlacksmith : ScreenHudCountPerBlacksmith;
            int splineRibbonPerBlacksmith = cleanHudTextScatter ? 0 : SplineRibbonCountPerBlacksmith;
            int overlayPerBlacksmith = cleanHudTextScatter ? 0 : GroundOverlayCountPerBlacksmith;
            int skinnedPerBlacksmith = cleanHudTextScatter ? 0 : SkinnedCountPerBlacksmith;

            int presenterBound = ResolveCapacityBound(metrics.PresenterCapacity, presenterPerBlacksmith);
            int primitiveBound = ResolveCapacityBound(metrics.PrimitiveCapacity, primitivePerBlacksmith);
            int worldHudBound = ResolveCapacityBound(metrics.WorldHudCapacity, worldHudPerBlacksmith);
            int screenHudBound = ResolveCapacityBound(metrics.ScreenHudCapacity, screenHudPerBlacksmith);
            int splineRibbonBound = ResolveCapacityBound(metrics.SplineRibbonCapacity, splineRibbonPerBlacksmith);
            int overlayBound = ResolveCapacityBound(metrics.GroundOverlayCapacity, overlayPerBlacksmith);
            int skinnedBound = ResolveCapacityBound(metrics.SkinnedCapacity, skinnedPerBlacksmith);
            int spawnQueueBound = metrics.SpawnQueueCapacity > 0
                ? Math.Max(ScatterMinTotal, metrics.SpawnQueueCapacity + 1)
                : ScatterMinTotal;

            int lowestBound = presenterBound;
            lowestBound = Math.Min(lowestBound, primitiveBound);
            lowestBound = Math.Min(lowestBound, worldHudBound);
            lowestBound = Math.Min(lowestBound, screenHudBound);
            lowestBound = Math.Min(lowestBound, splineRibbonBound);
            lowestBound = Math.Min(lowestBound, overlayBound);
            lowestBound = Math.Min(lowestBound, skinnedBound);
            lowestBound = Math.Min(lowestBound, spawnQueueBound);
            return Math.Clamp(lowestBound, ScatterMinTotal, ScatterUiHardMaxTotal);
        }

        private static int ResolveCapacityBound(int capacity, int perBlacksmith)
        {
            if (perBlacksmith <= 0)
            {
                return int.MaxValue;
            }

            if (capacity <= 0)
            {
                return ScatterMinTotal;
            }

            return Math.Max(ScatterMinTotal, capacity / perBlacksmith);
        }

        private void Disable(GameEngine engine)
        {
            if (IsInteractiveMode(engine) &&
                engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
            {
                _panelController.ClearIfOwned(root);
            }

            if (ReferenceEquals(_activeEngine, engine))
            {
                _activeEngine = null;
            }

            _panelDirty = true;
            _panelRefreshCooldown = 0f;
            _cachedPanelState = PresenterBlacksmithShowcasePanelState.Empty;
        }

        private void MarkPanelDirty()
        {
            _panelDirty = true;
            _panelRefreshCooldown = 0f;
        }

        private static int ReadRequiredMapMetadataInt(GameEngine engine, string section, string key)
        {
            JsonNode valueNode = ReadRequiredMapMetadataValue(engine, section, key);

            try
            {
                int value = valueNode.GetValue<int>();
                if (value <= 0)
                {
                    throw new InvalidOperationException(
                        $"metadata.{section}.{key} must be > 0 for the presenter blacksmith showcase path.");
                }

                return value;
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    $"metadata.{section}.{key} must be an integer for the presenter blacksmith showcase path.",
                    ex);
            }
            catch (InvalidOperationException ex) when (!ex.Message.Contains("metadata.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"metadata.{section}.{key} must be an integer for the presenter blacksmith showcase path.",
                    ex);
            }
        }

        private static float ReadRequiredMapMetadataFloat(GameEngine engine, string section, string key)
        {
            JsonNode valueNode = ReadRequiredMapMetadataValue(engine, section, key);

            try
            {
                float value = valueNode.GetValue<float>();
                if (!float.IsFinite(value))
                {
                    throw new InvalidOperationException(
                        $"metadata.{section}.{key} must be finite for the presenter blacksmith showcase path.");
                }

                return value;
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    $"metadata.{section}.{key} must be a number for the presenter blacksmith showcase path.",
                    ex);
            }
            catch (InvalidOperationException ex) when (!ex.Message.Contains("metadata.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"metadata.{section}.{key} must be a number for the presenter blacksmith showcase path.",
                    ex);
            }
        }

        private static float ReadRequiredPositiveMapMetadataFloat(GameEngine engine, string section, string key)
        {
            float value = ReadRequiredMapMetadataFloat(engine, section, key);
            if (value <= 0f)
            {
                throw new InvalidOperationException(
                    $"metadata.{section}.{key} must be > 0 for the presenter blacksmith showcase path.");
            }

            return value;
        }

        private static float ReadRequiredNonNegativeMapMetadataFloat(GameEngine engine, string section, string key)
        {
            float value = ReadRequiredMapMetadataFloat(engine, section, key);
            if (value < 0f)
            {
                throw new InvalidOperationException(
                    $"metadata.{section}.{key} must be >= 0 for the presenter blacksmith showcase path.");
            }

            return value;
        }

        private static JsonNode ReadRequiredMapMetadataValue(GameEngine engine, string section, string key)
        {
            if (engine.CurrentMapSession?.MapConfig?.Metadata == null ||
                !engine.CurrentMapSession.MapConfig.Metadata.TryGetValue(section, out JsonNode? sectionNode) ||
                sectionNode is not JsonObject sectionObject ||
                !sectionObject.TryGetPropertyValue(key, out JsonNode? valueNode) ||
                valueNode == null)
            {
                string mapId = engine.CurrentMapSession?.MapId.Value ?? "<none>";
                throw new InvalidOperationException(
                    $"Map '{mapId}' must declare metadata.{section}.{key} for the presenter blacksmith showcase path.");
            }

            return valueNode;
        }

        private static BlacksmithScatterConfig ReadScatterConfig(GameEngine engine)
        {
            int seed = ReadRequiredMapMetadataInt(engine, MetadataSectionKey, ScatterSeedMetadataKey);
            float minRadiusCm = ReadRequiredPositiveMapMetadataFloat(engine, MetadataSectionKey, ScatterMinRadiusMetadataKey);
            float maxRadiusCm = ReadRequiredPositiveMapMetadataFloat(engine, MetadataSectionKey, ScatterMaxRadiusMetadataKey);
            float jitterCm = ReadRequiredNonNegativeMapMetadataFloat(engine, MetadataSectionKey, ScatterJitterMetadataKey);
            if (maxRadiusCm <= minRadiusCm)
            {
                throw new InvalidOperationException(
                    $"metadata.{MetadataSectionKey}.{ScatterMaxRadiusMetadataKey} must be greater than {ScatterMinRadiusMetadataKey}.");
            }

            return new BlacksmithScatterConfig(seed, minRadiusCm, maxRadiusCm, jitterCm);
        }

        private static int ReadPositiveIntEnvOverride(string key, int configuredValue)
        {
            return TryReadPositiveIntEnv(key, out int parsed)
                ? parsed
                : configuredValue;
        }

        private static bool TryReadPositiveIntEnv(string key, out int value)
        {
            value = 0;
            string? raw = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            if (!int.TryParse(raw, out int parsed) || parsed <= 0)
            {
                throw new InvalidOperationException(
                    $"Environment variable {key} must be an integer > 0 when declared.");
            }

            value = parsed;
            return true;
        }

        private static bool ReadStrictBoolEnv(string key)
        {
            string? raw = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            if (string.Equals(raw, "true", StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(raw, "false", StringComparison.Ordinal))
            {
                return false;
            }

            throw new InvalidOperationException(
                $"Environment variable {key} must be exactly 'true' or 'false' when declared.");
        }

        private readonly record struct BlacksmithScatterConfig(
            int Seed,
            float MinRadiusCm,
            float MaxRadiusCm,
            float JitterCm);

        private readonly record struct PresenterMetrics(
            int BufferActiveCount,
            int RootOwnedCount,
            int RootCount,
            int WorkshopLeftCount,
            int WorkshopRightCount,
            int ChimneyCount,
            int SmokeCount,
            int RouteSplineCount,
            int DecalCount,
            int WorkerCount,
            int BarCount,
            int TextCount,
            string[] Lines)
        {
            public static readonly PresenterMetrics Empty = new(
                BufferActiveCount: 0,
                RootOwnedCount: 0,
                RootCount: 0,
                WorkshopLeftCount: 0,
                WorkshopRightCount: 0,
                ChimneyCount: 0,
                SmokeCount: 0,
                RouteSplineCount: 0,
                DecalCount: 0,
                WorkerCount: 0,
                BarCount: 0,
                TextCount: 0,
                Lines: new[] { "(presenter services unavailable)" });
        }

        private readonly record struct RenderMetrics(
            int VisibleWorkshopCount,
            int VisibleChimneyCount,
            int VisibleSmokeCount,
            int VisibleWorkerCount,
            int SplineRibbonCount,
            int GroundOverlayCount,
            int WorldHudBarCount,
            int WorldHudTextCount,
            int ScreenHudBarCount,
            int ScreenHudTextCount,
            int SmokeAttachedToChimneyCount,
            float WorldHudBarValueRange,
            float WorldHudTextCurrentRange,
            float PrimaryHudBarValue,
            float PrimaryHudTextCurrent,
            float PrimaryHudTextBase)
        {
            public static readonly RenderMetrics Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0f, 0f, 0f, 0f, 0f);
        }

        private readonly record struct CapacityMetrics(
            int PresenterCapacity,
            int PresenterActive,
            int PresentationEventCapacity,
            int PresentationEventCount,
            int PresentationEventDrops,
            int PrimitiveCapacity,
            int PrimitiveCount,
            int PrimitiveDrops,
            int WorldHudCapacity,
            int WorldHudCount,
            int WorldHudDrops,
            int ScreenHudCapacity,
            int ScreenHudCount,
            int ScreenHudDrops,
            int SkinnedCapacity,
            int SkinnedCount,
            int SkinnedDrops,
            int SplineRibbonCapacity,
            int SplineRibbonCount,
            int GroundOverlayCapacity,
            int GroundOverlayCount,
            int SpawnQueueCapacity,
            int SpawnQueueCount)
        {
            public static readonly CapacityMetrics Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }
    }
}
