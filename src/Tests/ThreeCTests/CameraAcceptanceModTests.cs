using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using CameraAcceptanceMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.UI;
using Ludots.UI.Input;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.ThreeC.Acceptance
{
    [TestFixture]
    public sealed class CameraAcceptanceModTests
    {
        private const int BlendSettleFrames = 40;

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraAcceptanceMod"
        };

        [Test]
        public void CameraAcceptanceMod_ProjectionMap_ClickGround_SpawnsEntity_AndEmitsCueMarkerThenExpires()
        {
            using var engine = CreateEngine(AcceptanceMods);
            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            AssertProjectionViewportState(engine);

            var backend = GetInputBackend(engine);
            int beforeDummyCount = CountEntitiesByName(engine.World, "Dummy");
            ClickGround(engine, backend, new Vector2(3200f, 2000f));

            int afterDummyCount = CountEntitiesByName(engine.World, "Dummy");
            Assert.That(afterDummyCount, Is.EqualTo(beforeDummyCount + 1), "Ground click should enqueue a runtime entity spawn.");
            Assert.That(HasNamedEntityAt(engine.World, "Dummy", new WorldCmInt2(3200, 2000)), Is.True, "Spawned entity should land on the raycast point.");

            var primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
            Assert.That(primitives, Is.Not.Null);
            Assert.That(primitives!.Count, Is.GreaterThan(0), "Ground click should emit a transient performer marker.");
            var cueMarkerPosition = WorldUnits.WorldCmToVisualMeters(new WorldCmInt2(3200, 2000), yMeters: 0.15f);
            bool foundCueMarker = false;
            foreach (ref readonly var primitive in primitives.GetSpan())
            {
                if (primitive.Position == cueMarkerPosition)
                {
                    foundCueMarker = true;
                    break;
                }
            }
            Assert.That(foundCueMarker, Is.True, "Ground click should emit a transient cue marker at the raycast point.");

            Tick(engine, 60);
            foundCueMarker = false;
            foreach (ref readonly var primitive in primitives.GetSpan())
            {
                if (primitive.Position == cueMarkerPosition)
                {
                    foundCueMarker = true;
                    break;
                }
            }
            Assert.That(foundCueMarker, Is.False, "Transient marker should expire after its configured lifetime.");
        }

        [Test]
        public void CameraAcceptanceMod_Panel_DoesNotCaptureWorldClicksOutsideCard()
        {
            using var engine = CreateEngine(AcceptanceMods);
            var uiRoot = new UIRoot();
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);

            LoadMap(engine, CameraAcceptanceIds.ProjectionMapId);

            Assert.That(uiRoot.Scene, Is.Not.Null, "Acceptance panel should mount when a UIRoot service is present.");

            bool handledOutside = uiRoot.HandleInput(new PointerEvent
            {
                DeviceType = InputDeviceType.Mouse,
                PointerId = 0,
                Action = PointerAction.Down,
                X = 900f,
                Y = 420f
            });

            bool handledInside = uiRoot.HandleInput(new PointerEvent
            {
                DeviceType = InputDeviceType.Mouse,
                PointerId = 0,
                Action = PointerAction.Down,
                X = 80f,
                Y = 80f
            });

            Assert.That(handledOutside, Is.False, "World clicks outside the panel card must pass through to gameplay input.");
            Assert.That(handledInside, Is.True, "Clicks on the panel card should remain UI-interactive.");
        }

        [Test]
        public void CameraAcceptanceMod_RtsMap_ComposesKeyboardEdgeGrabDragAndZoom()
        {
            using var engine = CreateEngine(AcceptanceMods);
            LoadMap(engine, CameraAcceptanceIds.RtsMapId);

            var backend = GetInputBackend(engine);
            var start = engine.GameSession.Camera.State.TargetCm;
            float startDistance = engine.GameSession.Camera.State.DistanceCm;

            HoldButton(engine, backend, "<Keyboard>/w", frames: 3);
            var afterKeyboard = engine.GameSession.Camera.State.TargetCm;
            Assert.That(afterKeyboard, Is.Not.EqualTo(start), "WASD pan should move the RTS camera target.");

            backend.SetMousePosition(new Vector2(1919f, 540f));
            Tick(engine, 3);
            var afterEdge = engine.GameSession.Camera.State.TargetCm;
            Assert.That(afterEdge, Is.Not.EqualTo(afterKeyboard), "Edge pan should move the RTS camera target.");

            DragMouse(engine, backend, "<Mouse>/MiddleButton", new Vector2(960f, 540f), new Vector2(1060f, 540f));
            var afterDrag = engine.GameSession.Camera.State.TargetCm;
            Assert.That(afterDrag, Is.Not.EqualTo(afterEdge), "Middle-button grab drag should move the RTS camera target.");

            ScrollWheel(engine, backend, 5f);
            Assert.That(engine.GameSession.Camera.State.DistanceCm, Is.LessThan(startDistance), "Mouse wheel should zoom the RTS camera.");
        }

        [Test]
        public void CameraAcceptanceMod_TpsMap_UsesRightMouseAimLookAndZoom()
        {
            using var engine = CreateEngine(AcceptanceMods);
            LoadMap(engine, CameraAcceptanceIds.TpsMapId);
            Tick(engine, BlendSettleFrames);

            var backend = GetInputBackend(engine);
            float startYaw = engine.GameSession.Camera.State.Yaw;
            float startDistance = engine.GameSession.Camera.State.DistanceCm;

            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId, Is.EqualTo(CameraAcceptanceIds.TpsCameraId));
            Assert.That(engine.GameSession.Camera.State.IsFollowing, Is.True);
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm, Is.EqualTo(new Vector2(1200f, 800f)));

            DragMouse(engine, backend, "<Mouse>/RightButton", new Vector2(960f, 540f), new Vector2(1010f, 500f));
            Assert.That(engine.GameSession.Camera.State.Yaw, Is.Not.EqualTo(startYaw), "Right-button aim look should rotate the TPS camera.");

            ScrollWheel(engine, backend, -3f);
            Assert.That(engine.GameSession.Camera.State.DistanceCm, Is.GreaterThan(startDistance), "Mouse wheel should zoom the TPS camera.");
        }

        [TestCase("<Keyboard>/1", CameraAcceptanceIds.BlendCutCameraId, CameraBlendCurve.Cut, false)]
        [TestCase("<Keyboard>/2", CameraAcceptanceIds.BlendLinearCameraId, CameraBlendCurve.Linear, true)]
        [TestCase("<Keyboard>/3", CameraAcceptanceIds.BlendSmoothCameraId, CameraBlendCurve.SmoothStep, true)]
        public void CameraAcceptanceMod_BlendMap_ClickGround_ActivatesRequestedCurve(
            string curveKey,
            string expectedCameraId,
            CameraBlendCurve expectedCurve,
            bool expectedBlending)
        {
            using var engine = CreateEngine(AcceptanceMods);
            LoadMap(engine, CameraAcceptanceIds.BlendMapId);

            var backend = GetInputBackend(engine);
            PressButton(engine, backend, curveKey);
            ClickGround(engine, backend, new Vector2(4800f, 2600f));

            var brain = engine.GameSession.Camera.VirtualCameraBrain;
            Assert.That(brain, Is.Not.Null);
            Assert.That(brain!.ActiveCameraId, Is.EqualTo(expectedCameraId));
            Assert.That(brain.ActiveDefinition?.BlendCurve, Is.EqualTo(expectedCurve));
            Assert.That(brain.IsBlending, Is.EqualTo(expectedBlending));

            Tick(engine, BlendSettleFrames);
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(new Vector2(4800f, 2600f)));
        }

        [Test]
        public void CameraAcceptanceMod_FollowMap_TracksSelectionAndStopsWithoutFallback()
        {
            using var engine = CreateEngine(AcceptanceMods);
            LoadMap(engine, CameraAcceptanceIds.FollowMapId);

            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId, Is.EqualTo(CameraAcceptanceIds.FollowCloseCameraId));
            Assert.That(engine.GameSession.Camera.State.IsFollowing, Is.False);
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(new Vector2(1600f, 1200f)));

            Entity captain = FindEntityByName(engine.World, CameraAcceptanceIds.CaptainName);
            Assert.That(captain, Is.Not.EqualTo(Entity.Null));

            var backend = GetInputBackend(engine);
            ClickGround(engine, backend, new Vector2(3400f, 2200f));
            Tick(engine, 3);
            Assert.That(engine.GameSession.Camera.State.IsFollowing, Is.True);
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm, Is.EqualTo(new Vector2(3400f, 2200f)));
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(new Vector2(3400f, 2200f)));

            ref var position = ref engine.World.Get<WorldPositionCm>(captain);
            position = WorldPositionCm.FromCm(4200, 2800);
            Tick(engine, 3);
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm, Is.EqualTo(new Vector2(4200f, 2800f)));
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(new Vector2(4200f, 2800f)));

            ClickGround(engine, backend, new Vector2(5200f, 4200f));
            Tick(engine, 3);
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm, Is.Null);
            Assert.That(engine.GameSession.Camera.State.IsFollowing, Is.False);
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(new Vector2(4200f, 2800f)), "Losing the follow target should leave the camera in place.");
        }

        [Test]
        public void CameraAcceptanceMod_MapLoad_PreservesSpatialQueryServiceIdentity_ForPreRegisteredSystems()
        {
            using var engine = CreateEngine(AcceptanceMods);
            var beforeLoad = engine.SpatialQueries;

            LoadMap(engine, CameraAcceptanceIds.FollowMapId);

            Assert.That(ReferenceEquals(beforeLoad, engine.SpatialQueries), Is.True,
                "Map load must hot-swap the spatial query backend on the shared service instead of replacing the service instance.");
            Assert.That(ReferenceEquals(beforeLoad, engine.GetService(CoreServiceKeys.SpatialQueryService)), Is.True,
                "GlobalContext must continue exposing the same shared spatial query service instance after map load.");
        }

        [Test]
        public void CameraAcceptanceMod_StackMap_ClearWalksBackThroughHigherPriorityShots()
        {
            using var engine = CreateEngine(AcceptanceMods);
            LoadMap(engine, CameraAcceptanceIds.StackMapId);

            var backend = GetInputBackend(engine);
            var brain = engine.GameSession.Camera.VirtualCameraBrain;
            Assert.That(brain, Is.Not.Null);
            Assert.That(brain!.ActiveCameraId, Is.EqualTo(CameraAcceptanceIds.TpsCameraId));
            Assert.That(brain.IsActive(CameraAcceptanceIds.TpsCameraId), Is.True);

            PressButton(engine, backend, "<Keyboard>/r");
            TickUntil(engine, () => engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId == CameraAcceptanceIds.StackRevealShotId);
            Assert.That(brain.IsActive(CameraAcceptanceIds.TpsCameraId), Is.True);

            PressButton(engine, backend, "<Keyboard>/t");
            TickUntil(engine, () => engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId == CameraAcceptanceIds.StackAlertShotId);
            Assert.That(brain.IsActive(CameraAcceptanceIds.StackRevealShotId), Is.True);

            PressButton(engine, backend, "<Keyboard>/enter");
            TickUntil(engine, () => engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId == CameraAcceptanceIds.StackRevealShotId);

            PressButton(engine, backend, "<Keyboard>/enter");
            TickUntil(engine, () => engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId == CameraAcceptanceIds.TpsCameraId);
            Assert.That(engine.GameSession.Camera.State.IsFollowing, Is.True);
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm, Is.EqualTo(new Vector2(1200f, 800f)));
        }

        private static GameEngine CreateEngine(params string[] modIds)
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, modIds);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);
            var view = new StubViewController(1920, 1080);
            engine.SetService(CoreServiceKeys.ViewController, view);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, new WorldMappedScreenRayProvider());
            var culling = new CameraCullingSystem(engine.World, engine.GameSession.Camera, engine.SpatialQueries, view);
            engine.RegisterPresentationSystem(culling);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, culling.DebugState);
            engine.Start();
            return engine;
        }

        private static void LoadMap(GameEngine engine, string mapId, int frames = 5)
        {
            engine.LoadMap(mapId);
            Assert.That(engine.CurrentMapSession?.PrimaryBoard, Is.Not.Null, $"{mapId} must declare a primary board.");
            Tick(engine, frames);
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var backend = new TestInputBackend();
            var inputHandler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.GlobalContext["Tests.CameraAcceptanceMod.InputBackend"] = backend;
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.Tick(1f / 60f);
            }
        }

        private static void TickUntil(GameEngine engine, Func<bool> predicate, int maxFrames = 60)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (predicate())
                {
                    return;
                }

                engine.Tick(1f / 60f);
            }

            Assert.That(predicate(), Is.True, $"Predicate was not satisfied within {maxFrames} frames.");
        }

        private static void HoldButton(GameEngine engine, TestInputBackend backend, string path, int frames)
        {
            backend.SetButton(path, true);
            Tick(engine, frames);
            backend.SetButton(path, false);
            Tick(engine, 1);
        }

        private static void PressButton(GameEngine engine, TestInputBackend backend, string path)
        {
            backend.SetButton(path, true);
            Tick(engine, 1);
            backend.SetButton(path, false);
            Tick(engine, 1);
        }

        private static void ScrollWheel(GameEngine engine, TestInputBackend backend, float delta)
        {
            backend.SetMouseWheel(delta);
            Tick(engine, 1);
            backend.SetMouseWheel(0f);
            Tick(engine, 2);
        }

        private static void DragMouse(GameEngine engine, TestInputBackend backend, string holdPath, Vector2 from, Vector2 to)
        {
            backend.SetMousePosition(from);
            Tick(engine, 1);
            backend.SetButton(holdPath, true);
            Tick(engine, 1);
            backend.SetMousePosition(to);
            Tick(engine, 1);
            backend.SetButton(holdPath, false);
            Tick(engine, 1);
        }

        private static void ClickGround(GameEngine engine, TestInputBackend backend, Vector2 worldPointCm)
        {
            backend.SetMousePosition(worldPointCm);
            Tick(engine, 1);
            backend.SetButton("<Mouse>/LeftButton", true);
            Tick(engine, 1);
            backend.SetButton("<Mouse>/LeftButton", false);
            Tick(engine, 1);
        }

        private static Entity FindEntityByName(World world, string name)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name entityName) =>
            {
                if (string.Equals(entityName.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });
            return result;
        }

        private static int CountEntitiesByName(World world, string name)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (ref Name entityName) =>
            {
                if (string.Equals(entityName.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            });
            return count;
        }

        private static bool HasNamedEntityAt(World world, string name, in WorldCmInt2 worldCm)
        {
            bool found = false;
            int targetX = worldCm.X;
            int targetY = worldCm.Y;
            var query = new QueryDescription().WithAll<Name, WorldPositionCm>();
            world.Query(in query, (ref Name entityName, ref WorldPositionCm position) =>
            {
                if (found)
                {
                    return;
                }

                if (!string.Equals(entityName.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var positionCm = position.Value.ToWorldCmInt2();
                found = positionCm.X == targetX && positionCm.Y == targetY;
            });
            return found;
        }

        private static void AssertProjectionViewportState(GameEngine engine)
        {
            var culling = engine.GetService(CoreServiceKeys.CameraCullingDebugState);
            Assert.That(culling, Is.Not.Null, "Projection acceptance should surface core culling debug state.");

            bool hasCullTrackedNamedEntity = false;
            var query = new QueryDescription().WithAll<Name, CullState>();
            engine.World.Query(in query, (Entity entity, ref Name name, ref CullState cull) =>
            {
                hasCullTrackedNamedEntity = true;
            });

            Assert.That(hasCullTrackedNamedEntity, Is.True, "Projection acceptance should attach core CullState tracking to scenario entities.");
        }

        private static TestInputBackend GetInputBackend(GameEngine engine)
        {
            return engine.GlobalContext["Tests.CameraAcceptanceMod.InputBackend"] as TestInputBackend
                ?? throw new InvalidOperationException("Test input backend is missing.");
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                var srcDir = Path.Combine(dir.FullName, "src");
                var assetsDir = Path.Combine(dir.FullName, "assets");
                if (Directory.Exists(srcDir) && Directory.Exists(assetsDir))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }

        private sealed class TestInputBackend : IInputBackend
        {
            private readonly Dictionary<string, bool> _buttons = new(StringComparer.Ordinal);
            private Vector2 _mousePosition;
            private float _mouseWheel;

            public void SetButton(string path, bool isDown)
            {
                _buttons[path] = isDown;
            }

            public void SetMousePosition(Vector2 position)
            {
                _mousePosition = position;
            }

            public void SetMouseWheel(float wheel)
            {
                _mouseWheel = wheel;
            }

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _buttons.TryGetValue(devicePath, out var isDown) && isDown;
            public Vector2 GetMousePosition() => _mousePosition;
            public float GetMouseWheel() => _mouseWheel;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private sealed class StubViewController : IViewController
        {
            public StubViewController(float width, float height)
            {
                Resolution = new Vector2(width, height);
            }

            public Vector2 Resolution { get; }
            public float Fov => 60f;
            public float AspectRatio => Resolution.Y <= 0f ? 1f : Resolution.X / Resolution.Y;
        }

        private sealed class WorldMappedScreenRayProvider : IScreenRayProvider
        {
            public ScreenRay GetRay(Vector2 screenPosition)
            {
                return new ScreenRay(
                    new Vector3(screenPosition.X / 100f, 10f, screenPosition.Y / 100f),
                    -Vector3.UnitY);
            }
        }
    }
}
