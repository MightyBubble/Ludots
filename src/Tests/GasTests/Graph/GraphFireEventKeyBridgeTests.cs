using System;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Map;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// Focused slice: the graph-runtime → TriggerManager FireEventKey bridge and the
    /// blackboard capacity-overflow throw. Bounded — no spawn/destroy, no new opcode yet.
    /// </summary>
    public sealed class GraphFireEventKeyBridgeTests
    {
        private sealed class RecordingTrigger : Trigger
        {
            public bool Executed { get; private set; }
            public string? SeenMapValue { get; private set; }

            public override Task ExecuteAsync(ScriptContext context)
            {
                Executed = true;
                SeenMapValue = context.Get<MapId>(ContextKeys.MapId).Value;
                return Task.CompletedTask;
            }
        }

        // ── FireEventKey bridge ──

        [Test]
        public void FireEventKey_MappedScope_FiresMapScopedTrigger()
        {
            using World world = World.Create();
            var mapId = new MapId("fire_map");
            Entity scope = world.Create();
            world.Add(scope, new MapEntity { MapId = mapId });

            var manager = new TriggerManager();
            var api = new GasGraphRuntimeApi(world);
            api.BindTriggerManager(manager);

            int keyId = ConfigKeyRegistry.Register("Custom.FireEvent");
            var trigger = new RecordingTrigger { EventKey = new EventKey("Custom.FireEvent") };
            manager.RegisterMapTriggers(mapId, new Trigger[] { trigger });

            api.FireEventKey(scope, keyId);

            Assert.That(trigger.Executed, Is.True, "Map-scoped trigger must run for the scope's map.");
            Assert.That(trigger.SeenMapValue, Is.EqualTo(mapId.Value), "Context must carry the resolved map id.");
        }

        [Test]
        public void FireEventKey_ValidMapScope_DoesNotDispatchToGlobalTrigger()
        {
            using World world = World.Create();
            var mapId = new MapId("fire_map_global_collision");
            Entity scope = world.Create();
            world.Add(scope, new MapEntity { MapId = mapId });

            var manager = new TriggerManager();
            var api = new GasGraphRuntimeApi(world);
            api.BindTriggerManager(manager);

            int keyId = ConfigKeyRegistry.Register("Custom.CollisionEvent");
            var globalTrigger = new RecordingTrigger { EventKey = new EventKey("Custom.CollisionEvent") };
            manager.RegisterTrigger(globalTrigger);
            var mapTrigger = new RecordingTrigger { EventKey = new EventKey("Custom.CollisionEvent") };
            manager.RegisterMapTriggers(mapId, new Trigger[] { mapTrigger });

            api.FireEventKey(scope, keyId);

            Assert.That(mapTrigger.Executed, Is.True, "Map-scoped trigger must run for the scope's map.");
            Assert.That(globalTrigger.Executed, Is.False,
                "A globally registered trigger with the same event key must not be dispatched by the map-scoped FireEventKey bridge.");
        }

        [Test]
        public void FireEventKey_NoMapScope_Throws()
        {
            using World world = World.Create();
            Entity scope = world.Create();

            var manager = new TriggerManager();
            var api = new GasGraphRuntimeApi(world);
            api.BindTriggerManager(manager);

            int keyId = ConfigKeyRegistry.Register("Custom.GlobalEvent");
            var ex = Assert.Throws<InvalidOperationException>(() => api.FireEventKey(scope, keyId));
            Assert.That(ex!.Message, Does.StartWith("GAS.GRAPH.ERR.FireEventKeyScopeInvalid"));
        }

        [Test]
        public void FireEventKey_DeadOrUnmappedScope_Throws()
        {
            using World world = World.Create();
            var manager = new TriggerManager();
            var api = new GasGraphRuntimeApi(world);
            api.BindTriggerManager(manager);
            int keyId = ConfigKeyRegistry.Register("Custom.InvalidScope");

            Entity dead = world.Create();
            world.Destroy(dead);
            var deadEx = Assert.Throws<InvalidOperationException>(() => api.FireEventKey(dead, keyId));
            Assert.That(deadEx!.Message, Does.StartWith("GAS.GRAPH.ERR.FireEventKeyScopeInvalid"));

            Entity unmapped = world.Create();
            var unmappedEx = Assert.Throws<InvalidOperationException>(() => api.FireEventKey(unmapped, keyId));
            Assert.That(unmappedEx!.Message, Does.StartWith("GAS.GRAPH.ERR.FireEventKeyScopeInvalid"));
        }

        [Test]
        public void FireEventKey_EmptyMapIdScope_Throws()
        {
            using World world = World.Create();
            Entity scope = world.Create(new MapEntity { MapId = new MapId(" ") });
            var manager = new TriggerManager();
            var api = new GasGraphRuntimeApi(world);
            api.BindTriggerManager(manager);
            int keyId = ConfigKeyRegistry.Register("Custom.EmptyMap");

            var ex = Assert.Throws<InvalidOperationException>(() => api.FireEventKey(scope, keyId));
            Assert.That(ex!.Message, Does.StartWith("GAS.GRAPH.ERR.FireEventKeyScopeInvalid"));
        }

        [Test]
        public void FireEventKey_UnboundTriggerManager_Throws()
        {
            using World world = World.Create();
            Entity scope = world.Create();
            var api = new GasGraphRuntimeApi(world);

            int keyId = ConfigKeyRegistry.Register("Custom.Unbound");
            var ex = Assert.Throws<InvalidOperationException>(() => api.FireEventKey(scope, keyId));
            Assert.That(ex!.Message, Does.StartWith("GAS.GRAPH.ERR.TriggerBridgeUnavailable"));
        }

        [Test]
        public void FireEventKey_UnknownKeyId_Throws()
        {
            using World world = World.Create();
            Entity scope = world.Create();

            var manager = new TriggerManager();
            var api = new GasGraphRuntimeApi(world);
            api.BindTriggerManager(manager);

            var ex = Assert.Throws<InvalidOperationException>(() => api.FireEventKey(scope, 999_999));
            Assert.That(ex!.Message, Does.StartWith("GAS.GRAPH.ERR.EventKeyNameUnknown"));
        }

        [Test]
        public void FireEventKey_DuringDerivedAttributeWrites_ThrowsBeforeDispatch()
        {
            using World world = World.Create();
            var mapId = new MapId("derived_event_map");
            Entity scope = world.Create(new AttributeBuffer(), new MapEntity { MapId = mapId });
            var manager = new TriggerManager();
            var api = new GasGraphRuntimeApi(world);
            api.BindTriggerManager(manager);
            int keyId = ConfigKeyRegistry.Register("Custom.DerivedEvent");
            var trigger = new RecordingTrigger { EventKey = new EventKey("Custom.DerivedEvent") };
            manager.RegisterMapTriggers(mapId, new Trigger[] { trigger });
            AttributeBuffer staged = world.Get<AttributeBuffer>(scope);

            api.BeginDerivedAttributeWrites(scope, in staged);
            try
            {
                var ex = Assert.Throws<InvalidOperationException>(() => api.FireEventKey(scope, keyId));
                Assert.That(ex!.Message, Does.StartWith(IDerivedAttributeGraphRuntimeApi.SideEffectForbiddenError));
                Assert.That(trigger.Executed, Is.False);
            }
            finally
            {
                api.EndDerivedAttributeWrites(scope, ref staged, commit: false);
            }
        }

        [Test]
        public void FireEventKey_DuringEffectSideEffectTransaction_ThrowsBeforeDispatch()
        {
            using World world = World.Create();
            var mapId = new MapId("effect_event_map");
            Entity scope = world.Create(new AttributeBuffer(), new MapEntity { MapId = mapId });
            var manager = new TriggerManager();
            var api = new GasGraphRuntimeApi(world);
            api.BindTriggerManager(manager);
            int keyId = ConfigKeyRegistry.Register("Custom.EffectEvent");
            var trigger = new RecordingTrigger { EventKey = new EventKey("Custom.EffectEvent") };
            manager.RegisterMapTriggers(mapId, new Trigger[] { trigger });
            using var transaction = new EffectPhaseSideEffectTransaction(
                world,
                tagOps: null,
                effectRequests: null,
                spawnRequests: null,
                presentationEvents: null,
                attributeEntityCapacity: 1);
            transaction.Begin();
            api.BeginEffectSideEffectTransaction(transaction);

            try
            {
                var ex = Assert.Throws<InvalidOperationException>(() => api.FireEventKey(scope, keyId));
                Assert.That(ex!.Message, Does.StartWith(EffectPhaseSideEffectTransaction.UnsupportedSideEffectError));
                Assert.That(trigger.Executed, Is.False);
            }
            finally
            {
                api.EndEffectSideEffectTransaction(transaction);
            }
        }

        // ── Blackboard capacity overflow throws ──

        [Test]
        public void BlackboardFloat_Overflow_Throws()
        {
            var bb = new BlackboardFloatBuffer();
            for (int i = 0; i < GasConstants.MAX_BLACKBOARD_ENTRIES; i++)
            {
                bb.Set(i, i);
            }

            var ex = Assert.Throws<InvalidOperationException>(
                () => bb.Set(GasConstants.MAX_BLACKBOARD_ENTRIES, 1f));
            Assert.That(ex!.Message, Does.StartWith(BlackboardFloatBuffer.CapacityExceededError));
        }

        [Test]
        public void BlackboardInt_Overflow_Throws()
        {
            var bb = new BlackboardIntBuffer();
            for (int i = 0; i < GasConstants.MAX_BLACKBOARD_ENTRIES; i++)
            {
                bb.Set(i, i);
            }

            var ex = Assert.Throws<InvalidOperationException>(
                () => bb.Set(GasConstants.MAX_BLACKBOARD_ENTRIES, 1));
            Assert.That(ex!.Message, Does.StartWith(BlackboardIntBuffer.CapacityExceededError));
        }

        [Test]
        public void BlackboardEntity_Overflow_Throws()
        {
            var bb = new BlackboardEntityBuffer();
            for (int i = 0; i < BlackboardEntityBuffer.MAX_ENTRIES; i++)
            {
                bb.Set(i, default(Entity));
            }

            var ex = Assert.Throws<InvalidOperationException>(
                () => bb.Set(BlackboardEntityBuffer.MAX_ENTRIES, default(Entity)));
            Assert.That(ex!.Message, Does.StartWith(BlackboardEntityBuffer.CapacityExceededError));
        }
    }
}
