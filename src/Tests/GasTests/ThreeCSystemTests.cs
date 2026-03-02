using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Spatial;
using Ludots.Core.Systems;
using NUnit.Framework;
using Schedulers;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.ThreeC
{
    [TestFixture]
    public class ThreeCSystemTests
    {
        // ══════════════════════════════════════════════════════════════
        //  Test Doubles
        // ══════════════════════════════════════════════════════════════

        /// <summary>Captures CameraRenderState3D sent by CameraPresenter.</summary>
        private sealed class StubCameraAdapter : ICameraAdapter
        {
            public CameraRenderState3D LastState { get; private set; }
            public int CallCount { get; private set; }

            public void UpdateCamera(in CameraRenderState3D state)
            {
                LastState = state;
                CallCount++;
            }
        }

        /// <summary>Configurable view properties for CameraCullingSystem.</summary>
        private sealed class StubViewController : IViewController
        {
            public Vector2 Resolution { get; set; } = new Vector2(1920, 1080);
            public float Fov { get; set; } = 60f;
            public float AspectRatio { get; set; } = 16f / 9f;
        }

        /// <summary>Returns a pre-loaded entity list from QueryAabb; other methods throw.</summary>
        private sealed class StubSpatialQueryService : ISpatialQueryService
        {
            public List<Entity> Entities { get; } = new List<Entity>();

            public SpatialQueryResult QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer)
            {
                int count = System.Math.Min(Entities.Count, buffer.Length);
                for (int i = 0; i < count; i++) buffer[i] = Entities[i];
                int dropped = Entities.Count - count;
                return new SpatialQueryResult(count, dropped > 0 ? dropped : 0);
            }

            public SpatialQueryResult QueryRadius(WorldCmInt2 center, int radiusCm, Span<Entity> buffer) => throw new NotSupportedException();
            public SpatialQueryResult QueryCone(WorldCmInt2 origin, int directionDeg, int halfAngleDeg, int rangeCm, Span<Entity> buffer) => throw new NotSupportedException();
            public SpatialQueryResult QueryRectangle(WorldCmInt2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer) => throw new NotSupportedException();
            public SpatialQueryResult QueryLine(WorldCmInt2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer) => throw new NotSupportedException();
            public SpatialQueryResult QueryHexRange(Ludots.Core.Map.Hex.HexCoordinates center, int hexRadius, Span<Entity> buffer) => throw new NotSupportedException();
            public SpatialQueryResult QueryHexRing(Ludots.Core.Map.Hex.HexCoordinates center, int hexRadius, Span<Entity> buffer) => throw new NotSupportedException();
        }

        /// <summary>Dictionary-driven input backend for deterministic tests.</summary>
        private sealed class StubInputBackend : IInputBackend
        {
            public Dictionary<string, bool> Buttons { get; } = new Dictionary<string, bool>();
            public Dictionary<string, float> Axes { get; } = new Dictionary<string, float>();
            public Vector2 MousePos { get; set; }
            public float MouseWheel { get; set; }

            public float GetAxis(string devicePath) => Axes.TryGetValue(devicePath, out var v) ? v : 0f;
            public bool GetButton(string devicePath) => Buttons.TryGetValue(devicePath, out var v) && v;
            public Vector2 GetMousePosition() => MousePos;
            public float GetMouseWheel() => MouseWheel;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        /// <summary>Identity coordinate converter for tests.</summary>
        private sealed class StubSpatialCoordinateConverter : ISpatialCoordinateConverter
        {
            public int GridCellSizeCm => 100;
            public WorldCmInt2 GridToWorld(in IntVector2 grid) => new WorldCmInt2(grid.X * 100, grid.Y * 100);
            public IntVector2 WorldToGrid(in WorldCmInt2 world) => new IntVector2(world.X / 100, world.Y / 100);
            public WorldCmInt2 HexToWorld(in Ludots.Core.Map.Hex.HexCoordinates hex) => WorldCmInt2.Zero;
            public Ludots.Core.Map.Hex.HexCoordinates WorldToHex(in WorldCmInt2 world) => default;
        }

        /// <summary>Camera controller driven by a lambda.</summary>
        private sealed class LambdaCameraController : ICameraController
        {
            private readonly Action<CameraState, float> _action;
            public int UpdateCallCount { get; private set; }

            public LambdaCameraController(Action<CameraState, float> action)
            {
                _action = action;
            }

            public void Update(CameraState state, float dt)
            {
                UpdateCallCount++;
                _action?.Invoke(state, dt);
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  SetUp
        // ══════════════════════════════════════════════════════════════

        [SetUp]
        public void Setup()
        {
            if (World.SharedJobScheduler == null)
            {
                World.SharedJobScheduler = new JobScheduler(new JobScheduler.Config
                {
                    ThreadPrefixName = "ThreeCTests",
                    ThreadCount = 0,
                    MaxExpectedConcurrentJobs = 64,
                    StrictAllocationMode = false
                });
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  A. Camera State & Manager
        // ══════════════════════════════════════════════════════════════

        [Test]
        public void CameraState_DefaultValues_MatchExpected()
        {
            var state = new CameraState();

            That(state.Yaw, Is.EqualTo(45f));
            That(state.Pitch, Is.EqualTo(45f));
            That(state.DistanceCm, Is.EqualTo(2000f));
            That(state.FovYDeg, Is.EqualTo(60f));
            That(state.ZoomLevel, Is.EqualTo(5));
            That(state.TargetCm, Is.EqualTo(Vector2.Zero));
        }

        [Test]
        public void CameraManager_SetController_DelegatesToUpdate()
        {
            var manager = new CameraManager();
            var controller = new LambdaCameraController((s, dt) => s.Yaw = 123f);
            manager.SetController(controller);

            manager.Update(0.016f);

            That(controller.UpdateCallCount, Is.EqualTo(1));
            That(manager.State.Yaw, Is.EqualTo(123f));
        }

        [Test]
        public void CameraManager_NullController_UpdateDoesNotThrow()
        {
            var manager = new CameraManager();
            // Controller is null by default — Update must not throw
            Assert.DoesNotThrow(() => manager.Update(0.016f));
        }

        // ══════════════════════════════════════════════════════════════
        //  B. Orbit3C Controller
        // ══════════════════════════════════════════════════════════════

        private static (StubInputBackend backend, PlayerInputHandler handler) BuildOrbitInputHandler()
        {
            var backend = new StubInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Move", Type = InputActionType.Axis2D },
                    new() { Id = "Zoom", Type = InputActionType.Axis1D },
                    new() { Id = "PointerPos", Type = InputActionType.Axis2D },
                    new() { Id = "OrbitRotateHold", Type = InputActionType.Button },
                    new() { Id = "RotateLeft", Type = InputActionType.Button },
                    new() { Id = "RotateRight", Type = InputActionType.Button },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "OrbitCamera",
                        Priority = 10,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "Zoom", Path = "<Mouse>/Scroll", Processors = new() },
                            new() { ActionId = "PointerPos", Path = "<Mouse>/Pos", Processors = new() },
                            new() { ActionId = "OrbitRotateHold", Path = "<Mouse>/MiddleButton", Processors = new() },
                            new() { ActionId = "RotateLeft", Path = "<Keyboard>/q", Processors = new() },
                            new() { ActionId = "RotateRight", Path = "<Keyboard>/e", Processors = new() },
                        }
                    }
                }
            };
            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("OrbitCamera");
            return (backend, handler);
        }

        [Test]
        public void Orbit3C_Zoom_ClampsToMinMax()
        {
            var (backend, handler) = BuildOrbitInputHandler();
            var cfg = new Orbit3CCameraConfig { MinDistanceCm = 500f, MaxDistanceCm = 5000f, ZoomCmPerWheel = 2000f };
            var controller = new Orbit3CCameraController(cfg, handler);
            var state = new CameraState { DistanceCm = 1000f };

            // Zoom in far beyond min
            backend.MouseWheel = 10f; // large positive → subtract from distance
            handler.Update();
            controller.Update(state, 0.016f);
            That(state.DistanceCm, Is.EqualTo(cfg.MinDistanceCm), "Should clamp to MinDistanceCm");

            // Zoom out far beyond max
            state.DistanceCm = 4000f;
            backend.MouseWheel = -10f; // large negative → add to distance
            handler.Update();
            controller.Update(state, 0.016f);
            That(state.DistanceCm, Is.EqualTo(cfg.MaxDistanceCm), "Should clamp to MaxDistanceCm");
        }

        [Test]
        public void Orbit3C_KeyboardRotate_YawWraps360()
        {
            var (backend, handler) = BuildOrbitInputHandler();
            var cfg = new Orbit3CCameraConfig { RotateDegPerSecond = 90f };
            var controller = new Orbit3CCameraController(cfg, handler);
            var state = new CameraState { Yaw = 355f };

            // RotateRight = true → positive yaw delta
            backend.Buttons["<Keyboard>/e"] = true;
            handler.Update();
            controller.Update(state, 1f); // 90° per second × 1 second

            // 355 + 90 = 445 → wraps to 85
            That(state.Yaw, Is.GreaterThanOrEqualTo(0f));
            That(state.Yaw, Is.LessThan(360f));
            That(state.Yaw, Is.EqualTo(85f).Within(0.01f));
        }

        [Test]
        public void Orbit3C_PitchClamp_RespectsMaxBound()
        {
            var (backend, handler) = BuildOrbitInputHandler();
            var cfg = new Orbit3CCameraConfig { MaxPitchDeg = 85f, MinPitchDeg = 10f, RotateDegPerPixel = 0.28f };
            var controller = new Orbit3CCameraController(cfg, handler);
            var state = new CameraState { Pitch = 80f };

            // Simulate mouse drag: hold middle button, move pointer down
            backend.Buttons["<Mouse>/MiddleButton"] = true;
            backend.MousePos = new Vector2(100f, 100f);
            handler.Update();
            controller.Update(state, 0.016f); // first frame: records lastPointerPos

            // Second frame: large downward delta → positive pitch
            backend.MousePos = new Vector2(100f, 500f);
            handler.Update();
            controller.Update(state, 0.016f);

            That(state.Pitch, Is.LessThanOrEqualTo(cfg.MaxPitchDeg), "Pitch must not exceed MaxPitchDeg");
            That(state.Pitch, Is.GreaterThanOrEqualTo(cfg.MinPitchDeg), "Pitch must not fall below MinPitchDeg");
        }

        // ══════════════════════════════════════════════════════════════
        //  C. Camera Presenter
        // ══════════════════════════════════════════════════════════════

        [Test]
        public void Presenter_FirstUpdate_SphericalToCartesianCorrect()
        {
            var adapter = new StubCameraAdapter();
            var coords = new StubSpatialCoordinateConverter();
            var presenter = new CameraPresenter(coords, adapter);

            var state = new CameraState { Yaw = 0f, Pitch = 45f, DistanceCm = 1000f, FovYDeg = 60f };
            state.TargetCm = Vector2.Zero;

            presenter.Update(state, 0.016f);

            // Yaw=0 → sin(0)=0, cos(0)=1
            // Pitch=45° → cos(π/4)≈0.7071, sin(π/4)≈0.7071
            // distM = 10m, hDist = 10*cos(45°)≈7.071, vDist = 10*sin(45°)≈7.071
            // offsetX = 7.071 * sin(0) = 0
            // offsetZ = -7.071 * cos(0) = -7.071
            // position ≈ (0, 7.071, -7.071)
            var pos = adapter.LastState.PositionCm;
            That(pos.X, Is.EqualTo(0f).Within(0.1f));
            That(pos.Y, Is.EqualTo(7.071f).Within(0.1f));
            That(pos.Z, Is.EqualTo(-7.071f).Within(0.1f));
            That(adapter.CallCount, Is.EqualTo(1));
        }

        [Test]
        public void Presenter_NearVerticalPitch_SwitchesUpToUnitZ()
        {
            var adapter = new StubCameraAdapter();
            var coords = new StubSpatialCoordinateConverter();
            var presenter = new CameraPresenter(coords, adapter);

            // Pitch ≈ 89° → forward ≈ (0, -1, 0) → dot(forward, UnitY) > 0.99 → up switches to UnitZ
            var state = new CameraState { Yaw = 0f, Pitch = 89f, DistanceCm = 1000f, FovYDeg = 60f };
            state.TargetCm = Vector2.Zero;

            presenter.Update(state, 0.016f);

            var up = adapter.LastState.Up;
            // Up should be approximately UnitZ, not UnitY
            That(MathF.Abs(up.Z), Is.GreaterThan(0.5f), "Near-vertical pitch should switch Up toward UnitZ");
        }

        [Test]
        public void Presenter_SecondUpdate_AppliesLerpSmoothing()
        {
            var adapter = new StubCameraAdapter();
            var coords = new StubSpatialCoordinateConverter();
            var presenter = new CameraPresenter(coords, adapter) { SmoothSpeed = 10f };

            // First frame: snap
            var state = new CameraState { Yaw = 0f, Pitch = 45f, DistanceCm = 1000f, FovYDeg = 60f };
            state.TargetCm = Vector2.Zero;
            presenter.Update(state, 0.016f);
            var firstPos = adapter.LastState.PositionCm;

            // Second frame: move target → position should lerp, not snap
            state.TargetCm = new Vector2(5000f, 5000f); // 50m offset
            presenter.Update(state, 0.016f);
            var secondPos = adapter.LastState.PositionCm;

            // The target changed dramatically, but with SmoothSpeed=10 and dt=0.016,
            // t = clamp(10*0.016, 0, 1) = 0.16, so position should move only ~16% of the way
            float targetVisualX = 5000f / 100f; // 50m
            That(secondPos.X, Is.GreaterThan(firstPos.X), "Position should move toward new target");
            That(secondPos.X, Is.LessThan(targetVisualX), "Position should not snap to target (smoothing)");
        }

        // ══════════════════════════════════════════════════════════════
        //  D. Camera Culling
        // ══════════════════════════════════════════════════════════════

        private static Entity CreateCullableEntity(World world, int xCm, int yCm)
        {
            var e = world.Create(
                WorldPositionCm.FromCm(xCm, yCm),
                new CullState(),
                new VisualModel { MeshId = 1, MaterialId = 1, BaseScale = 1f }
            );
            return e;
        }

        [Test]
        public void Culling_NearEntity_HighLOD()
        {
            using var world = World.Create();
            var manager = new CameraManager();
            manager.State.TargetCm = Vector2.Zero;
            manager.State.DistanceCm = 2000f;
            manager.State.Pitch = 45f;
            manager.State.FovYDeg = 60f;

            var spatial = new StubSpatialQueryService();
            var view = new StubViewController();

            var entity = CreateCullableEntity(world, 100, 100); // very close to camera target
            spatial.Entities.Add(entity);

            var system = new CameraCullingSystem(world, manager, spatial, view);
            system.Update(0.016f);

            ref var cull = ref world.Get<CullState>(entity);
            That(cull.LOD, Is.EqualTo(LODLevel.High));
            That(cull.IsVisible, Is.True);
        }

        [Test]
        public void Culling_MediumDistance_MediumLOD()
        {
            using var world = World.Create();
            var manager = new CameraManager();
            manager.State.TargetCm = Vector2.Zero;
            manager.State.DistanceCm = 30000f; // large distance → viewport covers test entities
            manager.State.Pitch = 45f;
            manager.State.FovYDeg = 60f;

            var spatial = new StubSpatialQueryService();
            var view = new StubViewController();

            // Place entity at ~7000cm from origin → between High(4000) and Medium(10000)
            var entity = CreateCullableEntity(world, 5000, 5000); // dist ≈ 7071cm
            spatial.Entities.Add(entity);

            var system = new CameraCullingSystem(world, manager, spatial, view);
            system.Update(0.016f);

            ref var cull = ref world.Get<CullState>(entity);
            That(cull.LOD, Is.EqualTo(LODLevel.Medium));
            That(cull.IsVisible, Is.True);
        }

        [Test]
        public void Culling_FarEntity_LowLOD()
        {
            using var world = World.Create();
            var manager = new CameraManager();
            manager.State.TargetCm = Vector2.Zero;
            manager.State.DistanceCm = 30000f; // large distance → viewport covers test entities
            manager.State.Pitch = 45f;
            manager.State.FovYDeg = 60f;

            var spatial = new StubSpatialQueryService();
            var view = new StubViewController();

            // Place entity at ~14142cm from origin → between Medium(10000) and Low(20000)
            var entity = CreateCullableEntity(world, 10000, 10000); // dist ≈ 14142cm
            spatial.Entities.Add(entity);

            var system = new CameraCullingSystem(world, manager, spatial, view);
            system.Update(0.016f);

            ref var cull = ref world.Get<CullState>(entity);
            That(cull.LOD, Is.EqualTo(LODLevel.Low));
            That(cull.IsVisible, Is.True);
        }

        [Test]
        public void Culling_BeyondLow_Culled()
        {
            using var world = World.Create();
            var manager = new CameraManager();
            manager.State.TargetCm = Vector2.Zero;
            manager.State.DistanceCm = 30000f; // large distance → viewport covers test entities
            manager.State.Pitch = 45f;
            manager.State.FovYDeg = 60f;

            var spatial = new StubSpatialQueryService();
            var view = new StubViewController();

            // Place entity at ~28284cm → beyond Low(20000) threshold
            var entity = CreateCullableEntity(world, 20000, 20000); // dist ≈ 28284cm
            spatial.Entities.Add(entity);

            var system = new CameraCullingSystem(world, manager, spatial, view);
            system.Update(0.016f);

            ref var cull = ref world.Get<CullState>(entity);
            That(cull.LOD, Is.EqualTo(LODLevel.Culled));
            That(cull.IsVisible, Is.False);
        }

        [Test]
        public void Culling_PrevVisibleEntityRemoved_MarkedCulled()
        {
            using var world = World.Create();
            var manager = new CameraManager();
            manager.State.TargetCm = Vector2.Zero;
            manager.State.DistanceCm = 2000f;
            manager.State.Pitch = 45f;
            manager.State.FovYDeg = 60f;

            var spatial = new StubSpatialQueryService();
            var view = new StubViewController();

            var entity = CreateCullableEntity(world, 100, 100);
            spatial.Entities.Add(entity);

            var system = new CameraCullingSystem(world, manager, spatial, view);

            // Frame 1: entity is visible
            system.Update(0.016f);
            That(world.Get<CullState>(entity).IsVisible, Is.True, "Frame 1: should be visible");

            // Frame 2: entity no longer returned by spatial query
            spatial.Entities.Clear();
            system.Update(0.016f);

            ref var cull = ref world.Get<CullState>(entity);
            That(cull.IsVisible, Is.False, "Frame 2: previously visible entity should be marked culled");
        }

        // ══════════════════════════════════════════════════════════════
        //  E. Character Position Pipeline
        // ══════════════════════════════════════════════════════════════

        [Test]
        public void Physics2DSync_ExactFixedPointCopy()
        {
            using var world = World.Create();
            var physPos = Position2D.FromCm(12345, 67890);
            var entity = world.Create(
                physPos,
                WorldPositionCm.FromCm(0, 0)
            );

            var system = new Physics2DToWorldPositionSyncSystem(world);
            system.Update(0.02f);

            ref var wp = ref world.Get<WorldPositionCm>(entity);
            That(wp.Value.X.RawValue, Is.EqualTo(physPos.Value.X.RawValue), "X must be bit-exact copy");
            That(wp.Value.Y.RawValue, Is.EqualTo(physPos.Value.Y.RawValue), "Y must be bit-exact copy");
        }

        [Test]
        public void SavePrevious_RetainsOldPosition()
        {
            using var world = World.Create();
            var entity = world.Create(
                WorldPositionCm.FromCm(100, 200),
                new PreviousWorldPositionCm { Value = Fix64Vec2.Zero }
            );

            var saveSystem = new SavePreviousWorldPositionSystem(world);
            saveSystem.Update(0.02f);

            // After save, previous should equal current
            ref var prev = ref world.Get<PreviousWorldPositionCm>(entity);
            That(prev.Value.X.RawValue, Is.EqualTo(Fix64.FromInt(100).RawValue));
            That(prev.Value.Y.RawValue, Is.EqualTo(Fix64.FromInt(200).RawValue));

            // Now change current position
            ref var cur = ref world.Get<WorldPositionCm>(entity);
            cur.Value = Fix64Vec2.FromInt(500, 600);

            // Save again
            saveSystem.Update(0.02f);

            ref var prev2 = ref world.Get<PreviousWorldPositionCm>(entity);
            That(prev2.Value.X.RawValue, Is.EqualTo(Fix64.FromInt(500).RawValue));
            That(prev2.Value.Y.RawValue, Is.EqualTo(Fix64.FromInt(600).RawValue));
        }

        [Test]
        public void VisualSync_HalfAlpha_InterpolatesMidpoint()
        {
            using var world = World.Create();

            // Create PresentationFrameState singleton with alpha=0.5
            world.Create(
                new PresentationFrameState { InterpolationAlpha = 0.5f, Enabled = true },
                new PresentationFrameStateTag()
            );

            var entity = world.Create(
                WorldPositionCm.FromCm(1000, 2000),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(0, 0) },
                VisualTransform.Default
            );

            var system = new WorldToVisualSyncSystem(world);
            system.Update(0.016f);

            ref var visual = ref world.Get<VisualTransform>(entity);
            // Midpoint: (500, 1000) cm → (5, 0, 10) m (XY logic → XZ visual)
            That(visual.Position.X, Is.EqualTo(5f).Within(0.05f));
            That(visual.Position.Y, Is.EqualTo(0f).Within(0.01f));
            That(visual.Position.Z, Is.EqualTo(10f).Within(0.05f));
        }

        [Test]
        public void VisualSync_CullInvisible_SkipsUpdate()
        {
            using var world = World.Create();

            world.Create(
                new PresentationFrameState { InterpolationAlpha = 1f, Enabled = true },
                new PresentationFrameStateTag()
            );

            var sentinel = new Vector3(999f, 999f, 999f);
            var entity = world.Create(
                WorldPositionCm.FromCm(100, 200),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(100, 200) },
                new VisualTransform { Position = sentinel, Rotation = Quaternion.Identity, Scale = Vector3.One },
                new CullState { IsVisible = false, LOD = LODLevel.Culled }
            );

            var system = new WorldToVisualSyncSystem(world);
            system.Update(0.016f);

            ref var visual = ref world.Get<VisualTransform>(entity);
            That(visual.Position, Is.EqualTo(sentinel), "Invisible entity should not have VisualTransform updated");
        }

        [Test]
        public void VisualSync_NoCullState_AlwaysUpdates()
        {
            using var world = World.Create();

            world.Create(
                new PresentationFrameState { InterpolationAlpha = 1f, Enabled = true },
                new PresentationFrameStateTag()
            );

            // Entity WITHOUT CullState component — should always sync
            var entity = world.Create(
                WorldPositionCm.FromCm(300, 400),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(300, 400) },
                new VisualTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity, Scale = Vector3.One }
            );

            var system = new WorldToVisualSyncSystem(world);
            system.Update(0.016f);

            ref var visual = ref world.Get<VisualTransform>(entity);
            // (300, 400) cm → (3, 0, 4) m
            That(visual.Position.X, Is.EqualTo(3f).Within(0.05f));
            That(visual.Position.Z, Is.EqualTo(4f).Within(0.05f));
        }

        // ══════════════════════════════════════════════════════════════
        //  F. Input Edge Detection & Order Building
        // ══════════════════════════════════════════════════════════════

        private static (StubInputBackend backend, PlayerInputHandler handler) BuildSimpleInputHandler()
        {
            var backend = new StubInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Attack", Type = InputActionType.Button },
                    new() { Id = "Move", Type = InputActionType.Button },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 10,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "Attack", Path = "<Keyboard>/a", Processors = new() },
                            new() { ActionId = "Move", Path = "<Mouse>/LeftButton", Processors = new() },
                        }
                    }
                }
            };
            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");
            return (backend, handler);
        }

        [Test]
        public void PlayerInputHandler_PressRelease_EdgeDetection()
        {
            var (backend, handler) = BuildSimpleInputHandler();

            // Frame 1: key not pressed
            handler.Update();
            That(handler.PressedThisFrame("Attack"), Is.False);
            That(handler.ReleasedThisFrame("Attack"), Is.False);

            // Frame 2: press key
            backend.Buttons["<Keyboard>/a"] = true;
            handler.Update();
            That(handler.PressedThisFrame("Attack"), Is.True, "First frame of press should be PressedThisFrame");
            That(handler.ReleasedThisFrame("Attack"), Is.False);

            // Frame 3: key held (still pressed)
            handler.Update();
            That(handler.PressedThisFrame("Attack"), Is.False, "Subsequent held frames should not be PressedThisFrame");
            That(handler.IsDown("Attack"), Is.True);

            // Frame 4: release key
            backend.Buttons["<Keyboard>/a"] = false;
            handler.Update();
            That(handler.ReleasedThisFrame("Attack"), Is.True, "First frame of release should be ReleasedThisFrame");
            That(handler.PressedThisFrame("Attack"), Is.False);
        }

        [Test]
        public void PlayerInputHandler_InputBlocked_SuppressesAll()
        {
            var (backend, handler) = BuildSimpleInputHandler();

            // Press key first to establish triggered state
            backend.Buttons["<Keyboard>/a"] = true;
            handler.Update();
            That(handler.IsDown("Attack"), Is.True);

            // Block input
            handler.InputBlocked = true;
            handler.Update();

            That(handler.IsDown("Attack"), Is.False, "Blocked input should suppress all actions");
            That(handler.PressedThisFrame("Attack"), Is.False);
            That(handler.ReleasedThisFrame("Attack"), Is.False);
        }

        [Test]
        public void InputOrderMapping_NonSkillPress_SubmitsOrder()
        {
            var (backend, handler) = BuildSimpleInputHandler();

            var orderConfig = new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.TargetFirst,
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Move",
                        Trigger = InputTriggerType.PressedThisFrame,
                        OrderTagKey = "moveTo",
                        IsSkillMapping = false,
                        SelectionType = OrderSelectionType.Position,
                        RequireSelection = false
                    }
                }
            };

            var system = new InputOrderMappingSystem(handler, orderConfig);
            system.SetTagKeyResolver(key => key == "moveTo" ? 1 : 0);
            system.SetGroundPositionProvider((out Vector3 pos) =>
            {
                pos = new Vector3(1000, 0, 2000);
                return true;
            });

            Order? capturedOrder = null;
            system.SetOrderSubmitHandler((in Order order) =>
            {
                capturedOrder = order;
            });

            using var world = World.Create();
            var player = world.Create();
            system.SetLocalPlayer(player, 1);

            // Frame 1: no press
            handler.Update();
            system.Update(0.016f);
            That(capturedOrder, Is.Null, "No order before button press");

            // Frame 2: press left mouse button
            backend.Buttons["<Mouse>/LeftButton"] = true;
            handler.Update();
            system.Update(0.016f);

            That(capturedOrder, Is.Not.Null, "Order should be submitted on press");
            That(capturedOrder.Value.OrderTagId, Is.EqualTo(1));
            That(capturedOrder.Value.PlayerId, Is.EqualTo(1));
        }
    }
}
