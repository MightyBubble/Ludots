using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Commands;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace GasTests
{
    [TestFixture]
    public class Phase2InfrastructureTests
    {
        // ────────────────────────────────────────────────────────
        // FireMapEvent isolation — only the specified map's triggers fire
        // ────────────────────────────────────────────────────────

        [Test]
        public void FireMapEvent_OnlyTriggersSpecifiedMap()
        {
            var tm = new TriggerManager();

            int mapAFired = 0;
            int mapBFired = 0;

            var triggerA = new Trigger { EventKey = GameEvents.MapLoaded, Priority = 0 };
            triggerA.AddAction(new DelegateCommand(_ => { mapAFired++; return Task.CompletedTask; }));

            var triggerB = new Trigger { EventKey = GameEvents.MapLoaded, Priority = 0 };
            triggerB.AddAction(new DelegateCommand(_ => { mapBFired++; return Task.CompletedTask; }));

            var mapIdA = new MapId("strategic");
            var mapIdB = new MapId("battle_42");

            tm.RegisterMapTriggers(mapIdA, new List<Trigger> { triggerA });
            tm.RegisterMapTriggers(mapIdB, new List<Trigger> { triggerB });

            // Fire only for mapA
            var ctx = new ScriptContext();
            tm.FireMapEvent(mapIdA, GameEvents.MapLoaded, ctx);

            Assert.That(mapAFired, Is.EqualTo(1), "MapA trigger should fire");
            Assert.That(mapBFired, Is.EqualTo(0), "MapB trigger should NOT fire");
        }

        [Test]
        public void FireMapEvent_NonMatchingEventKey_DoesNotFire()
        {
            var tm = new TriggerManager();
            int fired = 0;

            var trigger = new Trigger { EventKey = GameEvents.MapLoaded, Priority = 0 };
            trigger.AddAction(new DelegateCommand(_ => { fired++; return Task.CompletedTask; }));

            var mapId = new MapId("test_map");
            tm.RegisterMapTriggers(mapId, new List<Trigger> { trigger });

            // Fire a different event key
            tm.FireMapEvent(mapId, GameEvents.MapUnloaded, new ScriptContext());
            Assert.That(fired, Is.EqualTo(0));
        }

        [Test]
        public void FireMapEvent_ConcurrentMaps_Isolated()
        {
            var tm = new TriggerManager();
            var results = new List<string>();

            var triggerA = new Trigger { EventKey = GameEvents.MapLoaded, Priority = 0 };
            triggerA.AddAction(new DelegateCommand(_ => { results.Add("A"); return Task.CompletedTask; }));

            var triggerB = new Trigger { EventKey = GameEvents.MapLoaded, Priority = 0 };
            triggerB.AddAction(new DelegateCommand(_ => { results.Add("B"); return Task.CompletedTask; }));

            var mapA = new MapId("map_a");
            var mapB = new MapId("map_b");

            tm.RegisterMapTriggers(mapA, new List<Trigger> { triggerA });
            tm.RegisterMapTriggers(mapB, new List<Trigger> { triggerB });

            // Fire for both maps sequentially
            tm.FireMapEvent(mapA, GameEvents.MapLoaded, new ScriptContext());
            tm.FireMapEvent(mapB, GameEvents.MapLoaded, new ScriptContext());

            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results[0], Is.EqualTo("A"));
            Assert.That(results[1], Is.EqualTo("B"));
        }

        [Test]
        public void FireMapEvent_GlobalAndMapScoped_BothFireWithoutDuplication()
        {
            var tm = new TriggerManager();
            int globalCount = 0;
            int mapCount = 0;

            var globalTrigger = new Trigger { EventKey = GameEvents.MapLoaded, Priority = 20 };
            globalTrigger.AddAction(new DelegateCommand(_ => { globalCount++; return Task.CompletedTask; }));
            tm.RegisterTrigger(globalTrigger);

            var mapTrigger = new Trigger { EventKey = GameEvents.MapLoaded, Priority = 10 };
            mapTrigger.AddAction(new DelegateCommand(_ => { mapCount++; return Task.CompletedTask; }));

            var mapId = new MapId("map_scoped");
            tm.RegisterMapTriggers(mapId, new List<Trigger> { mapTrigger });

            tm.FireMapEvent(mapId, GameEvents.MapLoaded, new ScriptContext());

            Assert.That(mapCount, Is.EqualTo(1), "Map-scoped trigger should fire exactly once");
            Assert.That(globalCount, Is.EqualTo(1), "Global trigger should still fire for compatibility");
        }

        [Test]
        public void FireMapEvent_OtherMap_FiresGlobalButNotForeignMapScoped()
        {
            var tm = new TriggerManager();
            int globalCount = 0;
            int mapACount = 0;

            var globalTrigger = new Trigger { EventKey = GameEvents.MapLoaded, Priority = 0 };
            globalTrigger.AddAction(new DelegateCommand(_ => { globalCount++; return Task.CompletedTask; }));
            tm.RegisterTrigger(globalTrigger);

            var mapATrigger = new Trigger { EventKey = GameEvents.MapLoaded, Priority = 0 };
            mapATrigger.AddAction(new DelegateCommand(_ => { mapACount++; return Task.CompletedTask; }));
            tm.RegisterMapTriggers(new MapId("map_a"), new List<Trigger> { mapATrigger });

            // Fire for map_b: global should fire, map_a scoped should not.
            tm.FireMapEvent(new MapId("map_b"), GameEvents.MapLoaded, new ScriptContext());

            Assert.That(globalCount, Is.EqualTo(1));
            Assert.That(mapACount, Is.EqualTo(0));
        }

        // ────────────────────────────────────────────────────────
        // Priority sorting — lower Priority executes first
        // ────────────────────────────────────────────────────────

        [Test]
        public void FireMapEvent_SortsByPriority_LowerFirst()
        {
            var tm = new TriggerManager();
            var order = new List<int>();

            var t100 = new Trigger { EventKey = GameEvents.MapLoaded, Priority = 100 };
            t100.AddAction(new DelegateCommand(_ => { order.Add(100); return Task.CompletedTask; }));

            var t0 = new Trigger { EventKey = GameEvents.MapLoaded, Priority = 0 };
            t0.AddAction(new DelegateCommand(_ => { order.Add(0); return Task.CompletedTask; }));

            var t50 = new Trigger { EventKey = GameEvents.MapLoaded, Priority = 50 };
            t50.AddAction(new DelegateCommand(_ => { order.Add(50); return Task.CompletedTask; }));

            var mapId = new MapId("sorted_map");
            // Register in reverse priority order to verify sorting
            tm.RegisterMapTriggers(mapId, new List<Trigger> { t100, t0, t50 });

            tm.FireMapEvent(mapId, GameEvents.MapLoaded, new ScriptContext());

            Assert.That(order, Has.Count.EqualTo(3));
            Assert.That(order[0], Is.EqualTo(0));
            Assert.That(order[1], Is.EqualTo(50));
            Assert.That(order[2], Is.EqualTo(100));
        }

        [Test]
        public void FireEvent_GlobalAlsoSortsByPriority()
        {
            var tm = new TriggerManager();
            var order = new List<int>();

            var tHigh = new Trigger { EventKey = GameEvents.GameStart, Priority = 99 };
            tHigh.AddAction(new DelegateCommand(_ => { order.Add(99); return Task.CompletedTask; }));

            var tLow = new Trigger { EventKey = GameEvents.GameStart, Priority = 1 };
            tLow.AddAction(new DelegateCommand(_ => { order.Add(1); return Task.CompletedTask; }));

            tm.RegisterTrigger(tHigh);
            tm.RegisterTrigger(tLow);

            tm.FireEvent(GameEvents.GameStart, new ScriptContext());

            Assert.That(order, Has.Count.EqualTo(2));
            Assert.That(order[0], Is.EqualTo(1));
            Assert.That(order[1], Is.EqualTo(99));
        }

        // ────────────────────────────────────────────────────────
        // EventHandler — simple mod callbacks
        // ────────────────────────────────────────────────────────

        [Test]
        public void RegisterEventHandler_FiresOnMatchingEvent()
        {
            var tm = new TriggerManager();
            int called = 0;

            tm.RegisterEventHandler(GameEvents.GameStart, ctx =>
            {
                called++;
                return Task.CompletedTask;
            });

            tm.FireEvent(GameEvents.GameStart, new ScriptContext());
            Assert.That(called, Is.EqualTo(1));
        }

        [Test]
        public void RegisterEventHandler_FiresOnMapEvent()
        {
            var tm = new TriggerManager();
            int called = 0;

            tm.RegisterEventHandler(GameEvents.MapLoaded, ctx =>
            {
                called++;
                return Task.CompletedTask;
            });

            // EventHandlers fire for all maps (they're not map-scoped)
            var mapId = new MapId("any_map");
            tm.FireMapEvent(mapId, GameEvents.MapLoaded, new ScriptContext());
            Assert.That(called, Is.EqualTo(1));
        }

        // ────────────────────────────────────────────────────────
        // SystemFactoryRegistry
        // ────────────────────────────────────────────────────────

        [Test]
        public void SystemFactoryRegistry_Register_TryActivate_Succeeds()
        {
            var registry = new SystemFactoryRegistry();
            bool factoryCalled = false;

            registry.Register("TestSystem", SystemGroup.Cleanup, ctx =>
            {
                factoryCalled = true;
                return new StubSystem();
            });

            var engine = new GameEngine();
            engine.InitializeMinimal();

            var result = registry.TryActivate("TestSystem", new ScriptContext(), engine);

            Assert.That(result, Is.True);
            Assert.That(factoryCalled, Is.True);
            Assert.That(registry.IsActivated("TestSystem"), Is.True);
        }

        [Test]
        public void SystemFactoryRegistry_TryActivate_Idempotent()
        {
            var registry = new SystemFactoryRegistry();
            int callCount = 0;

            registry.Register("TestSystem", SystemGroup.Cleanup, ctx =>
            {
                callCount++;
                return new StubSystem();
            });

            var engine = new GameEngine();
            engine.InitializeMinimal();

            registry.TryActivate("TestSystem", new ScriptContext(), engine);
            var second = registry.TryActivate("TestSystem", new ScriptContext(), engine);

            Assert.That(second, Is.False, "Second activation should return false");
            Assert.That(callCount, Is.EqualTo(1), "Factory should only be called once");
        }

        [Test]
        public void SystemFactoryRegistry_RegisterPresentation_Activates()
        {
            var registry = new SystemFactoryRegistry();
            bool activated = false;

            registry.RegisterPresentation("TestPres", ctx =>
            {
                activated = true;
                return new StubSystem();
            });

            var engine = new GameEngine();
            engine.InitializeMinimal();

            var result = registry.TryActivate("TestPres", new ScriptContext(), engine);
            Assert.That(result, Is.True);
            Assert.That(activated, Is.True);
        }

        [Test]
        public void SystemFactoryRegistry_UnregisteredName_ReturnsFalse()
        {
            var registry = new SystemFactoryRegistry();
            var engine = new GameEngine();
            engine.InitializeMinimal();

            var result = registry.TryActivate("DoesNotExist", new ScriptContext(), engine);
            Assert.That(result, Is.False);
        }

        // ────────────────────────────────────────────────────────
        // TriggerDecoratorRegistry
        // ────────────────────────────────────────────────────────

        [Test]
        public void TriggerDecoratorRegistry_RegisterByType_AppliesMatching()
        {
            var registry = new TriggerDecoratorRegistry();
            bool decorated = false;

            registry.Register<TestTriggerA>(t => { decorated = true; });

            var trigger = new TestTriggerA();
            registry.Apply(trigger);

            Assert.That(decorated, Is.True);
        }

        [Test]
        public void TriggerDecoratorRegistry_RegisterByType_SkipsNonMatching()
        {
            var registry = new TriggerDecoratorRegistry();
            bool decorated = false;

            registry.Register<TestTriggerA>(t => { decorated = true; });

            var trigger = new TestTriggerB();
            registry.Apply(trigger);

            Assert.That(decorated, Is.False);
        }

        [Test]
        public void TriggerDecoratorRegistry_RegisterByName_AppliesMatching()
        {
            var registry = new TriggerDecoratorRegistry();
            bool decorated = false;

            registry.Register("TestTriggerA", t => { decorated = true; });

            var trigger = new TestTriggerA();
            registry.Apply(trigger);

            Assert.That(decorated, Is.True);
        }

        [Test]
        public void TriggerDecoratorRegistry_RegisterAnchor_InjectsCommand()
        {
            var registry = new TriggerDecoratorRegistry();
            int injectedValue = 0;

            registry.RegisterAnchor("map_ready",
                new DelegateCommand(_ => { injectedValue = 42; return Task.CompletedTask; }));

            var trigger = new Trigger { EventKey = GameEvents.MapLoaded };
            trigger.AddAction(new AnchorCommand("map_ready"));

            registry.Apply(trigger);

            Assert.That(trigger.Actions, Has.Count.EqualTo(2));
            Assert.That(trigger.Actions[0], Is.InstanceOf<AnchorCommand>());
            Assert.That(trigger.Actions[1], Is.InstanceOf<DelegateCommand>());
        }

        [Test]
        public void TriggerDecoratorRegistry_MultipleAnchorCommands_AllInjected()
        {
            var registry = new TriggerDecoratorRegistry();

            registry.RegisterAnchor("early", new DelegateCommand(_ => Task.CompletedTask));
            registry.RegisterAnchor("late", new DelegateCommand(_ => Task.CompletedTask));

            var trigger = new Trigger { EventKey = GameEvents.MapLoaded };
            trigger.AddAction(new AnchorCommand("early"));
            trigger.AddAction(new AnchorCommand("late"));

            registry.Apply(trigger);

            // Each anchor gets one command inserted after it
            Assert.That(trigger.Actions, Has.Count.EqualTo(4));
        }

        // ────────────────────────────────────────────────────────
        // MapSessionManager — additive model
        // ────────────────────────────────────────────────────────

        [Test]
        public void MapSessionManager_UnloadSession_RemovesFromFocusStack()
        {
            var mgr = new MapSessionManager();
            var config = new Ludots.Core.Config.MapConfig { Id = "a" };
            var configB = new Ludots.Core.Config.MapConfig { Id = "b" };

            mgr.CreateSession(new MapId("a"), config);
            mgr.PushFocused(new MapId("a"));

            mgr.CreateSession(new MapId("b"), configB);
            mgr.PushFocused(new MapId("b"));

            // B is on top
            Assert.That(mgr.FocusedSession.MapId, Is.EqualTo(new MapId("b")));

            // Unload B
            using var world = Arch.Core.World.Create();
            mgr.UnloadSession(new MapId("b"), world);

            // A should be restored
            Assert.That(mgr.FocusedSession.MapId, Is.EqualTo(new MapId("a")));
            Assert.That(mgr.FocusedSession.State, Is.EqualTo(MapSessionState.Active));
        }

        [Test]
        public void MapSessionManager_ConcurrentSessions_BothExist()
        {
            var mgr = new MapSessionManager();
            mgr.CreateSession(new MapId("strategic"), new Ludots.Core.Config.MapConfig { Id = "strategic" });
            mgr.PushFocused(new MapId("strategic"));

            mgr.CreateSession(new MapId("battle"), new Ludots.Core.Config.MapConfig { Id = "battle" });
            mgr.PushFocused(new MapId("battle"));

            // Both sessions exist
            Assert.That(mgr.All, Has.Count.EqualTo(2));
            Assert.That(mgr.GetSession(new MapId("strategic")), Is.Not.Null);
            Assert.That(mgr.GetSession(new MapId("battle")), Is.Not.Null);

            // Strategic is suspended, battle is active
            Assert.That(mgr.GetSession(new MapId("strategic")).State, Is.EqualTo(MapSessionState.Suspended));
            Assert.That(mgr.GetSession(new MapId("battle")).State, Is.EqualTo(MapSessionState.Active));
        }

        // ────────────────────────────────────────────────────────
        // Critical #1 regression: Cleanup only destroys its own MapId
        // ────────────────────────────────────────────────────────

        [Test]
        public void Cleanup_OnlyDestroysEntitiesForOwnMapId()
        {
            using var world = World.Create();

            var mapA = new MapId("strategic");
            var mapB = new MapId("battle_42");

            // Spawn 3 entities for mapA, 2 for mapB
            for (int i = 0; i < 3; i++)
                world.Create(new MapEntity { MapId = mapA });
            for (int i = 0; i < 2; i++)
                world.Create(new MapEntity { MapId = mapB });

            // Verify 5 total
            int total = 0;
            world.Query(new QueryDescription().WithAll<MapEntity>(), (Entity _) => total++);
            Assert.That(total, Is.EqualTo(5));

            // Cleanup only mapB
            var sessionB = new MapSession(mapB, new MapConfig { Id = "battle_42" });
            sessionB.Cleanup(world);

            // mapA entities should survive, mapB entities destroyed
            int remaining = 0;
            var survivorMapIds = new List<MapId>();
            world.Query(new QueryDescription().WithAll<MapEntity>(), (Entity e, ref MapEntity me) =>
            {
                remaining++;
                survivorMapIds.Add(me.MapId);
            });

            Assert.That(remaining, Is.EqualTo(3), "Only mapA's 3 entities should survive");
            for (int i = 0; i < survivorMapIds.Count; i++)
                Assert.That(survivorMapIds[i], Is.EqualTo(mapA));
        }

        [Test]
        public void Cleanup_EmptyMap_DoesNotAffectOtherMaps()
        {
            using var world = World.Create();

            var mapA = new MapId("has_entities");
            var mapEmpty = new MapId("empty_map");

            for (int i = 0; i < 4; i++)
                world.Create(new MapEntity { MapId = mapA });

            // Cleanup the empty map
            var sessionEmpty = new MapSession(mapEmpty, new MapConfig { Id = "empty_map" });
            sessionEmpty.Cleanup(world);

            // All mapA entities must survive
            int count = 0;
            world.Query(new QueryDescription().WithAll<MapEntity>(), (Entity _) => count++);
            Assert.That(count, Is.EqualTo(4));
        }

        [Test]
        public void SetMapEntitiesSuspended_AddsTagOnlyForTargetMap()
        {
            var engine = new GameEngine();
            engine.InitializeMinimal();
            var world = (World)typeof(GameEngine).GetProperty("World", BindingFlags.Public | BindingFlags.Instance)!.GetValue(engine)!;
            var mapA = new MapId("map_a");
            var mapB = new MapId("map_b");

            for (int i = 0; i < 3; i++) world.Create(new MapEntity { MapId = mapA });
            for (int i = 0; i < 2; i++) world.Create(new MapEntity { MapId = mapB });

            var method = typeof(GameEngine).GetMethod("SetMapEntitiesSuspended", BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(engine, new object[] { mapA, true });

            int aSuspended = 0;
            int bSuspended = 0;
            world.Query(new QueryDescription().WithAll<MapEntity, SuspendedTag>(), (Entity _, ref MapEntity me) =>
            {
                if (me.MapId == mapA) aSuspended++;
                if (me.MapId == mapB) bSuspended++;
            });

            Assert.That(aSuspended, Is.EqualTo(3));
            Assert.That(bSuspended, Is.EqualTo(0));
            world.Dispose();
        }

        [Test]
        public void SetMapEntitiesSuspended_RemoveTagOnlyForTargetMap()
        {
            var engine = new GameEngine();
            engine.InitializeMinimal();
            var world = (World)typeof(GameEngine).GetProperty("World", BindingFlags.Public | BindingFlags.Instance)!.GetValue(engine)!;
            var mapA = new MapId("map_a");
            var mapB = new MapId("map_b");

            for (int i = 0; i < 2; i++) world.Create(new MapEntity { MapId = mapA }, new SuspendedTag());
            for (int i = 0; i < 2; i++) world.Create(new MapEntity { MapId = mapB }, new SuspendedTag());

            var method = typeof(GameEngine).GetMethod("SetMapEntitiesSuspended", BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(engine, new object[] { mapA, false });

            int aSuspended = 0;
            int bSuspended = 0;
            world.Query(new QueryDescription().WithAll<MapEntity, SuspendedTag>(), (Entity _, ref MapEntity me) =>
            {
                if (me.MapId == mapA) aSuspended++;
                if (me.MapId == mapB) bSuspended++;
            });

            Assert.That(aSuspended, Is.EqualTo(0));
            Assert.That(bSuspended, Is.EqualTo(2));
            world.Dispose();
        }

        // ────────────────────────────────────────────────────────
        // Helper types
        // ────────────────────────────────────────────────────────

        private class TestTriggerA : Trigger
        {
            public TestTriggerA() { EventKey = GameEvents.MapLoaded; }
        }

        private class TestTriggerB : Trigger
        {
            public TestTriggerB() { EventKey = GameEvents.MapLoaded; }
        }

        private class DelegateCommand : GameCommand
        {
            private readonly Func<ScriptContext, Task> _action;
            public DelegateCommand(Func<ScriptContext, Task> action) => _action = action;
            public override Task ExecuteAsync(ScriptContext context) => _action(context);
        }

        private class StubSystem : Arch.System.ISystem<float>
        {
            public void Initialize() { }
            public void BeforeUpdate(in float t) { }
            public void Update(in float t) { }
            public void AfterUpdate(in float t) { }
            public void Dispose() { }
        }
    }

    // Extension to allow minimal initialization for tests
    public static class GameEngineTestExtensions
    {
        public static void InitializeMinimal(this GameEngine engine)
        {
            // Minimal setup: just create ECS world so RegisterSystem works
            var world = Arch.Core.World.Create();
            typeof(GameEngine).GetProperty("World")?.SetValue(engine, world);
        }
    }
}
