using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Selection;
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
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class AuthoritativeInputConvergenceTests
    {
        [Test]
        public void AuthoritativeInputAccumulator_PreservesEdgesUntilConsumed()
        {
            var (backend, handler) = BuildHandler();
            var accumulator = new AuthoritativeInputAccumulator();
            var snapshot = new FrozenInputActionReader();

            handler.Update();
            accumulator.CaptureVisualFrame(handler);
            accumulator.BuildTickSnapshot(snapshot);
            Assert.That(snapshot.PressedThisFrame("Attack"), Is.False);
            Assert.That(snapshot.IsDown("Attack"), Is.False);

            backend.Buttons["<Keyboard>/a"] = true;
            handler.Update();
            accumulator.CaptureVisualFrame(handler);

            handler.Update();
            accumulator.CaptureVisualFrame(handler);

            accumulator.BuildTickSnapshot(snapshot);
            Assert.That(snapshot.PressedThisFrame("Attack"), Is.True);
            Assert.That(snapshot.IsDown("Attack"), Is.True);

            accumulator.BuildTickSnapshot(snapshot);
            Assert.That(snapshot.PressedThisFrame("Attack"), Is.False);
            Assert.That(snapshot.IsDown("Attack"), Is.True);

            backend.Buttons["<Keyboard>/a"] = false;
            handler.Update();
            accumulator.CaptureVisualFrame(handler);

            accumulator.BuildTickSnapshot(snapshot);
            Assert.That(snapshot.ReleasedThisFrame("Attack"), Is.True);
            Assert.That(snapshot.IsDown("Attack"), Is.False);
        }

        [Test]
        public void InputOrderMapping_HeldQuickTap_EmitsStartAndEndOnSameLogicTick()
        {
            var (backend, handler) = BuildHandler();
            var accumulator = new AuthoritativeInputAccumulator();
            var snapshot = new FrozenInputActionReader();
            var config = new InputOrderMappingConfig
            {
                Mappings = new List<InputOrderMapping>
                {
                    new()
                    {
                        ActionId = "Attack",
                        Trigger = InputTriggerType.Held,
                        HeldPolicy = HeldPolicy.StartEnd,
                        OrderTypeKey = "beam",
                        SelectionType = OrderSelectionType.None,
                        RequireSelection = false,
                        IsSkillMapping = false,
                    }
                }
            };

            var system = new InputOrderMappingSystem(snapshot, config);
            var orders = new List<Order>();
            system.SetOrderTypeKeyResolver(key => key switch
            {
                "beam.Start" => 101,
                "beam.End" => 102,
                "beam" => 100,
                _ => 0
            });
            system.SetOrderSubmitHandler((in Order order) => orders.Add(order));

            using var world = World.Create();
            system.SetLocalPlayer(world.Create(), 1);

            backend.Buttons["<Keyboard>/a"] = true;
            handler.Update();
            accumulator.CaptureVisualFrame(handler);

            backend.Buttons["<Keyboard>/a"] = false;
            handler.Update();
            accumulator.CaptureVisualFrame(handler);

            accumulator.BuildTickSnapshot(snapshot);
            system.Update(0f);

            Assert.That(orders.Count, Is.EqualTo(2));
            Assert.That(orders[0].OrderTypeId, Is.EqualTo(101));
            Assert.That(orders[1].OrderTypeId, Is.EqualTo(102));
        }

        [Test]
        public void GasInputResponseSystem_UsesAuthoritativeSnapshotInsteadOfLiveHandler()
        {
            using var world = World.Create();

            var liveInput = BuildHandler().handler;
            var authoritativeInput = new FrozenInputActionReader();
            authoritativeInput.SetActionState("Confirm", Vector3.One, isDown: true, pressedThisFrame: true, releasedThisFrame: false);

            var target = world.Create();
            var local = world.Create();
            var selection = new SelectionRuntime(
                world,
                new SelectionRuntimeConfig(),
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
            Assert.That(selection.ReplaceSelection(local, SelectionSetKeys.LivePrimary, new[] { target }), Is.True);
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = liveInput,
                [CoreServiceKeys.AuthoritativeInput.Name] = authoritativeInput,
                [CoreServiceKeys.AuthoritativePointerButtons.Name] = new AuthoritativePointerButtonSnapshot(),
                [CoreServiceKeys.AbilityInputRequestQueue.Name] = new InputRequestQueue(),
                [CoreServiceKeys.InputResponseBuffer.Name] = new InputResponseBuffer(),
                [CoreServiceKeys.LocalPlayerEntity.Name] = local,
                [CoreServiceKeys.SelectionRuntime.Name] = selection,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings { ConfirmActionId = "Confirm" },
            };
            ((AuthoritativePointerButtonSnapshot)globals[CoreServiceKeys.AuthoritativePointerButtons.Name]).SetState(
                "Confirm",
                new PointerButtonState(
                    Vector2.Zero,
                    Vector2.Zero,
                    Vector2.Zero,
                    Vector2.Zero,
                    isDown: true,
                    pressedThisFrame: true,
                    releasedThisFrame: false,
                    hasPressPointer: true,
                    hasReleasePointer: false,
                    hasLastDownPointer: true));

            var system = new GasInputResponseSystem(world, globals);
            var requests = (InputRequestQueue)globals[CoreServiceKeys.AbilityInputRequestQueue.Name];
            var responses = (InputResponseBuffer)globals[CoreServiceKeys.InputResponseBuffer.Name];
            requests.TryEnqueue(new InputRequest { RequestId = 7, RequestTagId = 700 });

            system.Update(0f);

            Assert.That(responses.TryConsume(7, out var response), Is.True);
            Assert.That(response.Target, Is.EqualTo(target));
            Assert.That(response.ResponseTagId, Is.EqualTo(700));
        }

        [Test]
        public void InputRuntimeSystem_UiCaptured_SuppressesCameraUserInputOnlyForCapturedFrames()
        {
            var (backend, handler) = BuildCameraHandler();
            var session = new GameSession();
            var registry = new VirtualCameraRegistry();
            registry.Register(new VirtualCameraDefinition
            {
                Id = "EdgePan",
                Priority = 0,
                RigKind = CameraRigKind.Orbit,
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
            session.Camera.SetVirtualCameraRegistry(registry);
            session.Camera.ConfigureRuntime(handler, new StubViewController());
            session.Camera.ActivateVirtualCamera("EdgePan", blendDurationSeconds: 0f);

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = handler,
                [CoreServiceKeys.GameSession.Name] = session,
                [CoreServiceKeys.UiCaptured.Name] = true,
            };

            var system = new InputRuntimeSystem(globals);

            backend.MousePosition = Vector2.Zero;
            system.Update(1f);
            session.Camera.Update(1f);

            Assert.That(session.Camera.State.TargetCm.X, Is.EqualTo(0f).Within(0.01f));
            Assert.That(session.Camera.State.TargetCm.Y, Is.EqualTo(0f).Within(0.01f));

            globals[CoreServiceKeys.UiCaptured.Name] = false;
            system.Update(1f);
            session.Camera.Update(1f);

            Assert.That(session.Camera.State.TargetCm.Length(), Is.GreaterThan(0.01f));
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
                [CoreServiceKeys.VisualHeightmap.Name] = CreateFlatHeightmap(),
                [CoreServiceKeys.WorldSizeSpec.Name] = new WorldSizeSpec(new WorldAabbCm(-100000, -100000, 200000, 200000), 100),
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings(),
            };

            var system = new InputRuntimeSystem(globals, accumulator);
            system.Update(1f / 60f);
            accumulator.BuildTickSnapshot(snapshot);

            Assert.That(AuthoritativeGroundPointerHelper.TryRead(snapshot, out var worldCm), Is.True);
            Assert.That(worldCm.X, Is.EqualTo(1250));
            Assert.That(worldCm.Y, Is.EqualTo(3475));
        }

        [Test]
        public void InputRuntimeSystem_CapturesPointerButtonPressAndReleaseScreenPositionsWithinOneLogicTick()
        {
            var (backend, handler) = BuildSelectionHandler();
            var pointerButtons = new AuthoritativePointerButtonAccumulator();
            var snapshot = new AuthoritativePointerButtonSnapshot();
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = handler,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings()
            };

            var system = new InputRuntimeSystem(globals, pointerButtons: pointerButtons);

            backend.MousePosition = new Vector2(120f, 240f);
            system.Update(1f / 60f);

            backend.Buttons["<Mouse>/LeftButton"] = true;
            system.Update(1f / 60f);

            backend.MousePosition = new Vector2(420f, 540f);
            system.Update(1f / 60f);

            backend.Buttons["<Mouse>/LeftButton"] = false;
            system.Update(1f / 60f);

            pointerButtons.BuildTickSnapshot(snapshot);

            Assert.That(snapshot.TryGetState("Select", out var state), Is.True);
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
        public void InputRuntimeSystem_MinimapConsumerCapturesPointerAndZoomThroughUnifiedInput()
        {
            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(FindRepoRoot(), new[] { "LudotsCoreMod", "CoreInputMod" }),
                Path.Combine(FindRepoRoot(), "assets"));

            var backend = new TestInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Select", Type = InputActionType.Button },
                    new() { Id = "Command", Type = InputActionType.Button },
                    new() { Id = "Cancel", Type = InputActionType.Button },
                    new() { Id = "PointerPos", Type = InputActionType.Axis2D },
                    new() { Id = "Zoom", Type = InputActionType.Axis1D },
                    new() { Id = MinimapInputActions.Zoom, Type = InputActionType.Axis1D },
                    new() { Id = MinimapInputActions.ToggleRotateWithCamera, Type = InputActionType.Button },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 1,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "Select", Path = "<Mouse>/LeftButton", Processors = new() },
                            new() { ActionId = "Command", Path = "<Mouse>/RightButton", Processors = new() },
                            new() { ActionId = "Cancel", Path = "<Keyboard>/escape", Processors = new() },
                            new() { ActionId = "PointerPos", Path = "<Mouse>/Pos", Processors = new() },
                            new() { ActionId = "Zoom", Path = "<Mouse>/ScrollY", Processors = new() },
                            new() { ActionId = MinimapInputActions.Zoom, Path = "<Mouse>/ScrollY", Processors = new() },
                            new() { ActionId = MinimapInputActions.ToggleRotateWithCamera, Path = "<Keyboard>/f7", Processors = new() },
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");

            var minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
                ?? throw new InvalidOperationException("MinimapRuntime missing.");
            var markerBuffer = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapMarkerBuffer missing.");
            var screenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapScreenMarkerBuffer missing.");
            minimap.Visible = true;
            minimap.UseRtsFullMapPreset();
            minimap.Refresh(engine, markerBuffer, screenMarkers);

            engine.SetService(CoreServiceKeys.InputHandler, handler);
            engine.SetService(CoreServiceKeys.InputBackend, backend);
            engine.SetService(CoreServiceKeys.InteractionActionBindings, new InteractionActionBindings());
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.SetService(CoreServiceKeys.PointerInputCaptured, false);
            engine.SetService(CoreServiceKeys.InputFrameConsumers, new List<IInputFrameConsumer> { new MinimapInputConsumer(minimap) });

            var input = new AuthoritativeInputAccumulator();
            var pointerButtons = new AuthoritativePointerButtonAccumulator();
            var pointerSnapshot = new AuthoritativePointerButtonSnapshot();
            var system = new InputRuntimeSystem(engine.GlobalContext, input, pointerButtons);

            Vector2 click = new(minimap.FieldX + (minimap.FieldSize * 0.75f), minimap.FieldY + (minimap.FieldSize * 0.25f));
            float beforeHalfExtent = minimap.HalfExtentCm;
            backend.MousePosition = click;
            backend.MouseWheel = 1f;
            backend.Buttons["<Mouse>/LeftButton"] = true;
            Assert.That(minimap.TryScreenToWorld(click, out Vector2 expectedTarget), Is.True);

            system.Update(1f / 60f);
            pointerButtons.BuildTickSnapshot(pointerSnapshot);

            Assert.That(minimap.HalfExtentCm, Is.LessThan(beforeHalfExtent));
            Assert.That(handler.PressedThisFrame("Select"), Is.False, "Minimap click must consume the shared confirm action before authoritative gameplay capture.");
            Assert.That(handler.ReadAction<float>("Zoom"), Is.EqualTo(0f), "Minimap wheel must suppress the shared camera zoom action for this frame.");
            Assert.That(pointerSnapshot.TryGetState("Select", out var selectState), Is.True);
            Assert.That(selectState.PressedThisFrame, Is.False, "Pointer buttons snapshot must not leak minimap clicks into gameplay selection.");

            Assert.That(engine.GameSession.Camera.State.TargetCm.X, Is.EqualTo(expectedTarget.X).Within(1f));
            Assert.That(engine.GameSession.Camera.State.TargetCm.Y, Is.EqualTo(expectedTarget.Y).Within(1f));
        }

        [Test]
        public void InputRuntimeSystem_MinimapCommandClickOverridesAuthoritativeGroundPointer()
        {
            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(FindRepoRoot(), new[] { "LudotsCoreMod", "CoreInputMod" }),
                Path.Combine(FindRepoRoot(), "assets"));

            var backend = new TestInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Select", Type = InputActionType.Button },
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
                            new() { ActionId = "Select", Path = "<Mouse>/LeftButton", Processors = new() },
                            new() { ActionId = "Command", Path = "<Mouse>/RightButton", Processors = new() },
                            new() { ActionId = "Cancel", Path = "<Keyboard>/escape", Processors = new() },
                            new() { ActionId = "PointerPos", Path = "<Mouse>/Pos", Processors = new() },
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");

            var minimap = new MinimapRuntime(new MinimapRuntimeConfig
            {
                MinZoomExtentMode = MinimapZoomExtentMode.ExplicitCm,
                MinZoomExplicitHalfExtentCm = 750f,
                MaxZoomExtentMode = MinimapZoomExtentMode.ExplicitCm,
                MaxZoomExplicitHalfExtentCm = 22000f,
            });
            var markerBuffer = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapMarkerBuffer missing.");
            var screenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapScreenMarkerBuffer missing.");
            minimap.Visible = true;
            minimap.UseRtsFullMapPreset();
            minimap.Refresh(engine, markerBuffer, screenMarkers);

            var rayProvider = new CountingScreenRayProvider();
            engine.SetService(CoreServiceKeys.MinimapRuntime, minimap);
            engine.SetService(CoreServiceKeys.InputHandler, handler);
            engine.SetService(CoreServiceKeys.InputBackend, backend);
            engine.SetService(CoreServiceKeys.InteractionActionBindings, new InteractionActionBindings());
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.SetService(CoreServiceKeys.PointerInputCaptured, false);
            engine.SetService(CoreServiceKeys.InputFrameConsumers, new List<IInputFrameConsumer> { new MinimapInputConsumer(minimap) });
            engine.SetService(CoreServiceKeys.ScreenRayProvider, rayProvider);
            engine.SetService(CoreServiceKeys.VisualHeightmap, CreateFlatHeightmap());
            engine.SetService(CoreServiceKeys.WorldSizeSpec, new WorldSizeSpec(new WorldAabbCm(-100000, -100000, 200000, 200000), 100));

            var input = new AuthoritativeInputAccumulator();
            var snapshot = new FrozenInputActionReader();
            var pointerButtons = new AuthoritativePointerButtonAccumulator();
            var system = new InputRuntimeSystem(engine.GlobalContext, input, pointerButtons);

            Vector2 click = new(minimap.FieldX + (minimap.FieldSize * 0.80f), minimap.FieldY + (minimap.FieldSize * 0.20f));
            Assert.That(minimap.TryScreenToWorld(click, out Vector2 expectedWorldCm), Is.True);

            backend.MousePosition = click;
            backend.Buttons["<Mouse>/RightButton"] = true;
            system.Update(1f / 60f);
            input.BuildTickSnapshot(snapshot);

            Assert.That(snapshot.PressedThisFrame("Command"), Is.True, "Minimap command click must still enter the formal command action chain.");
            Assert.That(AuthoritativeGroundPointerHelper.TryRead(snapshot, out var worldCm), Is.True);
            Assert.That(worldCm.X, Is.EqualTo((int)MathF.Round(expectedWorldCm.X, MidpointRounding.AwayFromZero)));
            Assert.That(worldCm.Y, Is.EqualTo((int)MathF.Round(expectedWorldCm.Y, MidpointRounding.AwayFromZero)));
            Assert.That(rayProvider.CallCount, Is.EqualTo(0), "Minimap command click must not fall through to main viewport ground raycast.");
            Assert.That(engine.GetService(CoreServiceKeys.AuthoritativeGroundPointerOverride)?.HasOverride, Is.False);
        }

        [Test]
        public void InputRuntimeSystem_MinimapConsumerScopesWheelDragAndRotationToggleToUnifiedInput()
        {
            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(FindRepoRoot(), new[] { "LudotsCoreMod", "CoreInputMod" }),
                Path.Combine(FindRepoRoot(), "assets"));

            var backend = new TestInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Select", Type = InputActionType.Button },
                    new() { Id = "Command", Type = InputActionType.Button },
                    new() { Id = "Cancel", Type = InputActionType.Button },
                    new() { Id = "PointerPos", Type = InputActionType.Axis2D },
                    new() { Id = "Zoom", Type = InputActionType.Axis1D },
                    new() { Id = MinimapInputActions.Zoom, Type = InputActionType.Axis1D },
                    new() { Id = MinimapInputActions.ToggleRotateWithCamera, Type = InputActionType.Button },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 1,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "Select", Path = "<Mouse>/LeftButton", Processors = new() },
                            new() { ActionId = "Command", Path = "<Mouse>/RightButton", Processors = new() },
                            new() { ActionId = "Cancel", Path = "<Keyboard>/escape", Processors = new() },
                            new() { ActionId = "PointerPos", Path = "<Mouse>/Pos", Processors = new() },
                            new() { ActionId = "Zoom", Path = "<Mouse>/ScrollY", Processors = new() },
                            new() { ActionId = MinimapInputActions.Zoom, Path = "<Mouse>/ScrollY", Processors = new() },
                            new() { ActionId = MinimapInputActions.ToggleRotateWithCamera, Path = "<Keyboard>/f7", Processors = new() },
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");

            var minimap = engine.GetService(CoreServiceKeys.MinimapRuntime)
                ?? throw new InvalidOperationException("MinimapRuntime missing.");
            var markerBuffer = engine.GetService(CoreServiceKeys.MinimapMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapMarkerBuffer missing.");
            var screenMarkers = engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer)
                ?? throw new InvalidOperationException("MinimapScreenMarkerBuffer missing.");
            minimap.Visible = true;
            minimap.UseRtsFullMapPreset();
            engine.GameSession.Camera.ApplyPose(new CameraPoseRequest { Yaw = 90f });
            minimap.Refresh(engine, markerBuffer, screenMarkers);

            engine.SetService(CoreServiceKeys.InputHandler, handler);
            engine.SetService(CoreServiceKeys.InputBackend, backend);
            engine.SetService(CoreServiceKeys.InteractionActionBindings, new InteractionActionBindings());
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.SetService(CoreServiceKeys.PointerInputCaptured, false);
            engine.SetService(CoreServiceKeys.InputFrameConsumers, new List<IInputFrameConsumer> { new MinimapInputConsumer(minimap) });

            var input = new AuthoritativeInputAccumulator();
            var pointerButtons = new AuthoritativePointerButtonAccumulator();
            var system = new InputRuntimeSystem(engine.GlobalContext, input, pointerButtons);

            Vector2 outside = new(minimap.FieldX - 12f, minimap.FieldY - 12f);
            float halfBeforeOutsideWheel = minimap.HalfExtentCm;
            float zoomBeforeOutsideWheel = minimap.ZoomNormalized;
            backend.MousePosition = outside;
            backend.MouseWheel = 1f;
            system.Update(1f / 60f);

            Assert.That(minimap.HalfExtentCm, Is.EqualTo(halfBeforeOutsideWheel).Within(0.01f));
            Assert.That(minimap.ZoomNormalized, Is.EqualTo(zoomBeforeOutsideWheel).Within(0.0001f));
            Assert.That(handler.ReadAction<float>("Zoom"), Is.EqualTo(1f), "Wheel outside minimap must remain available to camera/gameplay input.");

            backend.MouseWheel = 0f;
            Vector2 slider = new(minimap.ZoomSliderX + (minimap.ZoomSliderWidth * 0.2f), minimap.ZoomSliderY + (minimap.ZoomSliderHeightPx * 0.5f));
            backend.MousePosition = slider;
            backend.Buttons["<Mouse>/LeftButton"] = true;
            system.Update(1f / 60f);
            Assert.That(minimap.ZoomNormalized, Is.EqualTo(0.2f).Within(0.03f), "Zoom slider must write the same normalized zoom state used by wheel zoom.");
            float halfAfterSlider = minimap.HalfExtentCm;
            float zoomAfterSlider = minimap.ZoomNormalized;
            backend.Buttons["<Mouse>/LeftButton"] = false;
            system.Update(1f / 60f);

            Vector2 insideFieldForWheel = new(minimap.FieldX + (minimap.FieldSize * 0.5f), minimap.FieldY + (minimap.FieldSize * 0.5f));
            backend.MousePosition = insideFieldForWheel;
            backend.MouseWheel = 1f;
            system.Update(1f / 60f);
            Assert.That(minimap.ZoomNormalized, Is.LessThan(zoomAfterSlider), "Wheel zoom must mutate the same normalized zoom SSOT as the slider.");
            Assert.That(minimap.HalfExtentCm, Is.LessThan(halfAfterSlider), "Wheel zoom in should shrink the metric half extent.");

            backend.MouseWheel = 0f;
            Vector2 firstDrag = new(minimap.FieldX + (minimap.FieldSize * 0.25f), minimap.FieldY + (minimap.FieldSize * 0.25f));
            Vector2 secondDrag = new(minimap.FieldX + (minimap.FieldSize * 0.85f), minimap.FieldY + (minimap.FieldSize * 0.72f));
            backend.MousePosition = firstDrag;
            backend.Buttons["<Mouse>/LeftButton"] = true;
            Assert.That(minimap.TryScreenToWorld(firstDrag, out Vector2 expectedFirstTarget), Is.True);
            system.Update(1f / 60f);
            Assert.That(engine.GameSession.Camera.State.TargetCm.X, Is.EqualTo(expectedFirstTarget.X).Within(1f));
            Assert.That(engine.GameSession.Camera.State.TargetCm.Y, Is.EqualTo(expectedFirstTarget.Y).Within(1f));

            backend.MousePosition = secondDrag;
            Assert.That(minimap.TryScreenToWorld(secondDrag, out Vector2 expectedSecondTarget), Is.True);
            system.Update(1f / 60f);
            Assert.That(engine.GameSession.Camera.State.TargetCm.X, Is.EqualTo(expectedSecondTarget.X).Within(1f));
            Assert.That(engine.GameSession.Camera.State.TargetCm.Y, Is.EqualTo(expectedSecondTarget.Y).Within(1f));
            Assert.That(handler.IsDown("Select"), Is.False, "Held minimap drag must keep suppressing gameplay select.");

            backend.Buttons["<Mouse>/LeftButton"] = false;
            system.Update(1f / 60f);

            Vector2 presetToggle = new(
                minimap.PresetToggleX + (minimap.PresetToggleWidth * 0.5f),
                minimap.PresetToggleY + (minimap.PresetToggleHeight * 0.5f));
            backend.MousePosition = presetToggle;
            backend.Buttons["<Mouse>/LeftButton"] = true;
            system.Update(1f / 60f);
            Assert.That(minimap.Preset, Is.EqualTo(MinimapPreset.FollowCamera), "Mode toggle button must switch to follow-camera through the shared pointer confirm input.");
            Assert.That(handler.PressedThisFrame("Select"), Is.False, "Mode toggle clicks must not leak into gameplay selection.");

            backend.Buttons["<Mouse>/LeftButton"] = false;
            system.Update(1f / 60f);

            Vector2 rotateToggle = new(
                minimap.RotateToggleX + (minimap.RotateToggleWidth * 0.5f),
                minimap.RotateToggleY + (minimap.RotateToggleHeight * 0.5f));
            bool beforeRotation = minimap.RotateWithCamera;
            backend.MousePosition = rotateToggle;
            backend.Buttons["<Mouse>/LeftButton"] = true;
            system.Update(1f / 60f);
            Assert.That(minimap.RotateWithCamera, Is.EqualTo(!beforeRotation), "Rotate toggle button must use the shared pointer confirm input.");
            Assert.That(handler.PressedThisFrame("Select"), Is.False, "Rotate toggle clicks must not leak into gameplay selection.");

            backend.Buttons["<Mouse>/LeftButton"] = false;
            system.Update(1f / 60f);

            beforeRotation = minimap.RotateWithCamera;
            backend.Buttons["<Keyboard>/f7"] = true;
            system.Update(1f / 60f);
            Assert.That(minimap.RotateWithCamera, Is.EqualTo(!beforeRotation));
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

        private static (TestInputBackend backend, PlayerInputHandler handler) BuildSelectionHandler()
        {
            var backend = new TestInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "Select", Type = InputActionType.Button },
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
                            new() { ActionId = "Select", Path = "<Mouse>/LeftButton", Processors = new() },
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

        private static IVisualHeightmap CreateFlatHeightmap()
        {
            return new VisualHeightmapRuntime(
                VisualHeightmapAsset.CreateSingleLayer(
                    new WorldAabbCm(-100000, -100000, 200000, 200000),
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
