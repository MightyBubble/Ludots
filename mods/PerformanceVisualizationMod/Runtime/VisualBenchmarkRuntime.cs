using System;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Knowledge;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using Ludots.UI;
using PerformanceVisualizationMod.UI;
using Ludots.Platform.Abstractions;

namespace PerformanceVisualizationMod.Runtime
{
    public sealed class VisualBenchmarkRuntime
    {
        private static readonly PresentationLocalBounds BenchmarkBounds =
            PresentationLocalBounds.Create(Vector3.Zero, new Vector3(0.425f, 0.425f, 0.425f));

        private static readonly PresentationLodProfile BenchmarkLodProfile =
            new(
                new PresentationLodEntry(maxDistanceCm: 6000f, minScreenCoverage01: 0.06f),
                new PresentationLodEntry(maxDistanceCm: 24000f, minScreenCoverage01: 0.01f),
                new PresentationLodEntry(maxDistanceCm: 90000f, minScreenCoverage01: 0.0025f));

        private const int SpawnBatchSize = 2048;
        private const int AttributeAttachSpawnBatchSize = 128;
        private const int DestroyBatchSize = 2048;
        private const int LiveKnowledgeConfidencePermille = 1000;

        private static readonly QueryDescription ScenarioEntitiesQuery = new QueryDescription()
            .WithAll<MapEntity, Name, PresentationStableId, PresentationLocalBounds>();

        private static readonly QueryDescription VisibleScenarioEntitiesQuery = new QueryDescription()
            .WithAll<MapEntity, Name, PresentationStableId, PresentationLocalBounds, CullState>();

        private readonly IModContext _context;
        private readonly VisualBenchmarkPanelController _panelController;
        private string? _activeMapId;
        private string _status = "Ready. Choose a benchmark profile to spawn map-scoped visuals.";
        private VisualBenchmarkScenarioConfig _scenario = VisualBenchmarkScenarioConfig.Small;
        private VisualBenchmarkScenarioConfig _requestedScenario = VisualBenchmarkScenarioConfig.Small;
        private BenchmarkPipelinePhase _pipelinePhase;
        private bool _hasRequestedScenario;
        private bool _clearRequested;
        private int _spawnCursor;
        private int _spawnedCount;
        private int _lastVisibleCount;
        private int _lastHudCount;
        private int _lastHudDropped;
        private int _screenHudCount;
        private int _screenHudDropped;
        private int _healthAttributeId;
        private int _benchmarkCubeDefinitionId;
        private Entity _audienceViewer = Entity.Null;
        private uint _directHudRandomState = 0x5EED_1000u;

        public VisualBenchmarkRuntime(IModContext context)
        {
            _context = context;
            _panelController = new VisualBenchmarkPanelController(this);
        }

        public bool IsActive => VisualBenchmarkIds.IsShowcaseMap(_activeMapId);
        public string Status => _status;

        private enum BenchmarkPipelinePhase : byte
        {
            Idle = 0,
            Clearing = 1,
            Spawning = 2,
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            string? mapId = engine.CurrentMapSession?.MapId.Value;
            if (!VisualBenchmarkIds.IsShowcaseMap(mapId))
            {
                Unbind(engine);
                return Task.CompletedTask;
            }

            _activeMapId = mapId;
            if (string.Equals(mapId, VisualBenchmarkIds.Hud100kMapId, StringComparison.OrdinalIgnoreCase))
            {
                _scenario = VisualBenchmarkScenarioConfig.Hud100k;
                _requestedScenario = _scenario;
                _hasRequestedScenario = false;
                _clearRequested = false;
                _pipelinePhase = BenchmarkPipelinePhase.Idle;
                _status = "HUD 100K direct-screen benchmark active.";
            }
            else if (string.Equals(mapId, VisualBenchmarkIds.SkiaHotpathMapId, StringComparison.OrdinalIgnoreCase))
            {
                _scenario = VisualBenchmarkScenarioConfig.SkiaHotpath;
                _requestedScenario = _scenario;
                _hasRequestedScenario = false;
                _clearRequested = false;
                _pipelinePhase = BenchmarkPipelinePhase.Idle;
                _status = "Skia hotpath direct-screen benchmark active.";
            }

