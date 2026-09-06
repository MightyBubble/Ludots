using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Map;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.TypedCollections;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// #1398 S2b: the context trigger gate — entering a context mounts the profile's
    /// <c>triggers[]</c> listeners on the subject, leaving unmounts them, derived contexts
    /// gate their own triggers the same way, and dead subjects are swept. Graph execution
    /// through the mounted TriggerGraph triggers rides the same executor covered by
    /// TriggerGraphMountTests; these tests pin the gating lifecycle itself.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class InteractionContextTriggerMountTests
    {
        private const string MapIdValue = "map_context_gate_probe";
        private const string GraphName = "Graph.TriggerGraph.ContextGate";
        private const string CounterGraph = "Graph.Script.ContextGate.DeactivatedCounter";
        private const string BattleProfile = "interaction.context.gate.battle";
        private const string BoxingProfile = "interaction.context.gate.boxing";
        private const string IdleProfile = "interaction.context.gate.idle";
        private const string CounterProfile = "interaction.context.gate.counter";
        private const int CounterKeyId = 1007;

        private readonly MapId _mapId = new(MapIdValue);

        [SetUp]
        public void SetUp()
        {
            GraphIdRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            GraphIdRegistry.Clear();
        }

        [Test]
        public void EnteringContext_MountsProfileTriggerListeners()
        {
            using World world = NewWorld(out var gate, out var profiles, out var triggers);
            Entity subject = world.Create(NewMapEntity());
            MountBaseContext(world, profiles, subject, BattleProfile);

            gate.Update(0.016f);

            int battleId = profiles.ProfileIdRegistry.GetId(BattleProfile);
            Assert.That(gate.TryGetMountedTriggers(subject, battleId, out var mounted), Is.True);
            Assert.That(mounted.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(mounted[0].EventKey, Is.EqualTo(GameEvents.MapLoaded), "the mount narrows to the profile's declared event");
            Assert.That(((TriggerGraphMountTrigger)mounted[0]).Scope, Is.EqualTo(subject));
            Assert.That(triggers.HasMapEventSubscribers(_mapId, GameEvents.MapLoaded), Is.True);
        }

        [Test]
        public void LeavingContext_UnmountsListeners()
        {
            using World world = NewWorld(out var gate, out var profiles, out var triggers);
            Entity subject = world.Create(NewMapEntity());
            MountBaseContext(world, profiles, subject, BattleProfile);
            gate.Update(0.016f);
            Assert.That(triggers.HasMapEventSubscribers(_mapId, GameEvents.MapLoaded), Is.True);

            world.Remove<InteractionContextInstance>(subject);
            gate.Update(0.016f);

            Assert.That(triggers.HasMapEventSubscribers(_mapId, GameEvents.MapLoaded), Is.False, "离开 context 后监听拆净");
            Assert.That(gate.MountedSubjectCount, Is.EqualTo(0));
        }

        [Test]
        public void DerivedContext_TriggersFollowActivationAndDeactivation()
        {
            using World world = NewWorld(out var gate, out var profiles, out var triggers);
            var runtime = new InteractionContextInstanceRuntime(world, profiles, NewEvents());
            Entity subject = world.Create(NewMapEntity());
            MountBaseContext(world, profiles, subject, BattleProfile);

            runtime.Activate(subject, ConfigKeyRegistry.Register(BoxingProfile), 0);
            gate.Update(0.016f);

            int battleId = profiles.ProfileIdRegistry.GetId(BattleProfile);
            int boxingId = profiles.ProfileIdRegistry.GetId(BoxingProfile);
            Assert.That(gate.TryGetMountedTriggers(subject, battleId, out _), Is.True, "base context keeps its triggers");
            Assert.That(gate.TryGetMountedTriggers(subject, boxingId, out var boxingMounts), Is.True, "衍生 context 的 triggers 随其激活挂上");
            Assert.That(boxingMounts.Count, Is.GreaterThanOrEqualTo(1));

            runtime.Deactivate(subject, ConfigKeyRegistry.Register(BoxingProfile));
            gate.Update(0.016f);
            Assert.That(gate.TryGetMountedTriggers(subject, boxingId, out _), Is.False, "衍生 context 停用即拆");
            Assert.That(gate.TryGetMountedTriggers(subject, battleId, out _), Is.True);
        }

        [Test]
        public void DeadSubject_MountsReclaimedOnDestroyTick()
        {
            using World world = NewWorld(out var gate, out var profiles, out var triggers);
            var entityMounts = new EntityTriggerGraphMounts(
                world,
                sessions: () => null,
                triggers,
                contextFactory: () => new ScriptContext(),
                decorators: () => null,
                programs: () => null,
                customEvents: () => null);
            Entity subject = world.Create(NewMapEntity());
            MountBaseContext(world, profiles, subject, BattleProfile);
            gate.Update(0.016f);
            Assert.That(triggers.HasMapEventSubscribers(_mapId, GameEvents.MapLoaded), Is.True);

            world.Destroy(subject);
            gate.Update(0.016f);

            // Destroy-time contract (#1398 刀2): the retired heartbeat sweep is gone. The
            // dead subject's own context mounts are reclaimed on the destroy handler — no
            // staged budget, no wave — so the ledger gap closes immediately.
            Assert.That(gate.MountedSubjectCount, Is.EqualTo(0));
            Assert.That(
                triggers.HasMapEventSubscribers(_mapId, GameEvents.MapLoaded),
                Is.False,
                "dead-subject context mounts are reclaimed on the destroy tick");
            Assert.That(entityMounts.GetDeadMountCount(_mapId), Is.EqualTo(0));
        }

        [Test]
        public void SubjectWithoutMapEntity_FailsFast()
        {
            using World world = NewWorld(out var gate, out var profiles, out _);
            Entity subject = world.Create();
            MountBaseContext(world, profiles, subject, BattleProfile);

            Assert.That(
                () => gate.Update(0.016f),
                Throws.InvalidOperationException.With.Message.Contains("MapEntity"));
        }

        [Test]
        public void ContextWithoutDeclaredTriggers_MountsNothing()
        {
            using World world = NewWorld(out var gate, out var profiles, out var triggers);
            Entity subject = world.Create(NewMapEntity());
            MountBaseContext(world, profiles, subject, IdleProfile);

            gate.Update(0.016f);

            Assert.That(gate.MountedSubjectCount, Is.EqualTo(0));
            Assert.That(triggers.HasMapEventSubscribers(_mapId, GameEvents.MapLoaded), Is.False);
        }

        /// <summary>
        /// #1398 刀3: the change point (DeactivateContext) runs the profile's onDeactivated
        /// slot synchronously — no 1-tick settlement delay, exactly once. The next gate update
        /// flushes the deferred unmount and its reconcile must NOT re-run the slot (mounts are
        /// already gone), and a later re-activation fires the slot again for the new window.
        /// </summary>
        [Test]
        public void ChangePointDeactivation_RunsDeactivatedSlotOnceAndReconcileDoesNotRepeat()
        {
            using World world = NewWorld(out var gate, out var profiles, out _);
            var runtime = new InteractionContextInstanceRuntime(world, profiles, NewEvents());
            runtime.BindDeactivatedSlotRunner(gate.RunDeactivatedSlotNow);

            Entity subject = world.Create(NewMapEntity());
            MountBaseContext(world, profiles, subject, BattleProfile);
            var counter = new Ludots.Core.Gameplay.GAS.Components.BlackboardIntBuffer();
            counter.Set(CounterKeyId, 0);
            world.Add(subject, counter);

            int counterProfileId = profiles.ProfileIdRegistry.GetId(CounterProfile);
            int bootstrapId = ConfigKeyRegistry.Register(CounterProfile);

            // Window opens: gate mounts the profile's triggers (Activated slot is empty here).
            runtime.Activate(subject, bootstrapId, 0);
            gate.Update(0.016f);
            Assert.That(gate.TryGetMountedTriggers(subject, counterProfileId, out _), Is.True,
                "counter profile's declared trigger mounts on activation");

            // Change point: DeactivateContext → onDeactivated slot runs synchronously.
            runtime.Deactivate(subject, bootstrapId);
            Assert.That(ReadCounter(world, subject), Is.EqualTo(1),
                "onDeactivated slot runs at the same change point, not on a later tick");

            // Next gate update: deferred unmount flush + reconcile must not re-run the slot.
            gate.Update(0.016f);
            Assert.That(ReadCounter(world, subject), Is.EqualTo(1),
                "reconcile must not re-run the deactivated slot (single execution)");
            Assert.That(gate.TryGetMountedTriggers(subject, counterProfileId, out _), Is.False,
                "change-point unmount is flushed by the gate update");

            // A fresh window still fires the slot once again (re-activation is not poisoned).
            runtime.Activate(subject, bootstrapId, 0);
            gate.Update(0.016f);
            runtime.Deactivate(subject, bootstrapId);
            Assert.That(ReadCounter(world, subject), Is.EqualTo(2),
                "re-activated window runs the deactivated slot again");
            gate.Update(0.016f);
            Assert.That(ReadCounter(world, subject), Is.EqualTo(2));
        }

        private static int ReadCounter(World world, Entity subject)
        {
            ref var bb = ref world.Get<Ludots.Core.Gameplay.GAS.Components.BlackboardIntBuffer>(subject);
            return bb.TryGet(CounterKeyId, out int value) ? value : -1;
        }

        private static MapEntity NewMapEntity() => new() { MapId = new MapId(MapIdValue) };

        private static void MountBaseContext(
            World world,
            InteractionContextProfileRegistry profiles,
            Entity subject,
            string profileId)
        {
            world.Add(subject, new InteractionContextInstance
            {
                ContextId = profiles.ProfileIdRegistry.GetId(profileId),
                Source = InteractionContextInstanceSource.TemplateSpawn,
            });
        }

        private static World NewWorld(
            out InteractionContextTriggerMountSystem gate,
            out InteractionContextProfileRegistry profiles,
            out TriggerManager triggers)
        {
            World world = World.Create();
            var programs = new GraphProgramRegistry();
            RegisterProbeGraph(programs);

            var profileIds = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            profiles = new InteractionContextProfileRegistry(profileIds);
            profiles.Install(
                new InteractionContextProfilesConfig
                {
                    Profiles = new List<InteractionContextProfileDefinition>
                    {
                        new()
                        {
                            Id = BattleProfile,
                            ActiveCollectionKey = "collection.gate.battle",
                            Triggers = new List<InteractionContextTriggerMount>
                            {
                                new() { Trigger = GraphName, Event = GameEvents.MapLoaded.Value },
                            },
                        },
                        new()
                        {
                            Id = BoxingProfile,
                            ActiveCollectionKey = "collection.gate.boxing",
                            Triggers = new List<InteractionContextTriggerMount>
                            {
                                new() { Trigger = GraphName, Event = GameEvents.MapLoaded.Value },
                            },
                        },
                        new()
                        {
                            Id = IdleProfile,
                            ActiveCollectionKey = "collection.gate.idle",
                        },
                        new()
                        {
                            Id = CounterProfile,
                            ActiveCollectionKey = "collection.gate.counter",
                            Triggers = new List<InteractionContextTriggerMount>
                            {
                                new() { Trigger = GraphName, Event = GameEvents.MapLoaded.Value },
                            },
                            OnDeactivated = new List<string> { CounterGraph },
                        },
                    },
                },
                NewRegistry(),
                NewRegistry(),
                NewRegistry(),
                new InteractionContextProfileReferenceCatalog(programs, Array.Empty<string>()));

            triggers = new TriggerManager();
            var customEvents = new CustomEventNameRegistry();
            var writer = NewReturnWriter(world, programs);
            gate = new InteractionContextTriggerMountSystem(
                world,
                triggers,
                profiles,
                programs,
                customEvents,
                eventSchemas: null,
                sessions: () => null,
                writer,
                new GasGraphRuntimeApi(world));
            return world;
        }

        private static GraphReturnWriter NewReturnWriter(World world, GraphProgramRegistry programs)
        {
            var collectionKeys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var entityCollections = new EntityCollectionStore(collectionKeys);
            var intIdCollections = new IntIdCollectionStore(collectionKeys);
            var outputValues = new GraphOutputValueStore(new StringIntRegistry(), initialCapacity: 4);
            return new GraphReturnWriter(
                world,
                programs,
                new GraphOutputSchemaRegistry(),
                GasGraphOpHandlerTable.Instance,
                entityCollections,
                intIdCollections,
                outputValues);
        }

        private static void RegisterProbeGraph(GraphProgramRegistry programs)
        {
            GraphInstruction[] program =
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            var entries = new[]
            {
                new TriggerGraphEntry("gate", GameEvents.MapLoaded.Value, 0, once: false),
            };
            int id = GraphIdRegistry.Register(GraphName);
            programs.Register(id, program, GraphKind.TriggerGraph, GraphInstructionSourceMap.Empty, null, entries);

            // Script body: increments E[caster].BlackboardInt[CounterKeyId] on every execution —
            // a monotonic counter that distinguishes a single Deactivated-slot run from a repeat.
            GraphInstruction[] counter =
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadCaster, Dst = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ReadBlackboardInt, A = 1, Dst = 0, Imm = CounterKeyId },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 2, Imm = 1 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.AddInt, A = 0, B = 2, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.WriteBlackboardInt, A = 1, B = 0, Imm = CounterKeyId },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            int counterId = GraphIdRegistry.Register(CounterGraph);
            programs.Register(counterId, counter, GraphKind.Script, GraphInstructionSourceMap.Empty, null, null);
        }

        private static StringIntRegistry NewRegistry()
            => new(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);

        private static Ludots.Core.Presentation.Events.PresentationEventStream NewEvents() => new(capacity: 8);
    }
}
