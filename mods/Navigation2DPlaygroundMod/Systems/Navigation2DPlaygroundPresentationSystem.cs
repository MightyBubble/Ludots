using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.UI;
using Navigation2DPlaygroundMod.Runtime;

namespace Navigation2DPlaygroundMod.Systems
{
    public sealed class Navigation2DPlaygroundPresentationSystem : ISystem<float>
    {
        private const int DesiredVelocityDrawStride = 8;

        private static readonly QueryDescription AgentQuery = new QueryDescription()
            .WithAll<NavAgent2D, VisualTransform, NavPlaygroundTeam>();

        private static readonly QueryDescription DesiredVelocityQuery = new QueryDescription()
            .WithAll<NavAgent2D, VisualTransform, NavDesiredVelocity2D, NavPlaygroundTeam>();

        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly DebugDrawCommandBuffer _debugDraw;
        private int _sphereMeshId;
        private float _smoothedFrameMs;

        public Navigation2DPlaygroundPresentationSystem(GameEngine engine, DebugDrawCommandBuffer debugDraw, MeshAssetRegistry meshes)
        {
            _engine = engine;
            _world = engine.World;
            _debugDraw = debugDraw;
            _sphereMeshId = meshes.GetId(WellKnownMeshKeys.Sphere);
        }

