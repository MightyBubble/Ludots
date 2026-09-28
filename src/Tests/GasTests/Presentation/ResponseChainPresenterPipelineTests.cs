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
                    CategoryId = tag,
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
                var promptState = new ResponseChainPromptState();
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
                    promptState,
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
                That(chainOrders.SubmitAssigned(ref activate), Is.EqualTo(OrderSubmitResult.Queued));
                That(chainOrders.SubmitAssigned(ref pass1), Is.EqualTo(OrderSubmitResult.Queued));
 
                processing.Update(0f);

                That(admissionResults.TryGet(activate.OrderId, OrderAdmissionStage.EntityIntake, out var activateIntake), Is.True);
                That(activateIntake.Result, Is.EqualTo(OrderSubmitResult.Activated));
                That(admissionResults.TryGet(pass1.OrderId, OrderAdmissionStage.EntityIntake, out var pass1Intake), Is.True);
                That(pass1Intake.Result, Is.EqualTo(OrderSubmitResult.Activated));
                That(promptState.IsOpen, Is.False, "the activated link re-prompted the same player, whose single pass resolved the chain");
                That(chainOrders.Count, Is.Zero);
 
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
                CategoryId = tag,
                LifetimeKind = EffectLifetimeKind.Instant,
                ClockId = GasClockId.Step,
                ParticipatesInResponse = true,
                Modifiers = default
            });
            FinalizeEffectTemplates(templates);

            var requests = new EffectRequestQueue();
            var admissionResults = new OrderAdmissionResultBuffer(8, 8);
            var chainOrders = new OrderQueue(8, admissionResults);
            var promptState = new ResponseChainPromptState();
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
                promptState,
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
            for (int frame = 0; frame < 10_000; frame++)
            {
                admissionResults.BeginLogicStep();
                if (previousPass1Id > 0)
                {
                    That(admissionResults.TryGet(previousPass1Id, OrderAdmissionStage.GlobalIntake, out _), Is.False);
                }

                requests.Publish(new EffectRequest
                {
                    Source = actor,
                    Target = actor,
                    TemplateId = tplRoot
                });
                processing.Update(0f);
                That(promptState.IsOpen, Is.True);
                That(orderRequests.TryDequeue(out _), Is.True);

                var pass1 = new Order { OrderTypeId = TestResponseChainOrderTypeIds.ChainPass, PlayerId = 1, Actor = actor, Target = actor };
                That(chainOrders.SubmitAssigned(ref pass1), Is.EqualTo(OrderSubmitResult.Queued));
                processing.Update(0f);

                That(admissionResults.TryGet(pass1.OrderId, OrderAdmissionStage.EntityIntake, out var pass1Intake), Is.True);
                That(pass1Intake.Result, Is.EqualTo(OrderSubmitResult.Activated));
                That(chainOrders.Count, Is.Zero);

                previousPass1Id = pass1.OrderId;
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
    }
}
