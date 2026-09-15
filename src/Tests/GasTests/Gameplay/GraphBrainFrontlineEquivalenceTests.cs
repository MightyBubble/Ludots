using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GraphBrains;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Equivalence suite for the frontline graph brains (issue #1536 切B): the mod's
    /// rts.frontline.attack / rts.frontline.transport Script graphs, driven by the generic
    /// GraphActionBrainHostSystem, reproduce the behavioral contracts the deleted Core
    /// ActionLoop systems carried (order consumption, pursuit routing, standoff ring slot,
    /// cooldown firing, dead-target parking, gather/load/haul/credit).
    /// </summary>
    [TestFixture, NonParallelizable]
    public class GraphBrainFrontlineEquivalenceTests
    {
        private const string AttackScript = "rts.frontline.attack";
        private const string TransportScript = "rts.frontline.transport";
        private const int MoveTo = 101;
        private const int AttackTarget = 102;
        private const int Gather = 172;
        private static int SinkTemplateKeyId;

        private sealed class OpenGate : IGameplayActionLoopGate
        {
            public static readonly OpenGate Instance = new();
            public bool CanAdvanceGameplay => true;
        }

        private static (GraphProgramRegistry Programs, GasGraphRuntimeApi Api) CompileFrontlineBrains(
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
                new Ludots.Core.Gameplay.Relationships.RelationshipReasonRegistry(),
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
            return (programs, api);
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

        [Test]
        public void AttackBrain_AcceptsOrder_RoutesToStandoffSlot_AndCompletesTheOrder()
        {
            using var world = World.Create();
            var effects = new EffectRequestQueue(16);
            var orderTypes = CreateOrderTypes();
            var orders = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));
            var (programs, api) = CompileFrontlineBrains(world, effects, orders, orderTypes);
            var host = new GraphActionBrainHostSystem(world, programs, api, OpenGate.Instance);

            Entity enemy = world.Create(
                Ludots.Core.Components.WorldPositionCm.FromCm(1000, 0),
                new Team { Id = 2 });
            Entity actor = world.Create(
                new GraphActionBrain { ScriptKey = AttackScript, ThinkEveryNTicks = 1 },
                ActiveOrder(7, AttackTarget, enemy),
                new PlayerOwner { PlayerId = 1 },
                new Team { Id = 1 },
                Ludots.Core.Components.WorldPositionCm.FromCm(0, 0),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer());

            host.Update(1f / 30f);

            // order consumed: buffer released + terminal result published
            That(world.Get<OrderBuffer>(actor).ActiveIndex, Is.EqualTo(-1));
            That(orderTypes.TerminalResults.Count, Is.EqualTo(1));

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
            var (programs, api) = CompileFrontlineBrains(world, effects, orders, orderTypes);
            var host = new GraphActionBrainHostSystem(world, programs, api, OpenGate.Instance);

            Entity enemy = world.Create(
                Ludots.Core.Components.WorldPositionCm.FromCm(1000, 0),
                new Team { Id = 2 },
                new AttributeBuffer());
            Entity actor = world.Create(
                new GraphActionBrain { ScriptKey = AttackScript, ThinkEveryNTicks = 1 },
                ActiveOrder(7, AttackTarget, enemy),
                new PlayerOwner { PlayerId = 1 },
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
        }

        [Test]
        public void TransportBrain_Gathers_Loads_Hauls_AndCreditsOnlyAtTheSink()
        {
            using var world = World.Create();
            var effects = new EffectRequestQueue(16);
            var orderTypes = CreateOrderTypes();
            var orders = new OrderQueue(64, new OrderAdmissionResultBuffer(64, 64));
            var (programs, api) = CompileFrontlineBrains(world, effects, orders, orderTypes);
            var host = new GraphActionBrainHostSystem(world, programs, api, OpenGate.Instance);

            var sinkAttributes = default(AttributeBuffer);
            Entity source = world.Create(
                Ludots.Core.Components.WorldPositionCm.FromCm(1000, 0));
            Entity sink = world.Create(
                new Ludots.Core.Components.MapEntity(),
                new Ludots.Core.Gameplay.Spawning.EntityTemplateKeyRef { TemplateKeyId = SinkTemplateKeyId },
                Ludots.Core.Components.WorldPositionCm.FromCm(600, -200),
                sinkAttributes,
                new PlayerOwner { PlayerId = 1 },
                new Ludots.Core.Gameplay.GAS.Components.DirtyFlags());
            Entity actor = world.Create(
                new GraphActionBrain { ScriptKey = TransportScript, ThinkEveryNTicks = 1 },
                ActiveOrder(9, Gather, source),
                new PlayerOwner { PlayerId = 1 },
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
            int phaseKey = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register("Brain.Transport.Phase");
            int ticksKey = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register("Brain.Transport.Ticks");
            That(bb.TryGet(phaseKey, out int phase) && phase == 2, Is.True, "arrival at source starts loading");
            That(bb.TryGet(ticksKey, out int ticks) && ticks == 60, Is.True);

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

            // teleport to the dock → credit 20 crystals, phase resets
            world.Set(actor, Ludots.Core.Components.WorldPositionCm.FromCm(600, -200));
            host.Update(1f / 30f);
            That(world.Get<AttributeBuffer>(sink).GetCurrent(
                Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId("Crystals")), Is.EqualTo(20f));
            That(world.Get<BlackboardIntBuffer>(actor).TryGet(phaseKey, out phase) && phase == 0, Is.True);
        }
    }
}
