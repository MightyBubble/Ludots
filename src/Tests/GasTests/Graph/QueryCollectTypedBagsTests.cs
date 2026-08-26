using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Graph
{
    [TestFixture]
    public sealed class QueryCollectTypedBagsTests
    {
        [SetUp]
        public void SetUp()
        {
            EffectTemplateIdRegistry.Clear();
            AbilityIdRegistry.Clear();
            TagRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            EffectTemplateIdRegistry.Clear();
            AbilityIdRegistry.Clear();
            TagRegistry.Clear();
        }

        [Test]
        public void QueryCollectEffectTemplates_ReturnsRegisteredIdsSorted()
        {
            int blessing = EffectTemplateIdRegistry.Register("祝福");
            int swift = EffectTemplateIdRegistry.Register("迅捷");
            using World world = World.Create();
            var api = new GasGraphRuntimeApi(world);

            Span<int> buffer = stackalloc int[8];
            int count = api.CollectEffectTemplateIds(buffer);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(buffer[0], Is.EqualTo(Math.Min(blessing, swift)));
            Assert.That(buffer[1], Is.EqualTo(Math.Max(blessing, swift)));
        }

        [Test]
        public void QueryCollectAbilitySlots_ListsResolvedSlotsInOrder()
        {
            using World world = World.Create();
            int fireball = AbilityIdRegistry.Register("火球");
            Entity hero = world.Create();
            var buffer = new AbilityStateBuffer();
            buffer.AddAbility(fireball);
            buffer.AddAbility(fireball);
            world.Add(hero, buffer);

            var api = new GasGraphRuntimeApi(world);
            Span<int> slots = stackalloc int[8];
            int count = api.CollectAbilitySlots(hero, slots);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(slots[0], Is.EqualTo(0));
            Assert.That(slots[1], Is.EqualTo(1));
        }

        [Test]
        public void QueryCollectPresentTags_ReadsTagCountContainer()
        {
            using World world = World.Create();
            int buff = TagRegistry.Register("状态.增益");
            Entity hero = world.Create();
            var counts = new TagCountContainer();
            Assert.That(counts.AddCount(buff, 1), Is.True);
            world.Add(hero, counts);

            var api = new GasGraphRuntimeApi(world);
            Span<int> tags = stackalloc int[8];
            int count = api.CollectPresentTags(hero, tags);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(tags[0], Is.EqualTo(buff));
        }

        [Test]
        public void QueryCollectAbilityHolders_FiltersCandidatesByAbility()
        {
            using World world = World.Create();
            int fireball = AbilityIdRegistry.Register("火球");
            Entity holder = world.Create();
            Entity other = world.Create();
            var holderSlots = new AbilityStateBuffer();
            holderSlots.AddAbility(fireball);
            world.Add(holder, holderSlots);
            world.Add(other, new AbilityStateBuffer());

            var api = new GasGraphRuntimeApi(world);
            Span<Entity> candidates = stackalloc Entity[2];
            candidates[0] = other;
            candidates[1] = holder;
            Span<Entity> outBuffer = stackalloc Entity[2];
            int count = api.CollectAbilityHolders(fireball, candidates, outBuffer);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(outBuffer[0], Is.EqualTo(holder));
        }

        [TestCase("QueryCollectEffectTemplates")]
        [TestCase("QueryCollectAbilitySlots")]
        [TestCase("QueryCollectInventoryItems")]
        [TestCase("QueryCollectItemDefinitions")]
        [TestCase("QueryCollectPresentTags")]
        [TestCase("QueryCollectActiveTasks")]
        [TestCase("QueryCollectProgressionNodes")]
        [TestCase("QueryCollectAbilityHolders")]
        public void TypedCollector_CompilesWithRequiredInputs(string op)
        {
            var document = new GraphControlFlowDocument
            {
                Id = $"Test.{op}",
                Kind = "Query",
            };
            var collect = new GraphControlFlowNode { Id = "collect", Op = op };
            if (op is "QueryCollectEffectTemplates" or "QueryCollectItemDefinitions")
            {
                document.Entry = collect.Id;
                document.Nodes.Add(collect);
            }
            else if (op == "QueryCollectAbilityHolders")
            {
                var all = new GraphControlFlowNode { Id = "all", Op = "QueryAllMapEntities" };
                collect.Ability = "Ability.GraphOps.Gallery";
                document.Entry = all.Id;
                document.Nodes.Add(all);
                document.Nodes.Add(collect);
                document.ControlEdges.Add(new(all.Id, GraphControlFlowPorts.Next, collect.Id));
                document.ValueEdges.Add(new(all.Id, GraphControlFlowPorts.Value, collect.Id, GraphControlFlowPorts.List));
            }
            else
            {
                var caster = new GraphControlFlowNode { Id = "caster", Op = "LoadCaster" };
                document.Entry = caster.Id;
                document.Nodes.Add(caster);
                document.Nodes.Add(collect);
                document.ControlEdges.Add(new(caster.Id, GraphControlFlowPorts.Next, collect.Id));
                document.ValueEdges.Add(new(caster.Id, GraphControlFlowPorts.Value, collect.Id, GraphControlFlowPorts.Source));
            }

            var (package, _, diagnostics) = GraphControlFlowCompiler.CompileWithOutputs(document);

            Assert.That(diagnostics, Is.Empty);
            Assert.That(package.HasValue, Is.True);
            Assert.That(
                Array.Exists(package!.Value.Program, instruction => instruction.Op == (ushort)Enum.Parse<GraphNodeOp>(op)),
                Is.True);
        }
    }
}
