using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.UI;
using PerformerBlacksmithShowcaseMod.UI;

namespace PerformerBlacksmithShowcaseMod.Runtime
{
    internal sealed class PerformerBlacksmithShowcaseRuntime : IBenchmarkSceneController
    {
        private const float RootSearchRadiusCm = 50f;
        private const int ScatterMinTotal = 1;
        private const int ScatterUiHardMaxTotal = 300_000;
        private const int ScatterDefaultTarget = 30_000;
        private const int BenchmarkSampleFrames = 60;
        private const int DetailedPanelCrowdThreshold = 2048;
        private const int PerformerCountPerBlacksmith = 9;
        private const int BenchmarkHudTextPerformerCountPerBlacksmith = 3;
        private const int BenchmarkHudTextPrimitiveCountPerBlacksmith = 1;
        private const int BenchmarkHudTextWorldHudCountPerBlacksmith = 2;
        private const int BenchmarkHudTextScreenHudCountPerBlacksmith = 2;
        private const int PrimitiveCountPerBlacksmith = 3;
        private const int WorldHudCountPerBlacksmith = 2;
        private const int ScreenHudCountPerBlacksmith = 2;
        private const int RoadSplineCountPerBlacksmith = 1;
        private const int GroundOverlayCountPerBlacksmith = 1;
        private const int SkinnedCountPerBlacksmith = 1;
        private const string AutoScatterTotalEnvKey = "LUDOTS_BLACKSMITH_AUTO_SCATTER_TOTAL";
        private const string AutoScatterMinRadiusEnvKey = "LUDOTS_BLACKSMITH_AUTO_SCATTER_MIN_RADIUS_CM";
        private const string AutoScatterMaxRadiusEnvKey = "LUDOTS_BLACKSMITH_AUTO_SCATTER_MAX_RADIUS_CM";
        private const string AutoMeshBenchmarkTotalEnvKey = "LUDOTS_BLACKSMITH_MESH_BENCHMARK_TOTAL";
        private const string AutoDynamicWorkerBenchmarkTotalEnvKey = "LUDOTS_BLACKSMITH_DYNAMIC_WORKER_BENCHMARK_TOTAL";
        private const string MetadataSectionKey = "performerBlacksmith";
        private const string DynamicWorkerBenchmarkTotalMetadataKey = "dynamicWorkerBenchmarkTotal";
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
        private const float PanelRefreshIntervalSeconds = 0.25f;
        private const float LargeCrowdPanelRefreshIntervalSeconds = 1.5f;

        private readonly PerformerBlacksmithShowcasePanelController _panelController;
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
        private int _scatterTargetTotal = ScatterDefaultTarget;
        private int _lastScatterSeed;
        private int _scatterGeneration;
        private int _lastQueuedScatterExtras;
        private bool _autoScatterApplied;
        private bool _autoMeshBenchmarkApplied;
        private bool _autoDynamicWorkerBenchmarkApplied;
        private bool _autoMinimapMarkerShowcaseApplied;
        private float _panelRefreshCooldown;
        private bool _panelDirty = true;
        private PerformerBlacksmithShowcasePanelState _cachedPanelState = PerformerBlacksmithShowcasePanelState.Empty;
        private static readonly string[] RegionNames = { "NORTH", "SOUTH" };

        public bool IsActive => _activeEngine != null && PerformerBlacksmithShowcaseIds.IsShowcaseMap(_activeEngine.CurrentMapSession?.MapId.Value);

        public bool SupportsScatterControl => _activeEngine != null && SupportsBlacksmithScatter(_activeEngine);

        public bool IsCleanPerformanceScene => _activeEngine != null && ShouldUseCleanPerformanceScene(_activeEngine);

        public bool SuppressHostDiagnosticUi => _activeEngine != null &&
            PerformerBlacksmithShowcaseIds.IsShowcaseMap(_activeEngine.CurrentMapSession?.MapId.Value) &&
            !ReadEnvBool(ForceBenchmarkUiEnvKey);

        public bool SuppressHostDebugGuides => _activeEngine != null &&
            PerformerBlacksmithShowcaseIds.IsShowcaseMap(_activeEngine.CurrentMapSession?.MapId.Value) &&
            !PerformerBlacksmithShowcaseIds.IsMinimapMarkerLargeWorldShowcaseMap(_activeEngine.CurrentMapSession?.MapId.Value);

        public int ScatterMin => ScatterMinTotal;

        public int ScatterMax => _activeEngine != null ? ResolveScatterUiMax(_activeEngine) : ScatterUiHardMaxTotal;

        public int ScatterTarget => _scatterTargetTotal;

        public int ScatterAppliedTotal => _activeEngine != null ? CountTrackedBlacksmithEntities(_activeEngine, out _) : 0;