        public void Initialize()
        {
            var meshRegistry = _engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry);
            _sphereMeshId = meshRegistry?.GetId(WellKnownMeshKeys.Sphere) ?? 2;
        }

        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float deltaTime)
        {
            if (!Navigation2DPlaygroundState.Enabled)
            {
                return;
            }

            _debugDraw.Clear();
            AppendSolidPrimitives();
            AppendDesiredVelocity();
            AppendFlowFieldDebug();
            AppendTelemetryOverlay(deltaTime);
        }

        private void AppendSolidPrimitives()
        {
            if (_engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer) is not PrimitiveDrawBuffer draw)
            {
                return;
            }

            foreach (ref var chunk in _world.Query(in AgentQuery))
            {
                var visuals = chunk.GetSpan<VisualTransform>();
                var teams = chunk.GetSpan<NavPlaygroundTeam>();
                bool isBlockerChunk = chunk.Has<NavPlaygroundBlocker>();

                for (int i = 0; i < chunk.Count; i++)
                {
                    bool isSelected = !isBlockerChunk && _world.Has<SelectedTag>(chunk.Entity(i));
                    Vector4 color = isBlockerChunk
                        ? new Vector4(0.35f, 0.55f, 1f, 1f)
                        : teams[i].Id == 0
                            ? (isSelected ? new Vector4(0.9f, 1f, 0.35f, 1f) : new Vector4(0.1f, 0.9f, 0.2f, 1f))
                            : (isSelected ? new Vector4(1f, 0.82f, 0.32f, 1f) : new Vector4(0.95f, 0.15f, 0.2f, 1f));
                    Vector3 scale = isBlockerChunk
                        ? new Vector3(0.8f, 0.8f, 0.8f)
                        : (isSelected ? new Vector3(0.8f, 0.8f, 0.8f) : new Vector3(0.6f, 0.6f, 0.6f));

                    ref var transform = ref visuals[i];
                    draw.TryAdd(new PrimitiveDrawItem
                    {
                        MeshAssetId = _sphereMeshId,
                        Position = new Vector3(transform.Position.X, 0.25f, transform.Position.Z),
                        Scale = scale,
                        Color = color
                    });
                }
            }
        }

        private void AppendDesiredVelocity()
        {
            foreach (ref var chunk in _world.Query(in DesiredVelocityQuery))
            {
                if (chunk.Has<NavPlaygroundBlocker>())
                {
                    continue;
                }

                var visuals = chunk.GetSpan<VisualTransform>();
                var desired = chunk.GetSpan<NavDesiredVelocity2D>();
                var teams = chunk.GetSpan<NavPlaygroundTeam>();

                for (int i = 0; i < chunk.Count; i++)
                {
                    if (DesiredVelocityDrawStride > 1 && (chunk.Entity(i).Id % DesiredVelocityDrawStride) != 0)
                    {
                        continue;
                    }

                    ref var transform = ref visuals[i];
                    Vector2 from = new(transform.Position.X, transform.Position.Z);
                    Vector2 delta = desired[i].ValueCmPerSec.ToVector2() * 0.005f;
                    Vector2 to = from + delta;

                    _debugDraw.Lines.Add(new DebugDrawLine2D
                    {
                        A = from,
                        B = to,
                        Thickness = 1f,
                        Color = teams[i].Id == 0 ? DebugDrawColor.Green : DebugDrawColor.Red
                    });
                }
            }
        }

        private void AppendFlowFieldDebug()
        {
            if (_engine.GetService(CoreServiceKeys.Navigation2DRuntime) is not Navigation2DRuntime runtime || !runtime.FlowDebugEnabled)
            {
                return;
            }

            int beforeLines = _debugDraw.Lines.Count;
            int mode = runtime.FlowDebugMode;
            int startFlow = mode == 1 ? 0 : (mode == 2 ? 1 : 0);
            int endFlowExclusive = mode == 1 ? 1 : (mode == 2 ? 2 : runtime.FlowCount);

            float halfM = 90f;
            float stepM = 4f;
            float arrowLenM = 1.5f;
            Fix64 maxSpeedCmPerSec = Fix64.FromInt(800);

            for (float y = -halfM; y <= halfM + 0.001f; y += stepM)
            {
                for (float x = -halfM; x <= halfM + 0.001f; x += stepM)
                {
                    for (int flowId = startFlow; flowId < endFlowExclusive; flowId++)
                    {
                        var flow = runtime.TryGetFlow(flowId);
                        if (flow == null)
                        {
                            continue;
                        }

                        Fix64Vec2 positionCm = Fix64Vec2.FromFloat(x * 100f, y * 100f);
                        if (!flow.TrySampleDesiredVelocityCm(positionCm, maxSpeedCmPerSec, out Fix64Vec2 desiredCmPerSec))
                        {
                            continue;
                        }

                        Vector2 direction = desiredCmPerSec.ToVector2();
                        float length = direction.Length();
                        if (length <= 1e-4f)
                        {
                            continue;
                        }

                        direction /= length;
                        float offsetX = flowId == 1 ? 0.6f : -0.6f;
                        Vector2 from = new(x + offsetX, y);
                        Vector2 to = from + direction * arrowLenM;
                        AddArrow(from, to, flowId == 0 ? DebugDrawColor.Cyan : DebugDrawColor.Yellow);
                    }
                }
            }

            _engine.SetService(Navigation2DPlaygroundKeys.FlowDebugLines, _debugDraw.Lines.Count - beforeLines);
        }

        private void AddArrow(Vector2 from, Vector2 to, DebugDrawColor color)
        {
            _debugDraw.Lines.Add(new DebugDrawLine2D { A = from, B = to, Thickness = 1f, Color = color });

            Vector2 direction = to - from;
            float length = direction.Length();
            if (length <= 1e-6f)
            {
                return;
            }

            direction /= length;
            float headLength = 1f;
            float angle = 0.43633232f;
            float c = MathF.Cos(angle);
            float s = MathF.Sin(angle);

            Vector2 left = new(direction.X * c - direction.Y * s, direction.X * s + direction.Y * c);
            Vector2 right = new(direction.X * c + direction.Y * s, -direction.X * s + direction.Y * c);

            _debugDraw.Lines.Add(new DebugDrawLine2D { A = to, B = to - left * headLength, Thickness = 1f, Color = color });
            _debugDraw.Lines.Add(new DebugDrawLine2D { A = to, B = to - right * headLength, Thickness = 1f, Color = color });
        }

        private void AppendTelemetryOverlay(float deltaTime)
        {
            if (_engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is not ScreenOverlayBuffer overlay)
            {
                return;
            }

            bool flowEnabled = false;
            bool flowDebug = false;
            int flowMode = 0;
            int flowIterations = 0;
            int flowActiveTiles = 0;
            int flowLoadedTiles = 0;
            int flowWindowWidth = 0;
            int flowWindowHeight = 0;
            int flowSelectedTiles = 0;
            int flowRetainedTiles = 0;
            int flowNewTiles = 0;
            int flowEvictedTiles = 0;
            string steeringMode = "Unavailable";
            bool temporalCoherenceEnabled = false;
            bool temporalRequireSteadyState = false;
            int temporalMaxReuseTicks = 0;
            bool steeringCacheFrameEnabled = false;
            int steeringCacheLookupsFrame = 0;
            int steeringCacheHitsFrame = 0;
            int steeringCacheStoresFrame = 0;
            float steeringCacheHitRate = 0f;
            string steeringCacheState = "Unavailable";
            string spatialMode = "Unavailable";
            long spatialRebuilds = 0;
            long spatialIncrementalUpdates = 0;
            long spatialDirtyAgents = 0;
            long spatialCellMigrations = 0;
            bool uiDirty = _engine.GetService(CoreServiceKeys.UIRoot) is UIRoot uiRoot && uiRoot.IsDirty;

            float frameMs = deltaTime > 1e-6f ? deltaTime * 1000f : 0f;
            if (_smoothedFrameMs <= 0f)
            {
                _smoothedFrameMs = frameMs;
            }
            else
            {
                _smoothedFrameMs += (frameMs - _smoothedFrameMs) * 0.1f;
            }

            float fps = _smoothedFrameMs > 1e-4f ? 1000f / _smoothedFrameMs : 0f;

            if (_engine.GetService(CoreServiceKeys.Navigation2DRuntime) is Navigation2DRuntime runtime)
            {
                flowEnabled = runtime.FlowEnabled;
                flowDebug = runtime.FlowDebugEnabled;
                flowMode = runtime.FlowDebugMode;
                flowIterations = runtime.FlowIterationsPerTick;
                for (int i = 0; i < runtime.FlowCount; i++)
                {
                    var flow = runtime.Flows[i];
                    flowActiveTiles += flow.ActiveTileCount;
                    flowLoadedTiles = Math.Max(flowLoadedTiles, flow.LoadedTileCount);
                    flowWindowWidth = Math.Max(flowWindowWidth, flow.InstrumentedWindowWidthTiles);
                    flowWindowHeight = Math.Max(flowWindowHeight, flow.InstrumentedWindowHeightTiles);
                    flowSelectedTiles += flow.InstrumentedSelectedTilesFrame;
                    flowRetainedTiles += flow.InstrumentedRetainedTilesFrame;
                    flowNewTiles += flow.InstrumentedNewTilesActivatedFrame;
                    flowEvictedTiles += flow.InstrumentedEvictedTilesFrame;
                }

                steeringMode = runtime.Config.Steering.Mode.ToString();
                temporalCoherenceEnabled = runtime.Config.Steering.TemporalCoherence.Enabled;
                temporalRequireSteadyState = runtime.Config.Steering.TemporalCoherence.RequireSteadyStateWorld;
                temporalMaxReuseTicks = runtime.Config.Steering.TemporalCoherence.MaxReuseTicks;
                steeringCacheFrameEnabled = runtime.AgentSoA.SteeringCacheFrameEnabled;
                steeringCacheLookupsFrame = runtime.AgentSoA.SteeringCacheLookupsFrame;
                steeringCacheHitsFrame = runtime.AgentSoA.SteeringCacheHitsFrame;
                steeringCacheStoresFrame = runtime.AgentSoA.SteeringCacheStoresFrame;
                steeringCacheHitRate = steeringCacheLookupsFrame > 0 ? (float)steeringCacheHitsFrame / steeringCacheLookupsFrame : 0f;
                steeringCacheState = !temporalCoherenceEnabled
                    ? "ConfigOff"
                    : (steeringCacheFrameEnabled ? "Active" : (temporalRequireSteadyState ? "WaitingSteadyState" : "Ready"));
                spatialMode = runtime.Config.Spatial.UpdateMode.ToString();
                spatialRebuilds = runtime.CellMap.InstrumentedFullRebuilds;
                spatialIncrementalUpdates = runtime.CellMap.InstrumentedIncrementalUpdates;
                spatialDirtyAgents = runtime.CellMap.InstrumentedDirtyAgents;
                spatialCellMigrations = runtime.CellMap.InstrumentedCellMigrations;
            }

            int agentsPerTeam = _engine.GetService(Navigation2DPlaygroundKeys.AgentsPerTeam);
            int liveTotal = _engine.GetService(Navigation2DPlaygroundKeys.LiveAgentsTotal);
            int blockerCount = _engine.GetService(Navigation2DPlaygroundKeys.BlockerCount);
            int scenarioIndex = _engine.GetService(Navigation2DPlaygroundKeys.ScenarioIndex);
            int scenarioCount = _engine.GetService(Navigation2DPlaygroundKeys.ScenarioCount);
            int scenarioTeamCount = _engine.GetService(Navigation2DPlaygroundKeys.ScenarioTeamCount);
            int spawnBatch = _engine.GetService(Navigation2DPlaygroundKeys.SpawnBatch);
            int flowDebugLines = _engine.GetService(Navigation2DPlaygroundKeys.FlowDebugLines);
            string scenarioId = _engine.GetService(Navigation2DPlaygroundKeys.ScenarioId) ?? "unknown";
            string scenarioName = _engine.GetService(Navigation2DPlaygroundKeys.ScenarioName) ?? "Unknown";

            Span<Entity> selected = stackalloc Entity[SelectionBuffer.CAPACITY];
            int selectedCount = Navigation2DPlaygroundSelectionView.CopySelectedEntities(_engine.World, _engine.GlobalContext, selected);

            int x = 16;
            int y = 540;
            int w = 860;
            int h = 192;
            Vector4 background = new(0.04f, 0.05f, 0.08f, 0.68f);
            Vector4 border = new(0.35f, 0.75f, 1f, 0.5f);
            Vector4 title = new(0.9f, 0.95f, 1f, 1f);
            Vector4 text = new(0.85f, 0.9f, 0.95f, 1f);
            Vector4 hint = new(0.68f, 0.78f, 0.9f, 0.95f);

            overlay.AddRect(x, y, w, h, background, border);
            overlay.AddText(x + 10, y + 8, "Navigation2D Playground Telemetry", 16, title);
            overlay.AddText(x + 10, y + 30, $"Frame={_smoothedFrameMs:F2}ms  FPS={fps:F1}  UiDirty={uiDirty}", 14, hint);
            overlay.AddText(x + 10, y + 50, $"Scenario={scenarioIndex + 1}/{scenarioCount}  {scenarioName} [{scenarioId}]  Tool={Navigation2DPlaygroundState.ToolMode}  Selected={selectedCount}", 14, text);
            overlay.AddText(x + 10, y + 70, $"Agents/team={agentsPerTeam}  Teams={scenarioTeamCount}  Live={liveTotal}  Blockers={blockerCount}  SpawnBatch={spawnBatch}", 14, text);
            overlay.AddText(x + 10, y + 90, $"Steering={steeringMode}  CacheCfg={temporalCoherenceEnabled}  CacheFrame={steeringCacheFrameEnabled}  MaxReuse={temporalMaxReuseTicks}  RequireSteady={temporalRequireSteadyState}", 14, text);
            overlay.AddText(x + 10, y + 110, $"CacheLookups={steeringCacheLookupsFrame}  Hits={steeringCacheHitsFrame}  Stores={steeringCacheStoresFrame}  HitRate={steeringCacheHitRate:P1}  State={steeringCacheState}", 14, text);
            overlay.AddText(x + 10, y + 130, $"FlowEnabled={flowEnabled}  FlowDebug={flowDebug}  Mode={flowMode}  Iter={flowIterations}  ActiveTiles={flowActiveTiles}  LoadedTiles={flowLoadedTiles}  DbgLines={flowDebugLines}", 14, text);
            overlay.AddText(x + 10, y + 150, $"FlowWindow={flowWindowWidth}x{flowWindowHeight}  Selected={flowSelectedTiles}  Retained={flowRetainedTiles}  New={flowNewTiles}  Evict={flowEvictedTiles}", 14, text);
            overlay.AddText(x + 10, y + 170, $"Spatial={spatialMode}  Rebuilds={spatialRebuilds}  Incremental={spatialIncrementalUpdates}  Dirty={spatialDirtyAgents}  CellMigrations={spatialCellMigrations}", 14, text);
            overlay.AddText(x + 10, y + 186, "Panel is primary UI. Overlay remains telemetry-only for headless evidence and perf reads.", 13, hint);
        }
    }
}
