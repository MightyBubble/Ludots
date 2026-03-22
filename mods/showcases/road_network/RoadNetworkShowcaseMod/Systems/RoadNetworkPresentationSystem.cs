using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Physics;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;
using RoadNetworkShowcaseMod.Gameplay;
using CoreInputMod.Systems;
using RoadNetworkShowcaseMod.Runtime;

namespace RoadNetworkShowcaseMod.Systems
{
    internal sealed class RoadNetworkPresentationSystem : ISystem<float>
    {
        private static readonly QueryDescription FortQuery = new QueryDescription()
            .WithAll<WorldPositionCm, Team, Name, RoadFortTag, RoadFortControlState>();

        private static readonly QueryDescription ColumnQuery = new QueryDescription()
            .WithAll<WorldPositionCm, Team, RoadColumnTag>();

        private static readonly Vector4 ChunkFill = new(0.16f, 0.23f, 0.31f, 0.16f);
        private static readonly Vector4 Blue = new(0.20f, 0.66f, 0.98f, 1f);
        private static readonly Vector4 Red = new(0.94f, 0.30f, 0.28f, 1f);
        private static readonly Vector4 Neutral = new(0.84f, 0.73f, 0.32f, 1f);

        private readonly World _world;
        private readonly RoadNetworkShowcaseRuntime _runtime;
        private readonly GameEngine _engine;
        private readonly RoadSplineBuffer? _roads;
        private readonly PrimitiveDrawBuffer? _primitives;
        private readonly ScreenOverlayBuffer? _overlay;
        private readonly int _cubeMeshId;
        private readonly int _sphereMeshId;

        public RoadNetworkPresentationSystem(GameEngine engine, RoadNetworkShowcaseRuntime runtime)
        {
            _engine = engine;
            _world = engine.World;
            _runtime = runtime;
            _roads = engine.GetService(CoreServiceKeys.RoadSplineBuffer);
            _primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
            _overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
            MeshAssetRegistry? meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry);
            _cubeMeshId = meshes?.GetId(WellKnownMeshKeys.Cube) ?? 1;
            _sphereMeshId = meshes?.GetId(WellKnownMeshKeys.Sphere) ?? 2;
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (!_runtime.IsActive || _runtime.ActiveBoard == null || _runtime.Scenario == null)
            {
                return;
            }

            EmitLoadedChunkTiles();
            EmitRoadSplines();
            EmitFortsAndColumns();
            EmitHud();
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private void EmitLoadedChunkTiles()
        {
            if (_primitives == null || _runtime.ActiveBoard == null)
            {
                return;
            }

            int chunkSizeCm = _runtime.ActiveBoard.LoadedChunksSource.ChunkSizeCm;
            float chunkSizeMeters = WorldUnits.CmToM(chunkSizeCm);
            foreach (long chunkKey in _runtime.ActiveBoard.LoadedChunksSource.ActiveChunkKeys)
            {
                (int chunkX, int chunkY) = Ludots.Core.Navigation.GraphWorld.GraphChunkKey.Unpack(chunkKey);
                float centerX = WorldUnits.CmToM((chunkX * chunkSizeCm) + (chunkSizeCm / 2f));
                float centerZ = WorldUnits.CmToM((chunkY * chunkSizeCm) + (chunkSizeCm / 2f));
                _primitives.TryAdd(new PrimitiveDrawItem
                {
                    MeshAssetId = _cubeMeshId,
                    Position = new Vector3(centerX, 0.01f, centerZ),
                    Scale = new Vector3(chunkSizeMeters, 0.02f, chunkSizeMeters),
                    Color = ChunkFill,
                    RenderPath = VisualRenderPath.StaticMesh,
                    Mobility = VisualMobility.Static,
                    Flags = VisualRuntimeFlags.Visible,
                    Visibility = VisualVisibility.Visible
                });
            }
        }

