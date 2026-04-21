using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
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
using Ludots.Core.Scripting;
using Ludots.UI;
using PerformerBlacksmithShowcaseMod.UI;

namespace PerformerBlacksmithShowcaseMod.Runtime
{
    internal sealed class PerformerBlacksmithShowcaseRuntime
    {
        private const float RootSearchRadiusCm = 50f;
        private const int ScatterMinTotal = 1;
        private const int ScatterUiHardMaxTotal = 300_000;
        private const int ScatterDefaultTarget = 30_000;
        private const int BenchmarkSampleFrames = 60;
        private const int PerformerCountPerBlacksmith = 9;
        private const int PrimitiveCountPerBlacksmith = 3;
        private const int WorldHudCountPerBlacksmith = 2;
        private const int ScreenHudCountPerBlacksmith = 2;
        private const int RoadSplineCountPerBlacksmith = 1;
        private const int GroundOverlayCountPerBlacksmith = 1;
        private const int SkinnedCountPerBlacksmith = 1;
        private const string AutoScatterTotalEnvKey = "LUDOTS_BLACKSMITH_AUTO_SCATTER_TOTAL";
        private const float PanelRefreshIntervalSeconds = 0.25f;

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
        private readonly double[] _tickSamplesMs = new double[BenchmarkSampleFrames];
        private int _tickSampleCursor;
        private int _tickSampleCount;
        private double _tickSampleSumMs;
        private double _tickSampleMaxMs;
        private double _lastFrameTickMs;
        private bool _autoScatterApplied;
        private float _panelRefreshCooldown;
        private bool _panelDirty = true;
        private PerformerBlacksmithShowcasePanelState _cachedPanelState = PerformerBlacksmithShowcasePanelState.Empty;
        private static readonly string[] RegionNames = { "NORTH", "SOUTH" };

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
            _buildingEntity = FindRootBuildingEntity(engine);
            _autoScatterApplied = CountBlacksmithEntities(engine, out _) > 1;
            TryApplyAutoScatter(engine);
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

            long start = Stopwatch.GetTimestamp();
            _activeEngine = engine;
            RefreshRootEntity(engine);
            _panelRefreshCooldown = MathF.Max(0f, _panelRefreshCooldown - (1f / 60f));
            if (_changeFlashTimer > 0f)
            {
                _changeFlashTimer = MathF.Max(0f, _changeFlashTimer - 0.016f);
                _panelDirty = true;
            }