            ConfigureCamera(engine, _scenario);
            RefreshPanel(engine);
            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            string mapId = context.Get(CoreServiceKeys.MapId).Value;
            if (VisualBenchmarkIds.IsShowcaseMap(mapId))
            {
                Unbind(engine);
            }

            return Task.CompletedTask;
        }

        public void RefreshPanel(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            if (!IsActive)
            {
                _panelController.ClearIfOwned(root);
                return;
            }

            SampleMetrics(engine);
            _panelController.MountOrSync(root, engine, BuildPanelState(engine));
        }

        public void Advance(GameEngine engine)
        {
            if (engine == null || !IsActive || engine.CurrentMapSession == null)
            {
                return;
            }

            if (_scenario.WorkloadKind == VisualBenchmarkWorkloadKind.DirectScreenHud100k)
            {
                FillDirectHud100k(engine);
                return;
            }

            if (_scenario.WorkloadKind == VisualBenchmarkWorkloadKind.DirectScreenHudHotpath)
            {
                FillDirectSkiaHotpath(engine);
                return;
            }

            if (_clearRequested)
            {
                if (GetScenarioEntityCount(engine) > 0)
                {
                    int destroyBatch = _scenario.AttachHealthAttributes ? AttributeAttachSpawnBatchSize : DestroyBatchSize;
                    int marked = MarkScenarioEntitiesForDestroy(engine, destroyBatch);
                    _pipelinePhase = BenchmarkPipelinePhase.Clearing;
                    _status = $"Clearing benchmark actors in batches ({marked:N0} this frame).";
                    return;
                }

                _clearRequested = false;
                _pipelinePhase = BenchmarkPipelinePhase.Idle;
                _spawnCursor = 0;
                _spawnedCount = 0;
                _status = "Scenario cleared. Map-scoped benchmark actors destroyed.";
                return;
            }

            if (_pipelinePhase == BenchmarkPipelinePhase.Clearing)
            {
                if (GetScenarioEntityCount(engine) > 0)
                {
                    int destroyBatch = _scenario.AttachHealthAttributes ? AttributeAttachSpawnBatchSize : DestroyBatchSize;
                    int marked = MarkScenarioEntitiesForDestroy(engine, destroyBatch);
                    _status = $"Clearing previous scenario for {_requestedScenario.Label} ({marked:N0} queued this frame).";
                    return;
                }

                if (_hasRequestedScenario)
                {
                    _pipelinePhase = BenchmarkPipelinePhase.Spawning;
                    _spawnCursor = 0;
                    _status = $"Previous scenario drained. Starting {_requestedScenario.Label}.";
                }
                else
                {
                    _pipelinePhase = BenchmarkPipelinePhase.Idle;
                    _status = "Scenario cleared. Map-scoped benchmark actors destroyed.";
                    return;
                }
            }

            if (!_hasRequestedScenario)
            {
                return;
            }

            if (_pipelinePhase == BenchmarkPipelinePhase.Spawning && _spawnCursor < _requestedScenario.EntityCount)
            {
                int batch = _requestedScenario.AttachHealthAttributes ? AttributeAttachSpawnBatchSize : SpawnBatchSize;
                int spawned = SpawnScenarioBatch(engine, _requestedScenario, _spawnCursor, batch);
                _spawnCursor += spawned;
                _scenario = _requestedScenario;
                _status = $"Spawning {_requestedScenario.Label}: {_spawnCursor:N0}/{_requestedScenario.EntityCount:N0}.";

                if (_spawnCursor >= _requestedScenario.EntityCount)
                {
                    _hasRequestedScenario = false;
                    _pipelinePhase = BenchmarkPipelinePhase.Idle;
                    _status = _requestedScenario.AttachHealthAttributes
                        ? $"Spawned {_requestedScenario.EntityCount:N0} map-scoped visual actors with presenter-driven health bars."
                        : $"Spawned {_requestedScenario.EntityCount:N0} map-scoped visual actors for pure visual stress.";
                    _context.Log($"[PerformanceVisualizationMod] {_status}");
                }
            }
        }