        private void EmitRoadSplines()
        {
            if (_roads == null || _runtime.ActiveBoard == null || _runtime.Scenario == null)
            {
                return;
            }

            foreach (long chunkKey in _runtime.ActiveBoard.LoadedChunksSource.ActiveChunkKeys)
            {
                if (!_runtime.Scenario.TryGetRoadSplineChunk(chunkKey, out RoadNetworkScenarioDefinition.RoadSplineSpec[]? chunkSplines))
                {
                    continue;
                }

                for (int i = 0; i < chunkSplines.Length; i++)
                {
                    ref readonly RoadNetworkScenarioDefinition.RoadSplineSpec spec = ref chunkSplines[i];
                    _roads.TryAdd(
                        spec.StableId,
                        spec.P0,
                        spec.P1,
                        spec.P2,
                        spec.P3,
                        spec.Width,
                        spec.Fill,
                        spec.Border,
                        spec.BorderWidth,
                        spec.Style);
                }
            }
        }

        private void EmitFortsAndColumns()
        {
            if (_primitives == null)
            {
                return;
            }

            foreach (ref var fortChunk in _world.Query(in FortQuery))
            {
                Span<WorldPositionCm> positions = fortChunk.GetSpan<WorldPositionCm>();
                Span<Team> teams = fortChunk.GetSpan<Team>();
                Span<RoadFortControlState> states = fortChunk.GetSpan<RoadFortControlState>();

                for (int i = 0; i < fortChunk.Count; i++)
                {
                    Vector3 position = ToMeters(positions[i].Value, yMeters: 0.55f);
                    _primitives.TryAdd(new PrimitiveDrawItem
                    {
                        MeshAssetId = _cubeMeshId,
                        Position = position,
                        Scale = new Vector3(1.35f, 1.1f, 1.35f),
                        Color = ResolveTeamColor(teams[i].Id),
                        RenderPath = VisualRenderPath.StaticMesh,
                        Mobility = VisualMobility.Static,
                        Flags = VisualRuntimeFlags.Visible,
                        Visibility = VisualVisibility.Visible
                    });

                    if (states[i].CapturingTeamId > 0 && states[i].CaptureDurationSeconds > 0f)
                    {
                        float ratio = Math.Clamp(states[i].CaptureProgressSeconds / states[i].CaptureDurationSeconds, 0f, 1f);
                        _primitives.TryAdd(new PrimitiveDrawItem
                        {
                            MeshAssetId = _sphereMeshId,
                            Position = position + new Vector3(0f, 1.05f, 0f),
                            Scale = new Vector3(0.22f + (0.55f * ratio)),
                            Color = ResolveTeamColor(states[i].CapturingTeamId),
                            RenderPath = VisualRenderPath.StaticMesh,
                            Mobility = VisualMobility.Movable,
                            Flags = VisualRuntimeFlags.Visible,
                            Visibility = VisualVisibility.Visible
                        });
                    }
                }
            }

            foreach (ref var columnChunk in _world.Query(in ColumnQuery))
            {
                Span<WorldPositionCm> positions = columnChunk.GetSpan<WorldPositionCm>();
                Span<Team> teams = columnChunk.GetSpan<Team>();

                for (int i = 0; i < columnChunk.Count; i++)
                {
                    Entity entity = columnChunk.Entity(i);
                    bool isSelected = _world.Has<SelectedTag>(entity);
                    _primitives.TryAdd(new PrimitiveDrawItem
                    {
                        MeshAssetId = _sphereMeshId,
                        Position = ToMeters(positions[i].Value, yMeters: isSelected ? 0.72f : 0.58f),
                        Scale = isSelected ? new Vector3(0.82f) : new Vector3(0.62f),
                        Color = ResolveTeamColor(teams[i].Id),
                        RenderPath = VisualRenderPath.StaticMesh,
                        Mobility = VisualMobility.Movable,
                        Flags = VisualRuntimeFlags.Visible,
                        Visibility = VisualVisibility.Visible
                    });
                }
            }
        }