        public PerformerBlacksmithShowcaseRuntime()
        {
            _panelController = new PerformerBlacksmithShowcasePanelController(this);
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            if (!PerformerBlacksmithShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
            {
                Disable(engine);
                return Task.CompletedTask;
            }

            _workingTagId = TagRegistry.Register("working");
            _durabilityAttributeId = AttributeRegistry.Register("Durability");
            _durabilityIntactEffectId = EffectTemplateIdRegistry.GetId(PerformerBlacksmithShowcaseIds.EffectSetDurabilityIntact);
            _durabilityDamagedEffectId = EffectTemplateIdRegistry.GetId(PerformerBlacksmithShowcaseIds.EffectSetDurabilityDamaged);
            _durabilityRuinedEffectId = EffectTemplateIdRegistry.GetId(PerformerBlacksmithShowcaseIds.EffectSetDurabilityRuined);
            _activeEngine = engine;
            ResetControlState();
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
            _panelDirty = true;
            _panelRefreshCooldown = 0f;
            _cachedPanelState = PerformerBlacksmithShowcasePanelState.Empty;
            return Task.CompletedTask;
        }

        public void Update(GameEngine engine)
        {
            if (!PerformerBlacksmithShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
            {
                Disable(engine);
                return;
            }

            _activeEngine = engine;
            if (IsInteractiveMode(engine))
            {
                RefreshRootEntity(engine);
            }

            _panelRefreshCooldown = MathF.Max(0f, _panelRefreshCooldown - (1f / 60f));
            if (_changeFlashTimer > 0f)
            {
                _changeFlashTimer = MathF.Max(0f, _changeFlashTimer - 0.016f);
                _panelDirty = true;
            }

            RefreshPanel(engine);
        }

        internal void ToggleWorking()
        {
            GameEngine engine = RequireShowcaseEngine();
            if (_destroyed || _buildingEntity == Entity.Null || !engine.World.IsAlive(_buildingEntity))
            {
                return;
            }

            _isWorking = !_isWorking;
            EnsureGameplayTagState(engine, _buildingEntity);
            TagOps tagOps = engine.GetService(CoreServiceKeys.TagOps)
                ?? throw new InvalidOperationException("TagOps service missing.");
            ref GameplayTagContainer tags = ref engine.World.Get<GameplayTagContainer>(_buildingEntity);
            ref TagCountContainer counts = ref engine.World.Get<TagCountContainer>(_buildingEntity);
            ref DirtyFlags dirty = ref engine.World.Get<DirtyFlags>(_buildingEntity);
            if (_isWorking)
            {
                tagOps.AddTag(ref tags, ref counts, _workingTagId, ref dirty);
            }
            else
            {
                tagOps.RemoveTag(ref tags, ref counts, _workingTagId, ref dirty);
            }

            Flash($"Working => {(_isWorking ? "ON" : "OFF")}");
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
                ?.AddDayNight(PerformerBlacksmithShowcaseIds.ParamDayNight, _isNight ? 1f : 0f);
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

            engine.World.Destroy(_buildingEntity);
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

            int seed = unchecked(Environment.TickCount ^ (clampedTotal * 7919) ^ (++_scatterGeneration * 104729));
            _lastScatterSeed = seed;
            string templateId = ResolveScatterTemplateId(engine);
            int requestedEntityCount = cleanHudTextScatter || meshOnlyScatter || meshHudBarScatter || meshHudTextScatter
                ? clampedTotal
                : clampedTotal - 1;
            int queued = PerformerBlacksmithScatterPlanner.EnqueueTemplateScatter(
                spawnQueue,
                engine.CurrentMapSession?.MapId ?? default,
                templateId,
                requestedEntityCount,
                seed,
                ResolveAutoScatterMinRadiusCm(),
                ResolveAutoScatterMaxRadiusCm(),
                PerformerBlacksmithScatterPlanner.DefaultJitterCm);
            _lastQueuedScatterExtras = queued;
            Flash($"Scatter total => {clampedTotal} (seed {seed}, queued {queued})");
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
                PerformerBlacksmithShowcaseIds.IsShowcaseMap(_activeEngine.CurrentMapSession?.MapId.Value))
            {
                return _activeEngine;
            }

            throw new InvalidOperationException("Blacksmith showcase actions require the showcase map to be active.");
        }

        private static bool IsInteractiveMode(GameEngine engine)
        {
            return PerformerBlacksmithShowcaseIds.IsInteractiveShowcaseMap(engine.CurrentMapSession?.MapId.Value);
        }

        private static bool IsScatterBenchmarkMode(GameEngine engine)
        {
            return PerformerBlacksmithShowcaseIds.IsScatterBenchmarkMap(engine.CurrentMapSession?.MapId.Value);
        }

        private static bool IsScatterHudBarBenchmarkMode(GameEngine engine)
        {
            return PerformerBlacksmithShowcaseIds.IsScatterHudBarBenchmarkMap(engine.CurrentMapSession?.MapId.Value);
        }

        private static bool IsScatterHudTextBenchmarkMode(GameEngine engine)
        {
            return PerformerBlacksmithShowcaseIds.IsScatterHudTextBenchmarkMap(engine.CurrentMapSession?.MapId.Value);
        }

        private static bool IsMeshBenchmarkMode(GameEngine engine)
        {
            return PerformerBlacksmithShowcaseIds.IsMeshBenchmarkMap(engine.CurrentMapSession?.MapId.Value);
        }

        private static bool IsDynamicWorkerBenchmarkMode(GameEngine engine)
        {
            return PerformerBlacksmithShowcaseIds.IsDynamicWorkerBenchmarkMap(engine.CurrentMapSession?.MapId.Value);
        }

        private static bool IsMinimapMarkerShowcaseMode(GameEngine engine)
        {
            return PerformerBlacksmithShowcaseIds.IsMinimapMarkerLargeWorldShowcaseMap(engine.CurrentMapSession?.MapId.Value);
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
                return PerformerBlacksmithShowcaseIds.MeshBenchmarkTemplateId;
            }

            if (IsScatterHudBarBenchmarkMode(engine))
            {
                return PerformerBlacksmithShowcaseIds.MeshHudBarBenchmarkTemplateId;
            }

            if (IsScatterHudTextBenchmarkMode(engine) || UsesCleanHudTextScatter(engine))
            {
                return PerformerBlacksmithShowcaseIds.MeshHudTextBenchmarkTemplateId;
            }

            return PerformerBlacksmithShowcaseIds.TemplateId;
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

            int requested = ReadEnvInt(AutoMeshBenchmarkTotalEnvKey, PerformerBlacksmithShowcaseIds.MeshBenchmarkDefaultTotal);
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
            return ReadEnvInt(AutoDynamicWorkerBenchmarkTotalEnvKey, metadataTotal);
        }

