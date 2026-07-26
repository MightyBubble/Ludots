using System;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class ResponseWindowRobustnessTests
    {
        [Test]
        public void ProposalProcessing_ResetSlice_DoesNotDoubleApplyInstantEffects()
        {
            var world = World.Create();
            try
            {
                int attrHealth = EnsureAttribute("ResponseWindowRobustness.Health");
                int tplInstant = 1001;

                var templates = new EffectTemplateRegistry();
                var mods = default(EffectModifiers);
                mods.Add(attrId: attrHealth, ModifierOp.Add, -10f);
                templates.Register(tplInstant, new EffectTemplateData
                {
                    TagId = 1,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = false,
                    Modifiers = mods
                });

                var budget = new GasBudget();
                var queue = new EffectRequestQueue();

                var target = world.Create(new AttributeBuffer(), new DirtyFlags());
                ref var attributes = ref world.Get<AttributeBuffer>(target);
                attributes.SetBase(attrHealth, 100f);
                attributes.SetCurrent(attrHealth, 100f);

                queue.Publish(new EffectRequest
                {
                    RootId = 1,
                    Source = default,
                    Target = target,
                    TargetContext = default,
                    TemplateId = tplInstant
                });

                var sys = new EffectProposalProcessingSystem(
                    world,
                    queue,
                    GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                    new Ludots.Core.Engine.DiscreteClock(),
                    budget,
                    templates,
                    inputRequests: null,
                    chainOrders: null,
                    responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                    tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()))
                {
                    MaxWorkUnitsPerSlice = 2
                };

                bool completed = sys.UpdateSlice(dt: 1f, timeBudgetMs: int.MaxValue);
                That(completed, Is.False);

                float hpAfterFirstSlice = world.Get<AttributeBuffer>(target).GetCurrent(attrHealth);
                That(hpAfterFirstSlice, Is.EqualTo(90f));
                That(queue.Count, Is.EqualTo(1));

                sys.ResetSlice();
                sys.MaxWorkUnitsPerSlice = int.MaxValue;

                while (!sys.UpdateSlice(dt: 1f, timeBudgetMs: int.MaxValue)) { }

                That(queue.Count, Is.EqualTo(0));
                That(world.Get<AttributeBuffer>(target).GetCurrent(attrHealth), Is.EqualTo(hpAfterFirstSlice));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void ProposalProcessing_ChainDepthOverflow_ThrowsBeforeDropping()
        {
            var world = World.Create();
            try
            {
                int tplRoot = 2000;
                int rootTag = 100;

                var templates = new EffectTemplateRegistry();
                templates.Register(tplRoot, new EffectTemplateData
                {
                    TagId = rootTag,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = true,
                    Modifiers = default
                });

                int chainResponses = ResponseChainListener.CAPACITY;
                for (int i = 0; i < chainResponses; i++)
                {
                    int tplId = 3000 + i;
                    templates.Register(tplId, new EffectTemplateData
                    {
                        TagId = 200 + i,
                        LifetimeKind = EffectLifetimeKind.Instant,
                        ClockId = GasClockId.Step,
                        DurationTicks = 0,
                        PeriodTicks = 0,
                        ExpireCondition = default,
                        ParticipatesInResponse = false,
                        Modifiers = default
                    });
                }

                var listenerEntity = world.Create();
                unsafe
                {
                    var listener = new ResponseChainListener();
                    for (int i = 0; i < chainResponses; i++)
                    {
                        listener.Add(rootTag, ResponseType.Chain, priority: 10, effectTemplateId: 3000 + i);
                    }
                    world.Add(listenerEntity, listener);
                }

                var budget = new GasBudget();
                var queue = new EffectRequestQueue();
                var target = world.Create();

                queue.Publish(new EffectRequest
                {
                    RootId = 1,
                    Source = default,
                    Target = target,
                    TargetContext = default,
                    TemplateId = tplRoot
                });

                var sys = new EffectProposalProcessingSystem(
                    world,
                    queue,
                    GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                    new Ludots.Core.Engine.DiscreteClock(),
                    budget,
                    templates,
                    inputRequests: null,
                    chainOrders: null,
                    responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                    tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()))
                {
                    MaxWorkUnitsPerSlice = int.MaxValue
                };

                var error = Throws<InvalidOperationException>(() =>
                {
                    while (!sys.UpdateSlice(dt: 1f, timeBudgetMs: int.MaxValue)) { }
                });

                That(error!.Message, Does.StartWith(EffectProposalProcessingSystem.WindowDepthExceededError));
                That(budget.ResponseDepthDropped, Is.EqualTo(1));
                That(queue.Count, Is.EqualTo(1));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void ProposalProcessing_ResponseQueueOverflow_ThrowsBeforeDropping()
        {
            var world = World.Create();
            try
            {
                const int tplRoot = 2100;
                const int rootTag = 110;

                var templates = new EffectTemplateRegistry();
                templates.Register(tplRoot, new EffectTemplateData
                {
                    TagId = rootTag,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = true,
                    Modifiers = default
                });

                unsafe
                {
                    for (int i = 0; i <= GasConstants.MAX_RESPONSES_PER_WINDOW; i++)
                    {
                        var listener = new ResponseChainListener();
                        That(listener.Add(rootTag, ResponseType.Modify, priority: i, modifyValue: 1f), Is.True);
                        world.Add(world.Create(), listener);
                    }
                }

                var budget = new GasBudget();
                var queue = new EffectRequestQueue();
                var target = world.Create();
                queue.Publish(new EffectRequest
                {
                    RootId = 2,
                    Source = default,
                    Target = target,
                    TargetContext = default,
                    TemplateId = tplRoot
                });

                var sys = new EffectProposalProcessingSystem(
                    world,
                    queue,
                    GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                    new Ludots.Core.Engine.DiscreteClock(),
                    budget,
                    templates,
                    inputRequests: null,
                    chainOrders: null,
                    responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                    tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

                var error = Throws<InvalidOperationException>(() =>
                {
                    while (!sys.UpdateSlice(dt: 1f, timeBudgetMs: int.MaxValue)) { }
                });

                That(error!.Message, Does.StartWith(EffectProposalProcessingSystem.ResponseQueueOverflowError));
                That(budget.ResponseQueueOverflowDropped, Is.EqualTo(1));
                That(queue.Count, Is.EqualTo(1));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void ProposalProcessing_PromptInputWithoutInputQueue_ThrowsBeforeWaiting()
        {
            var world = World.Create();
            try
            {
                var sys = CreatePromptInputSystem(world, inputRequests: null, orderRequests: null, out var queue);

                var error = Throws<InvalidOperationException>(() =>
                {
                    while (!sys.UpdateSlice(dt: 1f, timeBudgetMs: int.MaxValue)) { }
                });

                That(error!.Message, Does.StartWith(EffectProposalProcessingSystem.InputRequestQueueMissingError));
                That(queue.Count, Is.EqualTo(1));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void ProposalProcessing_PromptInputRequestQueueFull_ThrowsBeforeWaiting()
        {
            var world = World.Create();
            try
            {
                var inputRequests = new InputRequestQueue(capacity: 16);
                for (int i = 0; i < inputRequests.Capacity; i++)
                {
                    var request = new InputRequest { RequestId = 100 + i, RequestTagId = 900 };
                    That(inputRequests.TryEnqueue(in request), Is.True);
                }

                var sys = CreatePromptInputSystem(world, inputRequests, orderRequests: null, out var queue);

                var error = Throws<InvalidOperationException>(() =>
                {
                    while (!sys.UpdateSlice(dt: 1f, timeBudgetMs: int.MaxValue)) { }
                });

                That(error!.Message, Does.StartWith(EffectProposalProcessingSystem.InputRequestQueueFullError));
                That(inputRequests.Count, Is.EqualTo(inputRequests.Capacity));
                That(queue.Count, Is.EqualTo(1));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void ProposalProcessing_OrderRequestQueueFull_ThrowsBeforePublishingPrompt()
        {
            var world = World.Create();
            try
            {
                var inputRequests = new InputRequestQueue(capacity: 16);
                var orderRequests = new OrderRequestQueue(capacity: 16);
                for (int i = 0; i < orderRequests.Capacity; i++)
                {
                    var request = new OrderRequest { RequestId = 200 + i, PromptTagId = 901 };
                    That(orderRequests.TryEnqueue(in request), Is.True);
                }

                var sys = CreatePromptInputSystem(world, inputRequests, orderRequests, out var queue);

                var error = Throws<InvalidOperationException>(() =>
                {
                    while (!sys.UpdateSlice(dt: 1f, timeBudgetMs: int.MaxValue)) { }
                });

                That(error!.Message, Does.StartWith(EffectProposalProcessingSystem.OrderRequestQueueFullError));
                That(inputRequests.Count, Is.EqualTo(0), "OrderRequest queue full must not leave an orphan visible prompt.");
                That(orderRequests.Count, Is.EqualTo(orderRequests.Capacity));
                That(queue.Count, Is.EqualTo(1));
                That(sys.DebugWindowPhase, Is.EqualTo((byte)2), "WaitInput phase must remain retryable without marking the prompt sent.");
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void ProposalProcessing_ChainActivateResponseOverflow_PublishesTypedFailureAndDoesNotDropOrderSilently()
        {
            var world = World.Create();
            try
            {
                const int tplRoot = 2300;
                const int tplFollow = 2301;
                const int rootTag = 130;
                const int followTag = 131;

                var templates = new EffectTemplateRegistry();
                templates.Register(tplRoot, new EffectTemplateData
                {
                    TagId = rootTag,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    ParticipatesInResponse = true,
                    Modifiers = default
                });
                templates.Register(tplFollow, new EffectTemplateData
                {
                    TagId = followTag,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    ParticipatesInResponse = true,
                    Modifiers = default
                });

                unsafe
                {
                    for (int i = 0; i <= GasConstants.MAX_RESPONSES_PER_WINDOW; i++)
                    {
                        var listener = new ResponseChainListener();
                        That(listener.Add(followTag, ResponseType.Modify, priority: i, modifyValue: 1f), Is.True);
                        world.Add(world.Create(), listener);
                    }
                }

                var admissionResults = new OrderAdmissionResultBuffer(64, 64);
                admissionResults.BeginLogicStep();
                var chainOrders = new OrderQueue(64, admissionResults);
                var budget = new GasBudget();
                var queue = new EffectRequestQueue();
                var inputRequests = new InputRequestQueue();
                var orderRequests = new OrderRequestQueue();
                var target = world.Create(new PlayerOwner { PlayerId = 1 });
                var rootListener = default(ResponseChainListener);
                That(rootListener.Add(rootTag, ResponseType.PromptInput, priority: 100, effectTemplateId: tplRoot), Is.True);
                world.Add(target, rootListener);

                queue.Publish(new EffectRequest
                {
                    RootId = 11,
                    Source = target,
                    Target = target,
                    TemplateId = tplRoot
                });

                var sys = new EffectProposalProcessingSystem(
                    world,
                    queue,
                    GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                    new Ludots.Core.Engine.DiscreteClock(),
                    budget,
                    templates,
                    inputRequests,
                    chainOrders,
                    orderRequests: orderRequests,
                    responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                    tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

                while (!sys.UpdateSlice(dt: 1f, timeBudgetMs: int.MaxValue)) { }

                var args = default(OrderArgs);
                args.I0 = tplFollow;
                var activate = new Order
                {
                    OrderTypeId = TestResponseChainOrderTypeIds.ChainActivateEffect,
                    PlayerId = 1,
                    Actor = target,
                    Target = target,
                    Args = args
                };
                That(chainOrders.SubmitAssigned(ref activate), Is.EqualTo(OrderSubmitResult.Queued));
                That(chainOrders.Count, Is.EqualTo(1));

                while (!sys.UpdateSlice(dt: 1f, timeBudgetMs: int.MaxValue)) { }

                That(chainOrders.Count, Is.EqualTo(0));
                That(admissionResults.TryGet(activate.OrderId, OrderAdmissionStage.EntityIntake, out var intake), Is.True);
                That(intake.Result, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
                That(budget.ResponseQueueOverflowDropped, Is.EqualTo(1));
                admissionResults.EndEntityIntake();
                admissionResults.EndLogicStep();
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void ProposalProcessing_ChainPassWithSpatialPayload_ReleasesPayloadOnSuccess()
        {
            var world = World.Create();
            try
            {
                const int tplRoot = 2400;
                const int rootTag = 140;

                var templates = new EffectTemplateRegistry();
                templates.Register(tplRoot, new EffectTemplateData
                {
                    TagId = rootTag,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    ParticipatesInResponse = true,
                    Modifiers = default
                });

                var admissionResults = new OrderAdmissionResultBuffer(64, 64);
                admissionResults.BeginLogicStep();
                var chainOrders = new OrderQueue(64, admissionResults);
                var queue = new EffectRequestQueue();
                var inputRequests = new InputRequestQueue();
                var orderRequests = new OrderRequestQueue();
                var target = world.Create(
                    new PlayerOwner { PlayerId = 1 },
                    new OrderSpatialPayloadBuffer());
                var rootListener = default(ResponseChainListener);
                That(rootListener.Add(rootTag, ResponseType.PromptInput, priority: 100, effectTemplateId: tplRoot), Is.True);
                world.Add(target, rootListener);

                queue.Publish(new EffectRequest
                {
                    RootId = 12,
                    Source = target,
                    Target = target,
                    TemplateId = tplRoot
                });

                var sys = new EffectProposalProcessingSystem(
                    world,
                    queue,
                    GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                    new Ludots.Core.Engine.DiscreteClock(),
                    new GasBudget(),
                    templates,
                    inputRequests,
                    chainOrders,
                    orderRequests: orderRequests,
                    responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                    tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

                while (!sys.UpdateSlice(dt: 1f, timeBudgetMs: int.MaxValue)) { }

                int[] xs = { 0, 100, 200 };
                int[] ys = { 0, 0, 0 };
                var pass = new Order
                {
                    OrderTypeId = TestResponseChainOrderTypeIds.ChainPass,
                    PlayerId = 1,
                    Actor = target,
                    Target = target
                };
                OrderSpatialPayloadOps.SetPath(world, target, ref pass, xs, ys, 3);
                OrderSpatialPayloadHandle handle = pass.Args.Spatial.Payload;
                That(handle.IsValid, Is.True);
                That(chainOrders.SubmitAssigned(ref pass), Is.EqualTo(OrderSubmitResult.Queued));

                while (!sys.UpdateSlice(dt: 1f, timeBudgetMs: int.MaxValue)) { }

                That(admissionResults.TryGet(pass.OrderId, OrderAdmissionStage.EntityIntake, out var intake), Is.True);
                That(intake.Result, Is.EqualTo(OrderSubmitResult.Activated));
                That(handle.IsValid, Is.True);
                Throws<InvalidOperationException>(() =>
                {
                    ref var payloads = ref world.Get<OrderSpatialPayloadBuffer>(target);
                    _ = payloads.GetPointCount(in handle);
                });
                admissionResults.EndEntityIntake();
                admissionResults.EndLogicStep();
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void ProposalProcessing_ChainNegateWithSpatialPayload_ReleasesPayloadOnSuccess()
        {
            var world = World.Create();
            try
            {
                const int tplRoot = 2401;
                const int rootTag = 141;

                var templates = new EffectTemplateRegistry();
                templates.Register(tplRoot, new EffectTemplateData
                {
                    TagId = rootTag,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    ParticipatesInResponse = true,
                    Modifiers = default
                });

                var admissionResults = new OrderAdmissionResultBuffer(64, 64);
                admissionResults.BeginLogicStep();
                var chainOrders = new OrderQueue(64, admissionResults);
                var queue = new EffectRequestQueue();
                var inputRequests = new InputRequestQueue();
                var orderRequests = new OrderRequestQueue();
                var target = world.Create(
                    new PlayerOwner { PlayerId = 1 },
                    new OrderSpatialPayloadBuffer());
                var rootListener = default(ResponseChainListener);
                That(rootListener.Add(rootTag, ResponseType.PromptInput, priority: 100, effectTemplateId: tplRoot), Is.True);
                world.Add(target, rootListener);

                queue.Publish(new EffectRequest
                {
                    RootId = 13,
                    Source = target,
                    Target = target,
                    TemplateId = tplRoot
                });

                var sys = new EffectProposalProcessingSystem(
                    world,
                    queue,
                    GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                    new Ludots.Core.Engine.DiscreteClock(),
                    new GasBudget(),
                    templates,
                    inputRequests,
                    chainOrders,
                    orderRequests: orderRequests,
                    responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                    tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

                while (!sys.UpdateSlice(dt: 1f, timeBudgetMs: int.MaxValue)) { }

                int[] xs = { 0, 100, 200 };
                int[] ys = { 0, 0, 0 };
                var negate = new Order
                {
                    OrderTypeId = TestResponseChainOrderTypeIds.ChainNegate,
                    PlayerId = 1,
                    Actor = target,
                    Target = target
                };
                OrderSpatialPayloadOps.SetPath(world, target, ref negate, xs, ys, 3);
                OrderSpatialPayloadHandle handle = negate.Args.Spatial.Payload;
                That(handle.IsValid, Is.True);
                That(chainOrders.SubmitAssigned(ref negate), Is.EqualTo(OrderSubmitResult.Queued));

                while (!sys.UpdateSlice(dt: 1f, timeBudgetMs: int.MaxValue)) { }

                That(admissionResults.TryGet(negate.OrderId, OrderAdmissionStage.EntityIntake, out var intake), Is.True);
                That(intake.Result, Is.EqualTo(OrderSubmitResult.Activated));
                Throws<InvalidOperationException>(() =>
                {
                    ref var payloads = ref world.Get<OrderSpatialPayloadBuffer>(target);
                    _ = payloads.GetPointCount(in handle);
                });
                admissionResults.EndEntityIntake();
                admissionResults.EndLogicStep();
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void ProposalProcessing_ReleaseThrowAfterAdmissionCommit_DoesNotDoubleCommitReservation()
        {
            var world = World.Create();
            try
            {
                const int tplRoot = 2402;
                const int rootTag = 142;

                var templates = new EffectTemplateRegistry();
                templates.Register(tplRoot, new EffectTemplateData
                {
                    TagId = rootTag,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    ParticipatesInResponse = true,
                    Modifiers = default
                });

                var admissionResults = new OrderAdmissionResultBuffer(64, 64);
                admissionResults.BeginLogicStep();
                var chainOrders = new OrderQueue(64, admissionResults);
                var queue = new EffectRequestQueue();
                var inputRequests = new InputRequestQueue();
                var orderRequests = new OrderRequestQueue();
                var target = world.Create(
                    new PlayerOwner { PlayerId = 1 },
                    new OrderSpatialPayloadBuffer());
                var rootListener = default(ResponseChainListener);
                That(rootListener.Add(rootTag, ResponseType.PromptInput, priority: 100, effectTemplateId: tplRoot), Is.True);
                world.Add(target, rootListener);

                queue.Publish(new EffectRequest
                {
                    RootId = 14,
                    Source = target,
                    Target = target,
                    TemplateId = tplRoot
                });

                var sys = new EffectProposalProcessingSystem(
                    world,
                    queue,
                    GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                    new Ludots.Core.Engine.DiscreteClock(),
                    new GasBudget(),
                    templates,
                    inputRequests,
                    chainOrders,
                    orderRequests: orderRequests,
                    responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                    tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

                while (!sys.UpdateSlice(dt: 1f, timeBudgetMs: int.MaxValue)) { }

                int[] xs = { 0, 100, 200 };
                int[] ys = { 0, 0, 0 };
                var pass = new Order
                {
                    OrderTypeId = TestResponseChainOrderTypeIds.ChainPass,
                    PlayerId = 1,
                    Actor = target,
                    Target = target
                };
                OrderSpatialPayloadOps.SetPath(world, target, ref pass, xs, ys, 3);
                That(chainOrders.SubmitAssigned(ref pass), Is.EqualTo(OrderSubmitResult.Queued));

                // Valid payload handle remains on the order, but the buffer is gone → Release throws after Commit.
                world.Remove<OrderSpatialPayloadBuffer>(target);

                Throws<InvalidOperationException>(() =>
                {
                    while (!sys.UpdateSlice(dt: 1f, timeBudgetMs: int.MaxValue)) { }
                });

                That(admissionResults.ReservedCount, Is.EqualTo(0));
                That(CountEntityIntakeOutcomes(admissionResults, pass.OrderId), Is.EqualTo(1));
                That(admissionResults.TryGet(pass.OrderId, OrderAdmissionStage.EntityIntake, out var intake), Is.True);
                That(intake.Result, Is.EqualTo(OrderSubmitResult.Activated));
                DoesNotThrow(() => admissionResults.EndEntityIntake());
                DoesNotThrow(() => admissionResults.EndLogicStep());
            }
            finally
            {
                world.Dispose();
            }
        }

        private static int CountEntityIntakeOutcomes(OrderAdmissionResultBuffer results, int orderId)
        {
            int matched = 0;
            for (int i = 0; i < results.Count; i++)
            {
                ref readonly var outcome = ref results[i];
                if (outcome.OrderId == orderId && outcome.Stage == OrderAdmissionStage.EntityIntake)
                {
                    matched++;
                }
            }

            return matched;
        }

        private static int EnsureAttribute(string name)
        {
            int id = AttributeRegistry.GetId(name);
            return id != AttributeRegistry.InvalidId ? id : AttributeRegistry.Register(name);
        }

        private static EffectProposalProcessingSystem CreatePromptInputSystem(
            World world,
            InputRequestQueue inputRequests,
            OrderRequestQueue orderRequests,
            out EffectRequestQueue queue)
        {
            const int tplRoot = 2200;
            const int rootTag = 120;
            const int inputRequestTag = 920;

            var templates = new EffectTemplateRegistry();
            templates.Register(tplRoot, new EffectTemplateData
            {
                TagId = rootTag,
                LifetimeKind = EffectLifetimeKind.Instant,
                ClockId = GasClockId.Step,
                DurationTicks = 0,
                PeriodTicks = 0,
                ExpireCondition = default,
                ParticipatesInResponse = true,
                Modifiers = default
            });

            unsafe
            {
                var listener = new ResponseChainListener();
                That(listener.Add(rootTag, ResponseType.PromptInput, priority: 10, effectTemplateId: inputRequestTag), Is.True);
                world.Add(world.Create(), listener);
            }

            queue = new EffectRequestQueue();
            queue.Publish(new EffectRequest
            {
                RootId = 3,
                Source = world.Create(),
                Target = world.Create(),
                TargetContext = default,
                TemplateId = tplRoot
            });

            return new EffectProposalProcessingSystem(
                world,
                queue,
                GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                new Ludots.Core.Engine.DiscreteClock(),
                new GasBudget(),
                templates,
                inputRequests,
                chainOrders: null,
                orderRequests: orderRequests,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));
        }
    }
}