        private void EmitHud()
        {
            if (_overlay == null)
            {
                return;
            }

            int blueForts = 0;
            int redForts = 0;
            int neutralForts = 0;
            foreach (ref var fortChunk in _world.Query(in FortQuery))
            {
                Span<RoadFortControlState> states = fortChunk.GetSpan<RoadFortControlState>();
                for (int i = 0; i < fortChunk.Count; i++)
                {
                    switch (states[i].ControllerTeamId)
                    {
                        case 1:
                            blueForts++;
                            break;
                        case 2:
                            redForts++;
                            break;
                        default:
                            neutralForts++;
                            break;
                    }
                }
            }

            string groundDebug = ResolveDebugValue(LocalOrderSourceHelper.LastGroundWorldDebugKey, "<none>");
            string orderDebug = ResolveDebugValue(LocalOrderSourceHelper.LastOrderDebugKey, "<none>");
            string actorDebug = DescribeControlledActor();

            _overlay.AddRect(12, 12, 980, 234, new Vector4(0.04f, 0.07f, 0.10f, 0.78f), new Vector4(0.35f, 0.51f, 0.60f, 0.92f), stableId: 8000, dirtySerial: 1);
            _overlay.AddText(24, 24, "Road Network Showcase", 22, new Vector4(0.94f, 0.96f, 0.98f, 1f), stableId: 8001, dirtySerial: 1);
            _overlay.AddText(24, 50, $"Blue forts {blueForts} | Red forts {redForts} | Neutral forts {neutralForts}", 16, new Vector4(0.78f, 0.84f, 0.90f, 1f), stableId: 8002, dirtySerial: blueForts ^ (redForts << 8) ^ (neutralForts << 16));
            _overlay.AddText(24, 72, $"Loaded chunks {_runtime.LoadedChunkCount} | Loaded nodes {_runtime.LoadedNodeCount}", 16, new Vector4(0.78f, 0.84f, 0.90f, 1f), stableId: 8003, dirtySerial: _runtime.LoadedChunkCount ^ (_runtime.LoadedNodeCount << 8));
            _overlay.AddText(24, 94, "LMB select columns, RMB dispatch along roads, Shift queue, pan camera to stream chunks, Home reset camera.", 15, new Vector4(0.93f, 0.79f, 0.45f, 1f), stableId: 8004, dirtySerial: 1);
            _overlay.AddText(24, 116, $"Ground {groundDebug}", 13, new Vector4(0.68f, 0.83f, 0.96f, 1f), stableId: 8006, dirtySerial: groundDebug.GetHashCode(StringComparison.Ordinal));
            _overlay.AddText(24, 138, $"Order {orderDebug}", 13, new Vector4(0.71f, 0.88f, 0.73f, 1f), stableId: 8007, dirtySerial: orderDebug.GetHashCode(StringComparison.Ordinal));
            _overlay.AddText(24, 160, actorDebug, 13, new Vector4(0.94f, 0.84f, 0.70f, 1f), stableId: 8008, dirtySerial: actorDebug.GetHashCode(StringComparison.Ordinal));
            string motionDebug = DescribeControlledActorMotion();
            _overlay.AddText(24, 182, motionDebug, 13, new Vector4(0.86f, 0.88f, 0.98f, 1f), stableId: 8009, dirtySerial: motionDebug.GetHashCode(StringComparison.Ordinal));
            string status = _runtime.LastSubmitStatus;
            _overlay.AddText(24, 204, status, 14, new Vector4(0.90f, 0.92f, 0.95f, 1f), stableId: 8005, dirtySerial: status.GetHashCode(StringComparison.Ordinal));
        }