        private static int EnqueueMeshBenchmark(RuntimeEntitySpawnQueue queue, GameEngine engine, int total)
        {
            int count = Math.Max(0, total);
            if (count == 0)
            {
                return 0;
            }

            int seed = unchecked(Environment.TickCount ^ (count * 48611));
            var config = engine.GetService(CoreServiceKeys.GameConfig)
                ?? throw new InvalidOperationException("GameConfig service missing.");
            float halfWorldWidthCm = config.WorldWidthInTiles * config.GridCellSizeCm * 0.5f;
            float halfWorldHeightCm = config.WorldHeightInTiles * config.GridCellSizeCm * 0.5f;
            float maxWorldRadiusCm = MathF.Max(1000f, MathF.Min(halfWorldWidthCm, halfWorldHeightCm) - 600f);
            float minRadiusCm = MathF.Min(
                PerformerBlacksmithScatterPlanner.DefaultMinRadiusCm * 2.5f,
                MathF.Max(750f, maxWorldRadiusCm * 0.28f));
            float requestedRadiusCm = PerformerBlacksmithScatterPlanner.DefaultMaxRadiusCm * MathF.Max(3.5f, MathF.Sqrt(count / 220f));
            float maxRadiusCm = MathF.Max(minRadiusCm + 500f, MathF.Min(maxWorldRadiusCm, requestedRadiusCm));
            float jitterCm = PerformerBlacksmithScatterPlanner.DefaultJitterCm * 1.5f;

            return PerformerBlacksmithScatterPlanner.EnqueueTemplateScatter(
                queue,
                engine.CurrentMapSession?.MapId ?? default,
                PerformerBlacksmithShowcaseIds.MeshBenchmarkTemplateId,
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

            int seed = unchecked(Environment.TickCount ^ (count * 61543));
            IVisualHeightmapRenderSource heightmap = RequireVisualHeightmapRenderSource(engine);
            float jitterCm = ReadRequiredMapMetadataFloat(
                engine,
                MetadataSectionKey,
                DynamicWorkerScatterJitterMetadataKey);
            float paddingCm = ReadRequiredMapMetadataFloat(
                engine,
                MetadataSectionKey,
                DynamicWorkerScatterPaddingMetadataKey);
            if (jitterCm < 0f || paddingCm < 0f)
            {
                throw new InvalidOperationException(
                    $"Map '{engine.CurrentMapSession?.MapId.Value ?? "<none>"}' has invalid dynamic worker scatter metadata. " +
                    $"{DynamicWorkerScatterJitterMetadataKey} and {DynamicWorkerScatterPaddingMetadataKey} must be >= 0.");
            }

            float leftCm = heightmap.Bounds.Left + paddingCm + jitterCm;
            float rightCm = heightmap.Bounds.Right - paddingCm - jitterCm;
            float topCm = heightmap.Bounds.Top + paddingCm + jitterCm;
            float bottomCm = heightmap.Bounds.Bottom - paddingCm - jitterCm;
            if (leftCm >= rightCm || topCm >= bottomCm)
            {
                throw new InvalidOperationException(
                    $"Map '{engine.CurrentMapSession?.MapId.Value ?? "<none>"}' dynamic worker scatter padding leaves no valid VisualHeightmap spawn area.");
            }

            return PerformerBlacksmithScatterPlanner.EnqueueTemplateAreaScatter(
                queue,
                engine.CurrentMapSession?.MapId ?? default,
                PerformerBlacksmithShowcaseIds.DynamicWorkerTemplateId,
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

            float jitterCm = ReadRequiredMapMetadataFloat(engine, MetadataSectionKey, MinimapMarkerScatterJitterMetadataKey);
            float paddingCm = ReadRequiredMapMetadataFloat(engine, MetadataSectionKey, MinimapMarkerScatterPaddingMetadataKey);
            if (jitterCm < 0f || paddingCm < 0f)
            {
                throw new InvalidOperationException(
                    $"Map '{engine.CurrentMapSession?.MapId.Value ?? "<none>"}' has invalid minimap marker scatter metadata. " +
                    $"{MinimapMarkerScatterJitterMetadataKey} and {MinimapMarkerScatterPaddingMetadataKey} must be >= 0.");
            }

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

            int seed = ReadOptionalMapMetadataInt(engine, MetadataSectionKey, MinimapMarkerScatterSeedMetadataKey, unchecked(Environment.TickCount ^ (count * 31991)));
            int clusterCount = Math.Clamp(
                ReadOptionalMapMetadataInt(engine, MetadataSectionKey, MinimapMarkerVisibleClusterCountMetadataKey, 0),
                0,
                count);
            int queued = 0;
            if (clusterCount > 0)
            {
                float clusterCenterXCm = ReadOptionalMapMetadataFloat(engine, MetadataSectionKey, MinimapMarkerVisibleClusterCenterXMetadataKey, 0f);
                float clusterCenterYCm = ReadOptionalMapMetadataFloat(engine, MetadataSectionKey, MinimapMarkerVisibleClusterCenterYMetadataKey, 0f);
                float clusterRadiusCm = ReadRequiredMapMetadataFloat(engine, MetadataSectionKey, MinimapMarkerVisibleClusterRadiusMetadataKey);
                if (clusterRadiusCm <= 0f)
                {
                    throw new InvalidOperationException(
                        $"metadata.{MetadataSectionKey}.{MinimapMarkerVisibleClusterRadiusMetadataKey} must be > 0 for the minimap marker showcase.");
                }

                queued += PerformerBlacksmithScatterPlanner.EnqueueTemplateClusterScatter(
                    queue,
                    engine.CurrentMapSession?.MapId ?? default,
                    PerformerBlacksmithShowcaseIds.MinimapMarkerBallTemplateId,
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

            queued += PerformerBlacksmithScatterPlanner.EnqueueTemplateAreaScatter(
                queue,
                engine.CurrentMapSession?.MapId ?? default,
                PerformerBlacksmithShowcaseIds.MinimapMarkerBallTemplateId,
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
                if (!string.Equals(name.Value, PerformerBlacksmithShowcaseIds.EntityName, StringComparison.OrdinalIgnoreCase))
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
                TemplateId = PerformerBlacksmithShowcaseIds.TemplateId,
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

            ResetControlState();
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
                bool isBlacksmith = string.Equals(name.Value, PerformerBlacksmithShowcaseIds.EntityName, StringComparison.OrdinalIgnoreCase);
                bool isMeshBenchmark = string.Equals(name.Value, PerformerBlacksmithShowcaseIds.MeshBenchmarkEntityName, StringComparison.OrdinalIgnoreCase);
                bool isMeshHudBarBenchmark = string.Equals(name.Value, PerformerBlacksmithShowcaseIds.MeshHudBarBenchmarkEntityName, StringComparison.OrdinalIgnoreCase);
                bool isMeshHudTextBenchmark = string.Equals(name.Value, PerformerBlacksmithShowcaseIds.MeshHudTextBenchmarkEntityName, StringComparison.OrdinalIgnoreCase);
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
                    engine.World.Destroy(toDestroy[i]);
                }
            }
        }

        private void RefreshPanel(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            bool forcePanel = ReadEnvBool(ForcePanelEnvKey);
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

        private PerformerBlacksmithShowcasePanelState BuildPanelState(GameEngine engine, UIRoot root)
        {
            float viewportWidth = root.Width > 0f ? root.Width : 1280f;
            float viewportHeight = root.Height > 0f ? root.Height : 720f;
            float availableWidth = MathF.Max(220f, viewportWidth - 24f);
            float availableHeight = MathF.Max(220f, viewportHeight - 24f);
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
            PerformerMetrics performerMetrics = CapturePerformerMetrics(engine);
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
            string lastChange = _changeFlashTimer > 0f && !string.IsNullOrWhiteSpace(_lastChangedField)
                ? $"Last change: {_lastChangedField}"
                : string.Empty;

            string[] checklistLines =
            {
                performerMetrics.RootCount == (_destroyed ? 0 : 1) &&
                performerMetrics.WorkshopLeftCount == (_destroyed ? 0 : 1) &&
                performerMetrics.WorkshopRightCount == (_destroyed ? 0 : 1) &&
                performerMetrics.ChimneyCount == (_destroyed ? 0 : 1) &&
                performerMetrics.RouteSplineCount == (_destroyed ? 0 : 1) &&
                performerMetrics.DecalCount == (_destroyed ? 0 : 1) &&
                performerMetrics.WorkerCount == (_destroyed ? 0 : 1) &&
                performerMetrics.BarCount == (_destroyed ? 0 : 1) &&
                performerMetrics.TextCount == (_destroyed ? 0 : 1)
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
                renderMetrics.RoadSplineCount >= (_destroyed ? 0 : 1)
                    ? $"PASS spline route: {renderMetrics.RoadSplineCount} spline request(s) visible."
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
                $"Performers: owned {performerMetrics.RootOwnedCount} | active buffer {performerMetrics.BufferActiveCount}",
                $"Meshes: workshops {renderMetrics.VisibleWorkshopCount} | chimney {renderMetrics.VisibleChimneyCount} | smoke {renderMetrics.VisibleSmokeCount} | worker skinned {renderMetrics.VisibleWorkerCount}",
                $"Presentation: spline {renderMetrics.RoadSplineCount} | decal {renderMetrics.GroundOverlayCount} | world HUD {renderMetrics.WorldHudBarCount}/{renderMetrics.WorldHudTextCount} | screen HUD {renderMetrics.ScreenHudBarCount}/{renderMetrics.ScreenHudTextCount}",
                $"HUD truth: bar {renderMetrics.PrimaryHudBarValue:F2} | text current {renderMetrics.PrimaryHudTextCurrent:F0} | text base {renderMetrics.PrimaryHudTextBase:F0} | crowd range {renderMetrics.WorldHudBarValueRange:F2}/{renderMetrics.WorldHudTextCurrentRange:F1}",
                $"Drops: evt {capacityMetrics.PresentationEventDrops} | worldHud {capacityMetrics.WorldHudDrops} | screenHud {capacityMetrics.ScreenHudDrops} | prim {capacityMetrics.PrimitiveDrops} | skinned {capacityMetrics.SkinnedDrops}",
                $"State: region {_regionIndex} ({regionLabel}) | durability {durabilityRatio:F2} | working {(_isWorking ? 1 : 0)} | night {(_isNight ? 1 : 0)}",
                useDetailedPanelScan
                    ? "Diagnostics mode: detailed"
                    : $"Diagnostics mode: throttled for crowd>{DetailedPanelCrowdThreshold:n0}; panel avoids full-world scans."
            };

            return new PerformerBlacksmithShowcasePanelState(
                PanelLeft: 12f,
                PanelTop: 12f,
                PanelWidth: panelWidth,
                PanelHeight: panelHeight,
                ScrollHeight: scrollHeight,
                Title: "Performer Blacksmith Showcase",
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
                PerformerLines: performerMetrics.Lines);
        }

        private PerformerMetrics CapturePerformerMetrics(GameEngine engine)
        {
            PerformerEntityRuntime? performers = engine.GetService(CoreServiceKeys.PerformerEntityRuntime);
            PerformerDefinitionRegistry? definitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry);
            if (performers == null || definitions == null)
            {
                return PerformerMetrics.Empty;
            }

            int rootId = definitions.GetId(PerformerBlacksmithShowcaseIds.RootDefinitionId);
            int workshopLeftId = definitions.GetId(PerformerBlacksmithShowcaseIds.WorkshopLeftDefinitionId);
            int workshopRightId = definitions.GetId(PerformerBlacksmithShowcaseIds.WorkshopRightDefinitionId);
            int chimneyId = definitions.GetId(PerformerBlacksmithShowcaseIds.ChimneyDefinitionId);
            int smokeId = definitions.GetId(PerformerBlacksmithShowcaseIds.SmokeDefinitionId);
            int routeSplineId = definitions.GetId(PerformerBlacksmithShowcaseIds.RouteSplineDefinitionId);
            int decalId = definitions.GetId(PerformerBlacksmithShowcaseIds.DecalDefinitionId);
            int workerId = definitions.GetId(PerformerBlacksmithShowcaseIds.WorkerDefinitionId);
            int barId = definitions.GetId(PerformerBlacksmithShowcaseIds.DurabilityBarDefinitionId);
            int textId = definitions.GetId(PerformerBlacksmithShowcaseIds.DurabilityTextDefinitionId);

            int rootCount = CountAlive(performers.GetActiveByOwnerDefinition(rootId, _buildingEntity));
            int workshopLeftCount = CountAlive(performers.GetActiveByOwnerDefinition(workshopLeftId, _buildingEntity));
            int workshopRightCount = CountAlive(performers.GetActiveByOwnerDefinition(workshopRightId, _buildingEntity));
            int chimneyCount = CountAlive(performers.GetActiveByOwnerDefinition(chimneyId, _buildingEntity));
            int smokeCount = CountAlive(performers.GetActiveByOwnerDefinition(smokeId, _buildingEntity));
            int routeSplineCount = CountAlive(performers.GetActiveByOwnerDefinition(routeSplineId, _buildingEntity));
            int decalCount = CountAlive(performers.GetActiveByOwnerDefinition(decalId, _buildingEntity));
            int workerCount = CountAlive(performers.GetActiveByOwnerDefinition(workerId, _buildingEntity));
            int barCount = CountAlive(performers.GetActiveByOwnerDefinition(barId, _buildingEntity));
            int textCount = CountAlive(performers.GetActiveByOwnerDefinition(textId, _buildingEntity));
            int rootOwnedCount = rootCount + workshopLeftCount + workshopRightCount + chimneyCount + smokeCount + routeSplineCount + decalCount + workerCount + barCount + textCount;
            var lines = new List<string>(12);
            AppendPerformerLines(engine, performers, definitions, lines, rootId);
            AppendPerformerLines(engine, performers, definitions, lines, workshopLeftId);
            AppendPerformerLines(engine, performers, definitions, lines, workshopRightId);
            AppendPerformerLines(engine, performers, definitions, lines, chimneyId);
            AppendPerformerLines(engine, performers, definitions, lines, smokeId);
            AppendPerformerLines(engine, performers, definitions, lines, routeSplineId);
            AppendPerformerLines(engine, performers, definitions, lines, decalId);
            AppendPerformerLines(engine, performers, definitions, lines, workerId);
            AppendPerformerLines(engine, performers, definitions, lines, barId);
            AppendPerformerLines(engine, performers, definitions, lines, textId);

            if (lines.Count == 0)
            {
                lines.Add("(no root-owned performer instances)");
            }

            return new PerformerMetrics(
                performers.ActiveCount,
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
            RoadSplineBuffer? splines = engine.GetService(CoreServiceKeys.RoadSplineBuffer);
            MeshAssetRegistry? meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry);
            PerformerEntityRuntime? performers = engine.GetService(CoreServiceKeys.PerformerEntityRuntime);
            PerformerDefinitionRegistry? definitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry);
            if (primitives == null || skinned == null || worldHud == null || screenHud == null || overlays == null || splines == null || meshes == null || performers == null || definitions == null)
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
                int smokeId = definitions.GetId(PerformerBlacksmithShowcaseIds.SmokeDefinitionId);
                int chimneyId = definitions.GetId(PerformerBlacksmithShowcaseIds.ChimneyDefinitionId);
                var smokeQuery = new QueryDescription().WithAll<PerformerState, PerformerParent>();
                engine.World.Query(in smokeQuery, (Entity entity, ref PerformerState inst, ref PerformerParent parentComp) =>
                {
                    if (inst.DefId != smokeId || parentComp.Parent == Entity.Null || !engine.World.IsAlive(parentComp.Parent))
                    {
                        return;
                    }

                    if (!engine.World.Has<PerformerState>(parentComp.Parent))
                    {
                        return;
                    }

                    if (engine.World.Get<PerformerState>(parentComp.Parent).DefId == chimneyId)
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
            PresentationRuntimeConfig runtimeConfig = engine.MergedConfig?.Presentation ?? new PresentationRuntimeConfig();
            var performers = engine.GetService(CoreServiceKeys.PerformerEntityRuntime);
            var events = engine.GetService(CoreServiceKeys.PresentationEventStream);
            var worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer);
            var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
            var skinned = engine.GetService(CoreServiceKeys.PresentationSkinnedVisualBatchBuffer);
            var roadSplines = engine.GetService(CoreServiceKeys.RoadSplineBuffer);
            var overlays = engine.GetService(CoreServiceKeys.GroundOverlayBuffer);
            var spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue);

            return new CapacityMetrics(
                PerformerCapacity: runtimeConfig.GetEffectivePerformerInstanceCapacity(),
                PerformerActive: performers?.ActiveCount ?? 0,
                PresentationEventCapacity: events?.Capacity ?? runtimeConfig.GetEffectivePresentationEventStreamCapacity(),
                PresentationEventCount: events?.Count ?? 0,
                PresentationEventDrops: events?.DroppedSinceClear ?? 0,
                PrimitiveCapacity: primitives?.Capacity ?? runtimeConfig.GetEffectivePrimitiveDrawBufferCapacity(),
                PrimitiveCount: primitives?.Count ?? 0,
                PrimitiveDrops: primitives?.DroppedSinceClear ?? 0,
                WorldHudCapacity: worldHud?.Capacity ?? runtimeConfig.GetEffectiveWorldHudCapacity(),
                WorldHudCount: worldHud?.Count ?? 0,
                WorldHudDrops: worldHud?.DroppedSinceClear ?? 0,
                ScreenHudCapacity: screenHud?.Capacity ?? runtimeConfig.GetEffectiveScreenHudCapacity(),
                ScreenHudCount: screenHud?.Count ?? 0,
                ScreenHudDrops: screenHud?.DroppedSinceClear ?? 0,
                SkinnedCapacity: skinned?.Capacity ?? runtimeConfig.GetEffectiveSkinnedVisualBatchCapacity(),
                SkinnedCount: skinned?.Count ?? 0,
                SkinnedDrops: skinned?.DroppedSinceClear ?? 0,
                RoadSplineCapacity: roadSplines?.Capacity ?? runtimeConfig.GetEffectiveRoadSplineCapacity(),
                RoadSplineCount: roadSplines?.Count ?? 0,
                GroundOverlayCapacity: overlays?.Capacity ?? runtimeConfig.GetEffectiveGroundOverlayCapacity(),
                GroundOverlayCount: overlays?.Count ?? 0,
                SpawnQueueCapacity: spawnQueue?.Capacity ?? runtimeConfig.GetEffectiveRuntimeEntitySpawnQueueCapacity(),
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
                if (!string.Equals(name.Value, PerformerBlacksmithShowcaseIds.EntityName, StringComparison.OrdinalIgnoreCase))
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
                if (string.Equals(name.Value, PerformerBlacksmithShowcaseIds.MeshBenchmarkEntityName, StringComparison.OrdinalIgnoreCase))
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
                if (string.Equals(name.Value, PerformerBlacksmithShowcaseIds.MeshHudBarBenchmarkEntityName, StringComparison.OrdinalIgnoreCase))
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
                if (string.Equals(name.Value, PerformerBlacksmithShowcaseIds.MeshHudTextBenchmarkEntityName, StringComparison.OrdinalIgnoreCase))
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
                if (string.Equals(name.Value, PerformerBlacksmithShowcaseIds.DynamicWorkerEntityName, StringComparison.OrdinalIgnoreCase))
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
                if (string.Equals(name.Value, PerformerBlacksmithShowcaseIds.MinimapMarkerBallEntityName, StringComparison.OrdinalIgnoreCase))
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

            // Auto scatter is an explicit benchmark/debug opt-in. The showcase should
            // boot into its authored single-root baseline unless a launch env requests
            // a larger crowd. Tests and normal UAT then stay on the same baseline path.
            int requested = ReadEnvInt(AutoScatterTotalEnvKey, 0);
            if (requested > 1)
            {
                ApplyScatterLayout(requested);
            }

            _autoScatterApplied = true;
        }

        private void ResetControlState()
        {
            _isWorking = false;
            _isNight = false;
            _regionIndex = 0;
            _destroyed = false;
            _scatterRequestedTotal = 1;
            _scatterTargetTotal = ScatterDefaultTarget;
            _autoMeshBenchmarkApplied = false;
            _autoDynamicWorkerBenchmarkApplied = false;
            _autoMinimapMarkerShowcaseApplied = false;
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

        private void AppendPerformerLines(
            GameEngine engine,
            PerformerEntityRuntime performers,
            PerformerDefinitionRegistry definitions,
            List<string> lines,
            int definitionId)
        {
            if (_buildingEntity == Entity.Null || lines.Count >= 12)
            {
                return;
            }

            IReadOnlyList<Entity> scoped = performers.GetActiveByOwnerDefinition(definitionId, _buildingEntity);
            for (int i = 0; i < scoped.Count && lines.Count < 12; i++)
            {
                Entity entity = scoped[i];
                if (entity == Entity.Null || !engine.World.IsAlive(entity) || !engine.World.Has<PerformerState>(entity))
                {
                    continue;
                }

                PerformerState inst = engine.World.Get<PerformerState>(entity);
                int parentId = engine.World.Has<PerformerParent>(entity)
                    ? engine.World.Get<PerformerParent>(entity).Parent.Id
                    : 0;
                TransformSource source = engine.World.Has<PerformerTransformSource>(entity)
                    ? engine.World.Get<PerformerTransformSource>(entity).Value
                    : TransformSource.EntityTransform;
                float durabilityRatio = performers.ResolveFloat(entity, PerformerBlacksmithShowcaseIds.ParamDurability, -1f);
                int workshopState = performers.ResolveInt(entity, PerformerBlacksmithShowcaseIds.ParamWorkshopAssetState, -1);
                string definitionName = definitions.GetName(inst.DefId);
                lines.Add($"[{entity.Id}] {definitionName} parent={parentId} source={source} durability={durabilityRatio:F2} meshState={workshopState}");
            }
        }

        private string BuildBenchmarkSummary(GameEngine engine, int totalBlacksmiths, in RenderMetrics renderMetrics)
        {
            PresentationTimingDiagnostics? timings = engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
            if (timings == null)
            {
                return $"Runtime: timing diagnostics unavailable | world HUD {renderMetrics.WorldHudBarCount}/{renderMetrics.WorldHudTextCount} | screen HUD {renderMetrics.ScreenHudBarCount}/{renderMetrics.ScreenHudTextCount} @ blacksmiths {totalBlacksmiths}";
            }

            float frameMs = timings.LastWallFrameMs > 0.001f
                ? timings.LastWallFrameMs
                : (timings.WallFrameMs > 0.001f ? timings.WallFrameMs : (timings.LastFrameMs > 0.001f ? timings.LastFrameMs : timings.FrameMs));
            float tickMs = timings.LastTotalTickMs > 0.001f ? timings.LastTotalTickMs : timings.TotalTickMs;
            float uiRenderMs = timings.LastUiRenderMs;
            float uiUploadMs = timings.LastUiUploadMs;
            float overlayTotalMs = timings.LastScreenOverlayDrawMs;
            float overlayPaintMs = MathF.Max(0f, timings.LastScreenOverlayPaintMs - uiRenderMs);
            return $"Runtime: wall {frameMs:F2} ms | tick {tickMs:F2} ms | sim {timings.LastSimulationMs:F2} ms | presentation {timings.LastPresentationMs:F2} ms | cull {timings.LastCameraCullingMs:F2} ms | hudProj {timings.LastWorldHudProjectionMs:F2} ms | primitive {timings.PrimitiveRenderMs:F2} ms | uiRender {uiRenderMs:F2} ms | uiUpload {uiUploadMs:F2} ms | overlayPaint {overlayPaintMs:F2} ms | overlayComposite {timings.LastScreenOverlayCompositeMs:F2} ms | overlayFinal {timings.LastScreenOverlayFinalDrawMs:F2} ms | overlayTotal {overlayTotalMs:F2} ms | world HUD {renderMetrics.WorldHudBarCount}/{renderMetrics.WorldHudTextCount} | screen HUD {renderMetrics.ScreenHudBarCount}/{renderMetrics.ScreenHudTextCount} @ blacksmiths {totalBlacksmiths}";
        }

        private string BuildCapacitySummary(in CapacityMetrics metrics, int totalBlacksmiths)
        {
            int scatterUiMax = ComputeScatterCapacityMax(metrics, _activeEngine != null && UsesCleanHudTextScatter(_activeEngine));

            return $"Capacity: performers {metrics.PerformerActive}/{metrics.PerformerCapacity}, events {metrics.PresentationEventCount}/{metrics.PresentationEventCapacity}, primitives {metrics.PrimitiveCount}/{metrics.PrimitiveCapacity}, world HUD {metrics.WorldHudCount}/{metrics.WorldHudCapacity}, screen HUD {metrics.ScreenHudCount}/{metrics.ScreenHudCapacity}, skinned {metrics.SkinnedCount}/{metrics.SkinnedCapacity}, spline {metrics.RoadSplineCount}/{metrics.RoadSplineCapacity}, decal {metrics.GroundOverlayCount}/{metrics.GroundOverlayCapacity}, spawnQ {metrics.SpawnQueueCount}/{metrics.SpawnQueueCapacity} | UI max {scatterUiMax} | requested {totalBlacksmiths}";
        }

        private static int ComputeScatterCapacityMax(in CapacityMetrics metrics, bool cleanHudTextScatter)
        {
            int performerPerBlacksmith = cleanHudTextScatter ? BenchmarkHudTextPerformerCountPerBlacksmith : PerformerCountPerBlacksmith;
            int primitivePerBlacksmith = cleanHudTextScatter ? BenchmarkHudTextPrimitiveCountPerBlacksmith : PrimitiveCountPerBlacksmith;
            int worldHudPerBlacksmith = cleanHudTextScatter ? BenchmarkHudTextWorldHudCountPerBlacksmith : WorldHudCountPerBlacksmith;
            int screenHudPerBlacksmith = cleanHudTextScatter ? BenchmarkHudTextScreenHudCountPerBlacksmith : ScreenHudCountPerBlacksmith;
            int roadSplinePerBlacksmith = cleanHudTextScatter ? 0 : RoadSplineCountPerBlacksmith;
            int overlayPerBlacksmith = cleanHudTextScatter ? 0 : GroundOverlayCountPerBlacksmith;
            int skinnedPerBlacksmith = cleanHudTextScatter ? 0 : SkinnedCountPerBlacksmith;

            int performerBound = ResolveCapacityBound(metrics.PerformerCapacity, performerPerBlacksmith);
            int primitiveBound = ResolveCapacityBound(metrics.PrimitiveCapacity, primitivePerBlacksmith);
            int worldHudBound = ResolveCapacityBound(metrics.WorldHudCapacity, worldHudPerBlacksmith);
            int screenHudBound = ResolveCapacityBound(metrics.ScreenHudCapacity, screenHudPerBlacksmith);
            int roadSplineBound = ResolveCapacityBound(metrics.RoadSplineCapacity, roadSplinePerBlacksmith);
            int overlayBound = ResolveCapacityBound(metrics.GroundOverlayCapacity, overlayPerBlacksmith);
            int skinnedBound = ResolveCapacityBound(metrics.SkinnedCapacity, skinnedPerBlacksmith);
            int spawnQueueBound = metrics.SpawnQueueCapacity > 0
                ? Math.Max(ScatterMinTotal, metrics.SpawnQueueCapacity + 1)
                : ScatterMinTotal;

            int lowestBound = performerBound;
            lowestBound = Math.Min(lowestBound, primitiveBound);
            lowestBound = Math.Min(lowestBound, worldHudBound);
            lowestBound = Math.Min(lowestBound, screenHudBound);
            lowestBound = Math.Min(lowestBound, roadSplineBound);
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
            _cachedPanelState = PerformerBlacksmithShowcasePanelState.Empty;
        }

        private void MarkPanelDirty()
        {
            _panelDirty = true;
            _panelRefreshCooldown = 0f;
        }

        private static int ReadEnvInt(string key, int defaultValue)
        {
            string? raw = Environment.GetEnvironmentVariable(key);
            return int.TryParse(raw, out int parsed) && parsed > 0
                ? parsed
                : defaultValue;
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
                        $"metadata.{section}.{key} must be > 0 for the dynamic worker benchmark production path.");
                }

                return value;
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    $"metadata.{section}.{key} must be an integer for the dynamic worker benchmark production path.",
                    ex);
            }
            catch (InvalidOperationException ex) when (!ex.Message.Contains("metadata.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"metadata.{section}.{key} must be an integer for the dynamic worker benchmark production path.",
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
                        $"metadata.{section}.{key} must be finite for the dynamic worker benchmark production path.");
                }

                return value;
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    $"metadata.{section}.{key} must be a number for the dynamic worker benchmark production path.",
                    ex);
            }
            catch (InvalidOperationException ex) when (!ex.Message.Contains("metadata.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"metadata.{section}.{key} must be a number for the dynamic worker benchmark production path.",
                    ex);
            }
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
                    $"Map '{mapId}' must declare metadata.{section}.{key} for the dynamic worker benchmark production path.");
            }

            return valueNode;
        }

        private static int ReadOptionalMapMetadataInt(GameEngine engine, string section, string key, int defaultValue)
        {
            if (!TryReadMapMetadataValue(engine, section, key, out JsonNode? valueNode) ||
                valueNode == null)
            {
                return defaultValue;
            }

            try
            {
                return valueNode.GetValue<int>();
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    $"metadata.{section}.{key} must be an integer for the minimap marker showcase.",
                    ex);
            }
            catch (InvalidOperationException ex) when (!ex.Message.Contains("metadata.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"metadata.{section}.{key} must be an integer for the minimap marker showcase.",
                    ex);
            }
        }

        private static float ReadOptionalMapMetadataFloat(GameEngine engine, string section, string key, float defaultValue)
        {
            if (!TryReadMapMetadataValue(engine, section, key, out JsonNode? valueNode) ||
                valueNode == null)
            {
                return defaultValue;
            }

            try
            {
                float value = valueNode.GetValue<float>();
                if (!float.IsFinite(value))
                {
                    throw new InvalidOperationException(
                        $"metadata.{section}.{key} must be finite for the minimap marker showcase.");
                }

                return value;
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    $"metadata.{section}.{key} must be a number for the minimap marker showcase.",
                    ex);
            }
            catch (InvalidOperationException ex) when (!ex.Message.Contains("metadata.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"metadata.{section}.{key} must be a number for the minimap marker showcase.",
                    ex);
            }
        }

        private static bool TryReadMapMetadataValue(GameEngine engine, string section, string key, out JsonNode? valueNode)
        {
            valueNode = null;
            return engine.CurrentMapSession?.MapConfig?.Metadata != null &&
                   engine.CurrentMapSession.MapConfig.Metadata.TryGetValue(section, out JsonNode? sectionNode) &&
                   sectionNode is JsonObject sectionObject &&
                   sectionObject.TryGetPropertyValue(key, out valueNode);
        }

        private static float ResolveAutoScatterMinRadiusCm()
        {
            return ReadEnvFloat(
                AutoScatterMinRadiusEnvKey,
                PerformerBlacksmithScatterPlanner.DefaultMinRadiusCm);
        }

        private static float ResolveAutoScatterMaxRadiusCm()
        {
            float minRadius = ResolveAutoScatterMinRadiusCm();
            float defaultMax = MathF.Max(
                minRadius + 1f,
                PerformerBlacksmithScatterPlanner.DefaultMaxRadiusCm);
            float maxRadius = ReadEnvFloat(AutoScatterMaxRadiusEnvKey, defaultMax);
            return MathF.Max(minRadius + 1f, maxRadius);
        }

        private static float ReadEnvFloat(string key, float defaultValue)
        {
            string? raw = Environment.GetEnvironmentVariable(key);
            return float.TryParse(raw, out float parsed) && parsed > 0f
                ? parsed
                : defaultValue;
        }

        private static bool ReadEnvBool(string key)
        {
            string? raw = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            return raw.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        private readonly record struct PerformerMetrics(
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
            public static readonly PerformerMetrics Empty = new(
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
                Lines: new[] { "(performer services unavailable)" });
        }

        private readonly record struct RenderMetrics(
            int VisibleWorkshopCount,
            int VisibleChimneyCount,
            int VisibleSmokeCount,
            int VisibleWorkerCount,
            int RoadSplineCount,
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
            int PerformerCapacity,
            int PerformerActive,
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
            int RoadSplineCapacity,
            int RoadSplineCount,
            int GroundOverlayCapacity,
            int GroundOverlayCount,
            int SpawnQueueCapacity,
            int SpawnQueueCount)
        {
            public static readonly CapacityMetrics Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }
    }
}