        public bool TryRunScenario(GameEngine engine, string scenarioKey)
        {
            if (engine == null || !IsActive || engine.CurrentMapSession == null)
            {
                return false;
            }

            _requestedScenario = VisualBenchmarkScenarioConfig.FromKey(scenarioKey);
            _hasRequestedScenario = true;
            _clearRequested = false;
            _spawnCursor = 0;
            _pipelinePhase = GetScenarioEntityCount(engine) > 0
                ? BenchmarkPipelinePhase.Clearing
                : BenchmarkPipelinePhase.Spawning;
            _status = $"Queued {_requestedScenario.Label}. Runtime will migrate the showcase in formal lifecycle batches.";
            ConfigureCamera(engine, _requestedScenario);
            return true;
        }

        public bool TryClearScenario(GameEngine engine)
        {
            if (engine == null || !IsActive)
            {
                return false;
            }

            _hasRequestedScenario = false;
            _clearRequested = true;
            _spawnCursor = 0;
            _pipelinePhase = GetScenarioEntityCount(engine) > 0
                ? BenchmarkPipelinePhase.Clearing
                : BenchmarkPipelinePhase.Idle;
            _status = "Queued scenario clear. Runtime will drain map-scoped benchmark actors through lifecycle destroy events.";
            return true;
        }

        public bool TryResetCamera(GameEngine engine)
        {
            if (engine == null || !IsActive)
            {
                return false;
            }

            ConfigureCamera(engine, _scenario);
            _status = $"Camera reset for {_scenario.Label}.";
            return true;
        }

        internal VisualBenchmarkPanelState BuildPanelState(GameEngine engine)
        {
            Vector2 cameraTarget = ClientLocalSeatAccess.ResolveAuthorityCamera(engine).State.TargetCm;
            return new VisualBenchmarkPanelState(
                Title: "Visual Benchmark Showcase",
                Status: _status,
                Scenario: $"Scenario: {_scenario.Label} | Spawned: {_spawnedCount:N0}",
                Metrics: $"Entities: {_spawnedCount:N0} | Visible: {_lastVisibleCount:N0} | WorldHUD Bars: {_lastHudCount:N0} | ScreenHUD Items: {_screenHudCount:N0} | Drops W:{_lastHudDropped:N0}/S:{_screenHudDropped:N0}",
                Camera: $"Camera target ({cameraTarget.X:0},{cameraTarget.Y:0}) | Distance {ClientLocalSeatAccess.ResolveAuthorityCamera(engine).State.DistanceCm:0}",
                Hint: $"Pipeline: {_pipelinePhase} | Run 2K/8K to validate presenter HUD, 32K for pure visual stress, HUD100K/SkiaHotpath for direct screen-HUD overlay throughput.",
                Actions: new[]
                {
                    VisualBenchmarkScenarioConfig.Small.Label,
                    VisualBenchmarkScenarioConfig.Medium.Label,
                    VisualBenchmarkScenarioConfig.Large.Label,
                    VisualBenchmarkScenarioConfig.Hud100k.Label,
                    VisualBenchmarkScenarioConfig.SkiaHotpath.Label,
                    "Clear scenario",
                });
        }

        private void EnsureAttributeIds()
        {
            if (_healthAttributeId == 0)
            {
                _healthAttributeId = AttributeRegistry.Register("Health");
            }
        }

        private void SampleMetrics(GameEngine engine)
        {
            _spawnedCount = 0;
            _lastVisibleCount = 0;
            _screenHudCount = 0;
            _screenHudDropped = 0;

            MapId currentMapId = engine.CurrentMapSession?.MapId ?? default;
            engine.World.Query(in ScenarioEntitiesQuery, (ref MapEntity mapEntity, ref Name name, ref PresentationStableId __, ref PresentationLocalBounds ___) =>
            {
                if (!MatchesScenarioEntity(mapEntity, name, currentMapId))
                {
                    return;
                }

                _spawnedCount++;
            });

            engine.World.Query(in VisibleScenarioEntitiesQuery, (ref MapEntity mapEntity, ref Name name, ref PresentationStableId __, ref PresentationLocalBounds ___, ref CullState cull) =>
            {
                if (!MatchesScenarioEntity(mapEntity, name, currentMapId) || !cull.IsVisible)
                {
                    return;
                }

                _lastVisibleCount++;
            });

            WorldHudBatchBuffer? hud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            _lastHudCount = hud?.Count ?? 0;
            _lastHudDropped = hud?.DroppedSinceClear ?? 0;
            ScreenHudBatchBuffer? screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer);
            _screenHudCount = screenHud?.Count ?? 0;
            _screenHudDropped = screenHud?.DroppedSinceClear ?? 0;
        }

