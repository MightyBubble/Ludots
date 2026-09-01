using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// #1398 S2b gap 9 + Case E 01/02/06: the collection pass-through chain —
    /// DispatchCollectionEvent op → map event with reserved transport keys →
    /// EventKeyedCollectionWriter applying replace/add/subtract — and the
    /// template initialInteractionContext spawn mounting.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class EventKeyedCollectionWriterTests
    {
        private const string EventKeyName = "gameplay.test.selection_committed";
        private const string CollectionKey = "selected";
        private const string BattleProfile = "interaction.context.spawn.battle";

        [SetUp]
        public void SetUp()
        {
            ConfigKeyRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ConfigKeyRegistry.Clear();
        }

        [Test]
        public void ReplaceAddSubtract_WriteCollectionSemantics()
        {
            EntityCollectionStore store = NewStore(out int keyId);
            using World world = World.Create();
            Entity owner = world.Create();
            var writer = new EventKeyedCollectionWriter(store);
            writer.Register(EventKeyName);

            Entity a = world.Create();
            Entity b = world.Create();
            Entity c = world.Create();

            writer.HandleEvent(new EventKey(EventKeyName), Context(owner, keyId, (int)EventCollectionWriteOp.Replace, a, b));
            AssertCollection(store, owner, keyId, null, a, b);

            writer.HandleEvent(new EventKey(EventKeyName), Context(owner, keyId, (int)EventCollectionWriteOp.Add, b, c));
            AssertCollection(store, owner, keyId, "add unions without duplicating existing members", a, b, c);

            writer.HandleEvent(new EventKey(EventKeyName), Context(owner, keyId, (int)EventCollectionWriteOp.Subtract, b));
            AssertCollection(store, owner, keyId, null, a, c);

            writer.HandleEvent(new EventKey(EventKeyName), Context(owner, keyId, (int)EventCollectionWriteOp.Subtract, a, c));
            AssertCollection(store, owner, keyId, "subtract to empty clears the collection", Array.Empty<Entity>());
        }

        [Test]
        public void UnregisteredKey_IsIgnored_UnknownKeyAndMissingSet_FailFast()
        {
            EntityCollectionStore store = NewStore(out int keyId);
            using World world = World.Create();
            Entity owner = world.Create();
            var writer = new EventKeyedCollectionWriter(store);
            writer.Register(EventKeyName);

            Entity a = world.Create();
            var otherKey = new EventKey("gameplay.test.other_event");
            Assert.That(
                () => writer.HandleEvent(otherKey, Context(owner, keyId, 0, a)),
                Throws.Nothing,
                "events the writer did not register are not its concern");

            Assert.That(
                () => writer.HandleEvent(new EventKey(EventKeyName), Context(owner, keyId + 100, 0, a)),
                Throws.InvalidOperationException.With.Message.Contains("KeyUnknown"));

            ScriptContext missingSet = new ScriptContext();
            missingSet.Set(MapTriggerEventPayloadKeys.SourceEntity, owner);
            missingSet.Set(MapTriggerEventPayloadKeys.CollectionOp, 0);
            missingSet.Set(MapTriggerEventPayloadKeys.CollectionKey, keyId);
            Assert.That(
                () => writer.HandleEvent(new EventKey(EventKeyName), missingSet),
                Throws.InvalidOperationException.With.Message.Contains("EntitySetMissing"));

            ScriptContext badOp = Context(owner, keyId, 7, a);
            Assert.That(
                () => writer.HandleEvent(new EventKey(EventKeyName), badOp),
                Throws.InvalidOperationException.With.Message.Contains("OpInvalid"));
        }

        [Test]
        public void DispatchCollectionEventOp_FiresEventKeyConsumedByWriter()
        {
            EntityCollectionStore store = NewStore(out int keyId);
            using World world = World.Create();
            Entity rep = world.Create(NewMapEntity());
            Entity hit = world.Create();
            var triggerManager = new TriggerManager();
            var customEvents = new CustomEventNameRegistry();
            customEvents.Register(EventKeyName);
            var writer = new EventKeyedCollectionWriter(store);
            writer.Register(EventKeyName);
            triggerManager.RegisterEventHandler(
                new EventKey(EventKeyName),
                context => writer.HandleEvent(new EventKey(EventKeyName), context));

            var api = new GasGraphRuntimeApi(world);
            api.BindTriggerManager(triggerManager);
            api.BindCustomEvents(customEvents);

            GraphInstruction[] program =
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 2, Imm = (int)EventCollectionWriteOp.Replace },
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.DispatchCollectionEvent,
                    B = 2,
                    Imm = CollectionEventOpEncodingForTest.Pack(ConfigKeyRegistry.Register(EventKeyName), keyId),
                },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            var frame = GraphFrame.Bind(
                GraphKind.TriggerGraph,
                GraphEntityPreset.None,
                world,
                rep,
                rep,
                default,
                api,
                programs: null,
                new float[GraphVmLimits.MaxFloatRegisters],
                new int[GraphVmLimits.MaxIntRegisters],
                new byte[GraphVmLimits.MaxBoolRegisters],
                new Entity[GraphVmLimits.MaxEntityRegisters],
                new Entity[GraphVmLimits.MaxTargets],
                new int[GraphVmLimits.MaxIntIds],
                new int[GraphVmLimits.MaxCallStackDepth]);
            frame.Targets[0] = hit;
            frame.TargetList.SetCount(1);
            GraphExecutor.Execute(ref frame, program);

            AssertCollection(store, rep, keyId, "A 图透传 → 事件 key → B handler 写入 EntityCollectionStore", hit);
        }

        [Test]
        public void DispatchCollectionEventOp_UndeclaredEventKey_FailsFast()
        {
            using World world = World.Create();
            Entity rep = world.Create(NewMapEntity());
            var api = new GasGraphRuntimeApi(world);
            api.BindTriggerManager(new TriggerManager());
            api.BindCustomEvents(new CustomEventNameRegistry());

            var program = new GraphInstruction[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 2, Imm = 0 },
                new GraphInstruction
                {
                    Op = (ushort)GraphNodeOp.DispatchCollectionEvent,
                    B = 2,
                    Imm = CollectionEventOpEncodingForTest.Pack(ConfigKeyRegistry.Register("gameplay.test.undeclared"), ConfigKeyRegistry.Register(CollectionKey)),
                },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            var frame = GraphFrame.Bind(
                GraphKind.TriggerGraph,
                GraphEntityPreset.None,
                world,
                rep,
                rep,
                default,
                api,
                programs: null,
                new float[GraphVmLimits.MaxFloatRegisters],
                new int[GraphVmLimits.MaxIntRegisters],
                new byte[GraphVmLimits.MaxBoolRegisters],
                new Entity[GraphVmLimits.MaxEntityRegisters],
                new Entity[GraphVmLimits.MaxTargets],
                new int[GraphVmLimits.MaxIntIds],
                new int[GraphVmLimits.MaxCallStackDepth]);

            string? failure = null;
            try
            {
                GraphExecutor.Execute(ref frame, program);
            }
            catch (InvalidOperationException ex)
            {
                failure = ex.Message;
            }

            Assert.That(failure, Is.Not.Null.And.Contains("not a declared custom event"));
        }

        [Test]
        public void TemplateInitialInteractionContext_MountsSpawnContext()
        {
            using World world = World.Create();
            InteractionContextProfileRegistry profiles = NewProfiles(out int battleId);

            Entity spawned = world.Create();
            TemplateInteractionContextMounting.MountInitialContext(world, profiles, spawned, "tpl.test.rep", BattleProfile);

            Assert.That(world.TryGet<InteractionContextInstance>(spawned, out InteractionContextInstance instance), Is.True);
            Assert.That(instance.ContextId, Is.EqualTo(battleId));
            Assert.That(instance.Source, Is.EqualTo(InteractionContextInstanceSource.TemplateSpawn));
            Assert.That(instance.ParentContextId, Is.EqualTo(0));
            Assert.That(instance.ScopeTag, Is.EqualTo(0));

            Assert.That(
                () => TemplateInteractionContextMounting.MountInitialContext(world, profiles, spawned, "tpl.test.rep", BattleProfile),
                Throws.InvalidOperationException.With.Message.Contains("never overwrites"));
        }

        [Test]
        public void TemplateInitialInteractionContext_UnknownProfile_FailsFastBothPasses()
        {
            using World world = World.Create();
            InteractionContextProfileRegistry profiles = NewProfiles(out _);
            Entity spawned = world.Create();

            Assert.That(
                () => TemplateInteractionContextMounting.MountInitialContext(world, profiles, spawned, "tpl.test.rep", "interaction.context.spawn.ghost"),
                Throws.InvalidOperationException.With.Message.Contains("ghost"));

            Assert.That(
                () => TemplateInteractionContextMounting.ValidateTemplates(
                    new[] { NewTemplate("tpl.test.bad", "interaction.context.spawn.ghost") },
                    profiles),
                Throws.InvalidOperationException.With.Message.Contains("ghost"));

            Assert.That(
                () => TemplateInteractionContextMounting.ValidateTemplates(
                    new[] { NewTemplate("tpl.test.good", BattleProfile) },
                    profiles),
                Throws.Nothing);
        }

        private static MapEntity NewMapEntity() => new() { MapId = new MapId("map_collection_probe") };

        private static Ludots.Core.Config.EntityTemplate NewTemplate(string id, string? initialContext)
            => new() { Id = id, InitialInteractionContext = initialContext };

        private static ScriptContext Context(Entity owner, int keyId, int op, params Entity[] entities)
        {
            var context = new ScriptContext();
            context.Set(MapTriggerEventPayloadKeys.SourceEntity, owner);
            context.Set(MapTriggerEventPayloadKeys.CollectionEntitySet, entities);
            context.Set(MapTriggerEventPayloadKeys.CollectionOp, op);
            context.Set(MapTriggerEventPayloadKeys.CollectionKey, keyId);
            return context;
        }

        private static EntityCollectionStore NewStore(out int keyId)
        {
            var store = new EntityCollectionStore(
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
            keyId = store.KeyRegistry.Register(CollectionKey);
            return store;
        }

        private static InteractionContextProfileRegistry NewProfiles(out int battleId)
        {
            var profileIds = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var profiles = new InteractionContextProfileRegistry(profileIds);
            profiles.Install(
                new InteractionContextProfilesConfig
                {
                    Profiles = new List<InteractionContextProfileDefinition>
                    {
                        new()
                        {
                            Id = BattleProfile,
                            ActiveCollectionKey = "collection.spawn.battle",
                            ActiveEntityViewKey = "view.spawn.battle",
                        },
                    },
                },
                new StringIntRegistry(capacity: 4, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                new StringIntRegistry(capacity: 4, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                new StringIntRegistry(capacity: 4, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
            battleId = profileIds.GetId(BattleProfile);
            return profiles;
        }

        private static void AssertCollection(EntityCollectionStore store, Entity owner, int keyId, string? message = null, params Entity[] expected)
        {
            bool exists = store.TryGet(owner, keyId, out EntityCollectionHandle handle);
            EntityCollectionView view = default;
            exists = exists && store.TryGetView(handle, out view);
            Assert.That(exists, Is.True, message ?? "the collection exists");
            Assert.That(view.Count, Is.EqualTo(expected.Length), $"expected {expected.Length} members");
            var actual = new Entity[view.Count];
            store.CopyEntities(handle, 0, actual);
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>Test-visible alias of the internal op encoding.</summary>
        private static class CollectionEventOpEncodingForTest
        {
            public static int Pack(int eventKeyId, int collectionKeyId)
            {
                return eventKeyId | (collectionKeyId << 16);
            }
        }
    }
}
