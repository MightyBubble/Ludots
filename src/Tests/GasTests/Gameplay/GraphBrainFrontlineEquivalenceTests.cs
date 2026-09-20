using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.AI.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GraphBrains;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Equivalence suite for the frontline graph brains (issue #1536 migration): the mod's
    /// hfsm.rts.attack / hfsm.rts.transport HFSM definitions (states/transitions bound to
    /// ActionLib graphs), driven per entity by the generic HfsmWorld + GraphProgramHfsmHost
    /// adapter, reproduce the behavioral contracts the deleted Core ActionLoop systems carried
    /// (order consumption, pursuit routing, standoff ring slot, cooldown firing, dead-target
    /// parking, gather/load/haul/credit).
    /// </summary>
    [TestFixture, NonParallelizable]
    public class GraphBrainFrontlineEquivalenceTests
    {
        private const string AttackHfsm = "hfsm.rts.attack";
        private const string TransportHfsm = "hfsm.rts.transport";
        private const int MoveTo = 101;
        private const int AttackTarget = 102;
        private const int Gather = 172;
        private static int SinkTemplateKeyId;

        private sealed class OpenGate : IGameplayAdvanceGate
        {
            public static readonly OpenGate Instance = new();
            public bool CanAdvanceGameplay => true;
        }

        private static (GraphProgramRegistry Programs, GasGraphRuntimeApi Api, GraphBehaviorCatalog Behavior) CompileFrontlineBrains(
            World world, EffectRequestQueue effects, OrderQueue orders, OrderTypeRegistry orderTypes)
        {
            GraphIdRegistry.Clear();
            if (EffectTemplateIdRegistry.GetId("Effect.Rts.Frontline.InfantryDamage") <= 0)
            {
                EffectTemplateIdRegistry.Clear();
                EffectTemplateIdRegistry.Register("Effect.Rts.Frontline.InfantryDamage");
            }

            if (AttributeRegistry.GetId("Crystals") == AttributeRegistry.InvalidId)
            {
                AttributeRegistry.Clear();
                AttributeRegistry.Register("Crystals");
            }
            var templateKeys = new Ludots.Core.Gameplay.Spawning.EntityTemplateKeyRegistry();
            _ = templateKeys.Register("rts_frontline_core");
            var types = new Ludots.Core.Gameplay.Relationships.RelationshipTypeRegistry();
            types.Register("Owns");
            types.Register("Controls");
            var resolver = new GasGraphSymbolResolver(
                types,
                new Ludots.Core.Gameplay.Relationships.RelationshipMetricRegistry(),
                new Ludots.Core.Gameplay.Relationships.RelationshipFlagRegistry(),
                new Ludots.Core.Gameplay.GAS.TargetDispatchPresetRegistry(),
                entityTemplateKeys: templateKeys,
                orderTypes: orderTypes);

            string path = Path.Combine(
                FindRepoRoot(),
                "mods/showcases/rts_multiplayer_frontline/RtsMultiplayerFrontlineMod/assets/GAS/graphs.json");
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var programs = new GraphProgramRegistry();
            foreach (JsonNode node in JsonNode.Parse(File.ReadAllText(path))!.AsArray())
            {
                var graph = (JsonObject)node!;
                string id = graph["id"]!.GetValue<string>();
                GraphControlFlowCompileResult compiled = GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(
                    graph, id, options, new Ludots.Core.Scripting.EventSchemaRegistry());
                if (!compiled.Succeeded)
                {
                    string messages = string.Join("; ", compiled.Diagnostics.Select(d => $"{d.Code}:{d.Message}"));
                    throw new InvalidOperationException($"Frontline brain '{id}' failed to compile: {messages}");
                }

                GraphProgramPackage package = compiled.Package!.Value;
                int graphId = GraphIdRegistry.Register(id);
                programs.Register(graphId, package.Program, GraphKind.Script, GraphInstructionSourceMap.Empty, package.Symbols);
                GraphProgramSymbolPatcher.Patch(package.Symbols, package.Program, resolver);
            }

            GraphBehaviorCatalog behavior = LoadModHfsmAndActionLib(programs);

            var tagOps = new Ludots.Core.Gameplay.GAS.TagOps(
                new Ludots.Core.Gameplay.GAS.DirtyEntityQueue(8), new Ludots.Core.Gameplay.GAS.TagRuleRegistry());
            var relationships = new Ludots.Core.Gameplay.Relationships.RelationshipRuntime(
                world,
                types,
                new Ludots.Core.Gameplay.Relationships.RelationshipMetricRegistry(),
                new Ludots.Core.Gameplay.Relationships.RelationshipFlagRegistry(),
                new Ludots.Core.Gameplay.Relationships.RelationshipBandRegistry(),
                new Ludots.Core.Gameplay.Relationships.RelationshipChangeBuffer(),
                new Ludots.Core.Gameplay.Relationships.RelationshipReverseIndex(world));
            var entityQueries = new Ludots.Core.EntityQueries.EntitySetQueryRuntime(world, tagOps, relationships);
            var api = new GasGraphRuntimeApi(
                world,
                effectRequests: effects,
                tagOps: tagOps,
                relationshipRuntime: relationships,
                entityQueries: entityQueries);
            api.BindOrderPipeline(orders, orderTypes);
            SinkTemplateKeyId = templateKeys.GetId("rts_frontline_core");
            return (programs, api, behavior);
        }

        // Loads the mod's AI/hfsm.json and GAS/action_lib.json through the production catalog
        // loaders (the same path the game engine uses), so the equivalence suite exercises the
        // real HFSM/action bindings rather than a hand-rolled registry stub.
        private static GraphBehaviorCatalog LoadModHfsmAndActionLib(GraphProgramRegistry programs)
        {
            string modRoot = Path.Combine(
                FindRepoRoot(),
                "mods/showcases/rts_multiplayer_frontline/RtsMultiplayerFrontlineMod");
            var vfs = new VirtualFileSystem();
            vfs.Mount("RtsMultiplayerFrontlineMod", modRoot);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            modLoader.LoadedModIds.Add("RtsMultiplayerFrontlineMod");
            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("GAS/action_lib.json", ConfigMergePolicy.ArrayById, "name"));
            catalog.Add(new ConfigCatalogEntry("AI/hfsm.json", ConfigMergePolicy.ArrayById, "id"));

            var functions = new GraphFunctionCatalog();
            catalog.Add(new ConfigCatalogEntry("GAS/func_lib.json", ConfigMergePolicy.ArrayById, "name"));
            new GraphFunctionCatalogLoader(pipeline, functions, programs).Load(catalog);
            var actions = new GraphActionCatalog();
            new GraphActionCatalogLoader(pipeline, actions, programs, functions).Load(catalog);
            return new GraphBehaviorDefinitionLoader(pipeline, actions, functions).Load(catalog);
        }

        private static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "showcase.registry.json")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            return dir ?? throw new InvalidOperationException("Repo root not found.");
        }

        private static OrderTypeRegistry CreateOrderTypes()
        {
            var registry = new OrderTypeRegistry(new OrderTerminalResultBuffer(OrderTerminalResultBuffer.DefaultCapacity));
            foreach ((string key, int id) in new[] { ("moveTo", MoveTo), ("attackTarget", AttackTarget), ("frontlineGather", Gather) })
            {
                registry.Register(new OrderTypeConfig
                {
                    Key = key,
                    OrderTypeId = id,
                    Label = key,
                    MaxQueueSize = 1,
                    SameTypePolicy = SameTypePolicy.Replace,
                    QueueFullPolicy = QueueFullPolicy.RejectNew,
                    Priority = 100,
                    BufferWindowMs = 0,
                    PendingBufferWindowMs = 0,
                    CanInterruptSelf = true,
                    QueuedModeMaxSize = 1,
                    AllowQueuedMode = false,
                    ClearQueueOnActivate = true,
                });
            }

            return registry;
        }

        private static OrderBuffer ActiveOrder(int orderId, int orderTypeId, Entity target) => new()
        {
            ActiveIndex = 0,
            ActiveOrder = new QueuedOrder
            {
                Order = new Order { OrderId = orderId, OrderTypeId = orderTypeId, PlayerId = 1, Target = target },
            },
        };

        // Mirrors the template birth state (Entities/templates.json) so behavior graphs can
        // read their Brain.* keys from the first tick.
        private static GraphActionBrain AttackBrain() => new()
        {
            HfsmId = AttackHfsm,
            ThinkEveryNTicks = 1,
            BlackboardIntDefaults = new[]
            {
                ("Brain.Attack.Cooldown", 0), ("Brain.Attack.DirX", 0), ("Brain.Attack.DirY", 0),
            },
            BlackboardEntityDefaults = new[] { "Brain.Attack.Target" },
        };

        private static GraphActionBrain TransportBrain() => new()
        {
            HfsmId = TransportHfsm,
            ThinkEveryNTicks = 1,
            BlackboardIntDefaults = new[] { ("Brain.Transport.Ticks", 0) },
            BlackboardEntityDefaults = new[] { "Brain.Transport.Source", "Brain.Transport.Sink" },
        };

        [Test]
        public void AttackBrain_AcceptsOrder_RoutesToStandoffSlot_AndCompletesTheOrder()
        {
            using var world = World.Create();
            var effects = new EffectRequestQueue(16);
            var orderTypes = CreateOrderTypes();
            var orders = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));
            var (programs, api, behavior) = CompileFrontlineBrains(world, effects, orders, orderTypes);
            var host = new HfsmBrainHostSystem(world, programs, api, OpenGate.Instance, behavior);

            Entity enemy = world.Create(
                Ludots.Core.Components.WorldPositionCm.FromCm(1000, 0),
                new Team { Id = 2 });
            Entity actor = world.Create(
                AttackBrain(),
                ActiveOrder(7, AttackTarget, enemy),
                new PlayerOwner { PlayerId = 1 },
                new HfsmState(),
                new Team { Id = 1 },
                Ludots.Core.Components.WorldPositionCm.FromCm(0, 0),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer());

            host.Update(1f / 30f);

            // order consumed: buffer released + terminal result published
            That(world.Get<OrderBuffer>(actor).ActiveIndex, Is.EqualTo(-1));
            That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            That(host.TryGetLeafStateName(actor, out string routeState) && routeState == "pursuit", Is.True,
                "accepting an attack order enters the pursuit state");

            // pursuit move routed to the standoff ring slot: 1000 - 520 = 480cm on X
            That(orders.TryDequeue(out Order move), Is.True);
            That(move.OrderTypeId, Is.EqualTo(MoveTo));
            That(move.Target, Is.EqualTo(enemy));
            That((int)move.Args.Spatial.WorldCm.X, Is.EqualTo(480));
            That((int)move.Args.Spatial.WorldCm.Z, Is.EqualTo(0));

            // remembered target on the brain blackboard
        }

        [Test]
        public void AttackBrain_EngagesWhenInRange_FiresOnCooldown_AndParksOnDeadTarget()
        {
            using var world = World.Create();
            var effects = new EffectRequestQueue(16);
            var orderTypes = CreateOrderTypes();
            var orders = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));
            var (programs, api, behavior) = CompileFrontlineBrains(world, effects, orders, orderTypes);
            var host = new HfsmBrainHostSystem(world, programs, api, OpenGate.Instance, behavior);

            Entity enemy = world.Create(
                Ludots.Core.Components.WorldPositionCm.FromCm(1000, 0),
                new Team { Id = 2 },
                new AttributeBuffer());
            Entity actor = world.Create(
                AttackBrain(),
                ActiveOrder(7, AttackTarget, enemy),
                new PlayerOwner { PlayerId = 1 },
                new HfsmState(),
                new Team { Id = 1 },
                Ludots.Core.Components.WorldPositionCm.FromCm(600, 0),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer());

            host.Update(1f / 30f); // accept + standoff routing first (matches legacy standoff behavior)
            That(effects.Count, Is.EqualTo(0), "standoff routes before firing");
            host.Update(1f / 30f); // buffer empty + within range → engages and fires
            That(effects.Count, Is.EqualTo(1), "first engage tick fires the damage effect");
            That(world.Get<BlackboardIntBuffer>(actor).TryGet(
                Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register("Brain.Attack.Cooldown"), out int cd), Is.True);
            That(cd, Is.EqualTo(30), "cooldown resets to 30 after firing");

            for (int i = 0; i < 31; i++)
            {
                host.Update(1f / 30f);
            }

            That(effects.Count, Is.EqualTo(2), "after the cooldown window the effect fires again");

            world.Destroy(enemy);
            for (int i = 0; i < 5; i++)
            {
                host.Update(1f / 30f);
            }

            That(effects.Count, Is.EqualTo(2), "dead target parks the brain without firing");
            That(host.TryGetLeafStateName(actor, out string parkedState) && parkedState == "wait", Is.True,
                "a dead target parks the brain back in wait");
        }

        [Test]
        public void AttackBrain_ParksOnPlayerMoveOrder_AndResumesTheRememberedTarget()
        {
            using var world = World.Create();
            var effects = new EffectRequestQueue(16);
            var orderTypes = CreateOrderTypes();
            var orders = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));
            var (programs, api, behavior) = CompileFrontlineBrains(world, effects, orders, orderTypes);
            var host = new HfsmBrainHostSystem(world, programs, api, OpenGate.Instance, behavior);

            Entity enemy = world.Create(
                Ludots.Core.Components.WorldPositionCm.FromCm(1000, 0),
                new Team { Id = 2 },
                new AttributeBuffer());
            Entity actor = world.Create(
                AttackBrain(),
                ActiveOrder(7, AttackTarget, enemy),
                new PlayerOwner { PlayerId = 1 },
                new HfsmState(),
                new Team { Id = 1 },
                Ludots.Core.Components.WorldPositionCm.FromCm(0, 0),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer());

            host.Update(1f / 30f); // accept + standoff routing
            host.Update(1f / 30f); // out of range → persistent tracking
            That(host.TryGetLeafStateName(actor, out string trackingState) && trackingState == "track", Is.True,
                "out-of-range pursuit settles into the tracking state");

            world.Set(actor, ActiveOrder(8, MoveTo, Entity.Null));
            host.Update(1f / 30f);
            That(host.TryGetLeafStateName(actor, out string moveParksState) && moveParksState == "wait", Is.True,
                "a player move order parks the attack brain instead of fighting the move");

            world.Set(actor, new OrderBuffer { ActiveIndex = -1 });
            host.Update(1f / 30f);
            That(host.TryGetLeafStateName(actor, out string resumedState) && resumedState == "track", Is.True,
                "once the move order clears, the remembered target is resumed");
        }

        [Test]
        public void TransportBrain_Gathers_Loads_Hauls_AndCreditsOnlyAtTheSink()
        {
            using var world = World.Create();
            var effects = new EffectRequestQueue(16);
            var orderTypes = CreateOrderTypes();
            var orders = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));
            var (programs, api, behavior) = CompileFrontlineBrains(world, effects, orders, orderTypes);
            var host = new HfsmBrainHostSystem(world, programs, api, OpenGate.Instance, behavior);

            var sinkAttributes = default(AttributeBuffer);
            Entity source = world.Create(
                Ludots.Core.Components.WorldPositionCm.FromCm(1000, 0));
            Entity sink = world.Create(
                new Ludots.Core.Components.MapEntity(),
                new Team { Id = 1 },
                new Ludots.Core.Gameplay.Spawning.EntityTemplateKeyRef { TemplateKeyId = SinkTemplateKeyId },
                Ludots.Core.Components.WorldPositionCm.FromCm(600, -200),
                sinkAttributes,
                new PlayerOwner { PlayerId = 1 },
                new HfsmState(),
                new Ludots.Core.Gameplay.GAS.Components.DirtyFlags());
            Entity actor = world.Create(
                TransportBrain(),
                ActiveOrder(9, Gather, source),
                new PlayerOwner { PlayerId = 1 },
                new HfsmState(),
                Ludots.Core.Components.WorldPositionCm.FromCm(0, 0),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer());

            host.Update(1f / 30f); // accept: completes gather, routes to source
            That(world.Get<OrderBuffer>(actor).ActiveIndex, Is.EqualTo(-1));
            That(orders.TryDequeue(out Order toSource), Is.True);
            That((int)toSource.Args.Spatial.WorldCm.X, Is.EqualTo(1000));

            // teleport to the source; buffer empty → loading starts
            world.Set(actor, Ludots.Core.Components.WorldPositionCm.FromCm(1000, 0));
            host.Update(1f / 30f);
            var bb = world.Get<BlackboardIntBuffer>(actor);
            int ticksKey = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register("Brain.Transport.Ticks");
            That(host.TryGetLeafStateName(actor, out string arrivedState) && arrivedState == "loading", Is.True,
                "arrival at source enters the loading state");
            // loading.enter seeds 60 ticks; the HFSM entry tick also runs the leaf onTick, which
            // consumes the first tick, so the counter reads 59 while loading still spans 60 ticks.
            That(bb.TryGet(ticksKey, out int ticks) && ticks == 59, Is.True);

            for (int i = 0; i < 61; i++)
            {
                host.Update(1f / 30f);
            }

            // loading done → sink discovered and routed
            That(orders.TryDequeue(out Order toSink), Is.True);
            That(toSink.Target, Is.EqualTo(sink));
            That((int)toSink.Args.Spatial.WorldCm.X, Is.EqualTo(600));
            That((int)toSink.Args.Spatial.WorldCm.Z, Is.EqualTo(-200));
            That(world.Get<AttributeBuffer>(sink).GetCurrent(
                Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId("Crystals")), Is.EqualTo(0f),
                "no credit before docking");

            // teleport to the dock → credit 20 crystals, then the state machine parks back in idle
            world.Set(actor, Ludots.Core.Components.WorldPositionCm.FromCm(600, -200));
            host.Update(1f / 30f);
            That(world.Get<AttributeBuffer>(sink).GetCurrent(
                Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId("Crystals")), Is.EqualTo(20f));
            host.Update(1f / 30f);
            That(host.TryGetLeafStateName(actor, out string parkedState) && parkedState == "idle", Is.True,
                "a completed haul returns the harvester to idle");
        }
    }
}
