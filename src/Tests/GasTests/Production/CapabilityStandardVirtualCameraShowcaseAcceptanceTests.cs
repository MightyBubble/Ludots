using System;
using System.IO;
using System.Numerics;
using CapabilityStandardVirtualCameraShowcaseMod;
using CoreInputMod;
using CoreInputMod.ViewMode;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Attributes;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    [Category("acceptance")]
    public sealed class CapabilityStandardVirtualCameraShowcaseAcceptanceTests
    {
        private const int BlendSettleFrames = 30;
        private const string TestInputBackendKey = "Tests.CapabilityStandardVirtualCamera.InputBackend";

        private static readonly string[] ShowcaseMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraBootstrapMod",
            "VirtualCameraShotsMod",
            "CapabilityStandardVirtualCameraShowcaseMod"
        };

        private string? _tempRoot;

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(_tempRoot) && Directory.Exists(_tempRoot))
            {
                try
                {
                    Directory.Delete(_tempRoot, recursive: true);
                }
                catch (IOException ex)
                {
                    Assert.Fail($"Failed to delete temporary camera showcase test directory '{_tempRoot}': {ex}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    Assert.Fail($"Failed to delete temporary camera showcase test directory '{_tempRoot}': {ex}");
                }
            }
        }

        [Test]
        public void Showcase_LoadsDefaultCameraBootstrapAndTaggedShot()
        {
            using var engine = CreateEngine(ShowcaseMods);
            engine.LoadStartupMap();
            Tick(engine, BlendSettleFrames);

            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            Assert.That(engine.CurrentMapSession?.MapId.Value, Is.EqualTo(CapabilityStandardVirtualCameraShowcaseIds.MapId));
            Assert.That(engine.GlobalContext.ContainsKey(CapabilityStandardVirtualCameraShowcaseIds.RuntimeStateKey), Is.True);

            var registry = engine.GetService(CoreServiceKeys.VirtualCameraRegistry);
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry!.TryGet(CapabilityStandardVirtualCameraShowcaseIds.TacticalCameraId, out var tactical), Is.True);
            Assert.That(registry.TryGet(CapabilityStandardVirtualCameraShowcaseIds.BehaviorOrbitCameraId, out var behaviorOrbit), Is.True);
            Assert.That(registry.TryGet(CapabilityStandardVirtualCameraShowcaseIds.HeightmapOrbitCameraId, out var heightmapOrbit), Is.True);
            Assert.That(registry.TryGet(CapabilityStandardVirtualCameraShowcaseIds.TpsCameraId, out var tps), Is.True);
            Assert.That(registry.TryGet(CapabilityStandardVirtualCameraShowcaseIds.FpsCameraId, out var fps), Is.True);
            Assert.That(registry.TryGet(CapabilityStandardVirtualCameraShowcaseIds.RevealShotCameraId, out var shot), Is.True);
            Assert.That(engine.GetService(CoreServiceKeys.VisualHeightmap), Is.Not.Null);
            Assert.That(tactical.AllowUserInput, Is.True);
            Assert.That(behaviorOrbit!.RotateRequiresHold, Is.True);
            Assert.That(heightmapOrbit!.TargetHeightMode, Is.EqualTo(VirtualCameraTargetHeightMode.VisualHeightmap));
            Assert.That(heightmapOrbit.RotateRequiresHold, Is.True);
            Assert.That(tps!.RigKind, Is.EqualTo(CameraRigKind.ThirdPerson));
            Assert.That(tps.FollowTargetKind, Is.EqualTo(CameraFollowTargetKind.SolePossessedRep));
            Assert.That(tps.RigPivotOffsetCm, Is.EqualTo(new Vector3(65f, 35f, 90f)));
            Assert.That(tps.RigCameraOffsetCm, Is.EqualTo(new Vector3(70f, 10f, -20f)));
            Assert.That(tps.RotateRequiresHold, Is.False);
            Assert.That(fps!.RigKind, Is.EqualTo(CameraRigKind.FirstPerson));
            Assert.That(fps.DistanceCm, Is.EqualTo(0f).Within(0.001f));
            Assert.That(fps.FollowTargetKind, Is.EqualTo(CameraFollowTargetKind.SolePossessedRep));
            Assert.That(fps.RotateRequiresHold, Is.False);
            Assert.That(engine.GetService(CoreServiceKeys.CameraImpulseRuntime), Is.Not.Null);
            AssertCameraActionBinding(engine, CapabilityStandardVirtualCameraShowcaseIds.MoveActionId, CameraBehaviorAttributes.MoveX);
            AssertCameraActionBinding(engine, CapabilityStandardVirtualCameraShowcaseIds.MoveActionId, CameraBehaviorAttributes.MoveY);
            AssertCameraActionBinding(engine, CapabilityStandardVirtualCameraShowcaseIds.PointerPositionActionId, CameraBehaviorAttributes.PointerX);
            AssertCameraActionBinding(engine, CapabilityStandardVirtualCameraShowcaseIds.PointerPositionActionId, CameraBehaviorAttributes.PointerY);
            AssertCameraActionBinding(engine, CapabilityStandardVirtualCameraShowcaseIds.PointerPositionActionId, CameraBehaviorAttributes.PointerActive);
            AssertCameraActionBinding(engine, CapabilityStandardVirtualCameraShowcaseIds.PointerDeltaActionId, CameraBehaviorAttributes.PointerDeltaX);
            AssertCameraActionBinding(engine, CapabilityStandardVirtualCameraShowcaseIds.PointerDeltaActionId, CameraBehaviorAttributes.PointerDeltaY);
            AssertCameraActionBinding(engine, CapabilityStandardVirtualCameraShowcaseIds.GrabDragHoldActionId, CameraBehaviorAttributes.GrabDragHold);
            AssertCameraActionBinding(engine, CapabilityStandardVirtualCameraShowcaseIds.RotateLeftActionId, CameraBehaviorAttributes.RotateLeft);
            AssertCameraActionBinding(engine, CapabilityStandardVirtualCameraShowcaseIds.RotateRightActionId, CameraBehaviorAttributes.RotateRight);
            AssertCameraActionBinding(engine, CapabilityStandardVirtualCameraShowcaseIds.ZoomActionId, CameraBehaviorAttributes.Zoom);
            AssertActionAttributeBinding(
                engine,
                CapabilityStandardVirtualCameraShowcaseIds.AvatarMoveActionId,
                CapabilityStandardVirtualCameraShowcaseIds.AvatarMoveXAttribute,
                InputActionAttributeTargetKind.SolePossessedRep);
            AssertActionAttributeBinding(
                engine,
                CapabilityStandardVirtualCameraShowcaseIds.AvatarMoveActionId,
                CapabilityStandardVirtualCameraShowcaseIds.AvatarMoveYAttribute,
                InputActionAttributeTargetKind.SolePossessedRep);
            AssertCameraActionPreservesUntilSnapshot(engine, CapabilityStandardVirtualCameraShowcaseIds.ZoomActionId, CameraBehaviorAttributes.Zoom);
            Assert.That(shot.AllowUserInput, Is.False);
            Assert.That(shot.EnableZoom, Is.False);

            var brain = engine.GameSession.Camera.VirtualCameraBrain;
            Assert.That(brain, Is.Not.Null);
            Assert.That(brain!.ActiveCameraId, Is.EqualTo(CapabilityStandardVirtualCameraShowcaseIds.RevealShotCameraId));
            Assert.That(brain.IsActive(CapabilityStandardVirtualCameraShowcaseIds.TacticalCameraId), Is.True);
            Assert.That(engine.GameSession.Camera.State.RigKind, Is.EqualTo(CameraRigKind.TopDown));
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(new Vector2(4200f, 2600f)));
            Assert.That(engine.GameSession.Camera.State.DistanceCm, Is.EqualTo(9180f).Within(0.001f));

            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest { Clear = true });
            TickUntil(engine, () => engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId == CapabilityStandardVirtualCameraShowcaseIds.TacticalCameraId);
            Tick(engine, BlendSettleFrames);

            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId, Is.EqualTo(CapabilityStandardVirtualCameraShowcaseIds.TacticalCameraId));
            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.AllowsInput, Is.True);
        }

        [Test]
        public void Showcase_ViewModeSwitchActivatesBehaviorCameraAndZoomBehavior()
        {
            using var engine = CreateEngine(ShowcaseMods);
            engine.LoadStartupMap();
            Tick(engine, BlendSettleFrames);

            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest { Clear = true });
            Tick(engine, BlendSettleFrames);

            var manager = engine.GetService(CoreInputServiceKeys.ViewModeManager)
                ?? throw new InvalidOperationException("ViewModeManager is required.");
            Assert.That(manager.SwitchTo(CapabilityStandardVirtualCameraShowcaseIds.BehaviorOrbitModeId), Is.True);
            Tick(engine, BlendSettleFrames);

            var brain = engine.GameSession.Camera.VirtualCameraBrain;
            Assert.That(brain?.ActiveCameraId, Is.EqualTo(CapabilityStandardVirtualCameraShowcaseIds.BehaviorOrbitCameraId));
            Assert.That(brain?.AllowsInput, Is.True);
            AssertCameraActionBinding(engine, CapabilityStandardVirtualCameraShowcaseIds.RotateHoldActionId, CameraBehaviorAttributes.RotateHold);
            AssertCameraActionBinding(engine, CapabilityStandardVirtualCameraShowcaseIds.LookActionId, CameraBehaviorAttributes.LookX);
            AssertCameraActionBinding(engine, CapabilityStandardVirtualCameraShowcaseIds.LookActionId, CameraBehaviorAttributes.LookY);
            AssertCameraActionPreservesUntilSnapshot(engine, CapabilityStandardVirtualCameraShowcaseIds.LookActionId, CameraBehaviorAttributes.LookX);
            Assert.That(engine.GameSession.Camera.State.DistanceCm, Is.EqualTo(5600f).Within(0.001f));

            var input = engine.GetService(CoreServiceKeys.InputHandler)
                ?? throw new InvalidOperationException("InputHandler is required.");
            var behaviorInput = engine.GetService(CoreServiceKeys.CameraBehaviorInputState)
                ?? throw new InvalidOperationException("CameraBehaviorInputState is required.");
            Assert.That(input.HasAction(CapabilityStandardVirtualCameraShowcaseIds.ZoomActionId), Is.True);
            Assert.That(engine.GetService(CoreServiceKeys.UiCaptured), Is.False);
            int tickBeforeZoom = engine.GameSession.CurrentTick;
            input.InjectAction(CapabilityStandardVirtualCameraShowcaseIds.ZoomActionId, new Vector3(1f, 0f, 0f));
            Tick(engine, 1);
            Assert.That(input.ReadAction<float>(CapabilityStandardVirtualCameraShowcaseIds.ZoomActionId), Is.EqualTo(1f).Within(0.001f));
            if (engine.GameSession.CurrentTick <= tickBeforeZoom)
            {
                TickUntil(engine, () => engine.GameSession.CurrentTick > tickBeforeZoom, maxFrames: 4);
            }

            var authoritativeInput = engine.GetService(CoreServiceKeys.AuthoritativeInput)
                ?? throw new InvalidOperationException("AuthoritativeInput is required.");
            Assert.That(authoritativeInput.ReadAction<float>(CapabilityStandardVirtualCameraShowcaseIds.ZoomActionId), Is.EqualTo(1f).Within(0.001f));
            Assert.That(behaviorInput.Zoom, Is.EqualTo(1f).Within(0.001f));

            Assert.That(engine.GameSession.Camera.State.DistanceCm, Is.EqualTo(4700f).Within(0.001f));
        }

        [Test]
        public void Showcase_ViewModeSwitchesCoverHeightmapTpsAndFps()
        {
            using var engine = CreateEngine(ShowcaseMods);
            engine.LoadStartupMap();
            Tick(engine, BlendSettleFrames);

            var localPlayerPositionCm = new Vector2(1200f, 800f);

            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest { Clear = true });
            Tick(engine, BlendSettleFrames);

            var manager = engine.GetService(CoreInputServiceKeys.ViewModeManager)
                ?? throw new InvalidOperationException("ViewModeManager is required.");
            var heightmap = engine.GetService(CoreServiceKeys.VisualHeightmap)
                ?? throw new InvalidOperationException("VisualHeightmap is required.");
            var input = engine.GetService(CoreServiceKeys.InputHandler)
                ?? throw new InvalidOperationException("InputHandler is required.");
            var localPlayer = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            Assert.That(engine.World.IsAlive(localPlayer), Is.True);
            Assert.That(engine.World.Has<WorldPositionCm>(localPlayer), Is.True);
            Assert.That(engine.World.Has<FacingDirection>(localPlayer), Is.True);
            Assert.That(engine.World.Has<VisualTransform>(localPlayer), Is.True);

            Assert.That(input.HasAction(CapabilityStandardVirtualCameraShowcaseIds.BehaviorOrbitModeActionId), Is.True);
            Assert.That(input.HasAction(CapabilityStandardVirtualCameraShowcaseIds.HeightmapOrbitModeActionId), Is.True);
            Assert.That(input.HasAction(CapabilityStandardVirtualCameraShowcaseIds.TpsModeActionId), Is.True);
            Assert.That(input.HasAction(CapabilityStandardVirtualCameraShowcaseIds.FpsModeActionId), Is.True);
            Assert.That(input.HasAction(CapabilityStandardVirtualCameraShowcaseIds.AvatarMoveActionId), Is.True);
            var backend = (TestInputBackend)engine.GlobalContext[TestInputBackendKey];

            Assert.That(manager.SwitchTo(CapabilityStandardVirtualCameraShowcaseIds.HeightmapOrbitModeId), Is.True);
            Tick(engine, BlendSettleFrames);

            var brain = engine.GameSession.Camera.VirtualCameraBrain
                ?? throw new InvalidOperationException("VirtualCameraBrain is required.");
            Assert.That(brain.ActiveCameraId, Is.EqualTo(CapabilityStandardVirtualCameraShowcaseIds.HeightmapOrbitCameraId));
            Assert.That(brain.ActiveDefinition?.TargetHeightMode, Is.EqualTo(VirtualCameraTargetHeightMode.VisualHeightmap));
            Assert.That(heightmap.TrySampleHeightCm(5000f, 5000f, out float terrainHeightCm, layerIndex: 0), Is.True);
            Assert.That(
                engine.GameSession.Camera.State.TargetHeightCm,
                Is.EqualTo(terrainHeightCm + brain.ActiveDefinition!.TargetHeightOffsetCm).Within(0.01f));

            Assert.That(manager.SwitchTo(CapabilityStandardVirtualCameraShowcaseIds.TpsModeId), Is.True);
            Tick(engine, BlendSettleFrames);

            Assert.That(brain.ActiveCameraId, Is.EqualTo(CapabilityStandardVirtualCameraShowcaseIds.TpsCameraId));
            Assert.That(brain.ActiveDefinition?.RigKind, Is.EqualTo(CameraRigKind.ThirdPerson));
            Assert.That(brain.ActiveDefinition?.FollowTargetKind, Is.EqualTo(CameraFollowTargetKind.SolePossessedRep));
            Assert.That(brain.ActiveFollowTargetPositionCm, Is.EqualTo(localPlayerPositionCm));
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(localPlayerPositionCm));
            float tpsYawBeforeLook = engine.GameSession.Camera.State.Yaw;
            int tickBeforeTpsLook = engine.GameSession.CurrentTick;
            backend.SetMousePosition(new Vector2(20f, 0f));
            TickUntil(engine, () => engine.GameSession.CurrentTick > tickBeforeTpsLook, maxFrames: 8);
            Assert.That(MathF.Abs(engine.GameSession.Camera.State.Yaw - tpsYawBeforeLook), Is.GreaterThan(0.001f));

            int tickBeforeMove = engine.GameSession.CurrentTick;
            backend.SetButton("<Keyboard>/w", true);
            TickUntil(engine, () => engine.GameSession.CurrentTick > tickBeforeMove, maxFrames: 8);
            backend.SetButton("<Keyboard>/w", false);
            Assert.That(engine.GetService(CoreServiceKeys.UiCaptured), Is.False);
            Assert.That(
                input.ReadAction<Vector2>(CapabilityStandardVirtualCameraShowcaseIds.AvatarMoveActionId),
                Is.EqualTo(new Vector2(0f, 1f)));

            var authoritativeInput = engine.GetService(CoreServiceKeys.AuthoritativeInput)
                ?? throw new InvalidOperationException("AuthoritativeInput is required.");
            Assert.That(
                authoritativeInput.ReadAction<Vector2>(CapabilityStandardVirtualCameraShowcaseIds.AvatarMoveActionId),
                Is.EqualTo(new Vector2(0f, 1f)));

            int avatarMoveX = AttributeRegistry.GetId(CapabilityStandardVirtualCameraShowcaseIds.AvatarMoveXAttribute);
            int avatarMoveY = AttributeRegistry.GetId(CapabilityStandardVirtualCameraShowcaseIds.AvatarMoveYAttribute);
            ref var localPlayerAttributes = ref engine.World.Get<Ludots.Core.Gameplay.GAS.Components.AttributeBuffer>(localPlayer);
            Assert.That(localPlayerAttributes.GetCurrent(avatarMoveX), Is.EqualTo(0f).Within(0.001f));
            Assert.That(localPlayerAttributes.GetCurrent(avatarMoveY), Is.EqualTo(1f).Within(0.001f));

            Vector2 movedLocalPlayerPositionCm = engine.World.Get<WorldPositionCm>(localPlayer).Value.ToVector2();
            Assert.That(Vector2.DistanceSquared(movedLocalPlayerPositionCm, localPlayerPositionCm), Is.GreaterThan(1f));
            Assert.That(brain.ActiveFollowTargetPositionCm, Is.EqualTo(movedLocalPlayerPositionCm));
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(movedLocalPlayerPositionCm));
            Assert.That(
                heightmap.TrySampleHeightCm(movedLocalPlayerPositionCm.X, movedLocalPlayerPositionCm.Y, out float localTerrainHeightCm, layerIndex: 0),
                Is.True);
            Assert.That(
                engine.GameSession.Camera.State.TargetHeightCm,
                Is.EqualTo(localTerrainHeightCm + brain.ActiveDefinition!.TargetHeightOffsetCm).Within(0.01f));

            Assert.That(manager.SwitchTo(CapabilityStandardVirtualCameraShowcaseIds.FpsModeId), Is.True);
            Tick(engine, BlendSettleFrames);

            Assert.That(brain.ActiveCameraId, Is.EqualTo(CapabilityStandardVirtualCameraShowcaseIds.FpsCameraId));
            Assert.That(brain.ActiveDefinition?.RigKind, Is.EqualTo(CameraRigKind.FirstPerson));
            Assert.That(brain.ActiveDefinition?.FollowTargetKind, Is.EqualTo(CameraFollowTargetKind.SolePossessedRep));
            Assert.That(brain.ActiveFollowTargetPositionCm, Is.EqualTo(movedLocalPlayerPositionCm));
            Assert.That(engine.GameSession.Camera.State.DistanceCm, Is.EqualTo(0f).Within(0.001f));
            Assert.That(
                engine.GameSession.Camera.State.TargetHeightCm,
                Is.EqualTo(localTerrainHeightCm + brain.ActiveDefinition!.TargetHeightOffsetCm).Within(0.01f));
            float fpsYawBeforeLook = engine.GameSession.Camera.State.Yaw;
            int tickBeforeFpsLook = engine.GameSession.CurrentTick;
            backend.SetMousePosition(new Vector2(40f, 0f));
            TickUntil(engine, () => engine.GameSession.CurrentTick > tickBeforeFpsLook, maxFrames: 8);
            Assert.That(MathF.Abs(engine.GameSession.Camera.State.Yaw - fpsYawBeforeLook), Is.GreaterThan(0.001f));

            int tickBeforeFpsMove = engine.GameSession.CurrentTick;
            backend.SetButton("<Keyboard>/w", true);
            TickUntil(engine, () => engine.GameSession.CurrentTick > tickBeforeFpsMove, maxFrames: 8);
            backend.SetButton("<Keyboard>/w", false);

            Vector2 movedFpsLocalPlayerPositionCm = engine.World.Get<WorldPositionCm>(localPlayer).Value.ToVector2();
            Assert.That(Vector2.DistanceSquared(movedFpsLocalPlayerPositionCm, movedLocalPlayerPositionCm), Is.GreaterThan(1f));
            Assert.That(brain.ActiveFollowTargetPositionCm, Is.EqualTo(movedFpsLocalPlayerPositionCm));
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(movedFpsLocalPlayerPositionCm));
            Assert.That(
                heightmap.TrySampleHeightCm(movedFpsLocalPlayerPositionCm.X, movedFpsLocalPlayerPositionCm.Y, out float fpsTerrainHeightCm, layerIndex: 0),
                Is.True);
            Assert.That(
                engine.GameSession.Camera.State.TargetHeightCm,
                Is.EqualTo(fpsTerrainHeightCm + brain.ActiveDefinition!.TargetHeightOffsetCm).Within(0.01f));
        }

        [Test]
        public void Showcase_FunctionKeysSwitchToHeightmapAndTpsModes()
        {
            using var engine = CreateEngine(ShowcaseMods);
            engine.LoadStartupMap();
            Tick(engine, BlendSettleFrames);

            var backend = (TestInputBackend)engine.GlobalContext[TestInputBackendKey];
            var brain = engine.GameSession.Camera.VirtualCameraBrain
                ?? throw new InvalidOperationException("VirtualCameraBrain is required.");

            PressKeyUntil(
                engine,
                backend,
                "<Keyboard>/f6",
                () => brain.ActiveCameraId == CapabilityStandardVirtualCameraShowcaseIds.HeightmapOrbitCameraId);

            Assert.That(
                engine.GlobalContext[CoreInputMod.ViewMode.ViewModeManager.ActiveModeIdKey],
                Is.EqualTo(CapabilityStandardVirtualCameraShowcaseIds.HeightmapOrbitModeId));
            Assert.That(brain.ActiveCameraId, Is.EqualTo(CapabilityStandardVirtualCameraShowcaseIds.HeightmapOrbitCameraId));

            PressKeyUntil(
                engine,
                backend,
                "<Keyboard>/f7",
                () => brain.ActiveCameraId == CapabilityStandardVirtualCameraShowcaseIds.TpsCameraId);

            Assert.That(
                engine.GlobalContext[CoreInputMod.ViewMode.ViewModeManager.ActiveModeIdKey],
                Is.EqualTo(CapabilityStandardVirtualCameraShowcaseIds.TpsModeId));
            Assert.That(brain.ActiveCameraId, Is.EqualTo(CapabilityStandardVirtualCameraShowcaseIds.TpsCameraId));
            Assert.That(brain.ActiveDefinition?.RigKind, Is.EqualTo(CameraRigKind.ThirdPerson));
        }

        [Test]
        public void Loader_RejectsUnknownVirtualCameraFields()
        {
            WriteConfig("Core", "config_catalog.json",
                """[{ "Path": "Camera/virtual_cameras.json", "Policy": "ArrayById", "IdField": "id" }]""");
            WriteConfig("Core", "Camera/virtual_cameras.json",
                """
                [
                  {
                    "id": "Bad.Camera",
                    "viewMode": "MapDefault",
                    "distanceCm": 3000,
                    "fovYDeg": 60
                  }
                ]
                """);

            var ex = Assert.Throws<InvalidOperationException>(() => LoadVirtualCameraDefinitionsFromTempRoot());
            Assert.That(ex!.ToString(), Does.Contain("viewMode"));
        }

        [Test]
        public void Loader_RequiresExplicitBehaviorSwitchesForInteractiveCameras()
        {
            WriteConfig("Core", "config_catalog.json",
                """[{ "Path": "Camera/virtual_cameras.json", "Policy": "ArrayById", "IdField": "id" }]""");
            WriteConfig("Core", "Camera/virtual_cameras.json",
                """
                [
                  {
                    "id": "Bad.Interactive.Camera",
                    "distanceCm": 3000,
                    "fovYDeg": 60,
                    "panMode": "Keyboard",
                    "rotateMode": "None",
                    "allowUserInput": true
                  }
                ]
                """);

            var ex = Assert.Throws<InvalidOperationException>(() => LoadVirtualCameraDefinitionsFromTempRoot());
            Assert.That(ex!.ToString(), Does.Contain("enableZoom"));
        }

        [Test]
        public void Loader_RequiresExplicitRotateHoldSwitchForDragRotateCameras()
        {
            WriteConfig("Core", "config_catalog.json",
                """[{ "Path": "Camera/virtual_cameras.json", "Policy": "ArrayById", "IdField": "id" }]""");
            WriteConfig("Core", "Camera/virtual_cameras.json",
                """
                [
                  {
                    "id": "Bad.DragRotate.Camera",
                    "distanceCm": 3000,
                    "fovYDeg": 60,
                    "minPitchDeg": -20,
                    "maxPitchDeg": 60,
                    "panMode": "None",
                    "rotateMode": "DragRotate",
                    "rotateDegPerPixel": 0.2,
                    "enableZoom": false,
                    "allowUserInput": true
                  }
                ]
                """);

            var ex = Assert.Throws<InvalidOperationException>(() => LoadVirtualCameraDefinitionsFromTempRoot());
            Assert.That(ex!.ToString(), Does.Contain("rotateRequiresHold"));
        }

        private static GameEngine CreateEngine(params string[] modIds)
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, modIds);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);
            engine.Start();
            var behaviorInput = engine.GetService(CoreServiceKeys.CameraBehaviorInputState)
                ?? throw new InvalidOperationException("CameraBehaviorInputState is required.");
            engine.GameSession.Camera.ConfigureRuntime(
                behaviorInput,
                new StubViewController(),
                () => engine.WorldSizeSpec.Bounds,
                () => engine.GetService(CoreServiceKeys.VisualHeightmap));
            return engine;
        }

        private static void AssertCameraActionBinding(GameEngine engine, string actionId, string attributeName)
        {
            AssertActionAttributeBinding(
                engine,
                actionId,
                attributeName,
                InputActionAttributeTargetKind.CameraBehaviorInput);
        }

        private static void AssertActionAttributeBinding(
            GameEngine engine,
            string actionId,
            string attributeName,
            InputActionAttributeTargetKind target)
        {
            var registry = engine.GetService(CoreServiceKeys.InputActionAttributeBindingRegistry)
                ?? throw new InvalidOperationException("InputActionAttributeBindingRegistry is required.");
            int attributeId = AttributeRegistry.GetId(attributeName);
            Assert.That(attributeId, Is.Not.EqualTo(AttributeRegistry.InvalidId), $"Attribute '{attributeName}' must be registered.");

            InputActionAttributeBindingEntry[] entries = registry.Entries;
            for (int i = 0; i < entries.Length; i++)
            {
                if (string.Equals(entries[i].ActionId, actionId, StringComparison.Ordinal) &&
                    entries[i].AttributeId == attributeId)
                {
                    Assert.That(entries[i].Target, Is.EqualTo(target));
                    return;
                }
            }

            Assert.Fail($"Expected action '{actionId}' to write attribute '{attributeName}' on target '{target}'.");
        }

        private static void AssertCameraActionPreservesUntilSnapshot(GameEngine engine, string actionId, string attributeName)
        {
            var registry = engine.GetService(CoreServiceKeys.InputActionAttributeBindingRegistry)
                ?? throw new InvalidOperationException("InputActionAttributeBindingRegistry is required.");
            int attributeId = AttributeRegistry.GetId(attributeName);
            InputActionAttributeBindingEntry[] entries = registry.Entries;
            for (int i = 0; i < entries.Length; i++)
            {
                if (string.Equals(entries[i].ActionId, actionId, StringComparison.Ordinal) &&
                    entries[i].AttributeId == attributeId)
                {
                    Assert.That(entries[i].PreserveValueUntilSnapshot, Is.True);
                    return;
                }
            }

            Assert.Fail($"Expected action '{actionId}' to preserve camera behavior attribute '{attributeName}' until snapshot.");
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
            engine.GlobalContext[TestInputBackendKey] = backend;
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

        private static void PressKeyUntil(
            GameEngine engine,
            TestInputBackend backend,
            string keyPath,
            Func<bool> predicate,
            int maxFrames = 12)
        {
            backend.SetButton(keyPath, true);
            try
            {
                TickUntil(engine, predicate, maxFrames);
            }
            finally
            {
                backend.SetButton(keyPath, false);
                Tick(engine, 2);
            }
        }

        private void LoadVirtualCameraDefinitionsFromTempRoot()
        {
            string root = EnsureTempRoot();
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(root, "Core"));
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var registry = new VirtualCameraRegistry();

            new VirtualCameraDefinitionLoader(pipeline, registry).Load(catalog, new ConfigConflictReport());
        }

        private void WriteConfig(string modId, string relativePath, string content)
        {
            string root = EnsureTempRoot();
            string relativeDir = Path.GetDirectoryName(relativePath) ?? string.Empty;
            string dir = Path.Combine(root, modId, "Configs", relativeDir);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Path.GetFileName(relativePath)), content);
        }

        private string EnsureTempRoot()
        {
            if (!string.IsNullOrWhiteSpace(_tempRoot))
            {
                return _tempRoot;
            }

            _tempRoot = Path.Combine(Path.GetTempPath(), "Ludots_CapabilityVirtualCamera", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            return _tempRoot;
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
            public float GetAxis(string devicePath) => 0f;
            private Vector2 _mousePosition;

            public void SetMousePosition(Vector2 position) => _mousePosition = position;
            public Vector2 GetMousePosition() => _mousePosition;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;

            private readonly System.Collections.Generic.HashSet<string> _pressedButtons = new(StringComparer.Ordinal);

            public void SetButton(string devicePath, bool isDown)
            {
                if (isDown)
                {
                    _pressedButtons.Add(devicePath);
                }
                else
                {
                    _pressedButtons.Remove(devicePath);
                }
            }

            public bool GetButton(string devicePath) => _pressedButtons.Contains(devicePath);
        }

        private sealed class StubViewController : IViewController
        {
            public Vector2 Resolution => new(1280f, 720f);
            public float Fov => 60f;
            public float AspectRatio => Resolution.X / Resolution.Y;
        }
    }
}