            RefreshPanel(engine);
            ObserveTickSample((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
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

        internal void ApplyScatterLayout(int totalBuildings)
        {
            GameEngine engine = RequireShowcaseEngine();
            int clampedTotal = ClampScatterTotal(engine, totalBuildings);
            ClearScatterBuildings(engine);
            _scatterRequestedTotal = clampedTotal;
            _scatterTargetTotal = clampedTotal;
            _lastQueuedScatterExtras = 0;

            if (_destroyed)
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
            int queued = PerformerBlacksmithScatterPlanner.EnqueueScatter(
                spawnQueue,
                engine.CurrentMapSession?.MapId ?? default,
                clampedTotal - 1,
                seed);
            _lastQueuedScatterExtras = queued;
            Flash($"Scatter total => {clampedTotal} (seed {seed}, queued {queued})");
        }

        internal void AdjustScatterTarget(int delta)
        {
            GameEngine engine = RequireShowcaseEngine();
            _scatterTargetTotal = ClampScatterTotal(engine, _scatterTargetTotal + delta);
            Flash($"Scatter target => {_scatterTargetTotal}");
        }

        internal void SetScatterTargetFromRatio(float ratio)
        {
            GameEngine engine = RequireShowcaseEngine();
            float clampedRatio = Math.Clamp(ratio, 0f, 1f);
            int scatterUiMax = ResolveScatterUiMax(engine);
            int value = ScatterMinTotal + (int)MathF.Round(clampedRatio * (scatterUiMax - ScatterMinTotal));
            _scatterTargetTotal = ClampScatterTotal(engine, value);
            Flash($"Scatter target => {_scatterTargetTotal}");
        }

        internal void ApplyScatterTarget()
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
            var query = new QueryDescription().WithAll<Name, WorldPositionCm>();
            float rootDistanceSq = RootSearchRadiusCm * RootSearchRadiusCm;
            engine.World.Query(in query, (Entity entity, ref Name name, ref WorldPositionCm position) =>
            {
                if (!string.Equals(name.Value, PerformerBlacksmithShowcaseIds.EntityName, StringComparison.OrdinalIgnoreCase))
                {
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

            if (_panelDirty || _panelRefreshCooldown <= 0f)
            {
                _cachedPanelState = BuildPanelState(engine, root);
                _panelDirty = false;
                _panelRefreshCooldown = PanelRefreshIntervalSeconds;
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

            PerformerMetrics performerMetrics = CapturePerformerMetrics(engine);
            RenderMetrics renderMetrics = CaptureRenderMetrics(engine);
            CapacityMetrics capacityMetrics = CaptureCapacityMetrics(engine);
            int totalBlacksmiths = CountBlacksmithEntities(engine, out int scatterExtras);
            int durabilityPreset = ResolveDurabilityPreset(engine);
            float durabilityRatio = ResolveDurabilityRatio(engine);
            float durabilityCurrent = ResolveDurabilityCurrent(engine);
            float durabilityBase = ResolveDurabilityBase(engine);
            string regionLabel = RegionNames[Math.Clamp(_regionIndex, 0, RegionNames.Length - 1)];
            string dayNightLabel = _isNight ? "NIGHT" : "DAY";
            string rootLabel = _buildingEntity != Entity.Null && engine.World.IsAlive(_buildingEntity)
                ? (engine.World.TryGet(_buildingEntity, out Name name) ? $"{name.Value}#{_buildingEntity.Id}" : $"Entity#{_buildingEntity.Id}")
                : "(missing)";

            string sceneSummary = _destroyed
                ? "Root OFFLINE | click Respawn Root to rebuild the canonical showcase actor."
                : $"Root ONLINE | Working {(_isWorking ? "ON" : "OFF")} | Region {regionLabel} | Durability {durabilityCurrent:F0}/{durabilityBase:F0} ({durabilityRatio:F2}) | {dayNightLabel}";
            string scatterSummary = $"Scatter total {totalBlacksmiths} (target {_scatterRequestedTotal}, extras {scatterExtras}, queued {_lastQueuedScatterExtras}, seed {_lastScatterSeed})";
            string benchmarkSummary = BuildBenchmarkSummary(totalBlacksmiths, renderMetrics);
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
                $"State: region {_regionIndex} ({regionLabel}) | durability {durabilityRatio:F2} | working {(_isWorking ? 1 : 0)} | night {(_isNight ? 1 : 0)}"
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

            int rootOwnedCount = 0;
            int rootCount = 0;
            int workshopLeftCount = 0;
            int workshopRightCount = 0;
            int chimneyCount = 0;
            int smokeCount = 0;
            int routeSplineCount = 0;
            int decalCount = 0;
            int workerCount = 0;
            int barCount = 0;
            int textCount = 0;
            var lines = new List<string>(12);

            var query = new QueryDescription().WithAll<PerformerState, PerformerParent, PerformerTransformSource>();
            engine.World.Query(in query, (Entity entity, ref PerformerState inst, ref PerformerParent parent, ref PerformerTransformSource transformSource) =>
            {
                if (_buildingEntity == Entity.Null || inst.OwnerEntity != _buildingEntity)
                {
                    return;
                }

                rootOwnedCount++;
                if (inst.DefId == rootId) rootCount++;
                if (inst.DefId == workshopLeftId) workshopLeftCount++;
                if (inst.DefId == workshopRightId) workshopRightCount++;
                if (inst.DefId == chimneyId) chimneyCount++;
                if (inst.DefId == smokeId) smokeCount++;
                if (inst.DefId == routeSplineId) routeSplineCount++;
                if (inst.DefId == decalId) decalCount++;
                if (inst.DefId == workerId) workerCount++;
                if (inst.DefId == barId) barCount++;
                if (inst.DefId == textId) textCount++;

                if (lines.Count >= 12)
                {
                    return;
                }

                string definitionName = definitions.GetName(inst.DefId);
                float durabilityRatio = performers.ResolveFloat(entity, PerformerBlacksmithShowcaseIds.ParamDurability, -1f);
                int workshopState = performers.ResolveInt(entity, PerformerBlacksmithShowcaseIds.ParamWorkshopAssetState, -1);
                lines.Add($"[{entity.Id}] {definitionName} parent={parent.Parent.Id} source={transformSource.Value} durability={durabilityRatio:F2} meshState={workshopState}");
            });

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

        private RenderMetrics CaptureRenderMetrics(GameEngine engine)
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

            int northWorkshop = meshes.GetId("blacksmith.building.north.intact");
            int southWorkshop = meshes.GetId("blacksmith.building.south.intact");
            int damagedWorkshop = meshes.GetId("blacksmith.building.damaged");
            int ruinedWorkshop = meshes.GetId("blacksmith.building.ruined");
            int chimneyAsset = meshes.GetId("blacksmith.furnace");
            int smokeAsset = meshes.GetId("blacksmith.smoke.billboard");
            int workerAsset = meshes.GetId("blacksmith.worker.knight");

            int visibleWorkshopCount = 0;
            int visibleChimneyCount = 0;
            int visibleSmokeCount = 0;
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

            int visibleWorkerCount = 0;
            foreach (ref readonly SkinnedVisualBatchItem item in skinned.GetSpan())
            {
                if (item.MeshAssetId == workerAsset)
                {
                    visibleWorkerCount++;
                }
            }

            int barCount = 0;
            int textCount = 0;
            float minHudBarValue = float.MaxValue;
            float maxHudBarValue = float.MinValue;
            float minHudTextCurrent = float.MaxValue;
            float maxHudTextCurrent = float.MinValue;
            float primaryHudBarValue = 0f;
            float primaryHudTextCurrent = 0f;
            float primaryHudTextBase = 0f;
            foreach (ref readonly WorldHudItem item in worldHud.GetSpan())
            {
                if (item.Kind == WorldHudItemKind.Bar)
                {
                    barCount++;
                    minHudBarValue = MathF.Min(minHudBarValue, item.Value0);
                    maxHudBarValue = MathF.Max(maxHudBarValue, item.Value0);
                    if (primaryHudBarValue <= 0f)
                    {
                        primaryHudBarValue = item.Value0;
                    }
                }
                else if (item.Kind == WorldHudItemKind.Text)
                {
                    textCount++;
                    minHudTextCurrent = MathF.Min(minHudTextCurrent, item.Value0);
                    maxHudTextCurrent = MathF.Max(maxHudTextCurrent, item.Value0);
                    if (primaryHudTextBase <= 0f)
                    {
                        primaryHudTextCurrent = item.Value0;
                        primaryHudTextBase = item.Value1;
                    }
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

            int smokeId = definitions.GetId(PerformerBlacksmithShowcaseIds.SmokeDefinitionId);
            int chimneyId = definitions.GetId(PerformerBlacksmithShowcaseIds.ChimneyDefinitionId);
            int smokeAttachedToChimneyCount = 0;
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

            int currentTotal = CountBlacksmithEntities(engine, out _);
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
            return ComputeScatterCapacityMax(CaptureCapacityMetrics(engine));
        }

        private int ClampScatterTotal(GameEngine engine, int total)
        {
            return Math.Clamp(total, ScatterMinTotal, ResolveScatterUiMax(engine));
        }

        private void ObserveTickSample(double tickMs)
        {
            _lastFrameTickMs = tickMs;
            if (_tickSampleCount < BenchmarkSampleFrames)
            {
                _tickSamplesMs[_tickSampleCount++] = tickMs;
                _tickSampleSumMs += tickMs;
                if (tickMs > _tickSampleMaxMs)
                {
                    _tickSampleMaxMs = tickMs;
                }

                return;
            }

            double removed = _tickSamplesMs[_tickSampleCursor];
            _tickSamplesMs[_tickSampleCursor] = tickMs;
            _tickSampleCursor = (_tickSampleCursor + 1) % BenchmarkSampleFrames;
            _tickSampleSumMs += tickMs - removed;

            if (tickMs >= _tickSampleMaxMs)
            {
                _tickSampleMaxMs = tickMs;
                return;
            }

            if (Math.Abs(removed - _tickSampleMaxMs) < 0.0001d)
            {
                double recomputedMax = 0d;
                for (int i = 0; i < _tickSampleCount; i++)
                {
                    if (_tickSamplesMs[i] > recomputedMax)
                    {
                        recomputedMax = _tickSamplesMs[i];
                    }
                }

                _tickSampleMaxMs = recomputedMax;
            }
        }

        private string BuildBenchmarkSummary(int totalBlacksmiths, in RenderMetrics renderMetrics)
        {
            double avgTickMs = _tickSampleCount == 0 ? 0d : _tickSampleSumMs / _tickSampleCount;
            double avgFps = avgTickMs <= 0d ? 0d : 1000d / avgTickMs;
            return $"Benchmark: last {_tickSampleCount} tick(s) avg {avgTickMs:F2} ms | max {_tickSampleMaxMs:F2} ms | last {_lastFrameTickMs:F2} ms | fps {avgFps:F1} | world HUD {renderMetrics.WorldHudBarCount}/{renderMetrics.WorldHudTextCount} | screen HUD {renderMetrics.ScreenHudBarCount}/{renderMetrics.ScreenHudTextCount} @ blacksmiths {totalBlacksmiths}";
        }

        private string BuildCapacitySummary(in CapacityMetrics metrics, int totalBlacksmiths)
        {
            int scatterUiMax = ComputeScatterCapacityMax(metrics);

            return $"Capacity: performers {metrics.PerformerActive}/{metrics.PerformerCapacity}, events {metrics.PresentationEventCount}/{metrics.PresentationEventCapacity}, primitives {metrics.PrimitiveCount}/{metrics.PrimitiveCapacity}, world HUD {metrics.WorldHudCount}/{metrics.WorldHudCapacity}, screen HUD {metrics.ScreenHudCount}/{metrics.ScreenHudCapacity}, skinned {metrics.SkinnedCount}/{metrics.SkinnedCapacity}, spline {metrics.RoadSplineCount}/{metrics.RoadSplineCapacity}, decal {metrics.GroundOverlayCount}/{metrics.GroundOverlayCapacity}, spawnQ {metrics.SpawnQueueCount}/{metrics.SpawnQueueCapacity} | UI max {scatterUiMax} | requested {totalBlacksmiths}";
        }

        private static int ComputeScatterCapacityMax(in CapacityMetrics metrics)
        {
            int performerBound = ResolveCapacityBound(metrics.PerformerCapacity, PerformerCountPerBlacksmith);
            int primitiveBound = ResolveCapacityBound(metrics.PrimitiveCapacity, PrimitiveCountPerBlacksmith);
            int worldHudBound = ResolveCapacityBound(metrics.WorldHudCapacity, WorldHudCountPerBlacksmith);
            int screenHudBound = ResolveCapacityBound(metrics.ScreenHudCapacity, ScreenHudCountPerBlacksmith);
            int roadSplineBound = ResolveCapacityBound(metrics.RoadSplineCapacity, RoadSplineCountPerBlacksmith);
            int overlayBound = ResolveCapacityBound(metrics.GroundOverlayCapacity, GroundOverlayCountPerBlacksmith);
            int skinnedBound = ResolveCapacityBound(metrics.SkinnedCapacity, SkinnedCountPerBlacksmith);
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
            if (capacity <= 0 || perBlacksmith <= 0)
            {
                return ScatterMinTotal;
            }

            return Math.Max(ScatterMinTotal, capacity / perBlacksmith);
        }

        private void Disable(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
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

        private static int ReadEnvInt(string key, int fallback)
        {
            string? raw = Environment.GetEnvironmentVariable(key);
            return int.TryParse(raw, out int parsed) && parsed > 0
                ? parsed
                : fallback;
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
