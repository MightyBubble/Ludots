using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;
using Ludots.Platform.Abstractions;
 
namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class ResponseChainPresenterPipelineTests
    {
        [Test]
        public void PromptInput_PublishesOrderRequest_AndConsumesOrderTypeId()
        {
            var world = World.Create();
            try
            {
                const int tag = 1001;
                const int tplRoot = 10;
 
                var templates = new EffectTemplateRegistry();
                templates.Register(tplRoot, new EffectTemplateData
                {
                    TagId = tag,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = true,
                    Modifiers = default
                });
                FinalizeEffectTemplates(templates);
 
                var clock = new DiscreteClock();
                var conditions = new GasConditionRegistry();
                var budget = new GasBudget();
                var requests = new EffectRequestQueue();
                var inputReq = new InputRequestQueue();
                var admissionResults = new OrderAdmissionResultBuffer(64, 64);
                var chainOrders = new OrderQueue(64, admissionResults);
                var telemetry = new ResponseChainTelemetryBuffer();
                var orderReq = new OrderRequestQueue();
 
                var processing = new EffectProcessingLoopSystem(
                    world,
                    requests,
                    clock,
                    conditions,
                    16384,
                    GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                    budget,
                    templates,
                    inputReq,
                    chainOrders,
                    telemetry,
                    orderReq,
                    responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                    tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()))
                {
                    MaxWorkUnitsPerSlice = int.MaxValue
                };
 
                var target = world.Create(new AttributeBuffer(), new ActiveEffectContainer(), new DirtyFlags(), new PlayerOwner { PlayerId = 1 });
                var listener = default(ResponseChainListener);
                listener.Add(tag, ResponseType.PromptInput, priority: 100, effectTemplateId: tplRoot);
                world.Add(target, listener);
 
                admissionResults.BeginLogicStep();
                requests.Publish(new EffectRequest
                {
                    RootId = 0,
                    Source = target,
                    Target = target,
                    TargetContext = default,
                    TemplateId = tplRoot
                });
 
                processing.Update(0f);
 
                That(orderReq.TryDequeue(out var req), Is.True);
                That(req.PlayerId, Is.EqualTo(1));
                That(req.PromptTagId, Is.EqualTo(tplRoot));
                That(req.AllowedCount, Is.GreaterThanOrEqualTo(2));
 
                var args = default(OrderArgs);
                args.I0 = tplRoot;
                var activate = new Order { OrderTypeId = TestResponseChainOrderTypeIds.ChainActivateEffect, PlayerId = 1, Actor = target, Target = target, Args = args };
                var pass1 = new Order { OrderTypeId = TestResponseChainOrderTypeIds.ChainPass, PlayerId = 1, Actor = target, Target = target };
                var pass2 = new Order { OrderTypeId = TestResponseChainOrderTypeIds.ChainPass, PlayerId = 1, Actor = target, Target = target };
                That(chainOrders.SubmitAssigned(ref activate), Is.EqualTo(OrderSubmitResult.Queued));
                That(chainOrders.SubmitAssigned(ref pass1), Is.EqualTo(OrderSubmitResult.Queued));
                That(chainOrders.SubmitAssigned(ref pass2), Is.EqualTo(OrderSubmitResult.Queued));
 
                processing.Update(0f);

                That(admissionResults.TryGet(activate.OrderId, OrderAdmissionStage.EntityIntake, out var activateIntake), Is.True);
                That(activateIntake.Result, Is.EqualTo(OrderSubmitResult.Activated));
                That(admissionResults.TryGet(pass1.OrderId, OrderAdmissionStage.EntityIntake, out var pass1Intake), Is.True);
                That(pass1Intake.Result, Is.EqualTo(OrderSubmitResult.Activated));
                That(admissionResults.TryGet(pass2.OrderId, OrderAdmissionStage.EntityIntake, out var pass2Intake), Is.True);
                That(pass2Intake.Result, Is.EqualTo(OrderSubmitResult.Activated));
 
                bool sawAdded = false;
                bool sawClosed = false;
                for (int i = 0; i < telemetry.Count; i++)
                {
                    var e = telemetry[i];
                    if (e.Kind == ResponseChainTelemetryKind.ProposalAdded) sawAdded = true;
                    if (e.Kind == ResponseChainTelemetryKind.WindowClosed) sawClosed = true;
                }
 
                That(sawAdded, Is.True);
                That(sawClosed, Is.True);
                admissionResults.EndEntityIntake();
                admissionResults.EndLogicStep();
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void ResponseChain_ConsumedOrders_DoNotCarryGlobalIntakeAcross10000LogicSteps()
        {
            using var world = World.Create();
            const int tag = 1001;
            const int tplRoot = 10;

            var templates = new EffectTemplateRegistry();
            templates.Register(tplRoot, new EffectTemplateData
            {
                TagId = tag,
                LifetimeKind = EffectLifetimeKind.Instant,
                ClockId = GasClockId.Step,
                ParticipatesInResponse = true,
                Modifiers = default
            });
            FinalizeEffectTemplates(templates);

            var requests = new EffectRequestQueue();
            var admissionResults = new OrderAdmissionResultBuffer(8, 8);
            var chainOrders = new OrderQueue(8, admissionResults);
            var inputRequests = new InputRequestQueue(capacity: 8);
            var orderRequests = new OrderRequestQueue(capacity: 8);
            var processing = new EffectProcessingLoopSystem(
                world,
                requests,
                new DiscreteClock(),
                new GasConditionRegistry(),
                16384,
                GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                new GasBudget(),
                templates,
                inputRequests,
                chainOrders,
                new ResponseChainTelemetryBuffer(),
                orderRequests,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()))
            {
                MaxWorkUnitsPerSlice = int.MaxValue
            };

            var actor = world.Create(new AttributeBuffer(), new ActiveEffectContainer(), new DirtyFlags(), new PlayerOwner { PlayerId = 1 });
            var listener = default(ResponseChainListener);
            listener.Add(tag, ResponseType.PromptInput, priority: 100, effectTemplateId: tplRoot);
            world.Add(actor, listener);

            int previousPass1Id = 0;
            int previousPass2Id = 0;
            for (int frame = 0; frame < 10_000; frame++)
            {
                admissionResults.BeginLogicStep();
                if (previousPass1Id > 0)
                {
                    That(admissionResults.TryGet(previousPass1Id, OrderAdmissionStage.GlobalIntake, out _), Is.False);
                    That(admissionResults.TryGet(previousPass2Id, OrderAdmissionStage.GlobalIntake, out _), Is.False);
                }

                requests.Publish(new EffectRequest
                {
                    Source = actor,
                    Target = actor,
                    TemplateId = tplRoot
                });
                processing.Update(0f);
                That(inputRequests.TryDequeue(out _), Is.True);
                That(orderRequests.TryDequeue(out _), Is.True);

                var pass1 = new Order { OrderTypeId = TestResponseChainOrderTypeIds.ChainPass, PlayerId = 1, Actor = actor, Target = actor };
                var pass2 = new Order { OrderTypeId = TestResponseChainOrderTypeIds.ChainPass, PlayerId = 1, Actor = actor, Target = actor };
                That(chainOrders.SubmitAssigned(ref pass1), Is.EqualTo(OrderSubmitResult.Queued));
                That(chainOrders.SubmitAssigned(ref pass2), Is.EqualTo(OrderSubmitResult.Queued));
                processing.Update(0f);

                That(admissionResults.TryGet(pass1.OrderId, OrderAdmissionStage.EntityIntake, out var pass1Intake), Is.True);
                That(pass1Intake.Result, Is.EqualTo(OrderSubmitResult.Activated));
                That(admissionResults.TryGet(pass2.OrderId, OrderAdmissionStage.EntityIntake, out var pass2Intake), Is.True);
                That(pass2Intake.Result, Is.EqualTo(OrderSubmitResult.Activated));
                That(chainOrders.Count, Is.Zero);

                previousPass1Id = pass1.OrderId;
                previousPass2Id = pass2.OrderId;
                admissionResults.EndEntityIntake();
                admissionResults.EndLogicStep();
            }
        }

        [Test]
        public void ResponseChainDirectorSystem_ConsumesQueuedRequests_AndClosesUiState()
        {
            using var world = World.Create();

            var orderRequests = new OrderRequestQueue();
            var telemetry = new ResponseChainTelemetryBuffer();
            var ui = new ResponseChainUiState();
            var markers = new TransientMarkerBuffer();
            var meshes = new MeshAssetRegistry();
            var presenters = new PresenterDefinitionRegistry();

            var actor = world.Create();
            var request = default(OrderRequest);
            request.PlayerId = 7;
            request.PromptTagId = 7001;
            request.Actor = actor;
            request.Target = actor;
            request.AddAllowed(TestResponseChainOrderTypeIds.ChainPass);

            That(orderRequests.TryEnqueue(request), Is.True);

            var system = new ResponseChainDirectorSystem(world, orderRequests, telemetry, ui, markers, meshes, presenters);
            system.Update(0f);

            That(ui.Visible, Is.True);
            That(ui.RootId, Is.GreaterThan(0));
            That(ui.PlayerId, Is.EqualTo(7));
            That(ui.PromptTagId, Is.EqualTo(7001));
            That(ui.AllowedCount, Is.EqualTo(1));
            That(ui.AllowedOrderTypeIds[0], Is.EqualTo(TestResponseChainOrderTypeIds.ChainPass));

            That(telemetry.TryAdd(new ResponseChainTelemetryEvent
            {
                Kind = ResponseChainTelemetryKind.WindowClosed,
                RootId = ui.RootId,
                Source = actor,
                Target = actor
            }), Is.True);

            system.Update(0f);

            That(ui.Visible, Is.False);
        }

        [Test]
        public void ResponseChainDirectorSystem_RejectsReplacingActiveRootBeforeClose()
        {
            using var world = World.Create();

            var orderRequests = new OrderRequestQueue();
            var telemetry = new ResponseChainTelemetryBuffer();
            var ui = new ResponseChainUiState();
            var markers = new TransientMarkerBuffer();
            var meshes = new MeshAssetRegistry();
            var presenters = new PresenterDefinitionRegistry();

            var actor = world.Create();
            var system = new ResponseChainDirectorSystem(world, orderRequests, telemetry, ui, markers, meshes, presenters);

            var first = default(OrderRequest);
            first.PlayerId = 1;
            first.PromptTagId = 100;
            first.Actor = actor;
            first.Target = actor;
            That(orderRequests.TryEnqueue(first), Is.True);

            system.Update(0f);

            var second = default(OrderRequest);
            second.PlayerId = 2;
            second.PromptTagId = 200;
            second.Actor = actor;
            second.Target = actor;
            That(orderRequests.TryEnqueue(second), Is.True);

            var ex = Assert.Throws<System.InvalidOperationException>(() => system.Update(0f));
            That(ex?.Message, Does.Contain("cannot replace active root"));
        }

        [Test]
        public void ResponseChainDirectorSystem_OnlyEmitsCueMarkersForResolvedTelemetry()
        {
            using var world = World.Create();

            var orderRequests = new OrderRequestQueue();
            var telemetry = new ResponseChainTelemetryBuffer();
            var ui = new ResponseChainUiState();
            var markers = new TransientMarkerBuffer();
            var meshes = new MeshAssetRegistry();
            var presenters = new PresenterDefinitionRegistry();
            RegisterAuthoredCueMarker(meshes, presenters);

            var actor = world.Create(new VisualTransform { Position = new Vector3(2f, 0f, 3f), Scale = Vector3.One });
            That(telemetry.TryAdd(new ResponseChainTelemetryEvent
            {
                Kind = ResponseChainTelemetryKind.WindowOpened,
                RootId = 1,
                Source = actor,
                Target = actor
            }), Is.True);
            That(telemetry.TryAdd(new ResponseChainTelemetryEvent
            {
                Kind = ResponseChainTelemetryKind.ProposalResolved,
                RootId = 1,
                Source = actor,
                Target = actor,
                Outcome = ResponseChainResolveOutcome.AppliedInstant
            }), Is.True);

            var system = new ResponseChainDirectorSystem(world, orderRequests, telemetry, ui, markers, meshes, presenters);
            system.Update(0f);

            That(markers.Count, Is.EqualTo(1), "Only resolved response-chain telemetry should emit cue markers.");
        }

        [Test]
        public void ResponseChainHumanOrderSourceSystem_UsesSharedInputBindings()
        {
            using var world = World.Create();

            var (backend, handler) = BuildResponseChainHandler();
            var actor = world.Create();
            var ui = new ResponseChainUiState();
            var request = default(OrderRequest);
            request.PlayerId = 3;
            request.PromptTagId = 9001;
            request.Actor = actor;
            request.Target = actor;
            request.TargetContext = Entity.Null;
            ui.ApplyRequest(request);

            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = handler,
                [CoreServiceKeys.GameConfig.Name] = new GameConfig
                {
                    Constants = new GameConstants
                    {
                        ResponseChainOrderTypeIds = new Dictionary<string, int>
                        {
                            ["chainPass"] = TestResponseChainOrderTypeIds.ChainPass,
                            ["chainNegate"] = TestResponseChainOrderTypeIds.ChainNegate,
                            ["chainActivateEffect"] = TestResponseChainOrderTypeIds.ChainActivateEffect
                        }
                    }
                },
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings
                {
                    ResponseChainPassActionId = "UiPass",
                    ResponseChainNegateActionId = "UiNegate",
                    ResponseChainActivateActionId = "UiActivate"
                }
            };

            var chainOrders = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));
            var system = new ResponseChainHumanOrderSourceSystem(globals, ui, chainOrders);

            PressButton(handler, backend, "<Keyboard>/f");
            system.Update(0f);
            ReleaseButton(handler, backend, "<Keyboard>/f");
            That(chainOrders.TryDequeue(out var pass), Is.True);
            That(pass.OrderTypeId, Is.EqualTo(TestResponseChainOrderTypeIds.ChainPass));
            That(pass.PlayerId, Is.EqualTo(3));

            PressButton(handler, backend, "<Keyboard>/g");
            system.Update(0f);
            ReleaseButton(handler, backend, "<Keyboard>/g");
            That(chainOrders.TryDequeue(out var negate), Is.True);
            That(negate.OrderTypeId, Is.EqualTo(TestResponseChainOrderTypeIds.ChainNegate));

            PressButton(handler, backend, "<Keyboard>/h");
            system.Update(0f);
            ReleaseButton(handler, backend, "<Keyboard>/h");
            That(chainOrders.TryDequeue(out var activate), Is.True);
            That(activate.OrderTypeId, Is.EqualTo(TestResponseChainOrderTypeIds.ChainActivateEffect));
            That(activate.Args.I0, Is.EqualTo(9001));
        }

        [Test]
        public void ResponseChainHumanOrderSourceSystem_QueueFullPublishesRejectedOrderResult()
        {
            using var world = World.Create();
            var (backend, handler) = BuildResponseChainHandler();
            var actor = world.Create();
            var ui = new ResponseChainUiState();
            var request = default(OrderRequest);
            request.PlayerId = 3;
            request.PromptTagId = 9001;
            request.Actor = actor;
            request.Target = actor;
            request.TargetContext = Entity.Null;
            ui.ApplyRequest(request);
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = handler,
                [CoreServiceKeys.GameConfig.Name] = new GameConfig
                {
                    Constants = new GameConstants
                    {
                        ResponseChainOrderTypeIds = new Dictionary<string, int>
                        {
                            ["chainPass"] = TestResponseChainOrderTypeIds.ChainPass,
                            ["chainNegate"] = TestResponseChainOrderTypeIds.ChainNegate,
                            ["chainActivateEffect"] = TestResponseChainOrderTypeIds.ChainActivateEffect
                        }
                    }
                },
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings
                {
                    ResponseChainPassActionId = "UiPass",
                    ResponseChainNegateActionId = "UiNegate",
                    ResponseChainActivateActionId = "UiActivate"
                }
            };
            var admissionResults = new OrderAdmissionResultBuffer(4, 4);
            var chainOrders = new OrderQueue(capacity: 1, admissionResults);
            var seed = new Order { Actor = actor, OrderTypeId = TestResponseChainOrderTypeIds.ChainPass };
            That(chainOrders.TryEnqueue(in seed), Is.True);
            var system = new ResponseChainHumanOrderSourceSystem(globals, ui, chainOrders);

            PressButton(handler, backend, "<Keyboard>/f");
            system.Update(0f);

            That(chainOrders.Count, Is.EqualTo(1));
            That(system.LastSubmissionResult, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
            That(system.LastSubmittedOrderId, Is.GreaterThan(0));
            That(admissionResults.TryGet(system.LastSubmittedOrderId, OrderAdmissionStage.GlobalIntake, out var outcome), Is.True);
            That(outcome.Result, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
        }

        [Test]
        public void ResponseChainAiOrderSourceSystem_QueueFullDoesNotMarkRootSubmitted()
        {
            using var world = World.Create();
            Entity actor = world.Create();
            var ui = new ResponseChainUiState();
            var request = default(OrderRequest);
            request.RequestId = 77;
            request.PlayerId = 2;
            request.Actor = actor;
            request.Target = actor;
            request.TargetContext = Entity.Null;
            ui.ApplyRequest(request);
            var admissionResults = new OrderAdmissionResultBuffer(4, 4);
            var chainOrders = new OrderQueue(capacity: 1, admissionResults);
            var seed = new Order { Actor = actor, OrderTypeId = TestResponseChainOrderTypeIds.ChainPass };
            That(chainOrders.TryEnqueue(in seed), Is.True);
            var system = new ResponseChainAiOrderSourceSystem(
                ui,
                chainOrders,
                TestResponseChainOrderTypeIds.ChainPass);

            system.Update(0f);
            That(system.LastSubmissionResult, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
            That(chainOrders.TryDequeue(out _), Is.True);

            system.Update(0f);

            That(chainOrders.TryDequeue(out var retry), Is.True);
            That(retry.OrderTypeId, Is.EqualTo(TestResponseChainOrderTypeIds.ChainPass));
            That(retry.PlayerId, Is.EqualTo(2));
            That(system.LastSubmissionResult, Is.EqualTo(OrderSubmitResult.Queued));

            system.Update(0f);
            That(chainOrders.TryDequeue(out _), Is.False);
        }

        [Test]
        public void ResponseChainHumanOrderSourceSystem_MissingOrderTypes_IsRejected()
        {
            using var world = World.Create();
            var (_, handler) = BuildResponseChainHandler();
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.InputHandler.Name] = handler,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings
                {
                    ResponseChainPassActionId = "UiPass",
                    ResponseChainNegateActionId = "UiNegate",
                    ResponseChainActivateActionId = "UiActivate"
                }
            };

            var ex = Throws<InvalidOperationException>(() =>
                new ResponseChainHumanOrderSourceSystem(
                    globals,
                    new ResponseChainUiState(),
                    new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64))));

            That(ex!.Message, Does.Contain("GameConfig"));
            That(ex.Message, Does.Contain("chainPass"));
        }

        [Test]
        public void ResponseChainUiSyncSystem_UsesInteractionBindingHints()
        {
            var overlay = new ScreenOverlayBuffer();
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.ScreenOverlayBuffer.Name] = overlay,
                [CoreServiceKeys.InteractionActionBindings.Name] = new InteractionActionBindings
                {
                    ResponseChainPassActionId = "UiPass",
                    ResponseChainNegateActionId = "UiNegate",
                    ResponseChainActivateActionId = "UiActivate"
                }
            };

            var ui = new ResponseChainUiState();
            var request = default(OrderRequest);
            request.RequestId = 77;
            request.PlayerId = 4;
            request.PromptTagId = 88;
            request.AddAllowed(TestResponseChainOrderTypeIds.ChainPass);
            ui.ApplyRequest(request);

            var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(capacity: OrderTerminalResultBuffer.DefaultCapacity));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = TestResponseChainOrderTypeIds.ChainPass,
                Label = "Pass"
            });

            var system = new ResponseChainUiSyncSystem(globals, ui, orderTypes);
            system.Update(0f);

            string[] lines = GetOverlayStrings(overlay);
            That(lines, Has.Some.EqualTo("Pass=UiPass  Negate=UiNegate  Activate=UiActivate"));
            That(lines, Has.Some.EqualTo("- Pass (1)"));
        }

        private static string[] GetOverlayStrings(ScreenOverlayBuffer overlay)
        {
            var items = overlay.GetSpan();
            var lines = new List<string>(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                ScreenOverlayItem item = items[i];
                if (item.Kind == ScreenOverlayItemKind.Text)
                {
                    lines.Add(overlay.GetString(item.StringId) ?? string.Empty);
                }
            }

            return lines.ToArray();
        }

        private static void FinalizeEffectTemplates(EffectTemplateRegistry templates)
        {
            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            GasTestEffectExecutionPlanFinalizer.FinalizeAll(
                templates,
                new PresetTypeRegistry(),
                builtinHandlers,
                new GraphProgramRegistry(),
                "Test/ResponseChainPresenterPipelineTests.json");
        }

        private static (TestInputBackend backend, PlayerInputHandler handler) BuildResponseChainHandler()
        {
            var backend = new TestInputBackend();
            var config = new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "UiPass", Type = InputActionType.Button },
                    new() { Id = "UiNegate", Type = InputActionType.Button },
                    new() { Id = "UiActivate", Type = InputActionType.Button }
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = "Gameplay",
                        Priority = 1,
                        Bindings = new List<InputBindingDef>
                        {
                            new() { ActionId = "UiPass", Path = "<Keyboard>/f", Processors = new() },
                            new() { ActionId = "UiNegate", Path = "<Keyboard>/g", Processors = new() },
                            new() { ActionId = "UiActivate", Path = "<Keyboard>/h", Processors = new() }
                        }
                    }
                }
            };

            var handler = new PlayerInputHandler(backend, config);
            handler.PushContext("Gameplay");
            handler.Update();
            return (backend, handler);
        }

        private static void RegisterAuthoredCueMarker(MeshAssetRegistry meshes, PresenterDefinitionRegistry presenters)
        {
            int meshId = meshes.Register(
                WellKnownMeshKeys.CueMarker,
                MeshAssetDescriptor.Primitive(0, PrimitiveMeshKind.Cube));
            presenters.Register(WellKnownMeshKeys.CueMarker, new PresenterDefinition
            {
                DefaultLifetime = 0.35f,
                PositionOffset = new Vector3(0f, 0.2f, 0f),
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 0,
                        Kind = BehaviorKind.AssetBinding,
                        ActiveByDefault = true,
                        AssetBinding = new AssetBindingConfig
                        {
                            AssetKind = AssetKind.Mesh,
                            AssetId = meshId,
                            RenderPath = VisualRenderPath.StaticMesh,
                            Mobility = VisualMobility.Movable,
                            LocalScale = new Vector3(0.2f, 0.2f, 0.2f),
                        },
                    },
                ],
            });
        }

        private static void PressButton(PlayerInputHandler handler, TestInputBackend backend, string path)
        {
            backend.Buttons[path] = true;
            handler.Update();
        }

        private static void ReleaseButton(PlayerInputHandler handler, TestInputBackend backend, string path)
        {
            backend.Buttons[path] = false;
            handler.Update();
        }

        private sealed class TestInputBackend : IInputBackend
        {
            public Dictionary<string, bool> Buttons { get; } = new();
            public Vector2 MousePosition { get; set; }

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => Buttons.TryGetValue(devicePath, out bool down) && down;
            public Vector2 GetMousePosition() => MousePosition;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}
