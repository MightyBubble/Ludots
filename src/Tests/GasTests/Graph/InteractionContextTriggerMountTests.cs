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
        private const string BattleProfile = "interaction.context.gate.battle";
        private const string BoxingProfile = "interaction.context.gate.boxing";
        private const string IdleProfile = "interaction.context.gate.idle";

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
        public void DeadSubject_IsInertThenReclaimedByHeartbeatSweep()
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

            // New reclamation contract (#1398 D11): dead subjects go inert immediately —
            // not counted, never dispatching — and the bounded heartbeat sweep reclaims
            // the mounts, the same policy template mounts follow.
            Assert.That(gate.MountedSubjectCount, Is.EqualTo(0));
            Assert.That(
                triggers.HasMapEventSubscribers(_mapId, GameEvents.MapLoaded),
                Is.True,
                "dead-subject mounts stay registered until the budgeted sweep reclaims them");

            var heartbeat = new ScriptContext();
            heartbeat.Set(CoreServiceKeys.MapId, _mapId);
            triggers.FireMapEvent(_mapId, GameEvents.MapHeartbeat, heartbeat);

            Assert.That(triggers.HasMapEventSubscribers(_mapId, GameEvents.MapLoaded), Is.False);
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
        }

        private static StringIntRegistry NewRegistry()
            => new(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);

        private static Ludots.Core.Presentation.Events.PresentationEventStream NewEvents() => new(capacity: 8);
    }
}