        private void ConfigureCamera(GameEngine engine, VisualBenchmarkScenarioConfig scenario)
        {
            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
            {
                Id = "TopDown"
            });

            engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
            {
                VirtualCameraId = "TopDown",
                TargetCm = Vector2.Zero,
                DistanceCm = scenario.CameraDistanceCm,
                Pitch = 60f
            });
        }

        private int SpawnScenarioBatch(GameEngine engine, VisualBenchmarkScenarioConfig scenario, int startIndex, int batchSize)
        {
            if (scenario.AttachHealthAttributes)
            {
                EnsureAttributeIds();
            }

            World world = engine.World;
            Entity audienceViewer = scenario.AttachHealthAttributes
                ? EnsureBenchmarkAudience(engine)
                : Entity.Null;
            KnowledgeProjectionStore? knowledge = scenario.AttachHealthAttributes
                ? RequireKnowledgeStore(engine)
                : null;
            KnowledgeIdMask256 healthAttributeMask = scenario.AttachHealthAttributes
                ? KnowledgeIdMask256.Empty.WithId(_healthAttributeId)
                : KnowledgeIdMask256.Empty;
            int observedTick = scenario.AttachHealthAttributes
                ? KnowledgeProjectionConsumer.ResolveCurrentTick(engine.GlobalContext)
                : 0;
            var emptyKnowledgeMask = KnowledgeIdMask256.Empty;
            var stableIds = engine.GetService(CoreServiceKeys.PresentationStableIdAllocator)
                ?? throw new InvalidOperationException("PresentationStableIdAllocator service is missing.");
            var commands = engine.GetService(CoreServiceKeys.PresenterCommandBuffer)
                ?? throw new InvalidOperationException("PresenterCommandBuffer service is missing.");
            var presenters = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
                ?? throw new InvalidOperationException("PresenterDefinitionRegistry service is missing.");
            int definitionId = ResolveBenchmarkCubeDefinitionId(presenters);

            MapId mapId = engine.CurrentMapSession?.MapId ?? default;
            int centerOffsetX = (scenario.Columns - 1) * scenario.SpacingCm / 2;
            int centerOffsetY = (scenario.Rows - 1) * scenario.SpacingCm / 2;
            int endExclusive = Math.Min(startIndex + batchSize, scenario.EntityCount);

            var archetype = new ComponentType[]
            {
                typeof(MapEntity),
                typeof(Name),
                typeof(Position),
                typeof(WorldPositionCm),
                typeof(PreviousWorldPositionCm),
                typeof(VisualTransform),
                typeof(PresentationStableId),
                typeof(PresentationLocalBounds),
                typeof(PresentationLodProfile),
                typeof(CullState),
            };

            for (int i = startIndex; i < endExclusive; i++)
            {
                int row = i / scenario.Columns;
                int col = i % scenario.Columns;
                int xCm = (col * scenario.SpacingCm) - centerOffsetX;
                int yCm = (row * scenario.SpacingCm) - centerOffsetY;

                Entity entity = world.Create(archetype);
                world.Set(entity, new MapEntity { MapId = mapId });
                world.Set(entity, new Name { Value = $"{VisualBenchmarkIds.ScenarioLabel}.{i:000000}" });
                world.Set(entity, new Position { GridPos = new IntVector2(xCm / 100, yCm / 100) });

                var worldPos = WorldPositionCm.FromCm(xCm, yCm);
                world.Set(entity, worldPos);
                world.Set(entity, new PreviousWorldPositionCm { Value = worldPos.Value });
                world.Set(entity, VisualTransform.Default);
                world.Set(entity, new PresentationStableId { Value = stableIds.Allocate() });
                world.Set(entity, BenchmarkBounds);
                world.Set(entity, BenchmarkLodProfile);
                world.Set(entity, new CullState
                {
                    IsVisible = true,
                    LOD = LODLevel.High,
                });

                if (!commands.TryAdd(new PresenterCommand
                    {
                        CommandKind = PresenterCommandKind.CreatePresenter,
                        PresenterDefinitionId = definitionId,
                        ScopeTag = ResolveScenarioScopeId(entity),
                        Source = entity,
                        AnchorKind = PresentationAnchorKind.Entity,
                    }))
                {
                    throw new InvalidOperationException("PerformanceVisualizationMod failed to queue benchmark presenter creation.");
                }

                if (scenario.AttachHealthAttributes)
                {
                    world.Add(entity, new AttributeBuffer());
                    world.Add(entity, new DirtyFlags());
                    TagOps tagOps = engine.GetService(CoreServiceKeys.TagOps)
                        ?? throw new InvalidOperationException("PerformanceVisualizationMod requires TagOps.");
                    AttributeMutationOps.SetBase(world, entity, _healthAttributeId, 100f, tagOps);
                    AttributeMutationOps.SetCurrent(world, entity, _healthAttributeId, 40f + (i % 60), tagOps);

                    knowledge!.Upsert(
                        audienceViewer,
                        entity,
                        new KnowledgeDisclosureRecord(
                            KnowledgePresence.LiveVisible,
                            KnowledgePositionAccess.Live,
                            healthAttributeMask,
                            emptyKnowledgeMask,
                            emptyKnowledgeMask,
                            audienceViewer,
                            observedTick,
                            expiryTick: 0,
                            confidencePermille: LiveKnowledgeConfidencePermille,
                            revision: 0));
                }
            }

            return endExclusive - startIndex;
        }

        private int MarkScenarioEntitiesForDestroy(GameEngine engine, int batchSize)
        {
            MapId currentMapId = engine.CurrentMapSession?.MapId ?? default;
            if (string.IsNullOrWhiteSpace(currentMapId.Value))
            {
                return 0;
            }

            int marked = 0;
            engine.World.Query(in ScenarioEntitiesQuery, (Entity entity, ref MapEntity mapEntity, ref Name name, ref PresentationStableId __, ref PresentationLocalBounds ___) =>
            {
                if (marked >= batchSize || !MatchesScenarioEntity(mapEntity, name, currentMapId))
                {
                    return;
                }

                if (engine.World.Has<PresentationDestroyPending>(entity))
                {
                    return;
                }

                RemoveBenchmarkKnowledge(engine, entity);
                engine.World.Add(entity, new PresentationDestroyPending());
                marked++;
            });

            return marked;
        }

        private void Unbind(GameEngine engine)
        {
            _activeMapId = null;
            _hasRequestedScenario = false;
            _clearRequested = false;
            _spawnCursor = 0;
            _pipelinePhase = BenchmarkPipelinePhase.Idle;
            _spawnedCount = 0;
            _lastVisibleCount = 0;
            _lastHudCount = 0;
            _lastHudDropped = 0;
            _screenHudCount = 0;
            _screenHudDropped = 0;
            _status = "Ready. Choose a benchmark profile to spawn map-scoped visuals.";

            if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
            {
                _panelController.ClearIfOwned(root);
            }

            ClearOwnedBenchmarkAudience(engine);
        }

        private Entity EnsureBenchmarkAudience(GameEngine engine)
        {
            if (_audienceViewer != Entity.Null && engine.World.IsAlive(_audienceViewer))
            {
                return _audienceViewer;
            }

            Entity viewer = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            if (!engine.World.IsAlive(viewer))
            {
                throw new InvalidOperationException(
                    "PerformanceVisualizationMod requires a live sole ClientLocalSeat possession from launchContext.localSeats / startupLocalSeats.");
            }

            _audienceViewer = viewer;
            return viewer;
        }

        private static KnowledgeProjectionStore RequireKnowledgeStore(GameEngine engine)
        {
            return engine.GetService(CoreServiceKeys.KnowledgeProjectionStore)
                ?? throw new InvalidOperationException("PerformanceVisualizationMod requires KnowledgeProjectionStore before publishing benchmark HUD visibility.");
        }

        private void RemoveBenchmarkKnowledge(GameEngine engine, Entity target)
        {
            if (_audienceViewer == Entity.Null ||
                !engine.World.IsAlive(_audienceViewer) ||
                engine.GetService(CoreServiceKeys.KnowledgeProjectionStore) is not KnowledgeProjectionStore knowledge)
            {
                return;
            }

            knowledge.Remove(_audienceViewer, target);
        }

        private void ClearOwnedBenchmarkAudience(GameEngine engine)
        {
            _ = engine;
            _audienceViewer = Entity.Null;
        }

        private static bool MatchesScenarioEntity(in MapEntity mapEntity, in Name name, in MapId currentMapId)
        {
            return mapEntity.MapId == currentMapId &&
                   name.Value != null &&
                   name.Value.StartsWith(VisualBenchmarkIds.ScenarioLabel, StringComparison.Ordinal);
        }

        private int GetScenarioEntityCount(GameEngine engine)
        {
            MapId currentMapId = engine.CurrentMapSession?.MapId ?? default;
            int count = 0;
            engine.World.Query(in ScenarioEntitiesQuery, (ref MapEntity mapEntity, ref Name name, ref PresentationStableId __, ref PresentationLocalBounds ___) =>
            {
                if (MatchesScenarioEntity(mapEntity, name, currentMapId))
                {
                    count++;
                }
            });
            return count;
        }

        private int ResolveBenchmarkCubeDefinitionId(PresenterDefinitionRegistry presenters)
        {
            if (_benchmarkCubeDefinitionId > 0)
            {
                return _benchmarkCubeDefinitionId;
            }

            _benchmarkCubeDefinitionId = presenters.GetId(VisualBenchmarkIds.BenchmarkCubePresenterId);
            if (_benchmarkCubeDefinitionId <= 0)
            {
                throw new InvalidOperationException(
                    $"Presenter '{VisualBenchmarkIds.BenchmarkCubePresenterId}' is required by PerformanceVisualizationMod.");
            }

            return _benchmarkCubeDefinitionId;
        }

        private static int ResolveScenarioScopeId(Entity entity)
        {
            int scopeId = HashCode.Combine(VisualBenchmarkIds.ScenarioLabel, entity.Id, entity.WorldId, entity.Version) & int.MaxValue;
            return scopeId == 0 ? 1 : scopeId;
        }

        private void FillDirectHud100k(GameEngine engine)
        {
            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)
                ?? throw new InvalidOperationException("PresentationScreenHudBuffer service is missing.");
            screenHud.Clear();

            const int columns = 400;
            const int maxHealth = 1000;
            const float baseX = 4f;
            const float baseY = 6f;
            const float colSpacing = 4.1f;
            const float rowSpacing = 3.6f;
            const float barWidth = 3f;
            const float barHeight = 2f;
            const int fontSize = 8;
            Vector4 barBackground = new(0.10f, 0.12f, 0.16f, 0.86f);
            Vector4 barForeground = new(0.16f, 0.82f, 0.36f, 0.96f);
            Vector4 textColor = new(0.94f, 0.96f, 0.88f, 1f);

            for (int i = 0; i < VisualBenchmarkScenarioConfig.Hud100k.EntityCount; i++)
            {
                int currentHealth = (int)(NextRandom(ref _directHudRandomState) % (uint)(maxHealth + 1));
                int row = i / columns;
                int column = i % columns;
                float x = baseX + (column * colSpacing);
                float y = baseY + (row * rowSpacing);
                float fill = currentHealth / (float)maxHealth;

                screenHud.TryAddBar(new ScreenHudBarItem
                {
                    StableId = HudItemIdentity.ComposeStableId(i + 1, WorldHudItemKind.Bar, discriminator: _healthAttributeId == 0 ? 1 : _healthAttributeId),
                    DirtySerial = HudItemIdentity.ComposeBarDirtySerial(barWidth, barHeight, fill, barBackground, barForeground),
                    ScreenX = x,
                    ScreenY = y,
                    Width = barWidth,
                    Height = barHeight,
                    Value0 = fill,
                    Color0 = barBackground,
                    Color1 = barForeground,
                });

                screenHud.TryAddText(new ScreenHudTextItem
                {
                    StableId = HudItemIdentity.ComposeStableId(i + 1, WorldHudItemKind.Text, discriminator: _healthAttributeId == 0 ? 1 : _healthAttributeId),
                    DirtySerial = HudItemIdentity.ComposeTextDirtySerial(fontSize, 0, (int)WorldHudValueMode.AttributeCurrentOverBase, currentHealth, maxHealth, textColor, default),
                    ScreenX = x,
                    ScreenY = y - 7f,
                    FontSize = fontSize,
                    Color0 = textColor,
                    Value0 = currentHealth,
                    Value1 = maxHealth,
                    Id1 = (int)WorldHudValueMode.AttributeCurrentOverBase,
                });
            }

            _spawnedCount = VisualBenchmarkScenarioConfig.Hud100k.EntityCount;
            _lastVisibleCount = VisualBenchmarkScenarioConfig.Hud100k.EntityCount;
            _status = "HUD 100K direct-screen benchmark active.";
        }

        private void FillDirectSkiaHotpath(GameEngine engine)
        {
            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)
                ?? throw new InvalidOperationException("PresentationScreenHudBuffer service is missing.");
            screenHud.Clear();

            const int visibleEntityCount = 10240;
            const int columns = 64;
            const float baseBarX = 14f;
            const float baseBarY = 12f;
            const float colSpacing = 18.5f;
            const float rowSpacing = 4.25f;
            const float barWidth = 16f;
            const float barHeight = 3f;
            const int fontSize = 11;
            Vector4 barBackground = new(0.10f, 0.12f, 0.16f, 0.88f);
            Vector4 barForeground = new(0.15f, 0.82f, 0.46f, 0.96f);
            Vector4 textColor = new(0.96f, 0.96f, 0.90f, 1f);

            int valueOffset = (int)(NextRandom(ref _directHudRandomState) % 4096u);
            for (int i = 0; i < visibleEntityCount; i++)
            {
                int row = i / columns;
                int column = i % columns;
                float x = baseBarX + (column * colSpacing);
                float y = baseBarY + (row * rowSpacing);
                float fill = 0.18f + (((i + valueOffset) % 12) * 0.06f);
                if (fill > 0.98f)
                {
                    fill = 0.98f;
                }

                int numericValue = 100 + ((i + valueOffset) % 900);
                screenHud.TryAddBar(new ScreenHudBarItem
                {
                    StableId = HudItemIdentity.ComposeStableId(i + 1, WorldHudItemKind.Bar, discriminator: 1),
                    DirtySerial = HudItemIdentity.ComposeBarDirtySerial(barWidth, barHeight, fill, barBackground, barForeground),
                    ScreenX = x,
                    ScreenY = y,
                    Width = barWidth,
                    Height = barHeight,
                    Value0 = fill,
                    Color0 = barBackground,
                    Color1 = barForeground,
                });

                screenHud.TryAddText(new ScreenHudTextItem
                {
                    StableId = HudItemIdentity.ComposeStableId(i + 1, WorldHudItemKind.Text, discriminator: 2),
                    DirtySerial = HudItemIdentity.ComposeTextDirtySerial(fontSize, 0, (int)WorldHudValueMode.AttributeCurrent, numericValue, 0f, textColor, default),
                    ScreenX = x + 1f,
                    ScreenY = y - 9f,
                    FontSize = fontSize,
                    Color0 = textColor,
                    Value0 = numericValue,
                    Id1 = (int)WorldHudValueMode.AttributeCurrent,
                });
            }

            _spawnedCount = visibleEntityCount;
            _lastVisibleCount = visibleEntityCount;
            _status = "Skia hotpath direct-screen benchmark active.";
        }

        private static uint NextRandom(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }
    }
}
