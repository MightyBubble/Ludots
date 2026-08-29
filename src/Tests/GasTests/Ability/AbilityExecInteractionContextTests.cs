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
    /// <c>interactionContextProfile</c> mounts its context as entity state on the carrier's
    /// control-domain representative while its exec instance runs, and the mount is reclaimed on
    /// every teardown path (finish, interrupt, caster death). While the context is mounted,
    /// context-bound cast commits land in the ability's collection key and the steady-state
    /// command source stays untouched; abilities without a profile never mount anything. All
    /// profile/collection/tag names are test data, never Core concepts.
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
        private const string ContextIntentName = "intent.context.test.declared";
        private const string PlayerDefaultIntentName = "intent.context.test.player_default";
        private const string AbilityTargetsCollectionKey = "collection.ability.test.targets";
        private const string StunTagName = "test.state.stunned";
        private const string TerminalCapacityTagName = "test.exec.terminal_capacity_side_effect";

        [SetUp]
        public void SetUp()
        {
            TagRegistry.Clear();
        }

        [Test]
        public void ExecStart_MountsContext_CastCommitsSwitchKeys_AndFinishRestoresSteadyState()
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
            harness.Ownership.EnsureOwnership(p1Rep, actor);
            harness.ExecSystem.Update(0f);
            Assert.That(world.Has<AbilityExecInstance>(actor), Is.True, "Exec must be gate-waiting.");

            harness.ContextSystem.Update(0f);
            Assert.That(world.TryGet<ActiveInteractionContext>(p1Rep, out ActiveInteractionContext mounted), Is.True,
                "Exec start must mount the ability's context on the carrier's domain rep.");
            Assert.That(mounted.ActiveCollectionKeyId, Is.EqualTo(harness.AbilityTargetsKeyId), "The mounted context must expose the ability collection key.");
            Assert.That(mounted.ContextEntity, Is.EqualTo(actor), "Context ownership is the exec carrier entity.");
            Assert.That(mounted.Source, Is.EqualTo(ActiveInteractionContextSource.ExecLifecycle));

            // M2: casts during the ability context land in the ability key; command.source is untouched.
            harness.Writer.CommitCast(p1Rep, stackalloc Entity[] { m05, m06 }, EntityCollectionSourceKind.UiAcquisition);
            Span<Entity> rows = stackalloc Entity[8];
            Assert.That(harness.Store.TryGet(p1Rep, harness.AbilityTargetsKeyId, out EntityCollectionHandle abilityHandle), Is.True);
            int count = harness.Store.CopyEntities(abilityHandle, 0, rows);
            Assert.That(rows[..count].ToArray(), Is.EqualTo(new[] { m05, m06 }));
            Assert.That(harness.Store.TryGet(p1Rep, harness.CommandSourceKeyId, out EntityCollectionHandle commandHandle), Is.True);
            count = harness.Store.CopyEntities(commandHandle, 0, rows);
            Assert.That(rows[..count].ToArray(), Is.EqualTo(new[] { m01, m02 }), "command.source must not change while the ability context is active.");

            // Repeated updates while the exec waits must not duplicate the mount.
            harness.ContextSystem.Update(0f);
            Assert.That(world.CountEntities(new QueryDescription().WithAll<ActiveInteractionContext>()), Is.EqualTo(1));

            // Complete the exec: the event gate resolves on one tick, End fires on the next.
            harness.EventBus.Publish(new GameplayEvent { TagId = WaitEventTagId, Source = actor });
            harness.EventBus.Update();
            harness.ExecSystem.Update(0f);
            harness.ExecSystem.Update(0f);
            Assert.That(world.Has<AbilityExecInstance>(actor), Is.False, "Exec must be torn down after End.");

            harness.ContextSystem.Update(0f);
            Assert.That(world.Has<ActiveInteractionContext>(p1Rep), Is.False, "The context must be reclaimed when the exec ends.");

            harness.Writer.CommitCast(p1Rep, stackalloc Entity[] { m02 }, EntityCollectionSourceKind.UiAcquisition);
            Assert.That(harness.Store.TryGet(p1Rep, harness.CommandSourceKeyId, out commandHandle), Is.True);
            count = harness.Store.CopyEntities(commandHandle, 0, rows);
            Assert.That(rows[..count].ToArray(), Is.EqualTo(new[] { m02 }), "Casts write command.source again in the steady state.");
        }

        [Test]
        public void ExecInterrupted_ByTag_ReclaimsMount()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Entity rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity actor = harness.CreateCastingActor(AbilityWithContextId);
            harness.Ownership.EnsureOwnership(rep, actor);

            harness.ExecSystem.Update(0f);
            harness.ContextSystem.Update(0f);
            Assert.That(world.Has<ActiveInteractionContext>(rep), Is.True);

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
            Assert.That(world.Has<ActiveInteractionContext>(rep), Is.False, "Interrupted exec must reclaim its mounted context.");
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
        public void ExecCarrierDeath_ReclaimsMount()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Entity rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity actor = harness.CreateCastingActor(AbilityWithContextId);
            harness.Ownership.EnsureOwnership(rep, actor);

            harness.ExecSystem.Update(0f);
            harness.ContextSystem.Update(0f);
            Assert.That(world.Has<ActiveInteractionContext>(rep), Is.True);

            world.Destroy(actor);
            harness.ContextSystem.Update(0f);
            Assert.That(world.Has<ActiveInteractionContext>(rep), Is.False, "Caster death must reclaim the mounted context.");
        }

        [Test]
        public void ExecStart_MountsActiveContextOnTheDomainRep_ArbiterPrefersItOverPlayerDefault()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Entity rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity actor = harness.CreateCastingActor(AbilityWithContextId);
            harness.Ownership.EnsureOwnership(rep, actor);

            harness.ExecSystem.Update(0f);
            harness.ContextSystem.Update(0f);

            Assert.That(world.TryGet<ActiveInteractionContext>(rep, out ActiveInteractionContext mounted), Is.True,
                "the active context must be mounted on its carrier's control-domain rep.");
            Assert.That(mounted.ContextEntity, Is.EqualTo(actor));
            Assert.That(
                harness.ContextProfiles.ProfileIdRegistry.GetName(mounted.ContextId),
                Is.EqualTo(ContextProfileName),
                "the mounted state carries the context's profile identity.");
            Assert.That(
                mounted.CommandIntentProfileId,
                Is.EqualTo(harness.IntentIds.GetId(ContextIntentName)));

            CommandPref pref = NewPlayerDefaultPref(harness);
            Assert.That(
                CommandIntentArbiter.ResolveActiveCommandIntent(world, rep, in pref),
                Is.EqualTo(harness.IntentIds.GetId(ContextIntentName)),
                "DEC-14: the mounted context's explicit intent must win over the player default.");
        }

        [Test]
        public void ExecEnd_ReleasesMountedContext_SteadyStateArbiterAppliesPlayerDefault()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Entity rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity actor = harness.CreateCastingActor(AbilityWithContextId);
            harness.Ownership.EnsureOwnership(rep, actor);

            harness.ExecSystem.Update(0f);
            harness.ContextSystem.Update(0f);
            Assert.That(world.Has<ActiveInteractionContext>(rep), Is.True);

            harness.EventBus.Publish(new GameplayEvent { TagId = WaitEventTagId, Source = actor });
            harness.EventBus.Update();
            harness.ExecSystem.Update(0f);
            harness.ExecSystem.Update(0f);
            harness.ContextSystem.Update(0f);

            Assert.That(world.Has<ActiveInteractionContext>(rep), Is.False,
                "the mounted context must be released with the exec when it ends.");
            CommandPref pref = NewPlayerDefaultPref(harness);
            Assert.That(
                CommandIntentArbiter.ResolveActiveCommandIntent(world, rep, in pref),
                Is.EqualTo(pref.DefaultCommandIntentId),
                "DEC-14: steady state (no mounted context) routes through the player default.");
        }

        [Test]
        public void ExecCarrierDeath_KeepsMountedContextFrozenUntilReclaim()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Entity rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity actor = harness.CreateCastingActor(AbilityWithContextId);
            harness.Ownership.EnsureOwnership(rep, actor);

            harness.ExecSystem.Update(0f);
            harness.ContextSystem.Update(0f);

            world.Destroy(actor);
            Assert.That(
                world.TryGet<ActiveInteractionContext>(rep, out ActiveInteractionContext frozen) &&
                frozen.ContextEntity == actor,
                Is.True,
                "the pre-reclaim window keeps the dead carrier mounted so owner resolution fails closed instead of silently falling back.");

            harness.ContextSystem.Update(0f);
            Assert.That(world.Has<ActiveInteractionContext>(rep), Is.False,
                "reclaim must release the mounted context with the exec.");
        }

        [Test]
        public void ExecCarrierWithoutControlDomain_MountsNoInteractionState()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Entity actor = harness.CreateCastingActor(AbilityWithContextId);

            harness.ExecSystem.Update(0f);
            harness.ContextSystem.Update(0f);

            Assert.That(
                world.CountEntities(new QueryDescription().WithAll<ActiveInteractionContext>()),
                Is.Zero,
                "an exec whose carrier resolves to no control domain mounts onto no interaction subject.");
        }

        [Test]
        public void TwoContextExecsInOneDomain_LatestCarrierArbitrates_EndingItExposesTheLowerOne()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Entity rep = world.Create(new PlayerIdentity { PlayerId = 1 });

            Entity first = harness.CreateCastingActor(AbilityWithContextId);
            harness.Ownership.EnsureOwnership(rep, first);
            harness.ExecSystem.Update(0f);
            harness.ContextSystem.Update(0f);
            Assert.That(
                world.TryGet<ActiveInteractionContext>(rep, out ActiveInteractionContext mounted) &&
                mounted.ContextEntity == first,
                Is.True);

            Entity second = harness.CreateCastingActor(AbilityWithContextId);
            harness.Ownership.EnsureOwnership(rep, second);
            harness.ExecSystem.Update(0f);
            harness.ContextSystem.Update(0f);
            Assert.That(
                world.TryGet<ActiveInteractionContext>(rep, out mounted) && mounted.ContextEntity == second,
                Is.True,
                "the latest-activated carrier must arbitrate for the domain (LIFO).");

            // End only the topmost exec: the event gate wakes every waiter of the tag, so the
            // per-actor interrupt tag is the precise teardown path here.
            ref var secondTags = ref world.Get<GameplayTagContainer>(second);
            secondTags.AddTag(TagRegistry.GetId(StunTagName));
            harness.ExecSystem.Update(0f);
            harness.ContextSystem.Update(0f);

            Assert.That(
                world.TryGet<ActiveInteractionContext>(rep, out mounted) && mounted.ContextEntity == first,
                Is.True,
                "ending the topmost context must expose the still-active lower one, matching stack pop semantics.");
        }

        private static CommandPref NewPlayerDefaultPref(Harness harness)
        {
            CommandPref pref = default;
            pref.SetPlayerDefault(
                harness.IntentIds.Register(PlayerDefaultIntentName),
                castDispatchProfileId: 777);
            return pref;
        }

        [Test]
        public void AbilityWithoutProfile_NeverMountsInteractionState()
        {
            using var world = World.Create();
            Harness harness = Harness.Create(world);
            Entity actor = harness.CreateCastingActor(AbilityWithoutContextId);

            harness.ExecSystem.Update(0f);
            Assert.That(world.Has<AbilityExecInstance>(actor), Is.True);

            harness.ContextSystem.Update(0f);
            harness.ContextSystem.Update(0f);
            Assert.That(world.CountEntities(new QueryDescription().WithAll<ActiveInteractionContext>()), Is.Zero,
                "No mounted context for profile-less abilities.");
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
            Entity rep = world.Create(new PlayerIdentity { PlayerId = 1 });
            Entity actor = harness.CreateCastingActor(AbilityWithContextId);
            harness.Ownership.EnsureOwnership(rep, actor);
            harness.ExecSystem.Update(0f);
            harness.ContextSystem.Update(0f);
            Assert.That(world.Has<ActiveInteractionContext>(rep), Is.True);

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
            public InteractionContextProfileRegistry ContextProfiles = null!;
            public StringIntRegistry IntentIds = null!;
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

                var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry(), new GasBudget());
                var filterProfileIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var filters = new FilterProfileRegistry(filterProfileIds, world, tagOps);

                var commandIntentProfileIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                commandIntentProfileIds.Register(ContextIntentName);
                commandIntentProfileIds.Register(PlayerDefaultIntentName);

                var contextProfileIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var contextProfiles = new InteractionContextProfileRegistry(contextProfileIds);
                contextProfiles.Install(new InteractionContextProfilesConfig
                {
                    Profiles = new List<InteractionContextProfileDefinition>
                    {
                        new()
                        {
                            Id = ContextProfileName,
                            ActiveCollectionKey = AbilityTargetsCollectionKey,
                            ActiveEntityViewKey = "view.ability.test.targets",
                            CommandIntentId = ContextIntentName,
                        },
                        new()
                        {
                            Id = InteractionContextIds.Default,
                            ActiveCollectionKey = EntityCollectionKeys.CommandSource,
                            ActiveEntityViewKey = "view.test.default",
                        },
                    },
                }, keyRegistry, filterProfileIds, commandIntentProfileIds);

                var writer = new ContextBoundCollectionWriter(
                    world,
                    contextProfiles,
                    filters,
                    new DomainRoutedCollectionWriter(store, domains),
                    store);

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
                    ContextProfiles = contextProfiles,
                    IntentIds = commandIntentProfileIds,
                    Writer = writer,
                    EventBus = eventBus,
                    OrderTypes = orderTypes,
                    PresentationEvents = presentationEvents,
                    Definitions = definitions,
                    ExecSystem = execSystem,
                    ContextSystem = new AbilityExecInteractionContextSystem(world, contextProfiles, definitions, domains),
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
