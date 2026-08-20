using System;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Map;
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
        public void FireEventKey_NoMapScope_FallsBackToGlobalEvent()
        {
            using World world = World.Create();
            Entity scope = world.Create();

            var manager = new TriggerManager();
            var api = new GasGraphRuntimeApi(world);
            api.BindTriggerManager(manager);

            int keyId = ConfigKeyRegistry.Register("Custom.GlobalEvent");
            var trigger = new RecordingTrigger { EventKey = new EventKey("Custom.GlobalEvent") };
            manager.RegisterTrigger(trigger);

            api.FireEventKey(scope, keyId);

            Assert.That(trigger.Executed, Is.True, "Global trigger must run when the scope has no map.");
            Assert.That(trigger.SeenMapValue, Is.Null, "Global fallback must not populate a map id in context.");
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
