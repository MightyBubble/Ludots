using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// RFC-0065 CTX-6 (§6.1 M2, DEC-13 post-order sessions): an ability declaring
    /// <c>interactionContextProfile</c> pushes its context frame while its exec instance runs and
    /// the frame is reclaimed on every teardown path (finish, interrupt, caster death). While the
    /// frame is on top, context-bound cast commits land in the ability's collection key and the
    /// default command source stays untouched; abilities without a profile never touch the stack.
    /// All profile/collection/tag names are test data, never Core concepts.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class AbilityExecInteractionContextTests
    {
        private const int AbilityWithContextId = 9001;
        private const int AbilityWithoutContextId = 9002;
        private const int AbilityWithDanglingProfileId = 9003;
        private const int AbilityWithTagSignalId = 9004;
        private const int AbilityWithoutExplicitEndId = 9005;
        private const int CastOrderTypeId = 100;
        private const int WaitEventTagId = 5001;
        private const string ContextProfileName = "ctx.ability.test.confirm_targets";
        private const string AbilityTargetsCollectionKey = "collection.ability.test.targets";
        private const string StunTagName = "test.state.stunned";
        private const string TerminalCapacityTagName = "test.exec.terminal_capacity_side_effect";

        [SetUp]
        public void SetUp()
        {
            TagRegistry.Clear();
        }

        [Test]
        public void ExecStart_PushesFrame_CastCommitsSwitchKeys_AndFinishRestoresDefault()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Entity p1Rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity m01 = world.Create();
            Entity m02 = world.Create();
            Entity m05 = world.Create();
            Entity m06 = world.Create();
            harness.Ownership.EnsureOwnership(p1Rep, m01);
            harness.Ownership.EnsureOwnership(p1Rep, m02);
            harness.Ownership.EnsureOwnership(p1Rep, m05);
            harness.Ownership.EnsureOwnership(p1Rep, m06);

            harness.Writer.CommitCast(p1Rep, stackalloc Entity[] { m01, m02 }, EntityCollectionSourceKind.UiAcquisition);

            Entity actor = harness.CreateCastingActor(AbilityWithContextId);
            harness.ExecSystem.Update(0f);
            Assert.That(world.Has<AbilityExecInstance>(actor), Is.True, "Exec must be gate-waiting.");

            harness.ContextSystem.Update(0f);
            Assert.That(harness.Stack.Count, Is.EqualTo(2), "Exec start must push the ability's context frame.");
            Assert.That(harness.Stack.TryPeek(out InteractionContextFrame frame), Is.True);
            Assert.That(frame.ActiveCollectionKeyId, Is.EqualTo(harness.AbilityTargetsKeyId), "Top frame must expose the ability collection key.");
            Assert.That(frame.ContextEntity, Is.EqualTo(actor), "Frame ownership is the exec carrier entity.");

            // M2: casts during the ability frame land in the ability key; command.source is untouched.
            harness.Writer.CommitCast(p1Rep, stackalloc Entity[] { m05, m06 }, EntityCollectionSourceKind.UiAcquisition);
            Span<Entity> rows = stackalloc Entity[8];
            Assert.That(harness.Store.TryGet(p1Rep, harness.AbilityTargetsKeyId, out EntityCollectionHandle abilityHandle), Is.True);
            int count = harness.Store.CopyEntities(abilityHandle, 0, rows);
            Assert.That(rows[..count].ToArray(), Is.EqualTo(new[] { m05, m06 }));
            Assert.That(harness.Store.TryGet(p1Rep, harness.CommandSourceKeyId, out EntityCollectionHandle commandHandle), Is.True);
            count = harness.Store.CopyEntities(commandHandle, 0, rows);
            Assert.That(rows[..count].ToArray(), Is.EqualTo(new[] { m01, m02 }), "command.source must not change while the ability frame is active.");

            // Repeated updates while the exec waits must not duplicate the frame.
            harness.ContextSystem.Update(0f);
            Assert.That(harness.Stack.Count, Is.EqualTo(2));

            // Complete the exec: the event gate resolves on one tick, End fires on the next.
            harness.EventBus.Publish(new GameplayEvent { TagId = WaitEventTagId, Source = actor });
            harness.EventBus.Update();
            harness.ExecSystem.Update(0f);
            harness.ExecSystem.Update(0f);
            Assert.That(world.Has<AbilityExecInstance>(actor), Is.False, "Exec must be torn down after End.");

            harness.ContextSystem.Update(0f);
            Assert.That(harness.Stack.Count, Is.EqualTo(1), "Frame must be reclaimed when the exec ends.");
            Assert.That(harness.Stack.TryPeek(out frame), Is.True);
            Assert.That(frame.ActiveCollectionKeyId, Is.EqualTo(harness.CommandSourceKeyId), "Default frame is active again.");

            harness.Writer.CommitCast(p1Rep, stackalloc Entity[] { m02 }, EntityCollectionSourceKind.UiAcquisition);
            Assert.That(harness.Store.TryGet(p1Rep, harness.CommandSourceKeyId, out commandHandle), Is.True);
            count = harness.Store.CopyEntities(commandHandle, 0, rows);
            Assert.That(rows[..count].ToArray(), Is.EqualTo(new[] { m02 }), "Casts write command.source again after the frame is removed.");
        }

        [Test]
        public void ExecInterrupted_ByTag_ReclaimsFrame()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Entity actor = harness.CreateCastingActor(AbilityWithContextId);

            harness.ExecSystem.Update(0f);
            harness.ContextSystem.Update(0f);
            Assert.That(harness.Stack.Count, Is.EqualTo(2));

            // GAS arbitration interrupts the exec (DEC-13: input layer has no say); the tag is mod data.
            ref var tags = ref world.Get<GameplayTagContainer>(actor);
            tags.AddTag(TagRegistry.GetId(StunTagName));
            harness.ExecSystem.Update(0f);
            Assert.That(world.Has<AbilityExecInstance>(actor), Is.False, "Interrupt must tear the exec down.");
            Assert.That(harness.OrderTypes.TerminalResults.Count, Is.EqualTo(1));
            ref readonly var terminal = ref harness.OrderTypes.TerminalResults[0];
            Assert.That(terminal.OrderId, Is.EqualTo(7));
            Assert.That(terminal.State, Is.EqualTo(OrderTerminalState.Cancelled));
            Assert.That(terminal.FailureReason, Is.EqualTo(OrderFailureReason.Interrupted));

            harness.ContextSystem.Update(0f);
            Assert.That(harness.Stack.Count, Is.EqualTo(1), "Interrupted exec must reclaim its frame.");
        }

        [Test]
        public void ExecOrderReplaced_DiscardsOldExecWithoutFinalizingReplacement()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Entity actor = harness.CreateCastingActor(AbilityWithContextId);

            harness.ExecSystem.Update(0f);
            Assert.That(world.Get<AbilityExecInstance>(actor).OrderId, Is.EqualTo(7));

            var replacement = new Order
            {
                OrderId = 8,
                Actor = actor,
                OrderTypeId = CastOrderTypeId,
                SubmitMode = OrderSubmitMode.Immediate,
            };
            OrderSubmitResult submitResult = OrderSubmitter.Submit(
                world,
                actor,
                in replacement,
                harness.OrderTypes,
                orderRuleRegistry: null,
                currentStep: 1,
                stepRateHz: 30);

            Assert.That(submitResult, Is.EqualTo(OrderSubmitResult.Activated));
            Assert.That(harness.OrderTypes.TerminalResults.Count, Is.EqualTo(1));
            Assert.That(harness.OrderTypes.TerminalResults[0].OrderId, Is.EqualTo(7));
            Assert.That(harness.OrderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Cancelled));

            harness.ExecSystem.Update(0f);

            Assert.That(world.Has<AbilityExecInstance>(actor), Is.True, "The replacement cast may start during the same update rescan.");
            Assert.That(world.Get<AbilityExecInstance>(actor).OrderId, Is.EqualTo(8), "The cancelled execution must not keep running after its order is replaced.");
            Assert.That(world.Get<OrderBuffer>(actor).ActiveOrder.Order.OrderId, Is.EqualTo(8));
            Assert.That(harness.OrderTypes.TerminalResults.Count, Is.EqualTo(1), "Discarding the old execution must not finalize the replacement order.");
        }

        [Test]
        public void ExecFinish_WhenTerminalResultCapacityIsFull_LeavesOrderAndExecUntouchedForRetry()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, terminalResultCapacity: 1);
            Entity actor = harness.CreateCastingActor(AbilityWithContextId);

            var occupiedOrder = new Order { OrderId = 99, Actor = default, OrderTypeId = CastOrderTypeId };
            var occupiedBuffer = OrderBuffer.CreateEmpty();
            Entity occupiedActor = world.Create(
                occupiedBuffer,
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                new BlackboardSpatialBuffer());
            occupiedOrder.Actor = occupiedActor;
            ref var occupiedOrders = ref world.Get<OrderBuffer>(occupiedActor);
            occupiedOrders.SetActiveDirect(in occupiedOrder, priority: 100);
            Assert.That(OrderSubmitter.NotifyOrderComplete(world, occupiedActor, harness.OrderTypes), Is.True);

            harness.ExecSystem.Update(0f);
            harness.EventBus.Publish(new GameplayEvent { TagId = WaitEventTagId, Source = actor });
            harness.EventBus.Update();
            harness.ExecSystem.Update(0f);

            Assert.Throws<InvalidOperationException>(() => harness.ExecSystem.Update(0f));

            Assert.That(world.Has<AbilityExecInstance>(actor), Is.True, "A result-capacity fault must not tear down the execution before its terminal outcome is recorded.");
            ref readonly var exec = ref world.Get<AbilityExecInstance>(actor);
            Assert.That(exec.State, Is.EqualTo(AbilityExecRunState.Running));
            Assert.That(exec.NextItemIndex, Is.EqualTo(1));
            Assert.That(world.Get<OrderBuffer>(actor).ActiveOrder.Order.OrderId, Is.EqualTo(7));
            Assert.That(CountPresentationEvents(harness.PresentationEvents, GasPresentationEventKind.CastFinished), Is.Zero);

            harness.OrderTypes.TerminalResults.Clear();
            harness.ExecSystem.ResetSlice();
            harness.ExecSystem.Update(0f);

            Assert.That(world.Has<AbilityExecInstance>(actor), Is.False);
            Assert.That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
            Assert.That(harness.OrderTypes.TerminalResults.Count, Is.EqualTo(1));
            Assert.That(harness.OrderTypes.TerminalResults[0].OrderId, Is.EqualTo(7));
            Assert.That(CountPresentationEvents(harness.PresentationEvents, GasPresentationEventKind.CastFinished), Is.EqualTo(1));
        }

        [Test]
        public void ExecNaturalFinish_WhenTerminalResultCapacityIsFull_LeavesOrderAndExecUntouchedForRetry()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, terminalResultCapacity: 1);
            var spec = default(AbilityExecSpec);
            spec.ClockId = GasClockId.Step;
            spec.SetItem(0, ExecItemKind.EventGate, tick: 0, tagId: WaitEventTagId);
            harness.Definitions.Register(AbilityWithoutExplicitEndId, new AbilityDefinition { ExecSpec = spec });
            Entity actor = harness.CreateCastingActor(AbilityWithoutExplicitEndId);

            harness.ExecSystem.Update(0f);
            Assert.That(world.Get<AbilityExecInstance>(actor).State, Is.EqualTo(AbilityExecRunState.GateWaiting));

            harness.EventBus.Publish(new GameplayEvent { TagId = WaitEventTagId, Source = actor });
            harness.EventBus.Update();
            harness.ExecSystem.Update(0f);
            ref readonly var readyToFinish = ref world.Get<AbilityExecInstance>(actor);
            Assert.That(readyToFinish.State, Is.EqualTo(AbilityExecRunState.Running));
            Assert.That(readyToFinish.NextItemIndex, Is.EqualTo(1));

            var occupiedOrder = new Order { OrderId = 99, Actor = default, OrderTypeId = CastOrderTypeId };
            var occupiedBuffer = OrderBuffer.CreateEmpty();
            Entity occupiedActor = world.Create(
                occupiedBuffer,
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                new BlackboardSpatialBuffer());
            occupiedOrder.Actor = occupiedActor;
            ref var occupiedOrders = ref world.Get<OrderBuffer>(occupiedActor);
            occupiedOrders.SetActiveDirect(in occupiedOrder, priority: 100);
            Assert.That(OrderSubmitter.NotifyOrderComplete(world, occupiedActor, harness.OrderTypes), Is.True);

            Assert.Throws<InvalidOperationException>(() => harness.ExecSystem.Update(0f));

            Assert.That(world.Has<AbilityExecInstance>(actor), Is.True);
            ref readonly var blocked = ref world.Get<AbilityExecInstance>(actor);
            Assert.That(blocked.State, Is.EqualTo(AbilityExecRunState.Running));
            Assert.That(blocked.NextItemIndex, Is.EqualTo(1));
            Assert.That(world.Get<OrderBuffer>(actor).ActiveOrder.Order.OrderId, Is.EqualTo(7));
            Assert.That(CountPresentationEvents(harness.PresentationEvents, GasPresentationEventKind.CastFinished), Is.Zero);

            harness.OrderTypes.TerminalResults.Clear();
            harness.ExecSystem.ResetSlice();
            harness.ExecSystem.Update(0f);

            Assert.That(world.Has<AbilityExecInstance>(actor), Is.False);
            Assert.That(world.Get<OrderBuffer>(actor).HasActive, Is.False);
            Assert.That(harness.OrderTypes.TerminalResults.Count, Is.EqualTo(1));
            Assert.That(harness.OrderTypes.TerminalResults[0].OrderId, Is.EqualTo(7));
            Assert.That(CountPresentationEvents(harness.PresentationEvents, GasPresentationEventKind.CastFinished), Is.EqualTo(1));
        }

        [Test]
        public void ExecTimeline_WhenTerminalResultCapacityIsFull_DoesNotApplyDueSideEffects()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world, terminalResultCapacity: 1);
            int sideEffectTagId = TagRegistry.Register(TerminalCapacityTagName);
            var spec = default(AbilityExecSpec);
            spec.ClockId = GasClockId.Step;
            spec.SetItem(0, ExecItemKind.TagSignal, tick: 0, tagId: sideEffectTagId);
            spec.SetItem(1, ExecItemKind.End, tick: 0);
            harness.Definitions.Register(AbilityWithTagSignalId, new AbilityDefinition { ExecSpec = spec });
            Entity actor = harness.CreateCastingActor(AbilityWithTagSignalId);

            var occupiedOrder = new Order { OrderId = 99, Actor = default, OrderTypeId = CastOrderTypeId };
            var occupiedBuffer = OrderBuffer.CreateEmpty();
            Entity occupiedActor = world.Create(
                occupiedBuffer,
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                new BlackboardSpatialBuffer());
            occupiedOrder.Actor = occupiedActor;
            ref var occupiedOrders = ref world.Get<OrderBuffer>(occupiedActor);
            occupiedOrders.SetActiveDirect(in occupiedOrder, priority: 100);
            Assert.That(OrderSubmitter.NotifyOrderComplete(world, occupiedActor, harness.OrderTypes), Is.True);

            Assert.Throws<InvalidOperationException>(() => harness.ExecSystem.Update(0f));

            Assert.That(world.Get<GameplayTagContainer>(actor).HasTag(sideEffectTagId), Is.False);
            Assert.That(world.Has<AbilityExecInstance>(actor), Is.True);
            ref readonly var exec = ref world.Get<AbilityExecInstance>(actor);
            Assert.That(exec.State, Is.EqualTo(AbilityExecRunState.Running));
            Assert.That(exec.NextItemIndex, Is.Zero);
            Assert.That(world.Get<OrderBuffer>(actor).ActiveOrder.Order.OrderId, Is.EqualTo(7));
            Assert.That(CountPresentationEvents(harness.PresentationEvents, GasPresentationEventKind.CastFinished), Is.Zero);
        }

        [Test]
        public void ExecCarrierDeath_ReclaimsFrame()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Entity actor = harness.CreateCastingActor(AbilityWithContextId);

            harness.ExecSystem.Update(0f);
            harness.ContextSystem.Update(0f);
            Assert.That(harness.Stack.Count, Is.EqualTo(2));

            world.Destroy(actor);
            harness.ContextSystem.Update(0f);
            Assert.That(harness.Stack.Count, Is.EqualTo(1), "Caster death must reclaim the frame by context entity.");
        }

        [Test]
        public void AbilityWithoutProfile_NeverTouchesTheStack()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Entity actor = harness.CreateCastingActor(AbilityWithoutContextId);

            harness.ExecSystem.Update(0f);
            Assert.That(world.Has<AbilityExecInstance>(actor), Is.True);

            uint revisionBefore = harness.Stack.Revision;
            harness.ContextSystem.Update(0f);
            harness.ContextSystem.Update(0f);
            Assert.That(harness.Stack.Count, Is.EqualTo(1));
            Assert.That(harness.Stack.Revision, Is.EqualTo(revisionBefore), "No frame operations for profile-less abilities.");
        }

        [Test]
        public void DeclaredButUninstalledProfile_FailsFast()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.CreateCastingActor(AbilityWithDanglingProfileId);

            harness.ExecSystem.Update(0f);
            Assert.Throws<InvalidOperationException>(() => harness.ContextSystem.Update(0f));
        }

        [Test]
        public void SteadyState_TrackedWaitingExec_IsAllocationFree()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            harness.CreateCastingActor(AbilityWithContextId);
            harness.ExecSystem.Update(0f);
            harness.ContextSystem.Update(0f);
            Assert.That(harness.Stack.Count, Is.EqualTo(2));

            long allocated = MeasureContextUpdateAllocations(harness);
            allocated = Math.Min(allocated, MeasureContextUpdateAllocations(harness));
            Assert.That(allocated, Is.EqualTo(0), "Steady-state exec context reconciliation must be allocation free.");
        }

        private static long MeasureContextUpdateAllocations(Harness harness)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                harness.ContextSystem.Update(0f);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static int CountPresentationEvents(GasPresentationEventBuffer events, GasPresentationEventKind kind)
        {
            int count = 0;
            foreach (ref readonly var evt in events.Events)
            {
                if (evt.Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        private sealed class Harness
        {
            public World World = null!;
            public OwnershipResolver Ownership = null!;
            public EntityCollectionStore Store = null!;
            public InteractionContextStack Stack = null!;
            public ContextBoundCollectionWriter Writer = null!;
            public GameplayEventBus EventBus = null!;
            public OrderTypeRegistry OrderTypes = null!;
            public GasPresentationEventBuffer PresentationEvents = null!;
            public AbilityDefinitionRegistry Definitions = null!;
            public AbilityExecSystem ExecSystem = null!;
            public AbilityExecInteractionContextSystem ContextSystem = null!;
            public int CommandSourceKeyId;
            public int AbilityTargetsKeyId;

            public static Harness Create(World world, int terminalResultCapacity = OrderTerminalResultBuffer.DefaultCapacity)
            {
                var types = new RelationshipTypeRegistry();
                var relationships = new RelationshipRuntime(
                    world,
                    types,
                    new RelationshipMetricRegistry(),
                    new RelationshipFlagRegistry(),
                    new RelationshipBandRegistry(),
                    new RelationshipChangeBuffer(capacity: 4),
                    new RelationshipReverseIndex(world));
                int ownsTypeId = types.Register("Owns");
                int controlsTypeId = types.Register("Controls");
                var ownership = new OwnershipResolver(relationships, ownsTypeId);
                var domains = new ControlDomainQuery(world, relationships, ownership, ownsTypeId, controlsTypeId);

                var keyRegistry = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var store = new EntityCollectionStore(keyRegistry, initialCollectionCapacity: 16, initialRowCapacity: 128);
                var stack = new InteractionContextStack(keyRegistry);
                stack.Push(InteractionContextFrameDescriptor.Create(
                    InteractionContextIds.Default,
                    EntityCollectionKeys.CommandSource,
                    "view.test.default"));

                var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry(), new GasBudget());
                var filters = new FilterProfileRegistry(stack.FilterProfileIdRegistry, world, tagOps);
                var writer = new ContextBoundCollectionWriter(stack, filters, new DomainRoutedCollectionWriter(store, domains), store);

                var contextProfiles = new InteractionContextProfileRegistry(stack.ContextIdRegistry);
                contextProfiles.Install(new InteractionContextProfilesConfig
                {
                    Profiles = new List<InteractionContextProfileDefinition>
                    {
                        new()
                        {
                            Id = ContextProfileName,
                            ActiveCollectionKey = AbilityTargetsCollectionKey,
                            ActiveEntityViewKey = "view.ability.test.targets",
                        },
                    },
                });

                int stunTagId = TagRegistry.Register(StunTagName);
                var waitSpec = default(AbilityExecSpec);
                waitSpec.ClockId = GasClockId.Step;
                waitSpec.InterruptAny.AddTag(stunTagId);
                waitSpec.SetItem(0, ExecItemKind.EventGate, tick: 0, tagId: WaitEventTagId);
                waitSpec.SetItem(1, ExecItemKind.End, tick: 0);

                var definitions = new AbilityDefinitionRegistry();
                definitions.Register(AbilityWithContextId, new AbilityDefinition
                {
                    ExecSpec = waitSpec,
                    InteractionContextProfileId = ContextProfileName,
                    HasInteractionContextProfile = true,
                });
                definitions.Register(AbilityWithoutContextId, new AbilityDefinition { ExecSpec = waitSpec });
                definitions.Register(AbilityWithDanglingProfileId, new AbilityDefinition
                {
                    ExecSpec = waitSpec,
                    InteractionContextProfileId = "ctx.ability.test.not_installed",
                    HasInteractionContextProfile = true,
                });

                var eventBus = new GameplayEventBus();
                var orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(terminalResultCapacity));
                orderTypes.Register(new OrderTypeConfig
                {
                    OrderTypeId = CastOrderTypeId,
                    AllowQueuedMode = false,
                    ClearQueueOnActivate = true,
                    CanInterruptSelf = true,
                    IntArg0BlackboardKey = OrderBlackboardKeys.Cast_SlotIndex,
                    EntityBlackboardKey = OrderBlackboardKeys.Cast_TargetEntity,
                    SpatialBlackboardKey = OrderBlackboardKeys.Cast_TargetPosition,
                });

                var presentationEvents = new GasPresentationEventBuffer(32);
                var execSystem = new AbilityExecSystem(
                    world,
                    new DiscreteClock(),
                    new InputRequestQueue(),
                    new InputResponseBuffer(),
                    new EffectRequestQueue(),
                    4096,
                    definitions,
                    eventBus,
                    castAbilityOrderTypeId: CastOrderTypeId,
                    presentationEvents: presentationEvents,
                    orderTypeRegistry: orderTypes,
                    tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

                return new Harness
                {
                    World = world,
                    Ownership = ownership,
                    Store = store,
                    Stack = stack,
                    Writer = writer,
                    EventBus = eventBus,
                    OrderTypes = orderTypes,
                    PresentationEvents = presentationEvents,
                    Definitions = definitions,
                    ExecSystem = execSystem,
                    ContextSystem = new AbilityExecInteractionContextSystem(world, stack, contextProfiles, definitions),
                    CommandSourceKeyId = keyRegistry.Register(EntityCollectionKeys.CommandSource),
                    AbilityTargetsKeyId = keyRegistry.Register(AbilityTargetsCollectionKey),
                };
            }

            public Entity CreateCastingActor(int abilityId)
            {
                Entity actor = World.Create(
                    OrderBuffer.CreateEmpty(),
                    new BlackboardIntBuffer(),
                    new BlackboardEntityBuffer(),
                    new BlackboardSpatialBuffer(),
                    new AbilityStateBuffer(),
                    new GameplayTagContainer(),
                    new TagCountContainer());

                ref var abilities = ref World.Get<AbilityStateBuffer>(actor);
                abilities.AddAbility(abilityId);

                var order = new Order { OrderId = 7, Actor = actor, OrderTypeId = CastOrderTypeId };
                ref var orderBuffer = ref World.Get<OrderBuffer>(actor);
                orderBuffer.SetActiveDirect(in order, priority: 100);

                ref var blackboardInts = ref World.Get<BlackboardIntBuffer>(actor);
                blackboardInts.Set(OrderBlackboardKeys.Cast_SlotIndex, 0);
                return actor;
            }
        }
    }
}