        private string ResolveDebugValue(string key, string fallback)
        {
            if (_engine.GlobalContext.TryGetValue(key, out object? value) &&
                value is string text &&
                !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            return fallback;
        }

        private string DescribeControlledActor()
        {
            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? actorObj) ||
                actorObj is not Entity actor ||
                !_world.IsAlive(actor))
            {
                return "Actor <none>";
            }

            string world = _world.Has<WorldPositionCm>(actor)
                ? FormatFixVec(_world.Get<WorldPositionCm>(actor).Value)
                : "<no-world>";
            string activeOrder = "<none>";
            if (_world.Has<OrderBuffer>(actor))
            {
                ref var buffer = ref _world.Get<OrderBuffer>(actor);
                if (buffer.HasActive)
                {
                    ref Order order = ref buffer.ActiveOrder.Order;
                    int waypointIndex = 0;
                    string lifecycle = "<no-runtime>";
                    if (_world.Has<RoadMoveOrderRuntime>(actor))
                    {
                        ref readonly var moveState = ref _world.Get<RoadMoveOrderRuntime>(actor);
                        lifecycle = $"{moveState.LifecycleState}/{moveState.FailureReason} t:{moveState.TimeoutCount}";
                    }

                    if (_world.Has<RoadNavPlanRuntime>(actor))
                    {
                        ref readonly var state = ref _world.Get<RoadNavPlanRuntime>(actor);
                        if (state.BoundOrderId == order.OrderId)
                        {
                            waypointIndex = Math.Clamp(state.CurrentWaypointIndex, 0, Math.Max(0, state.PointCount - 1));
                        }
                    }

                    activeOrder = $"type:{order.OrderTypeId} order:{order.OrderId} wp:{waypointIndex} pts:{order.Args.Spatial.PointCount} {lifecycle}";
                }
            }

            string goal = "<none>";
            if (_world.Has<NavGoal2D>(actor))
            {
                ref var navGoal = ref _world.Get<NavGoal2D>(actor);
                if (navGoal.Kind == NavGoalKind2D.Point)
                {
                    goal = $"{FormatFixVec(navGoal.TargetCm)} r:{navGoal.RadiusCm.ToFloat():0}";
                }
            }

            return $"Actor world {world} | active {activeOrder} | goal {goal}";
        }

        private string DescribeControlledActorMotion()
        {
            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? actorObj) ||
                actorObj is not Entity actor ||
                !_world.IsAlive(actor))
            {
                return "Motion <none>";
            }

            string position2D = _world.Has<Position2D>(actor)
                ? FormatFixVec(_world.Get<Position2D>(actor).Value)
                : "<no-pos2d>";
            string velocity2D = _world.Has<Velocity2D>(actor)
                ? FormatFixVec(_world.Get<Velocity2D>(actor).Linear)
                : "<no-vel>";
            string force2D = _world.Has<ForceInput2D>(actor)
                ? FormatFixVec(_world.Get<ForceInput2D>(actor).Force)
                : "<no-force>";
            string desiredVelocity = _world.Has<NavDesiredVelocity2D>(actor)
                ? FormatFixVec(_world.Get<NavDesiredVelocity2D>(actor).ValueCmPerSec)
                : "<no-desired>";

            return $"Motion pos2d {position2D} | vel {velocity2D} | desired {desiredVelocity} | force {force2D}";
        }

        private static string FormatFixVec(in Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 value)
        {
            return $"({value.X.ToFloat():0},{value.Y.ToFloat():0})";
        }

        private static Vector3 ToMeters(in Ludots.Core.Mathematics.FixedPoint.Fix64Vec2 worldCm, float yMeters)
        {
            return new Vector3(WorldUnits.CmToM(worldCm.X.ToFloat()), yMeters, WorldUnits.CmToM(worldCm.Y.ToFloat()));
        }

        private static Vector4 ResolveTeamColor(int teamId)
        {
            return teamId switch
            {
                1 => Blue,
                2 => Red,
                _ => Neutral
            };
        }
    }
}
