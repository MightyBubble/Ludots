using System;
using System.IO;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Tests.TestCommon;
using Ludots.Core.Client;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Bindings;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Input.Attributes;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Systems;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Core.Engine;
using Ludots.Platform.Abstractions;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class AuthoritativeInputConvergenceTests
    {
        private const float InputConvergenceInitialZoomNormalized = 1f;
        private const float InputConvergenceWheelZoomNormalizedStep = 0.08f;
        private const int InputConvergenceDebugMarkerSampleCapacity = 64;
        private const float InputConvergenceMinZoomHalfExtentCm = 750f;
        private const float InputConvergenceMaxZoomHalfExtentCm = 22000f;
        private static readonly MinimapActionsConfig InputConvergenceMinimapActions = Ludots.Tests.Presentation.MinimapTestActions.Create();

        [Test]
        public void AuthoritativeInputAccumulator_PreservesEdgesUntilConsumed()
        {
            var (backend, handler) = BuildHandler();
            var accumulator = new AuthoritativeInputAccumulator();
            var snapshot = new FrozenInputActionReader();

            handler.Update(1f / 60f);
            accumulator.CaptureVisualFrame(handler);
            accumulator.BuildTickSnapshot(snapshot);
            Assert.That(snapshot.PressedThisFrame("Attack"), Is.False);
            Assert.That(snapshot.IsDown("Attack"), Is.False);

            backend.Buttons["<Keyboard>/a"] = true;
            handler.Update(1f / 60f);
            accumulator.CaptureVisualFrame(handler);

            handler.Update(1f / 60f);
            accumulator.CaptureVisualFrame(handler);

            accumulator.BuildTickSnapshot(snapshot);
            Assert.That(snapshot.PressedThisFrame("Attack"), Is.True);
            Assert.That(snapshot.IsDown("Attack"), Is.True);

            accumulator.BuildTickSnapshot(snapshot);
            Assert.That(snapshot.PressedThisFrame("Attack"), Is.False);
            Assert.That(snapshot.IsDown("Attack"), Is.True);

            backend.Buttons["<Keyboard>/a"] = false;
            handler.Update(1f / 60f);
            accumulator.CaptureVisualFrame(handler);

            accumulator.BuildTickSnapshot(snapshot);
            Assert.That(snapshot.ReleasedThisFrame("Attack"), Is.True);
            Assert.That(snapshot.IsDown("Attack"), Is.False);
        }

        [Test]
        public void InputRuntimeSystem_PreservesConfiguredActionValueUntilAuthoritativeSnapshot()
        {
            var backend = new TestInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Zoom", Type = InputActionType.Axis1D },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 1,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "Zoom", Path = "<Mouse>/ScrollY", Processors = new() },
                        }
                    }
                }
            };
            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");

            var accumulator = new AuthoritativeInputAccumulator();
            var snapshot = new FrozenInputActionReader();
            int zoomAttribute = AttributeRegistry.Register(CameraBehaviorAttributes.Zoom);
            var registry = new InputActionAttributeBindingRegistry();
            registry.Set(new[]
            {
                new InputActionAttributeBindingEntry(
                    "Zoom",
                    zoomAttribute,
                    InputActionAttributeValueKind.Axis1D,
                    0,
                    InputActionAttributeTargetKind.SolePossessedRep,
                    1f,
                    zeroWhenUiCaptured: true,
                    suppressOnUiWheelCaptured: true,
                    preserveValueUntilSnapshot: true),
            });

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = handler,
                [CoreServiceKeys.InputActionAttributeBindingRegistry.Name] = registry,
                [CoreServiceKeys.UiCaptured.Name] = false,
            };
            var system = new InputRuntimeSystem(globals, accumulator);

            handler.InjectAction("Zoom", new Vector3(1f, 0f, 0f));
            system.Update(1f / 60f);
            Assert.That(handler.ReadAction<float>("Zoom"), Is.EqualTo(1f).Within(0.001f));
            Assert.That(handler.ReadAction<Vector3>("Zoom").X, Is.EqualTo(1f).Within(0.001f));
            system.Update(1f / 60f);
            accumulator.BuildTickSnapshot(snapshot);

            Assert.That(snapshot.ReadAction<float>("Zoom"), Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void InputActionAttributeBindingSystem_WritesCameraBehaviorInputTargetComponentWithoutRequiringLocalPlayerAttributes()
        {
            using var world = World.Create();
            Entity localPlayerIdentity = world.Create();
            Entity cameraBehaviorInputTarget = world.Create(new AttributeBuffer(), new DirtyFlags(), new CameraBehaviorInputTarget());
            int lookXAttribute = AttributeRegistry.Register(CameraBehaviorAttributes.LookX);

            var authoritativeInput = new FrozenInputActionReader();
            authoritativeInput.SetActionState(
                "Look",
                new Vector3(3f, 0f, 0f),
                isDown: true,
                pressedThisFrame: true,
                releasedThisFrame: false,
                present: true);

            var registry = new InputActionAttributeBindingRegistry();
            registry.Set(new[]
            {
                new InputActionAttributeBindingEntry(
                    "Look",
                    lookXAttribute,
                    InputActionAttributeValueKind.Axis2D,
                    0,
                    InputActionAttributeTargetKind.CameraBehaviorInput,
                    1f,
                    zeroWhenUiCaptured: true,
                    suppressOnUiWheelCaptured: false,
                    preserveValueUntilSnapshot: true),
            });

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.AuthoritativeInput.Name] = authoritativeInput,
                [CoreServiceKeys.UiCaptured.Name] = false,
            };
            ClientLocalSeatTestBindings.BindSoleSeat(globals, localPlayerIdentity, 1, "seat.0");
            var system = new InputActionAttributeBindingSystem(
                world,
                globals,
                registry,
                new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

            system.Update(0f);

            Assert.That(world.Has<AttributeBuffer>(localPlayerIdentity), Is.False);
            ref AttributeBuffer cameraAttributes = ref world.Get<AttributeBuffer>(cameraBehaviorInputTarget);
            Assert.That(cameraAttributes.GetCurrent(lookXAttribute), Is.EqualTo(3f).Within(0.001f));
        }


        [Test]
        public void InputRuntimeSystem_UiCaptured_SuppressesCameraUserInputOnlyForCapturedFrames()
        {
            var (backend, handler) = BuildCameraHandler();
            var session = new GameSession();
            using var world = World.Create();
            var behaviorInput = new CameraBehaviorInputState();
            Entity localPlayer = world.Create(new AttributeBuffer(), new DirtyFlags(), new CameraBehaviorInputTarget());
            var registry = new VirtualCameraRegistry();
            registry.Register(new VirtualCameraDefinition
            {
                Id = "EdgePan",
                Priority = 0,
                FacingMode = CameraFacingMode.None,
                PanMode = CameraPanMode.EdgePan,
                EdgePanMarginPx = 10f,
                EdgePanSpeedCmPerSec = 8000f,
                RotateMode = CameraRotateMode.None,
                DistanceCm = 5000f,
                Pitch = 60f,
                FovYDeg = 60f,
                Yaw = 180f,
                EnableZoom = false,
                AllowUserInput = true
            });
            var camera = new CameraManager();
            camera.SetVirtualCameraRegistry(registry);
            camera.ConfigureRuntime(behaviorInput, new StubViewController());
            camera.ActivateVirtualCamera("EdgePan", blendDurationSeconds: 0f);

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = handler,
                [CoreServiceKeys.AuthoritativeInput.Name] = handler,
                [CoreServiceKeys.GameSession.Name] = session,
                [CoreServiceKeys.UiCaptured.Name] = true,
            };
            ClientLocalSeatBindings.BindSoleSeat(
                globals,
                localPlayer,
                playerId: 1,
                seatId: "seat.0",
                primaryCamera: camera,
                presentResolutionPx: ClientLocalSeatTestBindings.DefaultPresentResolutionPx);
            var actionBindings = new InputActionAttributeBindingRegistry();
            actionBindings.Set(new[]
            {
                new InputActionAttributeBindingEntry(
                    "PointerPos",
                    AttributeRegistry.Register(CameraBehaviorAttributes.PointerX),
                    InputActionAttributeValueKind.Axis2D,
                    0,
                    InputActionAttributeTargetKind.SolePossessedRep,
                    1f,
                    zeroWhenUiCaptured: true,
                    suppressOnUiWheelCaptured: false,
                    preserveValueUntilSnapshot: false),
                new InputActionAttributeBindingEntry(
                    "PointerPos",
                    AttributeRegistry.Register(CameraBehaviorAttributes.PointerY),
                    InputActionAttributeValueKind.Axis2D,
                    1,
                    InputActionAttributeTargetKind.SolePossessedRep,
                    1f,
                    zeroWhenUiCaptured: true,
                    suppressOnUiWheelCaptured: false,
                    preserveValueUntilSnapshot: false),
                new InputActionAttributeBindingEntry(
                    "PointerPos",
                    AttributeRegistry.Register(CameraBehaviorAttributes.PointerActive),
                    InputActionAttributeValueKind.Constant,
                    0,
                    InputActionAttributeTargetKind.SolePossessedRep,
                    1f,
                    zeroWhenUiCaptured: true,
                    suppressOnUiWheelCaptured: false,
                    preserveValueUntilSnapshot: false),
            });
            globals[CoreServiceKeys.InputActionAttributeBindingRegistry.Name] = actionBindings;

            var sinks = new AttributeSinkRegistry();
            GasAttributeSinks.RegisterBuiltins(sinks, behaviorInput);
            int cameraSinkId = sinks.GetId(GasSinkNames.CameraBehaviorInput);
            var attributeBindings = new AttributeBindingRegistry();
            attributeBindings.Set(
                new[]
                {
                    new AttributeBindingEntry(
                        AttributeRegistry.Register(CameraBehaviorAttributes.PointerX),
                        cameraSinkId,
                        CameraBehaviorInputChannels.PointerX,
                        AttributeBindingMode.Override,
                        AttributeBindingResetPolicy.ResetToZeroPerLogicFrame,
                        1f),
                    new AttributeBindingEntry(
                        AttributeRegistry.Register(CameraBehaviorAttributes.PointerY),
                        cameraSinkId,
                        CameraBehaviorInputChannels.PointerY,
                        AttributeBindingMode.Override,
                        AttributeBindingResetPolicy.ResetToZeroPerLogicFrame,
                        1f),
                    new AttributeBindingEntry(
                        AttributeRegistry.Register(CameraBehaviorAttributes.PointerActive),
                        cameraSinkId,
                        CameraBehaviorInputChannels.PointerActive,
                        AttributeBindingMode.Override,
                        AttributeBindingResetPolicy.ResetToZeroPerLogicFrame,
                        1f),
                },
                new[] { new AttributeBindingGroup(cameraSinkId, 0, 3) });

            var system = new InputRuntimeSystem(globals);
            var actionBindingSystem = new InputActionAttributeBindingSystem(
                world,
                globals,
                actionBindings,
                new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));
            var attributeBindingSystem = new AttributeBindingSystem(world, sinks, attributeBindings);

            backend.MousePosition = Vector2.Zero;
            system.Update(1f);
            actionBindingSystem.Update(1f);
            attributeBindingSystem.Update(1f);
            camera.Update(1f);

            Assert.That(camera.State.TargetCm.X, Is.EqualTo(0f).Within(0.01f));
            Assert.That(camera.State.TargetCm.Y, Is.EqualTo(0f).Within(0.01f));

            globals[CoreServiceKeys.UiCaptured.Name] = false;
            system.Update(1f);
            actionBindingSystem.Update(1f);
            attributeBindingSystem.Update(1f);
            camera.Update(1f);

            Assert.That(camera.State.TargetCm.Length(), Is.GreaterThan(0.01f));
        }

        [Test]
        public void InputRuntimeSystem_UiWheelCaptured_SuppressesCameraZoomWithoutBlockingOtherInput()
        {
            var backend = new TestInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Confirm", Type = InputActionType.Button },
                    new() { Id = "Zoom", Type = InputActionType.Axis1D },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 1,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "Confirm", Path = "<Mouse>/LeftButton", Processors = new() },
                            new() { ActionId = "Zoom", Path = "<Mouse>/ScrollY", Processors = new() },
                        }
                    }
                }
            };
            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");
            var actionBindings = new InputActionAttributeBindingRegistry();
            actionBindings.Set(new[]
            {
                new InputActionAttributeBindingEntry(
                    "Zoom",
                    AttributeRegistry.Register(CameraBehaviorAttributes.Zoom),
                    InputActionAttributeValueKind.Axis1D,
                    0,
                    InputActionAttributeTargetKind.SolePossessedRep,
                    1f,
                    zeroWhenUiCaptured: true,
                    suppressOnUiWheelCaptured: true,
                    preserveValueUntilSnapshot: true),
            });
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = handler,
                [CoreServiceKeys.InputActionAttributeBindingRegistry.Name] = actionBindings,
                [CoreServiceKeys.UiCaptured.Name] = false,
                [CoreServiceKeys.UiWheelCaptured.Name] = true,
            };
            var system = new InputRuntimeSystem(globals);

            backend.MouseWheel = 1f;
            backend.Buttons["<Mouse>/LeftButton"] = true;
            system.Update(1f / 60f);

            Assert.That(handler.ReadAction<float>("Zoom"), Is.EqualTo(0f), "UI wheel capture must suppress shared camera zoom.");
            Assert.That(handler.PressedThisFrame("Confirm"), Is.True, "UI wheel capture must not block unrelated gameplay input.");
            Assert.That(globals[CoreServiceKeys.UiWheelCaptured.Name], Is.False);
        }

        [Test]
        public void InputRuntimeSystem_CapturesGroundPointerIntoAuthoritativeSnapshot()
        {
            var (backend, handler) = BuildCameraHandler();
            var accumulator = new AuthoritativeInputAccumulator();
            var snapshot = new FrozenInputActionReader();
            backend.MousePosition = new Vector2(12.5f, 34.75f);

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = handler,
                [CoreServiceKeys.ScreenRayProvider.Name] = new VerticalScreenRayProvider(),
                [CoreServiceKeys.ContinuousHeightmap.Name] = CreateFlatHeightmap(),
                [CoreServiceKeys.WorldSizeSpec.Name] = new WorldSizeSpec(new WorldAabbCm(-100000, -100000, 200000, 200000), 100),
            };

            var system = new InputRuntimeSystem(globals, accumulator);
            system.Update(1f / 60f);
            accumulator.BuildTickSnapshot(snapshot);

            Assert.That(AuthoritativeGroundPointerHelper.TryRead(snapshot, out var worldCm), Is.True);
            Assert.That(worldCm.X, Is.EqualTo(1250));
            Assert.That(worldCm.Y, Is.EqualTo(3475));
        }

        [Test]
        public void InputRuntimeSystem_GroundPointerOverrideWinsUntilNextFixedSnapshot()
        {
            var (backend, handler) = BuildConfirmCommandHandler();
            var accumulator = new AuthoritativeInputAccumulator();
            var snapshot = new FrozenInputActionReader();
            var groundOverride = new AuthoritativeGroundPointerOverride();
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = handler,
                [CoreServiceKeys.ScreenRayProvider.Name] = new VerticalScreenRayProvider(),
                [CoreServiceKeys.ContinuousHeightmap.Name] = CreateFlatHeightmap(),
                [CoreServiceKeys.WorldSizeSpec.Name] = new WorldSizeSpec(new WorldAabbCm(-100000, -100000, 200000, 200000), 100),
                [CoreServiceKeys.AuthoritativeGroundPointerOverride.Name] = groundOverride,
            };

            var system = new InputRuntimeSystem(globals, accumulator);

            groundOverride.Set("Command", new Vector2(1320f, 720f));
            backend.MousePosition = new Vector2(12f, 34f);
            system.Update(1f / 120f);

            backend.MousePosition = new Vector2(99f, 88f);
            system.Update(1f / 120f);

            accumulator.BuildTickSnapshot(snapshot);

            Assert.That(AuthoritativeGroundPointerHelper.TryRead(snapshot, out var worldCm), Is.True);
            Assert.That(worldCm.X, Is.EqualTo(1320));
            Assert.That(worldCm.Y, Is.EqualTo(720));

            backend.MousePosition = new Vector2(99f, 88f);
            system.Update(1f / 120f);
            accumulator.BuildTickSnapshot(snapshot);

            Assert.That(AuthoritativeGroundPointerHelper.TryRead(snapshot, out worldCm), Is.True);
            Assert.That(worldCm.X, Is.EqualTo(9900));
            Assert.That(worldCm.Y, Is.EqualTo(8800));
        }

        [Test]
        public void InputRuntimeSystem_CapturesPointerButtonPressAndReleaseScreenPositionsWithinOneLogicTick()
        {
            var (backend, handler) = BuildConfirmCommandHandler();
            var pointerButtons = new AuthoritativePointerButtonAccumulator();
            var snapshot = new AuthoritativePointerButtonSnapshot();
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = handler,
            };

            var system = new InputRuntimeSystem(
                globals,
                pointerButtons: pointerButtons,
                pointerLifecycleActionIds: new[] { "Confirm" });

            backend.MousePosition = new Vector2(120f, 240f);
            system.Update(1f / 60f);

            backend.Buttons["<Mouse>/LeftButton"] = true;
            system.Update(1f / 60f);

            backend.MousePosition = new Vector2(420f, 540f);
            system.Update(1f / 60f);

            backend.Buttons["<Mouse>/LeftButton"] = false;
            system.Update(1f / 60f);

            pointerButtons.BuildTickSnapshot(snapshot);

            Assert.That(snapshot.TryGetState("Confirm", out var state), Is.True);
            Assert.That(state.PressedThisFrame, Is.True);
            Assert.That(state.ReleasedThisFrame, Is.True);
            Assert.That(state.IsDown, Is.False);
            Assert.That(state.HasPressPointer, Is.True);
            Assert.That(state.PressPointer, Is.EqualTo(new Vector2(120f, 240f)));
            Assert.That(state.HasReleasePointer, Is.True);
            Assert.That(state.ReleasePointer, Is.EqualTo(new Vector2(420f, 540f)));
            Assert.That(state.Pointer, Is.EqualTo(new Vector2(420f, 540f)));
        }

        [Test]
        public void InputRuntimeSystem_MinimapClickJumpsCameraAndDoesNotStartWorldSelect()
        {
            using var engine = CreateMinimapContextEngine(out TestInputBackend backend, out MinimapRuntime minimap);
            Entity hero = FindNamed(engine.World, "Hero");
            Assert.That(hero, Is.Not.EqualTo(Entity.Null));

            Vector2 click = new(minimap.FieldX + (minimap.FieldSize * 0.75f), minimap.FieldY + (minimap.FieldSize * 0.25f));
            Assert.That(minimap.TryScreenToWorld(click, out Vector2 expectedTarget), Is.True);
            backend.MousePosition = click;
            StepLogic(engine);

            backend.Buttons["<Mouse>/LeftButton"] = true;
            StepLogic(engine);
            Assert.That(engine.AuthorityCamera().State.TargetCm.X, Is.EqualTo(expectedTarget.X).Within(1f));
            Assert.That(engine.AuthorityCamera().State.TargetCm.Y, Is.EqualTo(expectedTarget.Y).Within(1f));
            Assert.That(ContextActive(engine, hero, "interaction.context.core.selecting"), Is.False,
                "A click on the minimap field must not open the world select context.");

            backend.Buttons["<Mouse>/LeftButton"] = false;
            StepLogic(engine);
            backend.MousePosition = new Vector2(minimap.FieldX - 12f, minimap.FieldY - 12f);
            StepLogic(engine);
            Assert.That(ContextActive(engine, hero, "interaction.context.minimap"), Is.False);

            backend.Buttons["<Mouse>/LeftButton"] = true;
            StepLogic(engine);
            Assert.That(ContextActive(engine, hero, "interaction.context.core.selecting"), Is.True,
                "A click outside the minimap still opens world select.");
        }

        [Test]
        public void InputRuntimeSystem_MinimapEnterAndPressSameTick_JumpsAndDoesNotSelect()
        {
            using var engine = CreateMinimapContextEngine(out TestInputBackend backend, out MinimapRuntime minimap);
            Entity hero = FindNamed(engine.World, "Hero");
            CommandIntentSubmissionBuffer submissions = engine.GetService(CoreServiceKeys.CommandIntentSubmissions)
                ?? throw new InvalidOperationException("CommandIntentSubmissions missing.");

            backend.MousePosition = new Vector2(minimap.FieldX - 12f, minimap.FieldY - 12f);
            StepLogic(engine);

            Vector2 click = new(minimap.FieldX + (minimap.FieldSize * 0.75f), minimap.FieldY + (minimap.FieldSize * 0.25f));
            Assert.That(minimap.TryScreenToWorld(click, out Vector2 expectedTarget), Is.True);
            backend.MousePosition = click;
            backend.Buttons["<Mouse>/LeftButton"] = true;
            StepLogic(engine);

            Assert.That(ContextActive(engine, hero, "interaction.context.minimap"), Is.True);
            Assert.That(ContextActive(engine, hero, "interaction.context.core.selecting"), Is.False,
                "A press that arrives on the same tick the pointer enters the minimap must not open world select.");
            Assert.That(submissions.Count, Is.EqualTo(0));
            Assert.That(engine.AuthorityCamera().State.TargetCm.X, Is.EqualTo(expectedTarget.X).Within(1f));
            Assert.That(engine.AuthorityCamera().State.TargetCm.Y, Is.EqualTo(expectedTarget.Y).Within(1f));
        }

        [Test]
        public void InputRuntimeSystem_UiCaptured_BlocksMinimap()
        {
            using var engine = CreateMinimapContextEngine(out TestInputBackend backend, out MinimapRuntime minimap);
            Entity hero = FindNamed(engine.World, "Hero");
            Vector2 click = new(minimap.FieldX + (minimap.FieldSize * 0.75f), minimap.FieldY + (minimap.FieldSize * 0.25f));
            backend.MousePosition = click;
            StepLogic(engine);
            Assert.That(ContextActive(engine, hero, "interaction.context.minimap"), Is.True);
            Vector2 cameraBefore = engine.AuthorityCamera().State.TargetCm;

            engine.SetService(CoreServiceKeys.UiCaptured, true);
            backend.Buttons["<Mouse>/LeftButton"] = true;
            StepLogic(engine);

            Assert.That(ContextActive(engine, hero, "interaction.context.minimap"), Is.False);
            Assert.That(ContextActive(engine, hero, "interaction.context.core.selecting"), Is.False);
            Assert.That(engine.AuthorityCamera().State.TargetCm, Is.EqualTo(cameraBefore));
        }

        [Test]
        public void InputRuntimeSystem_MinimapFieldKeepsRaycastGroundPointer()
        {
            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(FindRepoRoot(), new[] { "LudotsCoreMod", "CommandSourceCoreFixtureMod" }),
                Path.Combine(FindRepoRoot(), "assets"),
                Ludots.Tests.EngineUserData.CreateIsolatedRoot());

            var backend = new TestInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Confirm", Type = InputActionType.Button },
                    new() { Id = "Command", Type = InputActionType.Button },
                    new() { Id = "Cancel", Type = InputActionType.Button },
                    new() { Id = "PointerPos", Type = InputActionType.Axis2D },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 1,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "Confirm", Path = "<Mouse>/LeftButton", Processors = new() },
                            new() { ActionId = "Command", Path = "<Mouse>/RightButton", Processors = new() },
                            new() { ActionId = "Cancel", Path = "<Keyboard>/escape", Processors = new() },
                            new() { ActionId = "PointerPos", Path = "<Mouse>/Pos", Processors = new() },
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");

            var minimap = CreateInputConvergenceMinimap();
            var markerBuffer = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapMarkerBuffer missing.");
            var screenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapScreenMarkerBuffer missing.");
            engine.SetService(CoreServiceKeys.MinimapRuntime, minimap);
            minimap.Visible = true;
            minimap.UseRtsFullMapPreset();
            minimap.Refresh(engine, markerBuffer, screenMarkers);

            var rayProvider = new CountingScreenRayProvider();
            engine.SetService(CoreServiceKeys.InputHandler, handler);
            engine.SetService(CoreServiceKeys.InputBackend, backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, rayProvider);
            engine.SetService(CoreServiceKeys.ContinuousHeightmap, CreateFlatHeightmap());
            engine.SetService(CoreServiceKeys.WorldSizeSpec, new WorldSizeSpec(RaycastWorldBounds, 100));

            var input = new AuthoritativeInputAccumulator();
            var snapshot = new FrozenInputActionReader();
            var system = new InputRuntimeSystem(engine.GlobalContext, input);

            Vector2 click = new(minimap.FieldX + (minimap.FieldSize * 0.80f), minimap.FieldY + (minimap.FieldSize * 0.20f));
            Assert.That(minimap.TryScreenToWorld(click, out Vector2 expectedWorldCm), Is.True);

            backend.MousePosition = click;
            backend.Buttons["<Mouse>/RightButton"] = true;
            system.Update(1f / 60f);
            input.BuildTickSnapshot(snapshot);

            Assert.That(snapshot.PressedThisFrame("Command"), Is.True, "Minimap command click must still enter the formal command action chain.");
            Assert.That(AuthoritativeGroundPointerHelper.TryRead(snapshot, out var worldCm), Is.True);
            Assert.That(worldCm.X, Is.EqualTo(RaycastGroundCm(click.X)));
            Assert.That(worldCm.Y, Is.EqualTo(RaycastGroundCm(click.Y)));
            Assert.That(rayProvider.CallCount, Is.GreaterThan(0));
            Assert.That(engine.GetService(CoreServiceKeys.AuthoritativeGroundPointerOverride)?.HasOverride, Is.False);
        }

        [Test]
        public void MinimapCommandGraph_SubmitsProjectedGround_WhilePointerGroundStaysRaycast()
        {
            using var engine = CreateMinimapContextEngine(out TestInputBackend backend, out MinimapRuntime minimap);
            var rayProvider = new CountingScreenRayProvider();
            engine.SetService(CoreServiceKeys.ScreenRayProvider, rayProvider);
            engine.SetService(CoreServiceKeys.ContinuousHeightmap, CreateFlatHeightmap());
            engine.SetService(CoreServiceKeys.WorldSizeSpec, new WorldSizeSpec(RaycastWorldBounds, 100));

            backend.MousePosition = new Vector2(minimap.FieldX - 12f, minimap.FieldY - 12f);
            StepLogic(engine);

            Vector2 click = new(minimap.FieldX + (minimap.FieldSize * 0.80f), minimap.FieldY + (minimap.FieldSize * 0.20f));
            Assert.That(minimap.TryScreenToWorld(click, out Vector2 expectedWorldCm), Is.True);
            backend.MousePosition = click;
            backend.Buttons["<Mouse>/RightButton"] = true;
            StepLogic(engine);

            CommandIntentSubmissionBuffer submissions = engine.GetService(CoreServiceKeys.CommandIntentSubmissions)
                ?? throw new InvalidOperationException("CommandIntentSubmissions missing.");
            var expectedGround = new IntVector2(
                (int)MathF.Round(expectedWorldCm.X, MidpointRounding.AwayFromZero),
                (int)MathF.Round(expectedWorldCm.Y, MidpointRounding.AwayFromZero));
            bool submitted = false;
            for (int i = 0; i < submissions.Count; i++)
            {
                IntVector2 ground = submissions[i].GroundCm;
                if (ground.X == expectedGround.X && ground.Y == expectedGround.Y)
                {
                    submitted = true;
                }
            }

            Assert.That(submitted, Is.True, $"Command graph must submit the minimap projection {expectedGround}.");

            IInputActionReader snapshot = engine.GetService(CoreServiceKeys.AuthoritativeInput)
                ?? throw new InvalidOperationException("AuthoritativeInput missing.");
            Assert.That(AuthoritativeGroundPointerHelper.TryRead(snapshot, out WorldCmInt2 pointerGround), Is.True);
            Assert.That(pointerGround.X, Is.EqualTo(RaycastGroundCm(click.X)));
            Assert.That(pointerGround.Y, Is.EqualTo(RaycastGroundCm(click.Y)));
            Assert.That(rayProvider.CallCount, Is.GreaterThan(0));
        }

        [Test]
        public void InputRuntimeSystem_MinimapContextScopesWheelDragAndRotationToggle()
        {
            using var engine = CreateMinimapContextEngine(out TestInputBackend backend, out MinimapRuntime minimap);
            Entity hero = FindNamed(engine.World, "Hero");

            Vector2 outside = new(minimap.FieldX - 12f, minimap.FieldY - 12f);
            float halfBeforeOutsideWheel = minimap.HalfExtentCm;
            float zoomBeforeOutsideWheel = minimap.ZoomNormalized;
            backend.MousePosition = outside;
            backend.MouseWheel = 1f;
            StepLogic(engine);
            Assert.That(minimap.HalfExtentCm, Is.EqualTo(halfBeforeOutsideWheel).Within(0.01f));
            Assert.That(minimap.ZoomNormalized, Is.EqualTo(zoomBeforeOutsideWheel).Within(0.0001f));
            PlayerInputHandler handler = engine.GetService(CoreServiceKeys.InputHandler)
                ?? throw new InvalidOperationException("InputHandler missing.");
            Assert.That(handler.ReadAction<float>("Zoom"), Is.EqualTo(0.1f),
                "Wheel outside the minimap stays on camera Zoom. Camera.Rig scales ScrollY by 0.1.");

            backend.MouseWheel = 0f;
            Vector2 slider = new(minimap.ZoomSliderX + (minimap.ZoomSliderWidth * 0.2f), minimap.ZoomSliderY + (minimap.ZoomSliderHeightPx * 0.5f));
            backend.MousePosition = slider;
            StepLogic(engine);
            Assert.That(ContextActive(engine, hero, "interaction.context.minimap"), Is.True);
            backend.Buttons["<Mouse>/LeftButton"] = true;
            StepLogic(engine);
            Assert.That(minimap.ZoomNormalized, Is.EqualTo(0.2f).Within(0.03f));
            float halfAfterSlider = minimap.HalfExtentCm;
            float zoomAfterSlider = minimap.ZoomNormalized;
            backend.Buttons["<Mouse>/LeftButton"] = false;
            StepLogic(engine);

            Vector2 insideFieldForWheel = new(minimap.FieldX + (minimap.FieldSize * 0.5f), minimap.FieldY + (minimap.FieldSize * 0.5f));
            backend.MousePosition = insideFieldForWheel;
            backend.MouseWheel = 1f;
            StepLogic(engine);
            Assert.That(minimap.ZoomNormalized, Is.LessThan(zoomAfterSlider));
            Assert.That(minimap.HalfExtentCm, Is.LessThan(halfAfterSlider));
            Assert.That(handler.ReadAction<float>("Zoom"), Is.EqualTo(0f));

            backend.MouseWheel = 0f;
            Vector2 firstDrag = new(minimap.FieldX + (minimap.FieldSize * 0.25f), minimap.FieldY + (minimap.FieldSize * 0.25f));
            Vector2 secondDrag = new(minimap.FieldX + (minimap.FieldSize * 0.85f), minimap.FieldY + (minimap.FieldSize * 0.72f));
            backend.MousePosition = firstDrag;
            StepLogic(engine);
            backend.Buttons["<Mouse>/LeftButton"] = true;
            Assert.That(minimap.TryScreenToWorld(firstDrag, out Vector2 expectedFirstTarget), Is.True);
            StepLogic(engine);
            Assert.That(engine.AuthorityCamera().State.TargetCm.X, Is.EqualTo(expectedFirstTarget.X).Within(1f));
            Assert.That(engine.AuthorityCamera().State.TargetCm.Y, Is.EqualTo(expectedFirstTarget.Y).Within(1f));
            Assert.That(ContextActive(engine, hero, "interaction.context.core.selecting"), Is.False);

            backend.MousePosition = secondDrag;
            Assert.That(minimap.TryScreenToWorld(secondDrag, out Vector2 expectedSecondTarget), Is.True);
            StepLogic(engine);
            Assert.That(engine.AuthorityCamera().State.TargetCm.X, Is.EqualTo(expectedSecondTarget.X).Within(1f));
            Assert.That(engine.AuthorityCamera().State.TargetCm.Y, Is.EqualTo(expectedSecondTarget.Y).Within(1f));

            backend.Buttons["<Mouse>/LeftButton"] = false;
            StepLogic(engine);

            Vector2 presetToggle = new(
                minimap.PresetToggleX + (minimap.PresetToggleWidth * 0.5f),
                minimap.PresetToggleY + (minimap.PresetToggleHeight * 0.5f));
            backend.MousePosition = presetToggle;
            StepLogic(engine);
            backend.Buttons["<Mouse>/LeftButton"] = true;
            StepLogic(engine);
            Assert.That(minimap.Preset, Is.EqualTo(MinimapPreset.FollowCamera));
            Assert.That(ContextActive(engine, hero, "interaction.context.core.selecting"), Is.False);

            backend.Buttons["<Mouse>/LeftButton"] = false;
            StepLogic(engine);

            Vector2 rotateToggle = new(
                minimap.RotateToggleX + (minimap.RotateToggleWidth * 0.5f),
                minimap.RotateToggleY + (minimap.RotateToggleHeight * 0.5f));
            bool beforeRotation = minimap.RotateWithCamera;
            backend.MousePosition = rotateToggle;
            StepLogic(engine);
            backend.Buttons["<Mouse>/LeftButton"] = true;
            StepLogic(engine);
            Assert.That(minimap.RotateWithCamera, Is.EqualTo(!beforeRotation));
            Assert.That(ContextActive(engine, hero, "interaction.context.core.selecting"), Is.False);
        }

        [Test]
        public void MinimapCommandOwners_MergeCommandBindingOntoMinimapContext()
        {
            AssertOwnerMergesCommand("MinimapShowcaseMod", "FourXDemoMod");
            AssertOwnerMergesCommand("FogVisionDecayShowcaseMod");
            AssertOwnerMergesCommand("PresenterBlacksmithShowcaseMod");
        }

        private static void AssertOwnerMergesCommand(string owner, params string[] extraMods)
        {
            var mods = new List<string> { "LudotsCoreMod" };
            mods.AddRange(extraMods);
            mods.Add(owner);
            using GameEngine engine = CreateProfileEngine(mods.ToArray());
            InteractionContextProfileDefinition minimap = RequireProfile(engine, "interaction.context.minimap");
            Assert.That(minimap.Bindings, Does.Contain("Command"), owner);
            Assert.That(minimap.Bindings, Does.Contain("Minimap.Zoom"), owner);
            Assert.That(TriggerIds(minimap), Does.Contain("graph.minimap.command"), owner);
            Assert.That(TriggerIds(minimap), Does.Contain("graph.minimap.zoom"), owner);
        }

        [Test]
        public void LudotsCoreMod_MinimapContext_BindsZoomAndOmitsCommand()
        {
            using GameEngine engine = CreateProfileEngine("LudotsCoreMod", "CommandSourceCoreFixtureMod");
            InteractionContextProfileDefinition minimap = RequireProfile(engine, "interaction.context.minimap");
            Assert.That(minimap.Bindings, Does.Contain("Select.Begin"));
            Assert.That(minimap.Bindings, Does.Contain("Minimap.Zoom"));
            Assert.That(minimap.Bindings, Does.Not.Contain("Command"));
            Assert.That(TriggerIds(minimap), Does.Contain("graph.minimap.press"));
            Assert.That(TriggerIds(minimap), Does.Contain("graph.minimap.zoom"));
            Assert.That(TriggerIds(minimap), Does.Not.Contain("graph.minimap.command"));
        }

        [Test]
        public void RtsShowcase_MinimapClick_ActivatesMinimapAndDoesNotSelect()
        {
            using GameEngine engine = CreateProfileEngine(
                "LudotsCoreMod",
                "EntityCommandPanelMod",
                "RtsDemoMod");
            engine.SimulationBudgetMsPerFrame = int.MaxValue;
            engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)new SkiaTextMeasurer());
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)new SkiaImageSizeProvider());
            engine.Start();
            engine.LoadStartupMap();

            Assert.That(TriggerIds(RequireProfile(engine, "interaction.context.rts.battle")), Does.Not.Contain("graph.minimap.hover"));

            // rts_entry has no board, so the shared OneChunk extent cannot refresh the engine runtime.
            // The visible runtime still declares interaction.context.minimap; no RTS graph mounts hover.
            var markerBuffer = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapMarkerBuffer missing.");
            var screenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapScreenMarkerBuffer missing.");
            MinimapRuntime minimap = CreateInputConvergenceMinimap();
            engine.SetService(CoreServiceKeys.MinimapRuntime, minimap);
            minimap.Visible = true;
            minimap.UseRtsFullMapPreset();
            minimap.Refresh(engine, markerBuffer, screenMarkers);

            TestInputBackend backend = WireGameplayInput(engine);
            backend.MousePosition = new Vector2(minimap.FieldX - 12f, minimap.FieldY - 12f);
            StepLogic(engine);

            ClientLocalSeatRegistry seats = engine.GetService(CoreServiceKeys.ClientLocalSeatRegistry)
                ?? throw new InvalidOperationException("ClientLocalSeatRegistry missing.");
            ClientLocalSeat seat = seats.Require(seats.SeatIds[0]);
            Assert.That(seat.HasPossession, Is.True);
            Entity rep = seat.PossessedRep;

            Vector2 click = new(minimap.FieldX + (minimap.FieldSize * 0.75f), minimap.FieldY + (minimap.FieldSize * 0.25f));
            backend.MousePosition = click;
            backend.Buttons["<Mouse>/LeftButton"] = true;
            StepLogic(engine);

            Assert.That(ContextActive(engine, rep, "interaction.context.minimap"), Is.True);
            Assert.That(ContextActive(engine, rep, "interaction.context.rts.selecting"), Is.False);
        }

        private static (TestInputBackend backend, PlayerInputHandler handler) BuildHandler()
        {
            var backend = new TestInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Attack", Type = InputActionType.Button },
                    new() { Id = "Confirm", Type = InputActionType.Button },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 1,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "Attack", Path = "<Keyboard>/a", Processors = new() },
                            new() { ActionId = "Confirm", Path = "<Keyboard>/enter", Processors = new() },
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");
            return (backend, handler);
        }

        private static MinimapRuntime CreateInputConvergenceMinimap()
        {
            return new MinimapRuntime(new MinimapRuntimeConfig
            {
                InitialZoomNormalized = InputConvergenceInitialZoomNormalized,
                WheelZoomNormalizedStep = InputConvergenceWheelZoomNormalizedStep,
                ZoomSliderEnabled = true,
                ModeToggleEnabled = true,
                RotateToggleEnabled = true,
                DebugMarkerSampleCapacity = InputConvergenceDebugMarkerSampleCapacity,
                MinZoomExtentMode = MinimapZoomExtentMode.ExplicitCm,
                MinZoomExplicitHalfExtentCm = InputConvergenceMinZoomHalfExtentCm,
                MaxZoomExtentMode = MinimapZoomExtentMode.ExplicitCm,
                MaxZoomExplicitHalfExtentCm = InputConvergenceMaxZoomHalfExtentCm,
                InteractionContextId = "interaction.context.minimap",
                Actions = InputConvergenceMinimapActions,
            });
        }


        // Context mounts and action graphs run on the fixed step. A 1/60 visual frame
        // does not cross FixedDeltaTime (engine clock is 20 Hz), so it never opens the minimap context.
        private static void StepLogic(GameEngine engine) => engine.Tick(Time.FixedDeltaTime);

        private static GameEngine CreateMinimapContextEngine(out TestInputBackend backend, out MinimapRuntime minimap)
        {
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(FindRepoRoot(), new[] { "LudotsCoreMod", "CommandSourceCoreFixtureMod" }),
                Path.Combine(FindRepoRoot(), "assets"),
                Ludots.Tests.EngineUserData.CreateIsolatedRoot());
            // One Tick must finish the fixed step. The default 4ms budget yields inside
            // earlier time-sliced systems, so context mounts never reach action dispatch.
            engine.SimulationBudgetMsPerFrame = int.MaxValue;
            engine.Start();
            engine.LoadStartupMap();

            minimap = CreateInputConvergenceMinimap();
            var markerBuffer = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapMarkerBuffer missing.");
            var screenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapScreenMarkerBuffer missing.");
            engine.SetService(CoreServiceKeys.MinimapRuntime, minimap);
            minimap.Visible = true;
            minimap.UseRtsFullMapPreset();
            minimap.Refresh(engine, markerBuffer, screenMarkers);

            backend = new TestInputBackend();
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var handler = new PlayerInputHandler(backend, inputConfig);
            engine.GetService(CoreServiceKeys.ControlSchemeRuntime).ApplyActiveContexts(handler);

            engine.SetService(CoreServiceKeys.InputHandler, handler);
            engine.SetService(CoreServiceKeys.InputBackend, backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            return engine;
        }

        private static bool ContextActive(GameEngine engine, Entity hero, string profileName)
        {
            InteractionContextProfileRegistry profiles = engine.GetService(CoreServiceKeys.InteractionContextProfileRegistry)
                ?? throw new InvalidOperationException("InteractionContextProfileRegistry missing.");
            if (!profiles.ProfileIdRegistry.TryGetId(profileName, out int profileId))
            {
                throw new InvalidOperationException($"Interaction context '{profileName}' is not installed.");
            }

            Span<int> ids = stackalloc int[16];
            int count = InteractionContextInstanceRuntime.CopyActiveContextIdsNewestFirst(engine.World, hero, ids);
            for (int i = 0; i < count; i++)
            {
                if (ids[i] == profileId)
                {
                    return true;
                }
            }

            return false;
        }

        private static GameEngine CreateProfileEngine(params string[] mods)
        {
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(FindRepoRoot(), mods),
                Path.Combine(FindRepoRoot(), "assets"),
                Ludots.Tests.EngineUserData.CreateIsolatedRoot());
            return engine;
        }

        private static InteractionContextProfileDefinition RequireProfile(GameEngine engine, string profileName)
        {
            InteractionContextProfileRegistry profiles = engine.GetService(CoreServiceKeys.InteractionContextProfileRegistry)
                ?? throw new InvalidOperationException("InteractionContextProfileRegistry missing.");
            if (!profiles.ProfileIdRegistry.TryGetId(profileName, out int profileId) ||
                !profiles.TryGetDefinition(profileId, out InteractionContextProfileDefinition definition))
            {
                throw new InvalidOperationException($"Interaction context '{profileName}' is not installed.");
            }

            return definition;
        }

        private static List<string> TriggerIds(InteractionContextProfileDefinition definition)
        {
            var ids = new List<string>();
            if (definition.Triggers == null)
            {
                return ids;
            }

            for (int i = 0; i < definition.Triggers.Count; i++)
            {
                ids.Add(definition.Triggers[i].Trigger);
            }

            return ids;
        }

        private static TestInputBackend WireGameplayInput(GameEngine engine)
        {
            var backend = new TestInputBackend();
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var handler = new PlayerInputHandler(backend, inputConfig);
            engine.GetService(CoreServiceKeys.ControlSchemeRuntime).ApplyActiveContexts(handler);

            engine.SetService(CoreServiceKeys.InputHandler, handler);
            engine.SetService(CoreServiceKeys.InputBackend, backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            return backend;
        }

        private static Entity FindNamed(World world, string name)
        {
            var query = new QueryDescription().WithAll<Ludots.Core.Components.Name>();
            Entity found = Entity.Null;
            world.Query(in query, (Entity entity, ref Ludots.Core.Components.Name named) =>
            {
                if (string.Equals(named.Value, name, StringComparison.Ordinal))
                {
                    found = entity;
                }
            });
            return found;
        }

        private static (TestInputBackend backend, PlayerInputHandler handler) BuildCameraHandler()
        {
            var backend = new TestInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "PointerPos", Type = InputActionType.Axis2D },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Camera",
                        Priority = 1,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "PointerPos", Path = "<Mouse>/Pos", Processors = new() },
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Camera");
            return (backend, handler);
        }

        private static (TestInputBackend backend, PlayerInputHandler handler) BuildConfirmCommandHandler()
        {
            var backend = new TestInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Confirm", Type = InputActionType.Button },
                    new() { Id = "Command", Type = InputActionType.Button },
                    new() { Id = "Cancel", Type = InputActionType.Button },
                    new() { Id = "PointerPos", Type = InputActionType.Axis2D },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 1,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "Confirm", Path = "<Mouse>/LeftButton", Processors = new() },
                            new() { ActionId = "Command", Path = "<Mouse>/RightButton", Processors = new() },
                            new() { ActionId = "Cancel", Path = "<Keyboard>/escape", Processors = new() },
                            new() { ActionId = "PointerPos", Path = "<Mouse>/Pos", Processors = new() },
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");
            return (backend, handler);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "mods")) &&
                    File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Repository root not found from test work directory.");
        }

        private sealed class TestInputBackend : IInputBackend
        {
            public Dictionary<string, bool> Buttons { get; } = new Dictionary<string, bool>();
            public Vector2 MousePosition { get; set; }
            public float MouseWheel { get; set; }

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => Buttons.TryGetValue(devicePath, out var down) && down;
            public Vector2 GetMousePosition() => MousePosition;
            public float GetMouseWheel() => MouseWheel;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private sealed class StubViewController : IViewController
        {
            public Vector2 Resolution { get; } = new Vector2(1920f, 1080f);
            public float Fov => 60f;
            public float AspectRatio => Resolution.X / Resolution.Y;
        }

        private sealed class VerticalScreenRayProvider : IScreenRayProvider
        {
            public ScreenRay GetRay(Vector2 screenPosition)
            {
                return new ScreenRay(
                    new Vector3(screenPosition.X, 10f, screenPosition.Y),
                    new Vector3(0f, -1f, 0f));
            }
        }

        private sealed class CountingScreenRayProvider : IScreenRayProvider
        {
            public int CallCount { get; private set; }

            public ScreenRay GetRay(Vector2 screenPosition)
            {
                CallCount++;
                return new ScreenRay(
                    new Vector3(screenPosition.X, 10f, screenPosition.Y),
                    new Vector3(0f, -1f, 0f));
            }
        }

        private static readonly WorldAabbCm RaycastWorldBounds = new(-400000, -400000, 800000, 800000);

        private static int RaycastGroundCm(float screen)
        {
            return (int)MathF.Round(screen * 100f, MidpointRounding.AwayFromZero);
        }

        private static IContinuousHeightmap CreateFlatHeightmap()
        {
            return new ContinuousHeightmapRuntime(
                ContinuousHeightmapAsset.CreateSingleLayer(
                    RaycastWorldBounds,
                    sampleColumns: 2,
                    sampleRows: 2,
                    new short[]
                    {
                        0, 0,
                        0, 0,
                    }));
        }
    }
}
